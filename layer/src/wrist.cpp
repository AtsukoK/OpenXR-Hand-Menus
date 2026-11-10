// Wrist panels: a grid of buttons on each forearm. A panel only shows when that arm is turned so
// the panel faces the eyes AND the user is looking at it, held for a moment. The other hand
// presses by poking it (small movement) or by laser + trigger. The right panel can mirror the
// left panel's pages or have its own.
#include "engine.h"
#include <algorithm>

static constexpr float kDeg = 3.14159265f / 180.f;

void WristLayout(int n, int cols, std::vector<Cell>& out, Cell& header) {
    out.clear();
    const float pad = 0.03f, gap = 0.02f, headH = 0.14f;
    header = { pad, pad, 1 - 2 * pad, headH };
    if (n <= 0) return;
    cols = std::clamp(std::min(cols, n), 1, 4);
    int rows = (n + cols - 1) / cols;
    float top = pad + headH + gap;
    float availW = 1 - 2 * pad, availH = 1 - top - pad;
    float cw = (availW - gap * (cols - 1)) / cols;
    float ch = (availH - gap * (rows - 1)) / rows;
    float c = std::min(cw, ch * 1.25f); // cells a little wider than tall at most
    ch = std::min(ch, c);
    float gridW = c * cols + gap * (cols - 1);
    float left = 0.5f - gridW / 2;
    for (int i = 0; i < n; i++) {
        int r = i / cols, k = i % cols;
        out.push_back({ left + k * (c + gap), top + r * (ch + gap), c, ch });
    }
}

bool Engine::WristPoseNow(int h, XrPosef& pose, Vec3& n) const {
    const WristStyle& W = prof->wrist[h];
    const HandRaw& hr = fr.h[h];
    if (!hr.gripValid) return false;
    const XrQuaternionf& q = hr.gripPose.orientation;
    Vec3 ax = Rotate(q, { 1, 0, 0 }), Z = Rotate(q, { 0, 0, 1 });
    // OpenXR grip space: +X points out of the palm on the left hand and into it on the right.
    Vec3 palmOut = h == 0 ? ax : ax * -1.f;
    n = W.face == "inner" ? palmOut : palmOut * -1.f;
    // fingers: top of the text toward the fingers (hand held up, palm toward the face).
    // along_arm: text runs along the forearm, left to right as seen with the arm held across the body.
    // between: halfway between the two, for an arm held half raised.
    Vec3 yFingers = Z * -1.f;
    Vec3 xAlong = h == 0 ? Z * -1.f : Z;
    Vec3 yAlong = Cross(n, xAlong);
    Vec3 y = W.textUp == "along_arm" ? yAlong : W.textUp == "between" ? Norm(yFingers + yAlong) : yFingers;
    Vec3 x = Cross(y, n);
    Vec3 c = V(hr.gripPose.position) + Z * W.towardElbow + n * W.lift + Norm(Cross(Z, n)) * W.side;
    pose.position = X(c);
    pose.orientation = QuatFromBasis(x, y, n);
    return true;
}

int Engine::WristCellAt(int h, float u, float v) const {
    const WristView& wv = wrist[h];
    if (!wv.wheel) return -1;
    std::vector<Cell> cells; Cell hd;
    WristLayout((int)wv.wheel->items.size() + (wv.nested ? 1 : 0), prof->wrist[h].columns, cells, hd);
    for (int i = 0; i < (int)cells.size(); i++) {
        const Cell& c = cells[i];
        if (u >= c.x && u < c.x + c.w && v >= c.y && v < c.y + c.h) return i;
    }
    return -1;
}

void Engine::WristActivate(int h, int cell, int presser) {
    WristView& wv = wrist[h]; WristRun& R = wrun[h];
    if (prof->haptics) haptics.push_back({ presser, std::min(1.f, prof->hapticAmp * 1.6f), 0.03f });
    wv.pressed = cell; R.pressFlashUntil = now + 0.15; wv.version++;
    int idx = cell - (wv.nested ? 1 : 0);
    if (idx < 0) { // the automatic Back cell
        if (R.stack.size() > 1) R.stack.pop_back();
        wv.hover = -1; return;
    }
    const WheelItem& it = wv.wheel->items[idx];
    const Action& a = it.action;
    Log("%s wrist '%s'", h ? "right" : "left", it.label.c_str());
    switch (a.type) {
    case ActType::Page: {
        const auto& pages = prof->WristPages(h).pages;
        auto w = pages.find(a.page);
        if (w != pages.end()) { R.stack.push_back(&w->second); wv.hover = -1; }
        return;
    }
    case ActType::Wheel: // a real radial wheel; the panels step aside while it is open
        RunAction(a, presser, -1, false);
        return;
    case ActType::Back:
        if (R.stack.size() > 1) R.stack.pop_back();
        else R.closedByItem = true;
        wv.hover = -1; return;
    case ActType::Close:
        R.closedByItem = true; return;
    default:
        RunAction(a, presser, -1, true);
    }
}

