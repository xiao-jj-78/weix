[CmdletBinding()]
param([ValidateSet('Visual','Background','Plugins','Stop')][string]$Mode='Visual', [switch]$NoBrowser, [switch]$OpenWeChat)
$ErrorActionPreference='Stop'
$workspaceDir=$PSScriptRoot
$dataDir=Join-Path $workspaceDir 'data'
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
$statePath=Join-Path $dataDir 'launcher-state.json'
$baseUrl='http://127.0.0.1:8765'
function Get-WorkspaceStatus {
    try { Invoke-RestMethod -Uri "$baseUrl/api/status" -TimeoutSec 2 } catch { $null }
}
function Get-OwnedProcess($record) {
    if (-not $record) { return $null }
    $candidate=Get-Process -Id $record.pid -ErrorAction SilentlyContinue
    if ($candidate -and $candidate.Path -eq $record.path -and $candidate.StartTime.ToUniversalTime().Ticks.ToString() -eq [string]$record.startedTicks) { return $candidate }
    return $null
}
if ($Mode -eq 'Stop') {
    if (-not (Test-Path -LiteralPath $statePath)) { Write-Host 'No launcher-owned service is recorded.'; exit 0 }
    $saved=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $owned=Get-OwnedProcess $saved.console
    if ($owned) {
        $status=Get-WorkspaceStatus
        if (-not $status -or -not $status.remote.shutdown_available -or
            ($status.remote.pid -ne $owned.Id -and $status.remote.parent_pid -ne $owned.Id)) {
            throw 'The recorded process cannot be matched to the running service. No unrelated process was stopped.'
        }
        $stop=Invoke-RestMethod -Uri "$baseUrl/api/service/stop" -Method Post -ContentType 'application/json' -Body '{}' -TimeoutSec 12
        if (-not $stop.ok) { throw $stop.message }
        if (-not $owned.WaitForExit(20000)) { throw 'Service cleanup is still running. No process was forcibly terminated; retry Stop shortly.' }
        Remove-Item -LiteralPath $statePath -ErrorAction SilentlyContinue
        Write-Host 'Stopped the workspace service started by this launcher. Official WeChat remains open.'
    } else { Write-Host 'No matching launcher-owned process is running.' }
    exit 0
}
$status=Get-WorkspaceStatus
if ($status -and (-not $status.ok -or $status.app -ne '微信自动化控制台' -or $status.version -ne '1.1.0')) {
    throw 'Port 8765 is serving a different version. Close that service before starting this release.'
}
if (-not $status) {
    $consoleExe=Join-Path $workspaceDir '微信自动化控制台.exe'
    if (-not (Test-Path -LiteralPath $consoleExe)) { throw 'The packaged console executable is missing.' }
    $launched=Start-Process -FilePath $consoleExe -ArgumentList @('--serve','8765') -WorkingDirectory $workspaceDir -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $dataDir 'console-out.log') -RedirectStandardError (Join-Path $dataDir 'console-error.log')
    @{console=@{pid=$launched.Id;path=$consoleExe;startedTicks=$launched.StartTime.ToUniversalTime().Ticks.ToString()}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding utf8
    for ($attempt=0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 500
        $status=Get-WorkspaceStatus
        if ($status) { break }
        if ($launched.HasExited) { throw 'Console exited. Read data/console-error.log.' }
    }
    if (-not $status) { throw 'Console startup timed out. Read data/console-error.log.' }
    if (-not $status.ok -or $status.app -ne '微信自动化控制台' -or $status.version -ne '1.1.0' -or
        ($status.remote.pid -ne $launched.Id -and $status.remote.parent_pid -ne $launched.Id)) {
        throw 'Port 8765 does not belong to the service just started. No unrelated process was stopped.'
    }
}
if ($Mode -in @('Background','Plugins')) {
    $sdk=Invoke-RestMethod -Uri "$baseUrl/api/sdk/start" -Method Post -ContentType 'application/json' -Body '{}' -TimeoutSec 30
    if (-not $sdk.ok) { throw $sdk.message }
}
if ($Mode -eq 'Visual') {
    $nativeExe=Join-Path $workspaceDir 'maui\WeChatLikeMaui.exe'
    if (-not (Test-Path -LiteralPath $nativeExe)) { throw 'The native MAUI executable is missing.' }
    if ($OpenWeChat) { Start-Process -FilePath $nativeExe -ArgumentList '--wechat-direct' -WorkingDirectory (Split-Path -Parent $nativeExe) | Out-Null }
    else { Start-Process -FilePath $nativeExe -WorkingDirectory (Split-Path -Parent $nativeExe) | Out-Null }
} elseif ($Mode -eq 'Plugins') {
    if (-not $NoBrowser) { Start-Process "$baseUrl/?view=sdk" | Out-Null }
} elseif (-not $NoBrowser) { Start-Process "$baseUrl/" | Out-Null }
Write-Host 'Workspace ready. The phone uses this PC LAN address on port 8765. Login follows the official WeChat prompt and saved authorization.'
