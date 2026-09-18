param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CargoArgs
)

$ErrorActionPreference = 'Stop'

$msvcLib = "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231\lib\onecore\x64"
$sdkUcrt = "C:\Program Files (x86)\Windows Kits\10\Lib\10.0.26100.0\ucrt\x64"
$sdkUm = "C:\Program Files (x86)\Windows Kits\10\Lib\10.0.26100.0\um\x64"

$currentLib = [System.Environment]::GetEnvironmentVariable('LIB', 'Process')
$newLib = "$msvcLib;$sdkUcrt;$sdkUm;$currentLib".Trim(';')
[System.Environment]::SetEnvironmentVariable('LIB', $newLib, 'Process')

$env:PATH = "$env:USERPROFILE\.cargo\bin;$env:PATH"

if ($CargoArgs.Count -gt 0) {
    & cargo @CargoArgs
} else {
    & cargo --version
}
