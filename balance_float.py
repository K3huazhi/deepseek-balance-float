# -*- coding: utf-8 -*-
"""
DeepSeek 余额桌面悬浮窗 · WinUI 3 / Fluent 风格
------------------------------------------------
· Qt Quick 无边框透明窗口 + Windows App SDK 同款系统云母（Mica）背板
· 亮色 / 暗色 / 跟随系统，一键切换（带开关灯动画）
· 余额变化时：数字滚动 + 抖动 + 红色 -¥0.01 飘字
· 展开详情：充值 / 赠送余额、账户可用状态、今日消费、近 7 日柱状图、预计可用天数
· API Key 存在 Windows 凭据管理器里，不落盘；右键菜单可随时修改
· 拖动移动，位置自动记忆；右键菜单：刷新 / 主题 / 置顶 / 开机自启 / 退出

依赖：PySide6-Essentials（启动.bat 会自动检查）
"""
import configparser
import ctypes
import hashlib
import json
import os
import sys
import threading
import time
import urllib.request
import winreg
from ctypes import wintypes
from datetime import date, timedelta

from PySide6.QtCore import QObject, Property, Signal, Slot, Qt, QTimer, QByteArray, QUrl
from PySide6.QtGui import QGuiApplication, QCursor
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuick import QQuickWindow
from PySide6.QtWidgets import QApplication, QMenu

HERE = (os.path.dirname(os.path.abspath(sys.executable))
        if getattr(sys, "frozen", False)
        else os.path.dirname(os.path.abspath(__file__)))
LEGACY_CONF = os.path.join(HERE, "config.ini")
LEGACY_STATS = os.path.join(HERE, "balance_stats.json")
APP_NAME = "DeepSeek 余额"
DEFAULT_URL = "https://api.deepseek.com/user/balance"

WIN_W = 276
WIN_H = 120          # 收起高度
WIN_H_OPEN = 328     # 展开高度
MARGIN = 28

# ---------------------------------------------------------------- Win32 / DWM
DWMWA_USE_IMMERSIVE_DARK_MODE = 20
DWMWA_WINDOW_CORNER_PREFERENCE = 33
DWMWA_BORDER_COLOR = 34
DWMWA_SYSTEMBACKDROP_TYPE = 38

HWND_TOPMOST = -1
HWND_NOTOPMOST = -2
SWP_NOMOVE, SWP_NOSIZE, SWP_NOACTIVATE = 0x0002, 0x0001, 0x0010


def os_build():
    try:
        return sys.getwindowsversion().build
    except Exception:
        return 0


def mica_supported():
    return os_build() >= 22000


class Win11:
    """系统材质：云母 / 圆角 / 暗色标题栏 / 置顶"""

    def __init__(self):
        self.dwm = ctypes.WinDLL("dwmapi", use_last_error=True)
        self.user32 = ctypes.WinDLL("user32", use_last_error=True)
        # SetWindowPos 的 hWndInsertAfter 是 HANDLE：不声明 argtypes 时
        # Python 的 -1 / -2 会被当成 32 位整数，导致调用静默失败（置顶失效）
        self.user32.SetWindowPos.argtypes = [
            wintypes.HWND, wintypes.HWND,
            ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
            wintypes.UINT,
        ]
        self.user32.SetWindowPos.restype = wintypes.BOOL
        self.dwm.DwmSetWindowAttribute.argtypes = [
            wintypes.HWND, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD,
        ]
        self.dwm.DwmSetWindowAttribute.restype = ctypes.c_long

    def _set(self, hwnd, attr, value):
        v = ctypes.c_int(value)
        return self.dwm.DwmSetWindowAttribute(
            wintypes.HWND(int(hwnd)), attr, ctypes.byref(v), ctypes.sizeof(v))

    def apply_backdrop(self, hwnd, dark):
        if not hwnd:
            return
        hwnd = int(hwnd)
        try:
            self._set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1 if dark else 0)
            self._set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, 2)   # DWMWCP_ROUND
            self._set(hwnd, DWMWA_BORDER_COLOR, 0xFFFFFFFE)      # 隐藏系统描边
            if os_build() >= 22621:
                self._set(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, 2)    # DWMSBT_MAINWINDOW (Mica)
            elif os_build() >= 22000:
                self._set(hwnd, 1029, 1)                          # 旧版 Mica 开关
        except Exception as e:
            print("dwm:", e)

    def set_topmost(self, hwnd, on):
        try:
            self.user32.SetWindowPos(
                wintypes.HWND(int(hwnd)),
                wintypes.HWND(HWND_TOPMOST if on else HWND_NOTOPMOST),
                0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)
        except Exception as e:
            print("topmost:", e)


# ------------------------------------------------------------------ 设置存储
REG_PATH = r"Software\DeepSeekBalance"
CRED_TARGET = "DeepSeekBalance:api_key"
CRED_TYPE_GENERIC = 1
CRED_PERSIST_LOCAL_MACHINE = 2


class Store:
    """界面 / 接口设置存注册表，不生成任何配置文件"""

    def get(self, name, default=""):
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, REG_PATH) as k:
                v, _ = winreg.QueryValueEx(k, name)
                return v
        except Exception:
            return default

    def get_bool(self, name, default=False):
        v = self.get(name, None)
        return default if v is None else str(v) == "1"

    def set(self, name, value):
        try:
            with winreg.CreateKey(winreg.HKEY_CURRENT_USER, REG_PATH) as k:
                winreg.SetValueEx(k, name, 0, winreg.REG_SZ, str(value))
        except Exception as e:
            print("reg:", e)


class CREDENTIALW(ctypes.Structure):
    _fields_ = [
        ("Flags", wintypes.DWORD),
        ("Type", wintypes.DWORD),
        ("TargetName", wintypes.LPWSTR),
        ("Comment", wintypes.LPWSTR),
        ("LastWritten", wintypes.FILETIME),
        ("CredentialBlobSize", wintypes.DWORD),
        ("CredentialBlob", ctypes.POINTER(ctypes.c_byte)),
        ("Persist", wintypes.DWORD),
        ("AttributeCount", wintypes.DWORD),
        ("Attributes", ctypes.c_void_p),
        ("TargetAlias", wintypes.LPWSTR),
        ("UserName", wintypes.LPWSTR),
    ]


def cred_read():
    """从 Windows 凭据管理器读取 API Key"""
    try:
        advapi = ctypes.WinDLL("advapi32", use_last_error=True)
        pcred = ctypes.POINTER(CREDENTIALW)()
        if not advapi.CredReadW(CRED_TARGET, CRED_TYPE_GENERIC, 0, ctypes.byref(pcred)):
            return ""
        try:
            c = pcred.contents
            if not c.CredentialBlobSize:
                return ""
            raw = ctypes.string_at(c.CredentialBlob, c.CredentialBlobSize)
            return raw.decode("utf-16-le", "ignore").strip()
        finally:
            advapi.CredFree(pcred)
    except Exception as e:
        print("cred read:", e)
        return ""


def cred_write(key):
    """写入 Windows 凭据管理器（DPAPI 加密，不落盘）"""
    try:
        advapi = ctypes.WinDLL("advapi32", use_last_error=True)
        blob = key.encode("utf-16-le")
        buf = ctypes.create_string_buffer(blob, len(blob))
        cred = CREDENTIALW()
        cred.Flags = 0
        cred.Type = CRED_TYPE_GENERIC
        cred.TargetName = CRED_TARGET
        cred.Comment = "DeepSeek 余额悬浮窗"
        cred.CredentialBlobSize = len(blob)
        cred.CredentialBlob = ctypes.cast(buf, ctypes.POINTER(ctypes.c_byte))
        cred.Persist = CRED_PERSIST_LOCAL_MACHINE
        cred.AttributeCount = 0
        cred.Attributes = None
        cred.TargetAlias = None
        cred.UserName = "DeepSeek"
        return bool(advapi.CredWriteW(ctypes.byref(cred), 0))
    except Exception as e:
        print("cred write:", e)
        return False


def mask_key(key):
    """保留首尾，隐藏中间 6 位"""
    key = (key or "").strip()
    if len(key) <= 12:
        return key[:4] + "******"
    cut = (len(key) - 6) // 2
    return key[:cut] + "******" + key[cut + 6:]


def default_name(key):
    """没命名时用 sk- 后面的六位作为名称"""
    key = (key or "").strip()
    body = key[3:] if key.startswith("sk-") else key
    return body[:6] or "未命名"


def key_uid(key):
    return hashlib.sha1((key or "").encode("utf-8")).hexdigest()[:12]


def keys_load():
    """Key 列表保存在凭据管理器的 JSON 里，最多 10 个"""
    raw = cred_read()
    if not raw:
        return []
    raw = raw.strip()
    if raw.startswith("["):
        try:
            data = json.loads(raw)
            out = []
            for it in data:
                if isinstance(it, dict) and it.get("k"):
                    k = str(it["k"]).strip()
                    name = str(it.get("name") or "").strip() or default_name(k)
                    out.append({"k": k, "show": bool(it.get("show", True)), "name": name})
            return out[:10]
        except Exception:
            pass
    return [{"k": raw, "show": True, "name": default_name(raw)}]


def keys_save(keys):
    return cred_write(json.dumps(
        [{"k": x["k"], "show": bool(x["show"]),
          "name": x.get("name") or default_name(x["k"])} for x in keys],
        ensure_ascii=False, separators=(",", ":")))


