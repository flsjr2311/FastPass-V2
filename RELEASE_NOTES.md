# Release Notes — FastPass V2

## v2.0.0 - Commit Inicial (2025)

Primeira versão funcional completa do backend FastPass V2 — reescrita total do sistema legado com arquitetura moderna.

---

### Novidades

#### Arquitetura
- Clean Architecture com 5 projetos: Api, Application, Domain, Infrastructure, Worker
- .NET 8.0 com Minimal APIs (sem controllers)
- MySQL via ADO.NET puro (MySqlConnector 2.4.0)
- Migrações automáticas com SQL embarcado (15 migrações)
- Seed automático do usuário administrador

#### Autenticação e RBAC
- Sistema de sessões por cookie httpOnly (`fp_token`) com hash SHA-256
- Login com bloqueio automático após tentativas inválidas
- 22 permissões granulares organizadas por módulo
- 6 perfis padrão: Administrador, Gestor Operacional, Operador de Portaria, Auditor, Consulta, Cliente Online
- Permissões privilegiadas (escopo.global, perfil.permissoes.gerenciar)
- Escopo de acesso por evento e por portaria (por usuário)
- CRUD completo de usuários e perfis

#### Catálogo de Eventos
- Clientes (organizadores) com CRUD
- Locais (venues) com endereço, cidade, estado, capacidade
- Eventos com ciclo de vida completo: Draft → Preparing → Published → Running → Closed → Archived
- Setores por evento com capacidade
- Portarias com modo de operação (entrada validada / entrada+saída)
- Dispositivos por portaria (tipos: Legacy, Serial, Vcom, Mqtt, Simulator)
- Matriz portaria × setor com direção e janelas temporais
- Tipos de ingresso (com/sem reentrada)
- Lotes de ingressos com limites e período de venda
- Exclusão em cascata (evento → tickets → acessos → importações)

#### Ingressos
- Emissão individual de tickets
- Importação CSV com detecção automática de separador
- Preview de importação com mapeamento configurável de colunas
- 3 modos de importação: Adicionar+Atualizar, Só Adicionar, Só Atualizar
- Detecção de setores desconhecidos durante importação
- Alteração de status individual e em massa (bulk)
- Histórico de importações com contagem de resultados

#### Validação de Acesso (Core)
- 3 endpoints de validação: App, Catraca, Genérico
- Suporte a tickets e crachás de staff
- Idempotência por `IdempotencyKey` (evita duplicatas)
- Políticas de acesso configuráveis (allow/deny com prioridade)
- Filtros por: tipo de ingresso, lote, portaria, setor, direção
- Resposta estruturada com: decisão, ação do braço, pictograma, mensagem
- Controle de usos máximos e reentrada
- Contagem de pessoas dentro do evento/setor

#### Mensagens de Validação
- Templates globais de mensagem (padrão do sistema)
- Override de mensagens por evento
- Restauração ao padrão com DELETE
- Mensagens personalizáveis: título, texto, pictograma

#### Staff (Colaboradores)
- CRUD de membros com código, nome, departamento
- Credenciais (crachás) vinculadas ao membro
- Permissões de acesso por evento/portaria/setor com período
- Validação de crachá integrada ao fluxo de tickets

#### Relatórios e Exportação
- Histórico de tentativas de acesso com filtros avançados
- Paginação configurável
- Sumário de validações (aprovadas, rejeitadas, por período)
- Exportação CSV de tentativas de acesso
- Exportação ZIP completa (tickets + acessos)
- Resumo de tickets (quantidades por status, pessoas dentro)

#### Administração
- Reset de contadores de acesso (zera usos sem apagar tentativas)
- Exclusão irreversível de dados de evento (com confirmação textual)
- CORS configurável para frontend

#### Infraestrutura
- 15 migrações SQL aplicadas incrementalmente
- Health-check de banco de dados (`/health/database`)
- Worker com probe de conectividade (heartbeat 30s)
- CORS para frontend dev (localhost:5173)

---

### Migrações de Banco de Dados

| # | Arquivo | Conteúdo |
|---|---------|----------|
| 001 | `001_initial_v2.sql` | Schema inicial completo |
| 002 | `002_staff_credentials.sql` | Tabelas de credenciais de staff |
| 003 | `003_staff_access_key.sql` | Chave de acesso staff |
| 004 | `004_access_attempt_sector.sql` | Coluna setor na tentativa de acesso |
| 005 | `005_turnstile_validation.sql` | Suporte a validação por catraca |
| 006 | `006_access_messages.sql` | Mensagens de acesso configuráveis |
| 007 | `007_auth.sql` | Sistema de autenticação (usuários, sessões, perfis) |
| 008 | `008_roles_scope.sql` | Escopo de perfis por evento/portaria |
| 009 | `009_ticket_imports.sql` | Tabelas de importação de tickets |
| 010 | `010_fix_import_columns.sql` | Correção de colunas de importação |
| 011 | `011_clients.sql` | Tabela de clientes/organizadores |
| 012 | `012_staff_optional_code.sql` | Código de staff opcional |
| 013 | `013_user_physical_access.sql` | Acesso físico por usuário |
| 014 | `014_message_templates.sql` | Templates de mensagem globais |
| 015 | `015_ticket_sector.sql` | Setor vinculado ao ticket |

---

### Permissões do Sistema

| Código | Descrição |
|--------|-----------|
| `acesso.validar` | Validar acesso em portaria |
| `relatorio.ler` | Consultar relatórios operacionais |
| `acessos.ler` | Consultar log de acessos |
| `auditoria.ler` | Consultar auditoria administrativa |
| `cliente.gerenciar` | Gerenciar clientes e organizadores |
| `evento.criar` | Criar eventos |
| `evento.editar` | Editar eventos |
| `evento.excluir` | Excluir eventos |
| `portaria.gerenciar` | Gerenciar portarias |
| `setor.gerenciar` | Gerenciar setores |
| `relacao.gerenciar` | Gerenciar matriz portaria × setor |
| `dispositivo.gerenciar` | Gerenciar dispositivos e catracas |
| `ticket.consultar` | Consultar ingressos e histórico |
| `ticket.status` | Alterar status de ingressos |
| `ticket.importar` | Importar ingressos via CSV |
| `ticket.dados.excluir` | Excluir dados de ingressos de evento |
| `mensagem.gerenciar` | Configurar mensagens de validação |
| `staff.gerenciar` | Gerenciar colaboradores e crachás |
| `usuario.gerenciar` | Gerenciar usuários do sistema |
| `perfil.gerenciar` | Gerenciar perfis |
| `perfil.permissoes.gerenciar` | Atribuir permissões a perfis (privilegiada) |
| `sessao.revogar` | Revogar sessões de outros usuários |
| `escopo.global` | Acesso global a todos os eventos e portarias (privilegiada) |

---

### Limitações Conhecidas

- **Sem comunicação real com catracas** — os enums `DeviceType.Vcom` e `DeviceType.Mqtt` existem mas a integração não está implementada
- **Worker é placeholder** — apenas faz health-check do banco a cada 30s
- **Program.cs monolítico** — 1127 linhas com alguns trechos de SQL inline
- **Sem testes automatizados**
- **Sem Swagger/OpenAPI**
- **Sem Docker/CI/CD**

---

### Próxima Release (Planejado)

- Integração com catracas USR-Vcom (serial/TCP)
- Integração com catracas via MQTT
- Worker processando outbox de comandos para dispositivos
- App Android para validação de ingressos (QR Code/barras)
- Testes automatizados
