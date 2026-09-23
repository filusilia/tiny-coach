# ============================================================
# 神盾教练 (AegisCoach) - Wails v3 原生免安装客户端一键打包
# ============================================================
param (
    [switch]$NoBuild = $false
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$releaseDir = Join-Path $repoRoot 'release'

Write-Host '==========================================================' -ForegroundColor Cyan
Write-Host '   神盾教练 (AegisCoach) - 客户端免安装便携版打包 (Wails v3)' -ForegroundColor Cyan
Write-Host '==========================================================' -ForegroundColor Cyan

# ------------------------------------------------------------
# 终止运行中的旧进程
# ------------------------------------------------------------
Get-Process -Name "神盾教练" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "dota-ai-coach" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 200

# ------------------------------------------------------------
# [0/4] 深度清理历史缓存与构建产物
# ------------------------------------------------------------
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
    # --------------------------------------------------------
    # [1/4] 同步生成 Wails v3 类型安全前端绑定
    # --------------------------------------------------------
    $wails3Cmd = Get-Command wails3 -ErrorAction SilentlyContinue
    if ($wails3Cmd) {
        Write-Host '==> [1/4] 正在自动同步生成 Wails v3 类型安全前端绑定 (bindings)...' -ForegroundColor Cyan
        # 使用 -clean 清理旧绑定，-ts 生成 TypeScript，-d 指定输出目录
        & $wails3Cmd.Source generate bindings -clean -ts -d ./frontend/bindings
        if ($LASTEXITCODE -ne 0) { throw "绑定生成失败" }
    } else {
        Write-Host '    未检测到 wails3 命令，跳过绑定生成步骤' -ForegroundColor Yellow
    }

    # --------------------------------------------------------
    # [2/4] 构建前端静态资产 (Vite + Vue 3)
    # --------------------------------------------------------
    Write-Host '==> [2/4] 正在构建前端电竞 HUD 静态资产 (Vite + Vue 3)...' -ForegroundColor Cyan
    Push-Location (Join-Path $repoRoot 'frontend')
    try {
        & pnpm build
        if ($LASTEXITCODE -ne 0) { throw "前端构建失败" }
    } finally {
        Pop-Location
    }

    # --------------------------------------------------------
    # [3/4] 编译单文件可执行程序
    # --------------------------------------------------------
    Write-Host '==> [3/4] 正在编译单文件可执行程序...' -ForegroundColor Cyan
    Push-Location $repoRoot
    try {
        # 验证 Go 环境
        if (-not (Get-Command go -ErrorAction SilentlyContinue)) {
            throw "未能在系统中找到 go 命令，请确认 Go 已配置在系统 PATH 环境变量中"
        }

        # 生成 .syso 资源文件（图标 + manifest + info）
        $wails3Exe = if ($wails3Cmd) { $wails3Cmd.Source } else { $null }
        $sysoFile = Join-Path $repoRoot 'rsrc_windows_amd64.syso'
        $iconFile = Join-Path $repoRoot 'build\windows\icon.ico'
        $manifestFile = Join-Path $repoRoot 'build\windows\wails.exe.manifest'
        $infoFile = Join-Path $repoRoot 'build\windows\info.json'

        if ($wails3Exe -and (Test-Path $iconFile) -and (Test-Path $manifestFile) -and (Test-Path $infoFile)) {
            & $wails3Exe generate syso -arch amd64 -icon $iconFile -manifest $manifestFile -info $infoFile -out $sysoFile
            if (-not (Test-Path $sysoFile)) {
                throw "syso 文件生成失败"
            }
        } else {
            Write-Host '    未找到图标或 manifest 文件，跳过 syso 生成' -ForegroundColor Yellow
        }

        # 构建 ldflags
        $buildTimeStr = Get-Date -Format 'yyyy-MM-dd_HH:mm:ss'
        $gitCommitStr = "unknown"
        try {
            $gitOut = (git rev-parse --short HEAD 2>$null)
            if ($gitOut) { $gitCommitStr = $gitOut.Trim() }
        } catch {}

        $ldflags = "-H windowsgui -s -w -X main.Version=2.0.0 -X main.BuildTime=$buildTimeStr -X main.GitCommit=$gitCommitStr"

        try {
            # 推荐使用 wails3 build（自动处理前端构建、资源嵌入、图标生成）
            # 如果使用 wails3 build，可移除前端构建步骤和 syso 生成步骤
            & go build -tags "desktop,production" -ldflags "$ldflags" -o $outputExe
            if ($LASTEXITCODE -ne 0) { throw "Go 客户端编译失败" }
        } finally {
            # 清理 syso 临时文件
            if (Test-Path $sysoFile) { Remove-Item $sysoFile -Force -ErrorAction SilentlyContinue }
        }

        $exeSizeMb = [math]::Round(((Get-Item $outputExe).Length / 1MB), 2)
        Write-Host "    单文件客户端编译成功！体积: $exeSizeMb MB" -ForegroundColor Green
    } finally {
        Pop-Location
    }
}

# ------------------------------------------------------------
# [4/4] 汇总便携套件与配置文件
# ------------------------------------------------------------
Write-Host '==> [4/4] 正在汇总便携套件与配置文件...' -ForegroundColor Cyan
$assetsDir = Join-Path $repoRoot 'tools\assets'
if (Test-Path $assetsDir) {
    Get-ChildItem -Path $assetsDir -File | Copy-Item -Destination $releaseDir -Force
}

Write-Host "==> 发布打包圆满完成！输出位置: $outputExe" -ForegroundColor Green
