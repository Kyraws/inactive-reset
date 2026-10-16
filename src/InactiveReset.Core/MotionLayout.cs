using System.Collections.Frozen;
using System.Text.Json.Nodes;
namespace InactiveReset.Core;

/// <summary>Immutable reset layout derived from the loaded engine's instruction operands.</summary>
public sealed class MotionLayout
{
    public const int ResolverVersion=1;
    private readonly FrozenDictionary<string,ulong> _values;
    private readonly (ulong Rva,byte[] Bytes)[] _probes;
    private readonly (int Start,int End)[] _code;
    private readonly JsonObject _json;
    public string ExecutableSha256 { get; }
    public int ImageSize { get; }
    public int ProbeCount=>_probes.Length;
    public ulong VehiclePhysics => _values[nameof(VehiclePhysics)];
    public ulong VehicleMatrix => _values[nameof(VehicleMatrix)];
    public ulong VehiclePosition => _values[nameof(VehiclePosition)];
    public ulong VehicleEuler => _values[nameof(VehicleEuler)];
    public ulong VehicleWorldVelocity => _values[nameof(VehicleWorldVelocity)];
    public ulong VehicleLocalVelocity => _values[nameof(VehicleLocalVelocity)];
    public ulong VehicleLocalOmega => _values[nameof(VehicleLocalOmega)];
    public ulong VehicleWheelRoad0 => _values[nameof(VehicleWheelRoad0)];
    public ulong VehicleWheelRoad1 => _values[nameof(VehicleWheelRoad1)];
    public ulong VehicleWheelRoad2 => _values[nameof(VehicleWheelRoad2)];
    public ulong VehicleWheelRoad3 => _values[nameof(VehicleWheelRoad3)];
    public ulong VehicleCollisionBody => _values[nameof(VehicleCollisionBody)];
    public ulong VehicleOverlapFlag => _values[nameof(VehicleOverlapFlag)];
    public ulong VehiclePendingCollision => _values[nameof(VehiclePendingCollision)];
    public ulong VehiclePoseDirty => _values[nameof(VehiclePoseDirty)];
    public ulong VehicleRoadPointer => _values[nameof(VehicleRoadPointer)];
    public ulong VehicleRoadQuery => _values[nameof(VehicleRoadQuery)];
    public ulong VehicleEngagedGear => _values[nameof(VehicleEngagedGear)];
    public ulong VehicleRequestedGear => _values[nameof(VehicleRequestedGear)];
    public ulong PhysicsBodiesBegin => _values[nameof(PhysicsBodiesBegin)];
    public ulong PhysicsBodiesEnd => _values[nameof(PhysicsBodiesEnd)];
    public ulong PhysicsChassisBegin => _values[nameof(PhysicsChassisBegin)];
    public ulong PhysicsChassisEnd => _values[nameof(PhysicsChassisEnd)];
    public ulong PhysicsAggregateBody => _values[nameof(PhysicsAggregateBody)];
    public ulong PhysicsAggregateMatrix => _values[nameof(PhysicsAggregateMatrix)];
    public ulong VehicleCollisionPosition => _values[nameof(VehicleCollisionPosition)];
    public ulong VehicleCollisionVelocity => _values[nameof(VehicleCollisionVelocity)];
    public ulong PhysicsAggregatePosition => _values[nameof(PhysicsAggregatePosition)];
    public ulong PhysicsOriginOffset => _values[nameof(PhysicsOriginOffset)];
    public ulong PhysicsPoseReset => _values[nameof(PhysicsPoseReset)];
    public ulong WheelStride => _values[nameof(WheelStride)];
    public ulong WheelSpin => _values[nameof(WheelSpin)];
    public ulong WheelUpright => _values[nameof(WheelUpright)];
    public ulong WheelRadius => _values[nameof(WheelRadius)];
    public ulong WheelRate => _values[nameof(WheelRate)];
    public ulong WheelTyre => _values[nameof(WheelTyre)];
    public ulong WheelRecordBase => _values[nameof(WheelRecordBase)];
    public ulong WheelResetRelative => _values[nameof(WheelResetRelative)];
    public ulong TyreCount => _values[nameof(TyreCount)];
    public ulong TyreRecords => _values[nameof(TyreRecords)];
    public ulong TyreRecordStride => _values[nameof(TyreRecordStride)];
    public ulong TyreFlag0 => _values[nameof(TyreFlag0)];
    public ulong TyreFlag1 => _values[nameof(TyreFlag1)];
    public ulong TyreCacheBegin => _values[nameof(TyreCacheBegin)];
    public ulong TyreCacheEnd => _values[nameof(TyreCacheEnd)];
    public ulong TyreContactBody => _values[nameof(TyreContactBody)];
    public ulong BodyPosition => _values[nameof(BodyPosition)];
    public ulong BodyVelocity => _values[nameof(BodyVelocity)];
    public ulong BodyMatrix => _values[nameof(BodyMatrix)];
    public ulong BodyMomentum => _values[nameof(BodyMomentum)];
    public ulong BodyInverseDiagonal => _values[nameof(BodyInverseDiagonal)];
    public ulong BodyInverseInertia => _values[nameof(BodyInverseInertia)];
    public ulong BodyOmega => _values[nameof(BodyOmega)];
    public ulong BodyInertia => _values[nameof(BodyInertia)];
    public ulong PhysicsEngine => _values[nameof(PhysicsEngine)];
    public ulong GearCachedOmega => _values[nameof(GearCachedOmega)];
    public ulong GearInput => _values[nameof(GearInput)];
    public ulong GearRequested => _values[nameof(GearRequested)];
    public ulong GearPrevious => _values[nameof(GearPrevious)];
    public ulong GearEngaged => _values[nameof(GearEngaged)];
    public ulong GearMax => _values[nameof(GearMax)];
    public ulong GearRatios => _values[nameof(GearRatios)];
    public ulong GearRevOverrides => _values[nameof(GearRevOverrides)];
    public ulong GearRevBase => _values[nameof(GearRevBase)];
    public ulong GearIdle => _values[nameof(GearIdle)];
    public ulong GearDriveType => _values[nameof(GearDriveType)];
    public ulong GearClutch => _values[nameof(GearClutch)];
    public ulong GearActiveRevOverride => _values[nameof(GearActiveRevOverride)];
    public ulong GearShiftDirection => _values[nameof(GearShiftDirection)];
    public ulong GearShiftState => _values[nameof(GearShiftState)];
    public ulong EngineSetterSlot => _values[nameof(EngineSetterSlot)];
    public ulong CollisionEntryStride => _values[nameof(CollisionEntryStride)];
    public ulong CollisionDescriptor0 => _values[nameof(CollisionDescriptor0)];
    public ulong CollisionDescriptor1 => _values[nameof(CollisionDescriptor1)];
    public ulong CollisionDescriptorType => _values[nameof(CollisionDescriptorType)];
    public ulong CollisionDescriptorVehicle => _values[nameof(CollisionDescriptorVehicle)];
    public ulong PauseFlagRva => _values[nameof(PauseFlagRva)];
    public ulong CollisionBeginRva => _values[nameof(CollisionBeginRva)];
    public ulong CollisionEndRva => _values[nameof(CollisionEndRva)];
    public ulong VehicleWheelCacheStride=>VehicleWheelRoad1-VehicleWheelRoad0;
    public ulong WheelResetMarker=>WheelRecordBase+WheelResetRelative;

