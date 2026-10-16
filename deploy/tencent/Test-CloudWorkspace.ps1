$ErrorActionPreference='Stop'
$runtime=@()
if (Get-Command dotnet -ErrorAction SilentlyContinue) { $runtime=@(& dotnet --list-runtimes) }
$api=$null
try { $api=Invoke-RestMethod -Uri 'http://127.0.0.1:8765/api/status' -TimeoutSec 3 } catch { }
$sdk=$null
try { $sdk=Invoke-RestMethod -Uri 'http://127.0.0.1:8765/api/sdk/status' -TimeoutSec 3 } catch { }
[pscustomobject]@{
    WindowsVersion=[Environment]::OSVersion.VersionString
    SessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId
    Dotnet10=[bool]($runtime -match '^Microsoft.NETCore.App 10\.')
    AspNet10=[bool]($runtime -match '^Microsoft.AspNetCore.App 10\.')
    ControlReady=[bool]($api -and $api.ok)
    BindHost=if($api){$api.remote.bind_host}else{$null}
    SdkRunning=[bool]($sdk -and $sdk.running)
    LoginVerified=$false
    CloudDeploymentVerified=$false
    Note='Runtime checks do not prove WeChat login, interactive UI compatibility or cloud connectivity.'
}|ConvertTo-Json
