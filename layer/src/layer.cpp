// OpenXR Hand Menus - OpenXR API layer.
// Adds a configurable radial menu, chords and layers on top of any OpenXR game.
#include "panel.h"
#include "keys.h"
#include <openxr/openxr_loader_negotiation.h>
#include <unordered_map>
#include <map>
#include <set>
#include <climits>

#define LAYER_NAME "XR_APILAYER_NOVENDOR_openxr_hand_menus"

// ---------------------------------------------------------------- next-layer functions
static PFN_xrGetInstanceProcAddr gNextGipa = nullptr;
static XrInstance gInst = XR_NULL_HANDLE;
static struct {
    PFN_xrDestroyInstance DestroyInstance;
    PFN_xrCreateSession CreateSession;
    PFN_xrDestroySession DestroySession;
    PFN_xrCreateActionSet CreateActionSet;
    PFN_xrCreateAction CreateAction;
    PFN_xrSuggestInteractionProfileBindings SuggestInteractionProfileBindings;
    PFN_xrAttachSessionActionSets AttachSessionActionSets;
    PFN_xrSyncActions SyncActions;
    PFN_xrGetActionStateBoolean GetActionStateBoolean;
    PFN_xrGetActionStateFloat GetActionStateFloat;
    PFN_xrGetActionStateVector2f GetActionStateVector2f;
    PFN_xrWaitFrame WaitFrame;
    PFN_xrEndFrame EndFrame;
    PFN_xrStringToPath StringToPath;
    PFN_xrPathToString PathToString;
    PFN_xrCreateActionSpace CreateActionSpace;
    PFN_xrCreateReferenceSpace CreateReferenceSpace;
    PFN_xrDestroySpace DestroySpace;
    PFN_xrLocateSpace LocateSpace;
    PFN_xrApplyHapticFeedback ApplyHapticFeedback;
} N{};
static XrFns gSwap;

// ---------------------------------------------------------------- our action set
enum OA { A_BTN1, A_BTN1T, A_BTN2, A_BTN2T, A_TRIG, A_TRIGT, A_GRIP, A_STICK, A_STICKC, A_STICKT, A_REST, A_MENU, A_GRIPPOSE, A_AIMPOSE, A_HAPTIC, A_COUNT };
static const struct { const char* name; XrActionType type; } kActs[A_COUNT] = {
    {"vro_btn1", XR_ACTION_TYPE_BOOLEAN_INPUT}, {"vro_btn1_touch", XR_ACTION_TYPE_BOOLEAN_INPUT},
    {"vro_btn2", XR_ACTION_TYPE_BOOLEAN_INPUT}, {"vro_btn2_touch", XR_ACTION_TYPE_BOOLEAN_INPUT},
    {"vro_trigger", XR_ACTION_TYPE_FLOAT_INPUT}, {"vro_trigger_touch", XR_ACTION_TYPE_BOOLEAN_INPUT},
    {"vro_grip", XR_ACTION_TYPE_FLOAT_INPUT}, {"vro_stick", XR_ACTION_TYPE_VECTOR2F_INPUT},
    {"vro_stick_click", XR_ACTION_TYPE_BOOLEAN_INPUT}, {"vro_stick_touch", XR_ACTION_TYPE_BOOLEAN_INPUT},
    {"vro_thumbrest", XR_ACTION_TYPE_BOOLEAN_INPUT}, {"vro_menu", XR_ACTION_TYPE_BOOLEAN_INPUT},
    {"vro_grip_pose", XR_ACTION_TYPE_POSE_INPUT}, {"vro_aim_pose", XR_ACTION_TYPE_POSE_INPUT},
    {"vro_haptic", XR_ACTION_TYPE_VIBRATION_OUTPUT},
};
struct PB { int act; const char* sub; int hands; }; // hands: 1 left, 2 right, 3 both
static const PB kCommonPoses[] = { {A_GRIPPOSE, "input/grip/pose", 3}, {A_AIMPOSE, "input/aim/pose", 3}, {A_HAPTIC, "output/haptic", 3} };
static const PB kTouch[] = {
    {A_BTN1, "input/x/click", 1}, {A_BTN1T, "input/x/touch", 1}, {A_BTN2, "input/y/click", 1}, {A_BTN2T, "input/y/touch", 1}, {A_MENU, "input/menu/click", 1},
    {A_BTN1, "input/a/click", 2}, {A_BTN1T, "input/a/touch", 2}, {A_BTN2, "input/b/click", 2}, {A_BTN2T, "input/b/touch", 2},
    {A_TRIG, "input/trigger/value", 3}, {A_TRIGT, "input/trigger/touch", 3}, {A_GRIP, "input/squeeze/value", 3},
    {A_STICK, "input/thumbstick", 3}, {A_STICKC, "input/thumbstick/click", 3}, {A_STICKT, "input/thumbstick/touch", 3},
    {A_REST, "input/thumbrest/touch", 3},
};
static const PB kIndex[] = {
    {A_BTN1, "input/a/click", 3}, {A_BTN1T, "input/a/touch", 3}, {A_BTN2, "input/b/click", 3}, {A_BTN2T, "input/b/touch", 3},
    {A_TRIG, "input/trigger/value", 3}, {A_TRIGT, "input/trigger/touch", 3}, {A_GRIP, "input/squeeze/value", 3},
    {A_STICK, "input/thumbstick", 3}, {A_STICKC, "input/thumbstick/click", 3}, {A_STICKT, "input/thumbstick/touch", 3},
};
static const PB kWmr[] = {
    {A_MENU, "input/menu/click", 3}, {A_GRIP, "input/squeeze/click", 3}, {A_TRIG, "input/trigger/value", 3},
    {A_STICK, "input/thumbstick", 3}, {A_STICKC, "input/thumbstick/click", 3},
};
static const PB kVive[] = {
    {A_MENU, "input/menu/click", 3}, {A_GRIP, "input/squeeze/click", 3}, {A_TRIG, "input/trigger/value", 3},
    {A_STICK, "input/trackpad", 3}, {A_STICKC, "input/trackpad/click", 3}, {A_STICKT, "input/trackpad/touch", 3},
};
static const PB kSimple[] = { {A_TRIG, "input/select/click", 3}, {A_MENU, "input/menu/click", 3} };

