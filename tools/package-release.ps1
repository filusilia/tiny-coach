# AegisCoach 发布打包脚本 (免安装独立单文件客户端 exe，零注册表写入)
param (
    [switch]$NoBuild = $false,
    [switch]$Zip = $false,
    [switch]$IncludeAi = $false
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$releaseDir = Join-Path $repoRoot 'release'
$releasePublish = Join-Path $releaseDir 'publish'
$finalZipPath = Join-Path $releaseDir 'AegisCoach-免安装便携版.zip'

Write-Host '==========================================================' -ForegroundColor Cyan
Write-Host '   神盾教练 (AegisCoach) - 客户端免安装便携版一键打包' -ForegroundColor Cyan
Write-Host '==========================================================' -ForegroundColor Cyan

Write-Host '==> [1/4] 正在规范化全仓换行符 (Windows CRLF)...' -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'tools\Normalize-LineEndings.ps1')

# 确保输出目录就绪
if (Test-Path $releasePublish) {
    Remove-Item $releasePublish -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $releasePublish -Force | Out-Null

$clientExe = $null

if (-not $NoBuild) {
    Write-Host '==> [2/4] 正在构建前端电竞 HUD 静态资产 (Vite + Vue 3 + TypeScript)...' -ForegroundColor Cyan
    Set-Location (Join-Path $repoRoot 'frontend')
    & pnpm build
    if ($LASTEXITCODE -ne 0) {
        Set-Location $repoRoot
        Write-Error "前端构建失败，退出码: $LASTEXITCODE"
    }
    Set-Location $repoRoot

    Write-Host '==> [3/4] 正在编译免安装独立客户端 exe (Wails + Go 零 C++ 依赖)...' -ForegroundColor Cyan

    $goCandidates = @(
        "D:\develop\go\go1.27.1\bin\go.exe",
        "D:\develop\go\go1.26.2\bin\go.exe",
        "$env:ProgramFiles\Go\bin\go.exe"
    )
    $goExe = $null
    foreach ($candidate in $goCandidates) {
        if (Test-Path $candidate) {
            $goExe = $candidate
            break
        }
    }
    if (-not $goExe) {
        $goCmd = Get-Command go -ErrorAction SilentlyContinue
        if ($goCmd) { $goExe = $goCmd.Source }
    }

    $wailsCandidates = @(
        "$env:USERPROFILE\go\bin\wails.exe",
        "$env:GOPATH\bin\wails.exe"
    )
    $wailsExe = $null
    foreach ($candidate in $wailsCandidates) {
        if (Test-Path $candidate) {
            $wailsExe = $candidate
            break
        }
    }
    if (-not $wailsExe) {
        $wailsCmd = Get-Command wails -ErrorAction SilentlyContinue
        if ($wailsCmd) { $wailsExe = $wailsCmd.Source }
    }

    if ($goExe) {
        Write-Host "    发现 Go 极速便携构建器: $goExe" -ForegroundColor Green
        Push-Location $repoRoot
        $env:GOPROXY = "https://goproxy.cn,direct"
        $goDir = [System.IO.Path]::GetDirectoryName($goExe)
        $env:PATH = "$goDir;$env:USERPROFILE\go\bin;$env:PATH"
        & $goExe mod tidy

        $outputExe = Join-Path $releasePublish '神盾教练.exe'
        Write-Host "    正在生成单文件可执行程序: $outputExe ..." -ForegroundColor Cyan

        if ($wailsExe) {
            Write-Host "    使用 Wails 官方 CLI 进行完整桌面打包: $wailsExe" -ForegroundColor Green
            & $wailsExe build -clean -s -compiler "$goExe" -o "神盾教练.exe"
            $wailsOut = Join-Path $repoRoot 'build\bin\神盾教练.exe'
            if (Test-Path $wailsOut) {
                Copy-Item $wailsOut $outputExe -Force
                Remove-Item (Join-Path $repoRoot 'build\bin') -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        if (-not (Test-Path $outputExe)) {
            Write-Host "    回退使用 Go 原生带生产标签编译 (-tags desktop,production)..." -ForegroundColor Yellow
            & $goExe build -tags "desktop,production" -ldflags="-H windowsgui -s -w" -o $outputExe
        }
        Pop-Location

        if (Test-Path $outputExe) {
            $clientExe = $outputExe
            $exeSizeMb = [math]::Round(((Get-Item $outputExe).Length / 1MB), 2)
            Write-Host "    单文件客户端编译成功！体积: $exeSizeMb MB" -ForegroundColor Green
        }
    }
} else {
    $existingExe = Join-Path $releasePublish '神盾教练.exe'
    if (Test-Path $existingExe) {
        $clientExe = $existingExe
    }
}

Write-Host '==> [4/4] 正在汇总整理便携运行套件...' -ForegroundColor Cyan

# 复制便携说明与 GSI 配置文件
Copy-Item (Join-Path $repoRoot 'tools\assets\使用说明.txt') $releasePublish -Force
Copy-Item (Join-Path $repoRoot 'tools\assets\gamestate_integration_aegis.cfg') $releasePublish -Force


if ($Zip) {
    Write-Host "==> 正在压缩生成免安装便携分发包: $finalZipPath ..." -ForegroundColor Cyan
    if (Test-Path $finalZipPath) {
        Remove-Item $finalZipPath -Force
    }
    Compress-Archive -Path "$releasePublish\*" -DestinationPath $finalZipPath -Force
    $zipSizeMb = [math]::Round(((Get-Item $finalZipPath).Length / 1MB), 2)
    Write-Host "==> 打包完成！便携 ZIP 压缩包大小: $zipSizeMb MB" -ForegroundColor Green
}

Write-Host "==> 发布打包圆满完成！输出路径: $releasePublish" -ForegroundColor Green
if ($clientExe) {
    Write-Host "    主程序位置: $clientExe" -ForegroundColor Green
}