def migrate_legacy_config():
    """把旧版 config.ini 里的 Key 搬进凭据管理器、设置搬进注册表，再删掉 ini"""
    if not os.path.exists(LEGACY_CONF):
        return
    c = configparser.ConfigParser()
    try:
        c.read(LEGACY_CONF, encoding="utf-8")
    except Exception:
        return

    store = Store()
    key = ""
    if c.has_option("api", "key"):
        key = (c.get("api", "key") or "").strip()
    if key and not key.startswith("sk-把你的") and not cred_read():
        if not cred_write(key):
            print("迁移 key 失败，保留 config.ini")
            return

    for name, sec, opt, default in (
        ("theme", "ui", "theme", "auto"),
        ("x", "ui", "x", ""),
        ("y", "ui", "y", ""),
        ("pinned", "ui", "pinned", "1"),
        ("expanded", "ui", "expanded", "0"),
        ("interval", "api", "interval", "60"),
        ("url", "api", "url", DEFAULT_URL),
    ):
        if store.get(name, None) is None and c.has_option(sec, opt):
            store.set(name, (c.get(sec, opt) or "").strip())

    try:
        os.remove(LEGACY_CONF)
        print("已迁移 config.ini → 注册表 + 凭据管理器")
    except Exception as e:
        print("删除 config.ini:", e)


def dark_by_system():
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                            r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize") as k:
            return winreg.QueryValueEx(k, "AppsUseLightTheme")[0] == 0
    except Exception:
        return False


