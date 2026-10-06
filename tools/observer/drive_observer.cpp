#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <MinHook.h>
#include "drive_resolver.h"
#include <cstdio>

namespace {
using DriveFn = uintptr_t(__fastcall*)(void*, uintptr_t, uintptr_t, uintptr_t);
DriveFn original = nullptr;
volatile LONG calls = 0;
volatile LONG mode2_calls = 0;
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

uintptr_t __fastcall observe(void* object, uintptr_t mode, uintptr_t flag, uintptr_t fourth) {
    const uintptr_t result = original(object, mode, flag, fourth);
    const LONG number = InterlockedIncrement(&calls);
    const LONG mode2 = static_cast<uint8_t>(mode) == 2 ? InterlockedIncrement(&mode2_calls) : 0;
    if (number <= 20 || (mode2 > 0 && mode2 <= 20)) {
        char line[160];
        sprintf_s(line, "call=%ld mode=%u flag=%u object=%p result=0x%llX",
                  number, static_cast<unsigned>(static_cast<uint8_t>(mode)),
                  static_cast<unsigned>(static_cast<uint8_t>(flag)), object,
                  static_cast<unsigned long long>(result));
        note(line);
    }
    return result;
}

DWORD WINAPI initialize(void* module) {
    GetModuleFileNameW(static_cast<HMODULE>(module), log_path, MAX_PATH);
    wchar_t* slash = wcsrchr(log_path, L'\\');
    if (!slash) return 0;
    wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - log_path), L"lmu_drive_observer.log");
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
    DriveSite site{};
    if (!resolve_drive(reinterpret_cast<const uint8_t*>(base), nt->OptionalHeader.SizeOfImage, site)) {
        note("REFUSED: no unique validated Drive-path function"); return 0;
    }
    if (MH_Initialize() != MH_OK) { note("REFUSED: MinHook initialization failed"); return 0; }
    void* target = reinterpret_cast<void*>(base + site.function);
    if (MH_CreateHook(target, reinterpret_cast<void*>(&observe), reinterpret_cast<void**>(&original)) != MH_OK) {
        note("REFUSED: hook creation failed"); MH_Uninitialize(); return 0;
    }
    if (MH_EnableHook(target) != MH_OK) {
        note("REFUSED: hook enable failed"); MH_RemoveHook(target); MH_Uninitialize(); return 0;
    }
    char line[160];
    sprintf_s(line, "ACTIVE: pass-through Drive path function=%08llX count=%08llX table=%08llX",
              static_cast<unsigned long long>(site.function), static_cast<unsigned long long>(site.count),
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
