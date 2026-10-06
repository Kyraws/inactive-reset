#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <MinHook.h>
#include <intrin.h>
#include "pit_resolver.h"
#include <cstdint>
#include <cstdio>

namespace {
using MessageFn = uintptr_t(__fastcall*)(void*, const char*, uintptr_t, uintptr_t);
using DecisionFn = uintptr_t(__fastcall*)(void*, uint8_t);
MessageFn original = nullptr;
DecisionFn original_decision = nullptr;
uintptr_t message_address = 0;
uintptr_t flag_address = 0;
uintptr_t speed_call_return = 0;
volatile LONG calls = 0;
volatile LONG pit_events = 0;
volatile LONG speed_events = 0;
SRWLOCK log_lock = SRWLOCK_INIT;
wchar_t log_path[MAX_PATH] = {};

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

void note(const char* message) {
    AcquireSRWLockExclusive(&log_lock);
    FILE* file = nullptr;
    if (_wfopen_s(&file, log_path, L"a") == 0 && file) {
        fprintf(file, "%s\n", message);
        fclose(file);
    }
    ReleaseSRWLockExclusive(&log_lock);
}

uintptr_t __fastcall observe(void* rcx, const char* message, uintptr_t mode, uintptr_t code) {
    const bool speeding = reinterpret_cast<uintptr_t>(message) == message_address;
    const LONG before = speeding ? *reinterpret_cast<const volatile LONG*>(flag_address) : 0;
    const uintptr_t result = original(rcx, message, mode, code);
    const LONG number = InterlockedIncrement(&calls);
    if (speeding) {
        if (InterlockedIncrement(&pit_events) <= 20) {
            char line[180];
            sprintf_s(line, "PIT_SPEEDING_PENALTY_CALL call=%ld rcx=%p mode=%llu code=%llu flag_before=%ld flag_after=%ld",
                      number, rcx, static_cast<unsigned long long>(mode), static_cast<unsigned long long>(code), before,
                      *reinterpret_cast<const volatile LONG*>(flag_address));
            note(line);
        }
    } else if (number <= 3) {
        char line[130];
        sprintf_s(line, "other-message call=%ld rcx=%p message=%p mode=%llu code=%llu",
                  number, rcx, message, static_cast<unsigned long long>(mode), static_cast<unsigned long long>(code));
        note(line);
    }
    return result;
}

uintptr_t __fastcall observe_decision(void* rcx, uint8_t positive_excess) {
    const bool speed_gate = reinterpret_cast<uintptr_t>(_ReturnAddress()) == speed_call_return;
    if (speed_gate && InterlockedIncrement(&speed_events) <= 20) {
        char line[150];
        sprintf_s(line, "PIT_SPEED_GATE_CALL object=%p positive_excess=%u flag=%ld",
                  rcx, static_cast<unsigned>(positive_excess),
                  *reinterpret_cast<const volatile LONG*>(flag_address));
        note(line);
    }
    return original_decision(rcx, positive_excess);
}

DWORD WINAPI initialize(void* module) {
    GetModuleFileNameW(static_cast<HMODULE>(module), log_path, MAX_PATH);
    wchar_t* slash = wcsrchr(log_path, L'\\');
    if (!slash) return 0;
    wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - log_path), L"lmu_pit_observer.log");
    if (anticheat_present()) { note("REFUSED: protected launch detected"); return 0; }
    const auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    const auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 || dos->e_lfanew > 0x1000) {
        note("REFUSED: invalid image"); return 0;
    }
    const auto nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.SizeOfImage > 0x20000000) {
        note("REFUSED: invalid image size"); return 0;
    }
    PitSite site{};
    if (!resolve_pit(reinterpret_cast<const uint8_t*>(base), nt->OptionalHeader.SizeOfImage, site)) {
        note("REFUSED: no unique validated pit-speeding message path"); return 0;
    }
    message_address = base + site.message;
    flag_address = base + site.flag;
    speed_call_return = base + site.speed_call_return;
    auto target = reinterpret_cast<void*>(base + site.function);
    auto decision = reinterpret_cast<void*>(base + site.decision_function);
    if (MH_Initialize() != MH_OK) { note("REFUSED: MinHook initialization failed"); return 0; }
    if (MH_CreateHook(target, reinterpret_cast<void*>(&observe), reinterpret_cast<void**>(&original)) != MH_OK) {
        note("REFUSED: hook creation failed"); MH_Uninitialize(); return 0;
    }
    if (MH_CreateHook(decision, reinterpret_cast<void*>(&observe_decision),
                      reinterpret_cast<void**>(&original_decision)) != MH_OK) {
        note("REFUSED: decision hook creation failed"); MH_RemoveHook(target); MH_Uninitialize(); return 0;
    }
    if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK) {
        note("REFUSED: hook enable failed"); MH_DisableHook(MH_ALL_HOOKS);
        MH_RemoveHook(decision); MH_RemoveHook(target); MH_Uninitialize(); return 0;
    }
    char line[210];
    sprintf_s(line, "ACTIVE: pit-speed gate=%08llX return=%08llX penalty=%08llX message=%08llX flag=%08llX",
              static_cast<unsigned long long>(site.decision_function),
              static_cast<unsigned long long>(site.speed_call_return),
              static_cast<unsigned long long>(site.function), static_cast<unsigned long long>(site.message),
              static_cast<unsigned long long>(site.flag));
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
