# Design

## Context

Ver [proposal.md](proposal.md) e [delta de anotações](specs/transaction-annotations/spec.md). `TransactionAnnotation` já possui PK `TransactionId`, FK composta ao movimento/titular, FK composta à categoria/escopo, `Note` até 500 e `UpdatedAtUtc`. FNR-015 já lê categoria/observação e filtra/conta por categoria em snapshot; FNR-016 já publica catálogo pessoal/inicial e ausência virtual. Não existe operação HTTP para editar anotação. O PR #30 está integrado, mas o BACKLOG 0.55 ainda diz “aberto” e aguarda revisão.

## Goals / Non-Goals

**Goals:**
- Escrita isolada, parcial e atômica da organização pessoal, conservando os campos omitidos e impedindo atualizações perdidas entre edições de campos diferentes.
- Reusar o modelo e a leitura existentes; teste de ponta a ponta HTTP/PostgreSQL com dados somente sintéticos.

**Non-Goals:**
- Criar outra representação de categoria, mudar o schema sem necessidade, automatizar importação ou entregar a interface FNR-022.
- Interpretar observação como HTML, classificar compra ou criar métricas monetárias.

## Decisions

### D1. Contrato parcial explícito no HTTP

Usar `PATCH /api/transactions/{id}/annotation` com corpo estrito contendo apenas `categoryId` e/ou `note`; campos omitidos preservam estado, `categoryId: null` remove categoria e `note: null` ou string vazia limpa observação. Rejeitar corpo vazio, tipos incorretos, propriedades extras, IDs vazios e observação acima de 500 com 400 sanitizado. Responder 200 com `{ categoryId, note }` persistidos; não devolver descrição ou valores. O caminho de domínio já distingue ausência de categoria de UUID vazio. Preferir presença explícita de propriedades na desserialização/validação em vez de record com propriedades nullable sem marcador, que confundiria omissão com `null`; PUT de substituição integral arriscaria limpar campos inadvertidamente. Texto com espaços não é aparado implicitamente; só string vazia limpa, para preservar entrada válida exatamente.

### D2. Titularidade e validação na fronteira de escrita

`TransactionsController` obtém UserId da sessão validada e encaminha ao caso de uso; corpo nunca define titular, ID do movimento ou escopo. O adapter seleciona movimento pelo ID interno, titular e conexão/cartão próprios, confirma categoria de sistema (`ScopeId` vazio) ou pessoal (`ScopeId` do titular) pela mesma chave composta usada na persistência e grava apenas `TransactionAnnotation`. Movimento inexistente/alheio: 404 idêntico; categoria inexistente/alheia: 404 idêntico; entrada inválida: 400; sessão inválida: 401; 429/503/500 seguem a fronteira de erros existente. Não consultar Pluggy nem broker. Fazer a verificação de posse e escrita na mesma transação para impedir troca de estado entre autorização e gravação; as FKs/checks existentes são defesa adicional. Pré-consulta sem transação não basta contra TOCTOU.

### D3. Atualização serializada sem perda de campos independentes

Serializar edições do mesmo movimento na transação PostgreSQL com bloqueio de linha do movimento antes de ler/criar/atualizar sua anotação. Criar a primeira anotação apenas quando há conteúdo pessoal efetivo; uma edição que limpa ambos pode deixar a linha existente com campos nulos, sem criar categoria virtual. Sob edições concorrentes de campos distintos, cada uma lê o estado confirmado anterior, aplica apenas campos presentes e confirma uma anotação única; a segunda aguarda a primeira. Para duas edições do mesmo campo, vence a última confirmação, sem promessa de versão por cliente. Manter tratamento de violação inesperada da unicidade/FK como falha sanitizada, não duplicação ou sucesso parcial. Usar `UpdatedAtUtc` como instante da escrita, não como token de concorrência; reutilizar validação de domínio e `SaveChanges` transacional. Alternativa rejeitada: read-modify-write independente sem lock, que pode apagar o outro campo; adicionar coluna de versão/migration só para este caso amplia desnecessariamente o contrato.

### D4. Leitura e importação mantidas

Não alterar o envelope de `GET /api/transactions`: os campos `categoryId` e `note` e o filtro/`filteredCount` existentes leem a anotação confirmada. A promoção/importação conserva `FinancialTransaction.Id` e não substitui `TransactionAnnotation`; demonstrar atualização de fatos externos e reconexão conservadora em testes, sem depender de provedor real. Categoria manual não muda natureza/situação e não transforma desconhecidos em compras. Nenhuma migration planejada: a migration FNR-007 já criou a anotação e FNR-016 provisiona o catálogo de modo administrativo.

## Risks / Trade-offs

- [Lock por movimento pode serializar edições e competir com importação] → manter a transação curta, adquirir sempre primeiro o lock do movimento, testar espera/cancelamento e verificar ordem dos locks da importação antes de código.
- [Diferença entre omissão e `null` em JSON] → testes de contrato para omissão, limpeza, corpo vazio, campos extras e tipos inválidos; não usar binding que apague a distinção.
- [Escrita em categoria alheia ou catálogo ausente] → consulta com escopo e FK composta; quando catálogo inicial estiver ausente, falhar fechado de forma sanitizada em vez de criar uma categoria implícita.
- [Suíte PostgreSQL ampla historicamente não verde] → executar suíte focada e regressão proporcional; registrar separadamente falhas herdadas e não anunciar suite ampla aprovada sem execução verde.

## Migration Plan

Não criar migration nesta proposta. A base de destino deve ter migrations FNR-007 e FNR-016 aplicadas por procedimento administrativo antes de usar as rotas dependentes; conferir o schema local antes de liberar esta escrita. Se durante implementação aparecer mudança de schema indispensável, revisar este design e obter aprovação do ajuste antes de gerar migration; aplicar migration validada no destino antes de integração que dispare deploy. Rollback da API remove a operação nova sem apagar anotações já gravadas, que permanecem legíveis pela consulta existente; nunca tentar apagar histórico manual como parte do rollback.
