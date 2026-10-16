# ============================================================
# 神盾教练 (AegisCoach) - Wails v3 原生免安装客户端一键打包
# ============================================================
param (
    [switch]$NoBuild = $false
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$releaseDir = Join-Path $repoRoot 'release'

# 核心加固：无论从何处调用该脚本，强制将工作目录与进程目录锚定在仓库根目录
Set-Location $repoRoot
[System.IO.Directory]::SetCurrentDirectory($repoRoot)
[System.Environment]::CurrentDirectory = $repoRoot

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
# 注意：.syso 必须与编译包（cmd/aegis-coach）同目录才会被 Go 链接，
# 根目录残留为入口迁移前的历史遗留，一并清除以免误判生效来源。
$cleanFiles = @(
    (Join-Path $repoRoot 'cmd\aegis-coach\rsrc_windows_amd64.syso'),
    (Join-Path $repoRoot 'rsrc_windows_amd64.syso'),
    (Join-Path $repoRoot 'aegis-coach.exe')
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
        $bindingsDir = Join-Path $repoRoot 'frontend\bindings'
        Push-Location $repoRoot
        try {
            # 使用 -clean 清理旧绑定，-ts 生成 TypeScript，-names 使用方法名，-d 指定绝对路径
            & $wails3Cmd.Source generate bindings -clean -ts -names -d $bindingsDir ./cmd/aegis-coach ./internal/...
            if ($LASTEXITCODE -ne 0) { throw "绑定生成失败" }
            # 建立前端向后兼容别名重定向
            Set-Content -Path (Join-Path $bindingsDir "aegis-coach\app.ts") -Value "export * from './cmd/aegis-coach/app'" -Force
            Set-Content -Path (Join-Path $bindingsDir "aegis-coach\index.ts") -Value "export * as App from './cmd/aegis-coach/index.js';`nexport * from './cmd/aegis-coach/app.js';" -Force
        } finally {
            Pop-Location
        }
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
        # 关键：必须输出到被编译包自身目录 (cmd/aegis-coach)，Go 仅链接同目录下的 .syso；
        # 输出到仓库根会导致图标/manifest/版本信息被静默丢弃。
        $wails3Exe = if ($wails3Cmd) { $wails3Cmd.Source } else { $null }
        $sysoFile = Join-Path $repoRoot 'cmd\aegis-coach\rsrc_windows_amd64.syso'
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
            # 入口已迁移至 cmd/aegis-coach，必须显式指定包路径；
            # 省略包路径会让 go build 指向仓库根（现已无任何 .go 文件）而必然失败。
            # 版本注入目标为 package main 的变量（cmd/aegis-coach/main.go:Version/BuildTime/GitCommit）。
            & go build -tags "desktop,production" -ldflags "$ldflags" -o $outputExe ./cmd/aegis-coach
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
# [4/4] 汇总便携套件与配置文件 (含 lib 动态库与离线语音模型)
# ------------------------------------------------------------
Write-Host '==> [4/4] 正在汇总便携套件与配置文件...' -ForegroundColor Cyan
$assetsDir = Join-Path $repoRoot 'tools\assets'
if (Test-Path $assetsDir) {
    Get-ChildItem -Path $assetsDir -File | Copy-Item -Destination $releaseDir -Force
}

# 复制 ONNX Runtime 动态链接库 (lib/onnxruntime.dll)
$libSrc = Join-Path $repoRoot 'lib'
$libDest = Join-Path $releaseDir 'lib'
if (Test-Path $libSrc) {
    if (-not (Test-Path $libDest)) { New-Item -ItemType Directory -Path $libDest -Force | Out-Null }
    Get-ChildItem -Path $libSrc -Filter "*.dll" | Copy-Item -Destination $libDest -Force
    Write-Host "    已将 ONNX Runtime 动态库同步至 release/lib/" -ForegroundColor Green
}

# 复制本地已就绪的超保真拟人 MeloTTS 语音模型 (开箱即用高保真离线语音)
$meloSrc = Join-Path $repoRoot 'melo_weights'
$meloDest = Join-Path $releaseDir 'melo_weights'
if (Test-Path $meloSrc) {
    if (-not (Test-Path $meloDest)) { New-Item -ItemType Directory -Path $meloDest -Force | Out-Null }
    Get-ChildItem -Path $meloSrc | Copy-Item -Destination $meloDest -Recurse -Force
    Write-Host "    已将 MeloTTS 超保真拟人离线语音模型同步至 release/melo_weights/" -ForegroundColor Green
}

# 复制端侧 YOLOv8 赛前选人识别模型 (models/yolov8n_hero.onnx)
$modelsSrc = Join-Path $repoRoot 'models'
$modelsDest = Join-Path $releaseDir 'models'
if (Test-Path $modelsSrc) {
    if (-not (Test-Path $modelsDest)) { New-Item -ItemType Directory -Path $modelsDest -Force | Out-Null }
    Get-ChildItem -Path $modelsSrc | Copy-Item -Destination $modelsDest -Recurse -Force
    Write-Host "    已将端侧 YOLOv8 视觉识别模型同步至 release/models/" -ForegroundColor Green
}

# 复制端侧 ONNX Runtime WebAssembly 推理引擎 (ort/)
$ortSrc = Join-Path $repoRoot 'ort'
$ortDest = Join-Path $releaseDir 'ort'
if (Test-Path $ortSrc) {
    if (-not (Test-Path $ortDest)) { New-Item -ItemType Directory -Path $ortDest -Force | Out-Null }
    Get-ChildItem -Path $ortSrc | Copy-Item -Destination $ortDest -Recurse -Force
    Write-Host "    已将端侧 ONNX Runtime WebAssembly 运行时同步至 release/ort/" -ForegroundColor Green
}

# 复制高频离线预置音效资源 (audio/)
$audioSrc = Join-Path $repoRoot 'audio'
$audioDest = Join-Path $releaseDir 'audio'
if (Test-Path $audioSrc) {
    if (-not (Test-Path $audioDest)) { New-Item -ItemType Directory -Path $audioDest -Force | Out-Null }
    Get-ChildItem -Path $audioSrc | Copy-Item -Destination $audioDest -Recurse -Force
    Write-Host "    已将高频预置离线语音资源同步至 release/audio/" -ForegroundColor Green
}

# ------------------------------------------------------------
# [5/5] 安全加固：解除 Windows 区域锁定与 Authenticode 代码签名
# ------------------------------------------------------------
Write-Host '==> [5/5] 正在执行 Windows SmartScreen 与 Authenticode 安全加固...' -ForegroundColor Cyan
# 清除构建产物的所有 Web 区域锁定流 (Zone.Identifier)
Get-ChildItem -Path $releaseDir -Recurse -File | ForEach-Object {
    Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
}
Write-Host "    已解除 release/ 目录下所有文件的 Windows 锁定标记" -ForegroundColor Green

# 尝试为 release/神盾教练.exe 执行 Authenticode 代码签名
try {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=AegisCoach Local Dev, O=AegisCoach" -CertStoreLocation Cert:\CurrentUser\My -ErrorAction SilentlyContinue
    }
    if ($cert) {
        Set-AuthenticodeSignature -FilePath $outputExe -Certificate $cert -ErrorAction SilentlyContinue | Out-Null
        Write-Host "    已对可执行文件应用 Authenticode 代码签名保护" -ForegroundColor Green
    }
} catch {
    Write-Host "    代码签名可选步骤跳过: $($_.Exception.Message)" -ForegroundColor Gray
}

# 修复 Windows Mandatory Integrity Control (MIC) 级别，防止从受限父目录继承 Low Mandatory Level 导致 Win32k Lockdown / GUI 初始化失败 (1114 ERROR_DLL_INIT_FAILED)
try {
    & icacls $releaseDir /setintegritylevel "(OI)(CI)Medium" | Out-Null
    & icacls $outputExe /setintegritylevel Medium | Out-Null
    Write-Host "    已将 release/ 输出目录与可执行文件完整性级别设置为 Medium (标准桌面应用)" -ForegroundColor Green
} catch {
    Write-Host "    完整性级别设置跳过: $($_.Exception.Message)" -ForegroundColor Gray
}

Write-Host "==> 发布打包圆满完成！输出位置: $outputExe" -ForegroundColor Green
