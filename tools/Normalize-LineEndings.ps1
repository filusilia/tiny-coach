<#
.SYNOPSIS
    规范化神盾教练全仓文本文件的换行符为 Windows CRLF (\r\n)，彻底防止 Visual Studio 弹出“行尾不一致”提示。

.DESCRIPTION
    遍历仓库所有文本文件（排除 .git, .vs, bin, obj, .build, release 及二进制资源），
    检测是否存在未配对的单独 LF (\n) 或单独 CR (\r)。
    支持 -Check 模式（只检查不修改，发现异常退出码为 1），
    以及默认修复模式（自动将所有换行转换为统一的 CRLF，并保留原有 UTF-8 / UTF-8 BOM 编码）。
#>
[CmdletBinding()]
param (
    [switch]$Check = $false,
    [string]$Path = ""
)

$ErrorActionPreference = 'Stop'

$repoRoot = if ([string]::IsNullOrWhiteSpace($Path)) {
    (Resolve-Path "$PSScriptRoot\..").Path
} else {
    (Resolve-Path $Path).Path
}

$binaryExts = @(
    '.png', '.jpg', '.jpeg', '.ico', '.gif', '.bmp', '.webp',
    '.exe', '.dll', '.zip', '.tar', '.gz', '.7z',
    '.onnx', '.bin', '.pri', '.xbf', '.pdb', '.pyc', '.woff', '.woff2', '.ttf'
)

$allFiles = Get-ChildItem -Path $repoRoot -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/]\.git([\\/]|$)' -and
    $_.FullName -notmatch '[\\/]node_modules([\\/]|$)' -and
    $_.FullName -notmatch '[\\/]dist([\\/]|$)' -and
    $_.FullName -notmatch '[\\/]target([\\/]|$)' -and
    $_.FullName -notmatch '[\\/]\.vs([\\/]|$)' -and
    $_.FullName -notmatch '[\\/]bin([\\/]|$)' -and
    $_.FullName -notmatch '[\\/]obj([\\/]|$)' -and
    $_.FullName -notmatch '[\\/]\.build([\\/]|$)' -and
    $_.FullName -notmatch '[\\/]release([\\/]|$)' -and
    $_.Extension.ToLowerInvariant() -notin $binaryExts
}

$issues = @()
$fixedCount = 0

foreach ($file in $allFiles) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    if ($bytes.Length -eq 0) { continue }

    # 检测二进制（前 8000 字节存在空字节）
    $checkLen = [Math]::Min($bytes.Length, 8000)
    $isBinary = $false
    for ($i = 0; $i -lt $checkLen; $i++) {
        if ($bytes[$i] -eq 0) {
            $isBinary = $true
            break
        }
    }
    if ($isBinary) { continue }

    # 扫描不合规的换行符
    $hasStandaloneLf = $false
    $hasStandaloneCr = $false
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 10) { # LF
            if ($i -eq 0 -or $bytes[$i - 1] -ne 13) {
                $hasStandaloneLf = $true
                break
            }
        } elseif ($bytes[$i] -eq 13) { # CR
            if ($i -eq $bytes.Length - 1 -or $bytes[$i + 1] -ne 10) {
                $hasStandaloneCr = $true
                break
            }
        }
    }

    $relPath = $file.FullName.Substring($repoRoot.Length).TrimStart('\', '/')

    if ($hasStandaloneLf -or $hasStandaloneCr) {
        $issues += $relPath
        if (-not $Check) {
            $text = [System.IO.File]::ReadAllText($file.FullName)
            $normalized = $text -replace "`r`n", "`n" -replace "`r", "`n" -replace "`n", "`r`n"

            $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
            $isPs1 = $file.Extension.Equals('.ps1', [System.StringComparison]::OrdinalIgnoreCase)
            $encoding = if ($hasBom -or $isPs1) { [System.Text.Encoding]::UTF8 } else { New-Object System.Text.UTF8Encoding($false) }

            [System.IO.File]::WriteAllText($file.FullName, $normalized, $encoding)
            $fixedCount++
        }
    }
}

if ($Check) {
    if ($issues.Count -gt 0) {
        Write-Host " [FAIL] 发现 $($issues.Count) 个文件存在非规范换行符 (LF/CR/混合):" -ForegroundColor Red
        foreach ($item in $issues) {
            Write-Host "   - $item" -ForegroundColor Red
        }
        exit 1
    } else {
        Write-Host " [PASS] 全仓文本文件换行符均为规范 CRLF (`r`n)，共检查 $($allFiles.Count) 个文件。" -ForegroundColor Green
        exit 0
    }
} else {
    if ($fixedCount -gt 0) {
        Write-Host " [FIXED] 已将 $fixedCount 个文件的换行符自动规范化为 CRLF (`r`n):" -ForegroundColor Yellow
        foreach ($item in $issues) {
            Write-Host "   - $item" -ForegroundColor Yellow
        }
    } else {
        Write-Host " [OK] 全仓换行符检查通过，共 $($allFiles.Count) 个文本文件，无须修复。" -ForegroundColor Green
    }
    exit 0
}
