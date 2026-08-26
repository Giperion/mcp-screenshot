#!/usr/bin/env python3
"""Local stdio MCP: screenshot screen/window, find window bounds, focus a window.

Windows-only. No third-party packages — ctypes + stdlib.
"""

from __future__ import annotations

import base64
import ctypes
import json
import os
import sys
import time
import zlib
from ctypes import wintypes
from typing import Any

PROTOCOL_VERSION = "2024-11-05"
SERVER_NAME = "screenshot"
SERVER_VERSION = "1.0.0"

# Downscale so the model actually receives the image. 0 = native pixels.
DEFAULT_MAX_WIDTH = 1920

# ---------------------------------------------------------------------------
# Win32
# ---------------------------------------------------------------------------

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
try:
    dwmapi = ctypes.WinDLL("dwmapi")
except OSError:
    dwmapi = None
try:
    shcore = ctypes.WinDLL("shcore")
except OSError:
    shcore = None

SRCCOPY = 0x00CC0020
COLORONCOLOR = 3
BI_RGB = 0
DIB_RGB_COLORS = 0
DWMWA_CLOAKED = 14
MONITORINFOF_PRIMARY = 1
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
GW_OWNER = 4
SW_RESTORE = 9
SW_SHOW = 5
SM_XVIRTUALSCREEN = 76
SM_YVIRTUALSCREEN = 77
SM_CXVIRTUALSCREEN = 78
SM_CYVIRTUALSCREEN = 79

HWND = wintypes.HWND
HDC = wintypes.HDC
HBITMAP = wintypes.HBITMAP
HMONITOR = wintypes.HMONITOR
BOOL = wintypes.BOOL
LPARAM = wintypes.LPARAM


class RECT(ctypes.Structure):
    _fields_ = [
        ("left", ctypes.c_long),
        ("top", ctypes.c_long),
        ("right", ctypes.c_long),
        ("bottom", ctypes.c_long),
    ]


class POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD),
        ("biWidth", ctypes.c_long),
        ("biHeight", ctypes.c_long),
        ("biPlanes", wintypes.WORD),
        ("biBitCount", wintypes.WORD),
        ("biCompression", wintypes.DWORD),
        ("biSizeImage", wintypes.DWORD),
        ("biXPelsPerMeter", ctypes.c_long),
        ("biYPelsPerMeter", ctypes.c_long),
        ("biClrUsed", wintypes.DWORD),
        ("biClrImportant", wintypes.DWORD),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wintypes.DWORD * 3)]


class MONITORINFO(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.DWORD),
        ("rcMonitor", RECT),
        ("rcWork", RECT),
        ("dwFlags", wintypes.DWORD),
    ]


WNDENUMPROC = ctypes.WINFUNCTYPE(BOOL, HWND, LPARAM)
MONITORENUMPROC = ctypes.WINFUNCTYPE(BOOL, HMONITOR, HDC, ctypes.POINTER(RECT), LPARAM)

