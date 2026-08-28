# AegisCoach 发布打包脚本
param (
    [switch]$NoBuild = $false
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$projectPath = Join-Path $repoRoot 'native\AegisCoach.Native\AegisCoach.Native.csproj'
$publishSrc = Join-Path $repoRoot 'native\AegisCoach.Native\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\publish'
$releaseDir = Join-Path $repoRoot 'release'
$releasePublish = Join-Path $releaseDir 'publish'
$tempZipPath = Join-Path $releaseDir 'AegisCoach-release.zip'
$finalZipPath = Join-Path $releaseDir 'AegisCoach-发布版.zip'

if (-not $NoBuild) {
    Write-Host '==> 正在执行 dotnet publish (Release 优化配置)...' -ForegroundColor Cyan
    & dotnet publish $projectPath --configuration Release --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) {
        Write-Host '==> Release 未还原资产，回退至 Debug 优化发布...' -ForegroundColor Yellow
        & dotnet publish $projectPath --configuration Debug --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PublishSingleFile=false
        if ($LASTEXITCODE -ne 0) {
            Write-Error "dotnet publish 失败，退出码 $LASTEXITCODE"
        }
    }
}

$publishRelease = Join-Path $repoRoot 'native\AegisCoach.Native\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish'
$publishDebug = Join-Path $repoRoot 'native\AegisCoach.Native\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\publish'
$publishSrc = if (Test-Path $publishRelease) { $publishRelease } else { $publishDebug }

if (-not (Test-Path $publishSrc)) {
    Write-Error "发布输出目录不存在: $publishSrc"
}

Write-Host "==> 正在从 $publishSrc 同步发布产物到 release/publish..." -ForegroundColor Cyan
if (-not (Test-Path $releasePublish)) {
    New-Item -ItemType Directory -Path $releasePublish -Force | Out-Null
}

& robocopy $publishSrc $releasePublish /MIR /R:1 /W:1 /NP /NFL /NDL | Out-Null

Write-Host '==> 正在进行客户端瘦身优化（剥离无用转换前端与 PDB 符号）...' -ForegroundColor Cyan
# 移除无用的模型格式转换前端（运行时只加载已转换好的 IR 模型，不需要 TF/PyTorch 前端）
$unusedLibs = @(
    'openvino_tensorflow_frontend.dll',
    'openvino_pytorch_frontend.dll',
    'openvino_paddle_frontend.dll'
)
foreach ($lib in $unusedLibs) {
    $targetLib = Join-Path $releasePublish $lib
    if (Test-Path $targetLib) {
        Remove-Item $targetLib -Force
        Write-Host "    已移除无用转换库: $lib" -ForegroundColor DarkGray
    }
}

# 移除调试 PDB 符号以减小分发体积
Get-ChildItem $releasePublish -Filter '*.pdb' | Remove-Item -Force

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
$mainExe = Join-Path $releasePublish '神盾教练.exe'
if (Test-Path $mainExe) {
    Write-Host "==> 主程序可执行文件: $mainExe" -ForegroundColor Green
}
Write-Host "==> 发布打包完成: $($zipItem.FullName) ($sizeMb MB)" -ForegroundColor Green
