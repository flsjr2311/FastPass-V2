# Phase 2 UX - Turnstile Management Implementation

## Summary
Implemented Phase 2 UX improvements for FastPass turnstile management with 3-mode device operation (Active/Free/Blocked) and mode-specific display messages via Neon 1.3 MQTT protocol.

## Features Implemented

### 1. Device Operation Modes
- **Active**: Waiting for credential input - displays "PASSE SEU INGRESSO"
- **Free**: Open access - displays "LIBERADA ENTRE"
- **Blocked**: Restricted access - displays "BLOQUEADA CADEADO"

### 2. MQTT Integration (Neon 1.3)
- **Protocol**: Neon 1.3 MQTT
- **Display Format**: Monochrome, 2 lines × 16 characters max
- **Commands Implemented**:
  - `acc_req_auth`: Access response with display messages
  - `setconfig /config_accmode.json`: Set device mode (always "11")
  - `setconfig /config_msgs.json`: Configure display messages
  - `setconfig` with templates: Push message templates (00-30)

### 3. Backend Services
- `ITurnstileMessageTemplateService`: Interface for template management
- `MySqlTurnstileMessageTemplateService`: MySQL implementation
- 31 message templates (IDs 00-30) with Portuguese labels

### 4. Frontend
- **Catracas Tab**: View all connected turnstiles with status
- **Mode Control**: Switch between Active/Free/Blocked modes per turnstile
- **Message Templates Editor**: Configure display messages for each template ID
- **Real-time Status**: Monitor online/offline status and operation mode

### 5. Database Migrations
- `032_fix_turnstile_message_templates.sql`: Create template table structure
- `033_ensure_turnstile_templates_data.sql`: Backup INSERT for templates
- `034_repopulate_turnstile_templates.sql`: Ensure all 31 templates are populated
- `035_delete_catraca151_definitively.sql`: Clean up duplicate device entry
- `036_split_gate_turnstile_mode.sql`: Separa o modo da catraca da política de validação da portaria

## Technical Details

### Operation Mode Flow
```
Device Online (Keepalive)
  ↓
SendInitialModeMessageAsync() - Removed (catraca may not accept unsolicited messages)
SendConfigurationAsync() - Send 31 templates
SendMsgsConfigAsync() - Configure /config_msgs.json
SendAccmodeConfigAsync() - Enforce ACCMODE="11"
  ↓
User reads credential
  ↓
ValidateCredentialAsync()
  ↓
EncodeCommand(AccessValidationResult) - Send acc_req_auth with disp1/disp2 or template_id
```

### MQTT Topics
- **Request**: `${facility}/${catraca_alias}/from/access` (device sends credential)
- **Response**: `${facility}/${catraca_alias}/to/access` (FastPass sends validation)
- **Config**: `${facility}/${catraca_alias}/to/config` (templates, ACCMODE, messages)

### Message Template Mapping
- Template 2: "LIBERADA / ENTRE" (Free mode)
- Template 4: "BLOQUEADA / CADEADO" (Blocked mode)
- Template 5: "PASSE SEU / INGRESSO" (Active mode)

## Known Issues

### Display Not Updating ⏳
**Status**: Awaiting Neon support response

**Symptoms**: Catraca receives correct MQTT commands but display doesn't update

**Possible Causes**:
1. Catraca firmware may not support `disp1/disp2` fields in `acc_req_auth`
2. Display mode configuration on device may require physical reset
3. May need different command format not documented in Neon 1.3 manual
4. Firmware may have bug in display update handler

**Investigation Done**:
- ✅ MQTT messages verified arriving at broker
- ✅ Payload format matches Neon 1.3 specification
- ✅ Tópicos corretos (/to/access)
- ✅ Template IDs correctly mapped
- ✅ Config messages sent to /config_msgs.json

**Next Steps**:
- Await Neon support ticket response
- May require firmware update or physical device configuration

## Code Quality Improvements

### Migration Validation System
Added `MigrationValidation.cs` to catch common errors BEFORE execution:
- Validates DELETE statements require WHERE clauses
- Ensures foreign key dependencies are deleted first
- Warns about operations affecting critical tables (login, users, permissions)
- Prevents: "Cannot delete or update parent row" errors

### Error Prevention
- Database migrator now validates all statements before execution
- Prevents repeating same migration errors
- Reduces debugging time and token waste

## Files Modified

### Backend
- `src/FastPass.Api/Program.cs` - Added turnstile template endpoints
- `src/FastPass.Application/Catalog/CatalogContracts.cs` - New interfaces
- `src/FastPass.Application/Catalog/TurnstileMessageTemplateContracts.cs` - New contracts
- `src/FastPass.Infrastructure/Catalog/MySqlCatalogService.cs` - Device resolution
- `src/FastPass.Infrastructure/Catalog/MySqlTurnstileMessageTemplateService.cs` - Template service
- `src/FastPass.Infrastructure/Database/DatabaseMigrator.cs` - Added validation
- `src/FastPass.Infrastructure/Database/MigrationValidation.cs` - New validation system
- `src/FastPass.Worker/Mqtt/MqttTurnstileService.cs` - Mode handling, config sending
- `src/FastPass.Worker/Mqtt/TurnstileMessageCodec.cs` - Message encoding
- `src/FastPass.Worker/Mqtt/TurnstileTopics.cs` - Topic management