user32.EnumWindows.argtypes = [WNDENUMPROC, LPARAM]
user32.EnumWindows.restype = BOOL
user32.IsWindow.argtypes = [HWND]
user32.IsWindow.restype = BOOL
user32.IsWindowVisible.argtypes = [HWND]
user32.IsWindowVisible.restype = BOOL
user32.IsIconic.argtypes = [HWND]
user32.IsIconic.restype = BOOL
user32.GetWindow.argtypes = [HWND, wintypes.UINT]
user32.GetWindow.restype = HWND
user32.GetWindowTextLengthW.argtypes = [HWND]
user32.GetWindowTextLengthW.restype = ctypes.c_int
user32.GetWindowTextW.argtypes = [HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetWindowTextW.restype = ctypes.c_int
user32.GetClassNameW.argtypes = [HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetClassNameW.restype = ctypes.c_int
user32.GetWindowRect.argtypes = [HWND, ctypes.POINTER(RECT)]
user32.GetWindowRect.restype = BOOL
user32.GetClientRect.argtypes = [HWND, ctypes.POINTER(RECT)]
user32.GetClientRect.restype = BOOL
user32.ClientToScreen.argtypes = [HWND, ctypes.POINTER(POINT)]
user32.ClientToScreen.restype = BOOL
user32.GetWindowThreadProcessId.argtypes = [HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetWindowThreadProcessId.restype = wintypes.DWORD
user32.GetDC.argtypes = [HWND]
user32.GetDC.restype = HDC
user32.ReleaseDC.argtypes = [HWND, HDC]
user32.ReleaseDC.restype = ctypes.c_int
user32.GetSystemMetrics.argtypes = [ctypes.c_int]
user32.GetSystemMetrics.restype = ctypes.c_int
user32.GetForegroundWindow.argtypes = []
user32.GetForegroundWindow.restype = HWND
user32.SetForegroundWindow.argtypes = [HWND]
user32.SetForegroundWindow.restype = BOOL
user32.BringWindowToTop.argtypes = [HWND]
user32.BringWindowToTop.restype = BOOL
user32.ShowWindow.argtypes = [HWND, ctypes.c_int]
user32.ShowWindow.restype = BOOL
user32.AttachThreadInput.argtypes = [wintypes.DWORD, wintypes.DWORD, BOOL]
user32.AttachThreadInput.restype = BOOL
user32.GetMonitorInfoW.argtypes = [HMONITOR, ctypes.POINTER(MONITORINFO)]
user32.GetMonitorInfoW.restype = BOOL
user32.EnumDisplayMonitors.argtypes = [HDC, ctypes.c_void_p, MONITORENUMPROC, LPARAM]
user32.EnumDisplayMonitors.restype = BOOL
user32.keybd_event.argtypes = [wintypes.BYTE, wintypes.BYTE, wintypes.DWORD, ctypes.c_size_t]
user32.keybd_event.restype = None

gdi32.CreateCompatibleDC.argtypes = [HDC]
gdi32.CreateCompatibleDC.restype = HDC
gdi32.CreateCompatibleBitmap.argtypes = [HDC, ctypes.c_int, ctypes.c_int]
gdi32.CreateCompatibleBitmap.restype = HBITMAP
gdi32.SelectObject.argtypes = [HDC, wintypes.HGDIOBJ]
gdi32.SelectObject.restype = wintypes.HGDIOBJ
gdi32.BitBlt.argtypes = [HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, HDC, ctypes.c_int, ctypes.c_int, wintypes.DWORD]
gdi32.BitBlt.restype = BOOL
gdi32.SetStretchBltMode.argtypes = [HDC, ctypes.c_int]
gdi32.SetStretchBltMode.restype = ctypes.c_int
gdi32.StretchBlt.argtypes = [
    HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
    HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.DWORD,
]
gdi32.StretchBlt.restype = BOOL
gdi32.GetDIBits.argtypes = [HDC, HBITMAP, wintypes.UINT, wintypes.UINT, ctypes.c_void_p, ctypes.POINTER(BITMAPINFO), wintypes.UINT]
gdi32.GetDIBits.restype = ctypes.c_int
gdi32.DeleteObject.argtypes = [wintypes.HGDIOBJ]
gdi32.DeleteObject.restype = BOOL
gdi32.DeleteDC.argtypes = [HDC]
gdi32.DeleteDC.restype = BOOL

kernel32.OpenProcess.argtypes = [wintypes.DWORD, BOOL, wintypes.DWORD]
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = BOOL
kernel32.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
kernel32.QueryFullProcessImageNameW.restype = BOOL
kernel32.GetCurrentThreadId.argtypes = []
kernel32.GetCurrentThreadId.restype = wintypes.DWORD

if dwmapi is not None:
    dwmapi.DwmGetWindowAttribute.argtypes = [HWND, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD]
    dwmapi.DwmGetWindowAttribute.restype = ctypes.c_long

if shcore is not None:
    try:
        shcore.SetProcessDpiAwareness(2)  # PROCESS_PER_MONITOR_DPI_AWARE
    except Exception:
        pass


def _hwnd_int(hwnd: int | HWND) -> int:
    if isinstance(hwnd, int):
        return hwnd
    value = ctypes.cast(hwnd, ctypes.c_void_p).value
    return int(value or 0)


def _as_hwnd(value: int) -> HWND:
    return HWND(value)


def parse_hwnd(value: Any) -> int:
    if isinstance(value, bool) or value is None:
        raise ValueError("hwnd is required")
    if isinstance(value, int):
        return value
    text = str(value).strip()
    if not text:
        raise ValueError("hwnd is empty")
    return int(text, 16) if text.lower().startswith("0x") else int(text)


def hwnd_hex(hwnd: int) -> str:
    return f"0x{hwnd:X}"


def _window_title(hwnd: int) -> str:
    length = user32.GetWindowTextLengthW(_as_hwnd(hwnd))
    if length <= 0:
        return ""
    buf = ctypes.create_unicode_buffer(length + 1)
    user32.GetWindowTextW(_as_hwnd(hwnd), buf, length + 1)
    return buf.value


def _window_class(hwnd: int) -> str:
    buf = ctypes.create_unicode_buffer(256)
    user32.GetClassNameW(_as_hwnd(hwnd), buf, 256)
    return buf.value


def _rect_dict(rect: RECT) -> dict[str, int]:
    return {
        "x": int(rect.left),
        "y": int(rect.top),
        "width": int(rect.right - rect.left),
        "height": int(rect.bottom - rect.top),
        "left": int(rect.left),
        "top": int(rect.top),
        "right": int(rect.right),
        "bottom": int(rect.bottom),
    }


def _client_rect_screen(hwnd: int) -> dict[str, int] | None:
    client = RECT()
    if not user32.GetClientRect(_as_hwnd(hwnd), ctypes.byref(client)):
        return None
    origin = POINT(0, 0)
    if not user32.ClientToScreen(_as_hwnd(hwnd), ctypes.byref(origin)):
        return None
    width = int(client.right - client.left)
    height = int(client.bottom - client.top)
    return {
        "x": int(origin.x),
        "y": int(origin.y),
        "width": width,
        "height": height,
        "left": int(origin.x),
        "top": int(origin.y),
        "right": int(origin.x + width),
        "bottom": int(origin.y + height),
    }


_process_name_cache: dict[int, str] = {}


def _process_name(pid: int) -> str:
    if pid in _process_name_cache:
        return _process_name_cache[pid]
    handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    name = ""
    if handle:
        try:
            size = wintypes.DWORD(32768)
            buf = ctypes.create_unicode_buffer(size.value)
            if kernel32.QueryFullProcessImageNameW(handle, 0, buf, ctypes.byref(size)):
                name = os.path.basename(buf.value)
        finally:
            kernel32.CloseHandle(handle)
    _process_name_cache[pid] = name
    return name


def _is_cloaked(hwnd: int) -> bool:
    if dwmapi is None:
        return False
    cloaked = wintypes.DWORD(0)
    status = dwmapi.DwmGetWindowAttribute(_as_hwnd(hwnd), DWMWA_CLOAKED, ctypes.byref(cloaked), ctypes.sizeof(cloaked))
    return status == 0 and cloaked.value != 0


def list_monitors() -> list[dict[str, Any]]:
    found: list[dict[str, Any]] = []

    def _callback(hmon: HMONITOR, _hdc: HDC, _lprc: Any, _lp: LPARAM) -> bool:
        info = MONITORINFO()
        info.cbSize = ctypes.sizeof(MONITORINFO)
        if user32.GetMonitorInfoW(hmon, ctypes.byref(info)):
            item = _rect_dict(info.rcMonitor)
            item["index"] = len(found) + 1
            item["primary"] = bool(info.dwFlags & MONITORINFOF_PRIMARY)
            found.append(item)
        return True

    callback = MONITORENUMPROC(_callback)
    user32.EnumDisplayMonitors(None, None, callback, 0)
    return found


def virtual_screen() -> dict[str, int]:
    x = user32.GetSystemMetrics(SM_XVIRTUALSCREEN)
    y = user32.GetSystemMetrics(SM_YVIRTUALSCREEN)
    w = user32.GetSystemMetrics(SM_CXVIRTUALSCREEN)
    h = user32.GetSystemMetrics(SM_CYVIRTUALSCREEN)
    return {"x": x, "y": y, "width": w, "height": h, "left": x, "top": y, "right": x + w, "bottom": y + h}


def _window_info(hwnd: int) -> dict[str, Any] | None:
    if not user32.IsWindow(_as_hwnd(hwnd)):
        return None
    rect = RECT()
    if not user32.GetWindowRect(_as_hwnd(hwnd), ctypes.byref(rect)):
        return None
    pid = wintypes.DWORD(0)
    user32.GetWindowThreadProcessId(_as_hwnd(hwnd), ctypes.byref(pid))
    info: dict[str, Any] = {
        "hwnd": hwnd_hex(hwnd),
        "title": _window_title(hwnd),
        "class_name": _window_class(hwnd),
        "pid": int(pid.value),
        "process": _process_name(int(pid.value)),
        "visible": bool(user32.IsWindowVisible(_as_hwnd(hwnd))),
        "minimized": bool(user32.IsIconic(_as_hwnd(hwnd))),
        "foreground": _hwnd_int(user32.GetForegroundWindow()) == hwnd,
        "rect": _rect_dict(rect),
        "client_rect": _client_rect_screen(hwnd),
    }
    return info


def iter_top_windows() -> list[dict[str, Any]]:
    windows: list[dict[str, Any]] = []

    def _callback(hwnd_obj: HWND, _lp: LPARAM) -> bool:
        hwnd = _hwnd_int(hwnd_obj)
        if not user32.IsWindowVisible(hwnd_obj):
            return True
        if user32.GetWindow(hwnd_obj, GW_OWNER):
            return True
        if _is_cloaked(hwnd):
            return True
        title = _window_title(hwnd)
        if not title:
            return True
        class_name = _window_class(hwnd)
        if class_name in ("Progman", "WorkerW", "Shell_TrayWnd"):
            return True
        info = _window_info(hwnd)
        if info is None:
            return True
        if info["rect"]["width"] <= 0 or info["rect"]["height"] <= 0:
            return True
        windows.append(info)
        return True

    callback = WNDENUMPROC(_callback)
    user32.EnumWindows(callback, 0)
    return windows


def match_windows(
    hwnd: Any | None = None,
    title: str | None = None,
    process: str | None = None,
) -> list[dict[str, Any]]:
    if hwnd is not None and str(hwnd).strip() != "":
        handle = parse_hwnd(hwnd)
        info = _window_info(handle)
        if info is None:
            raise ValueError(f"Window not found: {hwnd_hex(handle)}")
        return [info]

    title_q = (title or "").strip().lower()
    process_q = (process or "").strip().lower()
    matches: list[dict[str, Any]] = []
    for info in iter_top_windows():
        if title_q and title_q not in info["title"].lower():
            continue
        if process_q and process_q not in info["process"].lower():
            continue
        matches.append(info)
    return matches


def resolve_one_window(arguments: dict[str, Any], *, required: bool = True) -> dict[str, Any]:
    hwnd = arguments.get("hwnd")
    title = arguments.get("title")
    process = arguments.get("process")
    if required and hwnd in (None, "") and not (title or "").strip() and not (process or "").strip():
        raise ValueError("Pass hwnd, title, or process")
    matches = match_windows(hwnd=hwnd, title=title, process=process)
    if not matches:
        raise ValueError("No matching visible window")
    if len(matches) > 1 and hwnd in (None, ""):
        listing = ", ".join(f"{w['hwnd']} '{w['title']}' ({w['process']})" for w in matches[:8])
        extra = "" if len(matches) <= 8 else f" … +{len(matches) - 8} more"
        raise ValueError(f"Ambiguous window match ({len(matches)}): {listing}{extra}. Pass hwnd.")
    return matches[0]


def focus_window(hwnd: int, restore: bool = True) -> bool:
    handle = _as_hwnd(hwnd)
    if restore and user32.IsIconic(handle):
        user32.ShowWindow(handle, SW_RESTORE)
        time.sleep(0.05)
    user32.ShowWindow(handle, SW_SHOW)
    fg = user32.GetForegroundWindow()
    if _hwnd_int(fg) == hwnd:
        return True
    fg_tid = user32.GetWindowThreadProcessId(fg, None)
    target_tid = user32.GetWindowThreadProcessId(handle, None)
    current_tid = kernel32.GetCurrentThreadId()
    user32.AttachThreadInput(current_tid, fg_tid, True)
    user32.AttachThreadInput(current_tid, target_tid, True)
    user32.BringWindowToTop(handle)
    user32.SetForegroundWindow(handle)
    user32.AttachThreadInput(current_tid, fg_tid, False)
    user32.AttachThreadInput(current_tid, target_tid, False)
    if _hwnd_int(user32.GetForegroundWindow()) == hwnd:
        return True
    # Alt-key trick: Windows sometimes blocks SetForegroundWindow without an input event.
    user32.keybd_event(0x12, 0, 0, 0)  # VK_MENU down
    user32.keybd_event(0x12, 0, 2, 0)  # up
    user32.SetForegroundWindow(handle)
    return _hwnd_int(user32.GetForegroundWindow()) == hwnd


# ---------------------------------------------------------------------------
# Capture + PNG
# ---------------------------------------------------------------------------

def _png_chunk(tag: bytes, data: bytes) -> bytes:
    crc = zlib.crc32(tag + data) & 0xFFFFFFFF
    return len(data).to_bytes(4, "big") + tag + data + crc.to_bytes(4, "big")


def bgra_to_png(width: int, height: int, bgra: bytes) -> bytes:
    mv = memoryview(bgra)
    raw = bytearray((1 + width * 3) * height)
    dst = 0
    src = 0
    for _y in range(height):
        raw[dst] = 0
        dst += 1
        for _x in range(width):
            raw[dst] = mv[src + 2]
            raw[dst + 1] = mv[src + 1]
            raw[dst + 2] = mv[src]
            dst += 3
            src += 4
    compressed = zlib.compress(bytes(raw), 6)
    ihdr = width.to_bytes(4, "big") + height.to_bytes(4, "big") + bytes([8, 2, 0, 0, 0])
    return b"\x89PNG\r\n\x1a\n" + _png_chunk(b"IHDR", ihdr) + _png_chunk(b"IDAT", compressed) + _png_chunk(b"IEND", b"")


def capture_rect(x: int, y: int, width: int, height: int, max_width: int = 0) -> tuple[bytes, int, int]:
    if width <= 0 or height <= 0:
        raise ValueError(f"Invalid capture size {width}x{height}")
    out_w, out_h = width, height
    if max_width > 0 and width > max_width:
        out_w = max_width
        out_h = max(1, height * out_w // width)
    screen_dc = user32.GetDC(None)
    if not screen_dc:
        raise RuntimeError("GetDC failed")
    mem_dc = gdi32.CreateCompatibleDC(screen_dc)
    bitmap = gdi32.CreateCompatibleBitmap(screen_dc, out_w, out_h)
    old = gdi32.SelectObject(mem_dc, bitmap)
    try:
        if out_w == width and out_h == height:
            ok = gdi32.BitBlt(mem_dc, 0, 0, out_w, out_h, screen_dc, x, y, SRCCOPY)
        else:
            gdi32.SetStretchBltMode(mem_dc, COLORONCOLOR)
            ok = gdi32.StretchBlt(mem_dc, 0, 0, out_w, out_h, screen_dc, x, y, width, height, SRCCOPY)
        if not ok:
            raise RuntimeError("BitBlt/StretchBlt failed")
        info = BITMAPINFO()
        info.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
        info.bmiHeader.biWidth = out_w
        info.bmiHeader.biHeight = -out_h
        info.bmiHeader.biPlanes = 1
        info.bmiHeader.biBitCount = 32
        info.bmiHeader.biCompression = BI_RGB
        buf = (ctypes.c_ubyte * (out_w * out_h * 4))()
        got = gdi32.GetDIBits(mem_dc, bitmap, 0, out_h, ctypes.byref(buf), ctypes.byref(info), DIB_RGB_COLORS)
        if got == 0:
            raise RuntimeError("GetDIBits failed")
        return bytes(buf), out_w, out_h
    finally:
        gdi32.SelectObject(mem_dc, old)
        gdi32.DeleteObject(bitmap)
        gdi32.DeleteDC(mem_dc)
        user32.ReleaseDC(None, screen_dc)


def encode_shot(bgra: bytes, width: int, height: int) -> str:
    png = bgra_to_png(width, height, bgra)
    return base64.b64encode(png).decode("ascii")


def _max_width_arg(arguments: dict[str, Any]) -> int:
    value = arguments.get("max_width", DEFAULT_MAX_WIDTH)
    if value is None:
        return DEFAULT_MAX_WIDTH
    return int(value)


# ---------------------------------------------------------------------------
# MCP protocol (same framing as FrameXML MCP)
# ---------------------------------------------------------------------------

def _read_message() -> dict[str, Any] | None:
    """Read one JSON-RPC message. Cursor uses newline-delimited JSON (MCP stdio spec)."""
    while True:
        raw = sys.stdin.buffer.readline()
        if not raw:
            return None
        if raw.lower().startswith(b"content-length:"):
            headers = {"content-length": raw.split(b":", 1)[1].decode("ascii", "replace").strip()}
            while True:
                line = sys.stdin.buffer.readline()
                if not line or line in (b"\r\n", b"\n"):
                    break
                if b":" in line:
                    key, value = line.decode("utf-8", "replace").split(":", 1)
                    headers[key.strip().lower()] = value.strip()
            length = int(headers.get("content-length", "0"))
            body = sys.stdin.buffer.read(length) if length > 0 else b""
            return json.loads(body.decode("utf-8")) if body else None
        text = raw.decode("utf-8", errors="replace").strip()
        if not text:
            continue
        return json.loads(text)


def _write_message(payload: dict[str, Any]) -> None:
    data = json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n"
    sys.stdout.buffer.write(data.encode("utf-8"))
    sys.stdout.buffer.flush()


def _text_result(text: str, is_error: bool = False) -> dict[str, Any]:
    return {"content": [{"type": "text", "text": text}], "isError": is_error}


def _image_result(text: str, png_b64: str) -> dict[str, Any]:
    return {
        "content": [
            {"type": "text", "text": text},
            {"type": "image", "data": png_b64, "mimeType": "image/png"},
        ]
    }


TOOLS = [
    {
        "name": "screenshot_screen",
        "description": (
            "Capture the desktop. Default is the primary monitor. "
            "Pass monitor (1-based) for a specific display, or all_monitors=true for the full virtual desktop. "
            "Optional x/y/width/height are virtual-screen coordinates. "
            "Returns a PNG image in the tool result (not a file)."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "monitor": {
                    "type": "integer",
                    "minimum": 1,
                    "description": "1-based monitor index. Omit = primary monitor.",
                },
                "all_monitors": {
                    "type": "boolean",
                    "default": False,
                    "description": "Capture the entire virtual screen (all monitors side by side).",
                },
                "x": {"type": "integer"},
                "y": {"type": "integer"},
                "width": {"type": "integer", "minimum": 1},
                "height": {"type": "integer", "minimum": 1},
                "max_width": {
                    "type": "integer",
                    "minimum": 0,
                    "default": DEFAULT_MAX_WIDTH,
                    "description": "Downscale longest side. 0 = native resolution.",
                },
            },
        },
    },
    {
        "name": "screenshot_window",
        "description": (
            "Capture a top-level window. Identify by hwnd (best), or unique title/process substring. "
            "Uses on-screen pixels (what you see). For GPU/game windows, call focus_window first if it is covered. "
            "Returns a PNG image in the tool result."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "hwnd": {"type": "string", "description": "Window handle, e.g. 0x4A2B10"},
                "title": {"type": "string", "description": "Case-insensitive title substring"},
                "process": {"type": "string", "description": "Exe name substring, e.g. UnrealEditor"},
                "client_only": {
                    "type": "boolean",
                    "default": False,
                    "description": "Capture client area only (no title bar / borders)",
                },
                "bring_to_front": {"type": "boolean", "default": False},
                "max_width": {
                    "type": "integer",
                    "minimum": 0,
                    "default": DEFAULT_MAX_WIDTH,
                    "description": "Downscale longest side. 0 = native resolution.",
                },
            },
        },
    },
    {
        "name": "find_window",
        "description": (
            "List visible top-level windows and their screen rectangles. "
            "Filter by hwnd, title substring, and/or process name. "
            "No filters = all titled visible windows. Also returns monitor layout."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "hwnd": {"type": "string"},
                "title": {"type": "string"},
                "process": {"type": "string"},
                "limit": {"type": "integer", "minimum": 1, "default": 50},
            },
        },
    },
    {
        "name": "focus_window",
        "description": "Bring a window to the foreground (restore if minimized). Identify by hwnd, title, or process.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "hwnd": {"type": "string"},
                "title": {"type": "string"},
                "process": {"type": "string"},
                "restore": {"type": "boolean", "default": True},
            },
        },
    },
]


