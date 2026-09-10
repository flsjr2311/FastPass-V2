# Quick Start - Testing Phase 1 UX Features

## 🚀 Prerequisites (All Running)

```
✅ API:      http://localhost:5088
✅ Frontend: http://localhost:5173
✅ MQTT:     localhost:1883
✅ Catracas: Catraca156, Catraca157 (online)
```

---

## 📱 Test 1: Login

1. Open http://localhost:5173
2. See login form
3. Enter credentials:
   - Username: `admin`
   - Password: `Admin@1234`
4. Click "Entrar"
5. **Expected**: Dashboard appears, main navigation visible

---

## 📍 Test 2: Navigate to Catracas

1. Click menu "⊟ Catracas" (under OPERAÇÃO)
2. Wait 2-3 seconds for load
3. **Expected Result**:
   ```
   Header: "Catracas"
   Subtitle: "2 catraca(s) · 2 online · ? sem atribuição"
   
   Two cards visible:
   ┌─ Catraca 156
   │  🟢 Online
   │  ID MQTT: catraca156
   │  [... metadata ...]
   │  ⚠️ Sem atribuição
   │  [Atribuir agora] button (BLUE)
   │
   └─ Catraca 157
      🟢 Online
      [similar layout]
   ```

---

## 🎯 Test 3: Open Assignment Modal

### Step 1: Click "Atribuir agora" on Catraca156

**Expected**:
```
Modal appears (overlay with backdrop)

┌─────────────────────────────────┐
│ Atribuir Catraca            [✕] │
├─────────────────────────────────┤
│ ID MQTT                         │
│ [catraca156]     (disabled)     │
│ Firmware: [version]             │
│ IP: [ip address]                │
│                                 │
│ Evento *                        │
│ [Selecione um evento...]        │
│                                 │
│ Nome da Catraca *               │
│ [Catraca catraca156]            │
│                                 │
├─────────────────────────────────┤
│ [Cancelar] [Atribuir]           │
└─────────────────────────────────┘
```

### Step 2: Select Evento Dropdown

**Click**: `[Selecione um evento...]`

**Expected**: 
- Dropdown opens
- Shows available events (e.g., "Event 1", "Event 2")
- Select first event

**Note**: If no events exist, create one first in "◈ Eventos" menu

### Step 3: Portaria Dropdown Appears

**Expected After selecting evento**:
```
Modal now shows:

Portaria * (NEW FIELD)
[Selecione uma portaria...]
```

**Click** the dropdown and select a portaria (e.g., "Portaria A (PA)")

### Step 4: Verify Name Pre-fill

**Expected**: 
```
Nome da Catraca *
[Catraca catraca156]  ← Pre-filled
```

Can edit to custom name if desired (e.g., "Catraca Camarote")

### Step 5: Submit

**Click**: `[Atribuir]` (blue button)

**Expected**:
- Button shows spinner: "Atribuindo..."
- After 1-2 seconds, modal closes
- Returns to Catracas view

---

## ✅ Test 4: Verify Status Updated

After modal closes, return to Catracas view.

### Expected: Catraca156 Card Updated

**Before Assignment**:
```
⚠️ Sem atribuição
[Atribuir agora]
```

**After Assignment**:
```
✅ Atribuída a Portaria A · evento Event 1
(Button "Atribuir agora" DISAPPEARS)
```

---

## 🔧 Test 5: Check Configuration

1. Navigate to "⚙ Portarias e Setores"
2. You should see **three tabs**:
   - `🚪 Portarias` (currently active)
   - `🎪 Setores`
   - `🔗 Matriz`

### On Portarias Tab

**Expected**:
```
Header: "Portarias"
(Card count: N)

┌──────────────────┐
│ 🚪 Portaria A    │
│                  │
│ PA (code)        │
│ Entrada (mode)   │
│                  │
│ 1 catraca        │  ← Increased by 1
│ N setores        │  ← If configured
│                  │
│ [Selecionar]     │
│ [Remover]        │
└──────────────────┘
```

**Catraca Counter**: Should increase by 1 from what it was before assignment

### Alerts Section

**Expected Above Tabs**:
- If any gates have NO sectors: Yellow alert appears
- If any sectors have NO gates: Yellow alert appears
- If all properly configured: No alerts visible

```
⚠️ [Alert icon] 1 portaria(s) sem setor associado
                Portaria A

⚠️ [Alert icon] 1 setor(es) sem portaria conectada
                Setor A
```

---

## 🎯 Test 6: Tabs Navigation

### Switch to Setores Tab

**Click**: `🎪 Setores`

