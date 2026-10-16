namespace InactiveReset.Core;

/// <summary>Build-bound direct pose transform. No garage transition or native function calls.</summary>
public sealed class DirectPosePlan
{
    public sealed record BodyState(double[] Position, double[] Matrix, double[] Velocity,
        double[] Momentum, double[] InverseDiagonal);
    public sealed record TransformedBody(double[] Position, double[] Matrix, double[] Velocity,
        double[] Momentum, double[] InverseInertia, double[] Omega);
    private sealed record Field(string Name, ulong Address, byte[] Payload, bool Invalidation=false, int FloatSize=0);
    private sealed record Tyre(ulong Pointer, int Count, ulong Records, ulong Begin);
    private readonly GameSession _game;
    private readonly MotionLayout _layout;
    private readonly int _slot;
    private readonly ulong _vehicle, _physics, _begin, _end;
    private readonly ulong[] _bodies;
    private readonly Tyre[] _tyres;
    private readonly Field[] _fields;
    private bool _applied;
    public int WrittenFields { get; private set; }
    public int BytesToWrite => _fields.Sum(f => f.Payload.Length);
    public RecordedPose Target { get; }
    public double[] WheelRates { get; private init; } = [];

    private DirectPosePlan(GameSession game, int slot, ulong vehicle, ulong physics, ulong begin, ulong end,
        ulong[] bodies, Tyre[] tyres, Field[] fields, RecordedPose target)
    { _game=game; _layout=game.RequireMotion(); _slot=slot; _vehicle=vehicle; _physics=physics; _begin=begin; _end=end;
      _bodies=bodies; _tyres=tyres; _fields=fields; Target=target; }

    private static void Vector(double[] v, int length)
    { if(v.Length!=length || v.Any(x=>!double.IsFinite(x))) throw new GateException("Invalid direct-reset vector."); }

    public static void RequireRotation(double[] m)
    {
        Vector(m,9);
        for(var a=0;a<3;a++) for(var b=0;b<3;b++)
            if(Math.Abs(Enumerable.Range(0,3).Sum(r=>m[r*3+a]*m[r*3+b])-(a==b?1:0))>0.0001)
                throw new GateException("Direct-reset orientation is not orthonormal.");
        var det=m[0]*(m[4]*m[8]-m[5]*m[7])-m[1]*(m[3]*m[8]-m[5]*m[6])+m[2]*(m[3]*m[7]-m[4]*m[6]);
        if(Math.Abs(det-1)>0.0001)throw new GateException("Direct-reset orientation is reflected.");
    }

    private static double[] Transpose(double[] m)=>Enumerable.Range(0,9).Select(i=>m[(i%3)*3+i/3]).ToArray();
    private static double[] Multiply(double[] a,double[] b)=>Enumerable.Range(0,9)
        .Select(i=>Enumerable.Range(0,3).Sum(k=>a[(i/3)*3+k]*b[k*3+i%3])).ToArray();
    public static double[] Rotate(double[] m,double[] v)
    { RequireRotation(m); Vector(v,3); return Enumerable.Range(0,3).Select(r=>Enumerable.Range(0,3).Sum(c=>m[r*3+c]*v[c])).ToArray(); }
    private static double[] Matrix(RecordedPose p)=>[p.Row0.X,p.Row0.Y,p.Row0.Z,p.Row1.X,p.Row1.Y,p.Row1.Z,p.Row2.X,p.Row2.Y,p.Row2.Z];

    internal static double[] Euler(double[] matrix)
    {
        RequireRotation(matrix);
        return [Math.Asin(Math.Clamp(-matrix[5],-1,1)),Math.Atan2(matrix[2],matrix[8]),Math.Atan2(matrix[3],matrix[4])];
    }

    internal static TransformedBody CollisionBody(double[] position,double[] matrix,double[] velocity,double[] inverseDiagonal)=>
        Transform(new(position,matrix,velocity,[0,0,0],inverseDiagonal),position,matrix,position,matrix);

    internal static int[] ContactResetOffsets(byte[] records,int count,int stride,int flag0,int flag1)
    {
        if(count is <0 or >4096||stride is <8 or >4096||flag0<0||flag1<0||flag0>=stride||flag1>=stride||flag0==flag1||records.Length!=count*stride)throw new GateException("Invalid tyre contact records.");
        var active=new List<int>();
        for(var i=0;i<count;i++){
            var start=i*stride;
            if(records[start+flag0]!=0)active.Add(start+flag0);
            if(records[start+flag1]!=0)active.Add(start+flag1);
        }
        return active.ToArray();
    }

