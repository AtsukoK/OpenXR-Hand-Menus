#pragma once
#include "config.h"
#include <set>

struct HandRaw {
    bool b1 = false, b1t = false, b2 = false, b2t = false;
    float trig = 0; bool trigT = false;
    float grip = 0;
    float sx = 0, sy = 0; bool sc = false, st = false;
    bool rest = false, menu = false;
    bool gripValid = false, aimValid = false;
    XrPosef gripPose{ {0,0,0,1}, {0,0,0} }, aimPose{ {0,0,0,1}, {0,0,0} };
};
struct Frame {
    HandRaw h[2];
    bool headValid = false;
    XrPosef head{ {0,0,0,1}, {0,0,0} };
    double t = 0;
};
// A fake value pushed into the game's own actions bound to a controller part.
struct Override {
    bool on = false;
    bool press = false, touch = false;
    float value = 0, x = 0, y = 0;
};
struct WheelView {
    bool visible = false;
    const Wheel* wheel = nullptr;
    int hovered = -1;
    std::string crumb;
    XrPosef pose{ {0,0,0,1}, {0,0,0} };
    float size = 0.24f;
    std::vector<bool> on;   // per item: its toggle is currently on
    uint64_t version = 0;
};
// Keyboard layout in key units (one letter key = 1x1).
enum KbKind { KB_KEY, KB_SHIFT, KB_CAPS, KB_CTRL, KB_ALT, KB_CLOSE };
struct KbKey { std::string label, shiftLabel, combo; float x, y, w, h; int kind; bool letter; bool repeat; };
const std::vector<KbKey>& KbLayout(float& unitsW, float& unitsH);

struct KeyboardView {
    bool visible = false;
    XrPosef pose{ {0,0,0,1}, {0,0,0} };
    float widthM = 0.56f, heightM = 0.19f;
    int hover[2] = { -1, -1 };
    int pressed = -1;
    bool shift = false, caps = false, ctrl = false, alt = false;
    uint64_t version = 0;
};

// Wrist panel: buttons on the forearm, shown when you turn it toward your eyes and look at it.
struct WristView {
    bool visible = false;
    const Wheel* wheel = nullptr;
    bool nested = false;    // a Back cell is added in front of the items
    int hover = -1;         // cell index
    int pressed = -1;
    XrPosef pose{ {0,0,0,1}, {0,0,0} };
    float sizeM = 0.11f;
    std::vector<bool> on;   // per cell: its toggle is currently on
    uint64_t version = 0;
};
struct Cell { float x, y, w, h; }; // 0..1, y down
// Header strip plus a grid; the same numbers are used for drawing and hit tests.
void WristLayout(int count, int columns, std::vector<Cell>& out, Cell& header);

struct HapticReq { int hand; float amp; float sec; };

class Engine {
public:
    std::shared_ptr<Profile> prof;
    WheelView view;
    KeyboardView kb;
    WristView wrist[2];             // [0] left forearm, [1] right forearm
    std::string toast; uint64_t toastVersion = 0; double toastUntil = 0;
    std::vector<HapticReq> haptics;
    bool reloadRequested = false;

    void SetProfile(std::shared_ptr<Profile> p);
    void Reset();
    void Update(const Frame& f);
    bool Blocked(int hand, int comp) const { return blockOut[hand][comp]; }
    const Override& Ov(int hand, int comp) const { return ovOut[hand][comp]; }
    void Toast(const std::string& s, double sec = 2.0);
    bool Active() const { return prof && prof->enabled; }
    // Is this action a toggle that is currently on? (key toggle, game input toggle, layer)
    bool IsOn(const Action& a) const;

private:
    struct BState { bool armed = false; double armTime = 0; bool fired = false; bool needEnd = false; double lastTap = -1; };
    struct Pulse { int hand, comp; Override o; double until; };
    struct WheelRun {
        bool open = false;
        std::vector<const Wheel*> stack;
        int hovered = -1, opener = -1, openerHand = 1, selHand = 1;
        std::string confirm;
        double hoverSince = 0, openTime = 0;
        bool stickOut = false, locked = false;
        XrPosef pose{ {0,0,0,1}, {0,0,0} };
    };

    Frame fr;
    bool cur[2][C_COUNT][F_COUNT]{}, prev[2][C_COUNT][F_COUNT]{};
    bool blockOut[2][C_COUNT]{}, sticky[2][C_COUNT]{};
    Override ovOut[2][C_COUNT];
    std::vector<BState> bs;
    std::set<std::string> layers;
    std::set<std::string> keyToggles;
    std::map<std::pair<int, int>, Override> xrToggles;
    std::map<int, std::pair<std::pair<int, int>, Override>> xrHolds; // binding -> override
    std::vector<Pulse> pulses;
    WheelRun wr;
    double now = 0;

    bool Down(const InputId& i) const { return cur[i.hand][i.comp][i.feat]; }
    bool Pressed(const InputId& i) const { return cur[i.hand][i.comp][i.feat] && !prev[i.hand][i.comp][i.feat]; }
    void ComputeDown();
    bool LayersOk(const Binding& b) const;
    void ProcessBindings();
    void Fire(int bi);
    void EndB(int bi);
    bool RunAction(const Action& a, int hand, int bi, bool fromWheel);
    void EndAction(const Action& a, int bi);
    void SetLayer(const std::string& l, bool on);
    void OpenWheel(const Action& a, int bi, int hand);
    void UpdateWheel();
    void Select(int idx);
    void CloseWheel();
    void PushWheel(const Wheel* w);
    void ComputeBlocks();
    bool CompActive(int h, int c) const;
    XrPosef WheelPose(int hand) const;
    static Override MakeOv(const InputId& t);
    void Dirty() { view.version++; }
    double kbOpenTime = 0, kbPressFlashUntil = 0, kbNextRepeat = 0;
    int kbRepeatKey = -1, kbRepeatHand = -1, kbOpener = -1;
    void OpenKeyboard(int bi);
    void CloseKeyboard();
    void UpdateKeyboard();
    void KbPress(int key, int hand);
    int KbHit(int hand) const;
    // wrist panel
    struct WristRun {
        std::vector<const Wheel*> stack;
        double condSince = -1, lostSince = -1, pressFlashUntil = 0, shownAt = 0;
        float prevDepth = 1.f; bool pokeArmed = true;
        bool closedByItem = false;
    };
    WristRun wrun[2];
    bool WristPoseNow(int h, XrPosef& pose, Vec3& normal) const;
    void UpdateWrist(int h);
    void WristActivate(int h, int cell, int presser);
    int WristCellAt(int h, float u, float v) const;
};
