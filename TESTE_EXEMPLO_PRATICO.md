# 🧪 Exemplo Prático de Teste - TurnstileOperationMode

## Assumindo que você tem:
- **Evento ID**: `550e8400-e29b-41d4-a716-446655440000`
- **Portaria (Gate) ID**: `6ba7b810-9dad-11d1-80b4-00c04fd430c8`
- **Catraca (Device) ID**: `6ba7b811-9dad-11d1-80b4-00c04fd430c8`

---

## 🧪 Teste 1: Mudar catraca para MODO LIVRE (FREE)

### Request:
```http
PUT http://localhost:5088/api/events/550e8400-e29b-41d4-a716-446655440000/gates/6ba7b810-9dad-11d1-80b4-00c04fd430c8/devices/6ba7b811-9dad-11d1-80b4-00c04fd430c8/operation-mode
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN_HERE

{
  "operationMode": "Free"
}
```

### Response (200 OK):
```json
{
  "id": "6ba7b811-9dad-11d1-80b4-00c04fd430c8",
  "eventId": "550e8400-e29b-41d4-a716-446655440000",
  "gateId": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
  "gateName": "Entrada Principal",
  "gateCode": "E001",
  "name": "Catraca 156",
  "identifier": "MQTT_156",
  "deviceType": "Mqtt",
  "active": true,
  "lastSeenAt": "2026-08-27T15:30:00Z",
  "configurationJson": null,
  "operationMode": "Free"
}
```

### 🎯 O que acontece agora:
- Catraca roda **livremente** para ambos os lados
- Sem validação de ingresso
- Display mostra: `CATRACA LIVRE`
- Qualquer um pode passar

---

## 🧪 Teste 2: Mudar catraca para MODO BLOQUEADO (BLOCKED)

### Request:
```http
PUT http://localhost:5088/api/events/550e8400-e29b-41d4-a716-446655440000/gates/6ba7b810-9dad-11d1-80b4-00c04fd430c8/devices/6ba7b811-9dad-11d1-80b4-00c04fd430c8/operation-mode
Content-Type: application/json

{
  "operationMode": "Blocked"
}
```

### Response (200 OK):
```json
{
  "id": "6ba7b811-9dad-11d1-80b4-00c04fd430c8",
  "operationMode": "Blocked",
  ...
}
```

### 🎯 O que acontece agora:
- Catraca **não gira** em nenhuma direção
- Display mostra: `CATRACA BLOQUEADA`
- Ninguém consegue passar
- Qualquer leitura de ingresso resulta em KeepLocked

---

## 🧪 Teste 3: Voltar ao MODO ATIVO (ACTIVE)

### Request:
```http
PUT http://localhost:5088/api/events/550e8400-e29b-41d4-a716-446655440000/gates/6ba7b810-9dad-11d1-80b4-00c04fd430c8/devices/6ba7b811-9dad-11d1-80b4-00c04fd430c8/operation-mode
Content-Type: application/json

{
  "operationMode": "Active"
}
```

### Response (200 OK):
```json
{
  "id": "6ba7b811-9dad-11d1-80b4-00c04fd430c8",
  "operationMode": "Active",
  ...
}
```

### 🎯 O que acontece agora:
- Catraca volta ao **comportamento normal**
- Valida ingressos
- Se autorizado: libera + `ACESSO CONCEDIDO`
- Se negado: bloqueia + `ACESSO NEGADO`

---

## 📊 Verificar estado atual

```http
GET http://localhost:5088/api/events/550e8400-e29b-41d4-a716-446655440000/devices?gateId=6ba7b810-9dad-11d1-80b4-00c04fd430c8
```

Response:
```json
[
  {
    "id": "6ba7b811-9dad-11d1-80b4-00c04fd430c8",
    "name": "Catraca 156",
    "identifier": "MQTT_156",
    "operationMode": "Free",  ← Vê o modo atual aqui
    "active": true,
    "lastSeenAt": "2026-08-27T15:32:10Z"
  }
]
```

---

## 🔐 Testes com cURL