### Frontend
- `web/src/App.tsx` - Added Catracas tab
- `web/src/api.ts` - New API calls for templates
- `web/src/types.ts` - New TypeScript types
- `web/src/styles.css` - Styling for new components

### Database Migrations
- `src/FastPass.Infrastructure/Database/Migrations/032-035_*.sql` - Template setup and cleanup

## Testing Performed
- ✅ Login works (admin / Admin@1234)
- ✅ API endpoints respond correctly
- ✅ Frontend displays catracas and templates
- ✅ MQTT messages send to correct topics
- ✅ Catracas 156 and 157 online and responding
- ✅ Templates (00-30) visible in UI
- ❓ Display messages update on catracas (awaiting support response)

## Deployment Notes
1. Apply all migrations (032-035) automatically on startup
2. No manual database configuration needed
3. Catracas will receive ACCMODE="11" on next keepalive
4. Message templates load from database on each connection

## Correções pós-Phase 2

### 1. Modo por catraca não conseguia voltar a herdar a portaria 🔴 → ✅
**Sintoma**: escolher "Herdar do padrão" ou clicar em "Remover" gravava um override
explícito de `Active`. A mensagem dizia "(Herdado da portaria)" enquanto o banco tinha
um override. O botão de remover nunca removia nada.

**Causa**: o front convertia o `null` em `'Active'` antes de chamar a API
(`operationMode ?? 'Active'`, em dois call sites), e o backend rejeitava vazio
(`OperationMode é obrigatório`) e sempre gravava um valor concreto — não existia
nenhum caminho, nem via API, para voltar ao estado "herda".

**Correção**:
- `SetDeviceOperationModeCommand.OperationMode` passou a ser `string?`
- `SetDeviceOperationModeAsync` grava `NULL` quando o valor vem nulo/vazio
- os dois call sites do front enviam `null` de verdade
- as mensagens passaram a ser derivadas do `DeviceView` retornado pelo servidor,
  não do que o front pediu

### 2. Modo da catraca corrompia a política de validação da portaria 🔴 → ✅
**Sintoma**: `POST /api/access/validate` devolvia
`400 {"error":"Modo operacional inválido configurado para a portaria."}` e **nenhum
ingresso passava em nenhuma portaria**.

**Causa**: `fp_event_gates.operation_mode` armazenava dois enums diferentes.
A Migration 005 criou a coluna para `GateOperationMode`
(`EntryAndExitValidated`/`EntryValidatedExitFree`) e a Migration 030 tentou recriá-la
para `TurnstileOperationMode` (`Active`/`Free`/`Blocked`) — o erro 1060 foi tolerado
pelo migrator e passou batido. `SetGateTurnstileModeAsync` gravava o modo da catraca
ali, e `MySqlAccessValidationService.ReadGateAsync` não conseguia mais interpretar
o valor.

**Correção**: Migration `036` cria `fp_event_gates.turnstile_mode`, move os valores
para a coluna certa e devolve `operation_mode` ao seu domínio. As leituras foram
separadas em `ListGatesAsync`, `ListDevicesAsync` e `ResolveTurnstileDeviceAsync`.

**Perda de dado**: portarias cujo `operation_mode` já havia sido sobrescrito voltaram
ao default `EntryAndExitValidated` — o valor original não é recuperável e deve ser
reconfigurado na aba Matriz.

### 3. Exclusão de catraca dava 500 e a UI não expunha remoção 🔴 → ✅
**Causa**: `fp_access_attempts.device_id` referencia `fp_devices(id)` com `RESTRICT`.
O hard delete estourava `MySqlException` não tratada. E o front nunca chamava o
`DELETE` que já existia na API.

**Correção**: erro 1451 virou `400` com orientação para desativar; a aba Dispositivos
ganhou **Desativar/Reativar** (reversível) e **Excluir** (com confirmação).

### 4. Ação de linha ambígua na aba Dispositivos ✅
O único botão dizia "Remover", numa coluna sem cabeçalho e com estilo vermelho
(`.reset-button`) — lia como "excluir a catraca", mas limpava o override.
Agora: coluna "Ações", botão **"Herdar portaria"** em estilo neutro, e o vermelho
reservado para o **"Excluir"**, que é destrutivo de fato.

### 5. `npm run build` estava quebrado ✅
`App.tsx` usava `TurnstileMessageTemplateView` sem importar o tipo, gerando dois
`TS2304`. O dev server do Vite não faz typecheck, então o erro só aparecia no build.

## Future Enhancements
- Support for custom message templates per event
- Scheduled mode changes (e.g., Active during day, Free after hours)
- Display message history/audit log
- Per-device operation logs
