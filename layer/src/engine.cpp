#include "engine.h"
#include "keys.h"
#include <algorithm>

static constexpr float kPi = 3.14159265f;

void Engine::SetProfile(std::shared_ptr<Profile> p) {
    Reset();
    prof = std::move(p);
    bs.assign(prof ? prof->bindings.size() : 0, BState{});
    if (prof) Keys::SetFocusWindow(prof->focusWindow);
}

void Engine::Reset() {
    if (wr.open) CloseWheel();
    kb = KeyboardView{}; kbRepeatKey = kbRepeatHand = -1;
    for (int h = 0; h < 2; h++) { wrist[h] = WristView{}; wrun[h] = WristRun{}; }
    Keys::ReleaseAll();
    keyToggles.clear();
    xrToggles.clear(); xrHolds.clear(); pulses.clear();
    layers.clear();
    for (auto& b : bs) b = BState{};
    memset(blockOut, 0, sizeof(blockOut));
    for (auto& h : ovOut) for (auto& o : h) o = Override{};
}

bool Engine::IsOn(const Action& a) const {
    switch (a.type) {
    case ActType::Key: return a.mode == "toggle" && keyToggles.count(a.keys) > 0;
    case ActType::XrInput: return a.mode == "toggle" && xrToggles.count({ a.target.hand, a.target.comp }) > 0;
    case ActType::Layer: return layers.count(a.layer) > 0;
    default: return false;
    }
}

void Engine::Toast(const std::string& s, double sec) {
    if (prof && !prof->notifications) return;
    toast = s; toastUntil = NowSec() + sec; toastVersion++;
}

void Engine::ComputeDown() {
    memcpy(prev, cur, sizeof(cur));
    memset(cur, 0, sizeof(cur));
    const Profile& p = *prof;
    for (int h = 0; h < 2; h++) {
        const HandRaw& r = fr.h[h];
        auto hyst = [&](int c, int f, bool cond_on, bool cond_hold) { cur[h][c][f] = prev[h][c][f] ? cond_hold : cond_on; };
        cur[h][C_BTN1][F_PRESS] = r.b1; cur[h][C_BTN1][F_TOUCH] = r.b1t || r.b1;
        cur[h][C_BTN2][F_PRESS] = r.b2; cur[h][C_BTN2][F_TOUCH] = r.b2t || r.b2;
        hyst(C_TRIGGER, F_PRESS, r.trig > p.pressOn, r.trig > p.pressOff);
        cur[h][C_TRIGGER][F_TOUCH] = r.trigT || cur[h][C_TRIGGER][F_PRESS];
        hyst(C_GRIP, F_PRESS, r.grip > p.pressOn, r.grip > p.pressOff);
        cur[h][C_GRIP][F_TOUCH] = cur[h][C_GRIP][F_PRESS];
        cur[h][C_STICK][F_PRESS] = r.sc;
        cur[h][C_STICK][F_TOUCH] = r.st || r.sc;
        hyst(C_STICK, F_UP, r.sy > p.stickOn, r.sy > p.stickOff);
        hyst(C_STICK, F_DOWN, r.sy < -p.stickOn, r.sy < -p.stickOff);
        hyst(C_STICK, F_RIGHT, r.sx > p.stickOn, r.sx > p.stickOff);
        hyst(C_STICK, F_LEFT, r.sx < -p.stickOn, r.sx < -p.stickOff);
        cur[h][C_THUMBREST][F_TOUCH] = cur[h][C_THUMBREST][F_PRESS] = r.rest;
        cur[h][C_MENU][F_PRESS] = cur[h][C_MENU][F_TOUCH] = r.menu;
    }
}

bool Engine::CompActive(int h, int c) const {
    const HandRaw& r = fr.h[h];
    switch (c) {
    case C_TRIGGER: return r.trig > prof->pressOff * 0.5f;
    case C_GRIP: return r.grip > prof->pressOff * 0.5f;
    case C_STICK: return r.sc || std::sqrt(r.sx * r.sx + r.sy * r.sy) > 0.25f;
    case C_THUMBREST: return false;
    default: return cur[h][c][F_PRESS];
    }
}

bool Engine::LayersOk(const Binding& b) const {
    for (auto& l : b.layers) if (!layers.count(l)) return false;
    for (auto& l : b.notLayers) if (layers.count(l)) return false;
    return true;
}

