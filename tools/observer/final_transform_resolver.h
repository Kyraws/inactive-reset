#pragma once

#include "drive_resolver.h"

// Resolve through the validated Drive caller's unconditional spot call.
inline bool resolve_final_transform(const uint8_t* image, size_t size, uintptr_t& target,
                                    uintptr_t& return_site) {
    DriveSite drive{};
    if (!resolve_drive(image, size, drive)) return false;
    const size_t call = drive.function + 0x474;
    if (call + 5 > size || image[call] != 0xe8) return false;
    int32_t displacement;
    memcpy(&displacement, image + call + 1, sizeof(displacement));
    const int64_t resolved = static_cast<int64_t>(call + 5) + displacement;
    constexpr uint8_t prologue[] = {0x40,0x53,0x56,0x41,0x56,0x48,0x83,0xec,0x60,
                                    0x48,0x8b,0x1d};
    if (resolved < 0 || static_cast<uint64_t>(resolved) + sizeof(prologue) > size ||
        memcmp(image + resolved, prologue, sizeof(prologue))) return false;
    target = static_cast<uintptr_t>(resolved);
    return_site = call + 5;
    return true;
}

// Resolve the ordinary-slot indexed helper from the same Drive function's
// mode-1 call. This is research instrumentation, not a placement write gate.
inline bool resolve_indexed_transform(const uint8_t* image, size_t size, uintptr_t& target,
                                      uintptr_t& return_site) {
    DriveSite drive{};
    if (!resolve_drive(image, size, drive)) return false;
    const size_t call = drive.function + 0x3e8;
    constexpr uint8_t mode1[] = {0xc7,0x44,0x24,0x20,1,0,0,0};
    if (call + 5 > size || image[call] != 0xe8 ||
        memcmp(image + call - 0x15, mode1, sizeof(mode1))) return false;
    int32_t displacement;
    memcpy(&displacement, image + call + 1, sizeof(displacement));
    const int64_t helper = static_cast<int64_t>(call + 5) + displacement;
    constexpr uint8_t prologue[] = {0x48,0x89,0x5c,0x24,0x08,0x48,0x89,0x74,
                                    0x24,0x10,0x57,0x48,0x83,0xec,0x20};
    constexpr uint8_t reader[] = {0x48,0x8d,0x0c,0x40,0x48,0x63,0x44,0x24,0x50,
                                  0x48,0x03,0xc8,0x48,0x8b,0x05};
    if (helper < 0 || static_cast<uint64_t>(helper) + 0xb6 + sizeof(reader) > size ||
        memcmp(image + helper, prologue, sizeof(prologue)) ||
        memcmp(image + helper + 0xb6, reader, sizeof(reader))) return false;
    target = static_cast<uintptr_t>(helper);
    return_site = call + 5;
    return true;
}
