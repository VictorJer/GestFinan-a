# Design

## Context

Ver [proposal.md](proposal.md). FNR-029 sucede o checkpoint P0 local aceito com ressalva; PRD RF10/RF11 e [delta investment-portfolio](specs/investment-portfolio/spec.md) definem o comportamento. O backend atual separa Domain, Application (MediatR), Infrastructure (EF Core/PostgreSQL) e controllers; `FinoraDbContext` valida entidades antes de `SaveChanges` e mantém migrations administrativas. A API obtém `UserId` das claims de sessão; a fronteira existente já determina 401, 404 uniforme e erros sanitizados. No Angular, `/area/*` usa guard de sessão, serviços de API/estado, menu condicional e descarte de dados ao mudar a geração da sessão. Não há tabela, handler, controller nem tela de investimentos. `docs/DESING.md` §6.4 é referência visual, não fonte de registros reais.

## Goals / Non-Goals

**Goals:**
- Manter um conjunto manual separado do grafo de cartão/Pluggy, persistente e pertencente ao titular, conciliando lista e resumo no backend.
- Reutilizar autenticação, padrões de validação/erros e estado de sessão já existentes, sem criar segundo mecanismo de autorização.
- Acrescentar migration incremental validável, sem mudança de migrations históricas ou bootstrap de banco.

**Non-Goals:**
- Sem integração de investimentos, cotação, ordem de compra/venda, histórico de aportes, taxa de retorno, projeção FNR-030, CSV FNR-031, dados fictícios persistidos ou agregados sobre movimentos do cartão.
- Sem deploy, migration em destino ou aceite de gates P0/MQ/hospedagem nesta etapa.

## Decisions

### D1 — Entidade própria e integridade no banco

Criar `Investment` em Domain com `Id` UUID, `UserId` obrigatório, `Name`, `Class`, `AppliedAmount` e `CurrentAmount`. Em Infrastructure mapear FK restritiva para `ApplicationUser`, índice `(UserId, Id)`, classes como códigos estáveis (`fixed_income`, `variable_income`, `real_estate_funds`) e checks de IDs não vazios, valores não negativos e classe permitida; validar também no Domain/Application e na barreira de gravação. A exclusão de investimento é explícita (não cascata), e nome igual não implica duplicata: posições manuais com nomes iguais são permitidas. Limitar nome a 120 caracteres após trim, recusando vazio/excesso; sem dados reais em seed/migration. Alternativa rejeitada: reutilizar entidades/transações do cartão, pois mistura propriedades e permitiria totais proibidos de movimentos.

### D2 — Unidade e precisão manual

Como RF10 não inclui campo de moeda e RNF-06 pede formato brasileiro, tratar valores desta primeira carteira como BRL, sem conversão ou soma com cartão. Persistir em `numeric(19,2)`, aceitar inclusive zero e recusar escala/overflow antes da coerção do provider. Para evitar perda binária de centavos no JavaScript, a fronteira HTTP recebe e retorna strings decimais canônicas invariáveis com ponto e exatamente duas casas (`0.00`, `123.45`), sem agrupamento, expoente ou zeros à esquerda; valores individuais são não negativos e limitados a `numeric(19,2)`, enquanto a diferença do resumo pode ser negativa. Totais e valores por classe são strings formatadas a partir dos resultados decimais da Application. A interface conserva a string e não converte valores monetários para `Number`; a apresentação localizada BRL em `pt-BR` deve preservar a precisão. Esta é uma premissa explícita para a revisão: se for necessária carteira multimoeda, revisar spec/design antes de código. Alternativa rejeitada: aceitar moedas heterogêneas sem campo de moeda e somá-las; seria um total enganoso.

### D3 — Contrato protegido e resumo em uma leitura