def _call_tool(name: str, arguments: dict[str, Any]) -> dict[str, Any]:
    args = arguments or {}
    try:
        if name == "find_window":
            hwnd = args.get("hwnd")
            title = args.get("title")
            process = args.get("process")
            limit = int(args.get("limit") or 50)
            if hwnd not in (None, "") or (title or "").strip() or (process or "").strip():
                matches = match_windows(hwnd=hwnd, title=title, process=process)
            else:
                matches = iter_top_windows()
            payload = {
                "count": len(matches),
                "windows": matches[:limit],
                "monitors": list_monitors(),
                "virtual_screen": virtual_screen(),
            }
            return _text_result(json.dumps(payload, ensure_ascii=False, indent=2))

        if name == "focus_window":
            info = resolve_one_window(args)
            hwnd = parse_hwnd(info["hwnd"])
            ok = focus_window(hwnd, restore=bool(args.get("restore", True)))
            time.sleep(0.08)
            updated = _window_info(hwnd) or info
            updated["focused"] = ok
            if not ok:
                return _text_result(
                    json.dumps({"ok": False, "window": updated, "error": "Windows blocked focus change"}, ensure_ascii=False, indent=2),
                    is_error=True,
                )
            return _text_result(json.dumps({"ok": True, "window": updated}, ensure_ascii=False, indent=2))

        if name == "screenshot_screen":
            monitors = list_monitors()
            if not monitors:
                raise RuntimeError("No monitors found")
            if args.get("all_monitors"):
                region = virtual_screen()
            elif args.get("monitor") is not None:
                idx = int(args["monitor"])
                if idx < 1 or idx > len(monitors):
                    raise ValueError(f"monitor must be 1..{len(monitors)}")
                region = monitors[idx - 1]
            else:
                region = next((m for m in monitors if m.get("primary")), monitors[0])
            x = int(args["x"]) if "x" in args else region["x"]
            y = int(args["y"]) if "y" in args else region["y"]
            src_w = int(args["width"]) if "width" in args else region["width"]
            src_h = int(args["height"]) if "height" in args else region["height"]
            bgra, out_w, out_h = capture_rect(x, y, src_w, src_h, _max_width_arg(args))
            b64 = encode_shot(bgra, out_w, out_h)
            meta = f"Screen {src_w}x{src_h} at ({x},{y}) → PNG {out_w}x{out_h}"
            return _image_result(meta, b64)

        if name == "screenshot_window":
            info = resolve_one_window(args)
            hwnd = parse_hwnd(info["hwnd"])
            if args.get("bring_to_front"):
                focus_window(hwnd)
                time.sleep(0.12)
                info = _window_info(hwnd) or info
            if info.get("minimized"):
                raise ValueError("Window is minimized — call focus_window first or pass bring_to_front=true")
            rect = info["client_rect"] if args.get("client_only") and info.get("client_rect") else info["rect"]
            src_w, src_h = rect["width"], rect["height"]
            bgra, out_w, out_h = capture_rect(rect["x"], rect["y"], src_w, src_h, _max_width_arg(args))
            b64 = encode_shot(bgra, out_w, out_h)
            meta = (
                f"Window {info['hwnd']} '{info['title']}' ({info['process']}) "
                f"{src_w}x{src_h} at ({rect['x']},{rect['y']}) → PNG {out_w}x{out_h}"
            )
            return _image_result(meta, b64)

        return _text_result(f"Unknown tool: {name}", is_error=True)
    except Exception as exc:
        return _text_result(str(exc), is_error=True)