static std::vector<PB> ProfileTable(const std::string& p) {
    std::vector<PB> v;
    auto add = [&](const PB* b, size_t n) { v.insert(v.end(), b, b + n); };
    if (p == "/interaction_profiles/oculus/touch_controller" || p == "/interaction_profiles/meta/touch_controller_plus" ||
        p == "/interaction_profiles/facebook/touch_controller_pro" || p == "/interaction_profiles/meta/touch_pro_controller" ||
        p == "/interaction_profiles/meta/touch_plus_controller") add(kTouch, std::size(kTouch));
    else if (p == "/interaction_profiles/valve/index_controller") add(kIndex, std::size(kIndex));
    else if (p == "/interaction_profiles/microsoft/motion_controller" || p == "/interaction_profiles/hp/mixed_reality_controller") add(kWmr, std::size(kWmr));
    else if (p == "/interaction_profiles/htc/vive_controller") add(kVive, std::size(kVive));
    else if (p == "/interaction_profiles/khr/simple_controller") add(kSimple, std::size(kSimple));
    else return v;
    add(kCommonPoses, std::size(kCommonPoses));
    return v;
}

// ---------------------------------------------------------------- state
static std::recursive_mutex gMx;
static XrActionSet gSet = XR_NULL_HANDLE;
static XrAction gA[A_COUNT]{};
static XrPath gHandPath[2]{};
static bool gAttached = false;
static int gMinPriority = INT_MAX;
static std::set<std::string> gOurProfiles;

struct AppBind { int hand, comp, pf; }; // pf: 0 press/value, 1 touch, 2 vec2, 3 x, 4 y
static std::unordered_map<uint64_t, std::vector<AppBind>> gAppBinds;
static std::unordered_map<uint64_t, std::vector<XrPath>> gActionSubs;

static XrSession gSess = XR_NULL_HANDLE;
static Gfx gGfx;
static XrSpace gLocal = XR_NULL_HANDLE, gView = XR_NULL_HANDLE, gGripSp[2]{}, gAimSp[2]{};
static XrTime gPredTime = 0;
static uint64_t gSyncIdx = 0;

static Engine gEng;
static Panel gWheelPanel, gToastPanel, gKbPanel, gWristPanel[2], gCursorPanel[2];
static uint64_t gDrawnWrist[2] = { ~0ull, ~0ull };
static int gDrawnCursor[2] = { -1, -1 };
static uint64_t gDrawnKb = ~0ull;
static uint64_t gDrawnWheel = ~0ull, gDrawnToast = ~0ull;
static int gWheelTex = 0;
static std::vector<uint32_t> gPix;

struct Track { uint64_t sync = ~0ull; float prev[2]{}, cur[2]{}; };
static std::unordered_map<uint64_t, Track> gTrack;

static FILETIME gCfgTimes[2]{};
static double gLastCfgCheck = 0;

static inline uint64_t H(XrAction a) { return (uint64_t)a; }

// ---------------------------------------------------------------- helpers
static XrPath P(const std::string& s) { XrPath p = XR_NULL_PATH; N.StringToPath(gInst, s.c_str(), &p); return p; }
static std::string S(XrPath p) {
    char buf[XR_MAX_PATH_LENGTH]; uint32_t n = 0;
    if (XR_FAILED(N.PathToString(gInst, p, sizeof(buf), &n, buf))) return {};
    return buf;
}

static bool ParseAppPath(const std::string& s, AppBind& b) {
    const std::string pre[2] = { "/user/hand/left/input/", "/user/hand/right/input/" };
    for (int h = 0; h < 2; h++) {
        if (s.rfind(pre[h], 0) != 0) continue;
        std::string rest = s.substr(pre[h].size());
        auto sl = rest.find('/');
        std::string comp = rest.substr(0, sl), feat = sl == std::string::npos ? "" : rest.substr(sl + 1);
        int c = comp == "select" ? C_TRIGGER : ParseCompName(comp);
        if (c < 0 || comp == "system") return false;
        b.hand = h; b.comp = c;
        if (feat == "touch") b.pf = 1;
        else if (feat.empty() && c == C_STICK) b.pf = 2;
        else if (feat == "x") b.pf = 3;
        else if (feat == "y") b.pf = 4;
        else if (feat == "click" || feat == "value" || feat == "force" || feat.empty()) b.pf = 0;
        else return false;
        return true;
    }
    return false;
}