    internal static bool CacheMatches(byte[] expected,byte[] actual,int floatSize)
    {
        if(expected.AsSpan().SequenceEqual(actual))return true;
        if(expected.Length!=actual.Length||floatSize is not (4 or 8)||expected.Length%floatSize!=0)return false;
        for(var i=0;i<expected.Length;i+=floatSize){
            var e=floatSize==4?BitConverter.ToSingle(expected,i):BitConverter.ToDouble(expected,i);
            var a=floatSize==4?BitConverter.ToSingle(actual,i):BitConverter.ToDouble(actual,i);
            // Native F4E7B0 normalizes the cached float matrix and copies floats into the collision body.
            if(!double.IsFinite(e)||!double.IsFinite(a)||Math.Abs(e-a)>5e-7*Math.Max(1,Math.Abs(e)))return false;
        }
        return true;
    }

    private static void RequireNoCollisionPairs(GameSession game,ulong vehicle)
    {
        // Native collision removal frees owning allocations. Never externally erase this vector.
        var layout=game.RequireMotion();
        var begin=game.Memory.ReadUInt64(game.ModuleBase+layout.CollisionBeginRva);
        var end=game.Memory.ReadUInt64(game.ModuleBase+layout.CollisionEndRva);
        if(end<begin||(end-begin)%layout.CollisionEntryStride!=0||(end-begin)/layout.CollisionEntryStride>65536||(end>begin&&begin==0))
            throw new GateException("Invalid direct-reset collision cache.");
        for(var entry=begin;entry<end;entry+=layout.CollisionEntryStride)
            foreach(var offset in new ulong[]{layout.CollisionDescriptor0,layout.CollisionDescriptor1}) {
                var descriptor=game.Memory.ReadUInt64(entry+offset);
                if(descriptor!=0&&game.Memory.ReadInt32(descriptor+layout.CollisionDescriptorType)==6&&game.Memory.ReadUInt64(descriptor+layout.CollisionDescriptorVehicle)==vehicle)
                    throw new GateException("Move clear of the collision before resetting on track.");
            }
    }

    public static double[] AggregatePosition(RecordedPose target,double[] originOffset)
    {
        if(!Geometry.BuildTargetFromRecordedPose(target).Valid)throw new GateException("Invalid direct-reset target pose.");
        var offset=Rotate(Matrix(target),originOffset);
        return new[]{(double)target.Position.X,target.Position.Y,target.Position.Z}.Zip(offset,(p,o)=>p-o).ToArray();
    }

    public static TransformedBody Transform(BodyState body,double[] oldPosition,double[] oldMatrix,
        double[] targetPosition,double[] targetMatrix)
    {
        Vector(body.Position,3);Vector(body.Velocity,3);Vector(body.Momentum,3);Vector(body.InverseDiagonal,3);
        Vector(oldPosition,3);Vector(targetPosition,3);RequireRotation(oldMatrix);RequireRotation(targetMatrix);RequireRotation(body.Matrix);
        if(body.InverseDiagonal.Any(v=>v<=0))throw new GateException("Invalid rigid-body inverse inertia.");
        var rotation=Multiply(targetMatrix,Transpose(oldMatrix));
        var position=Rotate(rotation,body.Position.Zip(oldPosition,(p,o)=>p-o).ToArray()).Zip(targetPosition,(p,t)=>p+t).ToArray();
        var matrix=Multiply(rotation,body.Matrix);RequireRotation(matrix);
        var velocity=Rotate(rotation,body.Velocity);var momentum=Rotate(rotation,body.Momentum);
        var inverse=Enumerable.Range(0,9).Select(i=>Enumerable.Range(0,3)
            .Sum(k=>matrix[(i/3)*3+k]*body.InverseDiagonal[k]*matrix[(i%3)*3+k])).ToArray();
        var omega=Enumerable.Range(0,3).Select(r=>Enumerable.Range(0,3).Sum(c=>inverse[r*3+c]*momentum[c])).ToArray();
        foreach(var v in new[]{position,matrix,velocity,momentum,inverse,omega})if(v.Any(x=>!double.IsFinite(x)))throw new GateException("Nonfinite direct-reset result.");
        return new(position,matrix,velocity,momentum,[inverse[0],inverse[1],inverse[4],inverse[2],inverse[5],inverse[8]],omega);
    }

