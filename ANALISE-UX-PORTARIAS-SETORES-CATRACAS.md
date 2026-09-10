# Análise UX: Portarias, Setores e Catracas

## Situação Atual

### Fluxo Confuso para Novo Usuário

```
1. Criar evento
2. Cadastrar portarias (portão, entrada, saída, camarote, pista, etc)
3. Cadastrar setores (camarote, pista, vip, backstage, etc)
4. Criar MATRIZ MANUALMENTE clicando em cada célula (portaria × setor × direção)
5. Conectar catraca ao MQTT
6. Procurar a catraca em "Catracas" (tela separada)
7. Ir para "Dispositivos" e DIGITAR O CÓDIGO DA CATRACA manualmente
8. Selecionaryualmente qual portaria a catraca pertence
```

**Problemas:**
- ❌ Usuário cria portaria "Camarote" e depois precisa lembrar qual é o ID dela
- ❌ Matriz 2D é confusa: não fica claro o que cada célula faz
- ❌ Catraca aparece em uma tela, mas associação é em outra (fragmentado)
- ❌ Sem guia: usuário não sabe se deve criar portaria/setor ANTES ou DEPOIS da catraca conectar
- ❌ Impossível saber quantas catracas estão associadas sem consultar múltiplas telas
- ❌ Sem feedback visual: depois de associar, catraca continua aparecendo como "sem atribuição"

---

## Melhorias Propostas

### 1. **Reorganizar Fluxo em 3 Abas Principais** ✨ VIÁVEL

**Atual (confuso):**
- Catálogo de Portarias
- Catálogo de Setores
- Matriz de Acesso
- Dispositivos
- (Catracas monitoradas em tela separada)

**Proposto (linear):**

```
┌─────────────────────────────────────────────────────────────┐
│ CONFIGURAÇÃO DO EVENTO: Teste Alpha                         │
├─────────────────────────────────────────────────────────────┤
│  📍 Portarias      │  🎪 Setores      │  🚪 Catracas        │
├─────────────────────────────────────────────────────────────┤
```

**Aba 1: PORTARIAS (Portões, Entradas, Saídas)**
- Listar portarias com status (ativa, inativa)
- **Coluna nova:** "Catracas atribuídas" → mostra quantas e quais
- **Coluna nova:** "Modo" → Entrada+Saída ou só Entrada
- Botão "+ Nova Portaria"
- Clicar em portaria expande e mostra:
  - Detalhes
  - Setores associados (checkboxes Entry/Exit)
  - Catracas associadas com status online/offline

**Aba 2: SETORES (Áreas, Zonas)**
- Listar setores com capacidade
- **Coluna nova:** "Portarias conectadas" → quantas portarias têm acesso
- Botão "+ Novo Setor"
- Clicar em setor mostra:
  - Portarias que podem acessar (com direção)
  - Capacidade restante

**Aba 3: CATRACAS (Descobertas via MQTT)**
- Listar todas as catracas online/offline
- **Status visual:** 🟢 Online / 🔴 Offline
- **Status associação:** ✅ Atribuída / ⚠️ Sem atribuição
- Um clique em catraca sem atribuição abre MODAL simples:
  ```
  ┌────────────────────────┐
  │ Associar Catraca       │
  ├────────────────────────┤
  │ ID: Catraca151         │
  │ Firmware: Neon 2.1     │
  │ IP: 192.168.1.100      │
  │                        │
  │ Selecionar Portaria:   │
  │ [Dropdown com lista]   │
  │ ⚬ Camarote (online)    │
  │ ⚬ Pista (online)       │
  │ ⚬ VIP (offline)        │
  │                        │
  │ Nome da Catraca:       │
  │ [Preenche automático]  │
  │ Catraca Camarote       │
  │                        │
  │ Tipo de dispositivo:   │
  │ [Mqtt] (pré-selecionado)
  │                        │
  │ [Cancelar] [Associar]  │
  └────────────────────────┘
  ```
- Depois de associar:
  - Catraca desaparece de "Sem Atribuição"
  - Aparece em "Portarias" → expandido → "Catracas"
  - Feedback: ✅ "Catraca151 associada à Portaria Camarote"

