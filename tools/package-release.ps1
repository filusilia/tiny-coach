# 神盾教练 (AegisCoach) - Wails v3 原生免安装客户端一键打包
param (
    [switch]$NoBuild = $false
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$releaseDir = Join-Path $repoRoot 'release'

Write-Host '==========================================================' -ForegroundColor Cyan
Write-Host '   神盾教练 (AegisCoach) - 客户端免安装便携版打包 (Wails v3)' -ForegroundColor Cyan
Write-Host '==========================================================' -ForegroundColor Cyan

# 终止运行中的旧进程
Get-Process -Name "神盾教练" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 150

# 准备 release 目录
if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
}

$outputExe = Join-Path $releaseDir '神盾教练.exe'

if (-not $NoBuild) {
    Write-Host '==> [1/3] 正在构建前端电竞 HUD 静态资产 (Vite + Vue 3)...' -ForegroundColor Cyan
    Push-Location (Join-Path $repoRoot 'frontend')
    try {
        & pnpm build
        if ($LASTEXITCODE -ne 0) { throw "前端构建失败" }
    } finally {
        Pop-Location
    }

    Write-Host '==> [2/3] 正在生成 Windows 专属图标与高 DPI 资产并编译...' -ForegroundColor Cyan
    Push-Location $repoRoot

    # 智能定位 go.exe 可执行程序 (防止 -NoProfile 环境缺失 PATH)
    $goExe = $null
    $goCandidates = @(
        "C:\Program Files\Go\bin\go.exe",
        "C:\Go\bin\go.exe",
        "$env:GOROOT\bin\go.exe",
        "$env:LOCALAPPDATA\Programs\Go\bin\go.exe",
        "D:\develop\go\bin\go.exe"
    )
    foreach ($c in $goCandidates) {
        if ($c -and (Test-Path $c)) { $goExe = $c; break }
    }
    if (-not $goExe) {
        $cmd = Get-Command go -ErrorAction SilentlyContinue
        if ($cmd) { $goExe = $cmd.Source }
    }
    if (-not $goExe) {
        throw "未能在系统中找到 go.exe，请确认已安装 Go"
    }

    # 确保 go 所在目录加入当前 session 的 PATH，避免子命令依赖缺失
    $goBinDir = Split-Path $goExe -Parent
    if ($env:PATH -notlike "*$goBinDir*") {
        $env:PATH = "$goBinDir;$env:PATH"
    }

    $wails3Candidates = @(
        "$env:USERPROFILE\go\bin\wails3.exe",
        "D:\develop\go\gopath\bin\wails3.exe"
    )
    $wails3Exe = $null
    foreach ($c in $wails3Candidates) {
        if (Test-Path $c) { $wails3Exe = $c; break }
    }
    if (-not $wails3Exe) {
        $cmd = Get-Command wails3 -ErrorAction SilentlyContinue
        if ($cmd) { $wails3Exe = $cmd.Source }
    }

    $sysoFile = Join-Path $repoRoot 'rsrc_windows_amd64.syso'
    $iconFile = Join-Path $repoRoot 'build\windows\icon.ico'
    $manifestFile = Join-Path $repoRoot 'build\windows\wails.exe.manifest'
    $infoFile = Join-Path $repoRoot 'build\windows\info.json'

    if ($wails3Exe -and (Test-Path $iconFile) -and (Test-Path $manifestFile) -and (Test-Path $infoFile)) {
        & $wails3Exe generate syso -arch amd64 -icon $iconFile -manifest $manifestFile -info $infoFile -out $sysoFile
    }

    try {
        & $goExe build -tags "desktop,production" -ldflags="-H windowsgui -s -w" -o $outputExe
        if ($LASTEXITCODE -ne 0) { throw "Go 客户端编译失败" }
    } finally {
        if (Test-Path $sysoFile) { Remove-Item $sysoFile -Force -ErrorAction SilentlyContinue }
        Pop-Location
    }

    $exeSizeMb = [math]::Round(((Get-Item $outputExe).Length / 1MB), 2)
    Write-Host "    单文件客户端编译成功！体积: $exeSizeMb MB" -ForegroundColor Green
}

Write-Host '==> [3/3] 正在汇总便携套件与配置文件...' -ForegroundColor Cyan
$assetsDir = Join-Path $repoRoot 'tools\assets'
if (Test-Path $assetsDir) {
    Get-ChildItem -Path $assetsDir -File | Copy-Item -Destination $releaseDir -Force
}

Write-Host "==> 发布打包圆满完成！输出位置: $outputExe" -ForegroundColor Green
