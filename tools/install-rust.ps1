$ErrorActionPreference = 'Stop'
$installer = Join-Path $env:TEMP 'rustup-init.exe'
Write-Host "正在下载 Rust 工具链安装器..."
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13
Invoke-WebRequest -Uri "https://static.rust-lang.org/rustup/dist/x86_64-pc-windows-msvc/rustup-init.exe" -OutFile $installer
Write-Host "正在执行静默安装 (rustup-init -y)..."
& $installer -y --default-toolchain stable
Write-Host "Rust 工具链安装完成！"
