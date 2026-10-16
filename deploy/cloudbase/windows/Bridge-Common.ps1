$ErrorActionPreference = 'Stop'

function Get-BridgeDirectory {
    if (-not $env:LOCALAPPDATA) { throw '未找到当前 Windows 用户目录。' }
    return Join-Path $env:LOCALAPPDATA 'WeChatLikeConsole\CloudBridge'
}

function Get-BridgePython {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python311\pythonw.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python311\python.exe'),
        (Join-Path $env:USERPROFILE '.workbuddy\binaries\python\envs\wechatbuild311\Scripts\pythonw.exe'),
        (Join-Path $env:USERPROFILE '.workbuddy\binaries\python\envs\wechatbuild311\Scripts\python.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    foreach ($name in @('pythonw.exe', 'python.exe')) {
        $candidate = Get-Command $name -ErrorAction SilentlyContinue
        if ($candidate -and $candidate.Source -notmatch '\\WindowsApps\\') { return $candidate.Source }
    }
    throw '未找到 Python 3，请安装官方 Python 3.11 或使用已有项目 Python 环境。'
}

function Get-ManagedBridgeProcess {
    $recordFile = Join-Path (Get-BridgeDirectory) 'runtime.json'
    if (-not (Test-Path -LiteralPath $recordFile)) { return $null }
    try {
        $record = Get-Content -LiteralPath $recordFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($record.state -ne 'running') { return $null }
        $expected = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'cloud_bridge.py'))
        if ([System.IO.Path]::GetFullPath([string]$record.scriptPath) -ne $expected) { return $null }
        $managedId = [int]$record.pid
        $candidate = Get-CimInstance Win32_Process -Filter "ProcessId=$managedId" -ErrorAction Stop
        if (-not $candidate -or $candidate.Name -notmatch '^pythonw?\.exe$') { return $null }
        if ($candidate.CommandLine -notmatch [regex]::Escape($expected)) { return $null }
        $process = Get-Process -Id $managedId -ErrorAction Stop
        $recordTime = [DateTimeOffset]::FromUnixTimeMilliseconds([long]([double]$record.startedAt * 1000))
        if ([Math]::Abs(($process.StartTime.ToUniversalTime() - $recordTime.UtcDateTime).TotalSeconds) -gt 10) { return $null }
        return $process
    } catch { return $null }
}