    private MotionLayout(JsonObject json,string hash)
    {
        if(hash.Length!=64||!hash.All(Uri.IsHexDigit))throw new GateException("Reset profile requires the full executable hash.");
        if(json["resolverVersion"]?.GetValue<int>()!=ResolverVersion||json["executableSha256"]?.GetValue<string>()!=hash)
            throw new GateException("Reset offsets do not belong to this game build/resolver.");
        ExecutableSha256=hash;ImageSize=json["imageSize"]!.GetValue<int>();
        var fields=json["fields"]!.AsObject();
        if(fields.Count!=Seeds.Count||Seeds.Any(s=>!fields.ContainsKey(s.Key)))throw new GateException("Incomplete reset offset profile.");
        _values=fields.ToFrozenDictionary(f=>f.Key,f=>OffsetProfile.ParseHex(f.Value!.GetValue<string>()));
        _probes=json["probes"]!.AsArray().Select(p=>(OffsetProfile.ParseHex(p!["rva"]!.GetValue<string>()),Convert.FromHexString(p["bytes"]!.GetValue<string>()))).ToArray();
        _code=json["codeRanges"]!.AsArray().Select(p=>(p![0]!.GetValue<int>(),p[1]!.GetValue<int>())).ToArray();
        if(ImageSize<=0||_code.Length==0||_code.Any(r=>r.Start<0||r.End<=r.Start||r.End>ImageSize))throw new GateException("Invalid reset code ranges.");
        if(_probes.Length!=Seeds.Sum(s=>s.Value!.AsArray().Count)+Contracts.Count||_probes.Any(p=>!InCode(p.Item1,p.Item2.Length)))
            throw new GateException("Incomplete or invalid reset code probes.");
        Validate();_json=json.DeepClone().AsObject();
    }

