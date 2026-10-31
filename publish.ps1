# Push this project to GitHub
# Usage: right-click -> "Run with PowerShell", or:
#   powershell -ExecutionPolicy Bypass -File .\publish.ps1

param(
    [string]$User = "K3huazhi",
    [string]$Repo = "deepseek-balance-float",
    [string]$Token = ""
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$git = "C:\Program Files\Git\cmd\git.exe"
if (-not (Test-Path $git)) {
    $cmd = Get-Command git -ErrorAction SilentlyContinue
    if ($cmd) { $git = $cmd.Source } else {
        Write-Host "git not found. Install it first:  winget install Git.Git" -ForegroundColor Red
        exit 1
    }
}

Set-Location $PSScriptRoot

$url = "https://github.com/$User/$Repo.git"
if ($Token) {
    $pushUrl = "https://$User`:$Token@github.com/$User/$Repo.git"
} else {
    $pushUrl = $url
}

Write-Host ""
Write-Host "Remote: $url" -ForegroundColor Cyan
Write-Host ""

$existing = & $git remote 2>$null
if ($existing -contains "origin") {
    & $git remote set-url origin $pushUrl
} else {
    & $git remote add origin $pushUrl
}

Write-Host "Pushing to main ..." -ForegroundColor Cyan
& $git push -u origin main

if ($LASTEXITCODE -eq 0) {
    # Hide the token again in the stored remote URL
    if ($Token) { & $git remote set-url origin $url }
    Write-Host ""
    Write-Host "OK  https://github.com/$User/$Repo" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Push failed." -ForegroundColor Red
    Write-Host " - Make sure the repo exists: https://github.com/new (public, no README)"
    Write-Host " - If login popup fails, create a token at https://github.com/settings/tokens (scope: repo)"
    Write-Host "   then run:  .\publish.ps1 -Token YOUR_TOKEN"
}