def _handle(message: dict[str, Any]) -> dict[str, Any] | None:
    method = message.get("method")
    msg_id = message.get("id")
    if method is None:
        return None
    if str(method).startswith("notifications/"):
        return None
    if method == "initialize":
        requested = ((message.get("params") or {}).get("protocolVersion") or PROTOCOL_VERSION)
        return {
            "jsonrpc": "2.0",
            "id": msg_id,
            "result": {
                "protocolVersion": requested,
                "capabilities": {"tools": {"listChanged": False}},
                "serverInfo": {"name": SERVER_NAME, "version": SERVER_VERSION},
            },
        }
    if method == "tools/list":
        return {"jsonrpc": "2.0", "id": msg_id, "result": {"tools": TOOLS}}
    if method == "tools/call":
        params = message.get("params") or {}
        result = _call_tool(params.get("name", ""), params.get("arguments") or {})
        return {"jsonrpc": "2.0", "id": msg_id, "result": result}
    if method in ("ping", "notifications/cancelled"):
        if msg_id is None:
            return None
        return {"jsonrpc": "2.0", "id": msg_id, "result": {}}
    return {
        "jsonrpc": "2.0",
        "id": msg_id,
        "error": {"code": -32601, "message": f"Method not found: {method}"},
    }


def _cli_list() -> None:
    payload = {
        "windows": iter_top_windows(),
        "monitors": list_monitors(),
        "virtual_screen": virtual_screen(),
    }
    print(json.dumps(payload, ensure_ascii=False, indent=2))


def _cli_screenshot(path: str) -> None:
    monitors = list_monitors()
    region = next((m for m in monitors if m.get("primary")), virtual_screen())
    bgra, w, h = capture_rect(region["x"], region["y"], region["width"], region["height"], DEFAULT_MAX_WIDTH)
    with open(path, "wb") as handle:
        handle.write(bgra_to_png(w, h, bgra))
    print(f"Wrote {path} ({w}x{h})")


def main() -> None:
    if sys.platform != "win32":
        print("This MCP is Windows-only.", file=sys.stderr)
        sys.exit(1)
    argv = sys.argv[1:]
    if argv:
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass
    if argv == ["--list"]:
        _cli_list()
        return
    if len(argv) == 2 and argv[0] == "--screenshot":
        _cli_screenshot(argv[1])
        return
    if argv:
        print("Usage: server.py | server.py --list | server.py --screenshot out.png", file=sys.stderr)
        sys.exit(2)
    print("screenshot MCP ready (stdio)", file=sys.stderr)
    while True:
        message = _read_message()
        if message is None:
            break
        reply = _handle(message)
        if reply is not None:
            _write_message(reply)


if __name__ == "__main__":
    main()