static void EnsureActions() {
    if (gSet != XR_NULL_HANDLE || gInst == XR_NULL_HANDLE) return;
    gHandPath[0] = P("/user/hand/left");
    gHandPath[1] = P("/user/hand/right");
    XrActionSetCreateInfo si{ XR_TYPE_ACTION_SET_CREATE_INFO };
    strcpy_s(si.actionSetName, "openxr_hand_menus_set");
    strcpy_s(si.localizedActionSetName, "OpenXR Hand Menus");
    si.priority = gMinPriority == INT_MAX ? 0 : (uint32_t)gMinPriority;
    XrResult r = N.CreateActionSet(gInst, &si, &gSet);
    if (XR_FAILED(r)) { Log("CreateActionSet failed %d", r); gSet = XR_NULL_HANDLE; return; }
    for (int i = 0; i < A_COUNT; i++) {
        XrActionCreateInfo ci{ XR_TYPE_ACTION_CREATE_INFO };
        strcpy_s(ci.actionName, kActs[i].name);
        strcpy_s(ci.localizedActionName, kActs[i].name);
        ci.actionType = kActs[i].type;
        ci.countSubactionPaths = 2; ci.subactionPaths = gHandPath;
        r = N.CreateAction(gSet, &ci, &gA[i]);
        if (XR_FAILED(r)) Log("CreateAction %s failed %d", kActs[i].name, r);
    }
    Log("our action set created, priority %u", si.priority);
}

static void ReloadProfile(bool announce) {
    std::wstring path; std::string err;
    auto p = LoadProfile(path, err);
    if (!p) {
        Log("config error: %s", err.c_str());
        gEng.Toast("Config error: " + err, 6.0);
        return;
    }
    gEng.SetProfile(p);
    gDrawnWheel = gDrawnKb = ~0ull; gDrawnWrist[0] = gDrawnWrist[1] = ~0ull;
    Log("loaded profile %s (%zu bindings, %zu wheels, enabled=%d)", p->sourceName.c_str(), p->bindings.size(), p->wheels.size(), p->enabled);
    if (announce) gEng.Toast("Hand menus: " + p->sourceName + " loaded");
}

static FILETIME FileTime(const std::wstring& f) {
    WIN32_FILE_ATTRIBUTE_DATA d{};
    if (!GetFileAttributesExW(f.c_str(), GetFileExInfoStandard, &d)) return {};
    return d.ftLastWriteTime;
}
static void CheckConfigChanged() {
    double now = NowSec();
    if (now - gLastCfgCheck < 1.0) return;
    gLastCfgCheck = now;
    std::wstring dir = ProfilesDir();
    FILETIME t[2] = { FileTime(dir + L"\\" + Utf8ToW(ExeName()) + L".json"), FileTime(dir + L"\\default.json") };
    bool changed = memcmp(t, gCfgTimes, sizeof(t)) != 0;
    bool first = gCfgTimes[1].dwLowDateTime == 0 && gCfgTimes[1].dwHighDateTime == 0;
    memcpy(gCfgTimes, t, sizeof(t));
    if (changed && !first) ReloadProfile(true);
}

static bool Locate(XrSpace sp, XrSpace base, XrPosef& out) {
    if (!sp || !base || !gPredTime) return false;
    XrSpaceLocation l{ XR_TYPE_SPACE_LOCATION };
    if (XR_FAILED(N.LocateSpace(sp, base, gPredTime, &l))) return false;
    const XrSpaceLocationFlags need = XR_SPACE_LOCATION_POSITION_VALID_BIT | XR_SPACE_LOCATION_ORIENTATION_VALID_BIT;
    if ((l.locationFlags & need) != need) return false;
    out = l.pose;
    return true;
}

static void ReadInputsAndUpdate() {
    Frame f;
    f.t = NowSec();
    for (int h = 0; h < 2; h++) {
        auto gb = [&](int a) {
            XrActionStateGetInfo gi{ XR_TYPE_ACTION_STATE_GET_INFO }; gi.action = gA[a]; gi.subactionPath = gHandPath[h];
            XrActionStateBoolean s{ XR_TYPE_ACTION_STATE_BOOLEAN };
            return XR_SUCCEEDED(N.GetActionStateBoolean(gSess, &gi, &s)) && s.isActive && s.currentState;
        };
        auto gf = [&](int a) {
            XrActionStateGetInfo gi{ XR_TYPE_ACTION_STATE_GET_INFO }; gi.action = gA[a]; gi.subactionPath = gHandPath[h];
            XrActionStateFloat s{ XR_TYPE_ACTION_STATE_FLOAT };
            return XR_SUCCEEDED(N.GetActionStateFloat(gSess, &gi, &s)) && s.isActive ? s.currentState : 0.f;
        };
        HandRaw& r = f.h[h];
        r.b1 = gb(A_BTN1); r.b1t = gb(A_BTN1T); r.b2 = gb(A_BTN2); r.b2t = gb(A_BTN2T);
        r.trig = gf(A_TRIG); r.trigT = gb(A_TRIGT); r.grip = gf(A_GRIP);
        {
            XrActionStateGetInfo gi{ XR_TYPE_ACTION_STATE_GET_INFO }; gi.action = gA[A_STICK]; gi.subactionPath = gHandPath[h];
            XrActionStateVector2f s{ XR_TYPE_ACTION_STATE_VECTOR2F };
            if (XR_SUCCEEDED(N.GetActionStateVector2f(gSess, &gi, &s)) && s.isActive) { r.sx = s.currentState.x; r.sy = s.currentState.y; }
        }
        r.sc = gb(A_STICKC); r.st = gb(A_STICKT); r.rest = gb(A_REST); r.menu = gb(A_MENU);
        r.gripValid = Locate(gGripSp[h], gLocal, r.gripPose);
        r.aimValid = Locate(gAimSp[h], gLocal, r.aimPose);
    }
    f.headValid = Locate(gView, gLocal, f.head);
    gEng.Update(f);

    for (auto& hq : gEng.haptics) {
        XrHapticActionInfo hi{ XR_TYPE_HAPTIC_ACTION_INFO }; hi.action = gA[A_HAPTIC]; hi.subactionPath = gHandPath[hq.hand];
        XrHapticVibration v{ XR_TYPE_HAPTIC_VIBRATION };
        v.amplitude = hq.amp; v.duration = (XrDuration)(hq.sec * 1e9); v.frequency = XR_FREQUENCY_UNSPECIFIED;
        N.ApplyHapticFeedback(gSess, &hi, (XrHapticBaseHeader*)&v);
    }
    gEng.haptics.clear();
    if (gEng.reloadRequested) { gEng.reloadRequested = false; ReloadProfile(true); }
    if (gEng.saveRequested && gEng.prof) { // leaving adjust mode: keep the new placement
        gEng.saveRequested = false;
        std::string err;
        if (SavePlacement(*gEng.prof, err)) Log("adjust: placement saved to %s", gEng.prof->sourceName.c_str());
        else { Log("adjust: save failed: %s", err.c_str()); gEng.Toast("Could not save positions: " + err, 6.0); }
    }
}

