#pragma once
#include "common.h"
#include <functional>

namespace Keys {
// Combo syntax: "Ctrl+Shift+S", "F5", "LMB", "WheelUp".
bool Validate(const std::string& combo, std::string& err);
bool ValidateSequence(const std::string& steps, std::string& err);
void Tap(const std::string& combo, int holdMs);
void Down(const std::string& combo);
void Up(const std::string& combo);
// "Ctrl+C; wait 100; down W; wait 500; up W; F5"
void Sequence(const std::string& steps);
void ReleaseAll();
void SetFocusWindow(bool on);
void Shutdown();
}
