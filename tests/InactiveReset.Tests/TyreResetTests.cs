using InactiveReset.Core;
using System.Text.Json;
using Xunit;
namespace InactiveReset.Tests;
public sealed class TyreResetTests
{
    [Fact]
    public void Individual_api_values_keep_wheel_order_and_optional_heat()
    {
        using var json = JsonDocument.Parse("""
            {"wheels":[{"conditionPercent":100,"temperatureCelsius":70},
            {"conditionPercent":85,"temperatureCelsius":null},
            {"conditionPercent":40,"temperatureCelsius":55},{"conditionPercent":15}]}
            """);
        var wheels = TyreResetOptions.FromJson(json.RootElement).ResolveWheels();
        Assert.Equal(new double[] {100,85,40,15}, wheels.Select(w=>w.ConditionPercent));
        Assert.Equal(new double?[] {70,null,55,null}, wheels.Select(w=>w.TemperatureCelsius));
    }

    [Fact]
    public void Uniform_callers_and_old_api_requests_still_prepare_four_equal_tyres()
    {
        using var json = JsonDocument.Parse("""{"conditionPercent":75,"temperatureCelsius":60}""");
        Assert.All(TyreResetOptions.FromJson(json.RootElement).ResolveWheels(), w=>Assert.Equal(new TyreWheelOptions(75,60),w));
        Assert.All(new TyreResetOptions(90).ResolveWheels(), w=>Assert.Equal(new TyreWheelOptions(90),w));
    }

    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(5)]
    public void Individual_requests_require_exactly_four_wheels(int count) =>
        Assert.Throws<ArgumentException>(()=>new TyreResetOptions(Wheels:Enumerable.Repeat(new TyreWheelOptions(),count).ToArray()).ResolveWheels());

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Every_wheel_is_validated_before_preparation(int index)
    {
        var wheels = Enumerable.Repeat(new TyreWheelOptions(),4).ToArray();
        wheels[index]=new(101,70);
        Assert.Throws<ArgumentException>(()=>new TyreResetOptions(Wheels:wheels).ResolveWheels());
        wheels[index]=new(50,double.NaN);
        Assert.Throws<ArgumentException>(()=>new TyreResetOptions(Wheels:wheels).ResolveWheels());
        wheels[index]=null!;
        Assert.Throws<ArgumentException>(()=>new TyreResetOptions(Wheels:wheels).ResolveWheels());
    }

    [Fact]
    public void Resolving_requests_takes_a_snapshot_before_arming()
    {
        var wheels = Enumerable.Repeat(new TyreWheelOptions(75,60),4).ToArray();
        var snapshot = new TyreResetOptions(Wheels:wheels).ResolveWheels();
        wheels[0]=new(0,0);
        Assert.Equal(new TyreWheelOptions(75,60),snapshot[0]);
    }

    [Fact]
    public void Readback_compares_each_wheel_with_its_own_condition_and_heat()
    {
        var wheel = new WheelTyreState(new(45,46,45),55,new(55,55,55),.4,0,1);
        Assert.True(TyreResetPreparation.RequestedTyreMatches(wheel,new(40,55)));
        Assert.False(TyreResetPreparation.RequestedTyreMatches(wheel,new(85,55)));
        Assert.False(TyreResetPreparation.RequestedTyreMatches(wheel,new(40,70)));
        Assert.True(TyreResetPreparation.RequestedTyreMatches(wheel,new(40)));
        Assert.False(TyreResetPreparation.RequestedTyreMatches(wheel with { Flat=true },new(40)));
        Assert.False(TyreResetPreparation.RequestedTyreMatches(wheel with { Detached=true },new(40)));
    }

    [Theory]
    [InlineData("{\"wheels\":null}")]
    [InlineData("{\"wheels\":{}}")]
    [InlineData("{\"wheels\":[]}")]
    public void Malformed_individual_api_requests_are_rejected(string body)
    {
        using var json = JsonDocument.Parse(body);
        Assert.Throws<ArgumentException>(()=>TyreResetOptions.FromJson(json.RootElement));
    }

    [Fact]
    public void Temperature_check_verifies_internal_layers_while_surface_can_cool()
    {
        var wheel=new WheelTyreState(new(63,64,63),70,new(70,70,70),1,0,1);
        Assert.True(TyreResetPreparation.InternalTemperatureMatches(wheel,70));
        Assert.False(TyreResetPreparation.InternalTemperatureMatches(wheel with { CarcassCelsius=20 },70));
        Assert.False(TyreResetPreparation.InternalTemperatureMatches(wheel with { InnerLayerCelsius=new(70,double.NaN,70) },70));
    }
    [Fact]
    public void Thermal_verification_allows_small_physics_updates_but_rejects_wrong_values()
    {
        var expected=BitConverter.GetBytes(343.15);
        Assert.True(ProcessMemory.ThermalReadbackMatches(expected,BitConverter.GetBytes(342.9),2));
        Assert.False(ProcessMemory.ThermalReadbackMatches(expected,BitConverter.GetBytes(300d),2));
        Assert.False(ProcessMemory.ThermalReadbackMatches(expected,BitConverter.GetBytes(double.NaN),2));
        Assert.False(ProcessMemory.ThermalReadbackMatches(expected,new byte[4],2));
    }

    [Theory]
    [InlineData(-1, 70)] [InlineData(101, 70)] [InlineData(100, -1)] [InlineData(100, 151)]
    [InlineData(double.NaN, 70)] [InlineData(100, double.PositiveInfinity)]
    public void Invalid_requests_are_rejected(double condition, double temperature) =>
        Assert.Throws<ArgumentException>(() => new TyreResetOptions(condition, temperature).Validate());

    [Fact]
    public void Inventory_uses_percentage_and_leaves_engine_heat_index_alone()
    {
        var payloads = TyreResetPreparation.InventoryPayloads(0x1000, 0xf0, 0x10, 75).ToArray();
        Assert.Equal(0x10f0UL, payloads[0].Address);
        Assert.Equal(-1d, BitConverter.ToDouble(payloads[0].Payload, 0));
        Assert.Equal(75d, BitConverter.ToDouble(payloads[0].Payload, 8));
        Assert.Equal(new byte[]{1,1}, payloads[1].Payload);
    }

    [Fact]
    public void Partial_staging_failure_restores_all_touched_records()
    {
        var memory = new Dictionary<ulong, byte[]> { [1]=[10], [2]=[20] };
        var writes = 0;
        void Write(ulong address, ReadOnlySpan<byte> data)
        {
            memory[address]=data.ToArray();
            if (++writes == 2) throw new MemoryAccessException("partial write");
        }
        using var transaction = new TyreInventoryTransaction(
            [new(1,[10],[11]), new(2,[20],[21])], Write, () => true, () => false);
        Assert.Throws<MemoryAccessException>(() => transaction.Stage());
        Assert.Equal(new byte[]{10}, memory[1]);
        Assert.Equal(new byte[]{20}, memory[2]);
        Assert.Equal(4, writes);
    }

    [Theory]
    [InlineData(false, false, false, 2)]
    [InlineData(true, false, false, 1)]
    [InlineData(false, true, false, 1)]
    [InlineData(false, false, true, 1)]
    public void Dispose_restores_only_unconsumed_records_in_the_same_session(bool commit, bool changed, bool driving, int expectedWrites)
    {
        var writes=0; var same=true; var player=false;
        var transaction=new TyreInventoryTransaction([new(1,[10],[11])], (_,_)=>writes++, () => same, () => player);
        transaction.Stage();
        if(commit) transaction.Commit();
        same=!changed; player=driving;
        transaction.Dispose(); transaction.Dispose();
        Assert.Equal(expectedWrites,writes);
    }

    [Fact]
    public void Drive_before_staging_does_not_write()
    {
        var writes=0;
        using var transaction=new TyreInventoryTransaction([new(1,[10],[11])], (_,_)=>writes++, () => true, () => true);
        Assert.Throws<GateException>(()=>transaction.Stage());
        Assert.Equal(0,writes);
    }
}
