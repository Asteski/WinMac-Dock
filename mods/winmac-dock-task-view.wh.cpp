// ==WindhawkMod==
// @id              winmac-dock-task-view
// @name            WinMac Dock Task View Bridge
// @description     Lets WinMac Dock remain visible in Task View when enabled in dock settings
// @version         0.2.0
// @author          WinMac Dock
// @include         explorer.exe
// @architecture    x86-64
// @compilerOptions -lcomctl32 -ldwmapi
// ==/WindhawkMod==

// ==WindhawkModReadme==
/*
# WinMac Dock Task View Bridge

Companion for WinMac Dock's Show dock in Task View option. Windows normally
places Task View above desktop applications. When the option is enabled, this
bridge changes only the two explicitly marked WinMac.Dock.exe rendering windows
to the notification layer (4), allowing the dock to remain visible. When it is
disabled, the dock returns to its normal desktop layer (1). The bridge does not
alter Task View, the Windows taskbar, or any other application's windows.
Fullscreen hiding and input remain controlled by the dock.

Band requests are applied on Explorer's authorized shell thread. A message-only
window keeps later requests and dock restarts working automatically; shell band
callbacks also provide a fallback. No privileges, certificates or security settings
are changed. Disable this mod in Windhawk to stop the bridge; restarting the
dock recreates its windows in the normal desktop layer.
*/
// ==/WindhawkModReadme==

#include <windows.h>
#include <cwchar>
#include <atomic>
#include <dwmapi.h>
#include <windhawk_utils.h>

using SetWindowBand_t = BOOL(WINAPI*)(HWND, HWND, DWORD);
using GetWindowBand_t = BOOL(WINAPI*)(HWND, PDWORD);
using ShowWindow_t = BOOL(WINAPI*)(HWND, int);
using SetWindowPos_t = BOOL(WINAPI*)(HWND, HWND, int, int, int, int, UINT);
ShowWindow_t ShowWindow_Original;
SetWindowPos_t SetWindowPos_Original;
SetWindowBand_t SetWindowBand_Original;
GetWindowBand_t GetWindowBand_Original;
thread_local bool g_syncing;
std::atomic<HWND> g_dock, g_material;
std::atomic<HWND> g_broker;
std::atomic_flag g_creating = ATOMIC_FLAG_INIT;
std::atomic<bool> g_stopping;
std::atomic<HWND> g_taskView;
std::atomic<unsigned> g_taskViewOpening;
std::atomic<ULONGLONG> g_taskViewWarmUntil;
void SyncBands();
void SyncOnShellThread();
bool IsTaskViewSurface(HWND window) {
    WCHAR cls[80]; DWORD pid = 0;
    GetWindowThreadProcessId(window, &pid);
    return pid == GetCurrentProcessId() && GetClassNameW(window, cls, ARRAYSIZE(cls)) &&
        (!wcscmp(cls, L"XamlExplorerHostIslandWindow") || !wcscmp(cls, L"MultitaskingViewFrame"));
}
struct TaskViewShowScope {
    bool active;
    TaskViewShowScope(HWND window, bool showing) : active(showing && !g_syncing && IsTaskViewSurface(window)) {
        if (!active) return;
        g_taskView = window; ++g_taskViewOpening;
        g_taskViewWarmUntil = GetTickCount64() + 100;
        SyncOnShellThread();
    }
    ~TaskViewShowScope() {
        if (!active) return;
        --g_taskViewOpening;
        SyncOnShellThread();
    }
};
bool SuppressTaskbar(HWND window) {
    HWND dock = g_dock.load();
    if (g_stopping || !dock || !GetPropW(dock, L"WinMac.Dock.HideWindowsTaskbar")) return false;
    WCHAR cls[64];
    if (!GetClassNameW(window, cls, ARRAYSIZE(cls))) return false;
    return !wcscmp(cls, L"Shell_TrayWnd") || !wcscmp(cls, L"Shell_SecondaryTrayWnd");
}
BOOL WINAPI ShowWindow_Hook(HWND window, int command) {
    TaskViewShowScope taskView(window, command != SW_HIDE);
    return ShowWindow_Original(window, SuppressTaskbar(window) ? SW_HIDE : command);
}
BOOL WINAPI SetWindowPos_Hook(HWND window, HWND after, int x, int y, int width, int height, UINT flags) {
    TaskViewShowScope taskView(window, (flags & SWP_SHOWWINDOW) && !(flags & SWP_HIDEWINDOW));
    if (SuppressTaskbar(window)) flags = (flags & ~SWP_SHOWWINDOW) | SWP_HIDEWINDOW;
    return SetWindowPos_Original(window, after, x, y, width, height, flags);
}
constexpr WCHAR kBrokerClass[] = L"WinMac.Dock.TaskViewBroker.V1";
const UINT g_initialize = RegisterWindowMessageW(L"WinMac.Dock.TaskViewBridge.Initialize.V1");
void SyncBands();
void SyncOnShellThread() {
    auto broker = g_broker.load();
    if (broker && GetWindowThreadProcessId(broker, nullptr) != GetCurrentThreadId()) {
        DWORD_PTR ignored;
        // Run before Explorer presents Task View, without leaving a cross-thread
        // shell call unbounded if Explorer is already shutting down.
        SendMessageTimeoutW(broker, WM_TIMER, 1, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, 100, &ignored);
    } else SyncBands();
}

