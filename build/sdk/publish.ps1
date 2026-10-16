param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$sdkBuildRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkMode = if ($SelfContained) { 'true' } else { 'false' }
& dotnet publish (Join-Path $sdkBuildRoot 'WeChatSdkHost/WeChatSdkHost.csproj') -c Release -f net10.0-windows -p:TargetFrameworks=net10.0-windows --self-contained $frameworkMode -o (Join-Path $sdkBuildRoot 'publish')
if ($LASTEXITCODE -ne 0) { throw 'SDK host publish failed.' }
& dotnet build (Join-Path $sdkBuildRoot 'WeChatSdk.SamplePlugin/WeChatSdk.SamplePlugin.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Sample plugin build failed.' }
& dotnet build (Join-Path $sdkBuildRoot 'WeChatSdk.OwnDataPlugin/WeChatSdk.OwnDataPlugin.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'OwnData plugin build failed.' }
$pluginDirectory = Join-Path $sdkBuildRoot 'publish/plugins'
New-Item -ItemType Directory -Force -Path $pluginDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $sdkBuildRoot 'WeChatSdk.SamplePlugin/bin/Release/net10.0-windows/WeChatSdk.SamplePlugin.dll') -Destination $pluginDirectory -Force
Copy-Item -LiteralPath (Join-Path $sdkBuildRoot 'WeChatSdk.OwnDataPlugin/bin/Release/net10.0-windows/WeChatSdk.OwnDataPlugin.dll') -Destination $pluginDirectory -Force
$license = Join-Path $sdkBuildRoot 'vendor/WeChatAuto.SDK/LICENSE'
if (Test-Path $license) { Copy-Item -LiteralPath $license -Destination (Join-Path $sdkBuildRoot 'publish/LICENSE-WeChatAuto.SDK.txt') -Force }
$readme = Join-Path $sdkBuildRoot 'README.md'
if (Test-Path $readme) { Copy-Item -LiteralPath $readme -Destination (Join-Path $sdkBuildRoot 'publish/README.md') -Force }
# Provide the console exe name the MAUI LocalConsoleHost expects to auto-launch.
Copy-Item -LiteralPath (Join-Path $sdkBuildRoot 'publish/WeChatSdkHost.exe') -Destination (Join-Path $sdkBuildRoot 'publish/微信自动化控制台.exe') -Force
Write-Host 'Ready: publish/WeChatSdkHost.exe --port 18766 (also published as 微信自动化控制台.exe)'