// ---------------------------------------------------------------- hooks
static XrResult XRAPI_CALL Hook_xrDestroyInstance(XrInstance inst) {
    {
        std::lock_guard l(gMx);
        Keys::Shutdown();
        DrawShutdown();
        gSet = XR_NULL_HANDLE; gAttached = false; gAppBinds.clear(); gActionSubs.clear(); gOurProfiles.clear();
        gMinPriority = INT_MAX; gInst = XR_NULL_HANDLE;
    }
    return N.DestroyInstance(inst);
}

static XrResult XRAPI_CALL Hook_xrCreateActionSet(XrInstance inst, const XrActionSetCreateInfo* ci, XrActionSet* set) {
    XrResult r = N.CreateActionSet(inst, ci, set);
    if (XR_SUCCEEDED(r)) { std::lock_guard l(gMx); gMinPriority = std::min(gMinPriority, (int)ci->priority); }
    return r;
}

static XrResult XRAPI_CALL Hook_xrCreateAction(XrActionSet set, const XrActionCreateInfo* ci, XrAction* a) {
    XrResult r = N.CreateAction(set, ci, a);
    if (XR_SUCCEEDED(r)) {
        std::lock_guard l(gMx);
        gActionSubs[H(*a)].assign(ci->subactionPaths, ci->subactionPaths + ci->countSubactionPaths);
    }
    return r;
}

static XrResult XRAPI_CALL Hook_xrSuggestInteractionProfileBindings(XrInstance inst, const XrInteractionProfileSuggestedBinding* sb) {
    std::lock_guard l(gMx);
    std::string prof = S(sb->interactionProfile);
    for (uint32_t i = 0; i < sb->countSuggestedBindings; i++) {
        AppBind b;
        if (ParseAppPath(S(sb->suggestedBindings[i].binding), b)) {
            auto& v = gAppBinds[H(sb->suggestedBindings[i].action)];
            bool dup = false;
            for (auto& e : v) if (e.hand == b.hand && e.comp == b.comp && e.pf == b.pf) dup = true;
            if (!dup) v.push_back(b);
        }
    }
    EnsureActions();
    auto table = ProfileTable(prof);
    if (gSet == XR_NULL_HANDLE || table.empty()) {
        Log("suggest %s: %u app bindings (not augmented)", prof.c_str(), sb->countSuggestedBindings);
        return N.SuggestInteractionProfileBindings(inst, sb);
    }
    std::vector<XrActionSuggestedBinding> all(sb->suggestedBindings, sb->suggestedBindings + sb->countSuggestedBindings);
    for (auto& e : table)
        for (int h = 0; h < 2; h++)
            if (e.hands & (1 << h)) all.push_back({ gA[e.act], P(std::string(h ? "/user/hand/right/" : "/user/hand/left/") + e.sub) });
    XrInteractionProfileSuggestedBinding copy = *sb;
    copy.countSuggestedBindings = (uint32_t)all.size();
    copy.suggestedBindings = all.data();
    XrResult r = N.SuggestInteractionProfileBindings(inst, &copy);
    if (XR_FAILED(r)) {
        Log("suggest %s with our bindings failed (%d), retrying app-only", prof.c_str(), r);
        return N.SuggestInteractionProfileBindings(inst, sb);
    }
    gOurProfiles.insert(prof);
    Log("suggest %s: %u app + %zu ours", prof.c_str(), sb->countSuggestedBindings, all.size() - sb->countSuggestedBindings);
    return r;
}

static XrResult XRAPI_CALL Hook_xrCreateSession(XrInstance inst, const XrSessionCreateInfo* ci, XrSession* s) {
    XrResult r = N.CreateSession(inst, ci, s);
    if (XR_FAILED(r)) return r;
    std::lock_guard l(gMx);
    gSess = *s;
    gGfx.Init(ci->next);
    Log("session created, graphics api %d", gGfx.api);
    if (!gGfx.api) Log("graphics API is not D3D11/D3D12: the wheel will not be drawn, bindings still work");
    XrReferenceSpaceCreateInfo rs{ XR_TYPE_REFERENCE_SPACE_CREATE_INFO };
    rs.poseInReferenceSpace.orientation.w = 1;
    rs.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_LOCAL;
    N.CreateReferenceSpace(gSess, &rs, &gLocal);
    rs.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_VIEW;
    N.CreateReferenceSpace(gSess, &rs, &gView);
    ReloadProfile(true);
    return r;
}

