param([int]$Port = 15255)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDll = Join-Path $taskRoot 'bin/Debug/net10.0/Foll-EmergencyAnalytics.dll'
if (-not (Test-Path -LiteralPath $taskDll)) { throw 'Ejecute dotnet build primero.' }
function New-TaskServiceJwt {
    function ConvertTo-TaskBase64Url([byte[]]$Value) { [Convert]::ToBase64String($Value).TrimEnd('=').Replace('+','-').Replace('/','_') }
    $taskHeader = ConvertTo-TaskBase64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $taskPayload = ConvertTo-TaskBase64Url ([Text.Encoding]::UTF8.GetBytes(('{"token_use":"service","client_id":"notification-communication","iss":"foll-iam","aud":"foll-emergency","scope":"emergencies.location.read","exp":' + [DateTimeOffset]::UtcNow.AddMinutes(5).ToUnixTimeSeconds() + '}')))
    $taskHmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::ASCII.GetBytes('smoke-test-only-key-with-at-least-32-bytes'))
    try { $taskSignature = ConvertTo-TaskBase64Url ($taskHmac.ComputeHash([Text.Encoding]::ASCII.GetBytes("$taskHeader.$taskPayload"))) } finally { $taskHmac.Dispose() }
    return "$taskHeader.$taskPayload.$taskSignature"
}
$taskArgs = @('"' + $taskDll + '"', '--urls', "http://127.0.0.1:$Port", '--JwtSettings:Secret=smoke-test-only-key-with-at-least-32-bytes', '--ServiceAuthentication:ClientSecret=smoke-test-only-client-secret-32-characters', '--EmergencyAnalyticsMqtt:Enabled=false', '--Outbox:Enabled=false', '--ApplyMigrationsOnStartup=false', '--Swagger:Enabled=true')
$taskProcess = Start-Process -FilePath 'dotnet' -ArgumentList $taskArgs -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru
try {
    $taskBase = "http://127.0.0.1:$Port"
    $taskReady = $false
    for ($taskAttempt = 0; $taskAttempt -lt 40; $taskAttempt++) {
        if ($taskProcess.HasExited) { throw 'El servicio terminó antes de iniciar.' }
        try { $taskResponse = Invoke-WebRequest "$taskBase/health/live" -TimeoutSec 1; $taskReady = $true; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $taskReady) { throw 'No respondió health/live.' }
    foreach ($taskCase in @(
        @{ Path = '/api/emergency/incidents/1'; Headers = @{ Authorization = 'Bearer ' + (New-TaskServiceJwt) } },
        @{ Path = '/internal/emergency/incidents/by-key/00000000-0000-0000-0000-000000000001'; Headers = @{ 'X-Foll-Service-Key' = 'obsolete-key' } }
    )) {
        $taskCode = 0
        try { $taskResult = Invoke-WebRequest "$taskBase$($taskCase.Path)" -Headers $taskCase.Headers -TimeoutSec 3; $taskCode = [int]$taskResult.StatusCode }
        catch { if ($_.Exception.Response) { $taskCode = [int]$_.Exception.Response.StatusCode } else { throw } }
        if ($taskCode -ne 401) { throw "Se esperaba 401 al usar identidad/credencial incorrecta; recibido $taskCode" }
    }
    foreach ($taskPath in @('/api/emergency/incidents/1', '/internal/emergency/incidents/by-key/00000000-0000-0000-0000-000000000001')) {
        $taskCode = 0
        try { $taskResult = Invoke-WebRequest "$taskBase$taskPath" -TimeoutSec 3; $taskCode = [int]$taskResult.StatusCode }
        catch { if ($_.Exception.Response) { $taskCode = [int]$_.Exception.Response.StatusCode } else { throw } }
        if ($taskCode -ne 401) { throw "Se esperaba 401 en $taskPath; recibido $taskCode" }
    }
    $taskSwagger = Invoke-RestMethod "$taskBase/swagger/v1/swagger.json" -TimeoutSec 5
    if (-not $taskSwagger.paths.'/api/emergency/incidents/{incidentId}/resolve') { throw 'No está el contrato de resolución en Swagger.' }
    Write-Output 'PASS: arranque real, health/live, protección JWT, protección de servicio y contrato Swagger; workers desactivados y sin migraciones.'
} finally {
    if (-not $taskProcess.HasExited) { Stop-Process -Id $taskProcess.Id -Force }
    $taskProcess.Dispose()
}
