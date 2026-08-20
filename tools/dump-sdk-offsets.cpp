// Emit the byte offsets of LMU's shared-memory layout as JSON.
//
// WHY THIS EXISTS. The C# tool reads LMU's shared memory directly, which means
// it needs exact offsets into a large proprietary struct. Hand-transcribing
// those from the SDK header is possible and quietly wrong the moment anything
// shifts -- and the failure mode is not a crash, it is plausible garbage.
//
// So we ask the compiler instead. This is the only C++ left in the project,
// it is a build-time tool rather than part of the product, and its output goes
// into an offsets JSON keyed by the SDK header's hash. Re-run it when LMU ships
// a new SharedMemoryInterface.hpp; nothing else needs to change.
//
// Build (adjust the SDK path if LMU is installed elsewhere):
//   cl /std:c++17 /EHsc /nologo /I"<LMU>\Support\SharedMemoryInterface"
//      dump-sdk-offsets.cpp /Fe:dump-sdk-offsets.exe

// The SDK header relies on several standard headers without including them
// itself (uint8_t, std::optional, std::exchange, std::swap). These must come
// first. Order matters here.
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <optional>
#include <utility>

#include "SharedMemoryInterface.hpp"

#define OFF(type, member) static_cast<size_t>(offsetof(type, member))

int main() {
    std::printf("{\n");
    std::printf("  \"$comment\": [\n");
    std::printf("    \"Byte offsets into LMU's shared memory, emitted by the compiler from\",\n");
    std::printf("    \"SharedMemoryInterface.hpp. Do not hand-edit: re-run tools/dump-sdk-offsets.\"\n");
    std::printf("  ],\n");

    std::printf("  \"mapName\": \"%s\",\n", LMU_SHARED_MEMORY_FILE);
    std::printf("  \"eventName\": \"%s\",\n", LMU_SHARED_MEMORY_EVENT);
    std::printf("  \"layoutSize\": %zu,\n", sizeof(SharedMemoryLayout));

    // data -> telemetry
    const size_t data = OFF(SharedMemoryLayout, data);
    const size_t telemetry = data + OFF(SharedMemoryObjectOut, telemetry);
    std::printf("  \"telemetry\": {\n");
    std::printf("    \"base\": %zu,\n", telemetry);
    std::printf("    \"activeVehicles\": %zu,\n",
                telemetry + OFF(SharedMemoryTelemetryData, activeVehicles));
    std::printf("    \"playerVehicleIdx\": %zu,\n",
                telemetry + OFF(SharedMemoryTelemetryData, playerVehicleIdx));
    std::printf("    \"playerHasVehicle\": %zu,\n",
                telemetry + OFF(SharedMemoryTelemetryData, playerHasVehicle));
    std::printf("    \"telemInfo\": %zu,\n",
                telemetry + OFF(SharedMemoryTelemetryData, telemInfo));
    std::printf("    \"telemInfoStride\": %zu\n", sizeof(TelemInfoV01));
    std::printf("  },\n");

    // Fields within one TelemInfoV01, relative to that vehicle's entry.
    std::printf("  \"telemInfo\": {\n");
    std::printf("    \"mID\": %zu,\n", OFF(TelemInfoV01, mID));
    std::printf("    \"mElapsedTime\": %zu,\n", OFF(TelemInfoV01, mElapsedTime));
    std::printf("    \"mLapNumber\": %zu,\n", OFF(TelemInfoV01, mLapNumber));
    std::printf("    \"mPos\": %zu,\n", OFF(TelemInfoV01, mPos));
    std::printf("    \"mLocalVel\": %zu,\n", OFF(TelemInfoV01, mLocalVel));
    std::printf("    \"mOri\": %zu,\n", OFF(TelemInfoV01, mOri));
    std::printf("    \"mGear\": %zu,\n", OFF(TelemInfoV01, mGear));
    std::printf("    \"vect3Stride\": %zu,\n", sizeof(TelemVect3));
    std::printf("    \"vect3ComponentSize\": %zu\n", sizeof(((TelemVect3*)nullptr)->x));
    std::printf("  },\n");

    // data -> scoring
    const size_t scoring = data + OFF(SharedMemoryObjectOut, scoring);
    std::printf("  \"scoring\": {\n");
    std::printf("    \"base\": %zu,\n", scoring);
    std::printf("    \"scoringInfo\": %zu,\n",
                scoring + OFF(SharedMemoryScoringData, scoringInfo));
    std::printf("    \"vehScoringInfo\": %zu,\n",
                scoring + OFF(SharedMemoryScoringData, vehScoringInfo));
    std::printf("    \"vehScoringInfoStride\": %zu\n", sizeof(VehicleScoringInfoV01));
    std::printf("  },\n");

    std::printf("  \"scoringInfo\": {\n");
    std::printf("    \"mTrackName\": %zu,\n", OFF(ScoringInfoV01, mTrackName));
    std::printf("    \"mTrackNameSize\": %zu,\n", sizeof(((ScoringInfoV01*)nullptr)->mTrackName));
    std::printf("    \"mNumVehicles\": %zu,\n", OFF(ScoringInfoV01, mNumVehicles));
    std::printf("    \"mSession\": %zu,\n", OFF(ScoringInfoV01, mSession));
    std::printf("    \"mLapDist\": %zu\n", OFF(ScoringInfoV01, mLapDist));
    std::printf("  },\n");

    std::printf("  \"vehScoringInfo\": {\n");
    std::printf("    \"mID\": %zu,\n", OFF(VehicleScoringInfoV01, mID));
    std::printf("    \"mVehicleName\": %zu,\n", OFF(VehicleScoringInfoV01, mVehicleName));
    std::printf("    \"mVehicleNameSize\": %zu,\n",
                sizeof(((VehicleScoringInfoV01*)nullptr)->mVehicleName));
    std::printf("    \"mIsPlayer\": %zu,\n", OFF(VehicleScoringInfoV01, mIsPlayer));
    std::printf("    \"mLapDist\": %zu,\n", OFF(VehicleScoringInfoV01, mLapDist));
    std::printf("    \"mTotalLaps\": %zu\n", OFF(VehicleScoringInfoV01, mTotalLaps));
    std::printf("  },\n");

    std::printf("  \"generic\": {\n");
    const size_t generic = data + OFF(SharedMemoryObjectOut, generic);
    std::printf("    \"base\": %zu,\n", generic);
    std::printf("    \"gameVersion\": %zu\n", generic + OFF(SharedMemoryGeneric, gameVersion));
    std::printf("  }\n");
    std::printf("}\n");
    return 0;
}
