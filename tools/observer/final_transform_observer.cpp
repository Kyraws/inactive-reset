#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <intrin.h>
#include <MinHook.h>
#include "final_transform_resolver.h"
#include <cstdio>

namespace {
using Transform = void(__fastcall*)(void*, int, int, float*, float*);
Transform original = nullptr;
uintptr_t expected_return = 0;
using Indexed = void(__fastcall*)(void*, void*, float*, float*, int);
Indexed original_indexed = nullptr;
uintptr_t expected_indexed_return = 0;
volatile LONG calls = 0;
SRWLOCK log_lock = SRWLOCK_INIT;
wchar_t log_path[MAX_PATH] = {};

void note(const char* line) {
    AcquireSRWLockExclusive(&log_lock);
    FILE* file = nullptr;
    if (_wfopen_s(&file, log_path, L"a") == 0 && file) {
        fprintf(file, "%s\n", line);
        fclose(file);
    }
    ReleaseSRWLockExclusive(&log_lock);
}

bool anticheat_present() {
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return true;
    PROCESSENTRY32W entry{sizeof(entry)};
    bool found = false;
    for (BOOL ok = Process32FirstW(snapshot, &entry); ok; ok = Process32NextW(snapshot, &entry)) {
        if (_wcsnicmp(entry.szExeFile, L"EasyAnti", 8) == 0 ||
            _wcsnicmp(entry.szExeFile, L"start_protected", 15) == 0) { found = true; break; }
    }
    CloseHandle(snapshot);
    if (found) return true;
    snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, GetCurrentProcessId());
    if (snapshot == INVALID_HANDLE_VALUE) return true;
    MODULEENTRY32W module{sizeof(module)};
    for (BOOL ok = Module32FirstW(snapshot, &module); ok; ok = Module32NextW(snapshot, &module)) {
        if (_wcsnicmp(module.szModule, L"EasyAnti", 8) == 0 ||
            _wcsnicmp(module.szModule, L"EAC", 3) == 0) { found = true; break; }
    }
    CloseHandle(snapshot);
    return found;
}

bool read_three(const void* source, float (&values)[3]) {
    SIZE_T read = 0;
    return ReadProcessMemory(GetCurrentProcess(), source, values, sizeof(values), &read) &&
           read == sizeof(values);
}

void __fastcall observe(void* context, int index, int selector, float* position, float* orientation) {
    const bool drive = reinterpret_cast<uintptr_t>(_ReturnAddress()) == expected_return;
    const LONG number = drive ? InterlockedIncrement(&calls) : 0;
    original(context, index, selector, position, orientation);
    if (!drive || number > 20) return;
    float output_pos[3]{}, output_ori[3]{};
    char line[280];
    if (read_three(position, output_pos) && read_three(orientation, output_ori)) {
        sprintf_s(line, "drive_spot=%ld index=%d selector=%d position=(%.6g,%.6g,%.6g) orientation=(%.6g,%.6g,%.6g)",
                  number, index, selector, output_pos[0], output_pos[1], output_pos[2],
                  output_ori[0], output_ori[1], output_ori[2]);
    } else {
        sprintf_s(line, "drive_spot=%ld index=%d selector=%d output-unreadable", number, index, selector);
    }
    note(line);
}

void __fastcall observe_indexed(void* context, void* container, float* position,
                                float* orientation, int mode) {
    const bool drive = reinterpret_cast<uintptr_t>(_ReturnAddress()) == expected_indexed_return;
    original_indexed(context, container, position, orientation, mode);
    if (!drive) return;
    float output_pos[3]{}, output_ori[3]{};
    char line[280];
    if (read_three(position, output_pos) && read_three(orientation, output_ori)) {
        sprintf_s(line, "drive_indexed mode=%d position=(%.6g,%.6g,%.6g) orientation=(%.6g,%.6g,%.6g)",
                  mode, output_pos[0], output_pos[1], output_pos[2],
                  output_ori[0], output_ori[1], output_ori[2]);
    } else {
        sprintf_s(line, "drive_indexed mode=%d output-unreadable", mode);
    }
    note(line);
}

DWORD WINAPI initialize(void* module) {
    GetModuleFileNameW(static_cast<HMODULE>(module), log_path, MAX_PATH);
    wchar_t* slash = wcsrchr(log_path, L'\\');
    if (!slash) return 0;
    wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - log_path), L"lmu_final_spot_observer.log");
    if (anticheat_present()) { note("REFUSED: protected launch detected"); return 0; }
    const auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 || dos->e_lfanew > 0x1000) {
        note("REFUSED: invalid image"); return 0;
    }
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.SizeOfImage > 0x20000000) {
        note("REFUSED: invalid image size"); return 0;
    }
    uintptr_t target_rva = 0, caller_rva = 0, indexed_rva = 0, indexed_caller = 0;
    if (!resolve_final_transform(reinterpret_cast<const uint8_t*>(base),
                                 nt->OptionalHeader.SizeOfImage, target_rva, caller_rva) ||
        !resolve_indexed_transform(reinterpret_cast<const uint8_t*>(base),
                                   nt->OptionalHeader.SizeOfImage, indexed_rva, indexed_caller)) {
        note("REFUSED: Drive spot/indexed calls not validated"); return 0;
    }
    if (MH_Initialize() != MH_OK) { note("REFUSED: MinHook initialization failed"); return 0; }
    void* target = reinterpret_cast<void*>(base + target_rva);
    void* indexed_target = reinterpret_cast<void*>(base + indexed_rva);
    if (MH_CreateHook(target, reinterpret_cast<void*>(&observe), reinterpret_cast<void**>(&original)) != MH_OK) {
        note("REFUSED: hook creation failed"); MH_Uninitialize(); return 0;
    }
    if (MH_CreateHook(indexed_target, reinterpret_cast<void*>(&observe_indexed),
                      reinterpret_cast<void**>(&original_indexed)) != MH_OK) {
        note("REFUSED: indexed hook creation failed"); MH_RemoveHook(target); MH_Uninitialize(); return 0;
    }
    expected_return = base + caller_rva;
    expected_indexed_return = base + indexed_caller;
    if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK) {
        note("REFUSED: hook enable failed");
        MH_DisableHook(MH_ALL_HOOKS);
        MH_RemoveHook(indexed_target); MH_RemoveHook(target); MH_Uninitialize(); return 0;
    }
    char line[180];
    sprintf_s(line, "ACTIVE: passive final spot=%08llX return=%08llX indexed=%08llX return=%08llX",
              static_cast<unsigned long long>(target_rva), static_cast<unsigned long long>(caller_rva),
              static_cast<unsigned long long>(indexed_rva), static_cast<unsigned long long>(indexed_caller));
    note(line);
    return 0;
}
}

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
        HANDLE thread = CreateThread(nullptr, 0, initialize, module, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    return TRUE;
}
