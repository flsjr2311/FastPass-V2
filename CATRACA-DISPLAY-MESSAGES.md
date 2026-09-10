# Mensagens no Display da Catraca - FastPass V2

## 🎯 Visão Geral

O sistema FastPass envia mensagens customizáveis para o display LCD da catraca (2 linhas × 16 caracteres). Essas mensagens variam de acordo com:

- **Status**: Ativa/Bloqueada/Manutenção
- **Resultado da validação**: Acesso concedido, acesso negado, etc.
- **Configuração customizável**: Por template global ou override por evento

---

## 📡 Fluxo de Mensagens

```
┌─────────────────────┐
│  Catraca lê QR/    │
│  cartão            │
└──────────┬──────────┘
           │ (publica em /from/access)
           ▼
┌─────────────────────────────────────┐
│  FastPass valida credencial         │
│  (MySqlAccessValidationService)    │
└──────────┬──────────────────────────┘
           │
    ┌──────┴────────┐
    │               │
    ▼ (Aprovado)    ▼ (Rejeitado)
┌──────────────┐ ┌──────────────┐
│ Mensagem OK  │ │ Mensagem NOK │
└──────┬───────┘ └──────┬───────┘
       │                │
       └────────┬───────┘
                │ ResolveAsync
                ▼
    ┌─────────────────────────────┐
    │ IAccessMessageService       │
    │ (busca template/override)   │
    └────────┬────────────────────┘
             │
             ▼
    ┌─────────────────────────────┐
    │ AccessValidationResult      │
    │ (Message + Presentation)    │
    └────────┬────────────────────┘
             │
             ▼
    ┌─────────────────────────────┐
    │ TurnstileMessageCodec       │
    │ .EncodeCommand()            │
    └────────┬────────────────────┘
             │ (JSON com message)
             ▼
┌─────────────────────────────────┐
│ MQTT publica em /to/access      │
│ {"message": "PASSE SEU...", ... }│
└────────┬────────────────────────┘
         │ (mensagem chega na catraca)
         ▼
    ┌─────────────────┐
    │ Display da      │
    │ Catraca mostra  │
    │ a mensagem      │
    └─────────────────┘
```

---

## 🖥️ Display LCD - Formato

**Hardware**: 2 linhas × 16 caracteres

```
┌────────────────┐
│ PASSE SEU INGR │  ← Linha 1 (16 chars)
│ ESSO          │  ← Linha 2 (16 chars)
└────────────────┘
```

**Limite**: Máximo 32 caracteres (16 por linha)

**Recomendação**: Usar linhas curtas e descritivas

---

## 📋 Códigos de Mensagem

O sistema mapeia automaticamente a decisão da validação para um **código de mensagem** (message code):

| Código | Cenário | Mensagem Padrão |
|--------|---------|-----------------|
| `ACESSO_CONCEDIDO` | Acesso aprovado | "ACESSO CONCEDIDO" |
| `INGRESSO_JA_UTILIZADO` | Ingresso já foi usado | "INGRESSO JÁ UTILIZADO" |
| `CRACHA_INATIVO` | Crachá de funcionário inativo | "CRACHÁ INATIVO" |
| `CRACHA_AINDA_NAO_VALIDO` | Crachá ainda não começou validade | "CRACHÁ NÃO VÁLIDO" |
| `CRACHA_EXPIRADO` | Crachá expirou | "CRACHÁ EXPIRADO" |
| `CRACHA_SEM_AUTORIZACAO` | Funcionário sem acesso | "SEM AUTORIZAÇÃO" |
| `INGRESSO_NAO_ENCONTRADO` | QR/cartão não cadastrado | "INGRESSO NÃO ENCONTRADO" |
| `INGRESSO_INATIVO` | Ingresso desabilitado | "INGRESSO INATIVO" |
| `MATRIZ_NAO_AUTORIZADA` | Portaria/setor não conectados | "ACESSO NÃO AUTORIZADO" |
| `INGRESSO_SEM_AUTORIZACAO` | Ingresso sem permissão | "SEM AUTORIZAÇÃO" |
| `PRESENCA_NAO_REGISTRADA` | Entrada não registrada | "PRESENÇA NÃO REGISTRADA" |
| `SAIDA_SEM_PRESENCA` | Saída sem entrada anterior | "SAÍDA SEM ENTRADA" |
| `ACESSO_NEGADO` | Motivo genérico | "ACESSO NEGADO" |

**Localização do código**: 
- Arquivo: `src/FastPass.Infrastructure/Access/MySqlAccessValidationService.cs`
- Função: `ResolveMessageCode()`

---

## 🔧 Configurar Mensagens

### Opção 1: Template Global (todos os eventos)

**Endpoint**: `PUT /api/message-templates/{code}`

