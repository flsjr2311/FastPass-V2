# Script para obter IDs válidos do banco de dados e testar a API
#
# Requer PowerShell 7+: o Add-Type/carregamento do MySqlConnector (.NET 8) falha no
# Windows PowerShell 5.1 com ReflectionTypeLoadException.

# Resolve a connection string na mesma ordem de precedência do ASP.NET Core em Development:
# user-secrets primeiro, appsettings.json como fallback.
$secretsPath = Join-Path $env:APPDATA 'Microsoft\UserSecrets\FastPass.Api-Development\secrets.json'
$connectionString = $null
$origem = 'appsettings.json'

if (Test-Path $secretsPath) {
    $fromSecrets = (Get-Content $secretsPath -Raw | ConvertFrom-Json).'ConnectionStrings:FastPass'
    if ($fromSecrets) {
        $connectionString = $fromSecrets
        $origem = 'user-secrets (tem precedência sobre o appsettings.json)'
    }
}

if (-not $connectionString) {
    $appsettings = Get-Content 'src/FastPass.Api/appsettings.json' | ConvertFrom-Json
    $connectionString = $appsettings.ConnectionStrings.FastPass
}

# Não imprime a senha no console.
$mascarada = ($connectionString -split ';' | ForEach-Object {
    if ($_ -match '^\s*(Password|Pwd)\s*=') { (($_ -split '=')[0]) + '=***' } else { $_ }
}) -join ';'

Write-Host "🔍 Conectando ao banco de dados..." -ForegroundColor Cyan
Write-Host "Origem: $origem" -ForegroundColor DarkGray
Write-Host "Connection: $mascarada`n" -ForegroundColor DarkGray

# Define a query SQL
$sqlQuery = @"
SELECT 
    e.id as event_id,
    e.name as event_name,
    g.id as gate_id,
    g.name as gate_name,
    d.id as device_id,
    d.name as device_name,
    d.identifier,
    d.operation_mode
FROM fp_events e
INNER JOIN fp_event_gates eg ON eg.event_id = e.id AND eg.active = 1
INNER JOIN fp_gates g ON g.id = eg.gate_id AND g.active = 1
INNER JOIN fp_devices d ON d.gate_id = g.id AND d.active = 1
WHERE e.status = 'Running'
LIMIT 1;
"@

try {
    # Usa MySqlConnector para conectar
    $connection = [MySqlConnector.MySqlConnection]::new($connectionString)
    $connection.Open()
    
    $command = $connection.CreateCommand()
    $command.CommandText = $sqlQuery
    
    $reader = $command.ExecuteReader()
    
    if ($reader.Read()) {
        $eventId = $reader["event_id"]
        $eventName = $reader["event_name"]
        $gateId = $reader["gate_id"]
        $gateName = $reader["gate_name"]
        $deviceId = $reader["device_id"]
        $deviceName = $reader["device_name"]
        $identifier = $reader["identifier"]
        $operationMode = $reader["operation_mode"]
        
        Write-Host "✅ Dados encontrados!`n" -ForegroundColor Green
        Write-Host "📊 Evento:" -ForegroundColor Yellow
        Write-Host "   ID: $eventId"
        Write-Host "   Nome: $eventName`n"
        
        Write-Host "🚪 Portaria:" -ForegroundColor Yellow
        Write-Host "   ID: $gateId"
        Write-Host "   Nome: $gateName`n"
        
        Write-Host "📱 Dispositivo (Catraca):" -ForegroundColor Yellow
        Write-Host "   ID: $deviceId"
        Write-Host "   Nome: $deviceName"
        Write-Host "   Identifier: $identifier"
        Write-Host "   Modo atual: $operationMode`n"
        
        Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host "📋 USE ESSES IDs NOS TESTES:" -ForegroundColor Cyan
        Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "PUT http://localhost:5088/api/events/$eventId/gates/$gateId/devices/$deviceId/operation-mode" -ForegroundColor Green
        Write-Host ""
        Write-Host "Body JSON:" -ForegroundColor Yellow
        Write-Host '{' -ForegroundColor White
        Write-Host '  "operationMode": "Free"  # ou "Active" ou "Blocked"' -ForegroundColor White
        Write-Host '}' -ForegroundColor White
        Write-Host ""
        Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
        
        # Oferece opção de testar
        Write-Host ""
        Write-Host "🧪 Deseja testar agora? (s/n)" -ForegroundColor Magenta
        $response = Read-Host
        
        if ($response -eq 's' -or $response -eq 'S' -or $response -eq 'y' -or $response -eq 'Y') {
            Write-Host ""
            Write-Host "Testando modos..." -ForegroundColor Cyan
            
            $host1 = "http://localhost:5088"
            $endpoint = "/api/events/$eventId/gates/$gateId/devices/$deviceId/operation-mode"
            
            foreach ($mode in @("Free", "Blocked", "Active")) {
                Write-Host ""
                Write-Host "📡 Testando modo: $mode" -ForegroundColor Yellow
                
                $body = @{
                    operationMode = $mode
                } | ConvertTo-Json
                
                try {
                    $result = Invoke-RestMethod -Uri "$host1$endpoint" `
                        -Method PUT `
                        -ContentType "application/json" `
                        -Body $body
                    
                    Write-Host "✅ Sucesso! Modo alterado para: $($result.operationMode)" -ForegroundColor Green
                } catch {
                    Write-Host "❌ Erro: $($_.Exception.Message)" -ForegroundColor Red
                }
                
                Start-Sleep -Seconds 1
            }
            
            Write-Host ""
            Write-Host "✅ Teste concluído!" -ForegroundColor Green
        }
    }
    else {
        Write-Host "⚠️ Nenhum evento ativo encontrado no banco de dados." -ForegroundColor Yellow
        Write-Host "Crie um evento com status 'Running' e dispositivos antes de testar." -ForegroundColor Yellow
    }
    
    $reader.Close()
    $connection.Close()
}
catch {
    Write-Host "❌ Erro ao conectar ao banco: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Certifique-se de que:" -ForegroundColor Yellow
    Write-Host "1. MySQL está rodando" -ForegroundColor Yellow
    Write-Host "2. A connection string está correta em appsettings.json" -ForegroundColor Yellow
    Write-Host "3. O banco FastPass existe" -ForegroundColor Yellow
}
