param([int]$Port = 18767)
$ErrorActionPreference = 'Stop'
$sdkBuildRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$hostPath = Join-Path $sdkBuildRoot 'publish/WeChatSdkHost.exe'
$baseUrl = "http://127.0.0.1:$Port"
$outLog = Join-Path $sdkBuildRoot 'smoke-stdout.log'
$errLog = Join-Path $sdkBuildRoot 'smoke-stderr.log'
$checks = [System.Collections.Generic.List[string]]::new()
$server = Start-Process -FilePath $hostPath -ArgumentList @('--port', $Port) -WorkingDirectory (Split-Path -Parent $hostPath) -WindowStyle Hidden -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
try {
    $status = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try { $status = Invoke-RestMethod "$baseUrl/api/sdk/status" -TimeoutSec 2; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$status.ok -or !$status.running -or !$status.sdk.loaded) { throw 'Host or actual SDK assembly is unavailable.' }
    $checks.Add('PASS host running / actual WeChatAuto4x.SDK assembly loaded')
    $bindings = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction Stop)
    if ($bindings | Where-Object { $_.LocalAddress -ne '127.0.0.1' }) { throw 'Host is exposed beyond loopback.' }
    $checks.Add('PASS loopback-only listener')
    $plugins = Invoke-RestMethod "$baseUrl/api/sdk/plugins" -TimeoutSec 3
    if (@($plugins.plugins).Count -ne 4 -or $plugins.errors.Count -gt 0) { throw 'Plugin catalog or sample DLL failed.' }
    if (!($plugins.plugins | Where-Object { $_.id -eq 'session-snapshot' -and $_.source -eq 'local-assembly' })) { throw 'External sample plugin was not loaded.' }
    $checks.Add('PASS 4 plugins / external sample DLL loaded')
    foreach ($id in @('sdk-health', 'session-snapshot')) {
        $result = Invoke-RestMethod "$baseUrl/api/sdk/plugins/run" -Method Post -ContentType 'application/json' -Body ('{"id":"' + $id + '"}') -TimeoutSec 5
        if (!$result.ok) { throw "Plugin failed: $id" }
        $checks.Add("PASS read-only plugin $id")
    }
    $unknown = Invoke-WebRequest "$baseUrl/api/sdk/plugins/run" -Method Post -ContentType 'application/json' -Body '{"id":"does-not-exist"}' -SkipHttpErrorCheck
    if ([int]$unknown.StatusCode -ne 404) { throw 'Unknown plugin was not rejected.' }
    $checks.Add('PASS unknown plugin rejected with 404')
    $badOrigin = Invoke-WebRequest "$baseUrl/api/sdk/plugins/run" -Method Post -ContentType 'application/json' -Body '{"id":"sdk-health"}' -Headers @{ Origin = 'https://example.invalid' } -SkipHttpErrorCheck
    if ([int]$badOrigin.StatusCode -ne 403) { throw 'Cross-site write was not rejected.' }
    $checks.Add('PASS nonlocal Origin rejected with 403')
    $badType = Invoke-WebRequest "$baseUrl/api/sdk/plugins/run" -Method Post -ContentType 'text/plain' -Body '{"id":"sdk-health"}' -SkipHttpErrorCheck
    if ([int]$badType.StatusCode -ne 415) { throw 'Non-JSON request was not rejected.' }
    $checks.Add('PASS non-JSON write rejected with 415')
    $checks.Add('No WeChat launch, login, contacts, chat reads, messages or likes executed.')
    $checks.Add('SDK_SMOKE_PASS')
    $checks | Set-Content -LiteralPath (Join-Path $sdkBuildRoot 'smoke-result.txt') -Encoding utf8
    $checks
} finally {
    if (!$server.HasExited) { Stop-Process -Id $server.Id -Force }
}
