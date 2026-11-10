#include <sstream>
#include <fstream>
#include "../src/panel.h"
#include "../src/keys.h"
#include <cstdio>
#include <objidl.h>
#include <algorithm>
namespace Gdiplus { using std::min; using std::max; }
#include <gdiplus.h>
using namespace Gdiplus;

static int fails = 0;
#define CHECK(c, msg) do { if (!(c)) { printf("FAIL: %s\n", msg); fails++; } else printf("ok:   %s\n", msg); } while (0)

static void SavePng(const std::vector<uint32_t>& px, int w, int h, const wchar_t* path) {
    Bitmap bmp(w, h, w * 4, PixelFormat32bppPARGB, (BYTE*)px.data());
    CLSID png; UINT n = 0, sz = 0; GetImageEncodersSize(&n, &sz);
    std::vector<BYTE> buf(sz); auto* enc = (ImageCodecInfo*)buf.data(); GetImageEncoders(n, sz, enc);
    for (UINT i = 0; i < n; i++) if (wcscmp(enc[i].MimeType, L"image/png") == 0) png = enc[i].Clsid;
    bmp.Save(path, &png);
}

int main(int argc, char** argv) {
    // Always the app's built-in default profile, never the user's own bindings.
    std::string err;
    auto p = ParseProfileText(DefaultProfileJson(), err);
    CHECK(p != nullptr, ("built-in default profile parses " + err).c_str());
    if (!p) return 1;
    p->notifications = true;
    p->sel.method = "thumbstick"; // the tests drive the stick, whatever the user picked
    p->blockWhileOpen = "hand";
    {
        for (auto& b : p->bindings) if (b.action.type == ActType::Wheel) b.enabled = false;
        Binding ob; ob.name = "test wheel"; ob.chordText = "right.thumbrest";
        InputId r; ParseInput("right.thumbrest", r); ob.chord.push_back(r);
        ob.gesture = "double_tap"; ob.doubleTapMs = 400; ob.block = "none";
        ob.action.type = ActType::Wheel; ob.action.wheel = "right"; ob.action.confirm = "stick_return";
        p->bindings.insert(p->bindings.begin(), ob);
    }
    Engine e; e.SetProfile(p);
    Frame f; double t = 0;
    f.headValid = true; f.head.position = { 0, 1.6f, 0 };
    f.h[1].gripValid = true; f.h[1].gripPose.position = { 0.2f, 1.1f, -0.3f };
    auto step = [&](int frames = 1) { for (int i = 0; i < frames; i++) { t += 0.011; f.t = t; e.Update(f); } };

    step(5);
    // double tap the right thumbrest
    f.h[1].rest = true; step(5); f.h[1].rest = false; step(5);
    CHECK(!e.view.visible, "single thumbrest tap does not open");
    f.h[1].rest = true; step(5);
    CHECK(e.view.visible, "double tap thumbrest opens wheel");
    CHECK(e.Blocked(1, C_STICK) && e.Blocked(1, C_TRIGGER), "right hand blocked while open");
    CHECK(!e.Blocked(0, C_TRIGGER), "left hand free while open");
    f.h[1].rest = false; step(3);
    CHECK(e.view.visible, "lifting the thumb keeps stick_return wheel open");
    // hover item 5 (More..., 6 items, up-left = 300 deg)
    int moreIdx = -1, nItems = (int)p->wheels["right"].items.size();
    for (int i = 0; i < nItems; i++) if (p->wheels["right"].items[i].action.type == ActType::Wheel) moreIdx = i;
    float ang = moreIdx * 6.2831853f / nItems;
    f.h[1].sx = std::sin(ang) * 0.95f; f.h[1].sy = std::cos(ang) * 0.95f; step(3);
    CHECK(moreIdx >= 0 && e.view.hovered == moreIdx, "stick toward the submenu item hovers it");
    std::vector<uint32_t> px;
    DrawWheel(px, p->style.texSize, *p, e.view);
    SavePng(px, p->style.texSize, p->style.texSize, L"wheel_main.png");
    f.h[1].sx = 0; f.h[1].sy = 0; step(3);
    CHECK(e.view.visible && e.view.wheel && e.view.wheel->name == "Right hand more (sub-wheel)", "stick return on submenu pushes 'more'");
    {
        auto& mw = p->wheels["Right hand more (sub-wheel)"].items; int bi = 0;
        for (int i = 0; i < (int)mw.size(); i++) if (mw[i].action.type == ActType::Back) bi = i;
        float a2 = bi * 6.2831853f / mw.size();
        f.h[1].sx = std::sin(a2) * 0.95f; f.h[1].sy = std::cos(a2) * 0.95f; step(3);
    }
    DrawWheel(px, p->style.texSize, *p, e.view);
    SavePng(px, p->style.texSize, p->style.texSize, L"wheel_more.png");
    f.h[1].sx = 0; f.h[1].sy = 0; step(3);
    CHECK(e.view.visible && e.view.wheel->name == "right", "selecting Back pops to the first wheel");
    f.h[1].b2 = true; step(2); f.h[1].b2 = false; step(2);
    CHECK(!e.view.visible, "B cancels wheel");
    CHECK(e.Blocked(1, C_BTN2) == false, "block released after B let go");

    // shift layer chord: left grip + left stick click, then A blocked & fires (A in layer)
    {   // the test brings its own layer binding, so the user's edits to default.json cannot break it
        Binding lb; lb.name = "test shift"; lb.chordText = "left.grip+left.thumbstick.click";
        InputId a, b; ParseInput("left.grip", a); ParseInput("left.thumbstick.click", b);
        lb.chord = { a, b }; lb.gesture = "while_held"; lb.block = "trigger";
        lb.action.type = ActType::Layer; lb.action.mode = "hold"; lb.action.layer = "testshift";
        p->bindings.push_back(lb);
        e.SetProfile(p);
    }
    f.h[0].grip = 1.f; step(3);
    CHECK(!e.Blocked(0, C_GRIP), "grip alone passes to game");
    f.h[0].sc = true; step(3);
    CHECK(e.Blocked(0, C_STICK), "layer trigger (stick click) blocked from game");
    CHECK(!e.Blocked(1, C_BTN1), "A not blocked before press");
    f.h[0].sc = false; step(3);
    f.h[0].grip = 0; step(10);
    CHECK(!e.Blocked(0, C_STICK), "stick unblocked after release");

    // ---------------- keyboard
    {
        Binding kbB; kbB.name = "kb"; kbB.chordText = "left.menu";
        InputId m; ParseInput("left.menu", m); kbB.chord.push_back(m);
        kbB.gesture = "press"; kbB.action.type = ActType::Keyboard;
        p->bindings.push_back(kbB);
        e.SetProfile(p);
        f.h[0].menu = true; step(2); f.h[0].menu = false; step(2);
        CHECK(e.kb.visible, "left menu opens keyboard");
        CHECK(e.Blocked(0, C_TRIGGER) && e.Blocked(1, C_TRIGGER), "both triggers hidden from game while typing");
        float uw, uh; const auto& keys = KbLayout(uw, uh);
        auto aimAt = [&](int hand, int key) {
            const KbKey& k = keys[key];
            float u = k.x + k.w / 2, v = k.y + k.h / 2;
            Vec3 local{ (u / uw - 0.5f) * e.kb.widthM, (0.5f - v / uh) * e.kb.heightM, 0 };
            Vec3 target = V(e.kb.pose.position) + Rotate(e.kb.pose.orientation, local);
            Vec3 from{ hand ? 0.15f : -0.15f, 1.25f, -0.15f };
            f.h[hand].aimValid = true;
            f.h[hand].aimPose.position = X(from);
            f.h[hand].aimPose.orientation = BillboardTo(target, from);
        };
        {
            int fk = 0; for (auto& k : keys) if (k.combo.size() >= 2 && k.combo[0] == 'F' && isdigit((unsigned char)k.combo[1])) fk++;
            CHECK(fk == 12 && keys[0].combo == "F1" && keys[11].combo == "F12", "keyboard has an F1-F12 row on top");
        }
        int shiftKey = -1, qKey = -1, closeKey = -1;
        for (int i = 0; i < (int)keys.size(); i++) {
            if (keys[i].kind == KB_SHIFT && shiftKey < 0) shiftKey = i;
            if (keys[i].combo == "q") qKey = i;
            if (keys[i].kind == KB_CLOSE) closeKey = i;
        }
        aimAt(1, shiftKey); aimAt(0, qKey); step(30);
        CHECK(e.kb.hover[1] == shiftKey, "right ray hovers Shift");
        CHECK(e.kb.hover[0] == qKey, "left ray hovers Q");
        f.h[1].trig = 1; step(2); f.h[1].trig = 0; step(2);
        CHECK(e.kb.shift, "trigger on Shift latches shift");
        std::vector<uint32_t> kpx; int KW = 1500, KH = (int)(KW * uh / uw);
        DrawKeyboard(kpx, KW, KH, e.kb, p->style);
        SavePng(kpx, KW, KH, L"keyboard.png");
        aimAt(1, closeKey); step(3);
        f.h[1].trig = 1; step(2); f.h[1].trig = 0; step(2);
        CHECK(!e.kb.visible, "Close key closes keyboard");
        f.h[0].menu = true; step(2); f.h[0].menu = false; step(2);
        f.h[1].b2 = true; step(2); f.h[1].b2 = false; step(2);
        CHECK(!e.kb.visible, "B closes keyboard");
    }

    // ---------------- wrist panel (left inner forearm, poked by the right hand)
    {
        Wheel tw; tw.name = "wristtest"; tw.title = "Wrist";
        const char* labels[] = { "Map", "Inventory", "Keyboard", "Save", "More" };
        for (int i = 0; i < 5; i++) {
            WheelItem it; it.label = labels[i];
            it.action.type = i == 2 ? ActType::Keyboard : ActType::Key; it.action.keys = "F13";
            if (i == 4) { it.action.type = ActType::Page; it.action.page = "second"; }
            tw.items.push_back(it);
        }
        Wheel second; second.name = "second"; second.title = "Second";
        { WheelItem it; it.label = "Esc"; it.action.type = ActType::Key; it.action.keys = "F13"; second.items.push_back(it); }
        p->wrist[0].pages["wristtest"] = tw; p->wrist[0].pages["second"] = second;
        p->wrist[0].enabled = true; p->wrist[0].start = "wristtest";
        p->wrist[0].face = "inner"; p->wrist[0].press = "both";
        e.SetProfile(p);
        f.head = XrPosef{ {0,0,0,1}, {0, 1.6f, 0} }; // looking straight ahead (-Z)
        // Left grip turned so the palm side faces the eyes, 0.4 m in front.
        f.h[0].gripValid = true;
        f.h[0].gripPose = XrPosef{ {0, -0.70710678f, 0, 0.70710678f}, {0, 1.6f, -0.4f} };
        f.h[1].aimValid = true; f.h[1].aimPose = XrPosef{ {0,0,0,1}, {0.3f, 1.2f, -0.3f} }; // right hand away
        step(5);
        CHECK(!e.wrist[0].visible, "wrist panel waits for the look to last");
        step(30);
        CHECK(e.wrist[0].visible, "turned wrist + look shows the wrist panel");
        std::vector<uint32_t> wpx; DrawWrist(wpx, 512, *p, p->wrist[0], e.wrist[0]);
        SavePng(wpx, 512, 512, L"wrist_panel.png");

        // Poke the third cell (Keyboard) from the front.
        std::vector<Cell> cells; Cell hd; WristLayout(5, p->wrist[0].columns, cells, hd);
        const Cell& c = cells[2];
        float u = c.x + c.w / 2, v = c.y + c.h / 2;
        auto tipAt = [&](float depth) {
            Vec3 l{ (u - 0.5f) * p->wrist[0].size, (0.5f - v) * p->wrist[0].size, depth + 0.015f };
            f.h[1].aimPose.orientation = e.wrist[0].pose.orientation; // pointing into the panel
            f.h[1].aimPose.position = X(V(e.wrist[0].pose.position) + Rotate(e.wrist[0].pose.orientation, l));
        };
        tipAt(0.03f); step(30);
        CHECK(e.wrist[0].hover == 2, "finger in front of a button hovers it");
        CHECK(!e.kb.visible, "hovering alone does not press");
        CHECK(e.wrist[0].cursor && e.wrist[0].cursorLevel > 0 && e.wrist[0].cursorLevel < 8, "touch cursor shows while the finger is close");
        {
            Vec3 lc = RotateInv(e.wrist[0].pose.orientation, V(e.wrist[0].cursorPose.position) - V(e.wrist[0].pose.position));
            CHECK(std::fabs(lc.x - (u - 0.5f) * p->wrist[0].size) < 0.002f && std::fabs(lc.y - (0.5f - v) * p->wrist[0].size) < 0.002f, "touch cursor sits under the fingertip");
            float sizeFar = e.wrist[0].cursorSize; tipAt(0.005f); step(1);
            CHECK(e.wrist[0].cursorSize < sizeFar && e.wrist[0].cursorLevel >= 7, "touch cursor shrinks as the finger closes in");
            std::vector<uint32_t> cpx; DrawCursor(cpx, 128, 4, p->style); SavePng(cpx, 128, 128, L"cursor_near.png");
            DrawCursor(cpx, 128, 8, p->style); SavePng(cpx, 128, 128, L"cursor_touch.png");
        }
        tipAt(-0.01f); step(2);
        CHECK(e.kb.visible, "poking through the button runs its action (keyboard)");
        CHECK(!e.wrist[0].visible, "wrist panel steps aside for the keyboard");
        f.h[1].b2 = true; step(2); f.h[1].b2 = false; step(2);

        // Laser at the More cell (index 4) + trigger opens the second page with a Back cell.
        step(40);
        {
            const Cell& m = cells[4];
            Vec3 l{ (m.x + m.w / 2 - 0.5f) * p->wrist[0].size, (0.5f - (m.y + m.h / 2)) * p->wrist[0].size, 0.25f };
            f.h[1].aimPose.orientation = e.wrist[0].pose.orientation;
            f.h[1].aimPose.position = X(V(e.wrist[0].pose.position) + Rotate(e.wrist[0].pose.orientation, l));
            step(3);
            f.h[1].trig = 1; step(2); f.h[1].trig = 0; step(3);
            CHECK(e.wrist[0].visible && e.wrist[0].wheel && e.wrist[0].wheel->name == "second" && e.wrist[0].nested, "page button opens its page, with Back");
        }

        // Looking away hides it; the back of the wrist does not show the inner panel.
        f.h[1].aimPose.position = { 0.3f, 1.2f, -0.3f };
        step(40);
        CHECK(e.wrist[0].visible, "panel back after the keyboard closes");
        f.head.orientation = { 0, 0.5f, 0, 0.8660254f }; // head turned 60 degrees left
        step(50);
        CHECK(!e.wrist[0].visible, "looking away hides the wrist panel");
        f.head.orientation = { 0, 0, 0, 1 };
        f.h[0].gripPose.orientation = { 0, 0.70710678f, 0, 0.70710678f }; // palm facing away
        step(50);
        CHECK(!e.wrist[0].visible, "arm turned the other way keeps it hidden");

        // Right forearm, mirroring the left panel's pages.
        p->wrist[1] = p->wrist[0]; p->wrist[1].pages.clear(); p->wrist[1].mirror = true;
        e.SetProfile(p);
        f.h[0].gripValid = false;
        f.h[0].aimValid = true; f.h[0].aimPose = XrPosef{ {0,0,0,1}, {-0.3f, 1.2f, -0.3f} };
        f.h[1].gripValid = true;
        f.h[1].gripPose = XrPosef{ {0, 0.70710678f, 0, 0.70710678f}, {0, 1.6f, -0.4f} }; // palm side toward the eyes
        step(40);
        CHECK(e.wrist[1].visible && e.wrist[1].wheel && e.wrist[1].wheel->name == "wristtest", "right forearm shows the mirrored left pages");
        CHECK(!e.wrist[0].visible, "left panel stays hidden meanwhile");

        // Right forearm with its own pages.
        Wheel own; own.name = "rmain"; own.title = "Right";
        { WheelItem it; it.label = "Jump"; it.action.type = ActType::Key; it.action.keys = "F13"; own.items.push_back(it); }
        p->wrist[1].mirror = false; p->wrist[1].pages["rmain"] = own; p->wrist[1].start = "rmain";
        e.SetProfile(p);
        step(40);
        CHECK(e.wrist[1].visible && e.wrist[1].wheel && e.wrist[1].wheel->name == "rmain", "right forearm shows its own pages");
        std::vector<uint32_t> rpx; DrawWrist(rpx, 512, *p, p->wrist[1], e.wrist[1]);
        SavePng(rpx, 512, 512, L"wrist_right.png");

        // Left forearm mirroring the right panel's own pages.
        p->wrist[0].mirror = true;
        e.SetProfile(p);
        f.h[1].gripValid = false;
        f.h[1].aimValid = true; f.h[1].aimPose = XrPosef{ {0,0,0,1}, {0.3f, 1.2f, -0.3f} };
        f.h[0].gripValid = true;
        f.h[0].gripPose = XrPosef{ {0, -0.70710678f, 0, 0.70710678f}, {0, 1.6f, -0.4f} };
        step(40);
        CHECK(e.wrist[0].visible && e.wrist[0].wheel && e.wrist[0].wheel->name == "rmain", "left forearm can mirror the right panel");
    }

    // Index-style opener: double touch of B. Pressing B twice fast (looting) must not open it.
    {
        auto q = ParseProfileText(DefaultProfileJson(), err);
        for (auto& b : q->bindings) if (b.action.type == ActType::Wheel) b.enabled = false;
        Binding ob; ob.name = "touch wheel"; ob.chordText = "right.secondary.touch";
        InputId r; ParseInput("right.secondary.touch", r); ob.chord.push_back(r);
        ob.gesture = "double_tap"; ob.doubleTapMs = 400; ob.block = "none";
        ob.action.type = ActType::Wheel; ob.action.wheel = "right"; ob.action.confirm = "stick_return";
        q->bindings.insert(q->bindings.begin(), ob);
        Engine e; e.SetProfile(q);
        Frame f; double t = 0;
        f.headValid = true; f.head.position = { 0, 1.6f, 0 };
        f.h[1].gripValid = true; f.h[1].gripPose.position = { 0.2f, 1.1f, -0.3f };
        auto step = [&](int frames = 1) { for (int i = 0; i < frames; i++) { t += 0.011; f.t = t; e.Update(f); } };
        step(5);
        for (int k = 0; k < 2; k++) {   // two quick real presses: touch first, then click
            f.h[1].b2t = true; step(1); f.h[1].b2 = true; step(3); f.h[1].b2 = false; f.h[1].b2t = false; step(4);
        }
        CHECK(!e.view.visible, "pressing B twice fast does not open the double-touch wheel");
        step(60);
        f.h[1].b2t = true; step(4); f.h[1].b2t = false; step(4); f.h[1].b2t = true; step(4);
        CHECK(e.view.visible, "touching B twice without pressing opens the wheel");
    }

    // Adjust mode: grab a wrist panel / the open wheel with the grip, move and resize it, then save.
    {
        auto q = ParseProfileText(DefaultProfileJson(), err);
        Binding ab; ab.name = "adjust"; ab.chordText = "left.x";
        InputId ai; ParseInput("left.x", ai); ab.chord.push_back(ai);
        ab.gesture = "press"; ab.block = "trigger"; ab.action.type = ActType::Adjust;
        q->bindings.push_back(ab);
        float elbow0 = q->wrist[0].towardElbow, size0 = q->wrist[0].size, up0 = q->style.handUp, rad0 = q->style.radius;
        Engine e; e.SetProfile(q);
        Frame f; double t = 0;
        f.headValid = true; f.head = XrPosef{ {0,0,0,1}, {0, 1.6f, 0} };
        // Left arm down and away from the face: normally no panel.
        f.h[0].gripValid = true; f.h[0].gripPose = XrPosef{ {0,0,0,1}, {-0.3f, 1.0f, -0.2f} };
        f.h[1].gripValid = f.h[1].aimValid = true; f.h[1].gripPose = XrPosef{ {0,0,0,1}, {0.3f, 1.0f, -0.2f} }; f.h[1].aimPose = f.h[1].gripPose;
        auto step = [&](int frames = 1) { for (int i = 0; i < frames; i++) { t += 0.011; f.t = t; e.Update(f); } };
        step(30);
        CHECK(!e.wrist[0].visible, "adjust: panel hidden before adjust mode");
        f.h[0].b1 = true; step(2); f.h[0].b1 = false; step(3);
        CHECK(e.adjust && e.wrist[0].visible && e.wrist[0].adjust, "adjust: mode on shows the wrist panel without looking at it");
        // Right grip grabs the left panel and moves 4 cm along +Z of the left grip (toward the elbow).
        Vec3 pc = V(e.wrist[0].pose.position);
        f.h[1].gripPose.position = X(pc + Vec3{ 0.01f, 0, 0 }); f.h[1].aimPose = f.h[1].gripPose; step(2);
        f.h[1].grip = 1; step(2);
        CHECK(e.Blocked(1, C_GRIP), "adjust: the grabbing grip is hidden from the game");
        f.h[1].gripPose.position = X(V(f.h[1].gripPose.position) + Vec3{ 0, 0, 0.04f }); f.h[1].aimPose = f.h[1].gripPose;
        f.h[1].sy = 1; step(10); f.h[1].sy = 0;
        Vec3 up0v = Rotate(e.wrist[0].pose.orientation, { 0, 1, 0 });
        f.h[1].sx = 1; step(20); f.h[1].sx = 0; // about 20 degrees
        Vec3 up1v = Rotate(e.wrist[0].pose.orientation, { 0, 1, 0 });
        float turned = std::acos(std::clamp(Dot(up0v, up1v), -1.f, 1.f)) * 57.2958f;
        CHECK(q->wrist[0].textUp == "custom" && turned > 12.f && turned < 30.f, ("adjust: stick sideways turns the panel (custom), turned " + std::to_string(turned)).c_str());
        CHECK(std::fabs(q->wrist[0].towardElbow - (elbow0 + 0.04f)) < 0.003f, "adjust: dragging moves the panel along the forearm");
        CHECK(q->wrist[0].size > size0, "adjust: stick up while holding makes the panel bigger");
        CHECK(std::fabs(V(e.wrist[0].pose.position).z - (pc.z + 0.04f)) < 0.003f, "adjust: the panel follows the hand");
        f.h[1].grip = 0; step(3);
        // Open the right wheel (double tap thumbrest), grab it with the left grip, lift it 5 cm.
        f.h[1].gripPose.position = { 0.3f, 1.0f, -0.3f };
        step(70);
        CHECK(e.view.visible && e.wrist[0].visible, "adjust: the main wheel shows by itself, next to the wrist panels");
        Vec3 wc = V(e.view.pose.position);
        f.h[0].gripPose.position = X(wc); step(2);
        f.h[0].grip = 1; step(2);
        f.h[0].gripPose.position = X(wc + Vec3{ 0, 0.05f, 0 }); f.h[0].sy = -1; step(10); f.h[0].sy = 0;
        CHECK(std::fabs(q->style.handUp - (up0 + 0.05f)) < 0.004f, "adjust: dragging the wheel up raises it above the hand");
        CHECK(q->style.radius < rad0, "adjust: stick down while holding makes the wheel smaller");
        f.h[0].grip = 0; step(3);
        f.h[0].b1 = true; step(2); f.h[0].b1 = false; step(3);
        CHECK(!e.adjust && e.saveRequested && !e.view.visible, "adjust: using it again leaves adjust mode, closes the wheel and asks to save");
    }

    // Adjust mode saving: write the placement into a (temporary) profile file and read it back.
    {
        std::wstring path = ProfilesDir() + L"\\vro_save_test.json";
        { std::ofstream o(path, std::ios::binary); o << DefaultProfileJson(); }
        auto q = ParseProfileText(DefaultProfileJson(), err);
        q->sourceName = "vro_save_test.json";
        q->style.handUp = 0.17f; q->style.radius = 0.15f; q->wrist[0].towardElbow = 0.2f; q->wrist[0].side = -0.01f; q->wrist[1].size = 0.13f;
        q->wrist[0].textUp = "custom"; q->wrist[0].rotateDeg = 33.f;
        std::string serr;
        bool ok = SavePlacement(*q, serr);
        std::ifstream in(path, std::ios::binary); std::stringstream ss; ss << in.rdbuf(); in.close();
        auto back = ParseProfileText(ss.str(), err);
        CHECK(ok && back && std::fabs(back->style.handUp - 0.17f) < 1e-4f && std::fabs(back->style.radius - 0.15f) < 1e-4f
              && std::fabs(back->wrist[0].towardElbow - 0.2f) < 1e-4f && std::fabs(back->wrist[0].side + 0.01f) < 1e-4f
              && std::fabs(back->wrist[1].size - 0.13f) < 1e-4f && back->wrist[0].textUp == "custom" && std::fabs(back->wrist[0].rotateDeg - 33.f) < 0.1f && back->wheels.size() == q->wheels.size(),
              ("adjust: positions are saved to the profile file and the rest is kept " + serr).c_str());
        DeleteFileW(path.c_str());
    }

    printf("\n%d failures\n", fails);
    Keys::Shutdown();
    DrawShutdown();
    return fails;
}
