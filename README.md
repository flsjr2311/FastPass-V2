# FastPass V2

Sistema de controle de acesso para eventos com validação em tempo real de ingressos e crachás de acesso físico via catracas, app mobile e API.

## Visao Geral

O FastPass V2 gerencia todo o ciclo de vida de controle de acesso em eventos: desde o cadastro de locais e eventos, importação de ingressos, configuração de políticas de acesso, até a validação em tempo real na portaria (via app Android ou catraca física).

## Arquitetura

```
┌─────────────────────────────────────────────────────────────────┐
│                         Clientes                                │
│   App Android  │  Frontend Web (Vite)  │  Catracas (Vcom/MQTT) │
└───────┬────────┴──────────┬─────────────┴──────────┬────────────┘
        │                   │                        │
        ▼                   ▼                        ▼
┌─────────────────────────────────────────────────────────────────┐
│                      FastPass.Api                                │
│              ASP.NET Core 8 - Minimal APIs                      │
│         Autenticação por cookie (SessionMiddleware)             │
└───────────────────────────────┬─────────────────────────────────┘
                                │
        ┌───────────────────────┼───────────────────────┐
        ▼                       ▼                       ▼
┌──────────────┐     ┌──────────────────┐     ┌──────────────────┐
│  Application │     │  Infrastructure  │     │     Domain       │
│  (Contratos) │     │  (MySQL/ADO.NET) │     │   (Entidades)   │
└──────────────┘     └──────────────────┘     └──────────────────┘
                                │
                                ▼
                    ┌────────────────────┐
                    │   MySQL 8.0+      │
                    │  fastpass_v2_dev   │
                    └────────────────────┘
```

### Projetos

| Projeto | Tipo | Responsabilidade |
|---------|------|------------------|
| `FastPass.Api` | ASP.NET Core Web | Endpoints HTTP, middleware de sessão, CORS |
| `FastPass.Application` | Class Library | Interfaces, contratos (commands/views), exceções |
| `FastPass.Domain` | Class Library | Entidades e enums do domínio |
| `FastPass.Infrastructure` | Class Library | Implementações MySQL, migrações, ADO.NET |
| `FastPass.Worker` | Worker Service | Health-check + serviço MQTT das catracas (recebe leitura, valida, comanda liberar/negar) |
| `web/` | React + Vite | Painel web (dashboard, catálogo, tickets, auditoria, relatórios) |
| `android/` | Kotlin + Compose | App de validação na portaria (câmera, leitor USB-C, manual) |

## Stack Tecnológica

