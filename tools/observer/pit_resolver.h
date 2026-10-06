#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>
#include <cstdint>
#include <cstring>

struct PitSite {
    uintptr_t function;
    uintptr_t message;
    uintptr_t flag;
    uintptr_t decision_function;
    uintptr_t speed_call_return;
};

inline bool resolve_pit(const uint8_t* image, size_t available, PitSite& found) {
    constexpr char message[] = "Speeding In Pitlane";
    constexpr uint8_t entry[] = {0x48, 0x89, 0x5c, 0x24, 0x08, 0x55, 0x56, 0x57,
                                 0x41, 0x56, 0x41, 0x57, 0x48, 0x8d, 0xac, 0x24};
    constexpr uint8_t penalty_entry[] = {0x48, 0x89, 0x5c, 0x24, 0x10, 0x48, 0x89, 0x6c,
                                         0x24, 0x18, 0x56, 0x57, 0x41, 0x54, 0x41, 0x56,
                                         0x41, 0x57};
    constexpr uint8_t penalty_entry_old[] = {0x48, 0x89, 0x5c, 0x24, 0x10, 0x48, 0x89, 0x6c,
                                             0x24, 0x18, 0x48, 0x89, 0x74, 0x24, 0x20, 0x57,
                                             0x41, 0x56, 0x41, 0x57};
    constexpr uint8_t decision_entry[] = {0x48, 0x89, 0x5c, 0x24, 0x18, 0x55, 0x56, 0x57,
                                          0x48, 0x83, 0xec, 0x50};
    constexpr uint8_t speed_gate[] = {0xf3, 0x0f, 0x10, 0x8b, 0x18, 0x72, 0x02, 0x00,
                                      0x0f, 0x2f, 0xca, 0x76, 0x33, 0x83, 0x3d};
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

    auto section_has = [&](uintptr_t rva, size_t bytes, DWORD flags, DWORD forbidden) {
        for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
            const auto& section = sections[i];
            const size_t start = section.VirtualAddress;
            const size_t end = start + section.Misc.VirtualSize;
            if (end <= size && rva >= start && rva <= end && bytes <= end - rva &&
                (section.Characteristics & flags) == flags && !(section.Characteristics & forbidden)) return true;
        }
        return false;
    };
    auto function_at = [&](uintptr_t rva) {
        size_t low = 0, high = function_count;
        while (low < high) {
            const size_t mid = low + (high - low) / 2;
            if (rva < functions[mid].BeginAddress) high = mid;
            else if (rva >= functions[mid].EndAddress) low = mid + 1;
            else return functions[mid].BeginAddress;
        }
        return static_cast<DWORD>(0);
    };

    uintptr_t literal = 0;
    for (unsigned s = 0; s < nt->FileHeader.NumberOfSections; ++s) {
        const auto& section = sections[s];
        if (!(section.Characteristics & IMAGE_SCN_MEM_READ) ||
            (section.Characteristics & (IMAGE_SCN_MEM_WRITE | IMAGE_SCN_MEM_EXECUTE))) continue;
        const size_t start = section.VirtualAddress;
        const size_t end = start + section.Misc.VirtualSize;
        if (end > size || end < start + sizeof(message)) return false;
        for (size_t rva = start; rva + sizeof(message) <= end; ++rva) {
            if (memcmp(image + rva, message, sizeof(message)) == 0) {
                if (literal) return false;
                literal = rva;
            }
        }
    }
    if (!literal) return false;

    bool unique = false;
    for (unsigned s = 0; s < nt->FileHeader.NumberOfSections; ++s) {
        const auto& section = sections[s];
        if (!(section.Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        const size_t start = section.VirtualAddress;
        const size_t end = start + section.Misc.VirtualSize;
        if (end > size || end < start + 0x1e) return false;
        for (size_t rva = start; rva + 0x1e <= end; ++rva) {
            const auto* p = image + rva;
            if (p[0] != 0x48 || p[1] != 0x8d || p[2] != 0x15 || p[0x19] != 0xe9) continue;
            int32_t displacement, jump;
            memcpy(&displacement, p + 3, sizeof(displacement));
            memcpy(&jump, p + 0x1a, sizeof(jump));
            if (static_cast<int64_t>(rva + 7) + displacement != static_cast<int64_t>(literal)) continue;
            const int64_t function = static_cast<int64_t>(rva + 0x1e) + jump;
            if (function < 0 || !section_has(function, 0x9c, IMAGE_SCN_MEM_EXECUTE, 0) ||
                function_at(function) != function ||
                memcmp(image + function, entry, sizeof(entry)) != 0) continue;
            const auto* reader = image + function + 0x86;
            if (reader[0] != 0x8b || reader[1] != 0x05 || reader[6] != 0x85 || reader[7] != 0xc0 ||
                reader[8] != 0x0f || reader[9] != 0x84 || reader[14] != 0x83 ||
                reader[15] != 0xf8 || reader[16] != 0x03) continue;
            int32_t flag_displacement;
            memcpy(&flag_displacement, reader + 2, sizeof(flag_displacement));
            const int64_t flag = function + 0x86 + 6 + flag_displacement;
            if (flag < 0 || !section_has(flag, 4, IMAGE_SCN_MEM_WRITE, IMAGE_SCN_MEM_EXECUTE)) continue;
            // The same literal's stop/go branch calls a different function.
            uintptr_t penalty_function = 0;
            for (size_t next = rva + 0x1e; next + 0x12 <= end && next < rva + 0x100; ++next) {
                const auto* q = image + next;
                if (q[0] != 0x48 || q[1] != 0x8d || q[2] != 0x15 || q[0x0d] != 0xe8 ||
                    function_at(next) != function_at(rva)) continue;
                int32_t next_displacement, call;
                memcpy(&next_displacement, q + 3, sizeof(next_displacement));
                memcpy(&call, q + 0x0e, sizeof(call));
                if (static_cast<int64_t>(next + 7) + next_displacement != static_cast<int64_t>(literal)) continue;
                const int64_t target = static_cast<int64_t>(next + 0x12) + call;
                if (target < 0 || !section_has(target, sizeof(penalty_entry_old), IMAGE_SCN_MEM_EXECUTE, 0) ||
                    function_at(target) != target ||
                    (memcmp(image + target, penalty_entry, sizeof(penalty_entry)) != 0 &&
                     memcmp(image + target, penalty_entry_old, sizeof(penalty_entry_old)) != 0)) continue;
                if (penalty_function) return false;
                penalty_function = static_cast<uintptr_t>(target);
            }
            if (!penalty_function) continue;
            uintptr_t decision_function = 0;
            for (size_t candidate = rva > 0x500 ? rva - 0x500 : 0; candidate < rva; ++candidate) {
                if (section_has(candidate, sizeof(decision_entry), IMAGE_SCN_MEM_EXECUTE, 0) &&
                    function_at(candidate) == candidate &&
                    memcmp(image + candidate, decision_entry, sizeof(decision_entry)) == 0) {
                    if (decision_function) return false;
                    decision_function = candidate;
                }
            }
            if (!decision_function) continue;
            uintptr_t speed_call_return = 0;
            for (unsigned t = 0; t < nt->FileHeader.NumberOfSections; ++t) {
                const auto& code = sections[t];
                if (!(code.Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
                const size_t begin = code.VirtualAddress, finish = begin + code.Misc.VirtualSize;
                if (finish > size) return false;
                for (size_t gate = begin; gate + 0x40 <= finish; ++gate) {
                    const auto* q = image + gate;
                    if (memcmp(q, speed_gate, sizeof(speed_gate)) != 0 ||
                        q[0x13] != 0 || q[0x14] != 0x74 || q[0x3b] != 0xe8) continue;
                    int32_t flag_disp, call_disp;
                    memcpy(&flag_disp, q + 0x0f, sizeof(flag_disp));
                    memcpy(&call_disp, q + 0x3c, sizeof(call_disp));
                    if (static_cast<int64_t>(gate + 0x14) + flag_disp != flag ||
                        static_cast<int64_t>(gate + 0x40) + call_disp != decision_function ||
                        !function_at(gate)) continue;
                    if (speed_call_return) return false;
                    speed_call_return = gate + 0x40;
                }
            }
            if (!speed_call_return) continue;
            if (unique) return false;
            found = {penalty_function, literal, static_cast<uintptr_t>(flag), decision_function, speed_call_return};
            unique = true;
        }
    }
    return unique;
}
