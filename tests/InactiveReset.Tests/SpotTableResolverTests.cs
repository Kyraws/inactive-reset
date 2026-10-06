using System.Buffers.Binary;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class SpotTableResolverTests
{
    [Fact]
    public void Pit_speed_rule_requires_unique_speed_gate_reader()
    {
        var image = MappedImage();
        void Gate(int at)
        {
            new byte[] { 0xF3, 0x0F, 0x10, 0x8B, 0x18, 0x72, 0x02, 0x00,
                         0x0F, 0x2F, 0xCA, 0x76, 0x33, 0x83, 0x3D }.CopyTo(image, at);
            Put32(image, at + 0x0F, 0x3100 - (at + 0x14));
            image[at + 0x14] = 0x74;
            image[at + 0x3B] = 0xE8;
        }
        Assert.Null(SpotTableResolver.FindPitSpeedRule(image));
        Gate(0x1100);
        Assert.Equal(0x3100u, SpotTableResolver.FindPitSpeedRule(image)?.FlagRulesRva);
        Gate(0x1500);
        Assert.Null(SpotTableResolver.FindPitSpeedRule(image));
        image[0x1500] = 0;
        image[0x1100 + 0x3B] = 0;
        Assert.Null(SpotTableResolver.FindPitSpeedRule(image));
    }

    [Theory]
    [InlineData(0x471D4, 0x471E8)]
    [InlineData(0x471E4, 0x471F8)]
    public void Requires_two_agreeing_independent_readers_and_rejects_ambiguity(int slot, int pit)
    {
        var image = MappedImage();
        IndexSite(image, 0x1400, slot, pit);
        IndexSite(image, 0x2600, slot, pit);
        GarageReader(image, 0x1100, 0x3200, 0x3104, 0x3108);
        GarageWriter(image, 0x2400, 0x3200, 0x3104, 0x3108);
        IndexedDestination(image, 0x1A00, 0x3300, pit, 0x3104);
        IndexedSpotFallback(image, 0x2B00, 0x3300);
        IndexedAssignmentLoop(image, 0x2700, 0x3300, pit);
        Mode2Reader(image, 0x1500, 0x90B0);
        ModeWriter(image, 0x2300, 0x90B0);
        Mode2IndexedCall(image, 0x1500, 0x1980);
        Reader(image, 0x1000, 0x3000, 0x3090, slot);
        Assert.Null(SpotTableResolver.FindPitPos(image));
        Reader(image, 0x2200, 0x3000, 0x3090, slot);
        var found = SpotTableResolver.FindPitPos(image);
        Assert.NotNull(found);
        Assert.Equal(0x3000u, found.PitPosRva);
        Assert.Equal(0x3090u, found.CountRva);
        Assert.Equal(2, found.IndependentReaders);
        Assert.Equal(SpotTableResolver.PitPosReaderPath.SpecialSlotBranch, found.ReaderPath);
        Assert.False(SpotTableResolver.MatchesOrdinaryPitPos(found, 0x3000));
        Assert.Throws<GateException>(() => SpotTableResolver.RequireOrdinaryPitPos(image, 0x3000));

        Reader(image, 0x2500, 0x3100, 0x3190, slot);
        Assert.Null(SpotTableResolver.FindPitPos(image));
        image[0x2500] = 0;
        image[0x2200 + 39] = 0;
        Assert.Null(SpotTableResolver.FindPitPos(image));
    }

    [Fact]
    public void Garage_table_requires_independent_reader_and_writer()
    {
        var image = MappedImage();
        GarageReader(image, 0x1100, 0x3200, 0x3104, 0x3108);
        Assert.Null(SpotTableResolver.FindGaragePos(image));
        GarageWriter(image, 0x2300, 0x3200, 0x3104, 0x3108);
        var found = SpotTableResolver.FindGaragePos(image);
        Assert.NotNull(found);
        Assert.Equal(0x3200u, found.GaragePosRva);
        Assert.Equal(0x3104u, found.MultRva);
        Assert.Equal(0x3108u, found.CountRva);

        GarageReader(image, 0x1400, 0x3300, 0x3114, 0x3118);
        GarageWriter(image, 0x2800, 0x3300, 0x3114, 0x3118);
        Assert.Null(SpotTableResolver.FindGaragePos(image));
        image[0x1400] = 0;
        image[0x2800] = 0;
        image[0x2300 + 39] = 0;
        Assert.Null(SpotTableResolver.FindGaragePos(image));
    }

    [Fact]
    public void Normal_slot_table_requires_reader_and_both_writers_without_ambiguity()
    {
        var image = MappedImage();
        NormalReader(image, 0x1000, 0x3000);
        NormalWriter(image, 0x2200, 0x3000, 0);
        Assert.Null(SpotTableResolver.FindNormalSlotTable(image));
        NormalWriter(image, 0x2400, 0x3000, 12);
        Assert.Equal(0x3000u, SpotTableResolver.FindNormalSlotTable(image)?.TableRva);

        NormalReader(image, 0x1300, 0x3100);
        NormalWriter(image, 0x2600, 0x3100, 0);
        NormalWriter(image, 0x2800, 0x3100, 12);
        Assert.Null(SpotTableResolver.FindNormalSlotTable(image));
        image[0x1300 + 21] = 0;
        Assert.Equal(0x3000u, SpotTableResolver.FindNormalSlotTable(image)?.TableRva);
    }

    [Fact]
    public void Normal_slot_selection_requires_verified_default_and_unique_override_branch()
    {
        var image = MappedImage();
        NormalReader(image, 0x1000, 0x3000);
        NormalWriter(image, 0x2200, 0x3000, 0);
        NormalWriter(image, 0x2400, 0x3000, 12);
        Selection(image, 0x1800, 0x3100, 0x3110, 0x3118, 0x3000);
        var selected = SpotTableResolver.FindNormalSlotSelection(image);
        Assert.NotNull(selected);
        Assert.Equal(0x3100u, selected.SwitchRva);
        Assert.Equal(0x3110u, selected.OverrideStartRva);
        Assert.Equal(0x3118u, selected.OverrideEndRva);
        image[0x1800 + 86 + 3]++;
        Assert.Null(SpotTableResolver.FindNormalSlotSelection(image));
        Selection(image, 0x1800, 0x3100, 0x3110, 0x3118, 0x3000);
        Selection(image, 0x1900, 0x3120, 0x3130, 0x3138, 0x3000);
        Assert.Null(SpotTableResolver.FindNormalSlotSelection(image));
    }

    [Fact]
    public void Track_limits_flags_require_initializer_and_separate_readers()
    {
        var image = MappedImage();
        TrackFlagsInit(image, 0x1100, 0x3100);
        TrackFlagRead(image, 0x1300, 0x3100);
        TrackFlagRead(image, 0x1500, 0x3101);
        TrackFlagRead(image, 0x1700, 0x3101);
        Assert.Null(SpotTableResolver.FindTrackLimitsFlags(image));
        TrackFlagRead(image, 0x1900, 0x3102);
        Assert.Equal(0x3100u, SpotTableResolver.FindTrackLimitsFlags(image)?.FirstRva);
        TrackFlagsInit(image, 0x1B00, 0x3200);
        Assert.Equal(0x3100u, SpotTableResolver.FindTrackLimitsFlags(image)?.FirstRva);
        TrackFlagRead(image, 0x1C00, 0x3200);
        TrackFlagRead(image, 0x1D00, 0x3201);
        TrackFlagRead(image, 0x1E00, 0x3201);
        TrackFlagRead(image, 0x1F00, 0x3202);
        Assert.Null(SpotTableResolver.FindTrackLimitsFlags(image));
    }

    [Fact]
    public void Indexed_destination_requires_pit_mult_and_two_agreeing_table_reads()
    {
        var image = MappedImage();
        IndexSite(image, 0x1000, 0x471D4, 0x471E8);
        IndexSite(image, 0x2300, 0x471D4, 0x471E8);
        GarageReader(image, 0x1100, 0x3200, 0x3104, 0x3108);
        GarageWriter(image, 0x2400, 0x3200, 0x3104, 0x3108);
        IndexedDestination(image, 0x1A00, 0x3300, 0x471E8, 0x3104);
        Assert.Null(SpotTableResolver.FindIndexedDestination(image));
        IndexedSpotFallback(image, 0x2B00, 0x3300);
        Assert.Null(SpotTableResolver.FindIndexedDestination(image));
        IndexedAssignmentLoop(image, 0x2700, 0x3300, 0x471E8);
        Assert.Null(SpotTableResolver.FindIndexedDestination(image));
        Mode2Reader(image, 0x1500, 0x90B0);
        ModeWriter(image, 0x2500, 0x90B0);
        Mode2IndexedCall(image, 0x1500, 0x1980);
        Assert.Equal(0x3300u, SpotTableResolver.FindIndexedDestination(image)?.TableRva);
        image[0x1500 + 27]++;
        Assert.Null(SpotTableResolver.FindIndexedDestination(image));
        Mode2IndexedCall(image, 0x1500, 0x1980);
        image[0x2700 + 0xF1] = 0;
        Assert.Null(SpotTableResolver.FindIndexedDestination(image));
        IndexedAssignmentLoop(image, 0x2700, 0x3300, 0x471E8);
        image[0x2B00 + 24]++;
        Assert.Null(SpotTableResolver.FindIndexedDestination(image));
        IndexedSpotFallback(image, 0x2B00, 0x3300);
        image[0x1A00 + 39 + 3]++;
        Assert.Null(SpotTableResolver.FindIndexedDestination(image));
        IndexedDestination(image, 0x1A00, 0x3300, 0x471E8, 0x3104);
        IndexedDestination(image, 0x1B00, 0x3400, 0x471E8, 0x3104);
        Assert.Null(SpotTableResolver.FindIndexedDestination(image));
    }

    [Fact]
    public void Destination_mode_requires_mode_two_consumer_and_masked_writer()
    {
        var image = MappedImage();
        Mode2Reader(image, 0x1200, 0x90B0);
        Assert.Null(SpotTableResolver.FindDestinationMode(image));
        ModeWriter(image, 0x2300, 0x90B0);
        Assert.Equal(0x90B0u, SpotTableResolver.FindDestinationMode(image)?.Offset);
        Mode2Reader(image, 0x1400, 0x9098);
        ModeWriter(image, 0x2500, 0x9098);
        Assert.Null(SpotTableResolver.FindDestinationMode(image));
        image[0x1400 + 18] = 0;
        Assert.Equal(0x90B0u, SpotTableResolver.FindDestinationMode(image)?.Offset);
    }

    [Fact]
    public void Pit_state_requires_reset_pair_and_separated_consumers()
    {
        var image = MappedImage();
        PitResetWriter(image, 0x1100, 0x8FD8);
        foreach (var site in new[] { 0x1300, 0x1700, 0x1B00, 0x1F00 })
            PitStateCompare(image, site, 0x8FD8);
        Assert.Null(SpotTableResolver.FindPitState(image));
        PitStateCompare(image, 0x2600, 0x8FD8);
        var found = SpotTableResolver.FindPitState(image);
        Assert.Equal(0x8FD8u, found?.StateOffset);
        Assert.Equal(0x8FE8u, found?.PitFlagOffset);
        SpotTableResolver.RequirePitState(image, IndexProfile(0x471D4, 0x471E8));
        Assert.Throws<GateException>(() => SpotTableResolver.RequirePitState(
            image, IndexProfile(0x471D4, 0x471E8, pitState: 0x8FC0)));
        image[0x1100 + 6] = 0;
        Assert.Null(SpotTableResolver.FindPitState(image));
    }

    [Fact]
    public void Legacy_pit_table_requires_two_readers_and_independent_mult_count()
    {
        var image = MappedImage();
        GarageReader(image, 0x1100, 0x3200, 0x3104, 0x3108);
        GarageWriter(image, 0x2300, 0x3200, 0x3104, 0x3108);
        LegacyPitReader(image, 0x1400, 0x3300, 0x3104);
        Assert.Null(SpotTableResolver.FindPitPos(image));
        LegacyPitReader(image, 0x1A00, 0x3300, 0x3104);
        var found = SpotTableResolver.FindPitPos(image);
        Assert.NotNull(found);
        Assert.Equal(0x3300u, found.PitPosRva);
        Assert.Equal(0x3108u, found.CountRva);
        Assert.Equal(SpotTableResolver.PitPosReaderPath.OrdinarySlotLoop, found.ReaderPath);
        Assert.True(SpotTableResolver.MatchesOrdinaryPitPos(found, 0x3300));
        Assert.False(SpotTableResolver.MatchesOrdinaryPitPos(found, 0x3400));
        SpotTableResolver.RequireOrdinaryPitPos(image, 0x3300);
        Assert.Throws<GateException>(() => SpotTableResolver.RequireOrdinaryPitPos(image, 0x3400));
        image[0x1A00 + 28] = 0;
        Assert.Null(SpotTableResolver.FindPitPos(image));
    }

    [Fact]
    public void Container_stride_requires_dominant_separated_code_evidence()
    {
        var image = MappedImage();
        for (var i = 0; i < 20; i++)
            StrideSite(image, 0x1000 + i * 0x120, 0x47308);
        Assert.Equal(0x47308u, SpotTableResolver.FindContainerStride(image)?.Stride);
        SpotTableResolver.RequireContainerStride(image, 0x47308);
        Assert.Throws<GateException>(() => SpotTableResolver.RequireContainerStride(image, 0x472C8));
        for (var i = 0; i < 2; i++)
            StrideSite(image, 0x2800 + i * 0x20, 0x72D18);
        Assert.Null(SpotTableResolver.FindContainerStride(image));
        image[0x2800] = 0;
        Assert.Equal(0x47308u, SpotTableResolver.FindContainerStride(image)?.Stride);
    }

    [Fact]
    public void Special_slot_indices_require_separated_agreement_and_refuse_ambiguity()
    {
        var image = MappedImage();
        IndexSite(image, 0x1000, 0x471D4, 0x471E8);
        Assert.Null(SpotTableResolver.FindSpecialSlotIndices(image));
        IndexSite(image, 0x2300, 0x471D4, 0x471E8);
        var found = SpotTableResolver.FindSpecialSlotIndices(image);
        Assert.NotNull(found);
        Assert.Equal(0x471D4u, found.SlotIndex);
        Assert.Equal(0x471E8u, found.PitIndex);
        Assert.Equal(0x471ECu, found.GarageIndex);
        Assert.Equal(0x1800u, found.TransformRva);
        SpotTableResolver.RequireSpecialSlotIndices(image, IndexProfile(0x471D4, 0x471E8), 0x1800);
        Assert.Throws<GateException>(() => SpotTableResolver.RequireSpecialSlotIndices(
            image, IndexProfile(0x47194, 0x471A8), 0x1800));
        Assert.Throws<GateException>(() => SpotTableResolver.RequireSpecialSlotIndices(
            image, IndexProfile(0x471D4, 0x471E8), 0x1900));
        Put32(image, 0x2300 + 34, 0x1900 - (0x2300 + 38));
        Assert.Null(SpotTableResolver.FindSpecialSlotIndices(image));
        Put32(image, 0x2300 + 34, 0x1800 - (0x2300 + 38));
        IndexSite(image, 0x2700, 0x47194, 0x471A8);
        Assert.Null(SpotTableResolver.FindSpecialSlotIndices(image));
        image[0x2700] = 0;
        image[0x2300 + 24] = 0;
        Assert.Null(SpotTableResolver.FindSpecialSlotIndices(image));
    }

    [Fact]
    public void Container_array_base_requires_separated_stride_index_agreement()
    {
        var image = MappedImage(0x2000000);
        for (var i = 0; i < 20; i++)
            StrideSite(image, 0x1000 + i * 0x100, 0x40000);
        ArraySite(image, 0x1110, 0x5000, 0x40000);
        Assert.Null(SpotTableResolver.FindContainerArrayBase(image));
        ArraySite(image, 0x2410, 0x5000, 0x40000);
        Assert.Equal(0x5000u, SpotTableResolver.FindContainerArrayBase(image)?.ArrayBaseRva);
        SpotTableResolver.RequireContainerArrayBase(image, 0x5000);
        Assert.Throws<GateException>(() => SpotTableResolver.RequireContainerArrayBase(image, 0x6000));
        ArraySite(image, 0x2510, 0x6000, 0x40000);
        Assert.Null(SpotTableResolver.FindContainerArrayBase(image));
    }

    [Fact]
    public void Control_owner_requires_handoff_slot_flow_and_separated_witness()
    {
        var image = MappedImage();
        IndexSite(image, 0x1100, 0x471D4, 0x471E8);
        IndexSite(image, 0x2500, 0x471D4, 0x471E8);
        OwnerSite(image, 0x1300, 0x6F90, 0x471D4);
        Assert.Null(SpotTableResolver.FindControlOwner(image));
        new byte[] { 0x83, 0xBB }.CopyTo(image, 0x2700);
        Put32(image, 0x2702, 0x6F90); image[0x2706] = 1;
        Assert.Equal(0x6F90u, SpotTableResolver.FindControlOwner(image)?.Offset);
        SpotTableResolver.RequireControlOwner(image, IndexProfile(0x471D4, 0x471E8));
        Assert.Throws<GateException>(() => SpotTableResolver.RequireControlOwner(
            image, IndexProfile(0x471D4, 0x471E8, 0x6F78)));
        Put32(image, 0x2702, 0x6F88);
        Assert.Null(SpotTableResolver.FindControlOwner(image));
        Put32(image, 0x2702, 0x6F90);
        Put32(image, 0x1300 + 28, 0x47194);
        Assert.Null(SpotTableResolver.FindControlOwner(image));
    }

    [Fact]
    public void Lateral_sign_requires_symmetric_branches_and_separated_reader()
    {
        var image = MappedImage();
        LateralSite(image, 0x1100, 0x468D0);
        Assert.Null(SpotTableResolver.FindLateralSign(image));
        new byte[] { 0xF3, 0x0F, 0x10, 0x83 }.CopyTo(image, 0x2500);
        Put32(image, 0x2504, 0x468D0);
        Assert.Equal(0x468D0u, SpotTableResolver.FindLateralSign(image)?.Offset);
        SpotTableResolver.RequireLateralSign(image, IndexProfile(0x471D4, 0x471E8));
        Assert.Throws<GateException>(() => SpotTableResolver.RequireLateralSign(
            image, IndexProfile(0x471D4, 0x471E8, lateral: 0x46898)));
        image[0x1100 + 43] = 0;
        Assert.Null(SpotTableResolver.FindLateralSign(image));
    }

    [Fact]
    public void Vehicle_dimensions_require_dominant_paired_and_separated_reads()
    {
        var image = MappedImage();
        for (var i = 0; i < 12; i++) ScalarLoad(image, 0x1000 + i * 0x80, 0x444EC);
        for (var i = 0; i < 24; i++) ScalarLoad(image, 0x1700 + i * 0x70, 0x444F0);
        Assert.Null(SpotTableResolver.FindVehicleDimensions(image));
        ScalarLoad(image, 0x1010, 0x444F0);
        ScalarLoad(image, 0x2600, 0x444EC);
        var found = SpotTableResolver.FindVehicleDimensions(image);
        Assert.NotNull(found);
        Assert.Equal(0x444ECu, found.LengthOffset);
        Assert.Equal(0x444F0u, found.WidthOffset);
        SpotTableResolver.RequireVehicleDimensions(image, IndexProfile(0x471D4, 0x471E8));
        Assert.Throws<GateException>(() => SpotTableResolver.RequireVehicleDimensions(
            image, IndexProfile(0x471D4, 0x471E8, length: 0x444B4)));
        for (var i = 0; i < 10; i++) ScalarLoad(image, 0x2800 + i * 0x18, 0x44540);
        for (var i = 0; i < 20; i++) ScalarLoad(image, 0x2900 + i * 0x18, 0x44544);
        Assert.Null(SpotTableResolver.FindVehicleDimensions(image));
    }

    private static byte[] MappedImage(int length = 0x5000)
    {
        var image = new byte[length];
        image[0] = (byte)'M'; image[1] = (byte)'Z';
        Put32(image, 0x3C, 0x80);
        Put32(image, 0x80, 0x4550);
        Put16(image, 0x86, 2);
        Put16(image, 0x94, 0xF0);
        Put16(image, 0x98, 0x20B);
        Put32(image, 0xD0, image.Length);
        Put32(image, 0x188 + 8, 0x2000); Put32(image, 0x188 + 12, 0x1000);
        Put32(image, 0x188 + 36, 0x60000020);
        Put32(image, 0x1B0 + 8, length - 0x3000); Put32(image, 0x1B0 + 12, 0x3000);
        Put32(image, 0x1B0 + 36, unchecked((int)0xC0000040));
        return image;
    }

    private static void Reader(byte[] image, int at, int table, int count, int slot)
    {
        image[at] = 0x83; image[at + 1] = 0xBB;
        Put32(image, at + 2, slot);
        image[at + 6] = 0x68;
        image[at + 9] = 0x8B; image[at + 10] = 0x05;
        Put32(image, at + 11, count - (at + 15));
        image[at + 39] = 0x48; image[at + 40] = 0x8B; image[at + 41] = 0x05;
        Put32(image, at + 42, table - (at + 46));
        new byte[] { 0x8B, 0x44, 0x00, 0x08 }.CopyTo(image, at + 48);
        new byte[] { 0xF2, 0x0F, 0x10, 0x44, 0x00, 0x0C }.CopyTo(image, at + 59);
        new byte[] { 0x8B, 0x44, 0x00, 0x14 }.CopyTo(image, at + 72);
    }

    private static void GarageReader(byte[] image, int at, int table, int mult, int count)
    {
        new byte[] { 0x48, 0x8B, 0x1D }.CopyTo(image, at);
        Put32(image, at + 3, count - (at + 7));
        image[at + 22] = 0x8B; image[at + 23] = 0x0D;
        Put32(image, at + 24, mult - (at + 28));
        image[at + 55] = 0x48; image[at + 56] = 0x8B; image[at + 57] = 0x05;
        Put32(image, at + 58, table - (at + 62));
        new byte[] { 0x8B, 0x44, 0x00, 0x08 }.CopyTo(image, at + 69);
        new byte[] { 0xF2, 0x0F, 0x10, 0x44, 0x00, 0x0C }.CopyTo(image, at + 79);
        new byte[] { 0x8B, 0x44, 0x00, 0x14 }.CopyTo(image, at + 89);
    }

    private static void GarageWriter(byte[] image, int at, int table, int mult, int count)
    {
        image[at] = 0x8B; image[at + 1] = 0x05;
        Put32(image, at + 2, count - (at + 6));
        new byte[] { 0x0F, 0xAF, 0x0D }.CopyTo(image, at + 21);
        Put32(image, at + 24, mult - (at + 28));
        new byte[] { 0x48, 0x8B, 0x05 }.CopyTo(image, at + 39);
        Put32(image, at + 42, table - (at + 46));
        new byte[] { 0xF3, 0x0F, 0x11, 0x04, 0x01 }.CopyTo(image, at + 64);
        new byte[] { 0xF3, 0x0F, 0x11, 0x4C, 0x01, 0x04 }.CopyTo(image, at + 69);
        new byte[] { 0xF3, 0x0F, 0x11, 0x54, 0x01, 0x08 }.CopyTo(image, at + 75);
    }

    private static void NormalReader(byte[] image, int at, int table)
    {
        new byte[] { 0x41, 0x83, 0xFA, 0x68 }.CopyTo(image, at);
        new byte[] { 0x48, 0x8B, 0x05 }.CopyTo(image, at + 10);
        Put32(image, at + 13, table - (at + 17));
        new byte[] { 0x48, 0xC1, 0xE1, 0x05, 0x0F, 0x2E, 0x04, 0x01, 0x0F, 0x85 }
            .CopyTo(image, at + 17);
    }

    private static void Selection(byte[] image, int at, int selector, int start, int end, int table)
    {
        new byte[] { 0x83, 0x3D }.CopyTo(image, at);
        Put32(image, at + 2, selector - (at + 7));
        image[at + 7] = 0x74; image[at + 8] = 77;
        new byte[] { 0x48, 0x8B, 0x05 }.CopyTo(image, at + 9);
        Put32(image, at + 12, start - (at + 16));
        new byte[] { 0x48, 0x3B, 0x05 }.CopyTo(image, at + 16);
        Put32(image, at + 19, end - (at + 23));
        image[at + 23] = 0x74; image[at + 24] = 61;
        new byte[] { 0x49, 0x63, 0xCA, 0x48, 0xC1, 0xE1, 0x05,
                     0xF2, 0x0F, 0x10, 0x04, 0x01 }.CopyTo(image, at + 25);
        new byte[] { 0x48, 0x8B, 0x05 }.CopyTo(image, at + 86);
        Put32(image, at + 89, table - (at + 93));
    }

    private static void TrackFlagsInit(byte[] image, int at, int first)
    {
        new byte[] { 0x66, 0xC7, 0x05 }.CopyTo(image, at);
        Put32(image, at + 3, first - (at + 9));
        image[at + 7] = 1; image[at + 8] = 1;
    }

    private static void TrackFlagRead(byte[] image, int at, int target)
    {
        image[at] = 0x80; image[at + 1] = 0x3D;
        Put32(image, at + 2, target - (at + 7));
    }

    private static void IndexedDestination(byte[] image, int at, int table, int pit, int mult)
    {
        image[at - 33] = 0x8B; image[at - 32] = 0x8B;
        Put32(image, at - 31, pit);
        image[at - 27] = 0x8B; image[at - 26] = 0x05;
        Put32(image, at - 25, mult - (at - 21));
        new byte[] { 0x48, 0x8D, 0x0C, 0x40, 0x48, 0x63, 0x44, 0x24, 0x50,
                     0x48, 0x03, 0xC8, 0x48, 0x8B, 0x05 }.CopyTo(image, at);
        Put32(image, at + 15, table - (at + 19));
        new byte[] { 0x48, 0xC1, 0xE1, 0x05, 0xF2, 0x0F, 0x10, 0x04, 0x01 }
            .CopyTo(image, at + 19);
        new byte[] { 0x48, 0x8B, 0x05 }.CopyTo(image, at + 39);
        Put32(image, at + 42, table - (at + 46));
    }

    private static void IndexedSpotFallback(byte[] image, int at, int table)
    {
        new byte[] { 0x4C, 0x8B, 0x05 }.CopyTo(image, at);
        Put32(image, at + 3, table - (at + 7));
        new byte[] { 0x4C, 0x8D, 0x0C, 0x40 }.CopyTo(image, at + 24);
        new byte[] { 0x49, 0xC1, 0xE1, 0x05 }.CopyTo(image, at + 32);
        new byte[] { 0xF2, 0x43, 0x0F, 0x10, 0x44, 0x01, 0x20 }
            .CopyTo(image, at + 46);
    }

    private static void IndexedAssignmentLoop(byte[] image, int at, int table, int pit)
    {
        new byte[] { 0x48, 0x8D, 0x0C, 0x40, 0x48, 0xC1, 0xE1, 0x05,
                     0x48, 0x8B, 0x05 }.CopyTo(image, at);
        Put32(image, at + 11, table - (at + 15));
        new byte[] { 0xF2, 0x0F, 0x10, 0x74, 0x01, 0x20,
                     0x44, 0x8B, 0x74, 0x01, 0x28,
                     0xF2, 0x0F, 0x10, 0x44, 0x01, 0x2C,
                     0x44, 0x8B, 0x7C, 0x01, 0x34 }.CopyTo(image, at + 15);
        image[at + 64] = 0xE8;
        Put32(image, at + 65, 0x1800 - (at + 69));
        image[at + 0xF1] = 0x89;
        image[at + 0xF2] = 0xBB;
        Put32(image, at + 0xF3, pit);
    }

    private static void Mode2Reader(byte[] image, int at, int offset)
    {
        image[at] = 0x83; image[at + 1] = 0xBA;
        Put32(image, at + 2, offset); image[at + 6] = 2;
        new byte[] { 0xC7, 0x44, 0x24, 0x20, 2, 0, 0, 0, 0xE8 }
            .CopyTo(image, at + 18);
    }

    private static void ModeWriter(byte[] image, int at, int offset)
    {
        new byte[] { 0x83, 0xE1, 0x03, 0x89, 0x88 }.CopyTo(image, at);
        Put32(image, at + 5, offset);
    }

    private static void Mode2IndexedCall(byte[] image, int reader, int helper)
    {
        Put32(image, reader + 27, helper - (reader + 31));
        new byte[] { 0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74,
                     0x24, 0x10, 0x57, 0x48, 0x83, 0xEC, 0x20 }
            .CopyTo(image, helper);
    }

    private static void PitResetWriter(byte[] image, int at, int state)
    {
        image[at] = 0x89; image[at + 1] = 0xAF;
        Put32(image, at + 2, state);
        new byte[] { 0x66, 0xC7, 0x87 }.CopyTo(image, at + 6);
        Put32(image, at + 9, state + 0x10);
    }

    private static void PitStateCompare(byte[] image, int at, int state)
    {
        image[at] = 0x83; image[at + 1] = 0xBB;
        Put32(image, at + 2, state); image[at + 6] = 4;
    }

    private static void LegacyPitReader(byte[] image, int at, int table, int mult)
    {
        image[at] = 0x8B; image[at + 1] = 0x05;
        Put32(image, at + 2, mult - (at + 6));
        new byte[] { 0x48, 0xC1, 0xE1, 0x05 }.CopyTo(image, at + 16);
        new byte[] { 0x48, 0x8B, 0x05 }.CopyTo(image, at + 28);
        Put32(image, at + 31, table - (at + 35));
        new byte[] { 0xF2, 0x0F, 0x10, 0x34, 0x01,
                     0xF2, 0x0F, 0x10, 0x44, 0x01, 0x0C }
            .CopyTo(image, at + 35);
    }

    private static void StrideSite(byte[] image, int at, int stride)
    {
        image[at] = 0x48; image[at + 1] = 0x69; image[at + 2] = 0xC8;
        Put32(image, at + 3, stride);
    }

    private static void ArraySite(byte[] image, int at, int arrayBase, int stride)
    {
        image[at] = 0x4C; image[at + 1] = 0x8D; image[at + 2] = 0x0D;
        Put32(image, at + 3, arrayBase - (at + 7));
        image[at + 16] = 0x48; image[at + 17] = 0x69; image[at + 18] = 0xCA;
        Put32(image, at + 19, stride);
        image[at + 23] = 0x49; image[at + 24] = 0x03; image[at + 25] = 0xC9;
    }

    private static void OwnerSite(byte[] image, int at, int owner, int slot)
    {
        new byte[] { 0x83, 0xBB }.CopyTo(image, at);
        Put32(image, at + 2, owner);
        new byte[] { 0x01, 0x0F, 0x85, 0, 0, 0, 0,
                     0x41, 0xB0, 0x01, 0x33, 0xD2, 0x48, 0x8B, 0xCB, 0xE8 }
            .CopyTo(image, at + 6);
        Put32(image, at + 22, 0x1800 - (at + 26));
        image[at + 26] = 0x83; image[at + 27] = 0xBB;
        Put32(image, at + 28, slot); image[at + 32] = 0x68;
    }

    private static void LateralSite(byte[] image, int at, int offset)
    {
        new byte[] { 0xF3, 0x0F, 0x10, 0x87 }.CopyTo(image, at);
        Put32(image, at + 4, offset);
        new byte[] { 0x0F, 0x2F, 0xC6, 0x76, 0x16, 0xF3, 0x0F, 0x5C, 0x05 }
            .CopyTo(image, at + 8);
        image[at + 21] = 0x0F; image[at + 22] = 0x2F; image[at + 23] = 0x87;
        Put32(image, at + 24, 0x46CF8);
        image[at + 28] = 0x77; image[at + 29] = 0x22; image[at + 30] = 0xE9;
        new byte[] { 0xF3, 0x0F, 0x10, 0x87 }.CopyTo(image, at + 35);
        Put32(image, at + 39, offset);
        new byte[] { 0xF3, 0x0F, 0x58, 0x05 }.CopyTo(image, at + 43);
        image[at + 51] = 0x0F; image[at + 52] = 0x2F; image[at + 53] = 0x87;
        Put32(image, at + 54, 0x46CF8);
    }

    private static void ScalarLoad(byte[] image, int at, int offset)
    {
        new byte[] { 0xF3, 0x0F, 0x10, 0x83 }.CopyTo(image, at);
        Put32(image, at + 4, offset);
    }

    private static void IndexSite(byte[] image, int at, int slot, int pit)
    {
        image[at] = 0x83; image[at + 1] = 0xBB;
        Put32(image, at + 2, slot); image[at + 6] = 0x68;
        image[at + 20] = 0x44; image[at + 21] = 0x8B; image[at + 22] = 0x83;
        Put32(image, at + 23, pit + 4);
        image[at + 27] = 0x8B; image[at + 28] = 0x93;
        Put32(image, at + 29, pit); image[at + 33] = 0xE8;
        Put32(image, at + 34, 0x1800 - (at + 38));
    }

    private static ContainerSpec IndexProfile(ulong slot, ulong pit, ulong owner = 0x6F90,
                                              ulong lateral = 0x468D0,
                                              ulong length = 0x444EC,
                                              ulong pitState = 0x8FD8) => new()
    {
        ArrayBase = new Rva(0, Confidence.Established),
        Pointer = new Rva(0, Confidence.Established),
        Table = new Rva(0, Confidence.Established),
        Stride = 0x47308,
        VehicleDelta = 8,
        Offsets = new Dictionary<string, FieldOffset>
        {
            ["slotIndex"] = new() { Offset = slot, Type = "int32", Base = OffsetBase.Container },
            ["pitIndex"] = new() { Offset = pit, Type = "int32", Base = OffsetBase.Container },
            ["garageIndex"] = new() { Offset = pit + 4, Type = "int32", Base = OffsetBase.Container },
            ["controlOwner"] = new() { Offset = owner, Type = "int32", Base = OffsetBase.Container },
            ["lateralSign"] = new() { Offset = lateral, Type = "float32", Base = OffsetBase.Container },
            ["vehicleLength"] = new() { Offset = length, Type = "float32", Base = OffsetBase.Container },
            ["vehicleWidth"] = new() { Offset = length + 4, Type = "float32", Base = OffsetBase.Container },
            ["pitState"] = new() { Offset = pitState, Type = "int32", Base = OffsetBase.Container },
            ["pitFlag"] = new() { Offset = pitState + 0x10, Type = "byte", Base = OffsetBase.Container },
        },
    };

    private static void NormalWriter(byte[] image, int at, int table, byte offset)
    {
        new byte[] { 0x83, 0xF8, 0x67, 0x48, 0xC1, 0xE1, 0x05, 0x48, 0x8B, 0x05 }
            .CopyTo(image, at);
        Put32(image, at + 10, table - (at + 14));
        if (offset == 0)
            new byte[] { 0xF3, 0x0F, 0x11, 0x04, 0x01, 0xF3, 0x0F, 0x11, 0x4C, 0x01, 0x04,
                         0xF3, 0x0F, 0x11, 0x54, 0x01, 0x08 }.CopyTo(image, at + 14);
        else
            new byte[] { 0xF3, 0x0F, 0x11, 0x44, 0x01, 0x0C, 0xF3, 0x0F, 0x11, 0x4C, 0x01, 0x10,
                         0xF3, 0x0F, 0x11, 0x54, 0x01, 0x14 }.CopyTo(image, at + 14);
    }

    private static void Put16(byte[] bytes, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at, 2), value);
    private static void Put32(byte[] bytes, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at, 4), value);
}