static void DestroySessionStuff() {
    gWheelPanel.Destroy(gSwap); gToastPanel.Destroy(gSwap); gKbPanel.Destroy(gSwap); gDrawnKb = ~0ull; for (int h = 0; h < 2; h++) { gWristPanel[h].Destroy(gSwap); gDrawnWrist[h] = ~0ull; gCursorPanel[h].Destroy(gSwap); gDrawnCursor[h] = -1; }
    for (int h = 0; h < 2; h++) {
        if (gGripSp[h]) N.DestroySpace(gGripSp[h]);
        if (gAimSp[h]) N.DestroySpace(gAimSp[h]);
        gGripSp[h] = gAimSp[h] = XR_NULL_HANDLE;
    }
    if (gLocal) N.DestroySpace(gLocal);
    if (gView) N.DestroySpace(gView);
    gLocal = gView = XR_NULL_HANDLE;
    gEng.Reset();
    gGfx.Release();
    gSess = XR_NULL_HANDLE; gAttached = false; gPredTime = 0;
    gDrawnWheel = gDrawnToast = ~0ull;
}

static XrResult XRAPI_CALL Hook_xrDestroySession(XrSession s) {
    {
        std::lock_guard l(gMx);
        if (s == gSess) DestroySessionStuff();
    }
    return N.DestroySession(s);
}

static XrResult XRAPI_CALL Hook_xrAttachSessionActionSets(XrSession s, const XrSessionActionSetsAttachInfo* ai) {
    std::lock_guard l(gMx);
    EnsureActions();
    if (gSet == XR_NULL_HANDLE || s != gSess) return N.AttachSessionActionSets(s, ai);
    if (gOurProfiles.empty()) Log("warning: the game suggested no controller profile we know; our inputs will be inactive");
    std::vector<XrActionSet> sets(ai->actionSets, ai->actionSets + ai->countActionSets);
    sets.push_back(gSet);
    XrSessionActionSetsAttachInfo copy = *ai;
    copy.countActionSets = (uint32_t)sets.size(); copy.actionSets = sets.data();
    XrResult r = N.AttachSessionActionSets(s, &copy);
    if (XR_FAILED(r)) {
        Log("attach with our set failed (%d), retrying without", r);
        return N.AttachSessionActionSets(s, ai);
    }
    gAttached = true;
    for (int h = 0; h < 2; h++) {
        XrActionSpaceCreateInfo ci{ XR_TYPE_ACTION_SPACE_CREATE_INFO };
        ci.poseInActionSpace.orientation.w = 1;
        ci.subactionPath = gHandPath[h];
        ci.action = gA[A_GRIPPOSE]; N.CreateActionSpace(s, &ci, &gGripSp[h]);
        ci.action = gA[A_AIMPOSE]; N.CreateActionSpace(s, &ci, &gAimSp[h]);
    }
    Log("action sets attached (%u app + ours)", ai->countActionSets);
    return r;
}

static XrResult XRAPI_CALL Hook_xrSyncActions(XrSession s, const XrActionsSyncInfo* si) {
    std::lock_guard l(gMx);
    if (!gAttached || s != gSess || !si) return N.SyncActions(s, si);
    std::vector<XrActiveActionSet> sets(si->activeActionSets, si->activeActionSets + si->countActiveActionSets);
    sets.push_back({ gSet, XR_NULL_PATH });
    XrActionsSyncInfo copy = *si;
    copy.countActiveActionSets = (uint32_t)sets.size(); copy.activeActionSets = sets.data();
    XrResult r = N.SyncActions(s, &copy);
    gSyncIdx++;
    if (r == XR_SUCCESS) ReadInputsAndUpdate();
    CheckConfigChanged();
    return r;
}

// Work out which hands a query covers: bit 0 left, bit 1 right.
static int HandsMask(XrPath sub) {
    if (sub == XR_NULL_PATH) return 3;
    if (sub == gHandPath[0]) return 1;
    if (sub == gHandPath[1]) return 2;
    return 0;
}

struct Eval { bool any = false, blockedAll = false, blockedSome = false; bool ov = false; Override o; int pf = 0; int freeHand = -1; };
static Eval Evaluate(XrAction a, XrPath sub) {
    Eval e;
    if (!gEng.Active()) return e;
    auto it = gAppBinds.find(H(a));
    if (it == gAppBinds.end()) return e;
    int mask = HandsMask(sub);
    int nb = 0, nblk = 0;
    for (auto& b : it->second) {
        if (!(mask & (1 << b.hand))) continue;
        e.any = true; nb++;
        const Override& o = gEng.Ov(b.hand, b.comp);
        if (o.on && !e.ov) { e.ov = true; e.o = o; e.pf = b.pf; }
        if (gEng.Blocked(b.hand, b.comp)) nblk++;
        else e.freeHand = b.hand;
    }
    e.blockedAll = nb > 0 && nblk == nb;
    e.blockedSome = nblk > 0 && nblk < nb;
    return e;
}

// Keep changedSinceLastSync honest when we alter values.
static bool TrackChange(XrAction a, XrPath sub, float v0, float v1) {
    uint64_t key = H(a) * 1000003ull ^ (uint64_t)sub;
    Track& t = gTrack[key];
    if (t.sync != gSyncIdx) { if (t.sync != ~0ull) { t.prev[0] = t.cur[0]; t.prev[1] = t.cur[1]; } else { t.prev[0] = v0; t.prev[1] = v1; } t.sync = gSyncIdx; }
    t.cur[0] = v0; t.cur[1] = v1;
    return t.prev[0] != v0 || t.prev[1] != v1;
}