Override Engine::MakeOv(const InputId& t) {
    Override o; o.on = true;
    switch (t.feat) {
    case F_PRESS: o.press = true; o.touch = true; o.value = 1; break;
    case F_TOUCH: o.touch = true; break;
    case F_UP: o.y = 1; o.touch = true; break;
    case F_DOWN: o.y = -1; o.touch = true; break;
    case F_LEFT: o.x = -1; o.touch = true; break;
    case F_RIGHT: o.x = 1; o.touch = true; break;
    }
    return o;
}

void Engine::SetLayer(const std::string& l, bool on) {
    bool had = layers.count(l) > 0;
    if (on == had) return;
    if (on) layers.insert(l); else layers.erase(l);
    Log("layer %s %s", l.c_str(), on ? "on" : "off");
    Toast("Layer " + l + (on ? " ON" : " off"), 1.2);
}

// Returns true when the action keeps running until EndAction (hold behaviour).
bool Engine::RunAction(const Action& a, int hand, int bi, bool fromWheel) {
    bool hold = (a.mode == "hold") && !fromWheel;
    switch (a.type) {
    case ActType::Key:
        if (a.mode == "toggle") {
            if (keyToggles.count(a.keys)) { keyToggles.erase(a.keys); Keys::Up(a.keys); }
            else { keyToggles.insert(a.keys); Keys::Down(a.keys); }
            return false;
        }
        if (hold) { Keys::Down(a.keys); return true; }
        Keys::Tap(a.keys, a.tapMs);
        return false;
    case ActType::Sequence:
        Keys::Sequence(a.steps);
        return false;
    case ActType::XrInput: {
        auto key = std::make_pair(a.target.hand, a.target.comp);
        if (a.mode == "toggle") {
            if (xrToggles.count(key)) xrToggles.erase(key); else xrToggles[key] = MakeOv(a.target);
            return false;
        }
        if (hold) { xrHolds[bi] = { key, MakeOv(a.target) }; return true; }
        pulses.push_back({ a.target.hand, a.target.comp, MakeOv(a.target), now + std::max(a.tapMs, 30) / 1000.0 });
        return false;
    }
    case ActType::Layer:
        if (a.mode == "hold" && !fromWheel) { SetLayer(a.layer, true); return true; }
        if (a.mode == "on") SetLayer(a.layer, true);
        else if (a.mode == "off") SetLayer(a.layer, false);
        else SetLayer(a.layer, !layers.count(a.layer));
        return false;
    case ActType::Wheel:
        if (fromWheel) { auto it = prof->wheels.find(a.wheel); if (it != prof->wheels.end()) PushWheel(&it->second); return false; }
        OpenWheel(a, bi, hand);
        return wr.open && wr.confirm == "release";
    case ActType::Back:
        if (wr.open) { if (wr.stack.size() > 1) { wr.stack.pop_back(); wr.hovered = -1; Dirty(); } else CloseWheel(); }
        return false;
    case ActType::Close:
        if (wr.open) CloseWheel();
        return false;
    case ActType::Reload:
        reloadRequested = true;
        return false;
    case ActType::Keyboard:
        OpenKeyboard(bi);
        return false;
    default:
        return false;
    }
}

void Engine::EndAction(const Action& a, int bi) {
    switch (a.type) {
    case ActType::Key: Keys::Up(a.keys); break;
    case ActType::XrInput: xrHolds.erase(bi); break;
    case ActType::Layer: SetLayer(a.layer, false); break;
    case ActType::Wheel:
        if (wr.open && wr.opener == bi && wr.confirm == "release") {
            if (wr.hovered >= 0) Select(wr.hovered); else CloseWheel();
        }
        break;
    default: break;
    }
}

void Engine::Fire(int bi) {
    const Binding& b = prof->bindings[bi];
    BState& s = bs[bi];
    s.fired = true;
    Log("fire '%s' (%s)", b.name.c_str(), b.chordText.c_str());
    bool holds = RunAction(b.action, b.chord.back().hand, bi, false);
    bool wantsEnd = holds && (b.gesture == "press" || b.gesture == "hold" || b.gesture == "while_held");
    if (wantsEnd) s.needEnd = true;
    else if (holds) EndAction(b.action, bi);
}

void Engine::EndB(int bi) {
    BState& s = bs[bi];
    if (s.needEnd) { s.needEnd = false; EndAction(prof->bindings[bi].action, bi); }
}

