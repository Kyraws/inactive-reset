#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <MinHook.h>
#include "pit_lookup_resolver.h"
#include <cstdio>

namespace {
using Lookup = uintptr_t(__fastcall*)(void*, int, void*, void*);
Lookup original = nullptr;
volatile LONG calls = 0;
SRWLOCK log_lock = SRWLOCK_INIT;
wchar_t log_path[MAX_PATH] = {};

void note(const char* message) {
    AcquireSRWLockExclusive(&log_lock);
    FILE* file = nullptr;
    if (_wfopen_s(&file, log_path, L"a") == 0 && file) {
        fprintf(file, "%s\n", message);
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

uintptr_t __fastcall observe(void* rcx, int index, void* position, void* direction) {
    const uintptr_t result = original(rcx, index, position, direction);
    const LONG number = InterlockedIncrement(&calls);
    if (number <= 30) {
        float xyz[3]{}, heading[3]{};
        SIZE_T a = 0, b = 0;
        char line[240];
        if (ReadProcessMemory(GetCurrentProcess(), position, xyz, sizeof(xyz), &a) && a == sizeof(xyz) &&
            ReadProcessMemory(GetCurrentProcess(), direction, heading, sizeof(heading), &b) && b == sizeof(heading)) {
            sprintf_s(line, "call=%ld requested_index=%d position=(%.6g,%.6g,%.6g) direction=(%.6g,%.6g,%.6g)",
                      number, index, xyz[0], xyz[1], xyz[2], heading[0], heading[1], heading[2]);
        } else {
            sprintf_s(line, "call=%ld requested_index=%d output-unreadable", number, index);
        }
        note(line);
    }
    return result;
}

DWORD WINAPI initialize(void* module) {
    GetModuleFileNameW(static_cast<HMODULE>(module), log_path, MAX_PATH);
    wchar_t* slash = wcsrchr(log_path, L'\\');
    if (!slash) return 0;
    wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - log_path), L"lmu_pit_lookup_observer.log");
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
    PitLookupSite site{};
    if (!resolve_pit_lookup(reinterpret_cast<const uint8_t*>(base), nt->OptionalHeader.SizeOfImage, site)) {
        note("REFUSED: no unique validated pit lookup"); return 0;
    }
    if (MH_Initialize() != MH_OK) { note("REFUSED: MinHook initialization failed"); return 0; }
    void* target = reinterpret_cast<void*>(base + site.function);
    if (MH_CreateHook(target, reinterpret_cast<void*>(&observe), reinterpret_cast<void**>(&original)) != MH_OK) {
        note("REFUSED: hook creation failed"); MH_Uninitialize(); return 0;
    }
    if (MH_EnableHook(target) != MH_OK) {
        note("REFUSED: hook enable failed"); MH_RemoveHook(target); MH_Uninitialize(); return 0;
    }
    char line[150];
    sprintf_s(line, "ACTIVE: pass-through Drive spot helper function=%08llX selector=%08llX table=%08llX",
              static_cast<unsigned long long>(site.function), static_cast<unsigned long long>(site.selector),
              static_cast<unsigned long long>(site.table));
    note(line);
    return 0;
}
} // namespace

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
        HANDLE thread = CreateThread(nullptr, 0, initialize, module, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    return TRUE;
}
