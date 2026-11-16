# Design

## Context

Ver `proposal.md` para motivação e escopo. O projeto `Finora.Infrastructure` hoje referencia somente `Microsoft.Extensions.Http`; não há `DbContext`, configuração Npgsql, migrations ou Compose no repositório. `Finora.Api` mantém `GET /health` sem dependência de banco, e os testes existentes usam MSTest/Microsoft.Testing.Platform sem PostgreSQL.

As versões de referência já registradas são EF Core 10.0.15, Npgsql EF 10.0.3 e `dotnet-ef` local 10.0.15. O major escolhido para o banco local é PostgreSQL 18; a Neon lista 18 entre suas versões suportadas. O TDD separa esta preparação da FNR-007, que criará o modelo relacional do produto, e proíbe migrations automáticas no startup.

## Goals / Non-Goals

**Goals:**

- Tornar reproduzível o PostgreSQL local persistente e a configuração segura de conexão.
- Verificar EF Core, Npgsql e comandos de migration contra um PostgreSQL real e isolado.
- Preparar o ponto de extensão de persistência em `Finora.Infrastructure` sem antecipar entidades/tabelas de domínio.
- Manter o health e os testes sem banco independentes da conexão persistente local.

**Non-Goals:**

- Criar entidades ou schema financeiro, APIs de persistência, autenticação ou integração de negócio; isso pertence a FNR-007 e tarefas posteriores.
- Aplicar migration à Neon, provisionar ambiente hospedado ou iniciar publicação.
- Rodar migrations automaticamente com a API, alterar o contrato de `GET /health` ou adicionar uma biblioteca de testes de containers.
- Definir políticas operacionais ainda pendentes de backup, retenção ou recuperação.

## Decisions

### D1 — PostgreSQL local em Docker Compose, major 18

Usar Compose em vez de instalação local do servidor, pois o TDD e o ambiente do projeto já adotam Docker e exigem instruções reproduzíveis. Fixar a imagem no major 18, sem usar a tag mutável `latest`; tornar porta e parâmetros locais configuráveis e manter volume nomeado no serviço de desenvolvimento. A configuração não versiona senha. A escolha de 18 foi confirmada para este planejamento; se o destino hospedado for configurado com outro major, a compatibilidade precisa ser resolvida antes de validar migrations de domínio.

Alternativas consideradas: PostgreSQL instalado diretamente no host, que reduz o isolamento entre máquinas; imagem `latest`, que pode mudar de major sem revisão; e executar o teste de migration sobre o banco pessoal persistente, que dificulta descarte seguro. Foram rejeitadas em favor de Compose pinado e de um serviço de validação descartável separado.

### D2 — Dependências alinhadas à baseline existente

Adicionar EF Core, EF Core Relational e EF Core Design 10.0.15, além de Npgsql EF 10.0.3, à Infrastructure e usar o `dotnet-ef` local 10.0.15 já fixado. O provider aceita EF Core Relational a partir de 10.0.4; sem uma referência direta, os projetos consumidores resolveram 10.0.4 enquanto a Infrastructure compilou contra 10.0.15, gerando conflitos MSB3277. Fixar Relational diretamente em 10.0.15 mantém coerente o assembly referenciado em toda a solução sem alterar a baseline. Atualizar os locks pelo restore real, preservar o modo bloqueado e não usar versões `latest`. Manter a configuração do provider junto à responsabilidade de persistência na Infrastructure, sem expor entidades EF à Application ou à API.

Alternativas consideradas: depender somente da faixa transitiva mínima do provider, o que reproduziu a divergência de patch nos consumidores; atualizar versões ou instalar ferramentas globalmente. A primeira é rejeitada pelo conflito observado; as demais não são adotadas porque a baseline já foi aprovada e a ferramenta local já está fixada. Qualquer incompatibilidade que ainda exija trocar a baseline deve retornar ao autor antes da alteração.

### D3 — Criação de contexto em tempo de design sem executar a API

