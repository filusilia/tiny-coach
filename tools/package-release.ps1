# AegisCoach 发布打包脚本 (便携版分层结构：最外层 exe + 内部 lib 运行库)
param (
    [switch]$NoBuild = $false,
    [switch]$Zip = $false
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$nativeProject = Join-Path $repoRoot 'native\AegisCoach.Native\AegisCoach.Native.csproj'
$launcherProject = Join-Path $repoRoot 'tools\AegisCoach.Launcher\AegisCoach.Launcher.csproj'
$releaseDir = Join-Path $repoRoot 'release'
$releasePublish = Join-Path $releaseDir 'publish'
$releaseLib = Join-Path $releasePublish 'lib'
$tempZipPath = Join-Path $releaseDir 'AegisCoach-release.zip'
$finalZipPath = Join-Path $releaseDir 'AegisCoach-发布版.zip'

if (-not $NoBuild) {
    Write-Host '==> 正在编译主程序 AegisCoach.Native (Release 优化配置)...' -ForegroundColor Cyan
    & dotnet publish $nativeProject --configuration Release --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) {
        Write-Host '==> Release 未还原资产，回退至 Debug 优化发布...' -ForegroundColor Yellow
        & dotnet publish $nativeProject --configuration Debug --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PublishSingleFile=false
        if ($LASTEXITCODE -ne 0) {
            Write-Error "主程序 dotnet publish 失败，退出码 $LASTEXITCODE"
        }
    }

    Write-Host '==> 正在编译便携单文件启动器 AegisCoach.Launcher...' -ForegroundColor Cyan
    & dotnet publish $launcherProject --configuration Release -r win-x64 --self-contained false -p:PublishSingleFile=true
    if ($LASTEXITCODE -ne 0) {
        Write-Error "启动器 dotnet publish 失败，退出码 $LASTEXITCODE"
    }
}

$publishRelease = Join-Path $repoRoot 'native\AegisCoach.Native\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish'
$publishDebug = Join-Path $repoRoot 'native\AegisCoach.Native\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\publish'
$publishSrc = if (Test-Path $publishRelease) { $publishRelease } elseif (Test-Path $publishDebug) { $publishDebug } else { (Get-ChildItem (Join-Path $repoRoot 'native\AegisCoach.Native\bin\x64\*\*\win-x64\publish') -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName) }
$launcherSrc = Join-Path $repoRoot 'tools\AegisCoach.Launcher\bin\Release\net10.0\win-x64\publish\神盾教练.exe'

if (-not (Test-Path $publishSrc)) {
    Write-Error "主程序发布输出目录不存在: $publishSrc"
}
if (-not (Test-Path $launcherSrc)) {
    Write-Error "启动器发布输出文件不存在: $launcherSrc"
}

# 清理旧发布目录中散落的根目录 DLL/文件，重新构建便携目录树
if (Test-Path $releasePublish) {
    Remove-Item $releasePublish -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $releaseLib -Force | Out-Null

Write-Host "==> 正在同步主程序运行库到 release/publish/lib..." -ForegroundColor Cyan
& robocopy $publishSrc $releaseLib /MIR /R:1 /W:1 /NP /NFL /NDL | Out-Null

Write-Host '==> 正在进行客户端瘦身优化（剥离无用转换前端与 PDB 符号）...' -ForegroundColor Cyan
$unusedLibs = @(
    'openvino_tensorflow_frontend.dll',
    'openvino_pytorch_frontend.dll',
    'openvino_paddle_frontend.dll'
)
foreach ($lib in $unusedLibs) {
    $targetLib = Join-Path $releaseLib $lib
    if (Test-Path $targetLib) {
        Remove-Item $targetLib -Force
        Write-Host "    已移除无用转换库: $lib" -ForegroundColor DarkGray
    }
}
Get-ChildItem $releaseLib -Filter '*.pdb' | Remove-Item -Force

# 将 lib 内部的核心程序标记为隐藏，让普通用户打开 lib 目录时只看到纯粹的库与资源，避免双重 exe 混淆
$internalExe = Join-Path $releaseLib '神盾教练.exe'
if (Test-Path $internalExe) {
    Set-ItemProperty -Path $internalExe -Name Attributes -Value ([System.IO.FileAttributes]::Hidden)
    Write-Host "    已将 lib 内部核心程序标记为隐藏" -ForegroundColor DarkGray
}

Write-Host "==> 正在安装便携版外层启动入口..." -ForegroundColor Cyan
$rootExe = Join-Path $releasePublish '神盾教练.exe'
Copy-Item -Path $launcherSrc -Destination $rootExe -Force

$readmePath = Join-Path $releasePublish '便携版说明.txt'
@"
神盾教练 - 便携绿色版
======================================
【启动方式】
直接双击最外层的“神盾教练.exe”即可运行。

【文件说明】
- 神盾教练.exe : 便携启动入口（位于最外层，干净清爽）
- lib\        : 核心程序组件、资源与全部依赖运行库（请勿移动或删除）
"@ | Set-Content -Path $readmePath -Encoding UTF8

$exeItem = Get-Item $rootExe
$exeKb = [Math]::Round($exeItem.Length / 1KB, 1)
$libFiles = (Get-ChildItem $releaseLib -Recurse -File).Count
Write-Host "==> 便携版外层入口: $rootExe ($exeKb KB，直接双击运行)" -ForegroundColor Green
Write-Host "==> 内部库组件目录: $releaseLib ($libFiles 个组件全部收归 lib 目录)" -ForegroundColor Green

if ($Zip) {
    Write-Host '==> 正在生成发布压缩包...' -ForegroundColor Cyan
    if (Test-Path $tempZipPath) { Remove-Item $tempZipPath -Force }
    if (Test-Path $finalZipPath) { Remove-Item $finalZipPath -Force }

    & tar.exe -a -cf $tempZipPath -C $releasePublish .
    if ($LASTEXITCODE -ne 0) {
        Write-Error "tar 压缩失败，退出码 $LASTEXITCODE"
    }

    [System.IO.File]::Move($tempZipPath, $finalZipPath)
    $zipItem = Get-Item $finalZipPath
    $sizeMb = [Math]::Round($zipItem.Length / 1MB, 2)
    Write-Host "==> 分发压缩包完成: $($zipItem.FullName) ($sizeMb MB)" -ForegroundColor Green
} else {
    if (Test-Path $tempZipPath) { Remove-Item $tempZipPath -Force }
    if (Test-Path $finalZipPath) { Remove-Item $finalZipPath -Force }
    Write-Host "==> 发布打包完成：已生成独立免安装可执行程序（符合便携版设置，默认不生成 zip）" -ForegroundColor Green
}