void Engine::UpdateWrist(int h) {
    const WristStyle& W = prof->wrist[h];
    const WristStyle& src = prof->WristPages(h); // whose pages are shown
    WristView& wv = wrist[h]; WristRun& R = wrun[h];
    auto hide = [&] {
        if (grab.target == h) grab = GrabRun{};
        if (wv.visible) { wv.visible = false; wv.hover = -1; wv.version++; Log("%s wrist panel hidden", h ? "right" : "left"); }
    };
    auto root = src.pages.find(src.start);
    if (!W.enabled || root == src.pages.end() || wr.open || kb.visible || !fr.headValid) {
        hide(); R.condSince = -1; return;
    }
    XrPosef pose; Vec3 n;
    if (!WristPoseNow(h, pose, n)) { hide(); R.condSince = -1; return; }

    // Is the panel turned toward the eyes and being looked at? Looser once shown, so it does not flicker.
    Vec3 eye = V(fr.head.position), c = V(pose.position);
    Vec3 toEye = eye - c; float dist = Len(toEye); toEye = Norm(toEye);
    Vec3 fwd = Rotate(fr.head.orientation, { 0, 0, -1 });
    float slack = wv.visible ? 15.f : 0.f;
    bool facing = Dot(n, toEye) >= std::cos((W.facingDeg + slack) * kDeg);
    bool looked = Dot(fwd, toEye * -1.f) >= std::cos((W.gazeDeg + slack) * kDeg);
    bool cond = (facing && looked && dist < 0.8f) || adjust; // adjust mode: always shown

    if (!cond) { R.condSince = -1; R.closedByItem = false; } // Close/Back on the first page needs a fresh look
    else if (R.condSince < 0) R.condSince = now;

    if (!wv.visible) {
        if (cond && !R.closedByItem && ((now - R.condSince) * 1000 >= W.showMs || adjust)) {
            R.stack.assign(1, &root->second);
            wv = WristView{}; wv.visible = true; wv.version++;
            R.shownAt = now; R.lostSince = -1; R.pokeArmed = false; R.prevDepth = 1.f;
            if (prof->haptics) haptics.push_back({ h, prof->hapticAmp * 0.6f, 0.02f });
            Log("%s wrist panel shown", h ? "right" : "left");
        } else return;
    } else {
        if (R.closedByItem) { hide(); return; }
        if (!cond) {
            if (R.lostSince < 0) R.lostSince = now;
            if ((now - R.lostSince) * 1000 >= W.hideMs) { hide(); return; }
        } else R.lostSince = -1;
    }

    wv.pose = pose;
    wv.sizeM = W.size;
    if (wv.adjust != adjust) { wv.adjust = adjust; wv.version++; }
    const Wheel* top = R.stack.back();
    bool nested = R.stack.size() > 1;
    if (wv.wheel != top || wv.nested != nested) { wv.wheel = top; wv.nested = nested; wv.hover = -1; wv.version++; }
    if (wv.pressed >= 0 && now > R.pressFlashUntil) { wv.pressed = -1; wv.version++; }
    {
        std::vector<bool> on;
        if (wv.nested) on.push_back(false); // the Back cell
        for (auto& it : wv.wheel->items) on.push_back(IsOn(it.action));
        if (on != wv.on) { wv.on = std::move(on); wv.version++; }
    }

    int o = 1 - h; // the pressing hand
    const HandRaw& hr = fr.h[o];
    int hover = -1;
    bool canPoke = W.press != "point", canPoint = W.press != "poke";
    auto toPanel = [&](Vec3 p, float& u, float& v, float& depth) {
        Vec3 l = RotateInv(pose.orientation, p - c);
        u = l.x / W.size + 0.5f; v = 0.5f - l.y / W.size; depth = l.z;
    };
    bool settled = now - R.shownAt > 0.2; // nothing fires in the first moment

    // Adjust mode: the other hand's grip grabs the panel; it follows that hand, and its stick resizes.
    if (grab.target == h && (!adjust || !cur[o][C_GRIP][F_PRESS] || !hr.gripValid)) {
        grab = GrabRun{};
        if (prof->haptics) haptics.push_back({ o, prof->hapticAmp * 0.6f, 0.015f });
    }
    if (adjust && grab.target < 0 && hr.gripValid && cur[o][C_GRIP][F_PRESS] && !prev[o][C_GRIP][F_PRESS]
        && Len(V(hr.gripPose.position) - c) < W.size * 0.7f + 0.08f) {
        grab = { h, o, c - V(hr.gripPose.position) };
        if (prof->haptics) haptics.push_back({ o, prof->hapticAmp, 0.02f });
    }
    if (grab.target == h) {
        WristStyle& Wm = prof->wrist[h];
        const XrQuaternionf& q = fr.h[h].gripPose.orientation;
        Vec3 Z = Rotate(q, { 0, 0, 1 }), side = Norm(Cross(Z, n));
        Vec3 d = V(hr.gripPose.position) + grab.off - V(fr.h[h].gripPose.position);
        Wm.towardElbow = Dot(d, Z); Wm.lift = Dot(d, n); Wm.side = Dot(d, side);
        float dt = (float)std::clamp(now - lastNow, 0.0, 0.1);
        Wm.size = std::clamp(Wm.size * (1.f + hr.sy * dt * 0.8f), 0.05f, 0.4f);
        Vec3 nn;
        if (WristPoseNow(h, pose, nn)) { wv.pose = pose; wv.sizeM = Wm.size; }
        wv.cursor = false;
        if (wv.hover != -1) { wv.hover = -1; wv.version++; }
        return;
    }
    // In adjust mode only page navigation and the Adjust button itself ("Done") can be pressed.
    auto allowed = [&](int cell) {
        if (!adjust) return true;
        int idx = cell - (wv.nested ? 1 : 0);
        if (idx < 0) return true; // the automatic Back cell
        if (idx >= (int)wv.wheel->items.size()) return false;
        ActType t = wv.wheel->items[idx].action.type;
        return t == ActType::Adjust || t == ActType::Page || t == ActType::Back;
    };

    // Poke: the tip of the other controller pushes through the panel surface.
    wv.cursor = false;
    if (canPoke && hr.aimValid) {
        Vec3 tip = V(hr.aimPose.position) + Rotate(hr.aimPose.orientation, { 0, 0, -1 }) * 0.015f;
        float u, v, d; toPanel(tip, u, v, d);
        bool inside = u >= 0 && u <= 1 && v >= 0 && v <= 1;
        if (inside && d > -0.04f && d < 0.05f) hover = WristCellAt(h, u, v);
        // Touch cursor: shows where the fingertip will land, shrinking and filling in as it gets close.
        const float kCursorRange = 0.06f;
        if (inside && d > -0.04f && d < kCursorRange) {
            float k = d <= 0.f ? 0.f : d / kCursorRange;   // 0 touching .. 1 far
            Vec3 at = c + Rotate(pose.orientation, { (u - 0.5f) * W.size, (0.5f - v) * W.size, 0.002f });
            wv.cursor = true;
            wv.cursorPose = { pose.orientation, { at.x, at.y, at.z } };
            wv.cursorSize = 0.010f + 0.018f * k;
            wv.cursorLevel = d <= 0.f ? 8 : std::min(7, (int)((1.f - k) * 8.f));
        } else wv.cursor = false;
        if (!inside || d > 0.02f) R.pokeArmed = true;  // pulled back out: ready for the next poke
        if (settled && inside && R.pokeArmed && R.prevDepth > 0.f && d <= 0.f && hover >= 0 && allowed(hover)) {
            R.pokeArmed = false;
            WristActivate(h, hover, o);
            if (!wv.visible || R.closedByItem) { hide(); return; }
        }
        R.prevDepth = inside ? d : 1.f;
    }
    // Point: laser from the other hand, trigger to press.
    if (canPoint && hover < 0 && hr.aimValid) {
        Vec3 ro = V(hr.aimPose.position), rd = Rotate(hr.aimPose.orientation, { 0, 0, -1 });
        float den = Dot(rd, n);
        if (std::fabs(den) > 1e-4f) {
            float t = Dot(c - ro, n) / den;
            if (t > 0 && t < 1.5f) {
                float u, v, d; toPanel(ro + rd * t, u, v, d);
                hover = WristCellAt(h, u, v);
            }
        }
        bool trigEdge = cur[o][C_TRIGGER][F_PRESS] && !prev[o][C_TRIGGER][F_PRESS];
        if (settled && trigEdge && hover >= 0 && allowed(hover)) {
            WristActivate(h, hover, o);
            if (R.closedByItem) { hide(); return; }
        }
    }
    if (hover != wv.hover) {
        wv.hover = hover; wv.version++;
        if (hover >= 0 && prof->haptics) haptics.push_back({ o, prof->hapticAmp * 0.5f, 0.008f });
    }
}
