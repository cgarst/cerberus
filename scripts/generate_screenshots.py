#!/usr/bin/env python3
"""Generate framed README screenshots for Cerberus (phone, tablet, and WPF config UI).

Serves CerberusService/WebUI on a temporary local port with ?demo=1, drives Playwright
for phone and tablet viewports, launches CerberusService.exe --demo-setup for the
native WPF configuration window, and composites each capture with device/window
bezels, drop shadows, and a soft gradient backdrop (adapted from MajesticTab).

Usage:
    pip install playwright pillow && playwright install chromium
    python scripts/generate_screenshots.py
    python scripts/generate_screenshots.py --only phone tablet config
"""
import argparse
import ctypes
import ctypes.wintypes
import functools
import http.server
import socket
import subprocess
import sys
import threading
import time
import urllib.parse
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
WEBUI_DIR = ROOT / "CerberusService" / "WebUI"
OUT_DIR = ROOT / "docs" / "screenshots"
SERVICE_EXE = ROOT / "build" / "CerberusService.exe"
SERVICE_DLL = ROOT / "CerberusService" / "bin" / "Release" / "net10.0-windows" / "win-x64" / "CerberusService.dll"

# Cerberus dark Steam-inspired backdrop palette
BG_TOP = (18, 28, 44)
BG_BOTTOM = (10, 14, 22)
GLOW = (45, 145, 220)

WIN_FONTS = ("C:/Windows/Fonts/segoeuib.ttf", "C:/Windows/Fonts/segoeui.ttf")


def load_font(size, bold=True):
    from PIL import ImageFont

    path = WIN_FONTS[0] if bold else WIN_FONTS[1]
    if Path(path).exists():
        return ImageFont.truetype(path, size)
    return ImageFont.load_default()


class _QuietHandler(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def translate_path(self, path):
        rel = urllib.parse.urlsplit(path).path.lstrip("/")
        target = WEBUI_DIR / urllib.parse.unquote(rel)
        if target.is_dir():
            index = target / "index.html"
            if index.exists():
                return str(index)
        if not target.exists() and (WEBUI_DIR / f"{rel}.html").exists():
            return str(WEBUI_DIR / f"{rel}.html")
        return str(target)


class _Server(http.server.ThreadingHTTPServer):
    request_queue_size = 128


def start_server():
    sock = socket.socket()
    sock.bind(("127.0.0.1", 0))
    port = sock.getsockname()[1]
    sock.close()
    httpd = _Server(("127.0.0.1", port), _QuietHandler)
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    return httpd, f"http://127.0.0.1:{port}"


def make_backdrop(width, height):
    from PIL import Image, ImageChops, ImageDraw, ImageFilter

    k = min(width, height) / 1080
    bg = Image.new("RGB", (width, height))
    px = ImageDraw.Draw(bg)
    for y in range(height):
        t = y / max(1, height - 1)
        px.line(
            [(0, y), (width, y)],
            fill=tuple(round(a + (b - a) * t) for a, b in zip(BG_TOP, BG_BOTTOM)),
        )
    glow = Image.new("RGB", (width, height), (0, 0, 0))
    ImageDraw.Draw(glow).ellipse(
        [width * 0.08, height * 0.18, width * 0.92, height * 0.95],
        fill=tuple(int(c * 0.28) for c in GLOW),
    )
    return ImageChops.add(bg, glow.filter(ImageFilter.GaussianBlur(150 * k)))


def compose_device_frame(raw_path, out_path, canvas_size, headline, subtitle, is_phone=False):
    """Compose a captured web screenshot into a rounded device bezel with backdrop & copy."""
    from PIL import Image, ImageDraw, ImageFilter

    W, H = canvas_size
    landscape = W > H
    k = min(W, H) / 1080
    bg = make_backdrop(W, H)
    draw = ImageDraw.Draw(bg)

    margin = 60 * k
    head_sz, sub_sz = (52, 30) if landscape else (62, 36)
    head_f, sub_f = load_font(round(head_sz * k), bold=True), load_font(round(sub_sz * k), bold=False)

    y = (48 if landscape else 92) * k
    draw.text((W / 2, y), headline, font=head_f, fill=(255, 255, 255), anchor="ma")
    y += (64 if landscape else 78) * k
    draw.text((W / 2, y), subtitle, font=sub_f, fill=(176, 198, 224), anchor="ma")
    y += (42 if landscape else 54) * k

    shot = Image.open(raw_path).convert("RGB")
    bezel = round((14 if is_phone else 14) * k)
    radius = round((58 if is_phone else 30) * k)
    top = round(y + (26 if landscape else 44) * k)
    bottom_pad = round(42 * k)

    avail_w = W - 2 * (bezel + round(44 * k))
    avail_h = H - top - bottom_pad - 2 * bezel
    scale = min(avail_w / shot.width, avail_h / shot.height)
    dev_w = round(shot.width * scale)
    dev_h = round(shot.height * scale)
    shot = shot.resize((dev_w, dev_h), Image.LANCZOS)

    frame_w, frame_h = dev_w + 2 * bezel, dev_h + 2 * bezel
    mask = Image.new("L", (frame_w, frame_h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, frame_w, frame_h], radius + bezel, fill=255)

    frame = Image.new("RGB", (frame_w, frame_h), (14, 17, 26))
    # Subtle outer metallic rim highlight
    ImageDraw.Draw(frame).rounded_rectangle(
        [0, 0, frame_w - 1, frame_h - 1],
        radius + bezel,
        outline=(54, 68, 92),
        width=max(2, round(2 * k)),
    )

    inner_mask = Image.new("L", (dev_w, dev_h), 0)
    ImageDraw.Draw(inner_mask).rounded_rectangle([0, 0, dev_w, dev_h], radius, fill=255)
    frame.paste(shot, (bezel, bezel), inner_mask)

    left = (W - frame_w) // 2
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 185), (left, top + round(22 * k)), mask)
    bg = Image.alpha_composite(
        bg.convert("RGBA"), shadow.filter(ImageFilter.GaussianBlur(34 * k))
    ).convert("RGB")
    bg.paste(frame, (left, top), mask)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.unlink(missing_ok=True)
    bg.save(out_path, optimize=True)


