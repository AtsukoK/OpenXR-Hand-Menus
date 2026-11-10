#include "panel.h"
#include <algorithm>
#include <cmath>
#include <map>
#include <memory>
#include <objidl.h>
namespace Gdiplus { using std::min; using std::max; }
#include <gdiplus.h>

using namespace Gdiplus;

// ---------- graphics context ----------
void Gfx::Init(const void* chain) {
    Release();
    auto* h = (const XrBaseInStructure*)chain;
    for (; h; h = h->next) {
        if (h->type == XR_TYPE_GRAPHICS_BINDING_D3D11_KHR) {
            auto* b = (const XrGraphicsBindingD3D11KHR*)h;
            d11 = b->device; d11->AddRef();
            d11->GetImmediateContext(&ctx);
            api = 11;
            return;
        }
        if (h->type == XR_TYPE_GRAPHICS_BINDING_D3D12_KHR) {
            auto* b = (const XrGraphicsBindingD3D12KHR*)h;
            d12 = b->device; d12->AddRef();
            queue = b->queue; queue->AddRef();
            d12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&alloc));
            d12->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, alloc, nullptr, IID_PPV_ARGS(&list));
            if (list) list->Close();
            d12->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence));
            ev = CreateEventW(nullptr, FALSE, FALSE, nullptr);
            api = 12;
            return;
        }
    }
    api = 0;
}
void Gfx::Release() {
    if (ctx) ctx->Release(); if (d11) d11->Release();
    if (list) list->Release(); if (alloc) alloc->Release(); if (fence) fence->Release();
    if (queue) queue->Release(); if (d12) d12->Release();
    if (ev) CloseHandle(ev);
    *this = Gfx{};
}

// ---------- swapchain panel ----------
bool Panel::Create(XrSession s, Gfx& g, const XrFns& f, int W, int H) {
    if (g.api == 0) return false;
    uint32_t n = 0;
    f.EnumerateSwapchainFormats(s, 0, &n, nullptr);
    std::vector<int64_t> fmts(n);
    f.EnumerateSwapchainFormats(s, n, &n, fmts.data());
    const int64_t pref[] = { DXGI_FORMAT_B8G8R8A8_UNORM_SRGB, DXGI_FORMAT_R8G8B8A8_UNORM_SRGB, DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_FORMAT_R8G8B8A8_UNORM };
    int64_t fmt = 0;
    for (auto p : pref) if (std::find(fmts.begin(), fmts.end(), p) != fmts.end()) { fmt = p; break; }
    if (!fmt) { Log("panel: no usable swapchain format"); return false; }
    swapRB = fmt == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB || fmt == DXGI_FORMAT_R8G8B8A8_UNORM;

    XrSwapchainCreateInfo ci{ XR_TYPE_SWAPCHAIN_CREATE_INFO };
    ci.usageFlags = XR_SWAPCHAIN_USAGE_SAMPLED_BIT | XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT;
    ci.format = fmt; ci.sampleCount = 1; ci.width = W; ci.height = H; ci.faceCount = 1; ci.arraySize = 1; ci.mipCount = 1;
    XrResult r = f.CreateSwapchain(s, &ci, &sc);
    if (XR_FAILED(r)) { Log("panel: xrCreateSwapchain failed %d", r); sc = XR_NULL_HANDLE; return false; }
    w = W; h = H;
    uint32_t cnt = 0;
    f.EnumerateSwapchainImages(sc, 0, &cnt, nullptr);
    imgs.clear();
    if (g.api == 11) {
        std::vector<XrSwapchainImageD3D11KHR> im(cnt, { XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR });
        f.EnumerateSwapchainImages(sc, cnt, &cnt, (XrSwapchainImageBaseHeader*)im.data());
        for (auto& i : im) imgs.push_back(i.texture);
    } else {
        std::vector<XrSwapchainImageD3D12KHR> im(cnt, { XR_TYPE_SWAPCHAIN_IMAGE_D3D12_KHR });
        f.EnumerateSwapchainImages(sc, cnt, &cnt, (XrSwapchainImageBaseHeader*)im.data());
        for (auto& i : im) imgs.push_back(i.texture);
    }
    Log("panel: swapchain %dx%d fmt %lld, %u images, api %d", W, H, (long long)fmt, cnt, g.api);
    return true;
}