void Engine::ProcessBindings() {
    auto& B = prof->bindings;
    // Releases and holds of armed bindings.
    for (int i = 0; i < (int)B.size(); i++) {
        BState& s = bs[i];
        if (!s.armed) continue;
        const Binding& b = B[i];
        const InputId& trig = b.chord.back();
        double dur = now - s.armTime;
        if (!Down(trig)) {
            s.armed = false;
            if (b.gesture == "tap" && dur * 1000 < b.tapMs) {
                bool otherFired = false;
                for (int j = 0; j < (int)B.size(); j++)
                    if (j != i && bs[j].fired && bs[j].armTime == s.armTime && B[j].chord.back() == trig) otherFired = true;
                if (!otherFired) Fire(i);
            }
            if (b.gesture == "hold" && !s.fired && b.tapPassthrough)
                pulses.push_back({ trig.hand, trig.comp, MakeOv(trig), now + 0.08 });
            EndB(i);
            continue;
        }
        if (b.gesture == "hold" && !s.fired && dur * 1000 >= b.holdMs) Fire(i);
    }
    // New presses: arm the most specific matching bindings.
    std::vector<int> cand;
    for (int i = 0; i < (int)B.size(); i++) {
        const Binding& b = B[i];
        if (!b.enabled || bs[i].armed) continue;
        const InputId& trig = b.chord.back();
        if (!Pressed(trig)) continue;
        if (wr.open && b.action.type != ActType::Wheel && b.action.type != ActType::Keyboard) continue;
        if (kb.visible && b.action.type != ActType::Keyboard) continue;
        if (!LayersOk(b)) continue;
        bool others = true;
        for (size_t k = 0; k + 1 < b.chord.size(); k++) if (!Down(b.chord[k])) others = false;
        if (others) cand.push_back(i);
    }
    // Group by trigger, keep max specificity per trigger.
    for (int i : cand) {
        const Binding& b = B[i];
        int spec = (int)(b.chord.size() + b.layers.size());
        bool best = true;
        for (int j : cand) {
            const Binding& o = B[j];
            if (o.chord.back() == b.chord.back() && (int)(o.chord.size() + o.layers.size()) > spec) best = false;
        }
        if (!best) continue;
        BState& s = bs[i];
        s.armed = true; s.armTime = now; s.fired = false;
        if (b.gesture == "press" || b.gesture == "while_held") Fire(i);
        else if (b.gesture == "double_tap") {
            if (s.lastTap >= 0 && (now - s.lastTap) * 1000 <= b.doubleTapMs) { s.lastTap = -1; Fire(i); }
            else s.lastTap = now;
        }
    }
}

XrPosef Engine::WheelPose(int hand) const {
    const Style& st = prof->style;
    Vec3 head = V(fr.head.position);
    Vec3 center;
    bool handAnchor = st.anchor == "hand" || st.anchor == "hand_locked";
    if (handAnchor && fr.h[hand].gripValid) {
        Vec3 g = V(fr.h[hand].gripPose.position);
        Vec3 toward = head - g; toward.y = 0; toward = Norm(toward);
        center = g + Vec3{ 0, st.handUp, 0 } + toward * st.handToward;
    } else {
        Vec3 f = Rotate(fr.head.orientation, { 0, 0, -1 });
        f.y = 0; f = Norm(f);
        center = head + f * st.distance - Vec3{ 0, st.headDown, 0 };
    }
    XrPosef p;
    p.position = X(center);
    p.orientation = BillboardTo(center, head);
    return p;
}

void Engine::OpenWheel(const Action& a, int bi, int hand) {
    auto it = prof->wheels.find(a.wheel);
    if (it == prof->wheels.end()) return;
    if (wr.open) { CloseWheel(); return; } // pressing the opener again closes it
    wr = WheelRun{};
    wr.open = true;
    wr.stack.push_back(&it->second);
    wr.opener = bi;
    wr.openerHand = hand;
    const Selection& sel = prof->sel;
    wr.selHand = sel.hand == "left" ? 0 : sel.hand == "right" ? 1 : hand;
    wr.confirm = a.confirm;
    if (wr.confirm == "auto" && bi < 0) wr.confirm = "button"; // opened from a wrist button
    else if (wr.confirm == "auto") {
        const Binding& b = prof->bindings[bi];
        const InputId& t = b.chord.back();
        if (t.feat == F_TOUCH) wr.confirm = "stick_return";
        else if (b.gesture == "while_held" || b.gesture == "hold") wr.confirm = "release";
        else wr.confirm = "button";
    }
    wr.openTime = now;
    wr.pose = WheelPose(hand);
    wr.locked = prof->style.anchor == "hand_locked" || prof->style.anchor == "head_locked";
    // If the stick is already pushed, wait for it to centre first.
    float m = std::hypot(fr.h[wr.selHand].sx, fr.h[wr.selHand].sy);
    wr.stickOut = false;
    if (m > sel.deadzone) wr.hoverSince = -1; // marker: ignore until centred
    Log("wheel '%s' open, confirm=%s", a.wheel.c_str(), wr.confirm.c_str());
    Dirty();
}

