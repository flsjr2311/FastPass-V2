# Protocolo NEON - Validação de Ingresso via MQTT

## Visão Geral

O protocolo NEON é responsável por validar ingressos em tempo real através da comunicação MQTT entre a API FastPass e os dispositivos de catraca (Neon).

A validação ocorre de forma **offline** (desconectada do banco principal) com aplicação imediata de regras de controle de acesso, garantindo rápida resposta nas catracas.

---

## Arquitetura

```
┌─────────────────┐         ┌─────────────────┐         ┌──────────────┐
│  Catraca MQTT   │         │  FastPass.Api   │         │   MySQL DB   │
│  (Neon Board)   │────────▶│  Worker Service │◀────────│  (fp_*..)    │
│  Identificador: │  MQTT   │  MqttTurnstile  │  Query  │              │
│  Catraca151     │         │  ValidationSvc  │         │              │
└─────────────────┘         └─────────────────┘         └──────────────┘
        │                            │
        │  Tópico: /from/access      │
        └───────────────────────────▶│
                                     │
                        ┌────────────┴─────────────┐
                        │ ValidateTicketAsync()    │
                        │ - Validar status         │
                        │ - Validar quota (uses)   │
                        │ - Validar entradas       │
                        │ - Verificar setor/porta  │
                        └────────────┬─────────────┘
                                     │
        ┌────────────────────────────│◀─────────────────────┐
        │  Tópico: /to/access        │                      │
        │                            │                      │
        ▼                            │              Resposta MQTT
   Catraca recebe                    ▼           (authorized, release)
   "authorized": true/false    ┌─────────────┐
   "release": true/false       │  Resultado  │
   e outros campos             │  Decision   │
                               └─────────────┘
```

---

## Tópicos MQTT

### Leitura (Catraca → API)

**Tópico:** `FastPass/{DEVICE_IDENTIFIER}/from/access`

**Payload (JSON):**
```json
{
  "cmd": "acc_req",
  "card": "HUXBRZ3N6REEWU498RWR"
}
```

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `cmd` | string | Comando fixo: `"acc_req"` (access request) |
| `card` | string | Código do ingresso a validar |

### Resposta (API → Catraca)

**Tópico:** `FastPass/{DEVICE_IDENTIFIER}/to/access`

**Payload (JSON):**
```json
{
  "cmd": "access",
  "authorized": true,
  "release": true,
  "ticket_id": "550e8400-e29b-41d4-a716-446655440000",
  "direction": "Entry",
  "entries_after": 1,
  "reason": null
}
```

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `cmd` | string | Comando fixo: `"access"` |
| `authorized` | boolean | `true` = ingresso válido, `false` = rejeitado |
| `release` | boolean | `true` = catraca deve liberar/girar, `false` = catraca mantém bloqueada |
| `ticket_id` | string (UUID) | ID do ingresso (se autorizado) |
| `direction` | string | `"Entry"` ou `"Exit"` (determinado pela lógica offline) |
| `entries_after` | integer | Quantidade de entradas restantes (se autorizado) |
| `reason` | string \| null | Motivo da negação (se rejeitado) |

---

## Regras de Validação

As validações ocorrem **sequencialmente** na ordem abaixo. A primeira falha retorna negação imediata:

### 1. Status do Ingresso

```csharp
if (ticket.Status != "active")
    return Denied("Ingresso inativo.");
```

**Motivo:** Apenas ingressos com status `active` podem ser utilizados.

---

### 2. Quota Geral (Uses)

```csharp
if (ticket.Uses >= ticket.MaximumUses)
    return Denied("Ingresso atingiu o limite de utilizações.");
```

**Motivo:** Cada ingresso tem um número máximo de vezes que pode ser utilizado (independente de entradas/saídas).

**Exemplo:** Ingresso com `maximum_uses = 1` e `uses = 1` já atingiu o limite.

---

### 3. Quantidade de Entradas (Direction = Entry)

```csharp
if (direction == Entry && ticket.EntriesUsed >= ticket.MaximumEntries)
    return Denied("Ingresso já utilizado.");
```

**Motivo:** Apenas para tentativas de **entrada**. Cada ingresso tem um número máximo de entradas permitidas.

**Exemplo:** Ingresso com `maximum_entries = 1` e `entries_used = 1` não permite mais entradas.

---

### 4. Vínculo Portaria-Setor (Gate-Sector)