bool Panel::Upload(Gfx& g, const XrFns& f, const std::vector<uint32_t>& px) {
    if (sc == XR_NULL_HANDLE) return false;
    std::vector<uint32_t> tmp;
    const uint32_t* src = px.data();
    if (swapRB) {
        tmp.resize(px.size());
        for (size_t i = 0; i < px.size(); i++) { uint32_t c = px[i]; tmp[i] = (c & 0xFF00FF00) | ((c >> 16) & 0xFF) | ((c & 0xFF) << 16); }
        src = tmp.data();
    }
    uint32_t idx = 0;
    XrSwapchainImageAcquireInfo ai{ XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
    if (XR_FAILED(f.AcquireSwapchainImage(sc, &ai, &idx))) return false;
    XrSwapchainImageWaitInfo wi{ XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO }; wi.timeout = XR_INFINITE_DURATION;
    f.WaitSwapchainImage(sc, &wi);

    if (g.api == 11) {
        g.ctx->UpdateSubresource((ID3D11Texture2D*)imgs[idx], 0, nullptr, src, w * 4, 0);
    } else if (g.api == 12 && g.list) {
        auto* tex = (ID3D12Resource*)imgs[idx];
        D3D12_RESOURCE_DESC desc = tex->GetDesc();
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT fp{}; UINT64 total = 0;
        g.d12->GetCopyableFootprints(&desc, 0, 1, 0, &fp, nullptr, nullptr, &total);
        if (!upload) {
            D3D12_HEAP_PROPERTIES hp{ D3D12_HEAP_TYPE_UPLOAD };
            D3D12_RESOURCE_DESC bd{};
            bd.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER; bd.Width = total; bd.Height = 1; bd.DepthOrArraySize = 1;
            bd.MipLevels = 1; bd.SampleDesc.Count = 1; bd.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
            g.d12->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &bd, D3D12_RESOURCE_STATE_GENERIC_READ, nullptr, IID_PPV_ARGS(&upload));
        }
        if (upload) {
            uint8_t* m = nullptr; D3D12_RANGE rr{ 0, 0 };
            upload->Map(0, &rr, (void**)&m);
            for (int y = 0; y < h; y++) memcpy(m + fp.Offset + (size_t)y * fp.Footprint.RowPitch, src + (size_t)y * w, (size_t)w * 4);
            upload->Unmap(0, nullptr);
            g.alloc->Reset();
            g.list->Reset(g.alloc, nullptr);
            D3D12_RESOURCE_BARRIER b{}; b.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
            b.Transition.pResource = tex; b.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
            b.Transition.StateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET; b.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
            g.list->ResourceBarrier(1, &b);
            D3D12_TEXTURE_COPY_LOCATION dst{ tex, D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX }; dst.SubresourceIndex = 0;
            D3D12_TEXTURE_COPY_LOCATION sl{ upload, D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT }; sl.PlacedFootprint = fp;
            g.list->CopyTextureRegion(&dst, 0, 0, 0, &sl, nullptr);
            std::swap(b.Transition.StateBefore, b.Transition.StateAfter);
            g.list->ResourceBarrier(1, &b);
            g.list->Close();
            ID3D12CommandList* lists[] = { g.list };
            g.queue->ExecuteCommandLists(1, lists);
            g.queue->Signal(g.fence, ++g.fenceVal);
            if (g.fence->GetCompletedValue() < g.fenceVal) { g.fence->SetEventOnCompletion(g.fenceVal, g.ev); WaitForSingleObject(g.ev, 1000); }
        }
    }
    XrSwapchainImageReleaseInfo ri{ XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
    f.ReleaseSwapchainImage(sc, &ri);
    ready = true;
    return true;
}

void Panel::Destroy(const XrFns& f) {
    if (sc != XR_NULL_HANDLE && f.DestroySwapchain) f.DestroySwapchain(sc);
    sc = XR_NULL_HANDLE;
    if (upload) upload->Release();
    upload = nullptr;
    imgs.clear(); ready = false;
}

// ---------- drawing ----------
static ULONG_PTR gGdiToken = 0;
static std::map<std::wstring, std::unique_ptr<Image>> gIcons;

static void EnsureGdi() {
    if (gGdiToken) return;
    GdiplusStartupInput in;
    GdiplusStartup(&gGdiToken, &in, nullptr);
}
void DrawShutdown() {
    gIcons.clear();
    if (gGdiToken) GdiplusShutdown(gGdiToken);
    gGdiToken = 0;
}

static Image* Icon(const std::string& path) {
    if (path.empty()) return nullptr;
    std::wstring p = Utf8ToW(path);
    if (p.find(L':') == std::wstring::npos) p = ProfilesDir() + L"\\icons\\" + p;
    auto it = gIcons.find(p);
    if (it != gIcons.end()) return it->second.get();
    std::unique_ptr<Image> img(Image::FromFile(p.c_str()));
    if (img && img->GetLastStatus() != Ok) img.reset();
    if (!img) Log("icon not found: %s", WToUtf8(p).c_str());
    Image* raw = img.get();
    gIcons[p] = std::move(img);
    return raw;
}

static void Grab(Bitmap& bmp, std::vector<uint32_t>& out, int w, int h) {
    out.resize((size_t)w * h);
    BitmapData bd; Rect r(0, 0, w, h);
    if (bmp.LockBits(&r, ImageLockModeRead, PixelFormat32bppPARGB, &bd) == Ok) {
        for (int y = 0; y < h; y++) memcpy(&out[(size_t)y * w], (uint8_t*)bd.Scan0 + (size_t)y * bd.Stride, (size_t)w * 4);
        bmp.UnlockBits(&bd);
    }
}

static std::unique_ptr<Font> MakeFont(const Style& st, float px, int style) {
    FontFamily fam(Utf8ToW(st.font).c_str());
    if (fam.GetLastStatus() != Ok) return std::make_unique<Font>(FontFamily::GenericSansSerif(), px, style, UnitPixel);
    return std::make_unique<Font>(&fam, px, style, UnitPixel);
}

void DrawWheel(std::vector<uint32_t>& out, int S, const Profile& p, const WheelView& v) {
    EnsureGdi();
    const Style& st = p.style;
    Bitmap bmp(S, S, PixelFormat32bppPARGB);
    Graphics g(&bmp);
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetTextRenderingHint(TextRenderingHintAntiAliasGridFit);
    g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
    g.Clear(Color(0, 0, 0, 0));

    float c = S / 2.f, R = S / 2.f - 4, r = R * st.innerRatio;
    SolidBrush bg{ Color(st.bg) };
    g.FillEllipse(&bg, c - R, c - R, 2 * R, 2 * R);

    const Wheel* w = v.wheel;
    int n = w ? (int)w->items.size() : 0;
    float seg = n ? 360.f / n : 360.f;
    auto font = MakeFont(st, S * 0.045f * st.fontScale, FontStyleBold);
    auto smallFont = MakeFont(st, S * 0.034f * st.fontScale, FontStyleRegular);
    StringFormat fmt; fmt.SetAlignment(StringAlignmentCenter); fmt.SetLineAlignment(StringAlignmentCenter);
    SolidBrush textB{ Color(st.text) }, accentB{ Color(st.accent) };
    Pen border(Color(st.border), 2.f);

    for (int i = 0; i < n; i++) {
        const WheelItem& it = w->items[i];
        float mid = -90.f + i * seg;
        float a0 = mid - seg / 2 + st.gapDeg / 2, sw = seg - st.gapDeg;
        GraphicsPath path;
        float ro = R - 6, ri = r + 4;
        path.AddArc(c - ro, c - ro, 2 * ro, 2 * ro, a0, sw);
        path.AddArc(c - ri, c - ri, 2 * ri, 2 * ri, a0 + sw, -sw);
        path.CloseFigure();
        bool hov = i == v.hovered;
        bool isOn = i < (int)v.on.size() && v.on[i];
        uint32_t col = hov ? st.hover : isOn ? st.toggled : (it.hasColor ? it.color : st.segment);
        SolidBrush sb{ Color(col) };
        g.FillPath(&sb, &path);
        if (hov) g.DrawPath(&border, &path);

        float rad = (mid) * 3.14159265f / 180.f;
        float tr = (ro + ri) / 2;
        float tx = c + std::cos(rad) * tr, ty = c + std::sin(rad) * tr;
        float boxW = std::min((ro - ri) * 1.25f, 2 * tr * std::sin(std::min(seg, 179.f) * 3.14159265f / 360.f) * 0.9f);
        float boxH = (ro - ri) * 0.9f;
        Image* icon = Icon(it.icon);
        if (icon) {
            float is = boxH * 0.45f;
            g.DrawImage(icon, tx - is / 2, ty - boxH * 0.42f, is, is);
            RectF tb(tx - boxW / 2, ty + boxH * 0.05f, boxW, boxH * 0.4f);
            g.DrawString(Utf8ToW(it.label).c_str(), -1, smallFont.get(), tb, &fmt, &textB);
        } else {
            RectF tb(tx - boxW / 2, ty - boxH / 2, boxW, boxH);
            g.DrawString(Utf8ToW(it.label).c_str(), -1, font.get(), tb, &fmt, &textB);
        }
        // Small marker for submenus.
        if (it.action.type == ActType::Wheel) {
            float mr = ro - 14;
            g.FillEllipse(&accentB, c + std::cos(rad) * mr - 5, c + std::sin(rad) * mr - 5, 10.f, 10.f);
        }
    }

    // Centre: title or hovered description.
    SolidBrush centerB{ Color(0xE0101014) };
    g.FillEllipse(&centerB, c - r, c - r, 2 * r, 2 * r);
    g.DrawEllipse(&border, c - r, c - r, 2 * r, 2 * r);
    std::string top = v.crumb, bottom;
    if (w && v.hovered >= 0 && v.hovered < n) {
        top = w->items[v.hovered].label;
        bottom = w->items[v.hovered].description;
    }
    if (n == 0) top = "(empty wheel)";
    RectF t1(c - r * 0.9f, c - r * 0.55f, r * 1.8f, r * (bottom.empty() ? 1.1f : 0.6f));
    g.DrawString(Utf8ToW(top).c_str(), -1, font.get(), t1, &fmt, bottom.empty() ? &accentB : &textB);
    if (!bottom.empty()) {
        RectF t2(c - r * 0.85f, c + r * 0.05f, r * 1.7f, r * 0.6f);
        g.DrawString(Utf8ToW(bottom).c_str(), -1, smallFont.get(), t2, &fmt, &accentB);
    }
    Grab(bmp, out, S, S);
}

void DrawToast(std::vector<uint32_t>& out, int W, int H, const std::string& text, const Style& st) {
    EnsureGdi();
    Bitmap bmp(W, H, PixelFormat32bppPARGB);
    Graphics g(&bmp);
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetTextRenderingHint(TextRenderingHintAntiAliasGridFit);
    g.Clear(Color(0, 0, 0, 0));
    GraphicsPath path;
    float rr = H * 0.4f;
    path.AddArc(2.f, 2.f, rr, rr, 180, 90);
    path.AddArc(W - rr - 2.f, 2.f, rr, rr, 270, 90);
    path.AddArc(W - rr - 2.f, H - rr - 2.f, rr, rr, 0, 90);
    path.AddArc(2.f, H - rr - 2.f, rr, rr, 90, 90);
    path.CloseFigure();
    SolidBrush bg{ Color(st.bg | 0x40000000u) };
    g.FillPath(&bg, &path);
    Pen pen(Color(st.accent), 2.f);
    g.DrawPath(&pen, &path);
    auto font = MakeFont(st, H * 0.38f, FontStyleBold);
    StringFormat fmt; fmt.SetAlignment(StringAlignmentCenter); fmt.SetLineAlignment(StringAlignmentCenter);
    SolidBrush tb{ Color(st.text) };
    RectF rc(0, 0, (float)W, (float)H);
    g.DrawString(Utf8ToW(text).c_str(), -1, font.get(), rc, &fmt, &tb);
    Grab(bmp, out, W, H);
}


// Touch cursor: a ring that thickens and brightens with closeness (level 0..7), a filled dot when touching (8).
void DrawCursor(std::vector<uint32_t>& out, int S, int level, const Style& st) {
    EnsureGdi();
    Bitmap bmp(S, S, PixelFormat32bppPARGB);
    Graphics g(&bmp);
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.Clear(Color(0, 0, 0, 0));
    float k = level / 8.f;
    float pad = S * 0.08f, d = S - 2 * pad;
    if (level >= 8) {
        SolidBrush fill{ Color(st.accent) };
        g.FillEllipse(&fill, pad, pad, d, d);
        Pen edge(Color(0xFFFFFFFF), S * 0.06f);
        g.DrawEllipse(&edge, pad, pad, d, d);
    } else {
        BYTE a = (BYTE)(90 + 150 * k);
        uint32_t c = st.accent;
        Pen ring(Color(a, (BYTE)(c >> 16), (BYTE)(c >> 8), (BYTE)c), S * (0.05f + 0.07f * k));
        g.DrawEllipse(&ring, pad, pad, d, d);
        SolidBrush dot{ Color((BYTE)(60 + 120 * k), 255, 255, 255) };
        float r = S * 0.06f;
        g.FillEllipse(&dot, S / 2.f - r, S / 2.f - r, 2 * r, 2 * r);
    }
    Grab(bmp, out, S, S);
}

// Laser pointer: a white dot with an accent ring, and a beam that fades toward the controller.
void DrawPointerDot(std::vector<uint32_t>& out, int S, const Style& st) {
    EnsureGdi();
    Bitmap bmp(S, S, PixelFormat32bppPARGB);
    Graphics g(&bmp);
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.Clear(Color(0, 0, 0, 0));
    float pad = S * 0.1f, d = S - 2 * pad;
    Pen ring(Color(st.accent), S * 0.12f);
    g.DrawEllipse(&ring, pad, pad, d, d);
    SolidBrush dot{ Color(255, 255, 255, 255) };
    float r = S * 0.22f;
    g.FillEllipse(&dot, S / 2.f - r, S / 2.f - r, 2 * r, 2 * r);
    Grab(bmp, out, S, S);
}
void DrawBeam(std::vector<uint32_t>& out, int W, int H, const Style& st) {
    out.assign((size_t)W * H, 0);
    uint32_t c = st.accent;
    for (int y = 0; y < H; y++) {
        // texture top = far end (hit), bottom = controller: fade out toward the controller
        float along = 1.f - (float)y / (H - 1);
        float a = 0.15f + 0.55f * (1.f - along);
        for (int x = 0; x < W; x++) {
            float across = 1.f - std::fabs((x + 0.5f) / W * 2.f - 1.f); // soft edges
            float A = a * std::min(1.f, across * 1.6f);
            uint32_t ai = (uint32_t)(A * 255.f);
            uint32_t r = ((c >> 16) & 255) * ai / 255, g = ((c >> 8) & 255) * ai / 255, b = (c & 255) * ai / 255;
            out[(size_t)y * W + x] = (ai << 24) | (r << 16) | (g << 8) | b;
        }
    }
}

void DrawKeyboard(std::vector<uint32_t>& out, int W, int Hp, const KeyboardView& kb, const Style& st) {
    EnsureGdi();
    Bitmap bmp(W, Hp, PixelFormat32bppPARGB);
    Graphics g(&bmp);
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetTextRenderingHint(TextRenderingHintAntiAliasGridFit);
    g.Clear(Color(0, 0, 0, 0));
    float uw, uh; const auto& keys = KbLayout(uw, uh);
    float pad = W * 0.006f;
    float sx = (W - 2 * pad) / uw, sy = (Hp - 2 * pad) / uh;

    auto roundRect = [](GraphicsPath& p, float x, float y, float w, float h, float r) {
        p.AddArc(x, y, r, r, 180, 90); p.AddArc(x + w - r, y, r, r, 270, 90);
        p.AddArc(x + w - r, y + h - r, r, r, 0, 90); p.AddArc(x, y + h - r, r, r, 90, 90); p.CloseFigure();
    };
    GraphicsPath bgp; roundRect(bgp, 0, 0, (float)W - 1, (float)Hp - 1, sy * 0.5f);
    SolidBrush bg{ Color(st.bg) };
    g.FillPath(&bg, &bgp);

    auto font = MakeFont(st, sy * 0.36f * st.fontScale, FontStyleBold);
    auto smallFont = MakeFont(st, sy * 0.26f * st.fontScale, FontStyleBold);
    StringFormat fmt; fmt.SetAlignment(StringAlignmentCenter); fmt.SetLineAlignment(StringAlignmentCenter);
    SolidBrush textB{ Color(st.text) }, accentB{ Color(st.accent) };
    Pen border(Color(st.border), 2.f);
    bool showUpper = kb.shift != kb.caps;

    for (int i = 0; i < (int)keys.size(); i++) {
        const KbKey& k = keys[i];
        float x = pad + k.x * sx + sx * 0.05f, y = pad + k.y * sy + sy * 0.06f;
        float w = k.w * sx - sx * 0.10f, h = k.h * sy - sy * 0.12f;
        bool hov = kb.hover[0] == i || kb.hover[1] == i;
        bool latched = (k.kind == KB_SHIFT && kb.shift) || (k.kind == KB_CAPS && kb.caps) ||
                       (k.kind == KB_CTRL && kb.ctrl) || (k.kind == KB_ALT && kb.alt);
        uint32_t col = st.segment;
        if (latched) col = st.accent & 0xA0FFFFFF;
        if (hov) col = st.hover;
        if (kb.pressed == i) col = 0xFFFFFFFF;
        GraphicsPath kp; roundRect(kp, x, y, w, h, sy * 0.22f);
        SolidBrush kbB{ Color(col) };
        g.FillPath(&kbB, &kp);
        if (hov) g.DrawPath(&border, &kp);
        std::string label = k.label;
        if (k.letter) label = showUpper ? k.shiftLabel : k.label;
        else if (k.kind == KB_KEY && kb.shift) label = k.shiftLabel;
        SolidBrush darkB{ Color(0xFF101418) };
        RectF rc(x, y, w, h);
        bool big = label.size() <= 2 || (unsigned char)label[0] >= 0x80;
        g.DrawString(Utf8ToW(label).c_str(), -1, big ? font.get() : smallFont.get(), rc, &fmt,
                     kb.pressed == i ? &darkB : (latched ? &darkB : &textB));
    }
    Grab(bmp, out, W, Hp);
}

void DrawWrist(std::vector<uint32_t>& out, int S, const Profile& p, const WristStyle& ws, const WristView& v) {
    EnsureGdi();
    Bitmap bmp(S, S, PixelFormat32bppPARGB);
    Graphics g(&bmp);
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetTextRenderingHint(TextRenderingHintAntiAliasGridFit);
    g.Clear(Color(0, 0, 0, 0));
    if (!v.wheel) { Grab(bmp, out, S, S); return; }
    const Style& st = p.style;
    int n = (int)v.wheel->items.size() + (v.nested ? 1 : 0);
    std::vector<Cell> cells; Cell hd;
    WristLayout(n, ws.columns, cells, hd);
    auto roundRect = [](GraphicsPath& gp, float x, float y, float w, float h, float r) {
        gp.AddArc(x, y, r, r, 180, 90); gp.AddArc(x + w - r, y, r, r, 270, 90);
        gp.AddArc(x + w - r, y + h - r, r, r, 0, 90); gp.AddArc(x, y + h - r, r, r, 90, 90); gp.CloseFigure();
    };
    float bottom = hd.y + hd.h;
    for (auto& c : cells) bottom = std::max(bottom, c.y + c.h);
    GraphicsPath bgp; roundRect(bgp, 1, 1, S - 2.f, (bottom + 0.03f) * S - 2.f, S * 0.06f);
    SolidBrush bg{ Color(st.bg) };
    g.FillPath(&bg, &bgp);
    Pen edge(Color(st.border), 2.f);
    g.DrawPath(&edge, &bgp);

    StringFormat fmt; fmt.SetAlignment(StringAlignmentCenter); fmt.SetLineAlignment(StringAlignmentCenter);
    SolidBrush textB{ Color(st.text) }, accentB{ Color(st.accent) }, darkB{ Color(0xFF101418) };
    auto headFont = MakeFont(st, S * 0.055f * st.fontScale, FontStyleBold);
    RectF hr(hd.x * S, hd.y * S, hd.w * S, hd.h * S);
    g.DrawString(Utf8ToW(v.adjust ? std::string("Adjust: grip to move") : v.wheel->title).c_str(), -1, headFont.get(), hr, &fmt, &accentB);

    float cellPx = cells.empty() ? 0 : cells[0].h * S;
    auto font = MakeFont(st, std::max(10.f, cellPx * 0.17f) * st.fontScale, FontStyleBold);
    Pen hovPen(Color(st.border | 0xFF000000u), 3.f);
    for (int i = 0; i < (int)cells.size(); i++) {
        const Cell& c = cells[i];
        float x = c.x * S, y = c.y * S, w = c.w * S, h = c.h * S;
        bool back = v.nested && i == 0;
        const WheelItem* it = back ? nullptr : &v.wheel->items[i - (v.nested ? 1 : 0)];
        uint32_t col = (it && it->hasColor) ? it->color : st.segment;
        bool hov = v.hover == i;
        bool isOn = i < (int)v.on.size() && v.on[i];
        if (isOn) col = st.toggled;
        if (hov) col = st.hover;
        if (v.pressed == i) col = 0xFFFFFFFF;
        GraphicsPath cp; roundRect(cp, x, y, w, h, h * 0.22f);
        SolidBrush cb{ Color(col) };
        g.FillPath(&cb, &cp);
        if (hov) g.DrawPath(&hovPen, &cp);
        std::string label = back ? "\xE2\x80\xB9 Back" : it->label;
        Image* icon = it ? Icon(it->icon) : nullptr;
        RectF rc(x + 2, y + 2, w - 4, h - 4);
        if (icon) {
            float is = h * 0.48f;
            g.DrawImage(icon, x + w / 2 - is / 2, y + h * 0.08f, is, is);
            rc = RectF(x + 2, y + h * 0.58f, w - 4, h * 0.40f);
        }
        g.DrawString(Utf8ToW(label).c_str(), -1, font.get(), rc, &fmt, v.pressed == i ? &darkB : &textB);
    }
    Grab(bmp, out, S, S);
}
