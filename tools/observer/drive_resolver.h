#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>
#include <cstdint>
#include <cstring>

struct DriveSite { uintptr_t function, count, table; };

inline bool resolve_drive(const uint8_t* image, size_t available, DriveSite& found) {
    if (!image || available < sizeof(IMAGE_DOS_HEADER)) return false;
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(image);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
        static_cast<size_t>(dos->e_lfanew) + sizeof(IMAGE_NT_HEADERS64) > available) return false;
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(image + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC ||
        nt->OptionalHeader.SizeOfImage > available || !nt->FileHeader.NumberOfSections ||
        nt->FileHeader.NumberOfSections > 96) return false;
    const auto* sections = IMAGE_FIRST_SECTION(nt);
    if (reinterpret_cast<const uint8_t*>(sections + nt->FileHeader.NumberOfSections) > image + available) return false;
    const size_t size = nt->OptionalHeader.SizeOfImage;
    constexpr uint8_t prefix[] = {0x48,0x8b,0xc4,0x48,0x89,0x58,0x08,0x48,0x89,0x70,0x10,
                                  0x48,0x89,0x78,0x20,0x44,0x88,0x40,0x18,0x55,0x41,0x54,0x41,0x55};
    auto rip = [&](size_t at, size_t displacement, size_t length) {
        int32_t relative;
        memcpy(&relative, image + at + displacement, sizeof(relative));
        return static_cast<int64_t>(at + length) + relative;
    };
    auto writable = [&](int64_t rva, size_t bytes) {
        if (rva < 0) return false;
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
            const auto& s = sections[i];
            const size_t start = s.VirtualAddress, end = start + s.Misc.VirtualSize;
            if (end <= size && static_cast<uint64_t>(rva) >= start &&
                static_cast<uint64_t>(rva) <= end && bytes <= end - static_cast<size_t>(rva) &&
                (s.Characteristics & IMAGE_SCN_MEM_WRITE) && !(s.Characteristics & IMAGE_SCN_MEM_EXECUTE)) return true;
        }
        return false;
    };
    bool unique = false;
    for (unsigned s = 0; s < nt->FileHeader.NumberOfSections; ++s) {
        const auto& section = sections[s];
        if (!(section.Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        const size_t start = section.VirtualAddress, end = start + section.Misc.VirtualSize;
        if (end > size || end < start || end - start < 0x480) return false;
        for (size_t at = start; at + 0x480 <= end; ++at) {
            if (memcmp(image + at, prefix, sizeof(prefix)) ||
                memcmp(image + at + 0x340, "\x48\x8b\x0d", 3) ||
                memcmp(image + at + 0x3f6, "\x8b\x05", 2) ||
                memcmp(image + at + 0x417, "\x48\x8b\x0d", 3) ||
                image[at + 0x474] != 0xe8) continue;
            const int64_t table = rip(at + 0x340, 3, 7);
            const int64_t count = rip(at + 0x3f6, 2, 6);
            if (table != rip(at + 0x417, 3, 7) || count - table != 0x90 ||
                !writable(table, 8) || !writable(count, 4)) continue;
            const int64_t garageFunction = rip(at + 0x474, 1, 5);
            if (garageFunction < static_cast<int64_t>(start) || garageFunction >= static_cast<int64_t>(end)) continue;
            if (unique) return false;
            found = {at, static_cast<uintptr_t>(count), static_cast<uintptr_t>(table)};
            unique = true;
        }
    }
    return unique;
}