    internal static MotionLayout FromJson(JsonObject json,string hash)
    {
        try { return new(json,hash); }
        catch(Exception ex) when(ex is System.Text.Json.JsonException or InvalidOperationException or FormatException
            or OverflowException or ArgumentException or NullReferenceException or KeyNotFoundException)
        { throw new GateException("Incomplete or malformed reset profile: "+ex.Message); }
    }
    internal JsonObject ToJson()=>_json.DeepClone().AsObject();
    internal static readonly JsonObject Seeds=LoadSeeds();
    internal static readonly JsonObject Contracts=LoadContracts();
    private static JsonObject LoadSeeds(){using var s=typeof(MotionLayout).Assembly.GetManifestResourceStream("InactiveReset.Core.discovery-motion.json")!;return JsonNode.Parse(s)!.AsObject();}
    private static JsonObject LoadContracts(){using var s=typeof(MotionLayout).Assembly.GetManifestResourceStream("InactiveReset.Core.discovery-motion-contracts.json")!;return JsonNode.Parse(s)!.AsObject();}
    internal static MotionLayout Discover(byte[] image,string hash)
    {
        var fields=new JsonObject();var probes=new JsonArray();
        foreach(var field in Seeds){
            ulong? value=null;
            foreach(var node in field.Value!.AsArray()){
                var found=CodeAnchor.Resolve(image,node!.AsObject(),field.Key);
                if(value is {} prior&&prior!=found.Value)throw new GateException($"Reset discovery: disagreeing {field.Key} consumers.");
                value=found.Value;
                probes.Add(new JsonObject{["rva"]=$"0x{found.At:X}",["bytes"]=Convert.ToHexString(image.AsSpan(found.At,found.Length))});
            }
            fields[field.Key]=$"0x{value!.Value:X}";
        }
        foreach(var contract in Contracts){
            var found=CodeAnchor.Resolve(image,contract.Value!.AsObject(),contract.Key);
            probes.Add(new JsonObject{["rva"]=$"0x{found.At:X}",["bytes"]=Convert.ToHexString(image.AsSpan(found.At,found.Length))});
        }
        var code=AutomaticOffsets.ExecutableRanges(image);
        return new(new JsonObject{["resolverVersion"]=ResolverVersion,["executableSha256"]=hash,["imageSize"]=image.Length,
            ["fields"]=fields,["probes"]=probes,["codeRanges"]=new JsonArray(code.Select(r=>(JsonNode?)new JsonArray(r.Start,r.End)).ToArray())},hash);
    }

