// In-game VR keyboard: point with either controller, trigger to type.
#include "engine.h"
#include "keys.h"
#include <algorithm>

const std::vector<KbKey>& KbLayout(float& W, float& Hh) {
    static std::vector<KbKey> keys;
    W = 15.f; Hh = 5.f;
    if (!keys.empty()) return keys;
    float x = 0, y = 0;
    auto add = [&](std::string label, std::string shiftLabel, std::string combo, float w, int kind = KB_KEY, bool letter = false, bool repeat = false) {
        if (shiftLabel.empty()) shiftLabel = letter ? std::string(1, (char)toupper(label[0])) : label;
        keys.push_back({ label, shiftLabel, combo, x, y, w, 1.f, kind, letter, repeat });
        x += w;
    };
    auto row = [&] { x = 0; y += 1; };
    add("Esc", "", "Esc", 1);
    const std::string digits = "1234567890", shifted = "!@#$%^&*()";
    for (int i = 0; i < 10; i++) add(std::string(1, digits[i]), std::string(1, shifted[i]), std::string(1, digits[i]), 1);
    add("-", "_", "minus", 1); add("=", "+", "equals", 1);
    add("← Bksp", "", "Backspace", 2, KB_KEY, false, true);
    row();
    add("Tab", "", "Tab", 1.5f);
    for (const char* c : { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" }) add(c, "", c, 1, KB_KEY, true);
    add("[", "{", "lbracket", 1); add("]", "}", "rbracket", 1); add("\\", "|", "backslash", 1.5f);
    row();
    add("Caps", "", "", 1.75f, KB_CAPS);
    for (const char* c : { "a", "s", "d", "f", "g", "h", "j", "k", "l" }) add(c, "", c, 1, KB_KEY, true);
    add(";", ":", "semicolon", 1); add("'", "\"", "quote", 1); add("Enter", "", "Enter", 2.25f);
    row();
    add("Shift", "", "", 2.25f, KB_SHIFT);
    for (const char* c : { "z", "x", "c", "v", "b", "n", "m" }) add(c, "", c, 1, KB_KEY, true);
    add(",", "<", "comma", 1); add(".", ">", "period", 1); add("/", "?", "slash", 1);
    add("Shift", "", "", 2.75f, KB_SHIFT);
    row();
    add("Ctrl", "", "", 1.5f, KB_CTRL); add("Alt", "", "", 1.5f, KB_ALT);
    add("Space", "", "Space", 5, KB_KEY, false, true); add("Del", "", "Delete", 1, KB_KEY, false, true);
    add("←", "", "Left", 1, KB_KEY, false, true); add("↑", "", "Up", 1, KB_KEY, false, true);
    add("↓", "", "Down", 1, KB_KEY, false, true); add("→", "", "Right", 1, KB_KEY, false, true);
    add("Close", "", "", 2, KB_CLOSE);
    return keys;
}

void Engine::OpenKeyboard(int bi) {
    if (kb.visible) { CloseKeyboard(); return; } // opener again = close
    if (wr.open) CloseWheel();
    float uw, uh; KbLayout(uw, uh);
    kb = KeyboardView{};
    kb.visible = true;
    kb.widthM = prof->kb.width;
    kb.heightM = prof->kb.width * uh / uw;
    Vec3 head = V(fr.head.position);
    Vec3 f = Rotate(fr.head.orientation, { 0, 0, -1 }); f.y = 0; f = Norm(f);
    Vec3 c = head + f * prof->kb.distance - Vec3{ 0, prof->kb.below, 0 };
    kb.pose.position = X(c);
    kb.pose.orientation = BillboardTo(c, head); // faces the eyes, so it tilts up like a desk
    kbOpenTime = now; kbOpener = bi; kbRepeatKey = -1;
    Log("keyboard open");
    kb.version++;
}

void Engine::CloseKeyboard() {
    kb.visible = false; kb.version++;
    kbRepeatKey = kbRepeatHand = -1;
    Log("keyboard closed");
}

int Engine::KbHit(int h) const {
    const HandRaw& hr = fr.h[h];
    if (!hr.aimValid) return -1;
    Vec3 o = V(hr.aimPose.position), d = Rotate(hr.aimPose.orientation, { 0, 0, -1 });
    Vec3 c = V(kb.pose.position), n = Rotate(kb.pose.orientation, { 0, 0, 1 });
    float den = Dot(d, n);
    if (std::fabs(den) < 1e-4f) return -1;
    float t = Dot(c - o, n) / den;
    if (t <= 0) return -1;
    Vec3 l = RotateInv(kb.pose.orientation, (o + d * t) - c);
    float uw, uh; const auto& keys = KbLayout(uw, uh);
    float u = (l.x / kb.widthM + 0.5f) * uw, v = (0.5f - l.y / kb.heightM) * uh;
    for (int i = 0; i < (int)keys.size(); i++) {
        const auto& k = keys[i];
        if (u >= k.x && u < k.x + k.w && v >= k.y && v < k.y + k.h) return i;
    }
    return -1;
}

void Engine::KbPress(int i, int hand) {
    float uw, uh; const auto& keys = KbLayout(uw, uh);
    const KbKey& k = keys[i];
    if (prof->haptics) haptics.push_back({ hand, std::min(1.f, prof->hapticAmp * 1.4f), 0.02f });
    kb.pressed = i; kbPressFlashUntil = now + 0.12;
    switch (k.kind) {
    case KB_SHIFT: kb.shift = !kb.shift; break;
    case KB_CAPS: kb.caps = !kb.caps; break;
    case KB_CTRL: kb.ctrl = !kb.ctrl; break;
    case KB_ALT: kb.alt = !kb.alt; break;
    case KB_CLOSE: CloseKeyboard(); return;
    default: {
        std::string combo;
        if (kb.ctrl) combo += "Ctrl+";
        if (kb.alt) combo += "Alt+";
        bool upper = k.letter ? (kb.shift != kb.caps) : kb.shift;
        if (upper) combo += "Shift+";
        combo += k.combo;
        Keys::Tap(combo, 25);
        kb.shift = kb.ctrl = kb.alt = false; // one-shot modifiers
        if (k.combo == "Enter" && prof->kb.closeOnEnter) { CloseKeyboard(); return; }
    }
    }
    kb.version++;
}

void Engine::UpdateKeyboard() {
    if (!kb.visible) return;
    for (int h = 0; h < 2; h++) {
        int hit = KbHit(h);
        if (hit != kb.hover[h]) {
            kb.hover[h] = hit; kb.version++;
            if (hit >= 0 && prof->haptics) haptics.push_back({ h, prof->hapticAmp * 0.5f, 0.008f });
        }
    }
    if (kb.pressed >= 0 && now > kbPressFlashUntil) { kb.pressed = -1; kb.version++; }
    int c = prof->sel.cancelComp; // cancel button on either hand closes, any time
    for (int h = 0; h < 2; h++)
        if (c >= 0 && cur[h][c][F_PRESS] && !prev[h][c][F_PRESS]) { CloseKeyboard(); return; }
    if (now - kbOpenTime < 0.25) return; // ignore the press that opened it
    float uw, uh; const auto& keys = KbLayout(uw, uh);
    for (int h = 0; h < 2; h++) {
        bool down = cur[h][C_TRIGGER][F_PRESS], edge = down && !prev[h][C_TRIGGER][F_PRESS];
        if (edge && kb.hover[h] >= 0) {
            int k = kb.hover[h];
            KbPress(k, h);
            if (!kb.visible) return;
            if (keys[k].repeat) { kbRepeatKey = k; kbRepeatHand = h; kbNextRepeat = now + 0.45; }
        }
        if (kbRepeatHand == h && (!down || kb.hover[h] != kbRepeatKey)) kbRepeatKey = kbRepeatHand = -1;
    }
    if (kbRepeatKey >= 0 && now >= kbNextRepeat) { KbPress(kbRepeatKey, kbRepeatHand); kbNextRepeat = now + 0.06; }
}
