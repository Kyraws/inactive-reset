#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>
#include <cstdint>
#include <cstring>

struct SpotSite {
    uintptr_t function;
    uintptr_t count;
    uintptr_t table;
};

inline bool resolve_spot(const uint8_t* image, size_t available, SpotSite& found) {
    constexpr uint8_t prefix[] = {0x40, 0x53, 0x56, 0x41, 0x56, 0x48,
                                  0x83, 0xEC, 0x60, 0x48, 0x8B, 0x1D};
    if (available < sizeof(IMAGE_DOS_HEADER)) return false;
    auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(image);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
        static_cast<size_t>(dos->e_lfanew) + sizeof(IMAGE_NT_HEADERS64) > available) return false;
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(image + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC ||
        nt->OptionalHeader.SizeOfImage > available || nt->FileHeader.NumberOfSections == 0 ||
        nt->FileHeader.NumberOfSections > 96) return false;
    const auto sections = IMAGE_FIRST_SECTION(nt);
    if (reinterpret_cast<const uint8_t*>(sections + nt->FileHeader.NumberOfSections) > image + available) return false;
    const size_t size = nt->OptionalHeader.SizeOfImage;

    auto writable = [&](uintptr_t rva, size_t bytes) {
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
            const auto& section = sections[i];
            const size_t start = section.VirtualAddress;
            const size_t end = start + section.Misc.VirtualSize;
            if (rva >= start && rva <= end && bytes <= end - rva &&
                end <= size && (section.Characteristics & IMAGE_SCN_MEM_WRITE) &&
                !(section.Characteristics & IMAGE_SCN_MEM_EXECUTE)) return true;
        }
        return false;
    };
    auto rip_target = [&](uintptr_t instruction, unsigned displacement_offset, unsigned length) {
        int32_t displacement;
        memcpy(&displacement, image + instruction + displacement_offset, sizeof(displacement));
        return static_cast<int64_t>(instruction + length) + displacement;
    };

    bool unique = false;
    for (unsigned s = 0; s < nt->FileHeader.NumberOfSections; ++s) {
        const auto& section = sections[s];
        if (!(section.Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        const size_t start = section.VirtualAddress;
        const size_t end = start + section.Misc.VirtualSize;
        if (end > size || end < start + 0x70) return false;
        for (size_t rva = start; rva + 0x70 <= end; ++rva) {
            const auto* p = image + rva;
            if (memcmp(p, prefix, sizeof(prefix)) != 0 ||
                p[0x1f] != 0x8b || p[0x20] != 0x0d ||
                p[0x40] != 0x48 || p[0x41] != 0x8b || p[0x42] != 0x05 ||
                p[0x69] != 0x48 || p[0x6a] != 0x8b || p[0x6b] != 0x05) continue;
            const int64_t count = rip_target(rva + 9, 3, 7);
            const int64_t multiplier = rip_target(rva + 0x1f, 2, 6);
            const int64_t table = rip_target(rva + 0x40, 3, 7);
            const int64_t table_again = rip_target(rva + 0x69, 3, 7);
            if (count < 4 || table < 0 || multiplier != count - 4 ||
                table_again != table || count <= table || count - table > 0x100 ||
                !writable(count, 4) || !writable(multiplier, 4) || !writable(table, 8)) continue;
            if (unique) return false;
            found = {rva, static_cast<uintptr_t>(count), static_cast<uintptr_t>(table)};
            unique = true;
        }
    }
    return unique;
}
