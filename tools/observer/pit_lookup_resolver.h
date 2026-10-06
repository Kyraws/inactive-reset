#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>
#include <cstdint>
#include <cstring>

struct PitLookupSite { uintptr_t function, selector, table; };

inline bool resolve_pit_lookup(const uint8_t* image, size_t available, PitLookupSite& found) {
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
    const uint8_t head[] = {0x48,0x83,0xec,0x18,0x45,0x33,0xd2,0xb8,0x67,0x00,0x00,0x00,
                            0x85,0xd2,0x4d,0x8b,0xd8};
    auto target = [&](size_t instruction, size_t displacement, size_t length) {
        int32_t relative;
        memcpy(&relative, image + instruction + displacement, sizeof(relative));
        return static_cast<int64_t>(instruction + length) + relative;
    };
    auto writable = [&](int64_t rva, size_t bytes) {
        if (rva < 0) return false;
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
            const auto& s = sections[i];
            const size_t start = s.VirtualAddress, end = start + s.Misc.VirtualSize;
            if (end <= size && static_cast<uint64_t>(rva) >= start && static_cast<uint64_t>(rva) <= end &&
                bytes <= end - static_cast<size_t>(rva) && (s.Characteristics & IMAGE_SCN_MEM_WRITE) &&
                !(s.Characteristics & IMAGE_SCN_MEM_EXECUTE)) return true;
        }
        return false;
    };
    bool unique = false;
    for (unsigned s = 0; s < nt->FileHeader.NumberOfSections; ++s) {
        const auto& section = sections[s];
        if (!(section.Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        const size_t start = section.VirtualAddress, end = start + section.Misc.VirtualSize;
        if (end > size || end < start || end - start < 0x234) return false;
        for (size_t rva = start; rva + 0x234 <= end; ++rva) {
            const auto* p = image + rva;
            if (memcmp(p, head, sizeof(head)) ||
                memcmp(p + 0x1d, "\x83\x3d", 2) ||
                memcmp(p + 0x26, "\x48\x8b\x05", 3) ||
                memcmp(p + 0x73, "\x48\x8b\x05", 3) ||
                memcmp(p + 0x22d, "\x48\x8b\x05", 3)) continue;
            const int64_t selector = target(rva + 0x1d, 2, 7);
            const int64_t alternate = target(rva + 0x26, 3, 7);
            const int64_t table = target(rva + 0x73, 3, 7);
            if (table != target(rva + 0x22d, 3, 7) ||
                alternate - table != 0x18 || selector - table != 0x8c ||
                !writable(selector, 4) || !writable(alternate, 8) || !writable(table, 8)) continue;
            if (unique) return false;
            found = {rva, static_cast<uintptr_t>(selector), static_cast<uintptr_t>(table)};
            unique = true;
        }
    }
    return unique;
}