- **.NET 8.0** (C# 12)
- **ASP.NET Core Minimal APIs** (sem controllers)
- **MySQL 8.0+** via MySqlConnector 2.4.0
- **ADO.NET puro** (sem ORM)
- **Autenticação**: sessões por cookie (`fp_token`) com hash SHA-256
- **RBAC**: 22 permissões granulares com perfis configuráveis

## Funcionalidades Implementadas

### Autenticação e Autorização
- Login/logout com cookie httpOnly
- Sessões no banco com expiração
- RBAC completo (usuários, perfis, permissões)
- Bloqueio por tentativas de login
- Escopo por evento/portaria por usuário

### Catálogo
- Clientes (organizadores)
- Locais (venues) com endereço e capacidade
- Eventos com ciclo de vida (Draft → Running → Archived)
- Setores por evento
- Portarias por evento (modo: entrada validada / entrada+saída validada)
- Dispositivos por portaria (tipo: Legacy, Serial, Vcom, Mqtt, Simulator)
- Matriz portaria × setor (com direção e janela temporal)

### Ingressos
- Tipos de ingresso (com/sem reentrada)
- Lotes com limites de quantidade e período de venda
- Campo de lote (`batch_name`) por ticket, lido do CSV
- Setor vinculado ao ticket
- Emissão individual
- Importação CSV com preview (separador automático, mapeamento de colunas: código, ID externo, setor, lote, status)
- Modos: Adicionar+Atualizar, Só Adicionar, Só Atualizar
- Tela de tickets com busca por código, filtro por status e alteração de status (individual e em massa)

### Validação de Acesso (Core)
- Endpoints: `/api/access/app/validate`, `/api/access/turnstile/validate`, `/api/access/validate`
- Validação de tickets e crachás de acesso físico de usuários
- **Direção inferida automaticamente** pela portaria e pelo estado do ticket (não precisa enviar no request)
- **Restrição por setor**: ticket só é liberado na portaria associada ao seu setor
- Políticas configuráveis (allow/deny por tipo, lote, portaria, setor, direção)
- Idempotência por `IdempotencyKey`
- Resposta com: decisão, ação do braço, pictograma, mensagem configurável
- Controle de usos/reentrada

### Mensagens de Validação
- Templates globais (padrão do sistema)
- Override por evento
- Restauração ao padrão

### Crachá de Acesso Físico (Usuários)
- Habilitado por usuário (`physical_access_enabled` + `access_badge_code`)
- Validado direto pela tabela de usuários, no mesmo fluxo dos tickets
- Autorização pelo escopo de eventos/portarias do usuário

### Validação Manual
- Tela para o operador liberar acesso em exceções (backstage, convidados, falha de catraca)
- Escolha de portaria e setor de validação
- Contabiliza na portaria/setor e marca o registro como `Manual` (canal), visível na auditoria
- Restrita a Administrador, Gestor Operacional e Operador de Portaria (`acesso.validar`)

### App Android (validação na portaria)
- App em `android/` (Kotlin + Jetpack Compose). Login do operador, escolha de evento/portaria e leitura do QR/código.
- Leitura por **câmera** (CameraX + ML Kit), **leitor USB-C** (HID) ou **digitação manual**.
- Feedback verde/vermelho com som e vibração; feed das últimas validações.
- Usa a sessão do operador e exige `acesso.validar`. Identifica o aparelho (nome + id de instalação) na auditoria.
- Detalhes de build e configuração em `android/README.md`.

### Relatórios
- Histórico de tentativas de acesso (filtros, paginação)
- Sumário de validações
- Exportação CSV
- Exportação ZIP (tickets + acessos)
- Resumo de tickets (contagens por status, pessoas dentro)

### Administração
- Reset de contadores de acesso
- Exclusão de dados de evento (com confirmação)
- Reset de senha de outro usuário por administrador (não exige a senha atual; limpa bloqueio e revoga sessões)

### Trilhas de auditoria (requer `auditoria.ler`)
- **Acessos ao sistema** (`fp_login_log`): tentativas de login com desfecho (sucesso, senha inválida, usuário inexistente, conta bloqueada, usuário inativo), usuário, IP e dispositivo.
- **Trilha de auditoria de ações** (`fp_audit_trail`): toda ação que muda estado (POST/PUT/DELETE/PATCH bem-sucedidos) capturada por middleware — quem, quando, ação legível, método+rota, alvo, status e resumo do corpo com senhas/tokens mascarados.
- Duas telas dedicadas no grupo Logs, com filtros e paginação. Gravação *best-effort* (nunca interrompe a operação).

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- MySQL 8.0+ (local ou remoto)
- (Opcional) Node.js 18+ para o frontend

## Configuração

1. Clone o repositório:
```bash
git clone https://github.com/flsjr2311/FastPass-V2.git
cd FastPass-V2
```

2. Configure a connection string em `src/FastPass.Api/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "FastPass": "Server=127.0.0.1;Port=3306;Database=fastpass_v2_dev;User ID=<user>;Password=<senha>;SslMode=None;AllowPublicKeyRetrieval=True;"
  },
  "Auth": {
    "AdminUser": "admin",
    "AdminName": "Administrador",
    "AdminPassword": "SuaSenhaForte123!"
  }
}
```

> ⚠️ **`AllowPublicKeyRetrieval=True` é obrigatório** com MySQL 8, que usa
> `caching_sha2_password` por padrão. Sem essa flag (e com `SslMode=None`), o
> MySqlConnector falha na conexão com:
> `Authentication method 'caching_sha2_password' failed.`

> ⚠️ **User-secrets tem precedência sobre o `appsettings.json` em Development.**
> Se existir um `ConnectionStrings:FastPass` nos user-secrets, editar o
> `appsettings.json` **não tem efeito nenhum** e a alteração é ignorada em silêncio.
> Antes de depurar conexão, verifique o que está realmente em uso:
> ```bash
> dotnet user-secrets list --project src/FastPass.Api
> ```
> Para definir a connection string por lá (recomendado, mantém a senha fora do Git):
> ```bash
> dotnet user-secrets set "ConnectionStrings:FastPass" "Server=127.0.0.1;Port=3306;Database=fastpass_v2_dev;User ID=<user>;Password=<senha>;SslMode=None;AllowPublicKeyRetrieval=True;" --project src/FastPass.Api
> ```

3. Execute a API (as migrações rodam automaticamente em Development):
```bash
cd src/FastPass.Api
dotnet run
```

A API estará disponível em `http://localhost:5088` (definido em `Properties/launchSettings.json`).
Health-check do banco: `GET http://localhost:5088/health/database`.

## Estrutura do Banco de Dados

As migrações são aplicadas automaticamente no ambiente Development. Arquivos SQL em:
`src/FastPass.Infrastructure/Database/Migrations/`

| Migração | Descrição |
|----------|-----------|
| 001 | Schema inicial (eventos, tickets, portarias, setores, dispositivos, tentativas) |
| 002 | Credenciais de staff *(removido na 018)* |
| 003 | Chave de acesso staff *(removido na 018)* |
| 004 | Setor na tentativa de acesso |
| 005 | Validação de catraca (turnstile) |
| 006 | Mensagens de acesso configuráveis |
| 007 | Autenticação (usuários, sessões, perfis, permissões) |
| 008 | Escopo de perfis por evento/portaria |
| 009 | Importação de tickets |
| 010 | Fix colunas de importação |
| 011 | Clientes (organizadores) |
| 012 | Código staff opcional *(removido na 018)* |
| 013 | Acesso físico por usuário |
| 014 | Templates de mensagem globais |
| 015 | Setor no ticket |
| 016 | Códigos legíveis (evento/portaria/setor) |
| 017 | Lote (batch_name) no ticket |
| 018 | Remoção completa da função de staff |
| 019 | Trilha de acessos ao sistema (`fp_login_log`) |
| 020 | Trilha de auditoria de ações (`fp_audit_trail`) |
| 021 | Identificação do aparelho do app em `fp_access_attempts` |
| 022 | Presença de catracas MQTT (`fp_turnstile_presence`) para o painel de monitoramento |
| 030 | Override de modo por catraca (`fp_devices.operation_mode`, NULL = herda da portaria) |
| 032 | Estrutura da tabela de templates de mensagem da catraca |
| 033 | Carga de segurança dos templates de mensagem |
| 034 | Repopulação dos 31 templates (00–30) |
| 035 | Remoção de device duplicado (Catraca151) |
| 036 | Separa `fp_event_gates.turnstile_mode` (modo físico da catraca) de `operation_mode` (política de validação da portaria) |

## Endpoints Principais

### Públicos
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/` | Health check (versão, status) |
| GET | `/health/database` | Verificação de conectividade com o banco |
| POST | `/api/auth/login` | Login |

### Autenticação (requer sessão)
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/auth/me` | Sessão atual |
| POST | `/api/auth/logout` | Encerrar sessão |
| PUT | `/api/auth/password` | Alterar senha |

### Catálogo (requer permissões específicas)
| Método | Rota | Descrição |
|--------|------|-----------|
| CRUD | `/api/clients` | Clientes |
| CRUD | `/api/venues` | Locais |
| CRUD | `/api/events` | Eventos |
| CRUD | `/api/events/{id}/sectors` | Setores |
| CRUD | `/api/events/{id}/gates` | Portarias |
| CRUD | `/api/events/{id}/gates/{id}/devices` | Dispositivos |
| CRUD | `/api/events/{id}/gate-sectors` | Matriz portaria × setor |

### Validação de Acesso
| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/access/app/validate` | Validação via App (requer sessão + `acesso.validar`) |
| POST | `/api/access/turnstile/validate` | Validação via Catraca |
| POST | `/api/access/manual/validate` | Validação manual pelo operador (requer sessão + `acesso.validar`) |
| POST | `/api/access/validate` | Validação genérica |

### Usuários e auditoria
| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/users/{id}/reset-password` | Reset de senha por admin (requer `usuario.gerenciar`) |
| GET | `/api/login-log` | Trilha de acessos ao sistema (requer `auditoria.ler`) |
| GET | `/api/audit-trail` | Trilha de auditoria de ações (requer `auditoria.ler`) |
| GET | `/api/turnstiles` | Monitoramento de catracas MQTT (requer `dispositivo.gerenciar`) |

## Pendências e Roadmap

### Concluído recentemente (v2.1.0)
- [x] Frontend web (login, dashboard, catálogo, tickets, auditoria, importação, relatórios)
- [x] Direção de acesso inferida automaticamente
- [x] Restrição de ticket por setor da portaria
- [x] Crachá de acesso físico validado direto por usuário (staff removido)
- [x] Códigos legíveis (evento/portaria/setor)
- [x] Lote (batch_name) no ticket via CSV
- [x] Busca/filtro/alteração de status na tela de tickets (restrita por permissão)
- [x] Validação manual pelo operador (canal Manual, marcado na auditoria)
- [x] Reset de senha de outro usuário por administrador
- [x] Trilha de acessos ao sistema (login) e trilha de auditoria de ações (middleware)
- [x] **App Android** — validação de ingressos (câmera/leitor USB-C/manual) com sessão do operador e identificação do aparelho

### Em andamento
- [~] **Integração MQTT com catracas** — fundação pronta no `FastPass.Worker` (conecta no broker,
  recebe telemetria/leitura, valida via canal Turnstile e comanda liberar/negar) + **painel de
  monitoramento** (tela "Catracas": placas identificadas, online/offline, firmware/IP e atribuição
  a portaria/evento). Falta confirmar o protocolo de leitura/comando do firmware (Neon 1.2) e o
  teste fim-a-fim com a catraca girando. Notas em `tools/mqtt/PROTOCOLO-NEON.md`.

### Prioridade Alta
- [ ] Confirmar protocolo da placa (verbo/payload de leitura e de comando) e cadastrar a catraca como device `Mqtt`
- [ ] Teste fim-a-fim MQTT: QR real → giro da catraca no sentido correto
- [ ] **Integração com catracas Vcom** — comunicação serial/TCP com catracas USR-Vcom
- [ ] **Worker de sync** — processamento do outbox para dispositivos
- [ ] Compilar o app Android no Android Studio e validar em campo (câmera + leitor USB-C real)

### Prioridade Média
- [ ] Testes automatizados (unitários + integração)
- [ ] Swagger/OpenAPI
- [ ] Refatorar Program.cs (extrair endpoints em módulos)
- [ ] Docker + docker-compose
- [ ] CI/CD pipeline

### Prioridade Baixa
- [ ] Modo offline no app Android
- [ ] Rate limiting no login
- [ ] Logs estruturados (Serilog)
- [ ] Métricas e monitoramento

## Licença

Projeto privado - FLSJR Sistemas.