Manter o contexto e o mapeamento do provider na Infrastructure. Fornecer uma forma de criação em tempo de design para o `dotnet-ef` que use a mesma configuração e o mesmo provider do runtime, sem iniciar a API nem depender de seus argumentos/serviços. O segredo de conexão vem de configuração local externa (variável de ambiente/User Secrets conforme o host), nunca de arquivo versionado, linha de comando ou log.

Alternativa considerada: deixar as ferramentas descobrirem o contexto executando o startup HTTP. Rejeitada porque `Program.cs` também mantém caminhos de execução próprios e o endpoint de health deve continuar sem conexão de banco.

### D4 — Migration de validação sem schema de negócio

Executar uma migration técnica explícita contra um banco PostgreSQL descartável local para comprovar criação do contexto, conexão, provider Npgsql e registro de migration. O contexto de validação não introduz tabelas do domínio financeiro; a FNR-007 adicionará as entidades, constraints e migrations reais. Não substituir migrations por `EnsureCreated`, que não valida o fluxo de migrations exigido pelo backlog.

Alternativas consideradas: antecipar o modelo da FNR-007, misturando dependências e aceites; ou criar uma migration automática no startup, contrariando DT-15. Ambas foram rejeitadas. A migration da FNR-006 não é aplicada à Neon e não serve como evidência de schema financeiro.

### D5 — Desenvolvimento persistente e testes descartáveis separados

Manter um serviço Compose persistente para desenvolvimento e uma configuração/serviço Compose distinto, em projeto Compose próprio e sem volume persistente, para testes de integração e validação da migration. Executar os testes existentes com MSTest/MTP, sem introduzir Testcontainers ou outra dependência. O harness de teste aceita somente host de loopback e database de teste esperado, e falha antes de conectar se a configuração apontar para outro host/database; a limpeza fica limitada ao projeto Compose descartável.

Alternativa considerada: reutilizar o banco persistente local ou uma conexão hospedada. Rejeitada porque testes poderiam alterar dados de desenvolvimento ou depender de rede/segredos externos, e a Neon é expressamente excluída dos testes automatizados.

### D6 — Não alterar inicialização nem health

Não chamar `Database.Migrate()` durante o startup. A aplicação continua inicializando e respondendo `GET /health` sem connection string; registro de serviços de persistência não deve converter liveness em readiness de banco. Migrations futuras serão executadas por comando administrativo explícito, conforme DT-15 e TDD §9.

Alternativa considerada: migrar automaticamente ao iniciar a API para simplificar desenvolvimento. Rejeitada por incompatibilidade com a decisão aprovada e pelo risco de concorrência/deploy implícito.

## Risks / Trade-offs

- [Docker Desktop/engine pode estar indisponível na execução] → Revalidar daemon Linux e Compose na implementação; evidência de AMBIENTE.md é histórica, não prova disponibilidade atual.
- [Versão local divergir do major hospedado] → Fixar 18 localmente e exigir verificação do target antes de validar migrations destinadas à publicação; não aplicar a migration de smoke test na Neon.
- [Credenciais serem versionadas ou expostas] → Manter arquivo local de segredos ignorado, fornecer apenas exemplo sem valor secreto e conferir diff/logs.
- [Testes atingirem o banco persistente por erro de configuração] → Usar serviço Compose e connection string dedicados a teste; validar destino/configuração antes de permitir operação destrutiva.
- [Migration técnica ser confundida com schema pronto do produto] → Documentar que ela valida apenas o fluxo/provider e deixar explícito que FNR-007 cria o modelo de domínio.

## Migration Plan

Não há migration de domínio nem implantação nesta mudança. A migration de validação roda somente no PostgreSQL descartável local; a execução administrativa e o banco de desenvolvimento persistente permanecem separados. Não remover dados do banco persistente para validar rollback. A migration de domínio será criada e validada na FNR-007, e qualquer uso futuro na Neon dependerá dos gates de PR/publicação do TDD §9.

## References

- EF Core — criação de `DbContext` em tempo de design: https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation
- Npgsql EF Core: https://www.npgsql.org/efcore/
- Suporte de versões PostgreSQL na Neon: https://neon.com/docs/postgresql/postgres-version-support
