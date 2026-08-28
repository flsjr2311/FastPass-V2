<#
.SYNOPSIS
    Teste de carga (stress test) de validações de acesso do FastPass.

.DESCRIPTION
    Dispara validações de acesso concorrentes contra a API FastPass (POST /api/access/app/validate),
    simulando o fluxo real de uma portaria: a maioria das tentativas é uma aprovação válida, e uma
    fração configurável (padrão 10%) simula erros esperados de operação:
      - Ingresso já usado (reenvia um código que a própria sessão de teste já aprovou)
      - Ingresso cancelado (usa um ticket com status "cancelled" já existente no evento)
      - Ingresso inexistente / de outro evento (gera um código aleatório que não existe)
      - Portaria sem autorização para o setor (usa uma combinação gate/sector fora da matriz)

    Ideal para deixar rodando em background enquanto se acompanha o Dashboard e os Relatórios
    (telas "Dashboard" e "Relatórios" do FastPass V2 Console) em tempo real.

    Roda inteiramente em PowerShell 5.1 (usa System.Net.Http.HttpClient com Task.WhenAll para
    concorrência real, sem depender de ForEach-Object -Parallel do PowerShell 7).

.PARAMETER ApiBaseUrl
    URL base da API FastPass. Padrão: http://127.0.0.1:5104

.PARAMETER EventId
    GUID do evento cujos ingressos serão usados no teste. Obrigatório.
    ATENÇÃO: o script consome (marca como usados) tickets ATIVOS reais do evento informado.
    Prefira rodar contra um evento de teste/homologação, não um evento de produção em operação.

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
    Pausa entre ondas de requisições concorrentes, em milissegundos. Controla o ritmo
    (requests/segundo) do teste. Padrão: 1000

.EXAMPLE
    .\Invoke-AccessValidationLoadTest.ps1 -EventId "ae7d5cdd-afd7-4b3c-98cc-10b2adca3589"

    Roda o teste padrão: 12 minutos, 6 validações simultâneas por onda, 10% de erro.

.EXAMPLE
    .\Invoke-AccessValidationLoadTest.ps1 -EventId "ae7d5cdd-afd7-4b3c-98cc-10b2adca3589" -DurationMinutes 15 -Concurrency 8 -ErrorRatePercent 15

    Roda 15 minutos com 8 validações simultâneas e 15% de taxa de erro.

.NOTES
    Salvo em fastpass_moderno/tools/load-test para reuso em futuras sessões.
    Pré-requisitos antes de rodar:
      - API FastPass rodando e acessível em -ApiBaseUrl
      - O evento informado deve ter ao menos 1 ticket ativo e ao menos 1 associação
        ativa de portaria x setor (direção Entry) cadastrada em Configuração > Portarias e Setores.
    Para interromper antes do tempo, use Ctrl+C — o resumo parcial não é impresso nesse caso
    (rode em uma janela dedicada ou redirecione a saída para um arquivo com Tee-Object se quiser log).
#>

param(
    [string]$ApiBaseUrl = "http://127.0.0.1:5104",
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

# ── HttpClient com cookie container compartilhado (mantém a sessão entre chamadas) ──
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
    # Sempre devolve um array (mesmo com 0 ou 1 item), sem o efeito de aninhamento que
    # ocorre quando o chamador envolve a invocação com @(). Ver comentário no ponto de uso.
    param([string]$Path)
    $resp = $client.GetAsync("$ApiBaseUrl$Path").GetAwaiter().GetResult()
    $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $resp.IsSuccessStatusCode) { throw "GET $Path falhou: $($resp.StatusCode) - $body" }
    $parsed = ConvertFrom-Json $body
    if ($null -eq $parsed) { return [Array]::CreateInstance([object], 0) }
    if ($parsed -is [Array]) { return $parsed }
    $arr = [Array]::CreateInstance([object], 1)
    $arr[0] = $parsed
    return $arr
}