    private bool InCode(ulong rva,int size)=>size>0&&rva<=(ulong)ImageSize&&(ulong)size<=(ulong)ImageSize-rva&&_code.Any(r=>rva>=(ulong)r.Start&&rva+(ulong)size<=(ulong)r.End);
    private void Validate()
    {
        foreach(var f in _values){
            if(f.Value==0||(!f.Key.EndsWith("Rva")&&f.Value>0x50000))throw new GateException($"Reset discovery: invalid {f.Key}.");
            if(f.Key.EndsWith("Rva")&&(f.Value+8>(ulong)ImageSize||_code.Any(r=>f.Value>=(ulong)r.Start&&f.Value<(ulong)r.End)))
                throw new GateException($"Reset discovery: {f.Key} is not module data.");
        }
        if(PhysicsBodiesEnd!=PhysicsBodiesBegin+8||PhysicsChassisEnd!=PhysicsChassisBegin+8||TyreCacheEnd!=TyreCacheBegin+8||TyreRecords!=TyreCount+8||
            PhysicsAggregatePosition!=PhysicsAggregateBody+BodyPosition||PhysicsAggregateMatrix!=PhysicsAggregateBody+BodyMatrix||
            VehicleCollisionPosition!=VehicleCollisionBody+BodyPosition||VehicleCollisionVelocity!=VehicleCollisionBody+BodyVelocity||
            CollisionEndRva!=CollisionBeginRva+8||CollisionDescriptor1!=CollisionDescriptor0+8||CollisionEntryStride<CollisionDescriptor1+8||CollisionEntryStride%8!=0||
            CollisionDescriptorVehicle!=CollisionDescriptorType+4||TyreRecordStride is <8 or >4096||TyreRecordStride<=Math.Max(TyreFlag0,TyreFlag1)||TyreRecordStride%8!=0||TyreFlag0==TyreFlag1||
            WheelStride is <0x100 or >0x10000||VehicleWheelRoad1<=VehicleWheelRoad0||VehicleWheelRoad2!=VehicleWheelRoad0+2*VehicleWheelCacheStride||VehicleWheelRoad3!=VehicleWheelRoad0+3*VehicleWheelCacheStride||
            EngineSetterSlot%8!=0||EngineSetterSlot>0x400)
            throw new GateException("Reset discovery: unsupported vector, body, wheel or collision layout relationships.");
        foreach(var offset in new[]{WheelSpin,WheelUpright,WheelRadius,WheelRate,WheelTyre,WheelResetMarker})
            if(offset<WheelRecordBase||offset>=WheelRecordBase+WheelStride)throw new GateException("Reset discovery: inconsistent wheel layout.");
        var body=new[]{(BodyPosition,24UL),(BodyVelocity,24UL),(BodyMatrix,72UL),(BodyMomentum,24UL),(BodyInverseDiagonal,24UL),(BodyInverseInertia,48UL),(BodyOmega,24UL),(BodyInertia,24UL)}.OrderBy(r=>r.Item1).ToArray();
        for(var i=0;i<body.Length;i++)if(body[i].Item1%8!=0||(i>0&&body[i-1].Item1+body[i-1].Item2>body[i].Item1))throw new GateException("Reset discovery: overlapping or unaligned rigid-body fields.");
    }

    internal void VerifyProbes(Func<ulong,int,byte[]> read)
    {
        foreach(var probe in _probes)if(!read(probe.Rva,probe.Bytes.Length).AsSpan().SequenceEqual(probe.Bytes))
            throw new GateException("Checkpoint reset code changed after discovery. Reconnect to resolve this game update.");
    }

    internal sealed record EngineSetter(ulong Vtable,ulong Function,ulong OmegaOffset,byte[] Code);
    internal EngineSetter ReadEngineSetter(GameSession game,ulong engine)
    {
        if(engine==0||engine%8!=0)throw new GateException("No compatible engine state is loaded.");
        var table=game.Memory.ReadUInt64(engine);
        if(table<game.ModuleBase||EngineSetterSlot+8>(ulong)ImageSize||table-game.ModuleBase>(ulong)ImageSize-EngineSetterSlot-8)
            throw new GateException("Unsupported engine vtable.");
        var function=game.Memory.ReadUInt64(table+EngineSetterSlot);
        if(function<game.ModuleBase||!InCode(function-game.ModuleBase,9))throw new GateException("Engine speed setter is not game code.");
        var code=game.Memory.ReadBytes(function,9);
        return new(table,function,DecodeEngineSetter(code),code);
    }
    internal static ulong DecodeEngineSetter(byte[] code)
    {
        if(code.Length!=9||!code.AsSpan(0,4).SequenceEqual(new byte[]{0xf2,0x0f,0x11,0x89})||code[8]!=0xc3)
            throw new GateException("Unsupported engine speed setter; no reset writes were made.");
        var field=BitConverter.ToInt32(code,4);
        if(field<=0||field>0x10000||field%8!=0)throw new GateException("Invalid engine speed field.");
        return (ulong)field;
    }
}
