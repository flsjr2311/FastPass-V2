# 🎨 Guia de Uso: Interface de Controle de Catracas

**Versão**: Phase 2 UX - TurnstileOperationMode  
**Data**: 2026-08-27  
**Status**: ✅ Implementado e testado

---

## 📍 Localização

1. Acesse: **http://localhost:5173**
2. Faça login: `admin` / `Admin@1234`
3. Navegue para: **Portarias e Setores** (sidebar esquerda)
4. Clique na aba: **⊟ Modo Catraca** (novo tab!)

---

## 🎮 Interface de Controle

### Visão Geral

A tela exibe um **card para cada portaria (gate)** com 3 botões de modo:

```
┌─────────────────────────────────────────┐
│ 🚪 Portaria Nome          [Código: ABC]  │
├─────────────────────────────────────────┤
│  Modo padrão da portaria:                │
│                                         │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐ │
│  │   📖    │  │   🟢    │  │   🔴    │ │
│  │  ATIVA  │  │LIBERADA │  │BLOQUEADA│ │
│  │lê ing.  │  │ gira    │  │ não     │ │
│  └─────────┘  └─────────┘  └─────────┘ │
│                                         │
│  ℹ️  Cada catraca individual pode ter   │
│  um override deste modo...              │
└─────────────────────────────────────────┘
```

---

## 🔘 Os 3 Modos Explicados

### 1️⃣ **ATIVA** (📖 lê ingresso)
- **Estado**: Display aguarda leitura
- **Mensagem LCD**: `"PASSE SEU\nINGRESSO"`
- **Pictograma**: Apagado (None)
- **Braço**: Aguarda validação
- **Uso**: Entrances que validam ingresso (padrão)

### 2️⃣ **LIBERADA** (🟢 gira livremente)
- **Estado**: Catraca destrancada, gira para ambos os lados
- **Mensagem LCD**: `"LIBERADA\nENTRE"`
- **Pictograma**: Seta verde (GreenArrowEntry)
- **Braço**: Destrancado (Unlock)
- **Uso**: Areas VIP, backstage, acesso livre temporário

### 3️⃣ **BLOQUEADA** (🔴 não gira)
- **Estado**: Catraca travada, não gira para nenhum lado
- **Mensagem LCD**: `"BLOQUEADA\nCADEADO"`
- **Pictograma**: Cruz vermelha (RedCross)
- **Braço**: Travado (KeepLocked)
- **Uso**: Portaria fechada, manutenção, restrições emergentes

---

## 📋 Passo a Passo: Mudar Modo de Uma Portaria

### Exemplo 1: Liberar a Portaria "Backstage" (VIP Access)

1. **Acesse "Modo Catraca"**
   - Menu lateral → "Portarias e Setores"
   - Tab "⊟ Modo Catraca"

2. **Localize o card "Backstage"**
   - Procure pela portaria na lista

3. **Clique no botão 🟢 LIBERADA**
   - Estado atual: talvez seja ATIVA
   - Novo estado: LIBERADA
   - Tempo: ~1 segundo (salva automaticamente)

4. **Confirmação**
   - Message no topo: "Modo da catraca em 'Backstage' agora é LIBERADA."
   - Botão fica destacado (azul)
   - Todas as catracas dessa portaria recebem novo modo via MQTT

### Exemplo 2: Bloquear Portaria "Saída" (Emergência)

1. Aba "⊟ Modo Catraca"
2. Card "Saída"
3. Clique 🔴 BLOQUEADA
4. Confirmação em 1 segundo

---

## 🔧 Override por Device (Per-Device)

### Situação: Catraca Individual Defeituosa

Imagine que a portaria "Pista" está em modo LIBERADA, mas uma catraca específica ("Catraca 151") está com problema.

**Solução**: Override de device (TBD - interface a completar)

```
Na tela de Dispositivos (future enhancement):
┌─────────────────────────────────┐
│ Device: Catraca 151 - Neon      │
│ Portaria: Pista (LIBERADA)      │
│ ┌─────────────────────────────┐ │
│ │ Override de Modo:           │ │
│ │ • ⚙ Usar padrão da porta   │ ← será LIBERADA
│ │ • 📖 ATIVA (forçar)        │ ← força leitura
│ │ • 🔴 BLOQUEADA (forçar)    │ ← força bloqueio
│ └─────────────────────────────┘ │
└─────────────────────────────────┘
```

**Resultado**:
- Gate "Pista": LIBERADA (padrão)
- Device "Catraca 151": BLOQUEADA (override)
- Outras catracas em "Pista": LIBERADA

---

## 📊 Estado Efetivo

O sistema resolve o modo da seguinte forma:

```
Modo Efetivo = Device Override OR Gate Default

Exemplos:
• Gate=ATIVA, Device=null → Efetivo=ATIVA (herda)
• Gate=ATIVA, Device=BLOQUEADA → Efetivo=BLOQUEADA (override)
• Gate=LIBERADA, Device=null → Efetivo=LIBERADA (herda)
```

