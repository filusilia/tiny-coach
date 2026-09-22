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
Get-Process -Name "dota-ai-coach" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 200

Write-Host '==> [0/4] 正在深度清理历史缓存与构建产物 (frontend/dist, build/bin, release)...' -ForegroundColor Cyan
$cleanDirs = @(
    (Join-Path $repoRoot 'frontend\dist'),
    (Join-Path $repoRoot 'build\bin'),
    $releaseDir
)
foreach ($d in $cleanDirs) {
    if (Test-Path $d) {
        Remove-Item -Path $d -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "    已彻底清理缓存: $d" -ForegroundColor Gray
    }
}

# 清理可能残留的临时编译产物
$cleanFiles = @(
    (Join-Path $repoRoot 'rsrc_windows_amd64.syso'),
    (Join-Path $repoRoot 'aegis-coach.exe'),
    (Join-Path $repoRoot 'demo.exe')
)
foreach ($f in $cleanFiles) {
    if (Test-Path $f) {
        Remove-Item -Path $f -Force -ErrorAction SilentlyContinue
    }
}

# 初始化全新干净的 release 输出目录
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

$outputExe = Join-Path $releaseDir '神盾教练.exe'

if (-not $NoBuild) {
    $wails3Cmd = Get-Command wails3 -ErrorAction SilentlyContinue
    if ($wails3Cmd) {
        Write-Host '==> [1/4] 正在自动同步生成 Wails v3 类型安全前端绑定 (bindings)...' -ForegroundColor Cyan
        & $wails3Cmd.Source generate bindings -names -ts -b
    }

    Write-Host '==> [2/4] 正在构建前端电竞 HUD 静态资产 (Vite + Vue 3)...' -ForegroundColor Cyan
    Push-Location (Join-Path $repoRoot 'frontend')
    try {
        & pnpm build
        if ($LASTEXITCODE -ne 0) { throw "前端构建失败" }
    } finally {
        Pop-Location
    }

    Write-Host '==> [3/4] 正在生成 Windows 专属图标并编译单文件可执行程序...' -ForegroundColor Cyan
    Push-Location $repoRoot

    # 验证 Go 环境
    if (-not (Get-Command go -ErrorAction SilentlyContinue)) {
        throw "未能在系统中找到 go 命令，请确认 Go 已配置在系统 PATH 环境变量中"
    }

    $wails3Cmd = Get-Command wails3 -ErrorAction SilentlyContinue
    $wails3Exe = if ($wails3Cmd) { $wails3Cmd.Source } else { $null }

    $sysoFile = Join-Path $repoRoot 'rsrc_windows_amd64.syso'
    $iconFile = Join-Path $repoRoot 'build\windows\icon.ico'
    $manifestFile = Join-Path $repoRoot 'build\windows\wails.exe.manifest'
    $infoFile = Join-Path $repoRoot 'build\windows\info.json'

    if ($wails3Exe -and (Test-Path $iconFile) -and (Test-Path $manifestFile) -and (Test-Path $infoFile)) {
        & $wails3Exe generate syso -arch amd64 -icon $iconFile -manifest $manifestFile -info $infoFile -out $sysoFile
    }

    $buildTimeStr = Get-Date -Format 'yyyy-MM-dd_HH:mm:ss'
    $gitCommitStr = "unknown"
    try {
        $gitOut = (git rev-parse --short HEAD 2>$null)
        if ($gitOut) { $gitCommitStr = $gitOut.Trim() }
    } catch {}

    $ldflags = "-H windowsgui -s -w -X main.Version=2.0.0 -X main.BuildTime=$buildTimeStr -X main.GitCommit=$gitCommitStr"

    try {
        & go build -tags "desktop,production" -ldflags "$ldflags" -o $outputExe
        if ($LASTEXITCODE -ne 0) { throw "Go 客户端编译失败" }
    } finally {
        if (Test-Path $sysoFile) { Remove-Item $sysoFile -Force -ErrorAction SilentlyContinue }
        Pop-Location
    }

    $exeSizeMb = [math]::Round(((Get-Item $outputExe).Length / 1MB), 2)
    Write-Host "    单文件客户端编译成功！体积: $exeSizeMb MB" -ForegroundColor Green

    $binDir = Join-Path $repoRoot 'build\bin'
    if (-not (Test-Path $binDir)) {
        New-Item -ItemType Directory -Path $binDir -Force | Out-Null
    }
    $legacyBinExe = Join-Path $binDir 'dota-ai-coach.exe'
    Copy-Item -Path $outputExe -Destination $legacyBinExe -Force
    Write-Host "    已同步镜像更新兼容入口: $legacyBinExe" -ForegroundColor Green
}

Write-Host '==> [4/4] 正在汇总便携套件与配置文件...' -ForegroundColor Cyan
$assetsDir = Join-Path $repoRoot 'tools\assets'
if (Test-Path $assetsDir) {
    Get-ChildItem -Path $assetsDir -File | Copy-Item -Destination $releaseDir -Force
}

Write-Host "==> 发布打包圆满完成！输出位置: $outputExe" -ForegroundColor Green
