#pragma once
#include "engine.h"

struct XrFns {
    PFN_xrEnumerateSwapchainFormats EnumerateSwapchainFormats = nullptr;
    PFN_xrCreateSwapchain CreateSwapchain = nullptr;
    PFN_xrDestroySwapchain DestroySwapchain = nullptr;
    PFN_xrEnumerateSwapchainImages EnumerateSwapchainImages = nullptr;
    PFN_xrAcquireSwapchainImage AcquireSwapchainImage = nullptr;
    PFN_xrWaitSwapchainImage WaitSwapchainImage = nullptr;
    PFN_xrReleaseSwapchainImage ReleaseSwapchainImage = nullptr;
};

struct Gfx {
    int api = 0; // 0 none, 11 = D3D11, 12 = D3D12
    ID3D11Device* d11 = nullptr;
    ID3D11DeviceContext* ctx = nullptr;
    ID3D12Device* d12 = nullptr;
    ID3D12CommandQueue* queue = nullptr;
    ID3D12CommandAllocator* alloc = nullptr;
    ID3D12GraphicsCommandList* list = nullptr;
    ID3D12Fence* fence = nullptr;
    UINT64 fenceVal = 0;
    HANDLE ev = nullptr;
    void Init(const void* bindingChain);
    void Release();
};

class Panel {
public:
    int w = 0, h = 0;
    XrSwapchain sc = XR_NULL_HANDLE;
    bool ready = false; // at least one image released
    bool Create(XrSession s, Gfx& g, const XrFns& f, int w, int h);
    bool Upload(Gfx& g, const XrFns& f, const std::vector<uint32_t>& bgraPremul);
    void Destroy(const XrFns& f);
private:
    bool swapRB = false;
    std::vector<void*> imgs;
    ID3D12Resource* upload = nullptr;
};

// CPU drawing (GDI+), output is premultiplied BGRA.
void DrawWheel(std::vector<uint32_t>& out, int size, const Profile& p, const WheelView& v);
void DrawToast(std::vector<uint32_t>& out, int w, int h, const std::string& text, const Style& st);
void DrawKeyboard(std::vector<uint32_t>& out, int w, int h, const KeyboardView& kb, const Style& st);
void DrawWrist(std::vector<uint32_t>& out, int size, const Profile& p, const WristStyle& w, const WristView& v);
void DrawCursor(std::vector<uint32_t>& out, int size, int level, const Style& st);
void DrawShutdown();
