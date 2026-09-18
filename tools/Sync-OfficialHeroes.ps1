[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$catalog = Get-Content -Raw (Join-Path $root 'public/assets/data/herolist.json') | ConvertFrom-Json
$heroes = @($catalog.result.data.heroes | Sort-Object id -Unique)
if ($heroes.Count -lt 127) { throw '本地英雄目录不完整，停止覆盖快照。' }
$results = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $heroes) {
    $url = "https://www.dota2.com.cn/datafeed/hero?hero_id=$($entry.id)"
    $reply = $null
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        Start-Sleep -Milliseconds (250 + 1000 * $attempt)
        try { $reply = Invoke-RestMethod -Uri $url -TimeoutSec 25; break }
        catch { if ($attempt -eq 2) { throw } }
    }
    $hero = $reply.result.heroes
    if ($reply.status -ne 'success' -or $hero.id -ne $entry.id -or @($hero.abilities).Count -eq 0) {
        throw "英雄 $($entry.id) 数据校验失败，不覆盖已有快照。"
    }
    $results.Add($hero)
    Write-Host "[$($results.Count)/$($heroes.Count)] $($hero.name_loc)"
}
$target = Join-Path $root 'public/assets/data/heroes-details-official.json'
$payload = [ordered]@{ source = 'https://www.dota2.com.cn/datafeed/hero'; fetched_at = [DateTimeOffset]::UtcNow.ToString('O'); heroes = $results.ToArray() }
$json = ($payload | ConvertTo-Json -Depth 100 -Compress) + "`r`n"
[IO.File]::WriteAllText("$target.tmp", $json, [Text.UTF8Encoding]::new($false))
[IO.File]::Move("$target.tmp", $target, $true)
Write-Host "已保存 $($results.Count) 位英雄的完整官网原始快照。"
