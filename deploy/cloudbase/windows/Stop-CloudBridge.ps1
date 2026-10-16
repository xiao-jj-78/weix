param()
. (Join-Path $PSScriptRoot 'Bridge-Common.ps1')
$process = Get-ManagedBridgeProcess
if (-not $process) { Write-Output '当前没有运行中的本机云端连接。'; exit 0 }
$stopFile = Join-Path (Get-BridgeDirectory) 'stop.request'
[System.IO.File]::WriteAllText($stopFile, [DateTime]::UtcNow.ToString('o'), (New-Object System.Text.UTF8Encoding($false)))
Write-Output '已请求停止连接，正在等待已发出的操作结束。'
# Do not terminate WeChat, the SDK, or an in-flight write. Maximum local timeout is 115 seconds.
if ($process.WaitForExit(130000)) { Write-Output '云端连接已停止。'; exit 0 }
Write-Output '操作仍在收尾，停止请求已保留。请稍后再查看状态。'
exit 2