---

## 🔄 Fluxo Completo

### 1. Usuário muda modo na Web UI
```
Clique em botão → API PUT /api/events/{id}/gates/{id}/turnstile-mode → DB atualizado
```

### 2. API salva no banco
```
fp_event_gates.operation_mode = 'Free' (exemplo)
```

### 3. Catraca recebe novo modo
```
MQTT Keepalive → Worker resolve gate+device → Envia mensagem LCD
Catraca.display = "LIBERADA\nENTRE"
Catraca.braço = UNLOCK
```

### 4. Próxima leitura
```
Leitura ingresso → MqttTurnstileService.HandleRead()
Valida com modo EFETIVO (Free ou Blocked ou Active)
Resposta apropriada ao ingresso
```

---

## 🚨 Dicas e Troubleshooting

### ❓ "Cliquei mas nada aconteceu"
- Verifique se a API está rodando: http://localhost:5088/health/database
- Verifique se está logado (cookie válido)
- Abra console (F12) → veja se há erro de permissão

### ❓ "Catraca não recebeu o novo modo"
- Worker MQTT conectado? Verifique logs
- Catraca enviou keepalive recentemente? Aguarde ~10s ou force com:
  ```bash
  mosquitto_pub -h 127.0.0.1 -t "FastPass/Catraca151/from/keepalive" -m '{}'
  ```

### ❓ "Não consigo ver a aba 'Modo Catraca'"
- Verifique permissão: deve ter `portaria.gerenciar`
- Recarregue a página: F5 ou Ctrl+Shift+R
- Selecione um evento primeiro

### ❓ "Quero reverter ao padrão ATIVA"
- Clique no botão 📖 ATIVA de novo
- Sistema envia mensagem "PASSE SEU\nINGRESSO" para a portaria

---

## 📱 Responsivo

A interface é **responsiva**:
- Desktop (480px+): Grid de 2-3 cards por linha
- Tablet (768px+): Grid ajusta automaticamente
- Mobile: Cards empilhados (1 por linha)

---

## 🔐 Permissões Necessárias

- **Para mudar modo da portaria**: `portaria.gerenciar`
- **Para override de device**: `dispositivo.gerenciar`

---

## 📝 Campos Exibidos

### Card de Portaria
- 🚪 Nome da portaria
- Código (se houver)
- 3 Botões de modo

### Informação
- Aviso: "Cada catraca individual pode ter um override..."
- Link para próximo passo (override por device)

---

## 🎯 Próximas Features (Roadmap)

- [ ] Tabela de dispositivos com override inline
- [ ] Seletor de override por device
- [ ] Visualização "Modo Efetivo" por device
- [ ] Bulk-change (mudar modo de várias portarias de uma vez)
- [ ] Agendamento de mudança de modo (ex.: modo FREE só 20:00-04:00)
- [ ] Histórico de mudanças (auditoria)

---

## 📸 Esboço Visual

```
┌───────────────────────────────────────────────────────────────────────┐
│ Dashboard / Portarias e Setores                                       │
│ [🚪] [🎪] [🔗] [⊟ Modo Catraca] ← YOU ARE HERE                       │
├───────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  TOPOLOGIA DE ACESSO — Controle de catracas                           │
│  Defina o comportamento de cada catraca: ativa/liberada/bloqueada     │
│                                                                         │
│  ┌──────────────────────┐  ┌──────────────────────┐  ┌────────────┐  │
│  │ 🚪 Camarote [PN-01]  │  │ 🚪 Pista [PI-01]     │  │ 🚪 VIP...  │  │
│  ├──────────────────────┤  ├──────────────────────┤  │            │  │
│  │ Modo padrão:         │  │ Modo padrão:         │  │ ...        │  │
│  │                      │  │                      │  │            │  │
│  │ 📖  🟢 🔴           │  │ 📖 🟢 🔴            │  │ 📖 🟢 🔴  │  │
│  │                      │  │                      │  │            │  │
│  │ ℹ️ Cada catraca...   │  │ ℹ️ Cada catraca...  │  │ ℹ️...      │  │
│  └──────────────────────┘  └──────────────────────┘  └────────────┘  │
│                                                                         │
│  [Message] "Modo da catraca em 'Camarote' agora é LIBERADA."          │
└───────────────────────────────────────────────────────────────────────┘
```

---

## 📞 Suporte

Erros? Dúvidas?
- Verifique `TEST_TURNSTILE_MODE.md` (teste end-to-end)
- Consulte logs:
  - API: Terminal do `dotnet run --project src/FastPass.Api`
  - Worker: Terminal do `dotnet run --project src/FastPass.Worker`

---

**Criado em**: 2026-08-27  
**Última atualização**: 2026-08-27
