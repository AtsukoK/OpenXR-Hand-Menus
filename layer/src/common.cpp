#include "common.h"
#include <shlobj.h>
#include <cstdio>
#include <cstdarg>
#include <algorithm>

static FILE* gLog = nullptr;
static std::mutex gLogMx;

std::wstring Utf8ToW(const std::string& s) {
    if (s.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
    std::wstring w(n, 0);
    MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), w.data(), n);
    return w;
}
std::string WToUtf8(const std::wstring& w) {
    if (w.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), nullptr, 0, nullptr, nullptr);
    std::string s(n, 0);
    WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), s.data(), n, nullptr, nullptr);
    return s;
}
std::wstring DataDir() {
    PWSTR p = nullptr;
    std::wstring d;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &p))) d = p;
    CoTaskMemFree(p);
    d += L"\\VRRadialOverlay";
    CreateDirectoryW(d.c_str(), nullptr);
    return d;
}
std::wstring ProfilesDir() {
    std::wstring d = DataDir() + L"\\profiles";
    CreateDirectoryW(d.c_str(), nullptr);
    return d;
}
std::string ExeName() {
    wchar_t buf[MAX_PATH]{};
    GetModuleFileNameW(nullptr, buf, MAX_PATH);
    std::wstring w = buf;
    auto s = w.find_last_of(L"\\/");
    if (s != std::wstring::npos) w = w.substr(s + 1);
    auto d = w.find_last_of(L'.');
    if (d != std::wstring::npos) w = w.substr(0, d);
    std::string r = WToUtf8(w);
    std::transform(r.begin(), r.end(), r.begin(), [](unsigned char c) { return (char)tolower(c); });
    return r;
}
double NowSec() {
    static LARGE_INTEGER f = [] { LARGE_INTEGER x; QueryPerformanceFrequency(&x); return x; }();
    LARGE_INTEGER c; QueryPerformanceCounter(&c);
    return (double)c.QuadPart / (double)f.QuadPart;
}
void LogInit() {
    std::lock_guard l(gLogMx);
    if (gLog) return;
    std::wstring d = DataDir() + L"\\logs";
    CreateDirectoryW(d.c_str(), nullptr);
    std::wstring f = d + L"\\" + Utf8ToW(ExeName()) + L".log";
    gLog = _wfsopen(f.c_str(), L"w", _SH_DENYWR);
}
void Log(const char* fmt, ...) {
    std::lock_guard l(gLogMx);
    if (!gLog) return;
    SYSTEMTIME t; GetLocalTime(&t);
    fprintf(gLog, "%02d:%02d:%02d.%03d ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
    va_list a; va_start(a, fmt); vfprintf(gLog, fmt, a); va_end(a);
    fputc('\n', gLog);
    fflush(gLog);
}

Vec3 Rotate(const XrQuaternionf& q, Vec3 v) {
    Vec3 u{ q.x, q.y, q.z };
    Vec3 t = Cross(u, v) * 2.f;
    return v + t * q.w + Cross(u, t);
}
Vec3 RotateInv(const XrQuaternionf& q, Vec3 v) {
    XrQuaternionf c{ -q.x, -q.y, -q.z, q.w };
    return Rotate(c, v);
}
XrQuaternionf QuatFromBasis(Vec3 x, Vec3 y, Vec3 z) {
    float m00 = x.x, m01 = y.x, m02 = z.x;
    float m10 = x.y, m11 = y.y, m12 = z.y;
    float m20 = x.z, m21 = y.z, m22 = z.z;
    float tr = m00 + m11 + m22;
    XrQuaternionf q;
    if (tr > 0) {
        float s = std::sqrt(tr + 1.f) * 2.f;
        q.w = 0.25f * s; q.x = (m21 - m12) / s; q.y = (m02 - m20) / s; q.z = (m10 - m01) / s;
    } else if (m00 > m11 && m00 > m22) {
        float s = std::sqrt(1.f + m00 - m11 - m22) * 2.f;
        q.w = (m21 - m12) / s; q.x = 0.25f * s; q.y = (m01 + m10) / s; q.z = (m02 + m20) / s;
    } else if (m11 > m22) {
        float s = std::sqrt(1.f + m11 - m00 - m22) * 2.f;
        q.w = (m02 - m20) / s; q.x = (m01 + m10) / s; q.y = 0.25f * s; q.z = (m12 + m21) / s;
    } else {
        float s = std::sqrt(1.f + m22 - m00 - m11) * 2.f;
        q.w = (m10 - m01) / s; q.x = (m02 + m20) / s; q.y = (m12 + m21) / s; q.z = 0.25f * s;
    }
    return q;
}
XrQuaternionf BillboardTo(Vec3 center, Vec3 eye) {
    Vec3 z = Norm(eye - center);
    Vec3 up{ 0, 1, 0 };
    Vec3 x = Cross(up, z);
    if (Len(x) < 1e-4f) x = { 1, 0, 0 };
    x = Norm(x);
    Vec3 y = Cross(z, x);
    return QuatFromBasis(x, y, z);
}
