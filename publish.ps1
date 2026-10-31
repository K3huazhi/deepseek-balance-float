# 一键发布到 GitHub
#
# 用法：右键 → 使用 PowerShell 运行，或在终端执行：
#   powershell -ExecutionPolicy Bypass -File .\publish.ps1
#
# 前提：
#   1. 已安装 Git（winget install Git.Git）
#   2. 已在 https://github.com/new 创建好空仓库（不要勾选 README / .gitignore / License）

param(
    [string]$User = "",
    [string]$Repo = "deepseek-balance-float",
    [string]$Visibility = "public"
)

$ErrorActionPreference = "Stop"
$git = "C:\Program Files\Git\cmd\git.exe"
if (-not (Test-Path $git)) {
    $git = (Get-Command git -ErrorAction SilentlyContinue).Source
}
if (-not $git) {
    Write-Host "找不到 git，请先执行： winget install Git.Git" -ForegroundColor Red
    exit 1
}

Set-Location $PSScriptRoot

if (-not $User) {
    $User = Read-Host "请输入你的 GitHub 用户名"
}
if (-not $Repo) {
    $Repo = Read-Host "仓库名（默认 deepseek-balance-float）"
    if (-not $Repo) { $Repo = "deepseek-balance-float" }
}

$remote = "https://github.com/$User/$Repo.git"
Write-Host ""
Write-Host "目标仓库：$remote" -ForegroundColor Cyan
Write-Host "提示：请先在 https://github.com/new 创建同名空仓库（不要初始化 README）" -ForegroundColor Yellow
$ok = Read-Host "确认继续？(y/N)"
if ($ok -ne "y" -and $ok -ne "Y") { exit 0 }

# 设置远程（已存在则更新）
$existing = & $git remote 2>$null
if ($existing -contains "origin") {
    & $git remote set-url origin $remote
} else {
    & $git remote add origin $remote
}

Write-Host ""
Write-Host "开始推送……首次推送会弹出 GitHub 登录窗口，按提示授权即可。" -ForegroundColor Cyan
& $git push -u origin main

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "✅ 发布成功： https://github.com/$User/$Repo" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "❌ 推送失败。常见原因：" -ForegroundColor Red
    Write-Host "   - 远程仓库不存在或名字不对（先在 GitHub 上创建）"
    Write-Host "   - 开启了两步验证但没用 token 登录："
    Write-Host "     到 https://github.com/settings/tokens 生成 classic token（勾选 repo）"
    Write-Host "     然后用 token 作为密码再次推送"
}