**Expected**:
```
Setores tab now active (underline/color change)
Shows sectors grid with cards
Each card shows:
- 🎪 Sector Name
- Capacity (if set)
- N portarias (counter)
```

### Switch to Matriz Tab

**Click**: `🔗 Matriz`

**Expected**:
```
Matrix view shows:
- Gates in columns
- Sectors in rows
- Checkboxes for Entry/Exit permissions
- Can toggle permissions on/off
```

---

## 🔄 Test 7: Real-time Status

### Simulate Catraca Going Offline

In another terminal (simulate MQTT disconnect):
```bash
# Stop keepalive messages or disconnect from broker
```

**Expected on Frontend**:
- Within 10 seconds, Catracas page auto-refreshes
- Catraca goes from 🟢 Online → 🔴 Offline
- Card becomes slightly transparent (72% opacity)
- Status still shows "✅ Atribuída a Portaria A" (assignment doesn't change)

### Catraca Comes Back Online

Resume MQTT connection.

**Expected**:
- Within 10 seconds, status changes back to 🟢 Online
- Opacity returns to 100%
- Card is interactive again

---

## 🧪 Test 8: Validation Rule

### Test Quota Exceeded Scenario

1. Go to "▣ Tickets"
2. Create/edit a ticket with `MaximumUses = 1`
3. Have catraca validate twice on same ticket

**Expected on First Validation**: ✅ Approved
**Expected on Second Validation**: ❌ Rejected with message

---

## 📊 Quick Checklist

### Frontend Behavior
- [ ] Login works (admin/Admin@1234)
- [ ] Catracas tab shows online catracas
- [ ] Modal opens and closes properly
- [ ] Assignment form works (all fields required)
- [ ] After assignment, card status updates
- [ ] Configuration tabs switch correctly
- [ ] Alerts appear/disappear based on config
- [ ] Auto-refresh works (check timestamps)

### Visual Design
- [ ] Cards use responsive grid (looks good at different widths)
- [ ] Colors match spec (green online, red offline, yellow alerts)
- [ ] Buttons have proper states (enabled/disabled/hover)
- [ ] Modal overlays the page
- [ ] Text is readable (contrast, font size)
- [ ] Icons/emojis display correctly

### API Integration
- [ ] GET /api/turnstiles returns catraca data
- [ ] POST /api/events/{id}/gates/{id}/devices creates device
- [ ] GET /api/events/{id}/devices lists assignments
- [ ] Errors display properly on API failure

### MQTT Integration
- [ ] Catracas appear in list (discovered via MQTT)
- [ ] Status reflects connectivity (online/offline)
- [ ] Keepalive messages flowing in API logs

---

## 🐛 Troubleshooting

### Problem: Modal doesn't open
**Solution**: 
- Check browser console for errors (F12 → Console)
- Verify API is running: `curl http://localhost:5088/api/events`
- Try refreshing page (Ctrl+R)

### Problem: Portaria dropdown empty
**Solution**:
- Need to create gates first (use "🚪 Portarias" tab)
- Or select different event that has gates

### Problem: Status not updating after assignment
**Solution**:
- Manual refresh page (Ctrl+R)
- Check API logs for errors
- Verify device creation succeeded (GET /api/events/{id}/devices)

### Problem: MQTT catracas not appearing
**Solution**:
- Check Mosquitto is running: `Get-Process mosquitto`
- Check API logs for MQTT connection
- Verify catracas are publishing to correct topic

---

## 💡 Tips

1. **Multiple Browsers**: Open http://localhost:5173 in two browser windows
   - Assign catraca in one window
   - See update in other window (auto-refresh every 10 seconds)

2. **Developer Tools**: Press F12 to open DevTools
   - Network tab: Watch API calls
   - Console: Check for errors
   - Application → Local Storage: See any cached data

3. **API Testing**: Use curl or Postman to test endpoints directly
   ```bash
   curl http://localhost:5088/api/turnstiles
   curl http://localhost:5088/api/events
   ```

4. **MQTT Monitoring**: Subscribe to MQTT topics
   ```bash
   mosquitto_sub -h localhost -t "FastPass/#"
   ```

---

## 📝 Notes

- Frontend auto-refreshes every 10 seconds for catraca list
- Modal auto-closes on successful assignment
- Alerts re-compute every time config data changes
- Tabs state persists within session (not in localStorage)
- Validation rule (quota) applies during access validation, not creation

---

**Ready to Test! 🎉**

Start with Test 1 (Login) and work through sequentially.
Each test builds on previous state.
Report any issues with test number and exact behavior observed.
