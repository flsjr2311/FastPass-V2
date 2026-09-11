# 🧪 Teste End-to-End: TurnstileOperationMode (Phase 2 UX)

## Preparação

### Pré-requisitos
- [ ] API rodando: http://localhost:5088
- [ ] Frontend rodando: http://localhost:5173
- [ ] Worker MQTT rodando e conectado ao broker (127.0.0.1:1883)
- [ ] Evento "Evento teste alpha" criado
- [ ] Portarias: Camarote, Open Bar, Pista, Vip
- [ ] Devices cadastrados: Catraca151, Catraca157, Catraca156

### Login
- Username: `admin`
- Password: `Admin@1234`

---

## Testes API (Verificados ✅)

### ✅ Teste 1: Migration 030 Executada
```
Status: ✅ Completo
- fp_event_gates.operation_mode criada (DEFAULT 'Active')
- fp_devices.operation_mode criada (NULL = inherit)
- Índices criados para performance
```

### ✅ Teste 2: GET /api/events/{eventId}/gates
```bash
GET http://localhost:5088/api/events/ae7d5cdd-afd7-4b3c-98cc-10b2adca3589/gates

Response esperado:
[
  {
    "id": "148f1fa1-d4be-44e1-8697-769d54d82ac2",
    "name": "Camarote",
    "operationMode": "Free",       # ← Campo novo!
    "turnstileMode": "Free"        # ← Campo novo!
  }
  ...
]

Status: ✅ Completo
```

### ✅ Teste 3: GET /api/events/{eventId}/devices
```bash
GET http://localhost:5088/api/events/ae7d5cdd-afd7-4b3c-98cc-10b2adca3589/devices

Response esperado:
[
  {
    "id": "7cc33399-1fb1-4b1b-b62c-36ff7b0bb66b",
    "name": "Catraca 151 - Neon",
    "operationMode": "Blocked",           # ← Gate mode ou override
    "operationModeOverride": "Blocked"    # ← Device override (Blocked neste teste)
  }
  ...
]

Status: ✅ Completo
```

### ✅ Teste 4: PUT /api/events/{eventId}/gates/{gateId}/turnstile-mode
```bash
PUT http://localhost:5088/api/events/ae7d5cdd-afd7-4b3c-98cc-10b2adca3589/gates/148f1fa1-d4be-44e1-8697-769d54d82ac2/turnstile-mode

Body:
{
  "turnstileMode": "Free"
}

Response esperado:
{
  "id": "148f1fa1-d4be-44e1-8697-769d54d82ac2",
  "name": "Camarote",
  "operationMode": "Free",      # ← Atualizado!
  "turnstileMode": "Free"
}

Status: ✅ Completo (Camarote mudada para Free)
```

### ✅ Teste 5: PUT /api/events/{eventId}/gates/{gateId}/devices/{deviceId}/operation-mode
```bash
PUT http://localhost:5088/api/events/ae7d5cdd-afd7-4b3c-98cc-10b2adca3589/gates/148f1fa1-d4be-44e1-8697-769d54d82ac2/devices/7cc33399-1fb1-4b1b-b62c-36ff7b0bb66b/operation-mode

Body:
{
  "operationMode": "Blocked"
}

Response esperado:
{
  "id": "7cc33399-1fb1-4b1b-b62c-36ff7b0bb66b",
  "name": "Catraca 151 - Neon",
  "operationMode": "Blocked",           # ← Gate mode
  "operationModeOverride": "Blocked"    # ← Device override (override)
}

Status: ✅ Completo (Device override para Blocked, gate está Free)
```

### ✅ Teste 6: Display Messages Verificadas
```
Modo ACTIVE:
  Mensagem LCD: "PASSE SEU\nINGRESSO"
  Pictograma:   None (apagado)
  Ação braço:   None (aguarda validação)

Modo FREE:
  Mensagem LCD: "LIBERADA\nENTRE"
  Pictograma:   GreenArrowEntry (verde, bidirecional)
  Ação braço:   Unlock

Modo BLOCKED:
  Mensagem LCD: "BLOQUEADA\nCADEADO"
  Pictograma:   RedCross (cruz vermelha)
  Ação braço:   KeepLocked

Status: ✅ Completo (verificado em MqttTurnstileService.SendInitialModeMessageAsync)
```

---

## Testes Web UI (A Executar)

### Teste 7: Web UI - Alterar Modo da Portaria
```
Passos:
1. Acesse http://localhost:5173
2. Login: admin / Admin@1234
3. Navegue para: Eventos → [Evento teste alpha] → Portarias
4. Clique em: "Camarote"
5. Localize: "Modo da Catraca" ou "Turnstile Mode"
6. Selecione: FREE
7. Clique: Salvar

Verificações:
- [ ] Portaria "Camarote" mostra modo "FREE"
- [ ] API retorna operationMode = "Free"
- [ ] MQTT publica mensagem para catraca com "LIBERADA\nENTRE"
- [ ] Catraca física exibe mensagem (se conectada)

Status: ⏳ Aguardando teste na Web UI
```

