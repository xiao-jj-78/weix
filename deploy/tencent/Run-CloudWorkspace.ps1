[CmdletBinding()]
param([switch]$RegisterAtLogon)
$ErrorActionPreference='Stop'
if ($RegisterAtLogon) {
    $currentUser=[Security.Principal.WindowsIdentity]::GetCurrent().Name
    $quotedScript='"'+(Join-Path $PSScriptRoot 'Run-CloudWorkspace.ps1')+'"'
    $action=New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -ExecutionPolicy Bypass -File '+$quotedScript) -WorkingDirectory $PSScriptRoot
    $trigger=New-ScheduledTaskTrigger -AtLogOn -User $currentUser
    $principal=New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName 'WeChatWorkspace-Interactive' -Action $action -Trigger $trigger -Principal $principal -Description 'Start the workspace only in the signed-in user desktop.' -Force | Out-Null
    Write-Output 'Registered for this user logon. No password was stored and Windows auto-login was not enabled.'
    exit 0
}
if ([Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) { throw 'Run this script from the signed-in user desktop, not a Windows service/TAT session.' }
$env:WLC_BIND_HOST='127.0.0.1'
& (Join-Path $PSScriptRoot 'Start-Workspace.ps1') -Mode Background
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$status=Invoke-RestMethod -Uri 'http://127.0.0.1:8765/api/status' -TimeoutSec 5
if ($status.remote.bind_host -ne '127.0.0.1') { throw 'An existing service is listening outside loopback. Stop that release before using the cloud launcher.' }
Write-Output 'CLOUD_LOCAL_READY http://127.0.0.1:8765/'
