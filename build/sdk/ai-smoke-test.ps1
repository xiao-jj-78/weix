param([int]$Port = 18769)
$ErrorActionPreference = 'Stop'
$sdkBuildRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$hostPath = Join-Path $sdkBuildRoot 'publish/WeChatSdkHost.exe'
$baseUrl = "http://127.0.0.1:$Port"
$temporaryConfig = Join-Path ([IO.Path]::GetTempPath()) ('WeChatAiHttpTest-' + [Guid]::NewGuid().ToString('N'))
$oldConfig = $env:WLC_SDK_AI_CONFIG_DIR
$checks = [System.Collections.Generic.List[string]]::new()
$server = $null
try {
    $env:WLC_SDK_AI_CONFIG_DIR = $temporaryConfig
    $server = Start-Process -FilePath $hostPath -ArgumentList @('--port', $Port) -WorkingDirectory (Split-Path -Parent $hostPath) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $sdkBuildRoot 'ai-smoke-stdout.log') -RedirectStandardError (Join-Path $sdkBuildRoot 'ai-smoke-stderr.log')
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try { $config = Invoke-RestMethod "$baseUrl/api/sdk/ai/config" -TimeoutSec 2; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$config.ok -or $config.running -or $config.config.profiles[0].baseUrl -ne '') { throw 'Initial configuration is not empty/disabled.' }
    $checks.Add('PASS initial disabled / no endpoint or model assumed')
    foreach ($path in @('preview', 'start')) {
        $reply = Invoke-WebRequest "$baseUrl/api/sdk/ai/$path" -Method Post -ContentType 'application/json' -Body '{"message":"offline test"}' -SkipHttpErrorCheck
        if ([int]$reply.StatusCode -ne 409) { throw "Unconfigured $path was not rejected." }
    }
    $checks.Add('PASS unconfigured preview/start rejected before AI or SDK access')
    $body = '{"profiles":[{"id":"http-test","name":"HTTP test","protocol":"openai-compatible","baseUrl":"https://unit.invalid/v1","model":"mock-model","apiKeyEnv":"","apiKey":"http-test-fixture-secret"}],"activeProfileId":"http-test","allowedContacts":["fixture"]}'
    $saved = Invoke-RestMethod "$baseUrl/api/sdk/ai/config" -Method Post -ContentType 'application/json' -Body $body
    if (!$saved.ok -or !$saved.config.profiles[0].hasApiKey) { throw 'Write-only key was not saved.' }
    $export = Invoke-WebRequest "$baseUrl/api/sdk/ai/profiles/export"
    if ($export.Content.Contains('http-test-fixture-secret') -or $export.Content.Contains('encryptedApiKey')) { throw 'Export disclosed key.' }
    if ((Get-Content -LiteralPath (Join-Path $temporaryConfig 'ai-config.json') -Raw).Contains('http-test-fixture-secret')) { throw 'Local config stored plaintext key.' }
    $checks.Add('PASS write-only Windows-protected key / sanitized export')
    $roundtrip = Invoke-RestMethod "$baseUrl/api/sdk/ai/profiles/import" -Method Post -ContentType 'application/json' -Body (@{json=$export.Content} | ConvertTo-Json -Compress)
    if (!$roundtrip.ok -or !$roundtrip.config.profiles[0].hasApiKey) { throw 'Import lost existing local key.' }
    $checks.Add('PASS sanitized import preserves current user key')
    $status = Invoke-RestMethod "$baseUrl/api/sdk/ai/status"
    if (!$status.ok -or $status.running -or $status.replyCount -ne 0) { throw 'Status incorrectly indicates activity.' }
    $checks.Add('PASS no automatic reply start after configuration/import')
    $badOrigin = Invoke-WebRequest "$baseUrl/api/sdk/ai/config" -Method Post -ContentType 'application/json' -Body '{}' -Headers @{Origin='https://example.invalid'} -SkipHttpErrorCheck
    if ([int]$badOrigin.StatusCode -ne 403) { throw 'Cross-site AI write accepted.' }
    $checks.Add('PASS cross-site AI configuration blocked')
    $stop = Invoke-RestMethod "$baseUrl/api/sdk/host/stop" -Method Post -ContentType 'application/json' -Body '{}'
    if (!$stop.ok -or !$server.WaitForExit(10000)) { throw 'Graceful stop failed.' }
    $checks.Add('PASS internal graceful host shutdown')
    $checks.Add('AI_HTTP_SMOKE_PASS network=local-host-only wechat=untouched')
    $checks | Set-Content -LiteralPath (Join-Path $sdkBuildRoot 'ai-smoke-result.txt') -Encoding utf8
    $checks
} finally {
    $env:WLC_SDK_AI_CONFIG_DIR = $oldConfig
    if ($null -ne $server -and !$server.HasExited) { Stop-Process -Id $server.Id -Force }
    if (Test-Path -LiteralPath $temporaryConfig) {
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryConfig)
        $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (!$resolvedTemporary.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedTemporary) -notmatch '^WeChatAiHttpTest-[0-9a-f]{32}$') { throw 'Refusing cleanup outside test temp directory.' }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