void Engine::PushWheel(const Wheel* w) {
    wr.stack.push_back(w);
    wr.hovered = -1;
    wr.hoverSince = now;
    Dirty();
}

void Engine::CloseWheel() {
    wr.open = false;
    wr.stack.clear();
    wr.hovered = -1;
    view.visible = false;
    Dirty();
}

void Engine::Select(int idx) {
    const Wheel* w = wr.stack.back();
    if (idx < 0 || idx >= (int)w->items.size()) return;
    const WheelItem& it = w->items[idx];
    Log("select '%s'", it.label.c_str());
    if (prof->haptics) haptics.push_back({ wr.selHand, std::min(1.f, prof->hapticAmp * 1.6f), 0.04f });
    if (it.action.type == ActType::Wheel || it.action.type == ActType::Back) { RunAction(it.action, wr.selHand, -1, true); return; }
    RunAction(it.action, wr.selHand, -1, true);
    if (wr.open && (prof->sel.closeAfterSelect || it.action.type == ActType::Close)) CloseWheel();
    else Dirty();
}

static int Sector(float x, float y, int n) {
    if (n <= 0) return -1;
    float ang = std::atan2(x, y) * 180.f / kPi; // 0 = up, clockwise
    float seg = 360.f / n;
    float a = std::fmod(ang + seg / 2 + 720.f, 360.f);
    return std::min(n - 1, (int)(a / seg));
}

void Engine::UpdateWheel() {
    if (!wr.open) return;
    const Selection& sel = prof->sel;
    const Wheel* w = wr.stack.back();
    int n = (int)w->items.size();
    if (!wr.locked) wr.pose = WheelPose(wr.openerHand);

    const HandRaw& hr = fr.h[wr.selHand];
    float mag = std::hypot(hr.sx, hr.sy);
    bool out = mag > sel.deadzone;
    if (wr.hoverSince < 0) { if (mag < sel.deadzone * 0.6f) wr.hoverSince = now; out = false; }
    bool useStick = sel.method != "point";
    bool usePoint = sel.method == "point" || sel.method == "both";
    int newHover = wr.hovered;

    if (useStick && out) newHover = Sector(hr.sx, hr.sy, n);
    if (usePoint && hr.aimValid && !(useStick && out)) {
        Vec3 o = V(hr.aimPose.position);
        Vec3 d = Rotate(hr.aimPose.orientation, { 0, 0, -1 });
        Vec3 c = V(wr.pose.position);
        Vec3 nrm = Rotate(wr.pose.orientation, { 0, 0, 1 });
        float den = Dot(d, nrm);
        newHover = -1;
        if (std::fabs(den) > 1e-4f) {
            float t = Dot(c - o, nrm) / den;
            if (t > 0) {
                Vec3 local = RotateInv(wr.pose.orientation, (o + d * t) - c);
                float r = std::hypot(local.x, local.y) / prof->style.radius;
                if (r > prof->style.innerRatio && r < 1.25f) newHover = Sector(local.x, local.y, n);
            }
        }
    }

    // Stick-return confirm: pick the hovered item when the stick comes back to the centre.
    if (useStick && wr.confirm == "stick_return" && wr.stickOut && !out && wr.hovered >= 0) {
        wr.stickOut = false;
        Select(wr.hovered);
        return;
    }
    wr.stickOut = out;

    if (newHover != wr.hovered) {
        wr.hovered = newHover;
        wr.hoverSince = now;
        if (newHover >= 0 && prof->haptics) haptics.push_back({ wr.selHand, prof->hapticAmp, 0.015f });
        Dirty();
    }
    // Release-confirm wheels open submenus by resting on them.
    if (wr.confirm == "release" && wr.hovered >= 0 && sel.submenuDwellMs > 0 &&
        w->items[wr.hovered].action.type == ActType::Wheel && (now - wr.hoverSince) * 1000 >= sel.submenuDwellMs) {
        Select(wr.hovered);
        return;
    }
    if (sel.confirmComp >= 0 && cur[wr.selHand][sel.confirmComp][F_PRESS] && !prev[wr.selHand][sel.confirmComp][F_PRESS]) {
        if (wr.hovered >= 0) { Select(wr.hovered); return; }
    }
    if (sel.cancelComp >= 0 && cur[wr.selHand][sel.cancelComp][F_PRESS] && !prev[wr.selHand][sel.cancelComp][F_PRESS]) {
        if (wr.stack.size() > 1) { wr.stack.pop_back(); wr.hovered = -1; Dirty(); }
        else CloseWheel();
        return;
    }
    if (sel.autoCloseMs > 0 && (now - wr.openTime) * 1000 > sel.autoCloseMs) CloseWheel();
}

