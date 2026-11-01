# -*- coding: utf-8 -*-
"""用真实 GUI 截图核对英文界面（需要 Windows 桌面会话）。

做法：把语言设置写成 en，启动程序，等界面稳定后用 PrintWindow 抓图。
"""
import ctypes
import os
import subprocess
import sys
import time

from PIL import Image

EXE = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   "bin", "Debug", "net48", "SharpGIS.exe")
OUT = os.path.join(os.environ["TEMP"], "sgcam_gui_en")
if not os.path.isdir(OUT):
    os.makedirs(OUT)

LANG_FILE = os.path.join(os.environ["APPDATA"], "SharpGIS", "language.txt")


def write_lang(code):
    d = os.path.dirname(LANG_FILE)
    if not os.path.isdir(d):
        os.makedirs(d)
    with open(LANG_FILE, "w", encoding="utf-8") as f:
        f.write(code)


def grab(hwnd, path):
    """用 PrintWindow 抓指定窗口。"""
    user32 = ctypes.windll.user32
    gdi32 = ctypes.windll.gdi32
    rect = ctypes.wintypes.RECT() if hasattr(ctypes, "wintypes") else None
    # 兼容写法
    class RECT(ctypes.Structure):
        _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                    ("right", ctypes.c_long), ("bottom", ctypes.c_long)]
    r = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = r.right - r.left, r.bottom - r.top
    if w <= 0 or h <= 0:
        return False
    hdc = user32.GetWindowDC(hwnd)
    mdc = gdi32.CreateCompatibleDC(hdc)
    bmp = gdi32.CreateCompatibleBitmap(hdc, w, h)
    gdi32.SelectObject(mdc, bmp)
    user32.PrintWindow(hwnd, mdc, 2)

    class BITMAPINFOHEADER(ctypes.Structure):
        _fields_ = [("biSize", ctypes.c_uint32), ("biWidth", ctypes.c_int32),
                    ("biHeight", ctypes.c_int32), ("biPlanes", ctypes.c_uint16),
                    ("biBitCount", ctypes.c_uint16), ("biCompression", ctypes.c_uint32),
                    ("biSizeImage", ctypes.c_uint32), ("biXPelsPerMeter", ctypes.c_int32),
                    ("biYPelsPerMeter", ctypes.c_int32), ("biClrUsed", ctypes.c_uint32),
                    ("biClrImportant", ctypes.c_uint32)]

    bi = BITMAPINFOHEADER()
    bi.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bi.biWidth = w
    bi.biHeight = -h
    bi.biPlanes = 1
    bi.biBitCount = 32
    bi.biCompression = 0
    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mdc, bmp, 0, h, buf, ctypes.byref(bi), 0)
    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mdc)
    user32.ReleaseDC(hwnd, hdc)
    img = Image.frombuffer("RGBA", (w, h), buf, "raw", "BGRA", 0, 1)
    img.convert("RGB").save(path)
    return True


def main():
    write_lang("en")
    proc = subprocess.Popen([EXE])
    time.sleep(9)

    user32 = ctypes.windll.user32
    hwnds = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
    def enum_proc(hwnd, lparam):
        if user32.IsWindowVisible(hwnd):
            length = user32.GetWindowTextLengthW(hwnd)
            if length > 0:
                buf = ctypes.create_unicode_buffer(length + 1)
                user32.GetWindowTextW(hwnd, buf, length + 1)
                hwnds.append((hwnd, buf.value))
        return True

    user32.EnumWindows(enum_proc, 0)

    shot = 0
    for hwnd, title in hwnds:
        if "SharpGIS" in title:
            p = os.path.join(OUT, "main_en.png")
            if grab(hwnd, p):
                shot += 1
                print("已抓取:", title, "→", p)

    print("抓图数量:", shot)
    proc.terminate()
    try:
        proc.wait(timeout=10)
    except Exception:
        proc.kill()
    write_lang("zh")
    print("已恢复中文设置")


if __name__ == "__main__":
    main()