---

### 2. **Matriz Visualmente Melhorada** ✨ VIÁVEL

**Atual:** Tabela 2D com checkboxes confusos

**Proposto:**

```
PORTARIA: Camarote                        MODO: Entrada + Saída validadas

┌─────────────────────────────────────┐
│ Setores com acesso a esta portaria: │
├─────────────────────────────────────┤
│ ☑ Camarote      Entrada ↓  Saída ↑  │
│ ☑ VIP           Entrada ↓  Saída ↑  │
│ ☐ Pista         Entrada ↓  Saída ↑  │
│ ☐ Backstage     Entrada ↓  Saída ↑  │
└─────────────────────────────────────┘

SETOR: Camarote

┌─────────────────────────────────────┐
│ Portarias com acesso a este setor:  │
├─────────────────────────────────────┤
│ ☑ Camarote      Entrada ↓  Saída ↑  │
│ ☑ Entrada Sul   Entrada ↓           │
│ ☐ Saída Norte                Saída ↑│
└─────────────────────────────────────┘
```

**Benefícios:**
- Contexto claro: qual portaria ou setor você está editando
- Menos confusão mental: não é uma matriz gigante
- Feedback em tempo real

---

### 3. **Visual Status em Cards** ✨ VIÁVEL

**Para Portarias:**
```
┌──────────────────────────────────────┐
│ 🚪 CAMAROTE                          │
├──────────────────────────────────────┤
│ Código: CAM-01                       │
│ Modo: Entrada + Saída                │
│ Setores: 3 ativos                    │
│ Catracas: 2 online, 0 offline        │
│                    [🔧 Editar]       │
└──────────────────────────────────────┘
```

**Para Setores:**
```
┌──────────────────────────────────────┐
│ 🎪 PISTA                             │
├──────────────────────────────────────┤
│ Capacidade: 5000 | Lotado: Não       │
│ Portarias: 2 conectadas              │
│                    [🔧 Editar]       │
└──────────────────────────────────────┘
```

**Para Catracas:**
```
┌──────────────────────────────────────┐
│ 🟢 CATRACA151 (Online)               │
├──────────────────────────────────────┤
│ Portaria: Camarote                   │
│ Firmware: Neon 2.1                   │
│ IP: 192.168.1.100                    │
│ Última leitura: 2min atrás            │
│                    [↔ Mudar portaria] │
└──────────────────────────────────────┘
```

---

### 4. **Validações e Alertas** ✨ VIÁVEL

**Avisos úteis:**
- ⚠️ "Portaria 'Camarote' criada mas nenhum setor associado"
- ⚠️ "Setor 'VIP' criado mas nenhuma portaria conectada"
- ⚠️ "Catraca151 online mas não atribuída" → botão rápido "Atribuir agora"
- ✅ "Portaria Camarote pronta: 1 catraca online, 3 setores, políticas OK"

---

### 5. **Wizard/Guia Rápido para Setup Inicial** ✨ MUITO VIÁVEL

Para novo evento, ofereceruma tela de setup guiado:

```
SETUP RÁPIDO - Evento Teste Alpha

Passo 1/3: Portarias
────────────────────
Quantas portarias você tem?

 ○ 1-2 (evento pequeno)
 ○ 3-5 (evento médio)
 ○ 6+ (evento grande)

[Nomes comuns]
☑ Entrada Norte
☑ Entrada Sul
☑ Camarote
☑ Pista
☐ VIP
☐ Outra...

[Próximo] [Pular]
```

Depois:

```
Passo 2/3: Setores
─────────────────
Crie os setores:

[Camarote    ] [Capacidade: 2000] [+]
[VIP         ] [Capacidade: 500 ] [+]
[Pista       ] [Capacidade: 5000] [+]

[Próximo] [Pular]
```

Depois:

```
Passo 3/3: Catracas
──────────────────
Catracas descobertas:

☐ Catraca151 (Online)    → [Atribuir a: Dropdown]
☐ Catraca152 (Online)    → [Atribuir a: Dropdown]
☐ Catraca153 (Offline)   → [Atribuir a: Dropdown]

[Concluir]
```

