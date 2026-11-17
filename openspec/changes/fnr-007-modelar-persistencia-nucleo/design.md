# Design

## Context

Ver [proposal.md](proposal.md) para motivação e [spec](specs/financial-data-persistence/spec.md) para o contrato. Este SDD está **em revisão**, sem aprovação automática pela escolha de uma fronteira ou pela validação do CLI.

Estado observado na preparação:

- `backend/Finora.Infrastructure/Persistence/FinoraDbContext.cs` é um `DbContext` vazio. Factory e configuração Npgsql já existem, sem registro do contexto no host HTTP.
- A migration `20261116175137_Fnr006PersistenceSmoke` tem Up/Down vazios; o snapshot não contém entidades. O teste `PostgreSqlPersistenceTests` atualmente aplica todas as migrations e exige somente essa migration e a tabela de histórico: essa expectativa precisa evoluir sem perder a cobertura histórica.
- `TransactionFacts`, normalizadores e classificadores puros existem no Domain. `TransactionDateResult.TransactionDate` já é nullable; comentários antigos usam “competência”, mas o contrato aprovado trata apenas data técnica. Não renomear APIs nem alterar regras financeiras nesta entrega; comentários dos pontos efetivamente alterados devem refletir a semântica correta.
- `Program.cs` oferece health/OpenAPI/Swagger e comandos locais Pluggy, sem Identity, banco ou endpoints financeiros no fluxo HTTP. Preservar esse desacoplamento.
- PostgreSQL 18, EF Core/Relational/Design 10.0.15 e Npgsql EF 10.0.3 constam da FNR-006. `compose.test.yaml` usa banco descartável; os testes exigem `FINORA_TEST_CONNECTION_STRING`, loopback e database `finora_integration_test` antes de abrir conexão.
- PRD 0.4/TDD 0.8 são referências aprovadas, mas não aprovam o detalhamento DT-40 deste SDD. O backlog registra aceite local da FNR-006 e ainda não registra PR/integração. Não substituir evidência de integração por presença de código na main local.

## Goals / Non-Goals

**Goals:**

- Um único contexto/transação para o armazenamento Identity e o grafo financeiro; invariantes relacionais verificadas no PostgreSQL real descartável.
- Separar fatos externos, interpretação conservadora e organização pessoal, sem exigir semântica financeira inexistente.
- Evoluir a migration técnica de forma incremental e manter a API anterior funcionando sem banco.

**Non-Goals:**

- Não implementar autenticação ou criar usuário de produção. Identity entra somente como modelo de armazenamento e principal das FKs, conforme a escolha explícita do autor nesta conversa.
- Não criar handlers/repositórios genéricos sem caso de uso, endpoints, importador, staging, upsert, reconciliador, processador, worker ou transportes. A atomicidade é exercitada por testes do contexto, não por uma “sincronização pronta” fictícia.
- Não definir leases, fencing, orçamento de retry, estados completos de entrega, tabelas de outbox ou índices de execução ativa. FNR-013 estenderá SyncRun e o mesmo contexto conforme TDD §20.
- Não implementar catálogo/provisionamento FNR-016, edição HTTP FNR-017, recorrência, investimento, somas monetárias ou coleta real.

## Decisions

### D1 — Camadas e Identity mínimo no mesmo contexto

Conservar o nome `FinoraDbContext`, factory e assembly de migrations. Entidades financeiras e suas invariantes ficam no Domain; configurações `IEntityTypeConfiguration` e `ApplicationUser` ficam na Infrastructure. Domain/Application não referenciam EF, ASP.NET ou Identity; usam UUID do titular, sem navegação para a classe Identity.

`ApplicationUser` deriva de `IdentityUser<Guid>`. O contexto passa a derivar de `IdentityUserContext<ApplicationUser, Guid>`, sem roles, chamando a configuração de base antes das configurações financeiras. Usar o modelo Identity básico de usuários/claims/logins/tokens, sem habilitar passkeys ou fluxos externos. As tabelas auxiliares são armazenamento do framework, não autorização para emitir tokens ou gravar JWT/segredos Pluggy nelas. Sem seed de usuário, senha padrão, `AddDefaultIdentity`, cadastro público ou middleware novo no HTTP.