**Exemplo**:
```json
{
  "code": "ACESSO_CONCEDIDO",
  "title": "PASSE",
  "message": "SEU INGRESSO",
  "backgroundStart": "#00AA00",
  "backgroundEnd": "#00AA00",
  "titleColor": "#FFFFFF",
  "messageColor": "#FFFFFF",
  "titleSize": "24",
  "messageSize": "16",
  "titleBold": true,
  "messageBold": false,
  "active": true
}
```

### Opção 2: Override por Evento

**Endpoint**: `PUT /api/events/{eventId}/access-messages/{code}`

Mesmo payload acima - override do template global para esse evento específico.

### Opção 3: Restaurar Padrão

**Endpoint**: `DELETE /api/events/{eventId}/access-messages/{code}`

Remove o override e volta a usar o template global.

---

## 📝 Campos de Mensagem

| Campo | Descrição | Limite | Exemplo |
|-------|-----------|--------|---------|
| `title` | Primeira linha do display | 16 chars | "PASSE" |
| `message` | Segunda linha do display | 16 chars | "SEU INGRESSO" |
| `backgroundStart` | Cor background (gradiente início) | hex | "#00AA00" |
| `backgroundEnd` | Cor background (gradiente fim) | hex | "#00AA00" |
| `titleColor` | Cor do title | hex | "#FFFFFF" |
| `messageColor` | Cor da message | hex | "#FFFFFF" |
| `titleSize` | Tamanho do font (title) | 12-48 | "24" |
| `messageSize` | Tamanho do font (message) | 12-48 | "16" |
| `titleBold` | Negrito no title | bool | true |
| `messageBold` | Negrito na message | bool | false |
| `active` | Mensagem ativa? | bool | true |

---

## 🎨 Exemplo de Configuração Completa

### Status Padrão da Catraca (Ativa)

**Código**: `CATRACA_ATIVA` (adicionar novo se não existir)

```json
{
  "title": "PASSE SEU",
  "message": "INGRESSO",
  "backgroundStart": "#0066CC",
  "backgroundEnd": "#0066CC",
  "titleColor": "#FFFFFF",
  "messageColor": "#FFFFFF",
  "titleSize": "16",
  "messageSize": "14",
  "titleBold": true,
  "messageBold": false,
  "active": true
}
```

Display:
```
┌────────────────┐
│ PASSE SEU      │
│ INGRESSO       │
└────────────────┘
```

### Status Catraca Bloqueada

**Código**: `CATRACA_BLOQUEADA` (adicionar novo se não existir)

```json
{
  "title": "CATRACA",
  "message": "BLOQUEADA",
  "backgroundStart": "#CC0000",
  "backgroundEnd": "#CC0000",
  "titleColor": "#FFFFFF",
  "messageColor": "#FFFFFF",
  "titleSize": "16",
  "messageSize": "14",
  "titleBold": true,
  "messageBold": true,
  "active": true
}
```

Display:
```
┌────────────────┐
│ CATRACA        │
│ BLOQUEADA      │
└────────────────┘
```

### Acesso Concedido

**Código**: `ACESSO_CONCEDIDO` (já existe)

```json
{
  "title": "✓ ACESSO",
  "message": "CONCEDIDO",
  "backgroundStart": "#00AA00",
  "backgroundEnd": "#00AA00",
  "titleColor": "#FFFFFF",
  "messageColor": "#FFFFFF",
  "titleSize": "18",
  "messageSize": "16",
  "titleBold": true,
  "messageBold": true,
  "active": true
}
```

Display:
```
┌────────────────┐
│ ✓ ACESSO       │
│ CONCEDIDO      │
└────────────────┘
```

### Acesso Negado / Ingresso Expirado

**Código**: `INGRESSO_JA_UTILIZADO` (já existe)

```json
{
  "title": "✕ ACESSO",
  "message": "NEGADO",
  "backgroundStart": "#CC0000",
  "backgroundEnd": "#CC0000",
  "titleColor": "#FFFFFF",
  "messageColor": "#FFFF00",
  "titleSize": "18",
  "messageSize": "16",
  "titleBold": true,
  "messageBold": true,
  "active": true
}
```

Display:
```
┌────────────────┐
│ ✕ ACESSO       │
│ NEGADO         │
└────────────────┘
```

---

## 🚀 Como Enviar Mensagens via API

### 1. Listar Mensagens Disponíveis

**GET** `/api/message-templates`

Retorna todos os templates globais:

```json
[
  {
    "code": "ACESSO_CONCEDIDO",
    "title": "ACESSO",
    "message": "CONCEDIDO",
    "active": true,
    ...
  },
  {
    "code": "ACESSO_NEGADO",
    "title": "ACESSO",
    "message": "NEGADO",
    "active": true,
    ...
  }
]
```