```csharp
if (!await HasActiveGateSectorAsync(
    eventId, gateId, ticket.SectorId, direction))
    return Denied("Portaria sem autorização para o setor deste ingresso.");
```

**Motivo:** O ingresso deve estar vinculado a um setor, e esse setor deve ter um vínculo **ativo** com a portaria (gate) e a direção (Entry/Exit).

**Tabela:** `fp_gate_sectors` com:
- `event_id = ingresso.event_id`
- `gate_id = comando.gate_id`
- `sector_id = ingresso.sector_id`
- `direction = "Entry"` ou `"Exit"`
- `active = 1`

---

### 5. Política de Acesso (Access Policy)

```csharp
if (!await HasTicketAuthorizationAsync(
    eventId, gateId, ticket.TicketTypeId, direction))
    return Denied("Ingresso sem autorização para este evento, portaria, setor ou direção.");
```

**Motivo:** Verifica se há uma **política de acesso** que autoriza este tipo de ingresso nesta portaria/direção.

**Tabela:** `fp_access_policies` com:
- `event_id = ingresso.event_id`
- `active = 1`
- `direction = "Entry"` ou `"Exit"`
- `allows = 1` (permite acesso)
- Campos opcionais: `gate_id`, `sector_id`, `ticket_type_id`, `batch_id`

---

### 6. Presença Registrada (Direction = Exit)

```csharp
if (direction == Exit && ticket.PeopleInside <= 0)
    return Denied("Não há presença registrada para este ingresso.");
```

**Motivo:** Para tentativas de **saída**, deve haver presença registrada (catraca em modo `EntryAndExitValidated`).

---

## Campos da Tabela `fp_tickets`

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `id` | CHAR(36) | UUID único do ingresso |
| `event_id` | CHAR(36) | Evento ao qual pertence |
| `ticket_type_id` | CHAR(36) | Tipo de ingresso (VIP, Camarote, Pista, etc) |
| `sector_id` | CHAR(36) | Setor do ingresso (Camarote, Pista, etc) |
| `code` | VARCHAR(60) | **Código único** do ingresso (lido na catraca) |
| `status` | VARCHAR(20) | `"active"` ou `"inactive"` |
| `maximum_uses` | INT | Máximo de vezes que pode ser utilizado |
| `uses` | INT | Quantas vezes foi utilizado (incremental) |
| `maximum_entries` | INT | Máximo de entradas permitidas |
| `entries_used` | INT | Quantas entradas foram realizadas |
| `people_inside` | INT | Presença dentro do evento (0 = fora, >0 = dentro) |
| `batch_id` | CHAR(36) | Lote ao qual pertence (opcional) |
| `batch_name` | VARCHAR(255) | Nome do lote de importação |
| `external_id` | VARCHAR(255) | ID externo (do sistema original) |
| `metadata_json` | JSON | Metadados adicionais |
| `created_at` | DATETIME(6) | Data/hora de criação |
| `updated_at` | DATETIME(6) | Data/hora da última atualização |

---

## Fluxo Completo de Validação

```
1. Catraca lê código do ingresso
   └─▶ Publica em FastPass/Catraca151/from/access
       {"cmd": "acc_req", "card": "HUXBRZ3N6REEWU498RWR"}

2. API FastPass.Worker recebe mensagem
   └─▶ MqttTurnstileService injeta em fila

3. ValidateTicketAsync() executa regras sequenciais:
   ├─ Verificar status = "active"? ✓
   ├─ Verificar uses < maximum_uses? ✓
   ├─ Se Entry: verificar entries_used < maximum_entries? ✓
   ├─ Verificar vínculo gate-sector-direction? ✓
   ├─ Verificar access_policy allows? ✓
   └─ Se Exit: verificar people_inside > 0? ✓

4. Se todas as validações passarem:
   ├─ UPDATE fp_tickets SET:
   │  ├─ entries_used = entries_used + 1 (se Entry)
   │  ├─ uses = uses + 1
   │  ├─ people_inside = people_inside + 1 (se Entry + tracking)
   │  └─ people_inside = people_inside - 1 (se Exit)
   └─ Retorna authorized: true, release: true

5. Se alguma validação falhar:
   └─ Retorna authorized: false, release: false
      com motivo da negação

6. API publica resposta
   └─▶ Publica em FastPass/Catraca151/to/access
       {"cmd": "access", "authorized": true, "release": true, ...}

7. Catraca recebe resposta
   └─▶ Se release: true → gira/libera acesso
       Se release: false → mantém bloqueada
```

---

