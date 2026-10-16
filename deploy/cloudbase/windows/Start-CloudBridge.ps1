param()
. (Join-Path $PSScriptRoot 'Bridge-Common.ps1')
$existing = Get-ManagedBridgeProcess
if ($existing) { Write-Output '云端连接已在运行。'; exit 0 }
$directory = Get-BridgeDirectory
if (-not (Test-Path -LiteralPath (Join-Path $directory 'config.json'))) {
    throw '请先完成云端连接初始化。令牌应保存为本机加密配置。'
}
$python = Get-BridgePython
$script = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'cloud_bridge.py'))
$check = Invoke-RestMethod -Uri 'http://127.0.0.1:8765/api/status' -TimeoutSec 5
if (-not $check.ok) { throw '请先启动电脑版微信工作台。' }
# No passwords or tokens are passed through the process command line.
$process = Start-Process -FilePath $python -ArgumentList @('-X', 'utf8', ('"{0}"' -f $script), 'run') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru
$deadline = (Get-Date).AddSeconds(12)
do {
    Start-Sleep -Milliseconds 400
    $owned = Get-ManagedBridgeProcess
    if ($owned -and $owned.Id -eq $process.Id) { Write-Output '云端连接已启动。电脑需要保持开机，并保持微信登录。'; exit 0 }
    $process.Refresh()
    if ($process.HasExited) { throw '云端连接未能启动，请检查当前用户的加密配置。' }
} while ((Get-Date) -lt $deadline)
throw '尚未确认云端连接启动状态，请查看本机连接配置。'
