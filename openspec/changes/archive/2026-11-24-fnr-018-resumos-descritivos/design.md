# Design

## Context

Ver [proposal.md](proposal.md) e [delta `card-movement-summary`](specs/card-movement-summary/spec.md). A API já possui `GET /api/transactions`: valida filtros em `GetTransactionsQuery`, resolve conexão/cartão próprios, usa `TransactionMovementReader` para consulta EF/PostgreSQL com anotação por titular e lê cobertura correlata no mesmo snapshot `RepeatableRead`. O `TransactionsController` serializa DTOs allowlist e erros sanitizados. A `main` local contém o merge `b173e58` do PR #31/FNR-017; o registro de execução no backlog ainda descreve esse PR como aberto. Não há Angular nem endpoint de resumo implementado nesta mudança.

## Goals / Non-Goals

**Goals:**

- Preservar um único predicado de universo filtrado entre lista e resumo, mantendo contagens coerentes sobre dados estáveis e preservando isolamento por titular/conexão.
- Projetar resposta pequena, com grupos fixos de natureza e situação, total observado, semântica de período e cobertura em campos separados.
- Evitar novas migrations e dependências; comprovar com PostgreSQL descartável e HTTP o comportamento e seus limites.

**Non-Goals:**

- Calcular indicadores financeiros ou reconstruir dados bancários ausentes; criar filtros por natureza/recorrência não previstos na lista.
- Criar componente Angular, gráfico, lista duplicada ou sincronização; isso permanece em FNR-022/023 e outras entregas.
- Reparar todas as fixtures históricas da suíte PostgreSQL ampla como efeito colateral desta mudança; registrar falhas não relacionadas e solicitar escopo próprio se impedirem o aceite.

## Decisions

### D1. Contrato HTTP e recorte de filtros

`GET /api/dashboard` recebe somente os seis filtros existentes (`cardId`, `categoryId`, `situation`, `from`, `toExclusive`, `search`); não recebe `page`, `pageSize`, titular, ordenação ou expressão SQL. Resposta HTTP allowlist: `filteredCount`, `countsByNature`, `countsBySituation`, `coverage` e `dateBasis: "TransactionDate"`; os dois conjuntos de contagens têm códigos string allowlist existentes e um grupo para cada valor permitido, inclusive zeros observados. `coverage` reaproveita os campos e códigos de `GET /api/transactions`. `filteredCount` é número de movimentos persistidos no filtro; nem `Purchase` nem `Effective` são declarados compras efetivadas. A borda mapeia status/Problem Details conforme §11 do TDD, sem refletir texto da busca nem expor IDs externos. Alternativa rejeitada: extensão com agregados no DTO da página, que acopla dashboard à paginação e permite comparações incorretas com `items`.

### D2. Reutilizar validação, titularidade e predicado, não carregar páginas para contar

Na Application, o caso de uso de resumo MediatR reutiliza a validação de filtros da lista (sem aceitar paginação externa), a resolução da conexão própria e a checagem de cartão antes do reader. Na Infrastructure, extrair o predicado base parametrizado de `TransactionMovementReader` para uma construção compartilhada que inclua a junção de anotação própria e os filtros exatos, sem mudar a projeção/paginação da FNR-015. A consulta agregada agrupa os movimentos filtrados por `Nature` e por `Situation` no PostgreSQL, sem selecionar ou somar `Amount`; completa na aplicação os buckets de enum sem ocorrência com zero referente somente ao conjunto persistido filtrado. Alternativa rejeitada: percorrer todas as páginas de movimentos ou duplicar o predicado em nova implementação, por custo, limites de paginação e deriva semântica de filtros.

### D3. Snapshot e cobertura honesta

Executar as contagens de ambos os eixos, `filteredCount` e leitura de cobertura no mesmo snapshot de leitura `RepeatableRead`, pelo menor tempo necessário e sem chamada externa. A cobertura segue a correlação já implementada em `TransactionMovementReader`: sucesso/janela anterior não são substituídos por execução mais recente falha/parcial. Dois requests separados (resumo e lista) não prometem snapshot idêntico sob escrita concorrente; testes de conciliação usam estado estável, e concorrência interna de uma mesma resposta é exercitada com barreira no PostgreSQL. Alternativa rejeitada: múltiplas consultas sem snapshot, que podem produzir `filteredCount` diferente da soma dos grupos durante importação ou PATCH.

### D4. Preservar semântica conservadora e contratos existentes

Agrupar os enums persistidos de natureza/situação, sem inferir pela descrição, pelo sinal, pela categoria ou pelo metadado de parcela. `PAGAMENTO` ambíguo continua `Unknown`; `PAGAMENTO_FATURA` coerente segue `BillPayment`; `Pending`/`FutureInstallment` são situações, não evidências de gasto. Data nula integra universo sem intervalo e não integra intervalo filtrado. A leitura não cria `SyncRun`; alterações em categoria passam a refletir o novo filtro após confirmação. Alternativa rejeitada: acrescentar classificador financeiro ou agrupar por valor/moeda, contrariando PRD RF07/§8 e DT-54.

### D5. Rastreabilidade pré-código e validação proporcional

Antes de código dependente, reconciliar o estado factual de FNR-017 no backlog e propor revisão canônica do contrato FNR-018 no TDD §§11/13, preservando histórico e exigindo aceite próprio do conteúdo; não atualizar PRD sem alteração de requisito. Testar validação/isolamento HTTP, invariantes de contagem e ausência de valores monetários em respostas e logs, execução sintética PostgreSQL com migrations atuais e medição local até 5.000 movimentos conforme RNF-08/M-05 sem atribuir SLA hospedado. A suíte ampla previamente registrada em FNR-017 teve 206/208 devido a duas fixtures legadas de schema; esse resultado não é evidência de regressão nova nem autorização para declará-la verde.

## Risks / Trade-offs

- [Deriva entre filtros de lista e resumo] → compartilhar a construção do predicado e testar os dois endpoints com filtros combinados, literais SQL e categoria alterada.
- [Consultas de agrupamento + snapshot oneram o banco] → executar `GROUP BY` no PostgreSQL, medir até 5.000 movimentos sintéticos e inspecionar planos; qualquer índice/migration exigiria revisão específica antes de ser introduzido.
- [Contagem zero interpretada como gasto zero] → manter `coverage` separado e semântica explícita de universo persistido/data técnica; buckets zero não dizem nada sobre origem não importada.
- [Conexão ambígua ou categoria alheia vaza dados] → replicar validações de propriedade e cenários 401/404/409, filtrar por titular em cada junção.
- [Código da FNR-015 alterado para compartilhar filtro causa regressão] → conservar comportamento/DTO da lista e executar seus testes atuais junto aos novos antes de declarar aceite.

## Migration Plan

Nenhuma migration de dados ou schema prevista. Após aprovação e validação, publicar a API pelo fluxo regular de PR e checks aplicáveis; o novo endpoint é aditivo. Reversão: voltar à versão anterior da API pelo fluxo de implantação aprovado, sem transformação de dados. A interface futura deverá tolerar ausência do endpoint até FNR-023; esta mudança não publica nem migra ambientes por si.