# ── Login ────────────────────────────────────────────────────────────────────────
Write-Host "Autenticando como '$Username'..."
$loginResult = Invoke-JsonPost "/api/auth/login" @{ username = $Username; password = $Password }
if ($loginResult.StatusCode -ne 200) {
    throw "Falha no login: HTTP $($loginResult.StatusCode) - $($loginResult.Body)"
}
Write-Host "Login OK."

# ── Coleta de dados do evento ────────────────────────────────────────────────────
Write-Host "Carregando tickets, portarias e matriz do evento $EventId..."
# IMPORTANTE: NAO envolver a chamada de Invoke-JsonGet com @() aqui -- quando a função
# devolve um array grande pelo pipeline, @(chamada-de-funcao) aninha o array (Object[][]),
# fazendo .Count reportar 1 em vez do total real. Atribuição direta preserva o array corretamente.
$tickets = Invoke-JsonGet "/api/events/$EventId/tickets"
$gates = Invoke-JsonGet "/api/events/$EventId/gates"
$gateSectors = Invoke-JsonGet "/api/events/$EventId/gate-sectors"

$activeTickets = @($tickets | Where-Object { $_.status -eq 'active' })
$cancelledTickets = @($tickets | Where-Object { $_.status -ne 'active' })
$validPairs = @($gateSectors | Where-Object { $_.active -and $_.direction -eq 'Entry' } | ForEach-Object {
        [PSCustomObject]@{ GateId = $_.gateId; SectorId = $_.sectorId; GateName = $_.gateName; SectorName = $_.sectorName }
    })

if ($activeTickets.Count -eq 0) { throw "Nenhum ticket ativo encontrado no evento $EventId." }
if ($validPairs.Count -eq 0) { throw "Nenhuma associacao portaria x setor ativa (direcao Entry) encontrada. Configure a matriz em Portarias e Setores antes de rodar o teste." }

Write-Host "Tickets ativos: $($activeTickets.Count) | Cancelados: $($cancelledTickets.Count) | Pares portaria x setor validos: $($validPairs.Count)"

# Combinacoes de portaria x setor SEM associacao ativa (usadas para simular "portaria errada")
$allGateIds = @($gates | ForEach-Object { $_.id })
$allSectorIds = @($gateSectors | ForEach-Object { $_.sectorId } | Select-Object -Unique)
$invalidPairs = New-Object System.Collections.Generic.List[Object]
foreach ($g in $allGateIds) {
    foreach ($s in $allSectorIds) {
        $isValid = $validPairs | Where-Object { $_.GateId -eq $g -and $_.SectorId -eq $s }
        if (-not $isValid) { $invalidPairs.Add([PSCustomObject]@{ GateId = $g; SectorId = $s }) }
    }
}
if ($invalidPairs.Count -eq 0) {
    Write-Warning "Nao ha combinacoes de portaria x setor invalidas disponiveis -- o cenario 'portaria errada' sera substituido por 'codigo inexistente'."
}

# ── Filas de tickets (embaralhadas) ──────────────────────────────────────────────
$freshQueue = New-Object System.Collections.Generic.Queue[Object]
foreach ($t in ($activeTickets | Get-Random -Count $activeTickets.Count)) { $freshQueue.Enqueue($t) }
$usedTickets = New-Object System.Collections.Generic.List[Object]
$poolExhaustedLogged = $false

# ── Contadores ───────────────────────────────────────────────────────────────────
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
        }
        else {
            $stats.Rejected++
            $reason = if ($parsed.reason) { $parsed.reason } else { "Motivo nao informado" }
            if (-not $stats.Reasons.ContainsKey($reason)) { $stats.Reasons[$reason] = 0 }
            $stats.Reasons[$reason]++
        }
    }
    catch {
        $stats.HttpError++
    }
}