LRESULT CALLBACK BrokerProc(HWND window, UINT message, WPARAM wp, LPARAM lp) {
    if (message == WM_TIMER && wp == 1) { SyncBands(); return 0; }
    if (message == WM_DESTROY) { KillTimer(window, 1); return 0; }
    return DefWindowProcW(window, message, wp, lp);
}

void EnsureBroker() {
    if (g_stopping || g_broker.load() || g_creating.test_and_set()) return;
    WNDCLASSW wc{};
    wc.lpfnWndProc = BrokerProc; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = kBrokerClass;
    if (RegisterClassW(&wc) || GetLastError() == ERROR_CLASS_ALREADY_EXISTS) {
        HWND window = CreateWindowExW(0, kBrokerClass, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, wc.hInstance, nullptr);
        if (window) { g_broker = window; SetTimer(window, 1, 200, nullptr); }
    }
    g_creating.clear();
}

LRESULT CALLBACK InitializeOnShellThread(HWND window, UINT message, WPARAM wp, LPARAM lp, DWORD_PTR) {
    if (message == g_initialize) { SyncBands(); EnsureBroker(); return 0; }
    return DefSubclassProc(window, message, wp, lp);
}

void Wh_ModAfterInit() {
    HWND taskbar = FindWindowW(L"Shell_TrayWnd", nullptr);
    DWORD pid = 0; GetWindowThreadProcessId(taskbar, &pid);
    if (pid == GetCurrentProcessId() && WindhawkUtils::SetWindowSubclassFromAnyThread(taskbar, InitializeOnShellThread, 0)) {
        SendMessageW(taskbar, g_initialize, 0, 0);
        WindhawkUtils::RemoveWindowSubclassFromAnyThread(taskbar, InitializeOnShellThread);
    }
}

BOOL CALLBACK SyncDock(HWND window, LPARAM) {
    auto request = reinterpret_cast<ULONG_PTR>(GetPropW(window, L"WinMac.Dock.TaskViewBand"));
    if (request != 1 && request != 4 && request != 5) return TRUE;
    // ZBID values aren't a numeric stacking order. Notification (4) is above
    // Task View's app-chrome (5); sharing 5 still leaves the dock covered.
    if (request == 5) request = 4; // Older dock clients used 5 as their request.
    WCHAR className[80], title[100];
    if (!GetClassNameW(window, className, ARRAYSIZE(className)) ||
        wcscmp(className, L"WinUIDesktopWin32WindowClass")) return TRUE;
    GetWindowTextW(window, title, ARRAYSIZE(title));
    if (wcscmp(title, L"WinMac Dock") && wcscmp(title, L"WinMac Dock material surface")) return TRUE;
    DWORD pid;
    GetWindowThreadProcessId(window, &pid);
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!process) return TRUE;
    WCHAR path[32768]; DWORD length = ARRAYSIZE(path);
    BOOL queried = QueryFullProcessImageNameW(process, 0, path, &length);
    CloseHandle(process);
    if (!queried) return TRUE;
    const WCHAR* name = wcsrchr(path, L'\\');
    if (!name || _wcsicmp(name + 1, L"WinMac.Dock.exe")) return TRUE;
    if (!wcscmp(title, L"WinMac Dock")) g_dock = window; else g_material = window;
    return TRUE;
}

bool TaskViewVisibleOrOpening() {
    if (g_taskViewOpening.load()) return true;
    auto window = g_taskView.load();
    if (!window || !IsWindow(window) || !IsWindowVisible(window)) return false;
    DWORD cloaked = 0;
    return (SUCCEEDED(DwmGetWindowAttribute(window, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))) && !cloaked) ||
        GetTickCount64() < g_taskViewWarmUntil.load();
}

void ApplyBand(HWND window, DWORD request) {
    DWORD current;
    if (GetWindowBand_Original(window, &current) && current != request) {
        if (SetWindowBand_Original(window, nullptr, static_cast<DWORD>(request))) {
            RemovePropW(window, L"WinMac.Dock.TaskViewBridgeError");
            Wh_Log(L"Dock surface %p: band %lu -> %lu", window, current, static_cast<DWORD>(request));
        }
        else {
            DWORD error = GetLastError();
            SetPropW(window, L"WinMac.Dock.TaskViewBridgeError", reinterpret_cast<HANDLE>(static_cast<ULONG_PTR>(error)));
            Wh_Log(L"Dock surface %p: band request failed (%lu)", window, error);
        }
    }
    SetPropW(window, L"WinMac.Dock.TaskViewBridgeSeen", reinterpret_cast<HANDLE>(1));
    SetPropW(window, L"WinMac.Dock.TaskViewBridgeVersion", reinterpret_cast<HANDLE>(151));
}