Adicionar `GET /api/investments` devolvendo `{ items, summary }`, ordenação estável por nome e ID; `POST /api/investments` (201 com item), `PUT /api/investments/{id}` (200 com item) e `DELETE /api/investments/{id}` (204). IDs da sessão, nunca de corpo/filtro; payload estrito de `{ name, class, appliedAmount, currentAmount }`, com valores monetários em strings canônicas com duas casas. Toda resposta também serializa valores monetários como strings canônicas, inclusive totais, diferença e distribuição por classe. UUID inválido ou entrada fora de contrato recebe 400; registro alheio/inexistente recebe 404; sem sessão 401; falhas seguem Problem Details sanitizado. Consultas de lista e resumo usam o mesmo conjunto filtrado por `UserId` numa leitura materializada; mudanças retornam apenas após gravação confirmada e o cliente relê o GET. O resumo soma aplicado/atual, calcula diferença e agrupa valor atual por classe; quando o total atual é zero, não inventa percentuais. Alternativa rejeitada: calcular valores no Angular ou agregar `FinancialTransaction`, pois tornaria a conciliação e a separação de domínios frágeis.

### D4 — Página protegida e estado de sessão

Adicionar rota `/area/investimentos`, serviço HTTP/estado vinculado à geração de `SessionService`, página com formulário acessível de criação/edição, confirmação de exclusão e lista/resumo responsivos em português. No `app.html`, mostrar link da carteira somente com sessão; simulador continua sem link ativo. Lidar separadamente com carregando, vazio, erro e salvando; após mutação confirmada reler lista+resumo. Cancelar/descartar respostas e rascunhos privados em logout, expiração ou troca de geração; abrir a página não chama sincronização nem API do provedor. Alternativa rejeitada: guardar carteira só em Signals/localStorage ou usar os quatro ativos fictícios do protótipo, pois não atenderiam à persistência do titular.

### D5 — Testes e documentação proporcionais

Cobrir invariantes/aritmética (zero, diferença negativa, agrupamentos, alterações e remoções), autorização 401/404, erro de validação, DTOs, resultado após recarga, vazio, falhas e isolamento entre sessões. Validar migration em PostgreSQL descartável autorizado em banco vazio e upgrade com dados existentes; verificar que dados P0 permanecem e que nenhuma migration roda no startup. Testes frontend usam respostas sintéticas, sem apresentá-las como integração real. Registrar no backlog/AMBIENTE evidência factual e limites, preservando revisões aprovadas do PRD/TDD se os requisitos permanecerem iguais. Alternativa rejeitada: confiar apenas em mock de UI para comprovar persistência/isolamento.

## Risks / Trade-offs

- [Premissa BRL/duas casas não cobre carteira multimoeda] → registrar limitação e submeter eventual ampliação a revisão de requisitos, sem somar moedas distintas.
- [Lista completa cresce e consulta fica lenta] → começar com tamanho manual limitado pelo uso atual; medir em teste apropriado e especificar paginação/consulta agregada se houver necessidade, sem omitir registros do resumo.
- [Concorrência entre edição e exclusão ou resposta tardia] → validar titular em cada comando, confirmar sucesso apenas depois da gravação e invalidar leituras de geração anterior; não mostrar otimisticamente dados não confirmados.
- [Migrations no ambiente errado ou deploy antes do schema] → usar banco descartável protegido nos testes, executar migration explicitamente no destino somente após autorização e verificação de compatibilidade antes do deploy dependente.

## Migration Plan

1. Gerar migration incremental para `Investment` e conferir diff/snapshot: não reescrever tabelas existentes, alterar dados P0 nem inserir ativos fictícios.
2. Testar aplicação em PostgreSQL descartável vazio e atualizado; validar constraints, CRUD, titularidade e preservação do grafo anterior. Não tocar banco pessoal/Neon nesta mudança de planejamento.
3. Em futura entrega hospedada autorizada, aplicar administrativamente a migration compatível no destino antes da integração que acionaria o deploy dependente; manter aplicação anterior compatível com a tabela adicional. Em falha, bloquear publicação e investigar/restaurar pelo processo de backup; não desfazer migration com dados reais sem plano/aceite explícitos.
