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

## Future Enhancements
- Support for custom message templates per event
- Scheduled mode changes (e.g., Active during day, Free after hours)
- Display message history/audit log
- Per-device operation logs