    public static DirectPosePlan Prepare(GameSession game,int slot,RecordedPose target,Vec3? capturedVelocity = null)
    {
        var layout=game.RequireMotion();
        var vehicle=game.Offsets.Containers.ContainerAddress(game.ModuleBase,slot)+game.Offsets.Containers.VehicleDelta;
        var physics=game.Memory.ReadUInt64(vehicle+layout.VehiclePhysics);
        if(physics==0)throw new GateException("No direct-reset physics object.");
        var begin=game.Memory.ReadUInt64(physics+layout.PhysicsBodiesBegin);var end=game.Memory.ReadUInt64(physics+layout.PhysicsBodiesEnd);
        if(begin==0||end<begin||end-begin!=96)throw new GateException("Direct reset requires the researched 12-body layout.");
        var bodies=Enumerable.Range(0,12).Select(i=>game.Memory.ReadUInt64(begin+(ulong)i*8)).ToArray();
        var tyres=Enumerable.Range(0,4).Select(i=>{
            var pointer=game.Memory.ReadUInt64(physics+(ulong)i*layout.WheelStride+layout.WheelTyre);
            if(pointer==0)throw new GateException("Missing tyre contact object.");
            var count=game.Memory.ReadInt32(pointer+layout.TyreCount);var records=game.Memory.ReadUInt64(pointer+layout.TyreRecords);
            var cacheBegin=game.Memory.ReadUInt64(pointer+layout.TyreCacheBegin);var cacheEnd=game.Memory.ReadUInt64(pointer+layout.TyreCacheEnd);
            if(count is < 0 or > 4096 || (count>0&&records==0) || cacheBegin==0 || cacheEnd<cacheBegin || cacheEnd-cacheBegin>1048576)
                throw new GateException("Invalid tyre contact cache.");
            return new Tyre(pointer,count,records,cacheBegin);
        }).ToArray();
        var contacts=tyres.Select(t=>t.Pointer+layout.TyreContactBody).ToArray();
        if(bodies.Concat(contacts).Any(p=>p==0||p%8!=0)||bodies.Concat(contacts).Append(physics+layout.PhysicsAggregateBody).Distinct().Count()!=17)
            throw new GateException("Invalid direct-reset body membership.");
        double[] Read(ulong p,int count){var bytes=game.Memory.ReadBytes(p,count*8);return Enumerable.Range(0,count).Select(i=>BitConverter.ToDouble(bytes,i*8)).ToArray();}
        BodyState Body(ulong p)=>new(Read(p+layout.BodyPosition,3),Read(p+layout.BodyMatrix,9),Read(p+layout.BodyVelocity,3),Read(p+layout.BodyMomentum,3),Read(p+layout.BodyInverseDiagonal,3));
        var aggregate=Body(physics+layout.PhysicsAggregateBody);var targetPosition=AggregatePosition(target,Read(physics+layout.PhysicsOriginOffset,3));var targetMatrix=Matrix(target);
        var velocity=capturedVelocity ?? new Vec3(0,0,0);MotionCapture.Validate(velocity);
        var worldVelocity=Rotate(targetMatrix,[velocity.X,velocity.Y,velocity.Z]);
        TransformedBody ResetMotion(TransformedBody state)=>state with{Velocity=worldVelocity.ToArray(),Momentum=[0,0,0],Omega=[0,0,0]};
        var transformed=bodies.ToDictionary(p=>p,p=>ResetMotion(Transform(Body(p),aggregate.Position,aggregate.Matrix,targetPosition,targetMatrix)));
        var rates=new double[4];
        for(var i=0;i<4;i++){
            var wheel=physics+(ulong)i*layout.WheelStride;var spin=game.Memory.ReadUInt64(wheel+layout.WheelSpin);
            if(!transformed.TryGetValue(spin,out var state))throw new GateException("Spin body is not a linked body.");
            var rotation=WheelRotationPlan.Calculate(velocity.Z,Read(wheel+layout.WheelRadius,1)[0],state.Matrix,Read(spin+layout.BodyInertia,3),[0,0,0]);
            rates[i]=rotation.Rate;transformed[spin]=state with{Omega=rotation.Omega,Momentum=rotation.Momentum};
        }
        var fields=new List<Field>();
        void Bytes(string name,ulong p,byte[] payload,bool invalidation=false,int floatSize=0){if(invalidation||!game.Memory.ReadBytes(p,payload.Length).AsSpan().SequenceEqual(payload))fields.Add(new(name,p,payload,invalidation,floatSize));}
        void Doubles(string name,ulong p,double[] values,bool nativeCache=false)=>Bytes(name,p,values.SelectMany(BitConverter.GetBytes).ToArray(),floatSize:nativeCache?8:0);
        void Floats(string name,ulong p,double[] values)=>Bytes(name,p,values.SelectMany(v=>BitConverter.GetBytes((float)v)).ToArray(),floatSize:4);
        void AddBody(ulong p,TransformedBody b,bool nativeCache=false){Doubles("position",p+layout.BodyPosition,b.Position,nativeCache);Doubles("orientation",p+layout.BodyMatrix,b.Matrix,nativeCache);
            Doubles("linear velocity",p+layout.BodyVelocity,b.Velocity,nativeCache);Doubles("angular momentum",p+layout.BodyMomentum,b.Momentum,nativeCache);
            Doubles("inverse inertia",p+layout.BodyInverseInertia,b.InverseInertia,nativeCache);Doubles("angular velocity",p+layout.BodyOmega,b.Omega,nativeCache);}
        // F73900/F4E7B0 refresh vehicle-level pose and collision state before updating linked bodies.
        RequireNoCollisionPairs(game,vehicle);
        var sdkPosition=new[]{(double)target.Position.X,target.Position.Y,target.Position.Z};
        Floats("vehicle orientation",vehicle+layout.VehicleMatrix,targetMatrix);
        Floats("vehicle position",vehicle+layout.VehiclePosition,sdkPosition);
        Floats("vehicle Euler angles",vehicle+layout.VehicleEuler,Euler(targetMatrix));
        Floats("vehicle world velocity",vehicle+layout.VehicleWorldVelocity,worldVelocity);
        Floats("vehicle local velocity",vehicle+layout.VehicleLocalVelocity,[velocity.X,velocity.Y,velocity.Z]);
        Floats("vehicle local angular velocity",vehicle+layout.VehicleLocalOmega,[0,0,0]);
        AddBody(vehicle+layout.VehicleCollisionBody,CollisionBody(sdkPosition,targetMatrix,worldVelocity,Read(vehicle+layout.VehicleCollisionBody+layout.BodyInverseDiagonal,3)),nativeCache:true);
        for(var i=0;i<4;i++)Bytes("wheel road cache",vehicle+(ulong)i*layout.VehicleWheelCacheStride+layout.VehicleWheelRoad0,BitConverter.GetBytes(0),invalidation:true);
        Bytes("road lookup invalidation",vehicle+layout.VehicleRoadPointer,BitConverter.GetBytes(0UL),invalidation:true);
        Bytes("collision overlap invalidation",vehicle+layout.VehicleOverlapFlag,[0],invalidation:true);
        Bytes("pending collision response",vehicle+layout.VehiclePendingCollision,[0],invalidation:true);
        AddBody(physics+layout.PhysicsAggregateBody,ResetMotion(Transform(aggregate,aggregate.Position,aggregate.Matrix,targetPosition,targetMatrix)));
        foreach(var p in bodies)AddBody(p,transformed[p]);
        for(var i=0;i<4;i++){
            var tyre=tyres[i];var spin=game.Memory.ReadUInt64(physics+(ulong)i*layout.WheelStride+layout.WheelSpin);
            if(!transformed.TryGetValue(spin,out var spinState))throw new GateException("Spin body is not a linked body.");
            var diagonal=Read(contacts[i]+layout.BodyInverseDiagonal,3);Vector(diagonal,3);
            if(diagonal.Any(v=>v<=0))throw new GateException("Invalid contact-body inverse inertia.");
            var localOmega=Rotate(Transpose(spinState.Matrix),spinState.Omega);
            var momentum=Rotate(spinState.Matrix,localOmega.Zip(diagonal,(w,d)=>w/d).ToArray());
            AddBody(contacts[i],Transform(new(spinState.Position,spinState.Matrix,spinState.Velocity,momentum,diagonal),targetPosition,targetMatrix,targetPosition,targetMatrix));
            var contactRecords=tyre.Count==0?[]:game.Memory.ReadBytes(tyre.Records,checked(tyre.Count*(int)layout.TyreRecordStride));
            foreach(var offset in ContactResetOffsets(contactRecords,tyre.Count,checked((int)layout.TyreRecordStride),checked((int)layout.TyreFlag0),checked((int)layout.TyreFlag1)))fields.Add(new("contact reset flag",tyre.Records+(ulong)offset,[0]));
            Bytes("contact cache end",tyre.Pointer+layout.TyreCacheEnd,BitConverter.GetBytes(tyre.Begin));
            Doubles("wheel reset marker",physics+(ulong)i*layout.WheelStride+layout.WheelResetMarker,[-double.MaxValue]);
            Doubles("wheel rotation rate",physics+(ulong)i*layout.WheelStride+layout.WheelRate,[rates[i]]);
        }
        Doubles("native pose reset field",physics+layout.PhysicsPoseReset,[0]);
        Bytes("vehicle pose dirty",vehicle+layout.VehiclePoseDirty,[1],invalidation:true);
        var plan=new DirectPosePlan(game,slot,vehicle,physics,begin,end,bodies,tyres,fields.ToArray(),target){WheelRates=rates};
        plan.Preflight();return plan;
    }

