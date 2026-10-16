[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackageZip,
    [string]$Destination='C:\WeChatWorkspace'
)
$ErrorActionPreference='Stop'
$package=(Resolve-Path -LiteralPath $PackageZip).Path
$installRoot=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
$driveRoot=[IO.Path]::GetPathRoot($installRoot).TrimEnd('\')
if ($installRoot -eq $driveRoot -or $installRoot -eq $env:WINDIR -or $installRoot -eq $env:ProgramFiles) { throw 'Choose a dedicated application directory.' }
if ((Test-Path -LiteralPath $installRoot) -and (Get-ChildItem -LiteralPath $installRoot -Force | Select-Object -First 1)) { throw 'Destination is not empty. Stop and back up the existing release before upgrading.' }
$staging=Join-Path ([IO.Path]::GetTempPath()) ('wechat-deploy-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($package)
try {
    foreach ($entry in $archive.Entries) {
        $target=[IO.Path]::GetFullPath((Join-Path $staging $entry.FullName))
        if (-not $target.StartsWith($staging+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Archive contains an unsafe path.' }
    }
} finally { $archive.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory($package,$staging)
$release=Get-ChildItem -LiteralPath $staging -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName '微信自动化控制台.exe') }
if (@($release).Count -ne 1) { throw 'The archive does not contain exactly one workspace release.' }
$manifestPath=Join-Path $release.FullName '文件校验.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Release checksum manifest is missing.' }
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($property in $manifest.PSObject.Properties) {
    $file=[IO.Path]::GetFullPath((Join-Path $release.FullName $property.Name))
    if (-not $file.StartsWith($release.FullName+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest contains an unsafe path.' }
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Missing release file: '+$property.Name) }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $property.Value.sha256) { throw ('Checksum mismatch: '+$property.Name) }
}
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Get-ChildItem -LiteralPath $release.FullName -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $installRoot -Recurse -Force }
foreach ($name in @('Run-CloudWorkspace.ps1','Test-CloudWorkspace.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $installRoot $name) -Force
}
Write-Output ('INSTALLED '+$installRoot)
Write-Output 'No cloud resources were created. Run Run-CloudWorkspace.ps1 from the intended signed-in Windows desktop.'