static bool CanRequery(XrAction a, int hand) {
    auto it = gActionSubs.find(H(a));
    if (it == gActionSubs.end()) return false;
    for (auto p : it->second) if (p == gHandPath[hand]) return true;
    return false;
}

static XrResult XRAPI_CALL Hook_xrGetActionStateBoolean(XrSession s, const XrActionStateGetInfo* gi, XrActionStateBoolean* st) {
    XrResult r = N.GetActionStateBoolean(s, gi, st);
    if (XR_FAILED(r) || !gi) return r;
    std::lock_guard l(gMx);
    Eval e = Evaluate(gi->action, gi->subactionPath);
    if (!e.any) return r;
    bool v = st->currentState;
    if (e.ov) { v = e.pf == 1 ? e.o.touch : e.o.press; st->isActive = XR_TRUE; }
    else if (e.blockedAll) v = false;
    else if (e.blockedSome && gi->subactionPath == XR_NULL_PATH) {
        if (e.freeHand >= 0 && CanRequery(gi->action, e.freeHand)) {
            XrActionStateGetInfo g2 = *gi; g2.subactionPath = gHandPath[e.freeHand];
            XrActionStateBoolean s2{ XR_TYPE_ACTION_STATE_BOOLEAN };
            if (XR_SUCCEEDED(N.GetActionStateBoolean(s, &g2, &s2))) v = s2.currentState; else v = false;
        } else v = false;
    }
    st->currentState = v;
    bool ch = TrackChange(gi->action, gi->subactionPath, v ? 1.f : 0.f, 0);
    st->changedSinceLastSync = ch;
    if (ch) st->lastChangeTime = gPredTime;
    return r;
}

static XrResult XRAPI_CALL Hook_xrGetActionStateFloat(XrSession s, const XrActionStateGetInfo* gi, XrActionStateFloat* st) {
    XrResult r = N.GetActionStateFloat(s, gi, st);
    if (XR_FAILED(r) || !gi) return r;
    std::lock_guard l(gMx);
    Eval e = Evaluate(gi->action, gi->subactionPath);
    if (!e.any) return r;
    float v = st->currentState;
    if (e.ov) {
        if (e.pf == 3) v = e.o.x; else if (e.pf == 4) v = e.o.y; else if (e.pf == 1) v = e.o.touch ? 1.f : 0.f; else v = e.o.value;
        st->isActive = XR_TRUE;
    } else if (e.blockedAll) v = 0;
    else if (e.blockedSome && gi->subactionPath == XR_NULL_PATH) {
        if (e.freeHand >= 0 && CanRequery(gi->action, e.freeHand)) {
            XrActionStateGetInfo g2 = *gi; g2.subactionPath = gHandPath[e.freeHand];
            XrActionStateFloat s2{ XR_TYPE_ACTION_STATE_FLOAT };
            v = XR_SUCCEEDED(N.GetActionStateFloat(s, &g2, &s2)) ? s2.currentState : 0.f;
        } else v = 0;
    }
    st->currentState = v;
    bool ch = TrackChange(gi->action, gi->subactionPath, v, 0);
    st->changedSinceLastSync = ch;
    if (ch) st->lastChangeTime = gPredTime;
    return r;
}

static XrResult XRAPI_CALL Hook_xrGetActionStateVector2f(XrSession s, const XrActionStateGetInfo* gi, XrActionStateVector2f* st) {
    XrResult r = N.GetActionStateVector2f(s, gi, st);
    if (XR_FAILED(r) || !gi) return r;
    std::lock_guard l(gMx);
    Eval e = Evaluate(gi->action, gi->subactionPath);
    if (!e.any) return r;
    XrVector2f v = st->currentState;
    if (e.ov) { v = { e.o.x, e.o.y }; st->isActive = XR_TRUE; }
    else if (e.blockedAll) v = { 0, 0 };
    else if (e.blockedSome && gi->subactionPath == XR_NULL_PATH) {
        if (e.freeHand >= 0 && CanRequery(gi->action, e.freeHand)) {
            XrActionStateGetInfo g2 = *gi; g2.subactionPath = gHandPath[e.freeHand];
            XrActionStateVector2f s2{ XR_TYPE_ACTION_STATE_VECTOR2F };
            v = XR_SUCCEEDED(N.GetActionStateVector2f(s, &g2, &s2)) ? s2.currentState : XrVector2f{ 0, 0 };
        } else v = { 0, 0 };
    }
    st->currentState = v;
    bool ch = TrackChange(gi->action, gi->subactionPath, v.x, v.y);
    st->changedSinceLastSync = ch;
    if (ch) st->lastChangeTime = gPredTime;
    return r;
}

static XrResult XRAPI_CALL Hook_xrWaitFrame(XrSession s, const XrFrameWaitInfo* wi, XrFrameState* fs) {
    XrResult r = N.WaitFrame(s, wi, fs);
    if (XR_SUCCEEDED(r) && fs) { std::lock_guard l(gMx); gPredTime = fs->predictedDisplayTime; }
    return r;
}