def compose_window_frame(raw_path, out_path, headline, subtitle):
    """Compose the native WPF Config UI capture on the Cerberus backdrop."""
    from PIL import Image, ImageDraw, ImageFilter

    shot = Image.open(raw_path).convert("RGB")
    W, H = 1920, 1200
    k = min(W, H) / 1080
    bg = make_backdrop(W, H)
    draw = ImageDraw.Draw(bg)

    head_f = load_font(round(50 * k), bold=True)
    sub_f = load_font(round(28 * k), bold=False)
    y = 48 * k
    draw.text((W / 2, y), headline, font=head_f, fill=(255, 255, 255), anchor="ma")
    y += 62 * k
    draw.text((W / 2, y), subtitle, font=sub_f, fill=(176, 198, 224), anchor="ma")
    y += 42 * k

    top = round(y + 24 * k)
    avail_w = W - round(120 * k)
    avail_h = H - top - round(56 * k)
    scale = min(avail_w / shot.width, avail_h / shot.height)
    win_w, win_h = round(shot.width * scale), round(shot.height * scale)
    shot = shot.resize((win_w, win_h), Image.LANCZOS)

    radius = round(14 * k)
    mask = Image.new("L", (win_w, win_h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, win_w, win_h], radius, fill=255)

    left = (W - win_w) // 2
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 190), (left, top + round(20 * k)), mask)
    bg = Image.alpha_composite(
        bg.convert("RGBA"), shadow.filter(ImageFilter.GaussianBlur(32 * k))
    ).convert("RGB")
    bg.paste(shot, (left, top), mask)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.unlink(missing_ok=True)
    bg.save(out_path, optimize=True)


