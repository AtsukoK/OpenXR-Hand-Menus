#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#define XR_USE_PLATFORM_WIN32
#define XR_USE_GRAPHICS_API_D3D11
#define XR_USE_GRAPHICS_API_D3D12
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include <string>
#include <vector>
#include <mutex>
#include <cmath>
#include <cstdint>

void Log(const char* fmt, ...);
void LogInit();
std::wstring Utf8ToW(const std::string& s);
std::string WToUtf8(const std::wstring& s);
std::wstring DataDir();      // %LOCALAPPDATA%\OpenXRHandMenus
std::wstring ProfilesDir();  // ...\profiles
std::string ExeName();       // lower-case exe name without extension
double NowSec();

struct Vec3 { float x = 0, y = 0, z = 0; };
inline Vec3 operator+(Vec3 a, Vec3 b) { return { a.x + b.x, a.y + b.y, a.z + b.z }; }
inline Vec3 operator-(Vec3 a, Vec3 b) { return { a.x - b.x, a.y - b.y, a.z - b.z }; }
inline Vec3 operator*(Vec3 a, float s) { return { a.x * s, a.y * s, a.z * s }; }
inline float Dot(Vec3 a, Vec3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
inline Vec3 Cross(Vec3 a, Vec3 b) { return { a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x }; }
inline float Len(Vec3 a) { return std::sqrt(Dot(a, a)); }
inline Vec3 Norm(Vec3 a) { float l = Len(a); return l > 1e-6f ? a * (1.f / l) : Vec3{ 0, 0, -1 }; }
inline Vec3 V(XrVector3f v) { return { v.x, v.y, v.z }; }
inline XrVector3f X(Vec3 v) { return { v.x, v.y, v.z }; }
Vec3 Rotate(const XrQuaternionf& q, Vec3 v);
Vec3 RotateInv(const XrQuaternionf& q, Vec3 v);
XrQuaternionf QuatFromBasis(Vec3 x, Vec3 y, Vec3 z);
XrQuaternionf BillboardTo(Vec3 center, Vec3 eye);
