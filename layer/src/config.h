#pragma once
#include "common.h"
#include <map>
#include <memory>

// Physical controller parts. BTN1 = A (right) / X (left), BTN2 = B / Y.
enum Comp { C_BTN1, C_BTN2, C_TRIGGER, C_GRIP, C_STICK, C_THUMBREST, C_MENU, C_COUNT };
// How the part is read. PRESS = click or analog over threshold.
enum Feat { F_PRESS, F_TOUCH, F_UP, F_DOWN, F_LEFT, F_RIGHT, F_COUNT };

struct InputId {
    int hand = -1, comp = -1, feat = F_PRESS;
    bool valid() const { return hand >= 0 && comp >= 0; }
    bool operator==(const InputId& o) const { return hand == o.hand && comp == o.comp && feat == o.feat; }
};
bool ParseInput(const std::string& s, InputId& out);
std::string InputName(const InputId& i);
int ParseCompName(const std::string& s); // "trigger", "primary", ... -> Comp or -1

enum class ActType { None, Key, Sequence, XrInput, Wheel, Back, Close, Layer, Reload, Keyboard, Page, Adjust };

struct Action {
    ActType type = ActType::None;
    std::string keys;          // key: "Ctrl+S"
    std::string mode = "tap";  // key/xr_input: tap|hold|toggle ; layer: hold|toggle|on|off
    int tapMs = 60;
    std::string steps;         // sequence: "Ctrl+C; wait 100; down W; up W"
    std::string wheel;         // wheel: name
    std::string confirm = "auto"; // wheel: auto|release|stick_return|button
    std::string layer;         // layer: name
    std::string page;          // page: wrist panel page name
    InputId target;            // xr_input: game input to fake
};

struct Binding {
    std::string name;
    bool enabled = true;
    std::vector<InputId> chord; // last entry is the trigger, the rest must be held
    std::string chordText;
    std::string gesture = "press"; // press|tap|hold|double_tap|while_held
    int holdMs = 400, tapMs = 300, doubleTapMs = 350;
    std::vector<std::string> layers, notLayers;
    std::string block = "trigger"; // none|trigger|all
    bool tapPassthrough = false;
    Action action;
};

struct WheelItem {
    std::string label, description, icon;
    bool hasColor = false; uint32_t color = 0;
    Action action;
};
struct Wheel { std::string name, title; std::vector<WheelItem> items; };

struct Style {
    std::string anchor = "hand_locked"; // hand|hand_locked|head|head_locked
    float radius = 0.12f, distance = 0.45f, headDown = 0.10f;
    float handUp = 0.10f, handToward = 0.06f;
    float innerRatio = 0.34f, gapDeg = 1.5f;
    std::string font = "Segoe UI";
    float fontScale = 1.f;
    int texSize = 768;
    uint32_t bg = 0xB0101418, segment = 0xD0283040, hover = 0xF03C8CFF, text = 0xFFFFFFFF, accent = 0xFF7FC4FF, border = 0x60FFFFFF;
    uint32_t toggled = 0xE02E9E5B; // buttons whose toggle (key, game input or layer) is currently on
};

struct Selection {
    std::string method = "thumbstick"; // thumbstick|point|both
    std::string hand = "opener";       // opener|left|right
    float deadzone = 0.5f;
    int confirmComp = C_TRIGGER;       // used when confirm = button
    int cancelComp = C_BTN2;
    int submenuDwellMs = 450;          // only for release-confirm wheels
    bool closeAfterSelect = true;
    int autoCloseMs = 0;
};

struct KbStyle {
    float width = 0.56f;     // metres
    float distance = 0.42f;  // in front of the head when opened
    float below = 0.30f;     // below eye level
    bool closeOnEnter = false;
};

struct WristStyle {
    bool enabled = false;
    bool mirror = false;            // show the other forearm's pages instead of its own
    std::map<std::string, Wheel> pages; // the panel's own pages (title + items)
    std::string start = "main";     // first page shown
    std::string face = "inner";     // inner = palm side / underside of the forearm, back = watch side
    float size = 0.11f;             // panel width and height in metres
    float towardElbow = 0.13f;      // from the controller grip back along the forearm
    std::string textUp = "fingers"; // fingers = top of the text toward the fingers (hand held up, palm to face)
                                    // custom = rotate_deg from along_arm (set by turning it in adjust mode)
                                    // between = halfway (45 degrees) between fingers and along_arm (arm half raised)
                                    // along_arm = text runs along the forearm (arm held across the body)
    float lift = 0.025f;            // off the skin
    float side = 0.f;               // sideways across the forearm (set by adjust mode)
    float rotateDeg = 0.f;          // text_up = custom: turn of the text from along_arm, degrees
    int columns = 3;
    float facingDeg = 35.f;         // how squarely the panel must face the eyes
    float gazeDeg = 20.f;           // how close to the centre of view it must be
    int showMs = 250, hideMs = 350;
    std::string press = "both";     // poke|point|both
};

struct Profile {
    std::string sourceName;
    bool enabled = true;
    std::string blockWhileOpen = "hand"; // none|minimal|hand|all
    float pressOn = 0.6f, pressOff = 0.4f, stickOn = 0.7f, stickOff = 0.5f;
    bool haptics = true; float hapticAmp = 0.35f;
    bool notifications = true;
    bool focusWindow = false;
    Style style;
    Selection sel;
    KbStyle kb;
    WristStyle wrist[2];            // [0] left forearm, [1] right forearm; each is pressed by the other hand
    // The panel whose pages a forearm shows (either one can mirror the other, not both).
    const WristStyle& WristPages(int h) const { return wrist[h].mirror ? wrist[1 - h] : wrist[h]; }
    std::vector<Binding> bindings;
    std::map<std::string, Wheel> wheels;
};

// Loads <exe>.json from the profiles folder, else default.json (written with a sample if missing).
std::shared_ptr<Profile> LoadProfile(std::wstring& pathOut, std::string& err);
const char* DefaultProfileJson();
// Adjust mode: write the wheel and wrist panel placement of p back into its profile file.
bool SavePlacement(const Profile& p, std::string& err);
std::shared_ptr<Profile> ParseProfileText(const std::string& text, std::string& err);