def capture_web_shots(only):
    from playwright.sync_api import sync_playwright

    if not WEBUI_DIR.exists():
        raise SystemExit(f"WebUI directory not found: {WEBUI_DIR}. Run scripts/export-web.sh first.")

    httpd, base = start_server()
    raw_dir = OUT_DIR / "raw"
    raw_dir.mkdir(parents=True, exist_ok=True)

    try:
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True)

            if "phone" in only:
                ctx = browser.new_context(
                    viewport={"width": 390, "height": 844},
                    device_scale_factor=3,
                    is_mobile=True,
                    has_touch=True,
                )
                page = ctx.new_page()
                page.goto(f"{base}/?demo=1", wait_until="networkidle", timeout=60000)
                page.wait_for_timeout(2800)
                raw = raw_dir / "phone-home.png"
                page.screenshot(path=str(raw))
                compose_device_frame(
                    raw,
                    OUT_DIR / "phone-home.png",
                    (1080, 1920),
                    "Control your PC from anywhere",
                    "Switch rooms, monitor live FPS & temps, and launch games",
                    is_phone=True,
                )
                print(f"Wrote {OUT_DIR / 'phone-home.png'}")
                ctx.close()

            if "tablet" in only:
                ctx = browser.new_context(
                    viewport={"width": 1280, "height": 800},
                    device_scale_factor=2,
                    is_mobile=True,
                    has_touch=True,
                )
                page = ctx.new_page()
                page.goto(f"{base}/?demo=1", wait_until="networkidle", timeout=60000)
                page.wait_for_timeout(2800)
                page.evaluate("""() => {
                    const scroller = Array.from(document.querySelectorAll('div')).find(
                        el => el.scrollHeight > el.clientHeight + 50 && getComputedStyle(el).overflowY !== 'visible'
                    );
                    if (scroller) scroller.scrollTop = 260;
                    else window.scrollTo(0, 260);
                }""")
                page.wait_for_timeout(300)
                raw = raw_dir / "tablet-dashboard.png"
                page.screenshot(path=str(raw))
                compose_device_frame(
                    raw,
                    OUT_DIR / "tablet-dashboard.png",
                    (2200, 1480),
                    "Full dashboard on tablet & desktop",
                    "Room profiles, live hardware telemetry, windows, and game shortcuts",
                    is_phone=False,
                )
                print(f"Wrote {OUT_DIR / 'tablet-dashboard.png'}")
                ctx.close()

            if "idle" in only:
                ctx = browser.new_context(
                    viewport={"width": 390, "height": 844},
                    device_scale_factor=3,
                    is_mobile=True,
                    has_touch=True,
                )
                page = ctx.new_page()
                page.goto(f"{base}/?demo=1&mode=office&idle=1", wait_until="networkidle", timeout=60000)
                page.wait_for_timeout(2800)
                raw = raw_dir / "phone-idle.png"
                page.screenshot(path=str(raw))
                compose_device_frame(
                    raw,
                    OUT_DIR / "phone-idle.png",
                    (1080, 1920),
                    "One tap to switch rooms",
                    "Start a living room session from your phone when your PC is idle",
                    is_phone=True,
                )
                print(f"Wrote {OUT_DIR / 'phone-idle.png'}")
                ctx.close()

            if "streaming" in only:
                ctx = browser.new_context(
                    viewport={"width": 390, "height": 844},
                    device_scale_factor=3,
                    is_mobile=True,
                    has_touch=True,
                )
                page = ctx.new_page()
                page.goto(f"{base}/?demo=1&streaming=1", wait_until="networkidle", timeout=60000)
                page.wait_for_timeout(2800)
                raw = raw_dir / "phone-streaming.png"
                page.screenshot(path=str(raw))
                compose_device_frame(
                    raw,
                    OUT_DIR / "phone-streaming.png",
                    (1080, 1920),
                    "Streaming session aware",
                    "Pauses auto-revert during Sunshine & Apollo streams",
                    is_phone=True,
                )
                print(f"Wrote {OUT_DIR / 'phone-streaming.png'}")
                ctx.close()

            browser.close()
    finally:
        httpd.shutdown()