void ApplyDockPair() {
    auto dock = g_dock.load(), material = g_material.load();
    if (!dock || !material || !IsWindow(dock) || !IsWindow(material)) return;
    DWORD request = static_cast<DWORD>(reinterpret_cast<ULONG_PTR>(GetPropW(dock, L"WinMac.Dock.TaskViewBand")));
    if (request == 5) request = 4;
    if (request != 1 && request != 4) return;
    // Only the dock's explicit Task View state may request elevation. Explorer
    // reuses shell island classes for overlays; seeing one being shown is not
    // sufficient evidence that Task View is opening (e.g. media-key volume UI).
    // Keep the early synchronization hooks, but never override desktop band 1.
    bool preempt = request == 4 && GetPropW(dock, L"WinMac.Dock.TaskViewEnabled") && TaskViewVisibleOrOpening();
    DWORD dockBand = 0, materialBand = 0;
    GetWindowBand_Original(dock, &dockBand); GetWindowBand_Original(material, &materialBand);
    bool changed = dockBand != request || materialBand != request;
    // Backing surface first, icons last. Restore pair order in the same shell
    // dispatch turn, before DWM can present a material-only frame.
    ApplyBand(material, request);
    ApplyBand(dock, request);
    static HWND orderedDock, orderedMaterial;
    if (changed || orderedDock != dock || orderedMaterial != material) {
        SetWindowPos_Original(material, dock, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        orderedDock = dock; orderedMaterial = material;
        if (preempt && changed) {
            auto count = reinterpret_cast<ULONG_PTR>(GetPropW(dock, L"WinMac.Dock.TaskViewPreemptions"));
            SetPropW(dock, L"WinMac.Dock.TaskViewPreemptions", reinterpret_cast<HANDLE>(count + 1));
        }
    }
}

void SyncBands() {
    if (g_stopping || g_syncing) return;
    g_syncing = true;
    EnumWindows(SyncDock, 0);
    if (auto dock = FindWindowW(L"WinUIDesktopWin32WindowClass", L"WinMac Dock")) SyncDock(dock, 0);
    if (auto material = FindWindowW(L"WinUIDesktopWin32WindowClass", L"WinMac Dock material surface")) SyncDock(material, 0);
    if (auto dock = g_dock.load(); dock && IsWindow(dock)) SyncDock(dock, 0);
    if (auto material = g_material.load(); material && IsWindow(material)) SyncDock(material, 0);
    ApplyDockPair();
    g_syncing = false;
}

BOOL WINAPI SetWindowBand_Hook(HWND window, HWND after, DWORD band) {
    TaskViewShowScope taskView(window, band == 5 && IsWindowVisible(window));
    BOOL result = SetWindowBand_Original(window, after, band);
    DWORD error = GetLastError();
    if (!g_syncing) {
        g_syncing = true;
        EnumWindows(SyncDock, 0);
        if (auto dock = FindWindowW(L"WinUIDesktopWin32WindowClass", L"WinMac Dock")) SyncDock(dock, 0);
        if (auto material = FindWindowW(L"WinUIDesktopWin32WindowClass", L"WinMac Dock material surface")) SyncDock(material, 0);
        // Raised shell-band windows can disappear from desktop-only enumeration.
        // Retain only these validated handles, checking their markers each time.
        if (auto dock = g_dock.load(); dock && IsWindow(dock)) SyncDock(dock, 0);
        if (auto material = g_material.load(); material && IsWindow(material)) SyncDock(material, 0);
        ApplyDockPair();
        // Stay on the shell thread that owns layer access. The timer belongs
        // to a message-only window, so teardown cannot leave a timer callback
        // pointing into an unloaded module.
        EnsureBroker();
        g_syncing = false;
    }
    SetLastError(error);
    return result;
}

void Wh_ModBeforeUninit() {
    g_stopping = true;
    if (auto broker = g_broker.exchange(nullptr); broker && IsWindow(broker)) SendMessageW(broker, WM_CLOSE, 0, 0);
}
void Wh_ModUninit() {
    UnregisterClassW(kBrokerClass, GetModuleHandleW(nullptr));
}

BOOL Wh_ModInit() {
    auto user32 = GetModuleHandleW(L"user32.dll");
    GetWindowBand_Original = reinterpret_cast<GetWindowBand_t>(GetProcAddress(user32, "GetWindowBand"));
    auto setBand = GetProcAddress(user32, "SetWindowBand");
    return GetWindowBand_Original && setBand &&
        Wh_SetFunctionHook(reinterpret_cast<void*>(setBand), reinterpret_cast<void*>(SetWindowBand_Hook),
                           reinterpret_cast<void**>(&SetWindowBand_Original)) &&
        Wh_SetFunctionHook(reinterpret_cast<void*>(GetProcAddress(user32, "ShowWindow")), reinterpret_cast<void*>(ShowWindow_Hook),
                           reinterpret_cast<void**>(&ShowWindow_Original)) &&
        Wh_SetFunctionHook(reinterpret_cast<void*>(GetProcAddress(user32, "SetWindowPos")), reinterpret_cast<void*>(SetWindowPos_Hook),
                           reinterpret_cast<void**>(&SetWindowPos_Original));
}