### 2. Editar Template Global

**PUT** `/api/message-templates/{code}`

```bash
curl -X PUT http://localhost:5088/api/message-templates/ACESSO_CONCEDIDO \
  -H "Content-Type: application/json" \
  -d '{
    "title": "✓ PASSE",
    "message": "LIBERADO",
    "backgroundStart": "#00CC00",
    "backgroundEnd": "#00CC00",
    "titleColor": "#FFFFFF",
    "messageColor": "#FFFFFF",
    "titleSize": "18",
    "messageSize": "16",
    "titleBold": true,
    "messageBold": true,
    "active": true
  }'
```

### 3. Override para um Evento

**GET** `/api/events/{eventId}/access-messages`

Retorna mensagens customizadas para esse evento (herdam do template se não houver override).

**PUT** `/api/events/{eventId}/access-messages/{code}`

```bash
curl -X PUT http://localhost:5088/api/events/12345-67890/access-messages/ACESSO_CONCEDIDO \
  -H "Content-Type: application/json" \
  -d '{
    "title": "BEM-VINDO",
    "message": "AO EVENTO",
    "backgroundStart": "#FF6600",
    "backgroundEnd": "#FF6600",
    "titleColor": "#FFFFFF",
    "messageColor": "#FFFFFF",
    "titleSize": "16",
    "messageSize": "14",
    "titleBold": true,
    "messageBold": false,
    "active": true
  }'
```

---

## 🔌 Payload MQTT Enviado para a Catraca

Quando a validação acontece, o sistema publica em `FastPass/Catraca{id}/to/access`:

```json
{
  "cmd": "access",
  "authorized": true,
  "release": true,
  "direction": "Entry",
  "pictogram": "GreenArrowEntry",
  "reasonCode": "ACESSO_CONCEDIDO",
  "message": "✓ PASSE LIBERADO",
  "attemptId": "550e8400-e29b-41d4-a716-446655440000"
}
```

**Campos**:
- `authorized`: `true` (aproved) | `false` (rejected)
- `release`: `true` libera a tranca | `false` mantém trancado
- `direction`: `Entry` (entrada) | `Exit` (saída)
- `pictogram`: `GreenArrowEntry`, `GreenArrowExit`, `RedCross`, `None`
- `message`: Texto para o display (até 32 chars, com quebra de linha)
- `reasonCode`: Código interno da mensagem

---

## 💡 Boas Práticas

### Mensagens Curtas
- **Máximo 32 caracteres total** (16 por linha)
- Use abreviações quando necessário
- Foque no essencial

### Emojis/Símbolos
- ✓ Acesso concedido (verde)
- ✕ Acesso negado (vermelho)
- ⚠ Aviso (amarelo)
- 🔒 Bloqueado (vermelho)

### Cores
- **Verde (#00AA00, #00CC00)**: Sucesso, OK
- **Vermelho (#CC0000, #DD0000)**: Erro, negado, bloqueado
- **Azul (#0066CC, #0088FF)**: Info, aguarde
- **Amarelo (#FFFF00)**: Aviso, atenção
- **Branco (#FFFFFF)**: Texto em contraste

### Exemplos Resumidos

```
Acesso Concedido:
  "✓ ENTRADA        PERMITIDA"

Acesso Negado:
  "✕ ACESSO NEGADO"

Ingresso Já Utilizado:
  "⚠ INGRESSO USADO"

Catraca Bloqueada:
  "CATRACA BLOQUEADA" (exibição estática, não resultado de validação)

Sem Autorização:
  "SEM PERMISSÃO"
```

---

## 🔄 Fluxo Simplificado de Teste

1. **Criar evento** em ⊟ Eventos
2. **Configurar portarias/setores** em ⚙ Portarias e Setores
3. **Importar ingressos** em ↑ Importar
4. **Editar templates** em ✉ Mensagens (admin)
   - Customizar `ACESSO_CONCEDIDO`
   - Customizar `ACESSO_NEGADO`
   - etc.
5. **Testar validação**:
   - Catraca lê QR/cartão
   - API valida
   - Mensagem aparece no display
   - Resultado esperado = controle de acesso

---

## 📚 Referências

- **Protocolo**: `PROTOCOLO-NEON.md`
- **API**: `src/FastPass.Api/Program.cs` (endpoints `/api/message-templates`, `/api/events/{id}/access-messages`)
- **Serviço**: `src/FastPass.Infrastructure/Access/MySqlAccessMessageService.cs`
- **Codec**: `src/FastPass.Worker/Mqtt/TurnstileMessageCodec.cs`

---

**Última atualização**: 2026-08-27
**Versão**: FastPass V2
