param()
. (Join-Path $PSScriptRoot 'Bridge-Common.ps1')
$python = Get-BridgePython
$script = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'cloud_bridge.py'))
Start-Process -FilePath $python -ArgumentList @('-X', 'utf8', ('"{0}"' -f $script), 'copy-browser-token') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
