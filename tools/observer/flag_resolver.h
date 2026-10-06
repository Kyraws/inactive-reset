#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>
#include <cstdint>
#include <cstring>

struct FlagSite {
    uintptr_t function;
    uintptr_t reader;
    uintptr_t value;
};

inline bool resolve_flag(const uint8_t* image, size_t available, FlagSite& found) {
    constexpr uint8_t entry[] = {0x48, 0x89, 0x4c, 0x24, 0x08, 0x55, 0x53, 0x56,
                                 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41,
                                 0x57, 0x48, 0x81, 0xec, 0xd8, 0x01, 0x00, 0x00};
    if (available < sizeof(IMAGE_DOS_HEADER)) return false;
    auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(image);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 ||
        static_cast<size_t>(dos->e_lfanew) + sizeof(IMAGE_NT_HEADERS64) > available) return false;
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(image + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC ||
        nt->OptionalHeader.SizeOfImage > available || nt->FileHeader.NumberOfSections > 96) return false;
    const size_t size = nt->OptionalHeader.SizeOfImage;
    const auto sections = IMAGE_FIRST_SECTION(nt);
    if (reinterpret_cast<const uint8_t*>(sections + nt->FileHeader.NumberOfSections) > image + size) return false;
    const auto& directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    if (!directory.VirtualAddress || directory.VirtualAddress >= size ||
        directory.Size > size - directory.VirtualAddress || directory.Size % sizeof(RUNTIME_FUNCTION)) return false;
    const auto functions = reinterpret_cast<const RUNTIME_FUNCTION*>(image + directory.VirtualAddress);
    const size_t function_count = directory.Size / sizeof(RUNTIME_FUNCTION);

    auto has_section = [&](uintptr_t rva, size_t bytes, DWORD flags) {
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
            const auto& section = sections[i];
            const size_t start = section.VirtualAddress;
            const size_t end = start + section.Misc.VirtualSize;
            if (end <= size && rva >= start && rva <= end && bytes <= end - rva &&
                (section.Characteristics & flags) == flags) return true;
        }
        return false;
    };
    auto containing = [&](uintptr_t rva) -> const RUNTIME_FUNCTION* {
        size_t low = 0, high = function_count;
        while (low < high) {
            const size_t mid = low + (high - low) / 2;
            if (rva < functions[mid].BeginAddress) high = mid;
            else if (rva >= functions[mid].EndAddress) low = mid + 1;
            else return &functions[mid];
        }
        return nullptr;
    };

    bool unique = false;
    for (unsigned s = 0; s < nt->FileHeader.NumberOfSections; ++s) {
        const auto& section = sections[s];
        if (!(section.Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        const size_t start = section.VirtualAddress;
        const size_t end = start + section.Misc.VirtualSize;
        if (end > size || end < start + 10) return false;
        for (size_t rva = start; rva + 10 <= end; ++rva) {
            const auto* p = image + rva;
            if (p[0] != 0x83 || p[1] != 0x3d || p[6] != 0x02 ||
                p[7] != 0x7c || p[8] != 0x14 || p[9] != 0xe8) continue;
            int32_t displacement;
            memcpy(&displacement, p + 2, sizeof(displacement));
            const int64_t value = static_cast<int64_t>(rva + 7) + displacement;
            if (value < 0 || !has_section(value, 4, IMAGE_SCN_MEM_WRITE)) continue;
            const auto* function = containing(rva);
            if (!function || function->BeginAddress >= size ||
                !has_section(function->BeginAddress, sizeof(entry), IMAGE_SCN_MEM_EXECUTE) ||
                memcmp(image + function->BeginAddress, entry, sizeof(entry)) != 0) continue;
            if (unique) return false;
            found = {function->BeginAddress, rva, static_cast<uintptr_t>(value)};
            unique = true;
        }
    }
    return unique;
}
