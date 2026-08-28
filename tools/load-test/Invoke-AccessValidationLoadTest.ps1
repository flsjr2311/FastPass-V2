<#
.SYNOPSIS
    Teste de carga (stress test) de validações de acesso do FastPass.

.DESCRIPTION
    Dispara validações de acesso concorrentes contra a API FastPass (POST /api/access/app/validate),
    simulando o fluxo real de uma portaria com inteligência:
      - Cada ticket é enviado para a portaria correta (baseado no setor do ticket e na matriz portaria×setor)
      - Tickets sem setor são distribuídos aleatoriamente entre portarias
      - Tickets são consumidos em ordem e não são reenviados até esgotar o pool
      - Uma fração configurável (padrão 10%) simula erros esperados de operação:
        * Ingresso já usado (reenvia um código que já aprovou — testa limite de entradas)
        * Ingresso cancelado (usa um ticket com status "cancelled")
        * Ingresso inexistente (gera um código aleatório)
        * Portaria errada (envia ticket para portaria de outro setor)

    Ideal para deixar rodando em background enquanto se acompanha o Dashboard e os Relatórios.

.PARAMETER ApiBaseUrl
    URL base da API FastPass. Padrão: http://127.0.0.1:5088

.PARAMETER EventId
    GUID do evento cujos ingressos serão usados no teste. Obrigatório.

.PARAMETER Username
    Usuário para login na API. Padrão: admin

.PARAMETER Password
    Senha para login na API. Padrão: Admin@1234

.PARAMETER DurationMinutes
    Duração total do teste, em minutos. Padrão: 12

.PARAMETER Concurrency
    Número de validações disparadas simultaneamente em cada "onda". Padrão: 6

.PARAMETER ErrorRatePercent
    Percentual aproximado de tentativas que devem falhar de propósito (0-100). Padrão: 10

.PARAMETER WaveDelayMs
    Pausa entre ondas de requisições concorrentes, em milissegundos. Padrão: 1000

.EXAMPLE
    .\Invoke-AccessValidationLoadTest.ps1 -EventId "ae7d5cdd-afd7-4b3c-98cc-10b2adca3589"

.NOTES
    Pré-requisitos:
      - API FastPass rodando e acessível em -ApiBaseUrl
      - O evento deve ter ao menos 1 ticket ativo
      - O evento deve ter ao menos 1 associação portaria×setor ativa (direção Entry)
#>

param(
    [string]$ApiBaseUrl = "http://127.0.0.1:5088",
    [Parameter(Mandatory = $true)][string]$EventId,
    [string]$Username = "admin",
    [string]$Password = "Admin@1234",
    [double]$DurationMinutes = 12,
    [int]$Concurrency = 6,
    [int]$ErrorRatePercent = 10,
    [int]$WaveDelayMs = 1000
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

# ── HttpClient com cookie container compartilhado ────────────────────────────
$cookieContainer = New-Object System.Net.CookieContainer
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.CookieContainer = $cookieContainer
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromSeconds(15)

function Invoke-JsonPost {
    param([string]$Path, [hashtable]$BodyObj)
    $json = $BodyObj | ConvertTo-Json -Depth 5 -Compress
    $content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, "application/json")
    $resp = $client.PostAsync("$ApiBaseUrl$Path", $content).GetAwaiter().GetResult()
    $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    return [PSCustomObject]@{ StatusCode = [int]$resp.StatusCode; Body = $body }
}

function Invoke-JsonGet {
    param([string]$Path)
    $resp = $client.GetAsync("$ApiBaseUrl$Path").GetAwaiter().GetResult()
    $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $resp.IsSuccessStatusCode) { throw "GET $Path falhou: $($resp.StatusCode) - $body" }
    $parsed = ConvertFrom-Json $body
    if ($null -eq $parsed) { return @() }
    if ($parsed -is [Array]) { return $parsed }
    return @($parsed)
}

# ── Login ────────────────────────────────────────────────────────────────────
Write-Host "Autenticando como '$Username'..."
$loginResult = Invoke-JsonPost "/api/auth/login" @{ username = $Username; password = $Password }
if ($loginResult.StatusCode -ne 200) {
    throw "Falha no login: HTTP $($loginResult.StatusCode) - $($loginResult.Body)"
}
Write-Host "Login OK."

# ── Coleta de dados do evento ────────────────────────────────────────────────
Write-Host "Carregando tickets, portarias e matriz do evento $EventId..."

$tickets = Invoke-JsonGet "/api/events/$EventId/tickets"
$gates = Invoke-JsonGet "/api/events/$EventId/gates"
$gateSectors = Invoke-JsonGet "/api/events/$EventId/gate-sectors"

$activeTickets = @($tickets | Where-Object { $_.status -eq 'active' })
$cancelledTickets = @($tickets | Where-Object { $_.status -ne 'active' })
$validPairs = @($gateSectors | Where-Object { $_.active -and $_.direction -eq 'Entry' } | ForEach-Object {
    [PSCustomObject]@{ GateId = $_.gateId; SectorId = $_.sectorId; GateName = $_.gateName; SectorName = $_.sectorName }
})

