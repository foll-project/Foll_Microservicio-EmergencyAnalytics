param([int]$Port = 15255, [switch]$CheckDatabase)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDll = Join-Path $taskRoot 'bin/Debug/net10.0/Foll-EmergencyAnalytics.dll'
if (-not (Test-Path -LiteralPath $taskDll)) { throw 'Ejecute dotnet build primero.' }
$taskArgs = @('"' + $taskDll + '"', '--urls', "http://127.0.0.1:$Port", '--JwtSettings:Secret=smoke-test-only-key-with-at-least-32-bytes', '--EmergencyAnalyticsMqtt:Enabled=false', '--Outbox:Enabled=false', '--IntegrationConsumers:Enabled=false', '--ApplyMigrationsOnStartup=false', '--Swagger:Enabled=true')
$taskProcess = Start-Process -FilePath 'dotnet' -ArgumentList $taskArgs -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru
try {
    $taskBase = "http://127.0.0.1:$Port"
    $taskReady = $false
    for ($taskAttempt = 0; $taskAttempt -lt 40; $taskAttempt++) {
        if ($taskProcess.HasExited) { throw 'El servicio terminó antes de iniciar.' }
        try { $taskResponse = Invoke-WebRequest -UseBasicParsing "$taskBase/health/live" -TimeoutSec 1; $taskReady = $true; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $taskReady) { throw 'No respondió health/live.' }
    if ($CheckDatabase) {
        $taskDatabaseHealth = Invoke-RestMethod "$taskBase/health/ready" -TimeoutSec 30
        if ($taskDatabaseHealth.status -ne 'ready') { throw 'La base de Emergency no está preparada.' }
        Write-Output 'PASS: health/ready consulta las cuatro tablas de Emergency.'
    }
    foreach ($taskPath in @('/api/emergency/incidents/1')) {
        $taskCode = 0
        try { $taskResult = Invoke-WebRequest -UseBasicParsing "$taskBase$taskPath" -TimeoutSec 3; $taskCode = [int]$taskResult.StatusCode }
        catch { if ($_.Exception.Response) { $taskCode = [int]$_.Exception.Response.StatusCode } else { throw } }
        if ($taskCode -ne 401) { throw "Se esperaba 401 en $taskPath; recibido $taskCode" }
    }
    $taskSwagger = Invoke-RestMethod "$taskBase/swagger/v1/swagger.json" -TimeoutSec 5
    if (-not $taskSwagger.paths.'/api/emergency/incidents/{incidentId}/resolve') { throw 'No está el contrato de resolución en Swagger.' }
    Write-Output 'PASS: arranque real, health/live, protección JWT, contrato Swagger; workers desactivados y sin migraciones.'
} finally {
    if (-not $taskProcess.HasExited) { Stop-Process -Id $taskProcess.Id -Force }
    $taskProcess.Dispose()
}
