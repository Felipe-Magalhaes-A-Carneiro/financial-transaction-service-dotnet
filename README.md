<!-- IA diz: Os badges no topo são gerados automaticamente pelo Shields.io.
     Substitua "SEU_USUARIO" pelo seu nome de usuário do GitHub em todos os badges. -->

<div align="center">

# 💳 Financial Transaction Service

**API RESTful assíncrona, escalável e resiliente para processamento de transações financeiras**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-12.0-239120?style=for-the-badge&logo=csharp)](https://docs.microsoft.com/dotnet/csharp/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?style=for-the-badge&logo=postgresql&logoColor=white)](https://postgresql.org/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-3.13-FF6600?style=for-the-badge&logo=rabbitmq&logoColor=white)](https://rabbitmq.com/)
[![Redis](https://img.shields.io/badge/Redis-7.2-DC382D?style=for-the-badge&logo=redis&logoColor=white)](https://redis.io/)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?style=for-the-badge&logo=docker&logoColor=white)](https://docs.docker.com/compose/)

[![CI](https://img.shields.io/github/actions/workflow/status/SEU_USUARIO/financial-transaction-service-dotnet/ci.yml?branch=main&style=for-the-badge&label=CI&logo=github-actions)](https://github.com/SEU_USUARIO/financial-transaction-service-dotnet/actions)
[![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)

</div>

---

## 📋 Índice

- [Sobre o Projeto](#-sobre-o-projeto)
- [Decisões de Arquitetura](#-decisões-de-arquitetura)
- [Stack Tecnológica](#-stack-tecnológica)
- [Estrutura do Projeto](#-estrutura-do-projeto)
- [Conceitos Técnicos Implementados](#-conceitos-técnicos-implementados)
- [Pré-requisitos](#-pré-requisitos)
- [Como Executar](#-como-executar)
- [Endpoints da API](#-endpoints-da-api)
- [Executando os Testes](#-executando-os-testes)
- [Variáveis de Ambiente](#-variáveis-de-ambiente)
- [Roadmap](#-roadmap)

---

## 🎯 Sobre o Projeto

Este projeto é uma implementação de referência de um **serviço de processamento de transações financeiras** construído com as práticas de engenharia exigidas por instituições financeiras de grande porte.

O serviço expõe uma API RESTful que recebe requisições de criação de transações, as persiste de forma segura e as encaminha assincronamente para processamento via fila de mensagens — garantindo rastreabilidade, resiliência a falhas e performance sob alta carga.

### Problema que resolve

Em sistemas financeiros, o processamento de transações enfrenta três desafios críticos simultaneamente:

| Desafio | Consequência se ignorado | Solução implementada |
|---|---|---|
| **Duplicidade de requisições** | Débito duplo na conta do cliente | Idempotência com Redis |
| **Falha no processamento** | Transação perdida sem rastro | Dead Letter Queue (DLQ) |
| **Alta concorrência** | Timeout e indisponibilidade | Processamento assíncrono com RabbitMQ |

---

## 🏛️ Decisões de Arquitetura

### Clean Architecture

O projeto segue os princípios da **Clean Architecture** (Robert C. Martin), organizando o código em quatro camadas com dependências unidirecionais:

```
┌────────────────────────────────────────────────────────────┐
│                    API (Apresentação)                      │
│              Controllers · Middlewares · DI                │
├────────────────────────────────────────────────────────────┤
│                Infrastructure (Infraestrutura)             │
│       EF Core · RabbitMQ · Redis · Repositórios            │
├────────────────────────────────────────────────────────────┤
│                 Application (Casos de Uso)                 │
│          Commands · Queries · DTOs · Interfaces            │
├────────────────────────────────────────────────────────────┤
│                    Domain (Núcleo)                         │
│        Entidades · Value Objects · Domain Events           │
└────────────────────────────────────────────────────────────┘
         ↑ Dependências fluem apenas de fora para dentro ↑
```

**Benefício prático:** a camada Domain — onde vivem as regras de negócio financeiro — não possui nenhuma dependência de framework externo. Isso significa que as regras críticas (validação de saldo, limites de transação) podem ser testadas de forma completamente isolada, sem banco de dados ou servidor HTTP.

### Fluxo de uma Transação

```
Cliente
  │
  │ POST /api/v1/transactions
  ▼
┌─────────────────────────┐
│  IdempotencyMiddleware  │──► Redis: correlation_id já existe? ──► 200 OK (cached)
└──────────┬──────────────┘
           │ (primeira requisição)
           ▼
┌─────────────────────────┐
│  TransactionsController │
└──────────┬──────────────┘
           │ MediatR → CreateTransactionCommand
           ▼
┌─────────────────────────┐
│  CommandHandler         │──► PostgreSQL: persiste Transaction (Status: Pending)
└──────────┬──────────────┘
           │
           │──► RabbitMQ: publica em transactions.exchange
           │
           ▼
       202 Accepted (+ Location header)
           │
           │   (processamento assíncrono)
           ▼
┌─────────────────────────┐
│ TransactionConsumer     │◄── RabbitMQ: consome transactions.process.queue
│  (Background Service)   │
└──────────┬──────────────┘
           │ MediatR → ProcessTransactionCommand
           ▼
┌─────────────────────────┐
│  ProcessHandler         │──► PostgreSQL: atualiza Status (Completed / Failed)
└─────────────────────────┘
           │
           │ (em caso de falha)
           ▼
    transactions.dead.queue (DLQ)
```

---

## 🛠️ Stack Tecnológica

| Categoria | Tecnologia | Justificativa |
|---|---|---|
| **Runtime** | .NET 8 / C# 12 | LTS, performance líder em benchmarks web (TechEmpower) |
| **Banco de dados** | PostgreSQL 16 | ACID compliance obrigatório em sistemas financeiros |
| **ORM** | Entity Framework Core 8 | Code-first, migrations versionadas, proteção N+1 via projeções |
| **Mensageria** | RabbitMQ 3.13 | AMQP, DLQ nativa, altamente adotado no mercado financeiro brasileiro |
| **Cache/Idempotência** | Redis 7.2 | Sub-millisecond latency, TTL nativo, padrão de mercado |
| **Mediator** | MediatR | Desacoplamento entre Controller e lógica de negócio; CQRS |
| **Validação** | FluentValidation | Regras de validação expressivas e testáveis isoladamente |
| **Contêineres** | Docker + Compose | Ambiente 100% reproduzível do dev ao deploy |
| **Testes** | xUnit + Testcontainers | Testes de integração reais contra infraestrutura em container |
| **Logging** | Serilog | Logging estruturado (JSON), essencial para observabilidade |

---

## 📁 Estrutura do Projeto

```
financial-transaction-service-dotnet/
├── src/
│   ├── FinancialTransactionService.Api/          # Camada de Apresentação
│   │   ├── Controllers/                          # Endpoints REST
│   │   └── Middleware/                           # Idempotência, Exceções
│   ├── FinancialTransactionService.Application/  # Casos de Uso (CQRS)
│   │   ├── Transactions/Commands/                # Escrita: criar, processar
│   │   └── Transactions/Queries/                 # Leitura: consultar
│   ├── FinancialTransactionService.Domain/       # Núcleo do negócio
│   │   ├── Entities/                             # Transaction, Account
│   │   └── ValueObjects/                         # Money, TransactionId
│   └── FinancialTransactionService.Infrastructure/ # Implementações externas
│       ├── Persistence/                          # EF Core + Repositórios
│       ├── Messaging/                            # RabbitMQ Publisher
│       └── Caching/                              # Redis Idempotency
├── tests/
│   ├── FinancialTransactionService.UnitTests/
│   └── FinancialTransactionService.IntegrationTests/
├── docker/
│   ├── postgres/init.sql
│   └── rabbitmq/definitions.json
├── docker-compose.yml
└── docker-compose.override.yml
```

---

## 🧠 Conceitos Técnicos Implementados

### 1. Idempotência com Redis
Toda requisição carrega um header `X-Correlation-Id` (UUID v4). Antes de processar, o middleware verifica no Redis se aquele ID já foi processado. Em caso positivo, retorna a resposta cacheada — **zero processamento duplicado**, fundamental para debitar/creditar contas.

```http
POST /api/v1/transactions
X-Correlation-Id: 550e8400-e29b-41d4-a716-446655440000
```

### 2. Dead Letter Queue (DLQ) com RabbitMQ
Mensagens que falham no processamento (ex: saldo insuficiente, timeout) são roteadas automaticamente para a `transactions.dead.queue` com metadados de falha. Isso garante **zero perda de mensagens** e auditabilidade completa.

```
transactions.process.queue
    │ (falha após N tentativas)
    ▼
transactions.exchange.dlx → transactions.dead.queue
```

### 3. Prevenção de N+1 com EF Core
Consultas que retornam listas de transações utilizam **projeções diretas para DTOs** com `.Select()`, evitando o carregamento de entidades completas e o subsequente N+1 de lazy loading — um dos problemas de performance mais comuns em sistemas ORM.

```csharp
// Padrão implementado (sem N+1):
var transactions = await context.Transactions
    .Where(t => t.AccountId == accountId)
    .Select(t => new TransactionDto(t.Id, t.Amount, t.Status))
    .ToListAsync();
```

### 4. Clean Architecture + CQRS com MediatR
Cada operação é um `Command` (escrita) ou `Query` (leitura) isolado, com seu próprio Handler, Validator e DTO. Isso garante que **adicionar um novo caso de uso nunca quebra os existentes** — princípio Open/Closed em ação.

---

## ✅ Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) ou superior
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (com Docker Compose v2)
- [Git](https://git-scm.com/)

---

## 🚀 Como Executar

### 1. Clone o repositório
```bash
git clone https://github.com/Felipe-Magalhaes-A-Carneiro/financial-transaction-service-dotnet.git
cd financial-transaction-service-dotnet
```

### 2. Configure as variáveis de ambiente
```bash
cp .env.example .env
# Edite o .env com suas credenciais locais (os valores padrão já funcionam para dev)
```

### 3. Suba a infraestrutura
```bash
# Inicia PostgreSQL, RabbitMQ e Redis em background
docker compose up postgres rabbitmq redis -d

# Acompanhe os logs até todos os serviços estarem "healthy"
docker compose ps
```

### 4. Execute as migrations do banco de dados
```bash
# A ser executado após o Capítulo 4
dotnet ef database update \
  --project src/FinancialTransactionService.Infrastructure \
  --startup-project src/FinancialTransactionService.Api
```

### 5. Inicie a API
```bash
dotnet run --project src/FinancialTransactionService.Api
```

A API estará disponível em:
- **HTTP:** `http://localhost:8080`
- **Swagger UI:** `http://localhost:8080/swagger`
- **RabbitMQ Management:** `http://localhost:15672`

---

## 📡 Endpoints da API

> Documentação interativa completa disponível via Swagger UI em `/swagger`

| Método | Endpoint | Descrição | Status |
|---|---|---|---|
| `POST` | `/api/v1/transactions` | Cria e enfileira uma transação | 🔜 Cap. 7 |
| `GET` | `/api/v1/transactions/{id}` | Consulta uma transação por ID | 🔜 Cap. 7 |
| `GET` | `/api/v1/transactions` | Lista transações de uma conta | 🔜 Cap. 7 |
| `GET` | `/health` | Health check da API e dependências | 🔜 Cap. 9 |

### Exemplo de Requisição

```http
POST /api/v1/transactions HTTP/1.1
Content-Type: application/json
Authorization: Bearer {jwt_token}
X-Correlation-Id: 550e8400-e29b-41d4-a716-446655440000

{
  "sourceAccountId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "destinationAccountId": "b2c3d4e5-f6a7-8901-bcde-f01234567891",
  "amount": 1500.00,
  "currency": "BRL",
  "type": "Transfer",
  "description": "Pagamento de serviço"
}
```

```http
HTTP/1.1 202 Accepted
Location: /api/v1/transactions/c3d4e5f6-a7b8-9012-cdef-012345678901
X-Correlation-Id: 550e8400-e29b-41d4-a716-446655440000
```

---

## 🧪 Executando os Testes

```bash
# Todos os testes
dotnet test

# Somente testes unitários (rápidos, sem Docker)
dotnet test --filter Category=Unit

# Somente testes de integração (requer Docker para Testcontainers)
dotnet test --filter Category=Integration

# Com relatório de cobertura
dotnet test --collect:"XPlat Code Coverage"
```

> Os testes de integração utilizam **Testcontainers** — sobem containers reais de PostgreSQL e Redis automaticamente durante a execução, sem necessidade de ambiente externo configurado.

---

## ⚙️ Variáveis de Ambiente

| Variável | Descrição | Padrão (dev) |
|---|---|---|
| `POSTGRES_USER` | Usuário do PostgreSQL | `fts_user` |
| `POSTGRES_PASSWORD` | Senha do PostgreSQL | — |
| `POSTGRES_DB` | Nome do banco de dados | `financial_transactions` |
| `RABBITMQ_USER` | Usuário do RabbitMQ | `fts_rabbit` |
| `RABBITMQ_PASSWORD` | Senha do RabbitMQ | — |
| `REDIS_PASSWORD` | Senha do Redis | — |
| `Jwt__SecretKey` | Chave secreta para assinar JWT | — |

> ⚠️ **Nunca** versione o arquivo `.env` ou qualquer secret no repositório. Use `.env.example` como template.

---

## 📍 Roadmap

- [x] **Cap. 1** — Fundação: Solution, Docker Compose, Documentação
- [ ] **Cap. 2** — Domínio: Entidades, Value Objects, Domain Events
- [ ] **Cap. 3** — Aplicação: Commands, Queries, Validators (CQRS + MediatR)
- [ ] **Cap. 4** — Infraestrutura: EF Core, PostgreSQL, Migrations
- [ ] **Cap. 5** — Mensageria: RabbitMQ Publisher e Consumer (DLQ)
- [ ] **Cap. 6** — Idempotência: Redis Middleware
- [ ] **Cap. 7** — API: Controllers, JWT, Swagger, Versionamento
- [ ] **Cap. 8** — Testes: Unitários e Integração com Testcontainers
- [ ] **Cap. 9** — Observabilidade: Serilog, Health Checks, CI/CD

---

## 📄 Licença

Distribuído sob a licença MIT. Veja [`LICENSE`](LICENSE) para mais detalhes.

---

<div align="center">
  Desenvolvido por <strong>Felipe Magalhães de Araujo Carneiro</strong> · 
  <a href="https://www.linkedin.com/in/felipe-magalhaes-arq/">LinkedIn</a> · 
  <a href="https://github.com/Felipe-Magalhaes-A-Carneiro">GitHub</a>
</div>