    public void RequireCurrent()
    {
        if(_game.Process.HasExited||new LiveStateReader(_game).ResolveSlotIndex()!=_slot||
            _game.Memory.ReadUInt64(_vehicle+_layout.VehiclePhysics)!=_physics||_game.Memory.ReadUInt64(_physics+_layout.PhysicsBodiesBegin)!=_begin||_game.Memory.ReadUInt64(_physics+_layout.PhysicsBodiesEnd)!=_end)
            throw new GateException("Direct-reset physics context changed.");
        RequireNoCollisionPairs(_game,_vehicle);
        var members=_game.Memory.ReadBytes(_begin,_bodies.Length*8);
        for(var i=0;i<_bodies.Length;i++)if(BitConverter.ToUInt64(members,i*8)!=_bodies[i])throw new GateException("Direct-reset body membership changed.");
        var tyrePointers=_game.Memory.ReadBytes(_physics+_layout.WheelTyre,checked((int)(3*_layout.WheelStride+8)));
        for(var i=0;i<4;i++){var t=_tyres[i];
            if(BitConverter.ToUInt64(tyrePointers,checked((int)((ulong)i*_layout.WheelStride)))!=t.Pointer)throw new GateException("Direct-reset tyre context changed.");
            var metadata=_game.Memory.ReadBytes(t.Pointer+_layout.TyreCount,checked((int)(_layout.TyreRecords-_layout.TyreCount+8)));
            if(BitConverter.ToInt32(metadata,0)!=t.Count||BitConverter.ToUInt64(metadata,checked((int)(_layout.TyreRecords-_layout.TyreCount)))!=t.Records||_game.Memory.ReadUInt64(t.Pointer+_layout.TyreCacheBegin)!=t.Begin)
                throw new GateException("Direct-reset tyre context changed.");}
    }
    public object ReadState(){RequireCurrent();return _fields.Select(f=>new{f.Name,bytes=Convert.ToHexString(_game.Memory.ReadBytes(f.Address,f.Payload.Length))}).ToArray();}
    public void Preflight(){RequireCurrent();foreach(var f in _fields)_game.Memory.RequireWritableDataPage(f.Address,f.Payload.Length);}
    public void Apply(Action beforeWrite){if(_applied)throw new GateException("Direct pose can only be applied once.");_applied=true;Preflight();
        foreach(var f in _fields){beforeWrite();RequireCurrent();_game.Memory.WriteVerified(f.Address,f.Payload);WrittenFields++;}}
    public void VerifyApplied(){
        RequireCurrent();
        // Invalidation writes verify immediately in Apply; native updates may consume them while physics is paused.
        foreach(var f in _fields.Where(f=>!f.Invalidation))
            if(!CacheMatches(f.Payload,_game.Memory.ReadBytes(f.Address,f.Payload.Length),f.FloatSize))throw new GateException($"Applied direct-reset {f.Name} changed.");
        if(_game.Memory.ReadUInt64(_vehicle+_layout.VehicleRoadPointer)!=0){
            double[] position=[_game.Memory.ReadSingle(_vehicle+_layout.VehicleRoadQuery),_game.Memory.ReadSingle(_vehicle+_layout.VehicleRoadQuery+4),_game.Memory.ReadSingle(_vehicle+_layout.VehicleRoadQuery+8)];
            var target=new[]{(double)Target.Position.X,Target.Position.Y,Target.Position.Z};
            if(!CacheMatches(target.SelectMany(BitConverter.GetBytes).ToArray(),position.SelectMany(BitConverter.GetBytes).ToArray(),8))
                throw new GateException("Direct-reset road lookup was refreshed for a different position.");
        }
    }
}