static XrResult XRAPI_CALL Hook_xrEndFrame(XrSession s, const XrFrameEndInfo* fi) {
    std::lock_guard l(gMx);
    if (s != gSess || !fi || !gEng.Active() || !gGfx.api) return N.EndFrame(s, fi);
    std::vector<const XrCompositionLayerBaseHeader*> layers(fi->layers, fi->layers + fi->layerCount);
    XrCompositionLayerQuad wq{ XR_TYPE_COMPOSITION_LAYER_QUAD }, tq{ XR_TYPE_COMPOSITION_LAYER_QUAD }, kq{ XR_TYPE_COMPOSITION_LAYER_QUAD }, rq[2]{ { XR_TYPE_COMPOSITION_LAYER_QUAD }, { XR_TYPE_COMPOSITION_LAYER_QUAD } }, cq[2]{ { XR_TYPE_COMPOSITION_LAYER_QUAD }, { XR_TYPE_COMPOSITION_LAYER_QUAD } };
    const Profile& p = *gEng.prof;

    if (gEng.view.visible && gLocal) {
        int tex = p.style.texSize;
        if (gWheelPanel.sc && gWheelTex != tex) { gWheelPanel.Destroy(gSwap); gDrawnWheel = ~0ull; }
        if (!gWheelPanel.sc && gWheelPanel.Create(gSess, gGfx, gSwap, tex, tex)) gWheelTex = tex;
        if (gWheelPanel.sc && gDrawnWheel != gEng.view.version) {
            DrawWheel(gPix, tex, p, gEng.view);
            if (gWheelPanel.Upload(gGfx, gSwap, gPix)) gDrawnWheel = gEng.view.version;
        }
        if (gWheelPanel.ready) {
            wq.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
            wq.space = gLocal;
            wq.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
            wq.subImage.swapchain = gWheelPanel.sc;
            wq.subImage.imageRect = { {0, 0}, {gWheelPanel.w, gWheelPanel.h} };
            wq.pose = gEng.view.pose;
            wq.size = { gEng.view.size, gEng.view.size };
            layers.push_back((const XrCompositionLayerBaseHeader*)&wq);
        }
    }
    if (gEng.kb.visible && gLocal) {
        const int KW = 1500;
        float uw, uh; KbLayout(uw, uh);
        const int KH = (int)(KW * uh / uw);
        if (!gKbPanel.sc) gKbPanel.Create(gSess, gGfx, gSwap, KW, KH);
        if (gKbPanel.sc && gDrawnKb != gEng.kb.version) {
            DrawKeyboard(gPix, KW, KH, gEng.kb, p.style);
            if (gKbPanel.Upload(gGfx, gSwap, gPix)) gDrawnKb = gEng.kb.version;
        }
        if (gKbPanel.ready) {
            kq.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
            kq.space = gLocal;
            kq.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
            kq.subImage.swapchain = gKbPanel.sc;
            kq.subImage.imageRect = { {0, 0}, {KW, KH} };
            kq.pose = gEng.kb.pose;
            kq.size = { gEng.kb.widthM, gEng.kb.heightM };
            layers.push_back((const XrCompositionLayerBaseHeader*)&kq);
        }
    }
    for (int h = 0; h < 2; h++) {
        const WristView& wv = gEng.wrist[h];
        if (!wv.visible || !gLocal) continue;
        const int WS = 512;
        Panel& pn = gWristPanel[h];
        if (!pn.sc) pn.Create(gSess, gGfx, gSwap, WS, WS);
        if (pn.sc && gDrawnWrist[h] != wv.version) {
            DrawWrist(gPix, WS, p, p.wrist[h], wv);
            if (pn.Upload(gGfx, gSwap, gPix)) gDrawnWrist[h] = wv.version;
        }
        if (pn.ready) {
            auto& q = rq[h];
            q.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
            q.space = gLocal;
            q.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
            q.subImage.swapchain = pn.sc;
            q.subImage.imageRect = { {0, 0}, {WS, WS} };
            q.pose = wv.pose;
            q.size = { wv.sizeM, wv.sizeM };
            layers.push_back((const XrCompositionLayerBaseHeader*)&q);
        }
        // Touch cursor on top of the panel: redrawn only when its look changes, moved every frame.
        if (pn.ready && wv.cursor) {
            const int CS = 128;
            Panel& cp = gCursorPanel[h];
            if (!cp.sc) cp.Create(gSess, gGfx, gSwap, CS, CS);
            if (cp.sc && gDrawnCursor[h] != wv.cursorLevel) {
                std::vector<uint32_t> px;
                DrawCursor(px, CS, wv.cursorLevel, p.style);
                if (cp.Upload(gGfx, gSwap, px)) gDrawnCursor[h] = wv.cursorLevel;
            }
            if (cp.ready) {
                auto& q = cq[h];
                q.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
                q.space = gLocal;
                q.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
                q.subImage.swapchain = cp.sc;
                q.subImage.imageRect = { {0, 0}, {CS, CS} };
                q.pose = wv.cursorPose;
                q.size = { wv.cursorSize, wv.cursorSize };
                layers.push_back((const XrCompositionLayerBaseHeader*)&q);
            }
        }
    }
    if (NowSec() < gEng.toastUntil && gView) {
        const int TW = 1024, TH = 128;
        if (!gToastPanel.sc) gToastPanel.Create(gSess, gGfx, gSwap, TW, TH);
        if (gToastPanel.sc && gDrawnToast != gEng.toastVersion) {
            DrawToast(gPix, TW, TH, gEng.toast, p.style);
            if (gToastPanel.Upload(gGfx, gSwap, gPix)) gDrawnToast = gEng.toastVersion;
        }
        if (gToastPanel.ready) {
            tq.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
            tq.space = gView;
            tq.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
            tq.subImage.swapchain = gToastPanel.sc;
            tq.subImage.imageRect = { {0, 0}, {TW, TH} };
            tq.pose.orientation.w = 1;
            tq.pose.position = { 0, -0.14f, -0.6f };
            tq.size = { 0.32f, 0.04f };
            layers.push_back((const XrCompositionLayerBaseHeader*)&tq);
        }
    }
    XrFrameEndInfo copy = *fi;
    copy.layerCount = (uint32_t)layers.size();
    copy.layers = layers.data();
    return N.EndFrame(s, &copy);
}

