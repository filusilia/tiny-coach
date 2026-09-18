# 神盾教练 - 本地 AI 推理运行库按需扩展包安装/下载助手
param (
    [string]$TargetDir = ""
)

$ErrorActionPreference = 'Stop'

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   神盾教练 (AegisCoach) - 本地 AI 运行库按需配置向导" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$scriptRoot = $PSScriptRoot
$repoRoot = (Resolve-Path "$scriptRoot\..").Path

# 自动定位发布运行目录
$installLib = if (-not [string]::IsNullOrWhiteSpace($TargetDir) -and (Test-Path $TargetDir)) {
    $TargetDir
} else {
    Join-Path $repoRoot "release\publish"
}

Write-Host "==> 目标安装目录: $installLib" -ForegroundColor Yellow

$zipPath = Join-Path "$repoRoot\release" "AegisCoach-AI-Runtime-扩展包.zip"

if (Test-Path $zipPath) {
    Write-Host "==> 发现本地已存在的 AI 运行库扩展包: $zipPath" -ForegroundColor Green
    Write-Host "==> 正在解压并部署到 $installLib ..." -ForegroundColor Cyan
    if (-not (Test-Path $installLib)) {
        New-Item -ItemType Directory -Path $installLib -Force | Out-Null
    }
    & tar.exe -xf $zipPath -C $installLib
    Write-Host "==> 本地 AI 推理运行库安装成功！重启神盾教练即可激活本地大模型。" -ForegroundColor Green
} else {
    Write-Host "==> 未在 release/ 目录下发现现成的 AI 扩展包。" -ForegroundColor Yellow
    Write-Host "    提示：如需从源码生成扩展包，请运行 tools\package-release.ps1。" -ForegroundColor Gray
}