### Teste 8: Web UI - Device Override
```
Passos:
1. Localize: "Dispositivos" na mesma portaria ou em tela separada
2. Selecione device: "Catraca 151 - Neon"
3. Localize: "Override de Modo" ou "Device Operation Mode"
4. Selecione: BLOCKED
5. Clique: Salvar

Verificações:
- [ ] Device "Catraca 151" mostra override "BLOCKED"
- [ ] API retorna operationModeOverride = "Blocked"
- [ ] Portaria = "FREE" mas device = "BLOCKED" → device prevalece
- [ ] MQTT publica mensagem específica: "BLOQUEADA\nCADEADO"
- [ ] Catraca física recebe modo BLOCKED (se conectada)

Status: ⏳ Aguardando teste na Web UI
```

### Teste 9: Web UI - Reset para ACTIVE
```
Passos:
1. Portaria "Camarote" → Modo: ACTIVE (padrão)
2. Device "Catraca 151" → Override: (limpar/null)
3. Salvar

Verificações:
- [ ] Portaria volta a ACTIVE
- [ ] Device herda modo ACTIVE da portaria (override = null)
- [ ] MQTT publica mensagem: "PASSE SEU\nINGRESSO"
- [ ] Catraca exibe novo modo

Status: ⏳ Aguardando teste na Web UI
```

### Teste 10: MQTT Keepalive em Tempo Real
```
Passos:
1. Conecte catraca física USB/Ethernet ou simule via mosquitto_pub:
   mosquitto_pub -h 127.0.0.1 -t "FastPass/Catraca151/from/keepalive" -m '{}'

2. Aguarde 2-3 segundos para processar

Verificações:
- [ ] Worker recebe keepalive no log
- [ ] Worker resolve gate + device → effective mode
- [ ] Worker publica mensagem LCD no tópico to/
- [ ] Catraca exibe mensagem correta (ACTIVE/FREE/BLOCKED)
- [ ] Pictograma correto exibido (None/GreenArrowEntry/RedCross)
- [ ] Logs do Worker mostram: "Enviada mensagem inicial de modo para 'Catraca151': [Mode]"

Status: ⏳ Aguardando teste real com catraca
```

---

## Resumo de Resultados

### APIs (✅ 6/6 Testes)
| Teste | Status | Resultado |
|-------|--------|-----------|
| 1. Migration 030 | ✅ | Colunas criadas e índices OK |
| 2. GET /gates | ✅ | operationMode presente |
| 3. GET /devices | ✅ | operationMode + operationModeOverride presente |
| 4. PUT /gates/{id}/turnstile-mode | ✅ | Gate atualizada (Free) |
| 5. PUT /devices/{id}/operation-mode | ✅ | Device override (Blocked) |
| 6. Display Messages | ✅ | Mensagens verificadas no código |

### Web UI (⏳ 0/4 Testes)
| Teste | Status | Resultado |
|-------|--------|-----------|
| 7. UI - Alterar Gate Mode | ⏳ | Aguardando execução |
| 8. UI - Device Override | ⏳ | Aguardando execução |
| 9. UI - Reset ACTIVE | ⏳ | Aguardando execução |
| 10. MQTT Keepalive Real | ⏳ | Aguardando execução |

---

## Notas de Engenharia

### Hierarquia de Modos
```
Gate Mode (default)
    ↓
Device Override (null = herda)
    ↓
Effective Mode (usado no MQTT)
```

### Resolução no Código
```csharp
var effectiveMode = device.OperationModeOverride ?? device.OperationMode;
```

### Mensagens LCD (2 linhas, sem word breaks)
- **ACTIVE**: "PASSE SEU" + "INGRESSO" = display aguardando ticket
- **FREE**: "LIBERADA" + "ENTRE" = display libera ambos lados
- **BLOCKED**: "BLOQUEADA" + "CADEADO" = display bloqueia

### Pictogramas (Monochrome Display)
- **None**: Display apagado (não exibe símbolo)
- **GreenArrowEntry**: Seta verde (entrada/saída liberada)
- **RedCross**: Cruz vermelha (bloqueada, não gira)

---

## Comandos Úteis

### Verificar logs do Worker
```powershell
# Terminal onde o Worker está rodando
# Procure por: "Enviada mensagem inicial de modo para 'Catraca[X]'"
```

### Simular keepalive da catraca
```bash
mosquitto_pub -h 127.0.0.1 -p 1883 -t "FastPass/Catraca151/from/keepalive" -m '{"uptime": 12345}'
```

### Consultar mode efetivo via API
```bash
# GET gates
curl -X GET "http://localhost:5088/api/events/ae7d5cdd-afd7-4b3c-98cc-10b2adca3589/gates" \
  -H "Cookie: fp_token=[TOKEN_AQUI]"

# GET devices
curl -X GET "http://localhost:5088/api/events/ae7d5cdd-afd7-4b3c-98cc-10b2adca3589/devices" \
  -H "Cookie: fp_token=[TOKEN_AQUI]"
```

---

**Criado em**: 2026-08-27  
**Fase**: Phase 2 UX - TurnstileOperationMode  
**Status**: End-to-End API ✅ | Web UI & MQTT Real ⏳