# ---------------------------------------------------------------------- QML
QML = r"""
import QtQuick
import QtQuick.Window
import QtQuick.Shapes

Window {
    id: win
    width: 276
    height: backend.multiView ? backend.multiHeight : (backend.expanded ? 328 : 120)
    visible: false
    color: "transparent"
    flags: Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.Tool
    title: "DeepSeek 余额"

    readonly property string whalePath: "M26.5174 3.39471C26.235 3.2567 26.1137 3.52006 25.9487 3.65346C25.8923 3.69659 25.8446 3.75294 25.7969 3.80469C25.3846 4.24516 24.9027 4.53439 24.2737 4.49989C23.3536 4.44814 22.5682 4.73737 21.8735 5.44119C21.7258 4.57349 21.2353 4.0554 20.4889 3.72304C20.0985 3.55054 19.7034 3.37746 19.4297 3.00197C19.2388 2.73459 19.1865 2.43673 19.091 2.14289C19.0301 1.96579 18.9697 1.78466 18.7656 1.75418C18.5442 1.71968 18.4574 1.90541 18.3705 2.06067C18.0232 2.69549 17.8887 3.39471 17.9019 4.10313C17.9324 5.6965 18.6051 6.96556 19.9421 7.86834C20.0939 7.97184 20.133 8.07535 20.0852 8.22658C19.9938 8.53766 19.8857 8.83955 19.7903 9.15063C19.7293 9.34901 19.6384 9.39271 19.4257 9.30588C18.692 8.9994 18.0583 8.54571 17.4982 7.99772C16.5477 7.07827 15.6881 6.06336 14.6162 5.26869C14.3644 5.08296 14.1125 4.91045 13.8521 4.746C12.7584 3.68394 13.9952 2.81164 14.2816 2.70814C14.5812 2.60003 14.3857 2.22857 13.4179 2.23317C12.4502 2.2372 11.5646 2.56151 10.4359 2.99335C10.2708 3.05832 10.0972 3.10547 9.91951 3.14457C8.8954 2.95022 7.83162 2.90709 6.72069 3.03245C4.62877 3.26533 2.95777 4.25436 1.72954 5.94261C0.254043 7.97184 -0.0932678 10.2777 0.33167 12.6824C0.778458 15.2171 2.07225 17.3153 4.06008 18.9558C6.12152 20.6567 8.49577 21.4905 11.2047 21.3306C12.8498 21.2358 14.6812 21.0155 16.7473 19.2669C17.2682 19.5262 17.8151 19.6297 18.7219 19.7074C19.4205 19.7723 20.0933 19.6729 20.6143 19.5648C21.4302 19.3923 21.3739 18.6367 21.0789 18.4981C18.6874 17.3843 19.2124 17.8374 18.7351 17.4706C19.9501 16.033 21.8063 13.4776 22.379 9.99821C22.4353 9.61409 22.5072 9.073 22.4986 8.76192C22.494 8.57216 22.5377 8.49856 22.7545 8.47671C23.3536 8.40771 23.935 8.24383 24.4692 7.94999C26.0188 7.10357 26.6439 5.71318 26.7911 4.04678C26.8129 3.79204 26.7865 3.52869 26.5174 3.39471ZM13.0143 18.3946C10.6964 16.5724 9.5722 15.9726 9.10816 15.9985C8.67402 16.0244 8.75222 16.5212 8.84768 16.8449C8.94773 17.1646 9.07768 17.3849 9.25996 17.6655C9.38589 17.8512 9.47272 18.1272 9.13404 18.3348C8.38766 18.7965 7.08985 18.1796 7.0289 18.1491C5.51833 17.2595 4.25559 16.0853 3.36546 14.4793C2.50581 12.9337 2.0067 11.2753 1.92447 9.50542C1.90262 9.07818 2.02855 8.92695 2.45406 8.84932C3.01413 8.74582 3.59144 8.72397 4.15093 8.80619C6.51656 9.15178 8.53027 10.2092 10.2185 11.8848C11.1822 12.8388 11.9114 13.979 12.6623 15.0929C13.461 16.2757 14.3201 17.4027 15.4144 18.3268C15.8008 18.6505 16.109 18.8966 16.404 19.0783C15.5144 19.1778 14.0297 19.1991 13.0143 18.3958V18.3946ZM14.1252 11.2489C14.1252 11.0591 14.277 10.9079 14.4679 10.9079C14.511 10.9079 14.5501 10.9165 14.5852 10.9292C14.6329 10.9464 14.6766 10.9723 14.7111 11.0114C14.7721 11.0718 14.8066 11.158 14.8066 11.2489C14.8066 11.4386 14.6548 11.5899 14.4639 11.5899C14.273 11.5899 14.1252 11.4386 14.1252 11.2489ZM17.5759 13.0188C17.3545 13.1096 17.1331 13.1873 16.9203 13.1959C16.5903 13.2131 16.2303 13.0791 16.0348 12.9153C15.7312 12.6605 15.5139 12.5179 15.423 12.0734C15.3839 11.8837 15.4057 11.5899 15.4402 11.4214C15.5185 11.0585 15.4316 10.8257 15.1757 10.614C14.9676 10.4415 14.7025 10.3938 14.4115 10.3938C14.3029 10.3938 14.2034 10.3461 14.1292 10.3076C14.0079 10.2472 13.9078 10.096 14.0033 9.91023C14.0338 9.84985 14.1815 9.70322 14.216 9.67734C14.6111 9.45251 15.0665 9.52612 15.488 9.6946C15.8784 9.85445 16.174 10.1477 16.5989 10.5623C17.033 11.0631 17.1112 11.2011 17.3585 11.5772C17.554 11.871 17.7317 12.1729 17.8536 12.5185C17.9272 12.7341 17.8317 12.9107 17.5759 13.0188Z"

    Behavior on height {
        NumberAnimation { duration: 200; easing.type: Easing.OutCubic }
    }

    property bool dark: backend.dark
    property bool mica: backend.micaSupported
    property color cAccent: backend.accentColor

    // ---------------- 换肤过渡：从按钮扩散的波浪 ----------------
    property bool transActive: false
    property real tween: dark ? 1 : 0
    property real waveRadius: 0
    property color transOld: "#FFF4F4F4"

    Behavior on tween {
        NumberAnimation { duration: 470; easing.type: Easing.InOutSine }
    }

    readonly property color lText: "#1B1B1F"
    readonly property color dText: "#FFFFFF"
    readonly property color lSub: "#5D5D5D"
    readonly property color dSub: "#BFBFBF"
    readonly property color lBorder: "#14000000"
    readonly property color dBorder: "#1CFFFFFF"
    readonly property color lHover: "#12000000"
    readonly property color dHover: "#24FFFFFF"

    function mixColor(a, b, k) {
        return Qt.rgba(a.r + (b.r - a.r) * k, a.g + (b.g - a.g) * k,
                       a.b + (b.b - a.b) * k, a.a + (b.a - a.a) * k)
    }

    readonly property color solidLight: "#FFF4F4F4"
    readonly property color solidDark: "#FF1F1F1F"
    readonly property color ringColor: dark ? "#FFFFFF" : "#1B1B1F"

    function solidFor(d) {
        return d ? solidDark : solidLight
    }

    function moneyColor(v) {
        if (v > 5)
            return win.dark ? "#6CCB5F" : "#0F7B3F"
        if (v > 0.5)
            return win.dark ? "#FCE100" : "#9D5D00"
        return win.dark ? "#FF99A4" : "#C42B1C"
    }

    property color cText: mixColor(lText, dText, tween)
    property color cSub: mixColor(lSub, dSub, tween)
    property color cBorder: dark ? dBorder : lBorder
    property color cHover: mixColor(lHover, dHover, tween)
    property color cCard: transActive ? solidFor(dark)
                                      : (mica ? (dark ? "#33000000" : "#99FFFFFF")
                                              : (dark ? "#F2202020" : "#F2F3F3F3"))

    // 卡片本体：与波纹遮罩共用同一条圆角路径，边缘像素对齐
    Shape {
        id: cardShape
        anchors.fill: parent

        ShapePath {
            fillColor: win.cCard
            strokeWidth: 0

            startX: 10
            startY: 0
            PathLine { x: win.width - 10; y: 0 }
            PathArc { x: win.width; y: 10; radiusX: 10; radiusY: 10; direction: PathArc.Clockwise }
            PathLine { x: win.width; y: win.height - 10 }
            PathArc { x: win.width - 10; y: win.height; radiusX: 10; radiusY: 10; direction: PathArc.Clockwise }
            PathLine { x: 10; y: win.height }
            PathArc { x: 0; y: win.height - 10; radiusX: 10; radiusY: 10; direction: PathArc.Clockwise }
            PathLine { x: 0; y: 10 }
            PathArc { x: 10; y: 0; radiusX: 10; radiusY: 10; direction: PathArc.Clockwise }
        }

        ShapePath {
            fillColor: "transparent"
            strokeWidth: 1
            strokeColor: win.cBorder

            startX: 9.5
            startY: 0.5
            PathLine { x: win.width - 9.5; y: 0.5 }
            PathArc { x: win.width - 0.5; y: 9.5; radiusX: 9.5; radiusY: 9.5; direction: PathArc.Clockwise }
            PathLine { x: win.width - 0.5; y: win.height - 9.5 }
            PathArc { x: win.width - 9.5; y: win.height - 0.5; radiusX: 9.5; radiusY: 9.5; direction: PathArc.Clockwise }
            PathLine { x: 9.5; y: win.height - 0.5 }
            PathArc { x: 0.5; y: win.height - 9.5; radiusX: 9.5; radiusY: 9.5; direction: PathArc.Clockwise }
            PathLine { x: 0.5; y: 9.5 }
            PathArc { x: 9.5; y: 0.5; radiusX: 9.5; radiusY: 9.5; direction: PathArc.Clockwise }
        }
    }

    // 换肤波浪：旧配色作为遮罩，圆形孔洞从按钮位置扩散
    Shape {
        id: waveShape
        anchors.fill: parent
        visible: win.transActive

        ShapePath {
            fillRule: ShapePath.OddEvenFill
            fillColor: win.transOld
            strokeWidth: 0

            startX: 10
            startY: 0
            PathLine { x: win.width - 10; y: 0 }
            PathArc { x: win.width; y: 10; radiusX: 10; radiusY: 10; direction: PathArc.Clockwise }
            PathLine { x: win.width; y: win.height - 10 }
            PathArc { x: win.width - 10; y: win.height; radiusX: 10; radiusY: 10; direction: PathArc.Clockwise }
            PathLine { x: 10; y: win.height }
            PathArc { x: 0; y: win.height - 10; radiusX: 10; radiusY: 10; direction: PathArc.Clockwise }
            PathLine { x: 0; y: 10 }
            PathArc { x: 10; y: 0; radiusX: 10; radiusY: 10; direction: PathArc.Clockwise }

            PathAngleArc {
                centerX: themeBtn.x + themeBtn.width / 2
                centerY: themeBtn.y + themeBtn.height / 2
                radiusX: Math.max(0.5, win.waveRadius)
                radiusY: Math.max(0.5, win.waveRadius)
                startAngle: 0
                sweepAngle: 360
                moveToStart: true
            }
        }
    }

    // 右侧淡淡的鲸鱼水印
    Item {
        x: 198; y: 54
        width: 64; height: 47
        opacity: 0.05

        Shape {
            width: 26.634
            height: 19.6
            scale: 64 / 26.634
            transformOrigin: Item.TopLeft
            x: -0.163 * (64 / 26.634)
            y: -1.75 * (64 / 26.634)

            ShapePath {
                strokeWidth: 0
                fillColor: win.cText
                PathSvg { path: win.whalePath }
            }
        }
    }

    // DeepSeek 小鲸鱼
    Item {
        x: 18; y: 13
        width: 20; height: 15

        Shape {
            width: 26.634
            height: 19.6
            scale: 20 / 26.634
            transformOrigin: Item.TopLeft
            x: -0.163 * (20 / 26.634)
            y: -1.75 * (20 / 26.634)
            opacity: 0.88

            ShapePath {
                strokeWidth: 0
                fillColor: win.cSub
                PathSvg { path: win.whalePath }
            }
        }
    }

    Text {
        x: 46; y: 13
        text: "DeepSeek 余额"
        color: win.cSub
        font.family: "Microsoft YaHei UI"
        font.pixelSize: 12
    }

    Text {
        anchors.right: foldBtn.left
        anchors.rightMargin: 6
        y: 14
        text: backend.clock
        color: win.cSub
        opacity: 0.85
        font.family: "Segoe UI Variable Small"
        font.pixelSize: 11
    }

    // 展开 / 收起详情
    Rectangle {
        id: foldBtn
        visible: !backend.multiView
        x: win.width - 18 - 26 - 4 - 26
        y: 7
        width: 26
        height: 26
        radius: 5
        z: 2
        color: foldArea.containsMouse ? win.cHover : "transparent"
        Behavior on color { ColorAnimation { duration: 130; easing.type: Easing.OutQuad } }

        Text {
            anchors.centerIn: parent
            text: backend.expanded ? "\uE70E" : "\uE70D"
            color: win.cSub
            font.family: "Segoe Fluent Icons"
            font.pixelSize: 11
        }

        MouseArea {
            id: foldArea
            anchors.fill: parent
            hoverEnabled: true
            cursorShape: Qt.PointingHandCursor
            onClicked: backend.toggleExpanded()
        }
    }

    Rectangle {
        id: themeBtn
        x: win.width - 18 - 26
        y: 7
        width: 26
        height: 26
        radius: 5
        z: 2
        color: themeArea.containsMouse ? win.cHover : "transparent"
        Behavior on color { ColorAnimation { duration: 130; easing.type: Easing.OutQuad } }

        // 开关灯时扩散的光环
        Rectangle {
            id: themeRipple
            anchors.centerIn: parent
            width: 22; height: 22; radius: 11
            color: "transparent"
            border.width: 1
            border.color: win.dark ? "#FFFFFF" : "#000000"
            opacity: 0
        }

        Text {
            id: themeIcon
            anchors.centerIn: parent
            text: win.dark ? "\uE706" : "\uE708"
            color: win.cSub
            font.family: "Segoe Fluent Icons"
            font.pixelSize: 13
        }

        MouseArea {
            id: themeArea
            anchors.fill: parent
            hoverEnabled: true
            cursorShape: Qt.PointingHandCursor
            onClicked: backend.toggleTheme()
        }

        SequentialAnimation {
            id: themePress
            NumberAnimation { target: themeBtn; property: "scale"; to: 0.86; duration: 70 }
            NumberAnimation { target: themeBtn; property: "scale"; to: 1.0; duration: 130; easing.type: Easing.OutBack }
        }
    }

    Text {
        id: bal
        visible: !backend.multiView
        x: 18
        y: 34
        text: backend.error ? "\u2014\u2014" : backend.symbol + win.shown.toFixed(2)
        color: backend.accentColor
        font.family: "Segoe UI Variable Display"
        font.pixelSize: 30
        font.weight: 600
        renderType: Text.NativeRendering
    }

    property real shown: 0
    Behavior on shown {
        NumberAnimation { duration: 420; easing.type: Easing.OutCubic }
    }

    Text {
        id: fly
        x: win.width - 20
        y: 56
        opacity: 0
        text: ""
        color: win.dark ? "#FF99A4" : "#C42B1C"
        font.family: "Segoe UI Variable Display"
        font.pixelSize: 13
        font.weight: 600
    }

    Text {
        visible: !backend.multiView
        x: 18
        y: 90
        width: win.width - 36
        text: backend.sub
        color: win.cSub
        elide: Text.ElideRight
        font.family: "Microsoft YaHei UI"
        font.pixelSize: 11
    }

    // ---------- 多 Key 列表（2~3 个时）----------
    Item {
        id: multiList
        visible: backend.multiView
        x: 18
        y: 48
        width: win.width - 36
        height: win.height - 56

        Repeater {
            model: backend.rows
            delegate: Item {
                width: multiList.width
                height: 42
                y: index * 46
                opacity: modelData.ok ? 1.0 : 0.55

                Text {
                    text: modelData.label
                    color: win.cSub
                    width: parent.width - 92
                    elide: Text.ElideRight
                    font.family: "Segoe UI Variable Small"
                    font.pixelSize: 11
                }

                Text {
                    anchors.right: parent.right
                    y: -5
                    text: modelData.value
                    color: win.moneyColor(modelData.total)
                    font.family: "Segoe UI Variable Display"
                    font.pixelSize: 18
                    font.weight: 600
                }

                Text {
                    y: 18
                    text: "今日消耗  " + modelData.spend
                    color: win.cSub
                    font.family: "Microsoft YaHei UI"
                    font.pixelSize: 11
                }
            }
        }
    }

    // ---------- 展开的详情面板 ----------
    Item {
        id: details
        x: 0
        y: 120
        width: win.width
        height: 208
        z: 1
        opacity: (backend.expanded && !backend.multiView) ? 1 : 0
        visible: opacity > 0.01
        Behavior on opacity { NumberAnimation { duration: 200; easing.type: Easing.OutCubic } }

        Rectangle {
            x: 18; y: 0
            width: win.width - 36
            height: 1
            color: win.cBorder
        }

        Text { x: 18; y: 12; text: "充值余额"; color: win.cSub; font.family: "Microsoft YaHei UI"; font.pixelSize: 11 }
        Text {
            anchors.right: parent.right; anchors.rightMargin: 18
            y: 10
            text: backend.symbol + backend.topped.toFixed(2)
            color: win.cText
            font.family: "Segoe UI Variable Text"
            font.pixelSize: 12
        }

        Text { x: 18; y: 34; text: "赠送余额"; color: win.cSub; font.family: "Microsoft YaHei UI"; font.pixelSize: 11 }
        Text {
            anchors.right: parent.right; anchors.rightMargin: 18
            y: 32
            text: backend.symbol + backend.granted.toFixed(2)
            color: win.cText
            font.family: "Segoe UI Variable Text"
            font.pixelSize: 12
        }

        Text { x: 18; y: 56; text: "今日消费"; color: win.cSub; font.family: "Microsoft YaHei UI"; font.pixelSize: 11 }
        Text {
            anchors.right: parent.right; anchors.rightMargin: 18
            y: 54
            text: (backend.todaySpend > 0.0001 ? "-" : "") + backend.symbol + backend.todaySpend.toFixed(2)
            color: backend.todaySpend > 0.0001 ? (win.dark ? "#FF99A4" : "#C42B1C") : win.cSub
            font.family: "Segoe UI Variable Text"
            font.pixelSize: 12
        }

        Text { x: 18; y: 78; text: "账户状态"; color: win.cSub; font.family: "Microsoft YaHei UI"; font.pixelSize: 11 }
        Text {
            anchors.right: parent.right; anchors.rightMargin: 18
            y: 76
            text: backend.available ? "正常" : "余额不足"
            color: backend.available ? (win.dark ? "#6CCB5F" : "#0F7B3F") : (win.dark ? "#FF99A4" : "#C42B1C")
            font.family: "Microsoft YaHei UI"
            font.pixelSize: 12
        }

        Text {
            x: 18; y: 102
            text: "近 7 日消耗"
            color: win.cSub
            opacity: 0.8
            font.family: "Microsoft YaHei UI"
            font.pixelSize: 10
        }

        Rectangle {
            x: 18; y: 150
            width: 214
            height: 1
            color: win.cBorder
        }

        Row {
            x: 18; y: 118
            spacing: 10
            Repeater {
                model: backend.weekSeries
                delegate: Item {
                    width: 22
                    height: 32
                    Rectangle {
                        width: 22
                        height: Math.max(3, 32 * (modelData / Math.max(0.0001, backend.weekMax)))
                        y: 32 - height
                        radius: 3
                        color: index === 6 ? win.cAccent : (win.dark ? "#3DFFFFFF" : "#26000000")
                    }
                }
            }
        }

        Text {
            x: 18; y: 158
            width: win.width - 36
            text: backend.daysLeftText
            color: win.cSub
            elide: Text.ElideRight
            font.family: "Microsoft YaHei UI"
            font.pixelSize: 11
        }

        Text {
            x: 18; y: 182
            text: "更新于 " + (backend.lastUpdate.length ? backend.lastUpdate : "—")
            color: win.cSub
            opacity: 0.7
            font.family: "Segoe UI Variable Small"
            font.pixelSize: 10
        }

        Rectangle {
            id: refreshBtn
            x: win.width - 18 - 46
            y: 176
            width: 46
            height: 22
            radius: 4
            z: 2
            color: refreshArea.containsMouse ? win.cHover : "transparent"

            Text {
                anchors.centerIn: parent
                text: "刷新"
                color: win.cAccent
                font.family: "Microsoft YaHei UI"
                font.pixelSize: 11
            }

            MouseArea {
                id: refreshArea
                anchors.fill: parent
                hoverEnabled: true
                cursorShape: Qt.PointingHandCursor
                onClicked: backend.fetch_async()
            }
        }
    }

    MouseArea {
        id: dragArea
        anchors.fill: parent
        acceptedButtons: Qt.LeftButton | Qt.RightButton
        cursorShape: Qt.SizeAllCursor
        property point startCursor
        property point startPos
        property bool moved: false
        property bool dragging: false

        onPressed: function (m) {
            if (m.button !== Qt.LeftButton)
                return
            startCursor = dragArea.mapToGlobal(m.x, m.y)
            startPos = Qt.point(win.x, win.y)
            moved = false
            dragging = true
        }
        onPositionChanged: function (m) {
            if (!dragging)
                return
            var p = dragArea.mapToGlobal(m.x, m.y)
            var dx = p.x - startCursor.x
            var dy = p.y - startCursor.y
            if (Math.abs(dx) + Math.abs(dy) > 2)
                moved = true
            win.x = Math.round(startPos.x + dx)
            win.y = Math.round(startPos.y + dy)
        }
        onReleased: function (m) {
            dragging = false
            if (m.button === Qt.LeftButton && moved)
                backend.savePos(win.x, win.y)
        }
        onCanceled: dragging = false
        onClicked: function (m) {
            if (m.button === Qt.RightButton)
                backend.showMenu()
        }
        onDoubleClicked: backend.fetch_async()
    }

    SequentialAnimation {
        id: shake
        NumberAnimation { target: bal; property: "x"; to: 14; duration: 45 }
        NumberAnimation { target: bal; property: "x"; to: 21; duration: 45 }
        NumberAnimation { target: bal; property: "x"; to: 16; duration: 45 }
        NumberAnimation { target: bal; property: "x"; to: 20; duration: 45 }
        NumberAnimation { target: bal; property: "x"; to: 18; duration: 60 }
    }

    ParallelAnimation {
        id: flyAnim
        NumberAnimation { target: fly; property: "y"; from: 56; to: 22; duration: 900; easing.type: Easing.OutCubic }
        SequentialAnimation {
            NumberAnimation { target: fly; property: "opacity"; from: 0; to: 1; duration: 140 }
            PauseAnimation { duration: 420 }
            NumberAnimation { target: fly; property: "opacity"; to: 0; duration: 340 }
        }
    }

    function playSpend(gap) {
        fly.text = "-" + backend.symbol + gap.toFixed(2)
        fly.x = win.width - 20 - fly.implicitWidth
        fly.y = 56
        fly.opacity = 0
        flyAnim.restart()
        shake.restart()
    }

    NumberAnimation {
        id: waveAnim
        target: win
        property: "waveRadius"
        from: 0
        to: 470
        duration: 470
        easing.type: Easing.InOutQuad
        onFinished: win.transActive = false
    }

    ParallelAnimation {
        id: themeAnim
        NumberAnimation { target: themeIcon; property: "scale"; from: 0.55; to: 1.0; duration: 260; easing.type: Easing.OutBack }
        NumberAnimation { target: themeRipple; property: "opacity"; from: 0.5; to: 0; duration: 420; easing.type: Easing.OutCubic }
        NumberAnimation { target: themeRipple; property: "scale"; from: 0.9; to: 2.8; duration: 420; easing.type: Easing.OutCubic }
    }

    Connections {
        target: backend
        function onBalanceChanged() { win.shown = backend.balanceValue }
        function onSpend(gap) { win.playSpend(gap) }
        function onDarkChanged() {
            win.transOld = win.solidFor(!backend.dark)
            win.transActive = true
            win.waveRadius = 0
            waveAnim.restart()
            themeAnim.restart()
            themePress.restart()
        }
    }

    // ---------------- API Key 管理 ----------------
    Window {
        id: keyWin
        width: 372
        height: 458
        visible: backend.keyDialogOpen
        color: "transparent"
        flags: Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.Tool
        title: "DeepSeek API Key"

        // 位置只在打开时算一次，避免点击时重算导致整窗跳动
        property bool placed: false
        function place() {
            var sw = keyWin.screen.width
            var sh = keyWin.screen.height
            keyWin.x = Math.max(8, Math.min(win.x + Math.round((win.width - keyWin.width) / 2),
                                            sw - keyWin.width - 8))
            keyWin.y = Math.max(8, Math.min(win.y + Math.round((win.height - keyWin.height) / 2),
                                            sh - keyWin.height - 8))
            placed = true
        }

        readonly property bool mica: win.mica

        Rectangle {
            anchors.fill: parent
            radius: 10
            color: win.mica ? (win.dark ? "#33000000" : "#99FFFFFF")
                            : (win.dark ? "#F2202020" : "#F2F3F3F3")
            border.width: 1
            border.color: win.cBorder
        }

        // 窗口拖动（标题栏 + 空白处都能拖）
        Item {
            id: dragLayer
            anchors.fill: parent

            property point startCursor
            property point startPos
            property bool moved: false
            property bool dragging: false

            MouseArea {
                id: dlgDragArea
                anchors.fill: parent

                onPressed: function (m) {
                    if (m.button !== Qt.LeftButton)
                        return
                    dragLayer.startCursor = dlgDragArea.mapToGlobal(m.x, m.y)
                    dragLayer.startPos = Qt.point(keyWin.x, keyWin.y)
                    dragLayer.moved = false
                    dragLayer.dragging = true
                }
                onPositionChanged: function (m) {
                    if (!dragLayer.dragging)
                        return
                    var p = dlgDragArea.mapToGlobal(m.x, m.y)
                    var dx = p.x - dragLayer.startCursor.x
                    var dy = p.y - dragLayer.startCursor.y
                    if (Math.abs(dx) + Math.abs(dy) > 2)
                        dragLayer.moved = true
                    keyWin.x = Math.round(dragLayer.startPos.x + dx)
                    keyWin.y = Math.round(dragLayer.startPos.y + dy)
                }
                onReleased: dragLayer.dragging = false
                onCanceled: dragLayer.dragging = false
            }
        }

        Text {
            x: 18; y: 14
            text: "API Key"
            color: win.cText
            font.family: "Microsoft YaHei UI"
            font.pixelSize: 12
            font.weight: 600
        }

        Rectangle {
            id: keyClose
            x: keyWin.width - 18 - 26
            y: 7
            width: 26; height: 26
            radius: 5
            color: keyCloseArea.containsMouse ? win.cHover : "transparent"
            Behavior on color { ColorAnimation { duration: 130; easing.type: Easing.OutQuad } }

            Text {
                anchors.centerIn: parent
                text: "\uE711"
                color: win.cSub
                font.family: "Segoe Fluent Icons"
                font.pixelSize: 11
            }
            MouseArea {
                id: keyCloseArea
                anchors.fill: parent
                hoverEnabled: true
                cursorShape: Qt.PointingHandCursor
                onClicked: backend.closeKeyDialog()
            }
        }

        // 新增输入框：右键直接粘贴
        Rectangle {
            id: addField
            x: 18
            y: 44
            width: keyWin.width - 36 - 64 - 8
            height: 34
            radius: 6
            color: win.dark ? "#26FFFFFF" : "#0F000000"
            border.width: 1
            border.color: addInput.activeFocus ? win.cAccent : win.cBorder

            TextInput {
                id: addInput
                anchors.fill: parent
                anchors.leftMargin: 10
                anchors.rightMargin: 8
                verticalAlignment: TextInput.AlignVCenter
                color: win.cText
                font.family: "Segoe UI Variable Text"
                font.pixelSize: 12
                selectByMouse: true
                clip: true
                text: backend.draftKey
                onTextChanged: if (text !== backend.draftKey) backend.draftKey = text
                onAccepted: backend.addKey(text)
                Keys.onEscapePressed: backend.closeKeyDialog()
            }

            Text {
                anchors.left: parent.left
                anchors.leftMargin: 11
                anchors.verticalCenter: parent.verticalCenter
                text: "右键粘贴 sk-..."
                color: win.cSub
                opacity: 0.55
                font.family: "Segoe UI Variable Text"
                font.pixelSize: 12
                visible: addInput.text.length === 0
            }

            MouseArea {
                anchors.fill: parent
                acceptedButtons: Qt.LeftButton | Qt.RightButton
                onClicked: function (mouse) {
                    if (mouse.button === Qt.RightButton)
                        backend.pasteKey()
                    addInput.forceActiveFocus()
                }
            }
        }

        Rectangle {
            id: addBtn
            x: addField.x + addField.width + 8
            y: 44
            width: 64; height: 34
            radius: 6
            color: addArea.containsMouse ? win.cHover : "transparent"
            border.width: 1
            border.color: win.cBorder
            Behavior on color { ColorAnimation { duration: 130; easing.type: Easing.OutQuad } }

            Text {
                anchors.centerIn: parent
                text: "添加"
                color: win.cText
                font.family: "Microsoft YaHei UI"
                font.pixelSize: 12
            }
            MouseArea {
                id: addArea
                anchors.fill: parent
                hoverEnabled: true
                cursorShape: Qt.PointingHandCursor
                onClicked: backend.addKey(addInput.text)
            }
        }

        Text {
            x: 18; y: 84
            text: "右键输入框直接粘贴 · 最多保存 10 个，最多同时显示 3 个"
            color: win.cSub
            font.family: "Microsoft YaHei UI"
            font.pixelSize: 10
        }

        Rectangle {
            x: 18; y: 102
            width: keyWin.width - 36
            height: 1
            color: win.cBorder
        }

        Flickable {
            id: keyList
            x: 18
            y: 110
            width: keyWin.width - 36
            height: 236
            clip: true
            contentHeight: keyCol.height
            boundsBehavior: Flickable.StopAtBounds

            Column {
                id: keyCol
                width: keyList.width
                spacing: 2

                Repeater {
                    model: backend.keys
                    delegate: Rectangle {
                        id: rowItem
                        width: keyCol.width
                        height: 46
                        radius: 5
                        color: rowArea.containsMouse ? win.cHover : "transparent"
                        Behavior on color { ColorAnimation { duration: 130; easing.type: Easing.OutQuad } }

                        property int rowIndex: index

                        MouseArea {
                            id: rowArea
                            anchors.fill: parent
                            hoverEnabled: true
                            acceptedButtons: Qt.NoButton
                        }

                        // 名称（点一下改名）
                        Text {
                            id: nameText
                            anchors.left: parent.left
                            anchors.leftMargin: 8
                            y: 9
                            width: parent.width - 120
                            text: modelData.name
                            color: win.cText
                            elide: Text.ElideRight
                            font.family: "Microsoft YaHei UI"
                            font.pixelSize: 12
                            font.weight: 600

                            MouseArea {
                                anchors.fill: parent
                                cursorShape: Qt.IBeamCursor
                                onClicked: backend.beginRename(rowItem.rowIndex)
                            }
                        }

                        // 掩码 Key
                        Text {
                            anchors.left: parent.left
                            anchors.leftMargin: 8
                            y: 27
                            width: parent.width - 120
                            text: modelData.label
                            color: win.cSub
                            opacity: 0.75
                            elide: Text.ElideRight
                            font.family: "Segoe UI Variable Small"
                            font.pixelSize: 10
                        }

                        // 删除（在开关左边）
                        Rectangle {
                            id: delBtn
                            anchors.right: showSwitch.left
                            anchors.rightMargin: 10
                            anchors.verticalCenter: parent.verticalCenter
                            width: 24; height: 24
                            radius: 5
                            color: delArea.containsMouse ? win.cHover : "transparent"
                            Behavior on color { ColorAnimation { duration: 130; easing.type: Easing.OutQuad } }

                            Text {
                                anchors.centerIn: parent
                                text: "\uE74D"
                                color: delArea.containsMouse ? (win.dark ? "#FF99A4" : "#C42B1C") : win.cSub
                                font.family: "Segoe Fluent Icons"
                                font.pixelSize: 10
                            }
                            MouseArea {
                                id: delArea
                                anchors.fill: parent
                                hoverEnabled: true
                                cursorShape: Qt.PointingHandCursor
                                onClicked: backend.askRemove(index)
                            }
                        }

                        // 左右横拨开关
                        Rectangle {
                            id: showSwitch
                            anchors.right: parent.right
                            anchors.rightMargin: 6
                            anchors.verticalCenter: parent.verticalCenter
                            width: 38; height: 20
                            radius: 10
                            color: modelData.show ? win.cAccent
                                                 : (win.dark ? "#3AFFFFFF" : "#22000000")
                            Behavior on color { ColorAnimation { duration: 150; easing.type: Easing.OutQuad } }

                            Rectangle {
                                width: 14; height: 14
                                radius: 7
                                y: 3
                                x: modelData.show ? parent.width - width - 3 : 3
                                color: "#FFFFFF"
                                Behavior on x {
                                    NumberAnimation { duration: 150; easing.type: Easing.OutCubic }
                                }
                            }

                            MouseArea {
                                anchors.fill: parent
                                cursorShape: Qt.PointingHandCursor
                                onClicked: backend.toggleShow(index)
                            }
                        }
                    }
                }
            }

            Text {
                visible: backend.keys.length === 0
                anchors.horizontalCenter: parent.horizontalCenter
                y: 96
                text: "还没有保存的 Key"
                color: win.cSub
                font.family: "Microsoft YaHei UI"
                font.pixelSize: 11
            }
        }

        // 改名条（点名称后出现）
        Rectangle {
            id: renameBar
            x: 18
            y: 352
            width: keyWin.width - 36
            height: 34
            radius: 6
            visible: backend.renameIndex >= 0
            color: win.dark ? "#26FFFFFF" : "#0F000000"
            border.width: 1
            border.color: renameInput.activeFocus ? win.cAccent : win.cBorder

            Text {
                x: 10
                anchors.verticalCenter: parent.verticalCenter
                text: "名称"
                color: win.cSub
                font.family: "Microsoft YaHei UI"
                font.pixelSize: 10
            }

            TextInput {
                id: renameInput
                x: 42
                anchors.verticalCenter: parent.verticalCenter
                width: parent.width - 42 - 8 - 52 - 52 - 10
                height: 22
                focus: backend.renameIndex >= 0
                color: win.cText
                font.family: "Microsoft YaHei UI"
                font.pixelSize: 12
                selectByMouse: true
                clip: true
                text: backend.draftName
                onTextChanged: if (text !== backend.draftName) backend.draftName = text
                onAccepted: backend.commitRename()
                Keys.onReturnPressed: backend.commitRename()
                Keys.onEnterPressed: backend.commitRename()
                Keys.onEscapePressed: backend.endRename()
            }

            Rectangle {
                id: renameOk
                anchors.right: renameCancel.left
                anchors.rightMargin: 6
                anchors.verticalCenter: parent.verticalCenter
                width: 46; height: 24
                radius: 5
                color: renameOkArea.containsMouse ? win.cHover : "transparent"
                border.width: 1
                border.color: win.cBorder
                Behavior on color { ColorAnimation { duration: 130; easing.type: Easing.OutQuad } }

                Text {
                    anchors.centerIn: parent
                    text: "确定"
                    color: win.cText
                    font.family: "Microsoft YaHei UI"
                    font.pixelSize: 11
                }
                MouseArea {
                    id: renameOkArea
                    anchors.fill: parent
                    hoverEnabled: true
                    cursorShape: Qt.PointingHandCursor
                    onClicked: backend.commitRename()
                }
            }

            Rectangle {
                id: renameCancel
                anchors.right: parent.right
                anchors.rightMargin: 6
                anchors.verticalCenter: parent.verticalCenter
                width: 46; height: 24
                radius: 5
                color: renameCancelArea.containsMouse ? win.cHover : "transparent"
                border.width: 1
                border.color: win.cBorder
                Behavior on color { ColorAnimation { duration: 130; easing.type: Easing.OutQuad } }

                Text {
                    anchors.centerIn: parent
                    text: "取消"
                    color: win.cSub
                    font.family: "Microsoft YaHei UI"
                    font.pixelSize: 11
                }
                MouseArea {
                    id: renameCancelArea
                    anchors.fill: parent
                    hoverEnabled: true
                    cursorShape: Qt.PointingHandCursor
                    onClicked: backend.endRename()
                }
            }
        }

        Text {
            x: 18
            y: 354
            width: keyWin.width - 36
            visible: backend.renameIndex < 0
            text: backend.keyError.length ? backend.keyError
                                          : "Key 存在 Windows 凭据管理器，界面只显示首尾，中间 6 位隐藏"
            color: backend.keyError.length ? (win.dark ? "#FF99A4" : "#C42B1C") : win.cSub
            elide: Text.ElideRight
            font.family: "Microsoft YaHei UI"
            font.pixelSize: 10
        }

        Rectangle {
            x: 18; y: 372
            width: keyWin.width - 36
            height: 1
            color: win.cBorder
        }

        // 二次确认
        Item {
            id: confirmBar
            x: 18
            y: 382
            width: keyWin.width - 36
            height: 30
            visible: backend.pendingRemove >= 0

            Text {
                anchors.left: parent.left
                anchors.verticalCenter: parent.verticalCenter
                text: "删除这个 Key？"
                color: win.cText
                font.family: "Microsoft YaHei UI"
                font.pixelSize: 11
            }

            Rectangle {
                id: cancelBtn
                anchors.right: okBtn.left
                anchors.rightMargin: 8
                anchors.verticalCenter: parent.verticalCenter
                width: 58; height: 26
                radius: 5
                color: cancelArea.containsMouse ? win.cHover : "transparent"
                border.width: 1
                border.color: win.cBorder
                Behavior on color { ColorAnimation { duration: 130; easing.type: Easing.OutQuad } }

                Text {
                    anchors.centerIn: parent
                    text: "取消"
                    color: win.cText
                    font.family: "Microsoft YaHei UI"
                    font.pixelSize: 11
                }
                MouseArea {
                    id: cancelArea
                    anchors.fill: parent
                    hoverEnabled: true
                    cursorShape: Qt.PointingHandCursor
                    onClicked: backend.cancelRemove()
                }
            }

            Rectangle {
                id: okBtn
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                width: 58; height: 26
                radius: 5
                color: okArea.containsMouse ? (win.dark ? "#4A1717" : "#FDE7E9")
                                            : (win.dark ? "#3A1C1C" : "#FCE4E6")
                border.width: 1
                border.color: win.dark ? "#66FF99A4" : "#44C42B1C"
                Behavior on color { ColorAnimation { duration: 130; easing.type: Easing.OutQuad } }

                Text {
                    anchors.centerIn: parent
                    text: "删除"
                    color: win.dark ? "#FF99A4" : "#C42B1C"
                    font.family: "Microsoft YaHei UI"
                    font.pixelSize: 11
                }
                MouseArea {
                    id: okArea
                    anchors.fill: parent
                    hoverEnabled: true
                    cursorShape: Qt.PointingHandCursor
                    onClicked: backend.confirmRemove()
                }
            }
        }

        Rectangle {
            id: doneBtn
            x: keyWin.width - 18 - 72
            y: 418
            width: 72; height: 28
            radius: 6
            color: doneArea.containsMouse ? win.cHover : "transparent"
            border.width: 1
            border.color: win.cBorder
            Behavior on color { ColorAnimation { duration: 130; easing.type: Easing.OutQuad } }

            Text {
                anchors.centerIn: parent
                text: "完成"
                color: win.cText
                font.family: "Microsoft YaHei UI"
                font.pixelSize: 12
            }
            MouseArea {
                id: doneArea
                anchors.fill: parent
                hoverEnabled: true
                cursorShape: Qt.PointingHandCursor
                onClicked: backend.closeKeyDialog()
            }
        }

        Component.onCompleted: backend.registerDialog(keyWin)

        onVisibleChanged: {
            if (visible) {
                place()
                keyWin.requestActivate()
                Qt.callLater(function () { addInput.forceActiveFocus() })
            }
        }
    }

    Component.onCompleted: {
        win.shown = backend.balanceValue
        backend.attach(win)
    }
}

"""


