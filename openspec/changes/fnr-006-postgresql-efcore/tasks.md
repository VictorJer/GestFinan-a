# Tasks

## 1. PostgreSQL local reproduzível

- [x] 1.1 Criar configuração Compose para PostgreSQL 18 de desenvolvimento com volume nomeado persistente, healthcheck, porta configurável vinculada a loopback e parâmetros externos; verificar `docker compose config`, disponibilidade do serviço e persistência após reinício sem apagar o volume.
- [x] 1.2 Criar configuração Compose separada para PostgreSQL 18 descartável de testes, sem volume persistente e com projeto/porta próprios; verificar isolamento do banco de desenvolvimento e que a limpeza afeta somente esse projeto Compose.
- [x] 1.3 Atualizar `docs/AMBIENTE.md` com `.env.example`, comandos reproduzíveis de inicialização, inspeção e encerramento dos serviços, avisos para não versionar segredos e distinção entre banco persistente e banco descartável; verificar comandos contra os arquivos Compose e confirmar que `.env` continua ignorado.

## 2. EF Core/Npgsql na Infrastructure

- [x] 2.1 Adicionar referências diretas a EF Core, EF Core Relational e EF Core Design 10.0.15 e Npgsql EF 10.0.3 em `Finora.Infrastructure`; gerar os locks com o procedimento de atualização documentado e verificar restore bloqueado, grafo dos consumidores resolvido em Relational 10.0.15, licenças/avisos aplicáveis e build sem o conflito MSB3277, sem trocar a baseline.
- [x] 2.2 Criar em `Finora.Infrastructure` o contexto técnico sem entidades de domínio, configurar Npgsql e disponibilizar factory de design-time que obtenha conexão de configuração externa sem iniciar `Finora.Api`; verificar `dotnet-ef dbcontext info`, provider/assembly usados e ausência de segredos na saída. Comentar em português os blocos lógicos manuais de configuração.
- [x] 2.3 Atualizar `docs/TDD.md` para registrar PostgreSQL 18 como major escolhido para desenvolvimento, mantendo o requisito de compatibilidade do major hospedado e as decisões existentes de migration administrativa/sem migration no startup; verificar coerência com FNR-006 e as referências de versão.

## 3. Migration técnica e isolamento de testes

- [x] 3.1 Gerar uma migration explícita de validação, sem tabelas de domínio, e aplicá-la somente ao PostgreSQL descartável local; verificar a criação do histórico de migrations, a ausência de schema financeiro e a repetição idempotente do comando, sem tocar no volume persistente nem na Neon.
- [x] 3.2 Adicionar teste de integração MSTest/MTP que use somente a configuração do serviço descartável para validar conexão e migration; verificar que configuração ausente, host diferente de loopback ou database diferente do nome de teste falha antes da conexão, sem fallback para o banco local/Neon e sem exigir dados reais.
- [x] 3.3 Verificar que a API continua iniciando sem connection string, que `GET /health` mantém HTTP 200 sem consultar o banco e que nenhum caminho de startup aplica migrations; executar os testes HTTP existentes e os novos testes de persistência.

## 4. Validação integrada e registro

- [x] 4.1 Executar `docker compose config` para as configurações afetadas, restore NuGet em modo bloqueado, build e testes backend; validar OpenSpec em modo estrito e `git diff --check`, registrando falhas reais e confirmando ausência de segredos, uso da Neon ou alteração do contrato de health.
- [x] 4.2 Atualizar o registro de execução de FNR-006 no backlog somente com evidências verificadas, mantendo pendências explícitas enquanto houver gates; verificar rastreabilidade ao TDD, AMBIENTE.md e esta mudança sem declarar integração/publicação antes de ocorrerem.
