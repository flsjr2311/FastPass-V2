# FastPass V2

Sistema de controle de acesso para eventos com validação em tempo real de ingressos e credenciais de staff via catracas, app mobile e API.

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
| `FastPass.Worker` | Worker Service | Serviço de background (health-check, futuro: sync) |

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
- Emissão individual
- Importação CSV com preview (separador automático, mapeamento de colunas)
- Modos: Adicionar+Atualizar, Só Adicionar, Só Atualizar
- Alteração de status em massa

### Validação de Acesso (Core)
- Endpoints: `/api/access/app/validate`, `/api/access/turnstile/validate`, `/api/access/validate`
- Validação de tickets e crachás de staff
- Políticas configuráveis (allow/deny por tipo, lote, portaria, setor, direção)
- Idempotência por `IdempotencyKey`
- Resposta com: decisão, ação do braço, pictograma, mensagem configurável
- Controle de usos/reentrada

### Mensagens de Validação
- Templates globais (padrão do sistema)
- Override por evento
- Restauração ao padrão

### Staff (Colaboradores)
- Cadastro de membros e credenciais (crachás)
- Permissão de acesso por evento/portaria/setor
- Validação de crachá no mesmo fluxo de tickets

### Relatórios
- Histórico de tentativas de acesso (filtros, paginação)
- Sumário de validações
- Exportação CSV
- Exportação ZIP (tickets + acessos)
- Resumo de tickets (contagens por status, pessoas dentro)

### Administração
- Reset de contadores de acesso
- Exclusão de dados de evento (com confirmação)
- Auditoria de ações administrativas

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
    "FastPass": "Server=127.0.0.1;Port=3306;Database=fastpass_v2_dev;User ID=<user>;Password=<senha>;SslMode=None;"
  },
  "Auth": {
    "AdminUser": "admin",
    "AdminName": "Administrador",
    "AdminPassword": "SuaSenhaForte123!"
  }
}
```

3. Execute a API (as migrações rodam automaticamente em Development):
```bash
cd src/FastPass.Api
dotnet run
```

A API estará disponível em `http://localhost:5062`.

## Estrutura do Banco de Dados

As migrações são aplicadas automaticamente no ambiente Development. Arquivos SQL em:
`src/FastPass.Infrastructure/Database/Migrations/`

| Migração | Descrição |
|----------|-----------|
| 001 | Schema inicial (eventos, tickets, portarias, setores, dispositivos, tentativas) |
| 002 | Credenciais de staff |
| 003 | Chave de acesso staff |
| 004 | Setor na tentativa de acesso |
| 005 | Validação de catraca (turnstile) |
| 006 | Mensagens de acesso configuráveis |
| 007 | Autenticação (usuários, sessões, perfis, permissões) |
| 008 | Escopo de perfis por evento/portaria |
| 009 | Importação de tickets |
| 010 | Fix colunas de importação |
| 011 | Clientes (organizadores) |
| 012 | Código staff opcional |
| 013 | Acesso físico por usuário |
| 014 | Templates de mensagem globais |
| 015 | Setor no ticket |

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
| POST | `/api/access/app/validate` | Validação via App |
| POST | `/api/access/turnstile/validate` | Validação via Catraca |
| POST | `/api/access/validate` | Validação genérica |

## Pendências e Roadmap

### Prioridade Alta
- [ ] **Integração com catracas Vcom** — comunicação serial/TCP com catracas USR-Vcom
- [ ] **Integração MQTT** — comunicação com catracas via broker MQTT
- [ ] **App Android** — leitura de QR Code/barras + validação de ingressos
- [ ] **Worker funcional** — processamento do outbox de sync para dispositivos

### Prioridade Média
- [ ] Testes automatizados (unitários + integração)
- [ ] Swagger/OpenAPI
- [ ] Refatorar Program.cs (extrair endpoints em módulos)
- [ ] Docker + docker-compose
- [ ] CI/CD pipeline

### Prioridade Baixa
- [ ] Frontend web completo (dashboard administrativo)
- [ ] Modo offline no app Android
- [ ] Rate limiting no login
- [ ] Logs estruturados (Serilog)
- [ ] Métricas e monitoramento

## Licença

Projeto privado - FLSJR Sistemas.