# -------------------------------------------------------------------- Backend
class Backend(QObject):
    darkChanged = Signal()
    balanceChanged = Signal()
    symbolChanged = Signal()
    subChanged = Signal()
    clockChanged = Signal()
    accentChanged = Signal()
    errorChanged = Signal()
    expandedChanged = Signal()
    detailsChanged = Signal()
    keyDialogChanged = Signal()
    draftKeyChanged = Signal()
    keyErrorChanged = Signal()
    keysChanged = Signal()
    rowsChanged = Signal()
    pendingRemoveChanged = Signal()
    renameStateChanged = Signal()
    draftNameChanged = Signal()
    multiChanged = Signal()
    spend = Signal(float)

    def __init__(self, store, app):
        super().__init__()
        self.store = store
        self.app = app
        self.win11 = Win11()
        self.window = None
        self.dialog = None
        self.hwnd = 0
        self._dark = False
        self._balance = 0.0
        self._symbol = "¥"
        self._sub = "正在获取…"
        self._clock = ""
        self._accent = "#0F7B3F"
        self._error = False
        self._last = None
        self._busy = False
        self._menu = None

        # 详情 / 统计
        self._expanded = store.get_bool("expanded", False)
        self._topped = 0.0
        self._granted = 0.0
        self._today_spend = 0.0
        self._week = [0.0] * 7
        self._week_max = 1.0
        self._days_left = "还没有消耗记录"
        self._last_update = ""
        self._available = True
        self._stat = {}

        # API Key 列表（凭据管理器 JSON：最多 10 个，最多 3 个显示）
        self._keys = keys_load()
        self._rows = []
        self._multi = False
        self._shown = sum(1 for x in self._keys if x["show"])
        self._load_stats()
        self._key_dialog_open = False
        self._draft_key = ""
        self._key_error = ""
        self._pending_remove = -1
        self._rename_index = -1
        self._draft_name = ""

        theme = store.get("theme", "auto")
        self._dark = dark_by_system() if theme == "auto" else (theme == "dark")

    @Property(bool, notify=darkChanged)
    def dark(self):
        return self._dark

    @Property(bool, constant=True)
    def micaSupported(self):
        return mica_supported()

    @Property(float, notify=balanceChanged)
    def balanceValue(self):
        return self._balance

    @Property(str, notify=symbolChanged)
    def symbol(self):
        return self._symbol

    @Property(str, notify=subChanged)
    def sub(self):
        return self._sub

    @Property(str, notify=clockChanged)
    def clock(self):
        return self._clock

    @Property(str, notify=accentChanged)
    def accentColor(self):
        return self._accent

    @Property(bool, notify=errorChanged)
    def error(self):
        return self._error

    # ------------------------------------------------------------ 详情数据
    @Property(bool, notify=expandedChanged)
    def expanded(self):
        return self._expanded

    @Property(float, notify=detailsChanged)
    def topped(self):
        return self._topped

    @Property(float, notify=detailsChanged)
    def granted(self):
        return self._granted

    @Property(float, notify=detailsChanged)
    def todaySpend(self):
        return self._today_spend

    @Property(float, notify=detailsChanged)
    def weekMax(self):
        return self._week_max

    @Property("QVariantList", notify=detailsChanged)
    def weekSeries(self):
        return self._week

    @Property(str, notify=detailsChanged)
    def daysLeftText(self):
        return self._days_left

    @Property(str, notify=detailsChanged)
    def lastUpdate(self):
        return self._last_update

    @Property(bool, notify=detailsChanged)
    def available(self):
        return self._available

    # ---------------------------------------------------------- API Key 对话框
    @Property(bool, notify=keyDialogChanged)
    def keyDialogOpen(self):
        return self._key_dialog_open

    @Property(str, notify=draftKeyChanged)
    def draftKey(self):
        return self._draft_key

    @draftKey.setter
    def draftKey(self, value):
        if value != self._draft_key:
            self._draft_key = value
            self.draftKeyChanged.emit()

    @Property(str, notify=keyErrorChanged)
    def keyError(self):
        return self._key_error

    @Property(bool, notify=keysChanged)
    def hasKey(self):
        return bool(self._keys)

    @Property("QVariantList", notify=keysChanged)
    def keys(self):
        return [{"label": mask_key(x["k"]), "show": x["show"],
                 "name": x.get("name") or default_name(x["k"])} for x in self._keys]

    @Property("QVariantList", notify=rowsChanged)
    def rows(self):
        return self._rows

    @Property(int, notify=renameStateChanged)
    def renameIndex(self):
        return self._rename_index

    @Property(str, notify=draftNameChanged)
    def draftName(self):
        return self._draft_name

    @draftName.setter
    def draftName(self, value):
        if value != self._draft_name:
            self._draft_name = value
            self.draftNameChanged.emit()

    @Property(int, notify=pendingRemoveChanged)
    def pendingRemove(self):
        return self._pending_remove

    @Property(bool, notify=multiChanged)
    def multiView(self):
        return self._multi

    @Property(int, notify=multiChanged)
    def multiHeight(self):
        return 54 + max(1, self._shown) * 46

    @Slot(QObject)
    def registerDialog(self, window):
        self.dialog = window
        self.win11.apply_backdrop(int(window.winId()), self._dark)

    @Slot()
    def refreshDialogBackdrop(self):
        if self.dialog:
            try:
                self.win11.apply_backdrop(int(self.dialog.winId()), self._dark)
            except Exception as e:
                print("dialog backdrop:", e)

    @Slot()
    def openKeyDialog(self):
        self._draft_key = ""
        self._key_error = ""
        self.draftKeyChanged.emit()
        self.keyErrorChanged.emit()
        self._key_dialog_open = True
        self.keyDialogChanged.emit()
        if self.dialog:
            QTimer.singleShot(20, self.refreshDialogBackdrop)
            QTimer.singleShot(30, self._focus_dialog)

    def _focus_dialog(self):
        try:
            self.dialog.requestActivate()
            self.win11.apply_backdrop(int(self.dialog.winId()), self._dark)
        except Exception as e:
            print("dialog:", e)

    @Slot()
    def pasteKey(self):
        """右键输入框时直接从剪贴板取值"""
        try:
            txt = (QGuiApplication.clipboard().text() or "").strip()
        except Exception:
            txt = ""
        if txt:
            self._draft_key = txt
            self._key_error = ""
            self.draftKeyChanged.emit()
            self.keyErrorChanged.emit()

    @Slot()
    def closeKeyDialog(self):
        self._pending_remove = -1
        self.pendingRemoveChanged.emit()
        self._key_dialog_open = False
        self._key_error = ""
        self.keyErrorChanged.emit()
        self.keyDialogChanged.emit()

    def _sync_ui(self):
        self._shown = sum(1 for x in self._keys if x["show"])
        self.keysChanged.emit()
        multi = self._shown >= 2
        if multi != self._multi:
            self._multi = multi
            self.multiChanged.emit()
        if self.window:
            QTimer.singleShot(260, self._keep_on_screen)

    def _commit_keys(self):
        keys_save(self._keys)
        self._sync_ui()
        self.fetch_async()

    @Slot(str)
    def addKey(self, text):
        key = (text or "").strip()
        if not key:
            self._key_error = "先粘贴一个 Key"
        elif not key.startswith("sk-") or len(key) < 20:
            self._key_error = "Key 格式不对，应以 sk- 开头"
        elif any(x["k"] == key for x in self._keys):
            self._key_error = "这个 Key 已经保存过了"
        elif len(self._keys) >= 10:
            self._key_error = "最多保存 10 个 Key"
        else:
            self._keys.append({"k": key, "show": len(self._keys) == 0,
                               "name": default_name(key)})
            self._draft_key = ""
            self._key_error = ""
            self.draftKeyChanged.emit()
            self._commit_keys()
            return
        self.keyErrorChanged.emit()

    @Slot(int)
    def askRemove(self, index):
        """只是弹二次确认，真正删除走 confirmRemove"""
        self._pending_remove = index if 0 <= index < len(self._keys) else -1
        self._key_error = ""
        self.keyErrorChanged.emit()
        self.pendingRemoveChanged.emit()

    @Slot()
    def cancelRemove(self):
        if self._pending_remove != -1:
            self._pending_remove = -1
            self.pendingRemoveChanged.emit()

    @Slot()
    def confirmRemove(self):
        index = getattr(self, "_pending_remove", -1)
        self._pending_remove = -1
        self.pendingRemoveChanged.emit()
        if 0 <= index < len(self._keys):
            self._keys.pop(index)
            self._key_error = ""
            self._commit_keys()

    @Slot(int)
    def removeKey(self, index):
        if 0 <= index < len(self._keys):
            self._keys.pop(index)
            self._key_error = ""
            self._commit_keys()

    @Slot(int)
    def beginRename(self, index):
        """进入改名编辑，底部出现输入框"""
        if 0 <= index < len(self._keys):
            self._rename_index = index
            self._draft_name = self._keys[index].get("name") or default_name(self._keys[index]["k"])
            self.renameStateChanged.emit()
            self.draftNameChanged.emit()

    @Slot()
    def endRename(self):
        self._rename_index = -1
        self._draft_name = ""
        self.renameStateChanged.emit()
        self.draftNameChanged.emit()

    @Slot()
    def commitRename(self):
        idx = self._rename_index
        name = (self._draft_name or "").strip()
        self.endRename()
        if 0 <= idx < len(self._keys):
            self._keys[idx]["name"] = name or default_name(self._keys[idx]["k"])
            self._key_error = ""
            self._commit_keys()

    @Slot(int, str)
    def renameKey(self, index, name):
        if not (0 <= index < len(self._keys)):
            return
        text = (name or "").strip()
        self._keys[index]["name"] = text or default_name(self._keys[index]["k"])
        self._key_error = ""
        self._commit_keys()

    @Slot(int)
    def toggleShow(self, index):
        if not (0 <= index < len(self._keys)):
            return
        item = self._keys[index]
        if item["show"]:
            if sum(1 for x in self._keys if x["show"]) <= 1:
                self._key_error = "至少要显示一个 Key"
                self.keyErrorChanged.emit()
                return
            item["show"] = False
        else:
            if sum(1 for x in self._keys if x["show"]) >= 3:
                self._key_error = "最多同时显示 3 个 Key"
                self.keyErrorChanged.emit()
                return
            item["show"] = True
        self._key_error = ""
        self._commit_keys()

    # ------------------------------------------------------------ 统计
    def _load_stats(self):
        """每个 Key 单独的统计，存在注册表里"""
        self._stat = {}
        raw = self.store.get("stats", "")
        if not raw:
            return
        try:
            d = json.loads(raw)
        except Exception:
            return
        if not isinstance(d, dict):
            return
        if "days" in d or "last_balance" in d:
            if self._keys:
                days = {}
                for kk, vv in (d.get("days") or {}).items():
                    try:
                        days[str(kk)] = float(vv)
                    except Exception:
                        pass
                lb = d.get("last_balance")
                self._stat[key_uid(self._keys[0]["k"])] = {
                    "days": days,
                    "last": float(lb) if lb is not None else None,
                }
            return
        for uid, v in d.items():
            if not isinstance(v, dict):
                continue
            days = {}
            for kk, vv in (v.get("days") or {}).items():
                try:
                    days[str(kk)] = float(vv)
                except Exception:
                    pass
            lb = v.get("last")
            self._stat[str(uid)] = {
                "days": days,
                "last": float(lb) if lb is not None else None,
            }

    def _save_stats(self):
        try:
            cutoff = (date.today() - timedelta(days=90)).isoformat()
            clean = {}
            for uid, v in self._stat.items():
                days = {k: x for k, x in v["days"].items() if k >= cutoff}
                clean[uid] = {"days": days, "last": v.get("last")}
            self.store.set("stats", json.dumps(clean, ensure_ascii=False,
                                               separators=(",", ":")))
        except Exception as e:
            print("stats:", e)

    def _stat_for(self, key):
        uid = key_uid(key)
        st = self._stat.get(uid)
        if st is None:
            st = {"days": {}, "last": None}
            self._stat[uid] = st
        return uid, st

    def _record_stats(self, st, total):
        last = st.get("last")
        if last is not None:
            spent = last - total
            if spent > 0.0005:
                day = date.today().isoformat()
                st["days"][day] = round(st["days"].get(day, 0.0) + spent, 4)
        st["last"] = total

    def _today_of(self, st):
        return round(st["days"].get(date.today().isoformat(), 0.0), 2)

    def _update_details(self, uid, total):
        st = self._stat.get(uid) or {"days": {}}
        days = st["days"]
        today = date.today()
        series = [round(days.get((today - timedelta(days=i)).isoformat(), 0.0), 2)
                  for i in range(6, -1, -1)]
        self._week = series
        self._week_max = max(series + [0.01])
        self._today_spend = series[-1]
        active = [v for v in series if v > 0]
        if not active:
            self._days_left = "还没有消耗记录，攒够数据后估算可用天数"
        else:
            avg = sum(active) / len(active)
            left = total / avg if avg > 0 else 0
            if left >= 365:
                self._days_left = "按近期消耗 ≈ 可用一年以上"
            else:
                self._days_left = "按近 7 日均耗 ≈ 还能用 %d 天" % max(1, round(left))
        self.detailsChanged.emit()

    # ---------------------------------------------------------- 生命周期
    @Slot(QObject)
    def attach(self, window):
        self.window = window
        self.hwnd = int(window.winId())
        self.win11.apply_backdrop(self.hwnd, self._dark)
        self._restore_geometry()
        self._apply_pinned(self.store.get_bool("pinned", True))
        window.setVisible(True)
        QTimer.singleShot(80, lambda: self.win11.apply_backdrop(self.hwnd, self._dark))
        self._tick()
        if self._keys:
            self.fetch_async()
        else:
            self._set_error("请先设置 API Key")
            QTimer.singleShot(650, self.openKeyDialog)

    def _restore_geometry(self):
        scr = QGuiApplication.primaryScreen().availableGeometry()
        try:
            sx = self.store.get("x", "")
            sy = self.store.get("y", "")
            x = int(sx) if sx else scr.right() - WIN_W - MARGIN
            y = int(sy) if sy else scr.top() + MARGIN
        except Exception:
            x, y = scr.right() - WIN_W - MARGIN, scr.top() + MARGIN
        x = max(scr.left(), min(x, scr.right() - WIN_W))
        y = max(scr.top(), min(y, scr.bottom() - (WIN_H_OPEN if self._expanded else WIN_H)))
        self.window.setX(x)
        self.window.setY(y)

    @Slot(int, int)
    def savePos(self, x, y):
        self.store.set("x", int(x))
        self.store.set("y", int(y))

    # -------------------------------------------------------------- 主题
    @Slot()
    def toggleTheme(self):
        self._set_theme("dark" if not self._dark else "light")

    @Slot(str)
    def setTheme(self, mode):
        self._set_theme(mode)

    def _set_theme(self, mode):
        self.store.set("theme", mode)
        dark = dark_by_system() if mode == "auto" else (mode == "dark")
        if dark == self._dark:
            return
        self._dark = dark
        self.darkChanged.emit()
        self.win11.apply_backdrop(self.hwnd, self._dark)
        if self.dialog:
            self.win11.apply_backdrop(int(self.dialog.winId()), self._dark)
        self._update_accent()

    def _update_accent(self):
        c = self._accent_for(self._balance)
        if c != self._accent:
            self._accent = c
            self.accentChanged.emit()

    # -------------------------------------------------------------- 置顶
    def _apply_pinned(self, on):
        self.store.set("pinned", "1" if on else "0")
        self.win11.set_topmost(self.hwnd, on)

    @Slot()
    def togglePin(self):
        pinned = self.store.get_bool("pinned", True)
        self._apply_pinned(not pinned)

    # ---------------------------------------------------------- 展开详情
    @Slot()
    def toggleExpanded(self):
        self._expanded = not self._expanded
        self.store.set("expanded", "1" if self._expanded else "0")
        self.expandedChanged.emit()
        QTimer.singleShot(220, self._keep_on_screen)

    def _keep_on_screen(self):
        if not self.window:
            return
        scr = QGuiApplication.primaryScreen().availableGeometry()
        h = int(self.window.property("height") or WIN_H)
        x = max(scr.left(), min(self.window.x(), scr.right() - WIN_W))
        y = max(scr.top(), min(self.window.y(), scr.bottom() - h))
        self.window.setX(x)
        self.window.setY(y)

    # ---------------------------------------------------------- 开机自启
    def _startup_bat(self):
        base = os.path.join(os.environ.get("APPDATA", ""),
                            r"Microsoft\Windows\Start Menu\Programs\Startup")
        return os.path.join(base, "DeepSeek余额悬浮窗.bat")

    @Slot(result=bool)
    def autostartEnabled(self):
        return os.path.exists(self._startup_bat())

    def _notify(self, text, title=APP_NAME):
        """窗口化运行时没有控制台，出错要用弹窗告诉用户"""
        try:
            ctypes.windll.user32.MessageBoxW(0, text, title, 0x10)
        except Exception:
            print(text)

    def _write_startup_bat(self, path):
        line = None
        lite_exe = os.environ.get("DSH_LITE_EXE")
        if getattr(sys, "frozen", False):
            line = 'start "" "%s"' % sys.executable
        elif lite_exe and os.path.exists(lite_exe):
            # Lite 版由启动器拉起，自启必须指向启动器本身
            line = 'start "" "%s"' % lite_exe
        else:
            pyw = os.path.join(os.path.dirname(sys.executable), "pythonw.exe")
            if not os.path.exists(pyw):
                pyw = sys.executable
            line = 'start "" "%s" "%s"' % (pyw, os.path.join(HERE, "balance_float.py"))

        os.makedirs(os.path.dirname(path), exist_ok=True)
        # 启动目录偶尔会被杀软/索引服务短暂占用，重试几次
        last = None
        for _ in range(4):
            try:
                with open(path, "w", encoding="gbk", newline="") as f:
                    f.write("@echo off\r\n" + line + "\r\n")
                return True
            except Exception as e:
                last = e
                time.sleep(0.2)
        raise last

    @Slot()
    def toggleAutostart(self):
        p = self._startup_bat()
        try:
            if os.path.exists(p):
                os.remove(p)
            else:
                self._write_startup_bat(p)
        except Exception as e:
            print("autostart:", e)
            self._notify("设置开机自启失败：\n\n%s\n\n"
                         "路径：%s\n\n"
                         "可以手动把启动器/程序做成快捷方式放进启动文件夹。" % (e, p))

    # ---------------------------------------------------------- 右键菜单
    @Slot()
    def showMenu(self):
        dark = self._dark
        bg = "#2B2B2B" if dark else "#F9F9F9"
        fg = "#FFFFFF" if dark else "#1B1B1F"
        hover = "#3A3A3A" if dark else "#EDEDED"
        line = "#3D3D3D" if dark else "#E3E3E3"

        m = QMenu()
        m.setAttribute(Qt.WA_TranslucentBackground)
        # 悬浮窗本身是置顶的，菜单必须也置顶，否则会被卡片挡住
        m.setWindowFlags(m.windowFlags() | Qt.FramelessWindowHint
                         | Qt.NoDropShadowWindowHint | Qt.WindowStaysOnTopHint)
        m.setMinimumWidth(158)
        style = ("QMenu{background:%s;border:1px solid %s;border-radius:8px;padding:5px;"
                 "color:%s;font-family:'Microsoft YaHei UI';font-size:12px;}"
                 "QMenu::item{padding:7px 26px 7px 12px;border-radius:5px;}"
                 "QMenu::item:selected{background:%s;}"
                 "QMenu::separator{height:1px;background:%s;margin:4px 8px;}"
                 % (bg, line, fg, hover, line))
        m.setStyleSheet(style)

        m.addAction("立即刷新", self.fetch_async)
        m.addAction("API Key 管理…", self.openKeyDialog)
        m.addSeparator()
        theme_menu = m.addMenu("主题")
        theme_menu.setStyleSheet(style)
        theme_menu.setWindowFlags(theme_menu.windowFlags() | Qt.FramelessWindowHint
                                  | Qt.NoDropShadowWindowHint | Qt.WindowStaysOnTopHint)
        cur_theme = self.store.get("theme", "auto")
        for label, mode in (("亮色", "light"), ("暗色", "dark"), ("跟随系统", "auto")):
            act = theme_menu.addAction(("● " if cur_theme == mode else "    ") + label)
            act.triggered.connect(lambda _=False, v=mode: self._set_theme(v))

        pinned = self.store.get_bool("pinned", True)
        act = m.addAction("取消置顶" if pinned else "置顶显示")
        act.triggered.connect(self.togglePin)
        auto_on = self.autostartEnabled()
        act = m.addAction(("✓ " if auto_on else "    ") + "开机自启")
        act.triggered.connect(self.toggleAutostart)
        m.addSeparator()
        m.addAction("退出", self.app.quit)
        self._menu = m
        m.aboutToHide.connect(self._clear_menu)
        m.popup(QCursor.pos())

    def _clear_menu(self):
        m = getattr(self, "_menu", None)
        if m is not None:
            m.deleteLater()
            self._menu = None

    # -------------------------------------------------------------- 数据
    def _tick(self):
        self._clock = time.strftime("%H:%M")
        self.clockChanged.emit()
        if self.store.get("theme", "auto") == "auto":
            d = dark_by_system()
            if d != self._dark:
                self._dark = d
                self.darkChanged.emit()
                self.win11.apply_backdrop(self.hwnd, self._dark)
                if self.dialog:
                    self.win11.apply_backdrop(int(self.dialog.winId()), self._dark)
                self._update_accent()
        try:
            wait = max(5, int(self.store.get("interval", "60")))
        except Exception:
            wait = 60
        QTimer.singleShot(wait * 1000, self._tick)

    @Slot()
    def fetch_async(self):
        if self._busy:
            return
        self._busy = True
        threading.Thread(target=self._fetch, daemon=True).start()

    def _accent_for(self, total):
        if total > 5:
            return "#6CCB5F" if self._dark else "#0F7B3F"
        if total > 0.5:
            return "#FCE100" if self._dark else "#9D5D00"
        return "#FF99A4" if self._dark else "#C42B1C"

    def _query(self, key, url):
        req = urllib.request.Request(url, headers={
            "Authorization": "Bearer " + key,
            "Accept": "application/json",
        })
        with urllib.request.urlopen(req, timeout=15) as r:
            return json.loads(r.read().decode("utf-8"))

    def _fetch(self):
        try:
            url = (self.store.get("url", DEFAULT_URL) or DEFAULT_URL).strip()
            shown = [x for x in self._keys if x["show"]][:3]
            if not shown:
                self._busy = False
                self._set_error("右键菜单 → API Key，添加一个 Key")
                return
            rows = []
            primary = None
            for item in shown:
                key = item["k"]
                try:
                    data = self._query(key, url)
                    infos = data.get("balance_infos") or []
                    it = infos[0]
                    cur = it.get("currency", "CNY")
                    sym = "¥" if cur == "CNY" else cur + " "
                    total = float(it.get("total_balance", 0))
                    got = float(it.get("granted_balance", 0))
                    topped = float(it.get("topped_up_balance", 0))
                    available = bool(data.get("is_available", True))
                    _uid, st = self._stat_for(key)
                    self._record_stats(st, total)
                    today = self._today_of(st)
                    rows.append({
                        "label": mask_key(key),
                        "value": sym + "%.2f" % total,
                        "total": total,
                        "spend": ("-" + sym + "%.2f" % today) if today > 0 else (sym + "0.00"),
                        "ok": available,
                    })
                    if primary is None:
                        primary = {"key": key, "sym": sym, "total": total, "got": got,
                                   "topped": topped, "available": available}
                except Exception as e:
                    print("err:", e)
                    rows.append({"label": mask_key(key), "value": "—", "total": 0.0,
                                 "spend": "读取失败", "ok": False})
            self._busy = False
            self._clock = time.strftime("%H:%M")
            self._rows = rows
            self._last_update = time.strftime("%H:%M:%S")
            self._save_stats()
            if primary is None:
                self._set_error("全部 Key 都读取失败，检查网络")
            else:
                self._apply_primary(primary)
            self.clockChanged.emit()
            self.rowsChanged.emit()
        except Exception as e:
            print("err:", e)
            self._set_error("拉取失败，检查网络 / key")

    def _apply_primary(self, info):
        sym = info["sym"]
        total = info["total"]
        got = info["got"]
        available = info["available"]
        sub = "可用余额" + ((" · 赠送 " + sym + ("%.2f" % got)) if got else "")
        if not available:
            sub = "余额不足，API 调用会被拒绝"
        old = self._last
        self._last = total
        self._symbol = sym
        self._balance = total
        self._sub = sub
        self._error = False
        self._topped = info["topped"]
        self._granted = got
        self._available = available
        uid, _st = self._stat_for(info["key"])
        self._update_details(uid, total)
        self.symbolChanged.emit()
        self.subChanged.emit()
        self.errorChanged.emit()
        self._update_accent()
        self.balanceChanged.emit()
        if old is not None and total < old - 0.0001:
            self.spend.emit(old - total)

    def _set_error(self, text):
        self._busy = False
        self._error = True
        self._sub = text
        self._accent = "#FF99A4" if self._dark else "#C42B1C"
        self.subChanged.emit()
        self.errorChanged.emit()
        self.accentChanged.emit()


_MUTEX = None


def already_running():
    """用命名互斥体保证只开一个悬浮窗"""
    global _MUTEX
    try:
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        _MUTEX = kernel32.CreateMutexW(None, False, "Local\\DeepSeekBalanceFloat")
        return ctypes.get_last_error() == 183  # ERROR_ALREADY_EXISTS
    except Exception:
        return False


def main():
    if already_running():
        return 0

    migrate_legacy_config()

    QQuickWindow.setDefaultAlphaBuffer(True)
    QGuiApplication.setHighDpiScaleFactorRoundingPolicy(
        Qt.HighDpiScaleFactorRoundingPolicy.PassThrough)

    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setQuitOnLastWindowClosed(True)

    backend = Backend(Store(), app)

    engine = QQmlApplicationEngine()
    engine.rootContext().setContextProperty("backend", backend)
    engine.loadData(QByteArray(QML.encode("utf-8")), QUrl.fromLocalFile(HERE + "/"))
    if not engine.rootObjects():
        print("QML 加载失败")
        return 1

    app.aboutToQuit.connect(backend._save_stats)
    return app.exec()


if __name__ == "__main__":
    sys.exit(main())
