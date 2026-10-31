# -*- coding: utf-8 -*-
"""
DeepSeek 余额悬浮窗 · Lite 启动器
--------------------------------
体积只有几百 KB：本身不含 Python 运行时，而是调用本机已安装的
Python + PySide6 来运行内嵌的应用源码。

流程：
1. 把内嵌的 balance_float.py 释放到 %TEMP%\\DeepSeekBalanceFloat\\
2. 探测可用的 Python 解释器（py 启动器 / PATH / 注册表 / 常见安装路径）
3. 选中第一个能 import PySide6 的解释器，以 detached 方式拉起应用
4. 本进程立即退出

因此程序本体目录不会产生任何文件。
"""
import ctypes
import glob
import os
import shutil
import subprocess
import sys
import tempfile

APP_NAME = "DeepSeek 余额悬浮窗"
SRC_NAME = "balance_float.py"
TEMP_SUBDIR = "DeepSeekBalanceFloat"

CREATE_NO_WINDOW = 0x08000000
DETACHED_PROCESS = 0x00000008


def msgbox(text, title=APP_NAME + " · Lite", icon=0x10):
    """无控制台环境下的提示框（0x10 错误 / 0x40 信息）"""
    try:
        ctypes.windll.user32.MessageBoxW(0, text, title, icon)
    except Exception:
        pass


def find_python_candidates():
    """收集可能可用的 Python 解释器，按优先级排序"""
    out = []

    for name in ("pythonw.exe", "python.exe"):
        p = shutil.which(name)
        if p:
            out.append(p)

    py_launcher = shutil.which("py.exe")
    if py_launcher:
        out.append(py_launcher)

    patterns = [
        os.path.expandvars(r"%LOCALAPPDATA%\Programs\Python\Python3*\pythonw.exe"),
        os.path.expandvars(r"%ProgramFiles%\Python3*\pythonw.exe"),
        os.path.expandvars(r"%ProgramFiles(x86)%\Python3*\pythonw.exe"),
        r"C:\Python3*\pythonw.exe",
    ]
    for pat in patterns:
        out.extend(sorted(glob.glob(pat), reverse=True))

    # 注册表里登记的安装路径
    try:
        import winreg

        for hive in (winreg.HKEY_CURRENT_USER, winreg.HKEY_LOCAL_MACHINE):
            try:
                with winreg.OpenKey(hive, r"SOFTWARE\Python\PythonCore") as root:
                    i = 0
                    while True:
                        try:
                            ver = winreg.EnumKey(root, i)
                        except OSError:
                            break
                        i += 1
                        try:
                            with winreg.OpenKey(root, ver + r"\InstallPath") as ip:
                                d, _ = winreg.QueryValueEx(ip, "")
                            for exe in ("pythonw.exe", "python.exe"):
                                p = os.path.join(d, exe)
                                if os.path.exists(p):
                                    out.append(p)
                        except OSError:
                            pass
            except OSError:
                pass
    except Exception:
        pass

    # 去重保序
    seen = set()
    uniq = []
    for p in out:
        k = os.path.normcase(os.path.abspath(p))
        if k not in seen:
            seen.add(k)
            uniq.append(p)
    return uniq


def has_pyside6(exe):
    """检查该解释器能否 import PySide6"""
    if os.path.basename(exe).lower() == "py.exe":
        cmd = [exe, "-3", "-c", "import PySide6"]
    else:
        cmd = [exe, "-c", "import PySide6"]
    try:
        r = subprocess.run(cmd, stdin=subprocess.DEVNULL,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                           timeout=40, creationflags=CREATE_NO_WINDOW)
        return r.returncode == 0
    except Exception:
        return False


def resource_source():
    """内嵌源码的位置（PyInstaller 解包目录或脚本同级）"""
    base = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base, SRC_NAME)


def main():
    src = resource_source()
    if not os.path.exists(src):
        msgbox("程序文件缺失，请重新下载 Lite 版。")
        return 1

    target_dir = os.path.join(tempfile.gettempdir(), TEMP_SUBDIR)
    try:
        os.makedirs(target_dir, exist_ok=True)
        target = os.path.join(target_dir, SRC_NAME)
        shutil.copyfile(src, target)
    except Exception as e:
        msgbox("无法写入临时目录：\n%s" % e)
        return 1

    tried = []
    for exe in find_python_candidates():
        tried.append(exe)
        if not has_pyside6(exe):
            continue
        cmd = [exe, target]
        is_console = exe.lower().endswith("python.exe")
        flags = DETACHED_PROCESS | (CREATE_NO_WINDOW if is_console else 0)
        env = dict(os.environ)
        env["DSH_LITE_EXE"] = os.path.abspath(sys.executable)
        try:
            subprocess.Popen(cmd, creationflags=flags, close_fds=True,
                             stdin=subprocess.DEVNULL,
                             stdout=subprocess.DEVNULL,
                             stderr=subprocess.DEVNULL,
                             cwd=target_dir, env=env)
            return 0
        except Exception:
            continue

    detail = ""
    if tried:
        detail = "\n已尝试：\n" + "\n".join("· " + t for t in tried[:6])

    msgbox(
        "没有找到可用的 Python 环境。\n\n"
        "Lite 版体积小，是因为它复用本机已有的 Python 与 PySide6，\n"
        "需要满足：\n"
        "  1. Python 3.9 或更高版本\n"
        "  2. 已安装 PySide6-Essentials\n\n"
        "安装方法：\n"
        "  1. 到 https://www.python.org/downloads/ 安装 Python\n"
        "     （安装时勾选 “Add python.exe to PATH”）\n"
        "  2. 打开命令行执行：\n"
        "         pip install PySide6-Essentials\n\n"
        "如果不想配置环境，请改用完整版（内置运行时，双击即用）。"
        + detail
    )
    return 1


if __name__ == "__main__":
    sys.exit(main())