### Modo FREE:
```bash
curl -X PUT \
  -H "Content-Type: application/json" \
  -d '{"operationMode":"Free"}' \
  http://localhost:5088/api/events/550e8400-e29b-41d4-a716-446655440000/gates/6ba7b810-9dad-11d1-80b4-00c04fd430c8/devices/6ba7b811-9dad-11d1-80b4-00c04fd430c8/operation-mode
```

### Modo BLOCKED:
```bash
curl -X PUT \
  -H "Content-Type: application/json" \
  -d '{"operationMode":"Blocked"}' \
  http://localhost:5088/api/events/550e8400-e29b-41d4-a716-446655440000/gates/6ba7b810-9dad-11d1-80b4-00c04fd430c8/devices/6ba7b811-9dad-11d1-80b4-00c04fd430c8/operation-mode
```

### Modo ACTIVE:
```bash
curl -X PUT \
  -H "Content-Type: application/json" \
  -d '{"operationMode":"Active"}' \
  http://localhost:5088/api/events/550e8400-e29b-41d4-a716-446655440000/gates/6ba7b810-9dad-11d1-80b4-00c04fd430c8/devices/6ba7b811-9dad-11d1-80b4-00c04fd430c8/operation-mode
```

---

## ❌ Erros possíveis

### 400 - Bad Request (Modo inválido)
```json
{
  "error": "OperationMode deve ser Active, Free ou Blocked."
}
```
✅ Solução: Use exatamente: `"Active"`, `"Free"` ou `"Blocked"` (case-sensitive)

### 400 - Bad Request (IDs inválidos)
```json
{
  "error": "Dispositivo não encontrado ou não pertence à portaria/evento informado."
}
```
✅ Solução: Verifique se os IDs estão corretos no banco

### 401 - Unauthorized
```json
{
  "error": "Sem sessão válida"
}
```
✅ Solução: Faça login primeiro, obtenha o token/cookie

### 403 - Forbidden
```json
{
  "error": "Sem permissão"
}
```
✅ Solução: Seu usuário precisa da permissão `dispositivo.gerenciar`

---

## 📝 Fluxo visual completo

```
┌─────────────────────────────────────────────────────────────────────┐
│                     API REST                                        │
│  PUT /api/events/.../devices/.../operation-mode                   │
│  Body: { "operationMode": "Free" }                                │
└──────────────────────┬──────────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│             FastPass.Api (Program.cs)                              │
│         SetDeviceOperationModeAsync()                             │
└──────────────────────┬──────────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│         MySqlCatalogService                                        │
│    UPDATE fp_devices SET operation_mode = 'Free'                  │
└──────────────────────┬──────────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│              Banco de Dados (MySQL)                                │
│           fp_devices.operation_mode = 'Free'                      │
└──────────────────────┬──────────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│          MqttTurnstileService (Worker)                            │
│   Próxima leitura: if device.OperationMode == "Free"             │
│      → Unlock (gira) + GreenArrow + "CATRACA LIVRE"              │
└──────────────────────┬──────────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│              MQTT (Broker)                                         │
│   Publish: FastPass/MQTT_156/to/access                           │
│   { "release": true, "message": "CATRACA\nLIVRE" }              │
└──────────────────────┬──────────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│              Catraca (Placa)                                       │
│   ✅ GIRA LIVREMENTE                                              │
│   Display: CATRACA LIVRE                                          │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 🎯 Próximos passos

1. **Obter IDs reais**: Execute `./get-test-ids.ps1` ou rode a query SQL
2. **Fazer login**: POST `/api/auth/login` para obter sessão
3. **Testar endpoint**: Use o PUT com os IDs do seu banco
4. **Verificar catraca**: Observe o comportamento físico + display
5. **Voltar ao normal**: Mude para "Active" quando terminar

---

## ✅ Checklist final

- [ ] Banco de dados com migration 030 executada
- [ ] FastPass.Api reiniciado
- [ ] IDs obtidos (evento, portaria, catraca)
- [ ] Login realizado (obtém token/cookie)
- [ ] Teste Free: catraca gira livre
- [ ] Teste Blocked: catraca não gira
- [ ] Teste Active: catraca valida novamente

🎉 **Pronto para produção!**