## Configuração MQTT (appsettings.json)

```json
{
  "Mqtt": {
    "Enabled": true,
    "Host": "127.0.0.1",
    "Port": 1883,
    "ClientId": "FastPass.Worker",
    "Username": null,
    "Password": null,
    "FacilityId": "FastPass",
    "TopicPrefix": "FastPass"
  }
}
```

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `Enabled` | boolean | Ativa/desativa o worker MQTT |
| `Host` | string | Host do broker MQTT |
| `Port` | integer | Porta do broker MQTT |
| `ClientId` | string | ID do cliente na conexão MQTT |
| `Username` | string \| null | Usuário para autenticação (opcional) |
| `Password` | string \| null | Senha para autenticação (opcional) |
| `FacilityId` | string | ID da instalação (identificador do local) |
| `TopicPrefix` | string | Prefixo de tópicos (`{TopicPrefix}/{DEVICE_ID}/from/access`) |

---

## Exemplo de Sucesso

**Requisição (Catraca):**
```json
{
  "cmd": "acc_req",
  "card": "HUXBRZ3N6REEWU498RWR"
}
```

**Resposta (API):**
```json
{
  "cmd": "access",
  "authorized": true,
  "release": true,
  "ticket_id": "550e8400-e29b-41d4-a716-446655440000",
  "direction": "Entry",
  "entries_after": 4,
  "reason": null
}
```

**Resultado:** ✅ Catraca gira/libera acesso

---

## Exemplo de Falha (Setor Errado)

**Requisição (Catraca em Camarote):**
```json
{
  "cmd": "acc_req",
  "card": "TESTESETORERRADO001"
}
```

**Resposta (API):**
```json
{
  "cmd": "access",
  "authorized": false,
  "release": false,
  "ticket_id": null,
  "direction": null,
  "entries_after": null,
  "reason": "Portaria sem autorização para o setor deste ingresso."
}
```

**Resultado:** ❌ Catraca mantém bloqueada

---

## Exemplo de Falha (Quota Atingida)

**Requisição:**
```json
{
  "cmd": "acc_req",
  "card": "TESTEQUOTAATINGIDA01"
}
```

**Resposta:**
```json
{
  "cmd": "access",
  "authorized": false,
  "release": false,
  "ticket_id": null,
  "direction": null,
  "entries_after": null,
  "reason": "Ingresso atingiu o limite de utilizações."
}
```

**Resultado:** ❌ Catraca mantém bloqueada

---

## Implementação

### Arquivo Principal
- **`src/FastPass.Infrastructure/Access/MySqlAccessValidationService.cs`**
  - Método: `ValidateTicketAsync()`
  - Aplica todas as 6 regras de validação

### Serviço MQTT Worker
- **`src/FastPass.Worker/Mqtt/MqttTurnstileService.cs`**
  - Conecta ao broker MQTT
  - Assina tópicos `/from/access`
  - Publica respostas em `/to/access`

### Registros em Program.cs
```csharp
// API
builder.Services.AddSingleton<IAccessValidationService, MySqlAccessValidationService>();
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));
builder.Services.AddHostedService<MqttTurnstileService>();
```

---

## Troubleshooting

### Catraca recebe sempre "authorized: false"

1. Verificar se ingresso existe no banco: `SELECT * FROM fp_tickets WHERE code = 'XXX'`
2. Verificar se status = `"active"`
3. Verificar vínculo gate-sector: `SELECT * FROM fp_gate_sectors WHERE gate_id = '...' AND sector_id = '...'`
4. Verificar access_policy: `SELECT * FROM fp_access_policies WHERE event_id = '...' AND direction = 'Entry'`

### API não conecta ao broker MQTT

1. Verificar se broker está rodando: `telnet localhost 1883`
2. Verificar configuração em `appsettings.json` (Host, Port)
3. Verificar logs da API: `dotnet run` (modo debug)

### Catraca está recebendo respostas genéricas

1. Verificar se tópico está correto: `FastPass/{DEVICE_IDENTIFIER}/to/access`
2. Verificar se device foi registrado: `SELECT * FROM fp_devices WHERE identifier = 'Catraca151'`
3. Verificar se device está vinculado ao gate certo

---

## Histórico de Versões

| Versão | Data | Mudanças |
|--------|------|----------|
| 1.0 | 27/08/2026 | Protocolo inicial NEON. Validação offline com 6 regras. |

---

**Última atualização:** 27 de agosto de 2026  
**Status:** ✅ Produção