function New-ScenarioPayload {
    $roll = Get-Random -Minimum 0 -Maximum 100
    if ($roll -lt $ErrorRatePercent) {
        $errRoll = Get-Random -Minimum 0 -Maximum 100
        if ($errRoll -lt 40 -and $usedTickets.Count -gt 0) {
            # Ingresso ja usado: reenvia um codigo que esta sessao ja aprovou antes
            $t = $usedTickets | Get-Random
            $pair = $validPairs | Get-Random
            return @{ credentialCode = $t.code; eventId = $EventId; gateId = $pair.GateId; sectorId = $pair.SectorId; direction = "Entry"; idempotencyKey = [guid]::NewGuid().ToString(); channel = "App" }
        }
        elseif ($errRoll -lt 65 -and $cancelledTickets.Count -gt 0) {
            # Ingresso cancelado (lista negra)
            $t = $cancelledTickets | Get-Random
            $pair = $validPairs | Get-Random
            return @{ credentialCode = $t.code; eventId = $EventId; gateId = $pair.GateId; sectorId = $pair.SectorId; direction = "Entry"; idempotencyKey = [guid]::NewGuid().ToString(); channel = "App" }
        }
        elseif ($errRoll -lt 85 -or $invalidPairs.Count -eq 0) {
            # Codigo inexistente (equivalente a ingresso de outro evento)
            $fake = "INVALIDO-" + [guid]::NewGuid().ToString("N").Substring(0, 10).ToUpper()
            $pair = $validPairs | Get-Random
            return @{ credentialCode = $fake; eventId = $EventId; gateId = $pair.GateId; sectorId = $pair.SectorId; direction = "Entry"; idempotencyKey = [guid]::NewGuid().ToString(); channel = "App" }
        }
        else {
            # Portaria/setor sem associacao ativa na matriz
            $t = if ($freshQueue.Count -gt 0) { $freshQueue.Peek() } else { $activeTickets | Get-Random }
            $pair = $invalidPairs | Get-Random
            return @{ credentialCode = $t.code; eventId = $EventId; gateId = $pair.GateId; sectorId = $pair.SectorId; direction = "Entry"; idempotencyKey = [guid]::NewGuid().ToString(); channel = "App" }
        }
    }
    else {
        # Aprovacao valida
        if ($freshQueue.Count -gt 0) {
            $t = $freshQueue.Dequeue()
            $usedTickets.Add($t)
        }
        elseif ($usedTickets.Count -gt 0) {
            if (-not $poolExhaustedLogged) {
                Write-Host ""
                Write-Host ">> Pool de tickets novos esgotado -- reciclando tickets ja aprovados (deve gerar 'Limite de entradas atingido' com mais frequencia a partir de agora)." -ForegroundColor Yellow
                Write-Host ""
                $script:poolExhaustedLogged = $true
            }
            $t = $usedTickets | Get-Random
        }
        else {
            $t = $activeTickets | Get-Random
        }
        $pair = $validPairs | Get-Random
        return @{ credentialCode = $t.code; eventId = $EventId; gateId = $pair.GateId; sectorId = $pair.SectorId; direction = "Entry"; idempotencyKey = [guid]::NewGuid().ToString(); channel = "App" }
    }
}

# ── Loop principal ───────────────────────────────────────────────────────────────
$startTime = Get-Date
$endTime = $startTime.AddMinutes($DurationMinutes)
$waveNumber = 0

Write-Host ""
Write-Host "=== Iniciando teste de carga ===" -ForegroundColor Cyan
Write-Host "Duracao: $DurationMinutes min | Concorrencia: $Concurrency por onda | Taxa de erro: ${ErrorRatePercent}% | Intervalo entre ondas: ${WaveDelayMs}ms"
Write-Host "Acompanhe o Dashboard e os Relatorios do evento no FastPass V2 Console enquanto o teste roda."
Write-Host "Pressione Ctrl+C para interromper antes do tempo previsto."
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
        Write-Host ("[{0,4}s] Onda {1,-5} Total: {2,-6} Aprovados: {3,-6} Rejeitados: {4,-6} ErrosHTTP: {5}" -f $elapsed, $waveNumber, $stats.Total, $stats.Approved, $stats.Rejected, $stats.HttpError)
    }

    $remainingMs = ($endTime - (Get-Date)).TotalMilliseconds
    if ($remainingMs -le 0) { break }
    Start-Sleep -Milliseconds ([math]::Min($WaveDelayMs, [math]::Max(0, $remainingMs)))
}

# ── Resumo final ─────────────────────────────────────────────────────────────────
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