def _capture_hwnd_printwindow(hwnd):
    """Capture a Win32 window (including non-client title bar & controls) via PrintWindow."""
    from PIL import Image

    user32 = ctypes.windll.user32
    gdi32 = ctypes.windll.gdi32
    dwmapi = ctypes.windll.dwmapi

    win_rect = ctypes.wintypes.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(win_rect))
    w = win_rect.right - win_rect.left
    h = win_rect.bottom - win_rect.top
    if w <= 0 or h <= 0:
        raise RuntimeError(f"Invalid window dimensions: {w}x{h}")

    ext_rect = ctypes.wintypes.RECT()
    if dwmapi.DwmGetWindowAttribute(hwnd, 9, ctypes.byref(ext_rect), ctypes.sizeof(ext_rect)) != 0:
        ext_rect = win_rect

    hdc_window = user32.GetWindowDC(hwnd)
    hdc_mem = gdi32.CreateCompatibleDC(hdc_window)
    hbmp = gdi32.CreateCompatibleBitmap(hdc_window, w, h)
    old_obj = gdi32.SelectObject(hdc_mem, hbmp)

    try:
        # PW_RENDERFULLCONTENT = 2 captures WPF / DWM / Fluent dark theme + title bar controls
        ok = user32.PrintWindow(hwnd, hdc_mem, 2)
        if not ok:
            ok = user32.PrintWindow(hwnd, hdc_mem, 0)
        if not ok:
            raise RuntimeError("PrintWindow failed for Config UI window.")

        class BITMAPINFOHEADER(ctypes.Structure):
            _fields_ = [
                ("biSize", ctypes.wintypes.DWORD),
                ("biWidth", ctypes.wintypes.LONG),
                ("biHeight", ctypes.wintypes.LONG),
                ("biPlanes", ctypes.wintypes.WORD),
                ("biBitCount", ctypes.wintypes.WORD),
                ("biCompression", ctypes.wintypes.DWORD),
                ("biSizeImage", ctypes.wintypes.DWORD),
                ("biXPelsPerMeter", ctypes.wintypes.LONG),
                ("biYPelsPerMeter", ctypes.wintypes.LONG),
                ("biClrUsed", ctypes.wintypes.DWORD),
                ("biClrImportant", ctypes.wintypes.DWORD),
            ]

        bmi = BITMAPINFOHEADER()
        bmi.biSize = ctypes.sizeof(BITMAPINFOHEADER)
        bmi.biWidth = w
        bmi.biHeight = -h  # top-down DIB
        bmi.biPlanes = 1
        bmi.biBitCount = 32
        bmi.biCompression = 0  # BI_RGB

        buf = ctypes.create_string_buffer(w * h * 4)
        gdi32.GetDIBits(hdc_mem, hbmp, 0, h, buf, ctypes.byref(bmi), 0)
        img = Image.frombuffer("RGBA", (w, h), buf, "raw", "BGRA", 0, 1).convert("RGB")
    finally:
        gdi32.SelectObject(hdc_mem, old_obj)
        gdi32.DeleteObject(hbmp)
        gdi32.DeleteDC(hdc_mem)
        user32.ReleaseDC(hwnd, hdc_window)

    # Crop invisible DWM shadow margin using DWMWA_EXTENDED_FRAME_BOUNDS
    crop_left = max(0, ext_rect.left - win_rect.left)
    crop_top = max(0, ext_rect.top - win_rect.top)
    crop_right = min(w, ext_rect.right - win_rect.left)
    crop_bottom = min(h, ext_rect.bottom - win_rect.top)
    if crop_right > crop_left and crop_bottom > crop_top:
        img = img.crop((crop_left, crop_top, crop_right, crop_bottom))
    return img


def capture_config_ui():
    """Launch CerberusService --demo-setup and capture its dark-mode WPF window with title bar."""
    if sys.platform != "win32":
        print("Skipping config UI capture on non-Windows host.")
        return

    raw_dir = OUT_DIR / "raw"
    raw_dir.mkdir(parents=True, exist_ok=True)
    raw = raw_dir / "config-ui.png"
    raw.unlink(missing_ok=True)

    if SERVICE_DLL.exists():
        cmd = ["dotnet", str(SERVICE_DLL), "--demo-setup"]
    elif SERVICE_EXE.exists():
        cmd = [str(SERVICE_EXE), "--demo-setup"]
    else:
        raise SystemExit(f"Service build not found: {SERVICE_EXE}. Run scripts/build.ps1 first.")

    user32 = ctypes.windll.user32
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(2)
    except Exception:
        pass

    proc = subprocess.Popen(cmd)
    hwnd = None
    try:
        deadline = time.time() + 15
        while time.time() < deadline:
            found = user32.FindWindowW(None, "Cerberus Configuration")
            if found and user32.IsWindowVisible(found):
                hwnd = found
                break
            time.sleep(0.25)

        if not hwnd:
            raise RuntimeError("Timed out waiting for Cerberus Configuration window.")

        time.sleep(1.2)
        img = _capture_hwnd_printwindow(hwnd)
        img.save(raw)

        compose_window_frame(
            raw,
            OUT_DIR / "config-ui.png",
            "Native Windows Setup & Profiles",
            "Discover monitors & audio devices, configure rooms, and pair via QR code",
        )
        print(f"Wrote {OUT_DIR / 'config-ui.png'}")
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--only",
        nargs="*",
        choices=["phone", "idle", "tablet", "streaming", "config"],
        default=["phone", "idle", "tablet", "streaming", "config"],
        help="Subset of screenshots to generate",
    )
    ap.add_argument("--keep-raw", action="store_true", help="Keep unframed captures in docs/screenshots/raw/")
    args = ap.parse_args()

    web_targets = [x for x in args.only if x in ("phone", "idle", "tablet", "streaming")]
    if web_targets:
        capture_web_shots(web_targets)
    if "config" in args.only:
        capture_config_ui()

    raw_dir = OUT_DIR / "raw"
    if not args.keep_raw and raw_dir.exists():
        for f in raw_dir.glob("*.png"):
            f.unlink(missing_ok=True)
        try:
            raw_dir.rmdir()
        except OSError:
            pass


if __name__ == "__main__":
    main()
