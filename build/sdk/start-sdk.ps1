$ErrorActionPreference = 'Stop'
$sdkBuildRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $sdkBuildRoot 'publish/WeChatSdkHost.exe') --port 18766