void Engine::ComputeBlocks() {
    bool want[2][C_COUNT]{};
    for (int i = 0; i < (int)bs.size(); i++) {
        if (!bs[i].armed) continue;
        const Binding& b = prof->bindings[i];
        if (b.block == "trigger") { auto& t = b.chord.back(); want[t.hand][t.comp] = true; }
        else if (b.block == "all") for (auto& t : b.chord) want[t.hand][t.comp] = true;
    }
    if (wr.open) {
        const std::string& m = prof->blockWhileOpen;
        if (m == "all") { for (auto& h : want) for (auto& c : h) c = true; }
        else if (m == "hand") { for (int c = 0; c < C_COUNT; c++) want[wr.selHand][c] = want[wr.openerHand][c] = true; }
        else if (m == "minimal") {
            want[wr.selHand][C_STICK] = true;
            if (prof->sel.confirmComp >= 0) want[wr.selHand][prof->sel.confirmComp] = true;
            if (prof->sel.cancelComp >= 0) want[wr.selHand][prof->sel.cancelComp] = true;
        }
    }
    if (kb.visible) for (auto& h : want) for (auto& c : h) c = true;
    for (int h = 0; h < 2; h++) // the pressing (other) hand's trigger belongs to the panel while over a button
        if (wrist[h].visible && wrist[h].hover >= 0) want[1 - h][C_TRIGGER] = true;
    for (int h = 0; h < 2; h++)
        for (int c = 0; c < C_COUNT; c++) {
            // Once blocked, stay blocked until the part is let go so the game never sees a half press.
            if (want[h][c]) sticky[h][c] = true;
            else if (sticky[h][c] && !CompActive(h, c)) sticky[h][c] = false;
            blockOut[h][c] = sticky[h][c];
        }
    for (auto& h : ovOut) for (auto& o : h) o = Override{};
    pulses.erase(std::remove_if(pulses.begin(), pulses.end(), [&](const Pulse& p) { return p.until < now; }), pulses.end());
    for (auto& p : pulses) ovOut[p.hand][p.comp] = p.o;
    for (auto& [k, o] : xrToggles) ovOut[k.first][k.second] = o;
    for (auto& [bi, ko] : xrHolds) ovOut[ko.first.first][ko.first.second] = ko.second;
}

void Engine::Update(const Frame& f) {
    if (!Active()) { memset(blockOut, 0, sizeof(blockOut)); for (auto& h : ovOut) for (auto& o : h) o = Override{}; return; }
    fr = f;
    now = f.t;
    ComputeDown();
    ProcessBindings();
    UpdateWheel();
    UpdateKeyboard();
    UpdateWrist(0);
    UpdateWrist(1);
    ComputeBlocks();

    view.visible = wr.open;
    if (wr.open) {
        const Wheel* w = wr.stack.back();
        if (view.wheel != w || view.hovered != wr.hovered) { view.wheel = w; view.hovered = wr.hovered; Dirty(); }
        std::string crumb;
        for (size_t i = 0; i < wr.stack.size(); i++) crumb += (i ? " > " : "") + wr.stack[i]->title;
        view.crumb = crumb;
        view.pose = wr.pose;
        std::vector<bool> on;
        for (auto& it : w->items) on.push_back(IsOn(it.action));
        if (on != view.on) { view.on = std::move(on); Dirty(); }
        view.size = prof->style.radius * 2.f;
    }
}
