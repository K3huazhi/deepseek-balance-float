#!/bin/bash
# 使用 Apple Developer ID 对 App 正式签名，并完成公证（notarization）与装订（staple）。
#
# 前置条件（需要你自己的 Apple Developer Program 账号）：
#   1. 钥匙串中已安装 "Developer ID Application: ..." 证书及私钥；
#   2. 已创建 notarytool 凭据配置，例如：
#        xcrun notarytool store-credentials "DSB-notary" \
#          --apple-id "you@example.com" --team-id "TEAMID" --password "app-specific-password"
#   3. 运行：
#        NOTARY_PROFILE=DSB-notary ./scripts/sign_and_notarize.sh
#
# 也支持 App Store Connect API Key：
#   APPLE_API_KEY=/path/AuthKey_XXX.p8 APPLE_API_KEY_ID=XXX APPLE_API_ISSUER=xxx-... \
#     ./scripts/sign_and_notarize.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

VERSION="${VERSION:-1.0.0}"
PYTHON="${PYTHON:-}"
if [[ -z "$PYTHON" && -x .venv-build/bin/python ]]; then
  PYTHON=".venv-build/bin/python"
elif [[ -z "$PYTHON" && -x /usr/bin/python3 ]]; then
  PYTHON=/usr/bin/python3
fi
APP="dist/DeepSeek 余额.app"
ZIP="dist/DeepSeek-Balance-Float-macos-v${VERSION}-universal.zip"
DMG="dist/DeepSeek-Balance-Float-macos-v${VERSION}-universal.dmg"

if [[ ! -d "$APP" ]]; then
  echo "找不到 $APP，请先运行 ./scripts/build.sh" >&2
  exit 1
fi

# ---------------------------------------------------------------- 证书
IDENTITY="${DEVELOPER_ID_APPLICATION:-}"
if [[ -z "$IDENTITY" ]]; then
  IDENTITY="$(security find-identity -v -p codesigning 2>/dev/null \
    | sed -n 's/.*"\(Developer ID Application:.*\)"/\1/p' | head -1)"
fi
if [[ -z "$IDENTITY" ]]; then
  echo "未找到 Developer ID Application 证书。" >&2
  echo "请先在 Apple Developer 后台创建证书并安装到登录钥匙串，然后重试。" >&2
  echo "当前可用签名身份：" >&2
  security find-identity -v -p codesigning >&2 || true
  exit 1
fi
echo "==> 签名身份：$IDENTITY"

# ---------------------------------------------------------- 公证凭据参数
NOTARY_ARGS=()
if [[ -n "${NOTARY_PROFILE:-}" ]]; then
  NOTARY_ARGS=(--keychain-profile "$NOTARY_PROFILE")
elif [[ -n "${APPLE_API_KEY:-}" && -n "${APPLE_API_KEY_ID:-}" && -n "${APPLE_API_ISSUER:-}" ]]; then
  NOTARY_ARGS=(--key "$APPLE_API_KEY" --key-id "$APPLE_API_KEY_ID" --issuer "$APPLE_API_ISSUER")
elif [[ -n "${APPLE_ID:-}" && -n "${APPLE_TEAM_ID:-}" && -n "${APPLE_APP_PASSWORD:-}" ]]; then
  NOTARY_ARGS=(--apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD")
else
  echo "缺少公证凭据。" >&2
  echo "请设置 NOTARY_PROFILE，或 APPLE_API_KEY/APPLE_API_KEY_ID/APPLE_API_ISSUER，" >&2
  echo "或 APPLE_ID/APPLE_TEAM_ID/APPLE_APP_PASSWORD 后重试。" >&2
  exit 1
fi

notarize() {
  local file="$1"
  echo "==> 提交公证：$(basename "$file")（可能需要几分钟）"
  local output
  if ! output="$(xcrun notarytool submit "$file" "${NOTARY_ARGS[@]}" --wait 2>&1)"; then
    echo "$output" >&2
    echo "公证提交失败。" >&2
    exit 1
  fi
  echo "$output"
  if ! grep -q "status: Accepted" <<<"$output"; then
    echo "公证未通过，请查看上面 notarytool 返回的日志。" >&2
    exit 1
  fi
}

# ---------------------------------------------------------- 1. 签名 App
echo "==> Developer ID 签名（Hardened Runtime + 安全时间戳）"
codesign --force --deep --options runtime --timestamp --sign "$IDENTITY" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"

# ------------------------------------------------- 2. 公证 App（借临时 zip）
TMPDIR="$(mktemp -d)"
trap 'rm -rf "$TMPDIR"' EXIT
TMPZIP="$TMPDIR/app.zip"
ditto -c -k --keepParent "$APP" "$TMPZIP"
notarize "$TMPZIP"
xcrun stapler staple "$APP"
xcrun stapler validate "$APP"

# ---------------------------------------------------------- 3. 重新打包
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"

echo "==> 重新生成 DMG"
"$PYTHON" scripts/make_dmg.py "$APP" "$DMG" "$VERSION"
codesign --force --timestamp --sign "$IDENTITY" "$DMG"

# ---------------------------------------------------------- 4. 公证 DMG
notarize "$DMG"
xcrun stapler staple "$DMG"
xcrun stapler validate "$DMG"

echo
echo "==> Gatekeeper 验证"
spctl --assess --type execute -vv "$APP" || true
spctl --assess --type open --context context:primary-signature -vv "$DMG" || true

echo
echo "完成：正式签名并公证的产物："
echo "  $APP"
echo "  $ZIP"
echo "  $DMG"