if ($activeTickets.Count -eq 0) { throw "Nenhum ticket ativo encontrado no evento $EventId." }
if ($validPairs.Count -eq 0) { throw "Nenhuma associacao portaria x setor ativa (direcao Entry) encontrada." }

# ── Construir mapa setor → portaria ─────────────────────────────────────────
$sectorToGate = @{}
foreach ($pair in $validPairs) {
    $sectorToGate[$pair.SectorId] = $pair.GateId
}
$allGateIds = @($validPairs | ForEach-Object { $_.GateId } | Select-Object -Unique)

# ── Classificar tickets por setor ────────────────────────────────────────────
$ticketsWithGate = New-Object System.Collections.Generic.List[Object]
$ticketsWithoutSector = 0
$ticketsWithSector = 0

foreach ($t in $activeTickets) {
    $gateId = $null
    if ($t.sectorId -and $sectorToGate.ContainsKey($t.sectorId)) {
        $gateId = $sectorToGate[$t.sectorId]
        $ticketsWithSector++
    } else {
        # Ticket sem setor — distribui para portaria aleatória
        $gateId = $allGateIds | Get-Random
        $ticketsWithoutSector++
    }
    $ticketsWithGate.Add([PSCustomObject]@{
        Code = $t.code
        GateId = $gateId
        SectorId = $t.sectorId
        MaxEntries = if ($t.maximumEntries) { $t.maximumEntries } else { 1 }
    })
}

Write-Host "Tickets ativos: $($activeTickets.Count) (com setor: $ticketsWithSector, sem setor: $ticketsWithoutSector)"
Write-Host "Cancelados: $($cancelledTickets.Count) | Portarias: $($allGateIds.Count) | Pares validos: $($validPairs.Count)"

# ── Construir portarias "erradas" para cada setor ────────────────────────────
$wrongGateForSector = @{}
foreach ($pair in $validPairs) {
    $otherGates = @($allGateIds | Where-Object { $_ -ne $pair.GateId })
    if ($otherGates.Count -gt 0) {
        $wrongGateForSector[$pair.SectorId] = $otherGates
    }
}

# ── Fila de tickets (embaralhada) ────────────────────────────────────────────
$freshQueue = New-Object System.Collections.Generic.Queue[Object]
foreach ($t in ($ticketsWithGate | Get-Random -Count $ticketsWithGate.Count)) { $freshQueue.Enqueue($t) }
$usedTickets = New-Object System.Collections.Generic.List[Object]
$poolExhaustedLogged = $false

# ── Contadores ───────────────────────────────────────────────────────────────
$stats = @{
    Total = 0; Approved = 0; Rejected = 0; HttpError = 0
    Reasons = @{}
}

function Register-Result {
    param([string]$DecisionJson, [int]$StatusCode)
    $stats.Total++
    if ($StatusCode -ne 200 -and $StatusCode -ne 201) {
        $stats.HttpError++
        return
    }
    try {
        $parsed = $DecisionJson | ConvertFrom-Json
        if ($parsed.approved) {
            $stats.Approved++
        } else {
            $stats.Rejected++
            $reason = if ($parsed.reason) { $parsed.reason } else { "Motivo nao informado" }
            if (-not $stats.Reasons.ContainsKey($reason)) { $stats.Reasons[$reason] = 0 }
            $stats.Reasons[$reason]++
        }
    } catch {
        $stats.HttpError++
    }
}

function New-ScenarioPayload {
    $roll = Get-Random -Minimum 0 -Maximum 100

    if ($roll -lt $ErrorRatePercent) {
        # ── Cenário de ERRO (10%) ────────────────────────────────────────────
        $errRoll = Get-Random -Minimum 0 -Maximum 100

        if ($errRoll -lt 30 -and $usedTickets.Count -gt 0) {
            # Ingresso já usado — reenvia para mesma portaria (testa limite de entradas)
            $t = $usedTickets | Get-Random
            return @{ credentialCode = $t.Code; eventId = $EventId; gateId = $t.GateId; idempotencyKey = [guid]::NewGuid().ToString() }
        }
        elseif ($errRoll -lt 55 -and $cancelledTickets.Count -gt 0) {
            # Ingresso cancelado
            $t = $cancelledTickets | Get-Random
            $gateId = $allGateIds | Get-Random
            return @{ credentialCode = $t.code; eventId = $EventId; gateId = $gateId; idempotencyKey = [guid]::NewGuid().ToString() }
        }
        elseif ($errRoll -lt 75) {
            # Código inexistente
            $fake = "INVALIDO-" + [guid]::NewGuid().ToString("N").Substring(0, 10).ToUpper()
            $gateId = $allGateIds | Get-Random
            return @{ credentialCode = $fake; eventId = $EventId; gateId = $gateId; idempotencyKey = [guid]::NewGuid().ToString() }
        }
        else {
            # Portaria errada (ticket com setor enviado para portaria de outro setor)
            $t = if ($freshQueue.Count -gt 0) { $freshQueue.Peek() } else { $ticketsWithGate | Get-Random }
            if ($t.SectorId -and $wrongGateForSector.ContainsKey($t.SectorId)) {
                $wrongGate = $wrongGateForSector[$t.SectorId] | Get-Random
                return @{ credentialCode = $t.Code; eventId = $EventId; gateId = $wrongGate; idempotencyKey = [guid]::NewGuid().ToString() }
            } else {
                # Fallback: código inexistente
                $fake = "INVALIDO-" + [guid]::NewGuid().ToString("N").Substring(0, 10).ToUpper()
                $gateId = $allGateIds | Get-Random
                return @{ credentialCode = $fake; eventId = $EventId; gateId = $gateId; idempotencyKey = [guid]::NewGuid().ToString() }
            }
        }
    }
    else {
        # ── Cenário de APROVAÇÃO (90%) ───────────────────────────────────────
        # Pega ticket da fila e envia para a portaria CORRETA
        if ($freshQueue.Count -gt 0) {
            $t = $freshQueue.Dequeue()
            $usedTickets.Add($t)
        }
        elseif ($usedTickets.Count -gt 0) {
            if (-not $poolExhaustedLogged) {
                Write-Host ""
                Write-Host ">> Pool de tickets novos esgotado -- reciclando (pode gerar rejeicoes de limite)." -ForegroundColor Yellow
                Write-Host ""
                $script:poolExhaustedLogged = $true
            }
            $t = $usedTickets | Get-Random
        }
        else {
            $t = $ticketsWithGate | Get-Random
        }
        return @{ credentialCode = $t.Code; eventId = $EventId; gateId = $t.GateId; idempotencyKey = [guid]::NewGuid().ToString() }
    }
}

