#include "keys.h"
#include <map>
#include <set>
#include <deque>
#include <thread>
#include <condition_variable>
#include <algorithm>

namespace Keys {

struct K { bool mouse = false; WORD vk = 0; bool ext = false; DWORD mDown = 0, mUp = 0, mData = 0; int wheel = 0; };

static std::string Lower(std::string s) { std::transform(s.begin(), s.end(), s.begin(), [](unsigned char c) { return (char)tolower(c); }); return s; }
static std::string Trim(const std::string& s) {
    size_t a = s.find_first_not_of(" \t"), b = s.find_last_not_of(" \t");
    return a == std::string::npos ? "" : s.substr(a, b - a + 1);
}

static const std::map<std::string, K>& Table() {
    static std::map<std::string, K> t = [] {
        std::map<std::string, K> m;
        auto k = [&](const char* n, WORD vk, bool ext = false) { K x; x.vk = vk; x.ext = ext; m[n] = x; };
        for (int i = 1; i <= 24; i++) k(("f" + std::to_string(i)).c_str(), (WORD)(VK_F1 + i - 1));
        for (char c = 'a'; c <= 'z'; c++) k(std::string(1, c).c_str(), (WORD)toupper(c));
        for (char c = '0'; c <= '9'; c++) { k(std::string(1, c).c_str(), (WORD)c); k(("num" + std::string(1, c)).c_str(), (WORD)(VK_NUMPAD0 + c - '0')); }
        k("space", VK_SPACE); k("enter", VK_RETURN); k("return", VK_RETURN); k("esc", VK_ESCAPE); k("escape", VK_ESCAPE);
        k("tab", VK_TAB); k("backspace", VK_BACK); k("delete", VK_DELETE, true); k("del", VK_DELETE, true);
        k("insert", VK_INSERT, true); k("ins", VK_INSERT, true); k("home", VK_HOME, true); k("end", VK_END, true);
        k("pageup", VK_PRIOR, true); k("pgup", VK_PRIOR, true); k("pagedown", VK_NEXT, true); k("pgdn", VK_NEXT, true);
        k("up", VK_UP, true); k("down", VK_DOWN, true); k("left", VK_LEFT, true); k("right", VK_RIGHT, true);
        k("ctrl", VK_LCONTROL); k("control", VK_LCONTROL); k("lctrl", VK_LCONTROL); k("rctrl", VK_RCONTROL, true);
        k("shift", VK_LSHIFT); k("lshift", VK_LSHIFT); k("rshift", VK_RSHIFT);
        k("alt", VK_LMENU); k("lalt", VK_LMENU); k("ralt", VK_RMENU, true);
        k("win", VK_LWIN, true); k("capslock", VK_CAPITAL); k("scrolllock", VK_SCROLL); k("pause", VK_PAUSE);
        k("printscreen", VK_SNAPSHOT, true); k("numlock", VK_NUMLOCK, true);
        k("numadd", VK_ADD); k("numsub", VK_SUBTRACT); k("nummul", VK_MULTIPLY); k("numdiv", VK_DIVIDE, true);
        k("numdec", VK_DECIMAL); k("numenter", VK_RETURN, true);
        k("minus", VK_OEM_MINUS); k("equals", VK_OEM_PLUS); k("comma", VK_OEM_COMMA); k("period", VK_OEM_PERIOD);
        k("slash", VK_OEM_2); k("grave", VK_OEM_3); k("tilde", VK_OEM_3); k("semicolon", VK_OEM_1);
        k("quote", VK_OEM_7); k("lbracket", VK_OEM_4); k("rbracket", VK_OEM_6); k("backslash", VK_OEM_5);
        auto mb = [&](const char* n, DWORD d, DWORD u, DWORD data = 0) { K x; x.mouse = true; x.mDown = d; x.mUp = u; x.mData = data; m[n] = x; };
        mb("lmb", MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP); mb("rmb", MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
        mb("mmb", MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
        mb("mouse4", MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON1); mb("mouse5", MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON2);
        K wu; wu.mouse = true; wu.wheel = WHEEL_DELTA; m["wheelup"] = wu;
        K wd; wd.mouse = true; wd.wheel = -WHEEL_DELTA; m["wheeldown"] = wd;
        return m;
    }();
    return t;
}

static bool Parse(const std::string& combo, std::vector<K>& out, std::string& err) {
    out.clear();
    std::string cur;
    std::vector<std::string> parts;
    for (char c : combo) { if (c == '+') { parts.push_back(Trim(cur)); cur.clear(); } else cur += c; }
    parts.push_back(Trim(cur));
    for (auto& p : parts) {
        auto it = Table().find(Lower(p));
        if (it == Table().end()) { err = "unknown key '" + p + "' in '" + combo + "'"; return false; }
        out.push_back(it->second);
    }
    if (out.empty()) { err = "empty key combo"; return false; }
    return true;
}

static bool gFocus = false;
static std::set<std::string> gHeld; // combos currently held via Down/toggle

static void FocusGame() {
    if (!gFocus) return;
    DWORD pid = GetCurrentProcessId();
    HWND fg = GetForegroundWindow();
    DWORD fpid = 0; GetWindowThreadProcessId(fg, &fpid);
    if (fpid == pid) return;
    struct C { DWORD pid; HWND best; } c{ pid, nullptr };
    EnumWindows([](HWND h, LPARAM lp) -> BOOL {
        auto* c = (C*)lp; DWORD p = 0; GetWindowThreadProcessId(h, &p);
        if (p == c->pid && IsWindowVisible(h) && !GetWindow(h, GW_OWNER)) { c->best = h; return FALSE; }
        return TRUE;
    }, (LPARAM)&c);
    if (c.best) SetForegroundWindow(c.best);
}

static void Send(const K& k, bool down) {
    INPUT in{};
    if (k.mouse) {
        in.type = INPUT_MOUSE;
        if (k.wheel) { if (!down) return; in.mi.dwFlags = MOUSEEVENTF_WHEEL; in.mi.mouseData = (DWORD)k.wheel; }
        else { in.mi.dwFlags = down ? k.mDown : k.mUp; in.mi.mouseData = k.mData; }
    } else {
        in.type = INPUT_KEYBOARD;
        in.ki.wScan = (WORD)MapVirtualKeyW(k.vk, MAPVK_VK_TO_VSC);
        in.ki.dwFlags = KEYEVENTF_SCANCODE | (k.ext ? KEYEVENTF_EXTENDEDKEY : 0) | (down ? 0 : KEYEVENTF_KEYUP);
        if (k.vk == VK_PAUSE) { in.ki.wVk = k.vk; in.ki.dwFlags &= ~KEYEVENTF_SCANCODE; }
    }
    SendInput(1, &in, sizeof(in));
}
static void ComboDown(const std::vector<K>& ks) { for (auto& k : ks) { Send(k, true); Sleep(5); } }
static void ComboUp(const std::vector<K>& ks) { for (auto it = ks.rbegin(); it != ks.rend(); ++it) { Send(*it, false); Sleep(5); } }

// Worker thread so waits never stall the game's frame.
static std::mutex gMx;
static std::condition_variable gCv;
static std::deque<std::function<void()>> gQ;
static std::thread gThread;
static bool gStop = false;

static void Post(std::function<void()> f) {
    {
        std::lock_guard l(gMx);
        if (!gThread.joinable()) {
            gStop = false;
            gThread = std::thread([] {
                for (;;) {
                    std::function<void()> job;
                    {
                        std::unique_lock l(gMx);
                        gCv.wait(l, [] { return gStop || !gQ.empty(); });
                        if (gStop && gQ.empty()) return;
                        job = std::move(gQ.front()); gQ.pop_front();
                    }
                    job();
                }
            });
        }
        gQ.push_back(std::move(f));
    }
    gCv.notify_one();
}

bool Validate(const std::string& combo, std::string& err) { std::vector<K> k; return Parse(combo, k, err); }

void Tap(const std::string& combo, int holdMs) {
    std::vector<K> ks; std::string err;
    if (!Parse(combo, ks, err)) { Log("key: %s", err.c_str()); return; }
    Post([ks, holdMs] { FocusGame(); ComboDown(ks); Sleep(std::max(holdMs, 10)); ComboUp(ks); });
}
void Down(const std::string& combo) {
    std::vector<K> ks; std::string err;
    if (!Parse(combo, ks, err)) { Log("key: %s", err.c_str()); return; }
    gHeld.insert(combo);
    Post([ks] { FocusGame(); ComboDown(ks); });
}
void Up(const std::string& combo) {
    std::vector<K> ks; std::string err;
    if (!Parse(combo, ks, err)) return;
    gHeld.erase(combo);
    Post([ks] { ComboUp(ks); });
}
void ReleaseAll() {
    auto held = gHeld;
    for (auto& c : held) Up(c);
}
void SetFocusWindow(bool on) { gFocus = on; }

static std::vector<std::string> SplitSteps(const std::string& s) {
    std::vector<std::string> out; std::string cur;
    for (char c : s) { if (c == ';' || c == '\n') { if (!Trim(cur).empty()) out.push_back(Trim(cur)); cur.clear(); } else cur += c; }
    if (!Trim(cur).empty()) out.push_back(Trim(cur));
    return out;
}

bool ValidateSequence(const std::string& steps, std::string& err) {
    for (auto& st : SplitSteps(steps)) {
        std::string l = Lower(st);
        std::vector<K> k;
        if (l.rfind("wait ", 0) == 0) { if (atoi(l.c_str() + 5) <= 0) { err = "bad wait '" + st + "'"; return false; } }
        else if (l.rfind("down ", 0) == 0) { if (!Parse(st.substr(5), k, err)) return false; }
        else if (l.rfind("up ", 0) == 0) { if (!Parse(st.substr(3), k, err)) return false; }
        else if (!Parse(st, k, err)) return false;
    }
    return true;
}

void Sequence(const std::string& steps) {
    std::string err;
    if (!ValidateSequence(steps, err)) { Log("sequence: %s", err.c_str()); return; }
    auto list = SplitSteps(steps);
    Post([list] {
        FocusGame();
        for (auto& st : list) {
            std::string l = Lower(st); std::vector<K> k; std::string e;
            if (l.rfind("wait ", 0) == 0) Sleep(std::min(atoi(l.c_str() + 5), 10000));
            else if (l.rfind("down ", 0) == 0) { Parse(st.substr(5), k, e); ComboDown(k); }
            else if (l.rfind("up ", 0) == 0) { Parse(st.substr(3), k, e); ComboUp(k); }
            else { Parse(st, k, e); ComboDown(k); Sleep(50); ComboUp(k); }
        }
    });
}

void Shutdown() {
    ReleaseAll();
    {
        std::lock_guard l(gMx);
        gStop = true;
    }
    gCv.notify_all();
    if (gThread.joinable()) gThread.join();
}
}
