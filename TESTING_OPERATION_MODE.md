# Testing TurnstileOperationMode (Active/Free/Blocked)

## 1. Obter dados válidos do banco

Execute no MySQL:

```sql
-- Buscar um evento ativo com portarias e dispositivos
SELECT 
    e.id as event_id,
    e.name as event_name,
    g.id as gate_id,
    g.name as gate_name,
    g.code as gate_code,
    d.id as device_id,
    d.name as device_name,
    d.identifier,
    d.operation_mode as device_override,  -- NULL = herda da portaria
    eg.turnstile_mode as gate_default     -- modo padrão da portaria
FROM fp_events e
INNER JOIN fp_event_gates eg ON eg.event_id = e.id AND eg.active = 1
INNER JOIN fp_gates g ON g.id = eg.gate_id AND g.active = 1
INNER JOIN fp_devices d ON d.gate_id = g.id AND d.active = 1
WHERE e.status = 'Running'
LIMIT 1;
```

Isso vai retornar algo como:
```
event_id: 12345678-1234-1234-1234-123456789012
gate_id:  87654321-4321-4321-4321-210987654321
device_id: aaaabbbb-cccc-dddd-eeee-ffff00001111
```

## 2. Testar o novo endpoint

Use os IDs obtidos acima para testar:

### Modo: FREE (Liberada)
```
PUT http://localhost:5088/api/events/12345678-1234-1234-1234-123456789012/gates/87654321-4321-4321-4321-210987654321/devices/aaaabbbb-cccc-dddd-eeee-ffff00001111/operation-mode

Content-Type: application/json

{
  "operationMode": "Free"
}
```

**Resultado esperado:**
```json
{
  "id": "aaaabbbb-cccc-dddd-eeee-ffff00001111",
  "eventId": "12345678-1234-1234-1234-123456789012",
  "gateId": "87654321-4321-4321-4321-210987654321",
  "gateName": "Entrada Principal",
  "name": "Catraca 156",
  "identifier": "MQTT_156",
  "operationMode": "Free",
  "active": true
}
```

A catraca agora rodará **livremente** sem validar ingressos!

---

### Modo: BLOCKED (Bloqueada)
```
PUT http://localhost:5088/api/events/12345678-1234-1234-1234-123456789012/gates/87654321-4321-4321-4321-210987654321/devices/aaaabbbb-cccc-dddd-eeee-ffff00001111/operation-mode

Content-Type: application/json

{
  "operationMode": "Blocked"
}
```

**Resultado esperado:**
- `operationMode`: "Blocked"

A catraca agora estará **travada** (não gira)!

---

### Modo: ACTIVE (Normal - validação)
```
PUT http://localhost:5088/api/events/12345678-1234-1234-1234-123456789012/gates/87654321-4321-4321-4321-210987654321/devices/aaaabbbb-cccc-dddd-eeee-ffff00001111/operation-mode

Content-Type: application/json

{
  "operationMode": "Active"
}
```

**Resultado esperado:**
- `operationMode`: "Active"

A catraca voltará a **validar ingressos**!

---

### Remover o override (voltar a herdar a portaria)
```
PUT http://localhost:5088/api/events/EVENT_ID/gates/GATE_ID/devices/DEVICE_ID/operation-mode

Content-Type: application/json

{
  "operationMode": null
}
```

**Resultado esperado:**
- `operationModeOverride`: `null` (gravado como `NULL` em `fp_devices.operation_mode`)
- `operationMode`: passa a refletir o `turnstile_mode` da portaria

String vazia (`""`) tem o mesmo efeito. Qualquer outro valor fora de
`Active|Free|Blocked` devolve `400`.

---

## 3. Verificar o comportamento na catraca

Após mudar o modo:

1. **FREE Mode**: 
   - Aproxime qualquer coisa do leitor
   - Catraca gira **sem validar**
   - Display mostra: `CATRACA LIVRE`

2. **BLOCKED Mode**:
   - Tente usar qualquer ingresso
   - Catraca **não gira**
   - Display mostra: `CATRACA BLOQUEADA`

3. **ACTIVE Mode**:
   - Catraca valida ingresso normalmente
   - Se autorizado: libera e mostra `ACESSO CONCEDIDO`
   - Se não autorizado: nega e mostra `ACESSO NEGADO`

---

## 4. Testar via cURL

```bash
# Modo FREE
curl -X PUT \
  -H "Content-Type: application/json" \
  -d '{"operationMode":"Free"}' \
  http://localhost:5088/api/events/EVENT_ID/gates/GATE_ID/devices/DEVICE_ID/operation-mode

# Modo BLOCKED
curl -X PUT \
  -H "Content-Type: application/json" \
  -d '{"operationMode":"Blocked"}' \
  http://localhost:5088/api/events/EVENT_ID/gates/GATE_ID/devices/DEVICE_ID/operation-mode

# Modo ACTIVE
curl -X PUT \
  -H "Content-Type: application/json" \
  -d '{"operationMode":"Active"}' \
  http://localhost:5088/api/events/EVENT_ID/gates/GATE_ID/devices/DEVICE_ID/operation-mode
```

---

## 5. Verificar estado atual da catraca

```
GET http://localhost:5088/api/events/EVENT_ID/devices?gateId=GATE_ID

Response:
[
  {
    "id": "DEVICE_ID",
    "name": "Catraca 156",
    "operationMode": "Free",  ← Mostra o modo atual
    ...
  }
]
```

---

## Notas importantes:

- ✅ A mudança é **instantânea** (não precisa reiniciar a catraca)
- ✅ A próxima leitura de ingresso respeitará o novo modo
- ✅ O banco armazena o override em `fp_devices.operation_mode` (`NULL` = herda da portaria)
- ✅ O padrão da portaria fica em `fp_event_gates.turnstile_mode`
- ✅ `operationMode` na resposta é o modo **efetivo** (`override ?? padrão da portaria`);
  o override em si vem em `operationModeOverride`
- ✅ Requer permissão: `dispositivo.gerenciar`
- ✅ Modo padrão ao criar device: herda a portaria (sem override)

---

## Para usar em produção:

1. As migrations são aplicadas automaticamente no startup da API. Para rodar manualmente:
   ```sql
   SOURCE src/FastPass.Infrastructure/Database/Migrations/030_add_turnstile_operation_mode.sql;
   SOURCE src/FastPass.Infrastructure/Database/Migrations/036_split_gate_turnstile_mode.sql;
   ```

   A 036 é obrigatória: sem ela o modo da catraca é gravado em
   `fp_event_gates.operation_mode` e a validação de acesso da portaria passa a falhar com
   `"Modo operacional inválido configurado para a portaria."`

2. Reinicie o FastPass.Api

3. Use a API conforme descrito acima