// ---------------------------------------------------------------- dispatch
static XrResult XRAPI_CALL Hook_xrGetInstanceProcAddr(XrInstance inst, const char* name, PFN_xrVoidFunction* fn) {
    XrResult r = gNextGipa(inst, name, fn);
    if (XR_FAILED(r) || !name) return r;
    std::string n = name;
#define HOOK(x) if (n == "xr" #x) { *fn = (PFN_xrVoidFunction)Hook_xr##x; return r; }
    HOOK(GetInstanceProcAddr) HOOK(DestroyInstance) HOOK(CreateSession) HOOK(DestroySession)
    HOOK(CreateActionSet) HOOK(CreateAction) HOOK(SuggestInteractionProfileBindings) HOOK(AttachSessionActionSets)
    HOOK(SyncActions) HOOK(GetActionStateBoolean) HOOK(GetActionStateFloat) HOOK(GetActionStateVector2f)
    HOOK(WaitFrame) HOOK(EndFrame)
#undef HOOK
    return r;
}

static XrResult XRAPI_CALL Hook_xrCreateApiLayerInstance(const XrInstanceCreateInfo* info, const XrApiLayerCreateInfo* li, XrInstance* inst) {
    LogInit();
    if (!li || !li->nextInfo) return XR_ERROR_INITIALIZATION_FAILED;
    gNextGipa = li->nextInfo->nextGetInstanceProcAddr;
    XrApiLayerCreateInfo chain = *li;
    chain.nextInfo = li->nextInfo->next;
    XrResult r = li->nextInfo->nextCreateApiLayerInstance(info, &chain, inst);
    if (XR_FAILED(r)) return r;
    gInst = *inst;
    auto get = [&](const char* nm, auto& f) { gNextGipa(gInst, nm, (PFN_xrVoidFunction*)&f); };
    get("xrDestroyInstance", N.DestroyInstance); get("xrCreateSession", N.CreateSession); get("xrDestroySession", N.DestroySession);
    get("xrCreateActionSet", N.CreateActionSet); get("xrCreateAction", N.CreateAction);
    get("xrSuggestInteractionProfileBindings", N.SuggestInteractionProfileBindings);
    get("xrAttachSessionActionSets", N.AttachSessionActionSets); get("xrSyncActions", N.SyncActions);
    get("xrGetActionStateBoolean", N.GetActionStateBoolean); get("xrGetActionStateFloat", N.GetActionStateFloat);
    get("xrGetActionStateVector2f", N.GetActionStateVector2f);
    get("xrWaitFrame", N.WaitFrame); get("xrEndFrame", N.EndFrame);
    get("xrStringToPath", N.StringToPath); get("xrPathToString", N.PathToString);
    get("xrCreateActionSpace", N.CreateActionSpace); get("xrCreateReferenceSpace", N.CreateReferenceSpace);
    get("xrDestroySpace", N.DestroySpace); get("xrLocateSpace", N.LocateSpace);
    get("xrApplyHapticFeedback", N.ApplyHapticFeedback);
    get("xrEnumerateSwapchainFormats", gSwap.EnumerateSwapchainFormats); get("xrCreateSwapchain", gSwap.CreateSwapchain);
    get("xrDestroySwapchain", gSwap.DestroySwapchain); get("xrEnumerateSwapchainImages", gSwap.EnumerateSwapchainImages);
    get("xrAcquireSwapchainImage", gSwap.AcquireSwapchainImage); get("xrWaitSwapchainImage", gSwap.WaitSwapchainImage);
    get("xrReleaseSwapchainImage", gSwap.ReleaseSwapchainImage);
    Log("instance created for '%s' (%s)", info->applicationInfo.applicationName, ExeName().c_str());
    return r;
}

extern "C" __declspec(dllexport) XrResult XRAPI_CALL xrNegotiateLoaderApiLayerInterface(
    const XrNegotiateLoaderInfo* li, const char* layerName, XrNegotiateApiLayerRequest* req) {
    if (!li || !req || li->structType != XR_LOADER_INTERFACE_STRUCT_LOADER_INFO ||
        req->structType != XR_LOADER_INTERFACE_STRUCT_API_LAYER_REQUEST ||
        li->minInterfaceVersion > XR_CURRENT_LOADER_API_LAYER_VERSION || li->maxInterfaceVersion < XR_CURRENT_LOADER_API_LAYER_VERSION)
        return XR_ERROR_INITIALIZATION_FAILED;
    if (layerName && strcmp(layerName, LAYER_NAME) != 0) return XR_ERROR_INITIALIZATION_FAILED;
    req->layerInterfaceVersion = XR_CURRENT_LOADER_API_LAYER_VERSION;
    req->layerApiVersion = XR_CURRENT_API_VERSION;
    req->getInstanceProcAddr = Hook_xrGetInstanceProcAddr;
    req->createApiLayerInstance = Hook_xrCreateApiLayerInstance;
    return XR_SUCCESS;
}
