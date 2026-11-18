#!/usr/bin/env python3
"""制作带背景、图标布局与「应用程序」快捷方式的安装 DMG。

用法: python3 scripts/make_dmg.py <App.app> <output.dmg> [version]
依赖: dmgbuild（见 requirements-build.txt，仅构建期使用）。
"""
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def main() -> int:
    try:
        from dmgbuild import build_dmg
    except ImportError:
        print("缺少 DMG 构建依赖 dmgbuild。请先在仓库根目录执行：", file=sys.stderr)
        print("  python3 -m venv .venv-build", file=sys.stderr)
        print("  .venv-build/bin/python -m pip install -r macos/scripts/requirements-build.txt", file=sys.stderr)
        print("  PYTHON=.venv-build/bin/python ./scripts/build.sh", file=sys.stderr)
        return 1

    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    app_path = os.path.abspath(sys.argv[1])
    output = os.path.abspath(sys.argv[2])
    version = sys.argv[3] if len(sys.argv) > 3 else "1.0.0"
    app_name = os.path.basename(app_path)
    background = os.path.join(ROOT, "resources", "dmg", "background.png")
    volume_icon = os.path.join(ROOT, "resources", "AppIcon.icns")

    for path in (app_path, background, volume_icon):
        if not os.path.exists(path):
            print(f"缺少文件: {path}", file=sys.stderr)
            return 1

    if os.path.exists(output):
        os.remove(output)

    settings = {
        "files": [app_path],
        "symlinks": {"应用程序": "/Applications"},
        "icon": volume_icon,
        "background": background,
        "window_rect": ((260, 160), (660, 420)),
        "default_view": "icon-view",
        "show_status_bar": False,
        "show_tab_view": False,
        "show_toolbar": False,
        "show_pathbar": False,
        "show_sidebar": False,
        "icon_size": 128.0,
        "text_size": 13.0,
        "label_pos": "bottom",
        "arrange_by": None,
        "grid_offset": (0, 0),
        "grid_spacing": 100.0,
        "scroll_position": (0.0, 0.0),
        "icon_locations": {
            app_name: (165, 220),
            "应用程序": (495, 220),
        },
        "format": "UDZO",
        "filesystem": "HFS+",
    }

    build_dmg(output, "DeepSeek 余额", settings=settings)
    set_file_icon(output)
    print(f"DMG 已生成: {output}  (v{version})")
    return 0


def set_file_icon(dmg_path: str) -> None:
    """给 .dmg 文件本身加上应用图标，Finder 里更醒目。"""
    tools = ["/usr/bin/DeRez", "/usr/bin/Rez", "/usr/bin/SetFile"]
    if not all(os.path.exists(t) for t in tools):
        return
    source_icon = os.path.join(ROOT, "resources", "icon-1024.png")
    if not os.path.exists(source_icon):
        return
    with tempfile.TemporaryDirectory() as tmp:
        png = os.path.join(tmp, "icon.png")
        rsrc = os.path.join(tmp, "icon.rsrc")
        shutil.copyfile(source_icon, png)
        subprocess.run(["/usr/bin/sips", "-i", png], check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        with open(rsrc, "w") as handle:
            subprocess.run(["/usr/bin/DeRez", "-only", "icns", png],
                           check=True, stdout=handle)
        subprocess.run(["/usr/bin/Rez", "-append", rsrc, "-o", dmg_path],
                       check=True, stdout=subprocess.DEVNULL)
        subprocess.run(["/usr/bin/SetFile", "-a", "C", dmg_path], check=True)


if __name__ == "__main__":
    sys.exit(main())