# ── Loop principal ───────────────────────────────────────────────────────────
$startTime = Get-Date
$endTime = $startTime.AddMinutes($DurationMinutes)
$waveNumber = 0

Write-Host ""
Write-Host "=== Iniciando teste de carga ===" -ForegroundColor Cyan
Write-Host "Duracao: $DurationMinutes min | Concorrencia: $Concurrency por onda | Taxa de erro: ${ErrorRatePercent}% | Intervalo: ${WaveDelayMs}ms"
Write-Host "Acompanhe o Dashboard e os Relatorios do evento no FastPass V2 Console."
Write-Host "Pressione Ctrl+C para interromper."
Write-Host ""

while ((Get-Date) -lt $endTime) {
    $waveNumber++
    $tasks = New-Object System.Collections.Generic.List[Object]

    for ($i = 0; $i -lt $Concurrency; $i++) {
        $payload = New-ScenarioPayload
        $json = $payload | ConvertTo-Json -Compress
        $content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, "application/json")
        $tasks.Add($client.PostAsync("$ApiBaseUrl/api/access/app/validate", $content))
    }

    [System.Threading.Tasks.Task]::WhenAll([System.Threading.Tasks.Task[]]$tasks.ToArray()).GetAwaiter().GetResult() | Out-Null

    foreach ($task in $tasks) {
        $resp = $task.Result
        $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        Register-Result -DecisionJson $body -StatusCode ([int]$resp.StatusCode)
    }

    if ($waveNumber % 5 -eq 0) {
        $elapsed = [math]::Round(((Get-Date) - $startTime).TotalSeconds, 0)
        $approvalPct = if ($stats.Total -gt 0) { [math]::Round($stats.Approved / $stats.Total * 100, 1) } else { 0 }
        Write-Host ("[{0,4}s] Onda {1,-5} Total: {2,-6} Aprovados: {3,-6} Rejeitados: {4,-6} Taxa: {5}%" -f $elapsed, $waveNumber, $stats.Total, $stats.Approved, $stats.Rejected, $approvalPct)
    }

    $remainingMs = ($endTime - (Get-Date)).TotalMilliseconds
    if ($remainingMs -le 0) { break }
    Start-Sleep -Milliseconds ([math]::Min($WaveDelayMs, [math]::Max(0, $remainingMs)))
}

# ── Resumo final ─────────────────────────────────────────────────────────────
$totalElapsedSec = [math]::Round(((Get-Date) - $startTime).TotalSeconds, 1)
Write-Host ""
Write-Host "=== Teste concluido em $totalElapsedSec s ($waveNumber ondas) ===" -ForegroundColor Cyan
Write-Host "Total de validacoes enviadas: $($stats.Total)"
if ($stats.Total -gt 0) {
    Write-Host ("Aprovadas:  {0} ({1}%)" -f $stats.Approved, [math]::Round($stats.Approved / $stats.Total * 100, 1))
    Write-Host ("Rejeitadas: {0} ({1}%)" -f $stats.Rejected, [math]::Round($stats.Rejected / $stats.Total * 100, 1))
}
if ($stats.HttpError -gt 0) { Write-Host "Erros de HTTP/parse: $($stats.HttpError)" -ForegroundColor Yellow }
Write-Host ""
if ($stats.Reasons.Count -gt 0) {
    Write-Host "Motivos de rejeicao:"
    $stats.Reasons.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
        Write-Host ("  {0,-75} {1}" -f $_.Key, $_.Value)
    }
}

$client.Dispose()