Propor `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.15, mantendo o patch EF; conferir disponibilidade e grafo antes de instalar. Fixar de forma compartilhada as opções de armazenamento que moldam o schema (versão básica do modelo e limites de chaves), igualmente na factory e nos testes e, futuramente, em FNR-008. Não depender de opções presentes somente no host HTTP. IDs de usuário são UUID não vazios; não introduzir identidade sintética de produção. As fixtures criam apenas usuários sintéticos mínimos no banco descartável, sem validar login ou gerar senha.

**Alternativas:** adiar FK de usuário para FNR-008 foi apresentado, mas o autor escolheu incluir a base Identity. Criar tabela paralela de titulares exigiria reconciliação futura desnecessária; implementar login agora ultrapassaria o escopo.

### D2 — Grafo persistido e campos mínimos

Nomes abaixo concretizam DT-40 como proposta sujeita à revisão do SDD. Tabelas financeiras no schema `public`, sem extensão de nomenclatura adicional. UUIDs gerados na aplicação; defaults não atribuem um titular.

| Entidade | Campos e regras de armazenamento |
|---|---|
| ApplicationUser | Modelo básico Identity, chave Guid; campos internos do framework sem dados financeiros adicionais. Sessões pertencem à FNR-009. |
| FinancialConnection | Id, UserId, Provider, ExternalItemId, SourceStatus nullable, ExpiresAtUtc nullable, LastAttemptAtUtc nullable, LastSuccessfulImportAtUtc nullable, SourceUpdatedAtUtc nullable. Situação externa textual preservada, sem inventar enum contratual de conexão. |
| CardAccount | Id, UserId, ConnectionId, ExternalId, Name nullable, LastFour nullable, CurrencyCode; limite disponível/total opcionais em decimal quando fornecidos. Não coletar número completo; final, se armazenado, é apenas o final permitido, nunca origem de identidade. |
| FinancialTransaction | Id, UserId, CardAccountId; campos originais permitidos por TransactionFacts, incluindo ExternalId e ExternalAccountId, Description, Amount, CurrencyCode, OriginalDate, Type/Status/OperationType nullable e metadados de parcela/fatura opcionais. Interpretação em colunas separadas, conforme D4. |
| Category | Id, UserId nullable, IsSystem, Name, NormalizedName e ScopeId técnico de D3. Nome/chave até 60 caracteres, não vazios. UserId nulo somente para sistema. Normalização e catálogo/provisionamento permanecem na FNR-016. |
| TransactionAnnotation | TransactionId como PK, UserId, CategoryId nullable, CategoryScopeId nullable, Note nullable até 500 caracteres e UpdatedAtUtc. Limpar Note resulta em null sem alterar categoria; sem anotação e anotação com categoria nula significam ausência de escolha manual. |
| SyncRun | Id, UserId, ConnectionId, CreatedAtUtc, StartedAtUtc/FinishedAtUtc nullable, RequestedFrom obrigatório e RequestedToExclusive nullable, State, PagesRead/ReceivedCount/UniqueCount/PersistedCount nullable, ObservedFrom/ObservedTo nullable, CoverageState obrigatório e FailureCode nullable. |

Para SyncRun, `RequestedFrom` é obrigatório e `RequestedToExclusive` nulo representa janela superior aberta, sem descartar parcelas futuras. Se ambos presentes, fim é maior que início. Estados básicos `Pending`, `Running`, `Succeeded`, `Failed` representam somente registro, não executor entregue. Cobertura começa `Unknown`; `Partial` e `CompleteForRequestedWindow` qualificam a leitura do intervalo solicitado, nunca 12 meses de cobertura bancária. ObservedFrom/To são extremos observados opcionais, não continuidade inferida. Contadores ausentes são null e contadores conhecidos são não negativos. FinishedAtUtc não precede início/criação; falha usa código interno allowlisted de até 64 caracteres, nunca exception.Message do provedor. Gravar SyncRun não atualiza automaticamente o sucesso da conexão; a operação futura que confirma importação completa será transacional em FNR-012/013.

Não colocar campos de anotação, candidato de recorrência, competência financeira ou métricas monetárias em FinancialTransaction. Não usar JSON bruto/JSONB de resposta como atalho para campos ainda desconhecidos.

**Alternativas:** uma tabela contendo payload completo viola minimização e dificulta constraints; colocar anotações no mesmo registro aumenta o risco de sobrescrita pelo futuro importador. Modelo completo de processamento agora fecharia decisões próprias da FNR-013 sem evidência.

### D3 — Integridade por titular e categoria de sistema sem brecha de nulo

FKs simples verificam existência de usuário. Para os vínculos financeiros, configurar chaves alternativas e FKs compostas explicitamente, sem confiar só no filtro de aplicação:

- FinancialConnection expõe chave alternativa `(Id, UserId)`; CardAccount e SyncRun referenciam `(ConnectionId, UserId)`.
- CardAccount expõe `(Id, UserId, ExternalId)`; FinancialTransaction referencia `(CardAccountId, UserId, ExternalAccountId)`, impedindo inclusive divergência entre conta externa original e cartão interno.
- FinancialTransaction expõe `(Id, UserId)`; TransactionAnnotation referencia `(TransactionId, UserId)`. TransactionId como PK também garante uma única anotação.
- Category pessoal tem FK opcional para usuário; `IsSystem` equivale a `UserId IS NULL` por CHECK. `ScopeId` UUID não nulo deve ser igual a UserId nas pessoais e ao UUID vazio nas de sistema, também por CHECK. UUID vazio é marcador de escopo, não um usuário; CHECK em ApplicationUser impede usá-lo como Id.
- Category expõe `(Id, ScopeId)`. Na anotação, CategoryId e CategoryScopeId são ambos nulos ou ambos preenchidos; CHECK exige que o escopo preenchido seja UserId ou UUID vazio. FK `(CategoryId, CategoryScopeId)` referencia Category. Isso permite sistema/próprio titular, mas impede categoria pessoal alheia mesmo com SQL direto.

Esse campo de escopo é uma redundância técnica validada pelo banco, sem titular fictício. Não usar FK `(CategoryId, UserId nullable)` isoladamente: uma FK composta com componente nulo deixaria a verificação incompleta. Usar campos comuns com CHECK, sem triggers nem colunas calculadas para essa regra.

Configurar `Restrict/NoAction` nas relações financeiras e na exclusão do usuário com dados financeiros. Cascades internos padrão de Identity não autorizam apagar o grafo financeiro. Não expor operações de transferência de propriedade; atualizar apenas dados externos não altera Id/UserId das entidades. Sem rotina automática de exclusão ou limpeza por ausência na origem.

**Alternativas:** somente validação em serviço permite inconsistência por gravação concorrente ou SQL direto; trigger adicionaria mecanismo procedural e validação adicional. O par escopo + FK mantém a regra declarativa e testável no banco.

### D4 — Identidade, dinheiro e tempo sem perda silenciosa

Identidade externa usa string com comparação ordinal na aplicação e collation determinística `C` nas colunas de identidade no PostgreSQL, sem trim, mudança de caixa, parse UUID ou hash como identidade única. Como limite técnico inicial para indexação B-tree, aceitar até **1.024 bytes UTF-8 por ID externo indexado**, com CHECK por tamanho em bytes e validação na fronteira, mantendo o conteúdo integral em `text`. Provider usa código interno `pluggy`, com limite de 32 caracteres. Esses limites são propostas de armazenamento, não limites comprovados do provedor; exceder deve falhar explicitamente, nunca truncar ou fundir. Mudança necessária exige revisar o SDD antes de implementar tratamento diferente.

Unicidade: conexão `(UserId, Provider, ExternalItemId)`, cartão `(ConnectionId, ExternalId)` e movimento `(CardAccountId, ExternalId)`. O contexto do provedor é herdado da conexão. Isso não comprova continuidade entre reconexões: preservar Id interno onde continuidade for comprovada será responsabilidade FNR-012; não mesclar por nome/final.

Dinheiro usa `decimal`/`numeric(19,4)` e moeda separada sem conversão. Faixa individual: -999999999999999,9999 a +999999999999999,9999. Validar faixa e fração exata antes de envio ao provider, inclusive em atualizações, usando invariantes das entidades e uma barreira comum de validação de gravação do contexto. Mais casas somente com zeros excedentes não alteram o número e podem ser representadas; fração não nula além de 4 é rejeitada. PostgreSQL pode arredondar na coerção para numeric(19,4), portanto CHECK após coerção sozinho não resolve esse requisito. Testar os caminhos de gravação síncrono/assíncrono e não introduzir bulk/ExecuteUpdate que contorne essa barreira. Escrita SQL administrativa arbitrária não é uma API de gravação da aplicação.

Moeda não é restrita a BRL nem preenchida com BRL por default. Description/Name de origem e datas brutas usam texto sem truncamento silencioso; campos-base de identidade, descrição, valor, moeda e texto da data principal não podem estar ausentes. Tipo/status/operação e metadados opcionais permanecem nulos se ausentes. Preservar números de parcela incoerentes em colunas nullable, sem CHECK que impeça registrar a inconsistência; a interpretação conservadora registra o motivo. Não transformar null em zero ou criar parcelas.

`OriginalDate` continua texto; `TransactionDate` é DateOnly nullable em coluna `date`. Calendário `yyyy-MM-dd` permanece no mesmo dia; instantes com Z/offset seguem `America/Sao_Paulo`; data ambígua mantém dia nulo e motivo do normalizador. Preservar metadados de compra/fatura como recebidos, inclusive `yyyy-MM` sem dia inventado. Auditoria/execução usa UTC em `timestamp with time zone`, nunca conversão da data de calendário para meia-noite UTC.

Persistir Nature, NatureReason, Situation, SituationReason e DateReason separadamente, usando os enums/códigos internos existentes, representados por texto estável no banco. Reutilizar classificadores puros com regra positiva de compra desligada. Registrar `NormalizationRuleVersion` e `NormalizationReferenceDate` do snapshot interpretado: a referência é necessária para explicar situação futura dependente do tempo. Unknown exige motivo aplicável; situação/erro de data não devem apagar motivo da natureza. Não armazenar uma elegibilidade positiva de gasto como fato financeiro. Isso não altera a capability de normalização nem reclassifica a evidência histórica.

**Alternativas:** armazenar só dia derivado perde evidência; timestamp para qualquer data desloca calendários; numeric sem validação prévia arredonda silenciosamente; um único motivo de exclusão perde explicações de eixos independentes.

### D5 — Categorias, índices e fronteira das consultas

Persistir `NormalizedName` fornecido por uma política consistente, sem aplicar uma nova política de acentos nesta entrega. Unicidade `(ScopeId, NormalizedName)` impede duplicação pessoal dentro do titular e duplicação de sistema, inclusive concorrente. FNR-016 define a transformação executável e a validação adicional contra nomes iniciais, conforme TDD §15; nenhum endpoint ou seed automático é criado aqui. Fixtures passam chaves sintéticas explícitas e testam a unicidade, não presumem política de produção aprovada.

Categoria ausente é CategoryId/CategoryScopeId nulos, sem ID especial “Sem categoria” armazenado na anotação. FNR-016 deverá representar a opção visual conforme essa convenção e o catálogo, sem duas formas persistidas para ausência. A normalização pessoal não é aplicada à descrição externa.

Além das PKs/chaves alternativas/unicidades, índices iniciais:

- FinancialTransaction `(CardAccountId, TransactionDate, Id)` para consulta estável; data nula continua explícita, sem fabricar fallback.
- TransactionAnnotation `(CategoryId)` para filtro por categoria.
- FinancialConnection `(UserId)` somente se não estiver coberto pelo prefixo da unicidade já criada.
- SyncRun `(ConnectionId, State)` para inspeção por conexão/situação, sem restrição de uma execução ativa ainda.

Conferir os índices gerados por convenção para não duplicar índices já cobertos. Sem índice full-text/trigram, extensão PostgreSQL ou SLA novo; FNR-015/024 refinam consultas com dados representativos. PK/AK UUID referenciadas podem parecer redundantes, mas sustentam a integridade de proprietário, não um índice especulativo de performance.

**Alternativas:** implantar API de categorias agora antecipa FNR-016; fechar algoritmo de busca/normalização e índices de todas as consultas sem contratos consolidados cria trabalho sem evidência.

### D6 — Atomicidade e testes de contrato persistido

Usar o mesmo contexto e transação curta explícita quando o conjunto exige mais de um SaveChanges. Não adicionar UnitOfWork genérico ou contrato IQueryable na Application. Um único SaveChanges também é atômico, mas não demonstra rollback de várias etapas: o teste deve confirmar uma primeira gravação dentro da transação, provocar falha posterior e verificar tudo com **outro contexto** depois do rollback. Nenhum HTTP externo ocorre dentro de transação.

Manter a defesa `DisposablePostgresTestConfiguration` antes de abrir conexão, executar migrations/DDL ou limpar fixtures. Usar somente `finora_integration_test` de `compose.test.yaml`, sem fallback; serializar testes que compartilham schema. Recriação/downgrade e limpeza são permitidos somente nesse destino descartável explicitamente validado, nunca volume pessoal/Neon.

Separar duas coberturas: teste histórico aplica somente a migration FNR-006 a partir de base vazia e verifica a tabela de histórico; teste atual aplica a cadeia completa e verifica schema novo. Um `MigrateAsync()` sem alvo seguido da asserção “só smoke” não pode permanecer. Testes de reset de schema devem ter fixture exclusiva e serializada para não desfazer migrations de outro teste.

Matriz mínima: migração vazia/atualização/reaplicação/downgrade controlado; FK inexistente e titular cruzado em cada aresta; categoria sistema/própria/alheia e pares nulos inválidos; duplicatas sequenciais e concorrentes com contextos independentes; IDs distintos coincidentes e opacos; round-trip de decimais/moedas/datas/metadados; Unknown com motivos separados; anotação preservada em atualização controlada; rollback após primeira gravação e exclusão restrita. Verificar metadados do schema para precisão/índices/constraints e modelo Identity sem AuthSessions ou tabelas de outbox/recorrência. Fixtures são sintéticas; isso não valida importação real, reconexão, revogação ou autorização HTTP.

## Risks / Trade-offs

- [Base FNR-006 com encerramento documental desatualizado] --> Conferir evidência de PR/integração antes de iniciar implementação FNR-007; não alterar seu histórico nesta proposta.
- [Identity adiciona armazenamento antes dos comandos] --> Limitar ao schema básico, sem provisionamento e sem novos serviços HTTP; FNR-008 reutiliza o contexto e opções, sem criar segunda identidade.
- [Mais chaves compostas e escopo técnico] --> Testar constraints pelo banco, inclusive inserção direta de relações inválidas; documentar o marcador UUID vazio e proibir usuário com esse Id.
- [Limite técnico de IDs não provado na origem] --> Rejeitar excesso explicitamente, testar bytes multibyte e revisar antes de ampliar; nenhuma coleta nova é requisito deste planejamento.
- [Numeric arredonda antes de CHECK] --> Validar na fronteira comum de gravação e provar ausência de persistência em falha de escala/faixa; não prometer proteção contra SQL externo arbitrário.
- [Política de categorias ainda pendente em FNR-016] --> Armazenar chave e constraints por escopo agora; algoritmo, nomes reservados e conflitos com sistema ficam na entrega própria.
- [Schema básico de SyncRun não executa sincronização] --> Não declarar single-flight, idempotência de processamento, cobertura bancária ou outbox concluídos por criar tabelas.
- [Modelos design-time/runtime Identity divergentes] --> Centralizar opções de modelo e comparar snapshot/modelo em teste antes de gerar/aplicar migration.

## Migration Plan

1. Antes de código: revisar/aprovar proposta, spec e este SDD; confirmar dependências e contexto Git. A branch documental atual não autoriza implementação direta na main. Concretizações do TDD §10 e a fronteira Identity devem ser apresentadas em revisão canônica preservando TDD 0.8 e seu aceite; não tratar revisão nova como aprovada por herança.
2. Após solicitação explícita de apply e aprovação necessária, adicionar o pacote Identity compatível e modelo/mapeamentos. Preservar a migration técnica anterior e gerar nova migration incremental `Fnr007CorePersistence` na Infrastructure. Snapshot representa o modelo novo, sem reescrever o histórico aplicado.
3. Revisar Up/Down e SQL gerado: criação de tabelas/constraints/índices, sem seed, consultas externas, somas ou DROP de estruturas anteriores. Aplicar por comando administrativo somente ao banco descartável nesta entrega. Repetir a cadeia em base vazia e em base com apenas FNR-006; testar reaplicação e round-trip do schema.
4. Validar downgrade ao alvo FNR-006 somente em banco descartável, ciente de que remove os dados das tabelas FNR-007, e reaplicar. Esse teste não é autorização de rollback destrutivo em outro ambiente. Quando houver dados reais, preferir correção incremental; remoção exige plano e autorização específicos.
5. Executar restore bloqueado, build/testes, regressão health/Swagger/comandos locais, OpenSpec estrito e verificações documentais. Registrar evidências reais, nova revisão documental e aceite sem antecipar PR/merge/publicação. Sync/arquivamento somente após validação/aceite e no fluxo aplicável.
6. Não usar Neon nem banco pessoal para testes. Não há deploy nesta tarefa. Na futura hospedagem, seguir TDD §9: major compatível e migration administrativa aprovada antes da integração que dispara o deploy dependente, sem migration no startup.

## Referências consultadas

Consulta de documentação em 17/11/2026, sem prova de execução/compatibilidade local nesta proposta:

- [Customização do modelo Identity, chaves UUID, contexto sem roles e opções de design-time](https://learn.microsoft.com/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0), via Microsoft Learn.
- [Chaves estrangeiras e alternativas no EF Core](https://learn.microsoft.com/ef/core/modeling/relationships/foreign-and-principal-keys), via Context7 `/dotnet/entityframework.docs`; atenção a componentes nullable em FKs compostas.
- [Provider Npgsql EF](https://www.npgsql.org/efcore/) e documentação/tag `/npgsql/efcore.pg/v10.0.3` consultadas via Context7. APIs e compatibilidade do pacote Identity novo devem ser comprovadas pelo restore/build e snapshot do incremento, não inferidas de exemplos de outra versão.