---

### 6. **Dashboard de Status Geral** ✨ VIÁVEL

Adicionar uma tela resumida antes de entrar na configuração:

```
╔════════════════════════════════════════════════════════════╗
║ STATUS: Evento Teste Alpha                      [ATIVO]   ║
╠════════════════════════════════════════════════════════════╣
║                                                            ║
║  Portarias: 4                                              ║
║  ├─ Camarote: 🟢 2 catracas online                        ║
║  ├─ Pista: 🟢 3 catracas online                           ║
║  ├─ VIP: 🔴 0 catracas (offline/não conectada)            ║
║  └─ Entrada: 🟢 1 catraca online                          ║
║                                                            ║
║  Setores: 4                                                ║
║  ├─ Camarote: ✅ Conectado a 2 portarias                  ║
║  ├─ Pista: ✅ Conectado a 2 portarias                     ║
║  ├─ VIP: ⚠️  Conectado a 0 portarias                      ║
║  └─ Backstage: ✅ Conectado a 1 portaria                  ║
║                                                            ║
║  Catracas: 6                                               ║
║  ├─ Online: 5 🟢                                           ║
║  ├─ Offline: 1 🔴                                          ║
║  └─ Sem atribuição: 0 ⚠️                                   ║
║                                                            ║
║  Status de Prontidão: 95% ✅                              ║
║                                                            ║
╚════════════════════════════════════════════════════════════╝
```

Clicando em cada seção, leva para a aba correspondente.

---

### 7. **Buscas e Filtros** ✨ VIÁVEL

Permitir buscar:
- Catraca por nome/ID/IP
- Portaria por nome/código
- Setor por nome
- Filtrar catracas por status (Online, Offline, Sem atribuição)

---

### 8. **Edição em Contexto (Inline)** ✨ PARCIALMENTE VIÁVEL

Permitir editar nomes/códigos diretamente na lista (tipo Trello):
- Clicar no nome de uma portaria
- Editar inline
- Enter para salvar

---

## Priorização de Implementação

### Fase 1 (CRÍTICA - semana 1):
1. ✅ **Modal de Associação de Catraca** - Remove confusão entre telas
2. ✅ **Status Visual em Cards** - Melhora identificação rápida
3. ✅ **Avisos/Alertas** - Previne erros de configuração

### Fase 2 (IMPORTANTE - semana 2):
4. ✅ **Reorganizar em 3 Abas** - Melhora fluxo
5. ✅ **Matriz Contextualizada** - Menos confusa

### Fase 3 (LEGAL - semana 3):
6. ✅ **Dashboard de Status** - Visão geral rápida
7. ✅ **Wizard Setup Inicial** - Facilita primeiros eventos
8. ✅ **Edição Inline** - Agiliza ajustes

---

## Impacto Estimado

| Melhoria | Impacto | Esforço | ROI |
|----------|---------|---------|-----|
| Modal Associação Catraca | Alto | Baixo | 🟢 |
| Status em Cards | Alto | Baixo | 🟢 |
| Avisos Inteligentes | Médio | Médio | 🟢 |
| 3 Abas | Alto | Médio | 🟢 |
| Matriz Contextualizada | Médio | Médio | 🟡 |
| Dashboard Status | Médio | Alto | 🟡 |
| Wizard Setup | Baixo | Alto | 🟡 |
| Edição Inline | Baixo | Médio | 🟡 |

---

## Recomendação

**Comece pela Fase 1 (CRÍTICA)** - são mudanças que:
- ✅ Resolvem o maior problema (catraca fragmentada em 2 telas)
- ✅ Levam ~4-6 horas de trabalho
- ✅ Impactam 100% dos usuários diariamente
- ✅ Podem ser feitas agora sem redesign maior

Depois, se houver tempo, adicione a Fase 2 (abas) que reorganiza tudo de forma mais limpa.

---

**Quer que eu comece pela Fase 1? Vou implementar:**
1. Modal para associar catraca a portaria
2. Cards com status visual
3. Avisos inteligentes
