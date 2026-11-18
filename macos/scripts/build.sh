#!/bin/bash
# 构建 macOS 通用二进制并打包为 .app / .zip。
# 需要 Xcode Command Line Tools（Swift 6.2+，macOS 26 SDK）。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

VERSION="${VERSION:-1.0.0}"
BUILD="${BUILD:-1}"
APP_NAME="DeepSeek 余额"
APP="dist/${APP_NAME}.app"
SDK="$(xcrun --sdk macosx --show-sdk-path)"
SOURCES=(Sources/DeepSeekBalanceFloat/*.swift)

mkdir -p build dist

echo "==> 编译 arm64 / x86_64（macOS 26）"
for ARCH in arm64 x86_64; do
  if [[ ! -x "build/DeepSeekBalanceFloat-$ARCH" || "${FORCE:-0}" == "1" ]]; then
    swiftc -O -wmo -target "${ARCH}-apple-macos26.0" -sdk "$SDK" "${SOURCES[@]}" \
      -o "build/DeepSeekBalanceFloat-$ARCH"
  fi
done

echo "==> 合并通用二进制"
lipo -create build/DeepSeekBalanceFloat-arm64 build/DeepSeekBalanceFloat-x86_64 \
  -output build/DeepSeekBalanceFloat

echo "==> 自检"
./build/DeepSeekBalanceFloat --self-test >/dev/null

echo "==> 组装 ${APP_NAME}.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp build/DeepSeekBalanceFloat "$APP/Contents/MacOS/DeepSeekBalanceFloat"
cp resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
cp resources/Info.plist "$APP/Contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$VERSION" "$APP/Contents/Info.plist"
plutil -replace CFBundleVersion -string "$BUILD" "$APP/Contents/Info.plist"
plutil -lint "$APP/Contents/Info.plist" >/dev/null
xattr -cr "$APP"

SIGN_IDENTITY="${DEVELOPER_ID_APPLICATION:-}"
if [[ -z "$SIGN_IDENTITY" ]]; then
  SIGN_IDENTITY="$(security find-identity -v -p codesigning 2>/dev/null \
    | sed -n 's/.*"\(Developer ID Application:.*\)"/\1/p' | head -1)"
fi

if [[ -n "$SIGN_IDENTITY" ]]; then
  echo "==> Developer ID 签名：$SIGN_IDENTITY"
  codesign --force --deep --options runtime --timestamp \
    --sign "$SIGN_IDENTITY" "$APP" >/dev/null
else
  echo "==> 临时签名（ad-hoc；未找到 Developer ID Application 证书）"
  codesign --force --sign - --identifier "com.k3huazhi.DeepSeekBalanceFloat" "$APP" >/dev/null
fi
codesign --verify --deep --strict --verbose=2 "$APP" 2>&1 | tail -1

echo "==> 打包 zip"
rm -f "dist/DeepSeek-Balance-Float-macos-v${VERSION}-universal.zip"
ditto -c -k --keepParent "$APP" "dist/DeepSeek-Balance-Float-macos-v${VERSION}-universal.zip"

echo "==> 制作安装 DMG"
DMG="dist/DeepSeek-Balance-Float-macos-v${VERSION}-universal.dmg"
PYTHON="${PYTHON:-}"
if [[ -z "$PYTHON" && -x .venv-build/bin/python ]]; then
  PYTHON=".venv-build/bin/python"
elif [[ -z "$PYTHON" && -x /usr/bin/python3 ]]; then
  PYTHON=/usr/bin/python3
elif [[ -z "$PYTHON" ]] && command -v python3 >/dev/null 2>&1; then
  PYTHON="$(command -v python3)"
fi

DMG_OK=0
if [[ -n "$PYTHON" ]] && "$PYTHON" -c "import dmgbuild" >/dev/null 2>&1; then
  if "$PYTHON" "scripts/make_dmg.py" "$APP" "$DMG" "$VERSION"; then
    DMG_OK=1
    echo "    已生成带背景的拖拽安装 DMG"
  fi
fi

if [[ "$DMG_OK" != "1" ]]; then
  echo "    未找到 dmgbuild，回退为普通 DMG。"
  echo "    需要完整安装界面时先执行："
  echo "      python3 -m venv .venv-build"
  echo "      .venv-build/bin/python -m pip install -r scripts/requirements-build.txt"
  echo "      PYTHON=.venv-build/bin/python ./scripts/build.sh"
  STAGE="$(mktemp -d)"
  cp -R "$APP" "$STAGE/"
  ln -s /Applications "$STAGE/Applications"
  rm -f "$DMG"
  hdiutil create -quiet -volname "DeepSeek 余额" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
  rm -rf "$STAGE"
fi

echo
echo "完成：$APP"
echo "      dist/DeepSeek-Balance-Float-macos-v${VERSION}-universal.zip"
echo "      dist/DeepSeek-Balance-Float-macos-v${VERSION}-universal.dmg"
lipo -info "$APP/Contents/MacOS/DeepSeekBalanceFloat"

if [[ -n "$SIGN_IDENTITY" ]] && \
   [[ -n "${NOTARY_PROFILE:-}${APPLE_API_KEY:-}${APPLE_ID:-}${APPLE_APP_PASSWORD:-}" ]]; then
  echo
  echo "==> 检测到 Developer ID 与公证凭据，继续公证"
  VERSION="$VERSION" ./scripts/sign_and_notarize.sh
else
  echo
  echo "提示：当前为 ad-hoc 签名。若要消除 Gatekeeper 提示，请安装 Developer ID 证书并配置公证凭据，"
  echo "      然后运行 NOTARY_PROFILE=<配置名> ./scripts/sign_and_notarize.sh"
fi
