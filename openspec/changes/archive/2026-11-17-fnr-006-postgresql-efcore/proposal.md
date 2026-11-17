# Proposal

## Why

O backend criado pela FNR-003 ainda não possui persistência nem configuração do EF Core/Npgsql. A FNR-006 precisa validar o banco local e o fluxo de migrations antes de a FNR-007 definir o modelo relacional do produto.

## What Changes

- Preparar PostgreSQL 18 local via Docker Compose, com volume persistente para desenvolvimento, parâmetros externos e instruções reproduzíveis, sem versionar credenciais.
- Adicionar ao projeto `Finora.Infrastructure` as dependências da baseline já aprovada: EF Core, EF Core Relational e EF Core Design 10.0.15, além de Npgsql EF 10.0.3; fixar Relational diretamente para manter o mesmo patch nos projetos consumidores, preservar `dotnet-ef` local 10.0.15 e atualizar os locks NuGet a partir do grafo real.
- Configurar a criação de contexto em tempo de design e validar localmente o fluxo de migration por comando explícito. Essa migration é uma prova técnica sem tabelas de domínio; o modelo e schema de negócio pertencem à FNR-007.
- Separar o PostgreSQL de desenvolvimento do banco descartável usado nos testes de integração e da Neon. Não executar migrations automaticamente no startup da API nem usar a Neon em testes.
- Documentar configuração, inicialização, validação e encerramento do ambiente local, sem registrar segredos ou evidências fictícias.

## Capabilities

### New Capabilities

Nenhuma. Esta mudança prepara ferramentas e infraestrutura de desenvolvimento; não altera comportamento observável do produto. A seção de specs será deliberadamente ignorada (`skip_specs: true`) para não inventar requisito de aplicação.

### Modified Capabilities

Nenhuma.

## Impact

- Backend: `Finora.Infrastructure`, referências NuGet/lock e suporte técnico à criação de migrations; a API continua sem depender do banco para seu endpoint de health.
- Desenvolvimento: configuração Docker Compose local, configuração externa de conexão e documentação reproduzível.
- Testes: conexão de integração isolada a um PostgreSQL descartável, sem compartilhar o banco local persistente ou a Neon.
- Rastreabilidade: backlog FNR-006 e decisões existentes no TDD §§9–10 e `docs/AMBIENTE.md`. A versão local proposta é PostgreSQL 18; o ambiente hospedado deverá usar major compatível. FNR-007 permanece responsável pelo modelo e pelas migrations de domínio.
