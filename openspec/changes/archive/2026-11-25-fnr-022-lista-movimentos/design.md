# Design

## Context

Ver `proposal.md` para motivação e `specs/` para os contratos observáveis. O frontend Angular possui `/area` protegida, menu desktop/móvel, `SessionService` volátil, interceptor Bearer restrito à base da API e serviços de cartões com projeção allowlist e geração de sessão. Ainda não existe cliente nem tela de movimentos. O backend já oferece `GET /api/transactions`, `GET/POST /api/categories` e `PATCH /api/transactions/{id}/annotation`; paginação/contagem, cobertura, limite dos filtros e escrita parcial pertencem à API. O resultado do PR #39 consta do Git local; a revisão 0.70 do backlog permanece pendente de reconciliação/aceite próprios.

## Goals / Non-Goals

**Goals:**
- Tornar a lista reutilizável posteriormente pela FNR-023 sem levar lógica de filtros, contagem ou classificação financeira para um dashboard paralelo.
- Preservar isolamento por sessão, leitura sob demanda e separação entre histórico consultado, execução de sincronização e confirmação de anotação.
- Usar contratos já existentes e testar a apresentação descritiva sem depender de dados do provedor ou API hospedada.

**Non-Goals:**
- Alterar DTOs/rotas de backend, migrations, corretor de natureza, regra de compra/competência ou importação.
- Construir resumos/gráficos FNR-023, classificador FNR-038, exportação, dados fictícios de produção ou homologação de CORS/Pluggy real.

## Decisions

### D1 — Rota própria, com guarda existente e nenhuma intenção adicional

Adicionar `/area/movimentos` com a mesma guarda usada por `/area` e expor o link apenas com sessão local válida no menu desktop/móvel. Deixar `AuthenticatedAreaPage` responsável pela entrada e sua avaliação única por geração; a lista não invoca `evaluateOnEntry`, endpoints `/sync*` nem os serviços de intenção. Manter ligação à área de cartões para eventual atualização manual explícita. Uma rota separada evita montar o estado de conexão durante cada filtro; incorporar tudo na página de cartões criaria novo acoplamento e risco de POST involuntário. A distinção entre rota protegida local e autorização real pela API permanece.

### D2 — Cliente e estado próprios, sem duplicar contrato do servidor

Criar um cliente de leitura/escrita da lista que use `NORMALIZED_API_BASE_URL`, HttpClient e interceptor existentes; ler respostas como `unknown` e projetar allowlists compatíveis com os DTOs verificados em `TransactionsController`/README. Consultar páginas sob `cardId?`, `categoryId?`, `situation?`, `from?`, `toExclusive?`, `search?`, `page?`, `pageSize?`; não inventar filtros, ordenação ou números totais locais. Usar `pageSize` fixo inicial de 25 (dentro do limite 1–100). Obter cartões por leitura protegida e categorias do catálogo para opções; “Sem categoria” é opção de edição com ID nulo, não filtro de categoria não atribuída que a API não oferece. Validar antes da requisição intervalos (`yyyy-MM-dd`, fim exclusivo posterior), tamanho de busca (máximo 200), observação (até 500) e nome da categoria (até 60), sem substituir a validação da API. Alternativa de copiar respostas sem projeção é rejeitada porque permitiria exibir campos não aprovados ou erros brutos.

### D3 — Snapshot da sessão e transições coerentes de lista/filtros

Manter rascunho de filtros separado dos filtros aplicados e da página atual. Aplicar novo filtro volta à página 1; paginar conserva o mesmo conjunto; `filteredCount` e `coverage` vêm somente da resposta correspondente. Consulta posterior cancela/ignora a anterior; o estado mostra carregando e não mistura itens/contagem/cobertura de filtros distintos. Após edição confirmada, reler a página/filtros ativos: se o movimento saiu do filtro ou a página deixou de existir, ajustar para página válida e reler; manter a seleção do titular sem recarregar a aplicação. Em falha transitória reter apenas o último snapshot confirmado na mesma geração, identificado como anterior, com repetição explícita. No fim da sessão, apagar snapshots e cancelar requests; sucesso tardio de leitura/escrita da geração antiga nunca modifica a nova. Reutilizar o padrão `CardConnectionStateService` sem fazer a lista depender de um serviço de sincronização.

### D4 — Escrita confirmada e opções do catálogo

`GET /api/categories` popula opções de sistema, pessoais e virtual. `POST /api/categories` envia somente `{ name }`; 201 adiciona a opção confirmada ao seletor; 409, 400 e falhas não criam opção. `PATCH /api/transactions/{id}/annotation` envia somente campos realmente editados: omissão preserva o outro, `categoryId: null` remove associação, `note: null`/`""` limpa observação. A UI não transforma uma tentativa pendente em sucesso e, após confirmação, relê a página para refletir filtro e contagem da API; sem mutation otimista que produza contagem incorreta. Ao receber 404 para movimento/categoria, orientar nova leitura sem inferir propriedade. Apenas a lista/seletores de apresentação mudam; fatos importados permanecem intactos.

### D5 — Exibição conservadora, privada e acessível

Apresentar valor individual junto da moeda informada sem somar ou converter; uma lista com moedas/sinais mistos continua sendo apenas uma lista. Data original é texto de origem, `transactionDate` é o dia técnico do filtro; datas inválidas ou ausentes recebem rótulo seguro, não competência inventada. Mapear naturezas, situações e motivos conhecidos a textos controlados, mantendo desconhecidos explícitos; não interpolar corpo de erro ou enum não reconhecido como orientação. Mostrar `coverage.lastSuccessfulImportAtUtc`, janela confirmada e execução recente separadamente; `CompleteForRequestedWindow` não prova histórico integral. Usar rótulos, tabela/lista responsiva com rolagem quando necessária, controles e foco visíveis, feedback textual e navegação por teclado. Acessibilidade e ausência de agregado monetário são verificadas nas telas mobile/desktop e testes, não inferidas do design visual.

## Risks / Trade-offs

- [Contrato backend evoluir sem aviso] → Validar shape e campos conhecidos em testes HTTP sintéticos, comparar com DTO/README antes de implementar; mudanças incompatíveis exigem revisão do planejamento, não fallback silencioso.
- [Resposta tardia ou falha de PATCH causar vazamento/perda de edição] → Geração de sessão, cancelamento, confirmação explícita e releitura do servidor; testar logout/401 durante GET/POST/PATCH e falhas concorrentes.
- [Trocar categoria faz a página atual parecer vazia] → Distinguir `filteredCount` de itens da página e ajustar página após confirmação com nova consulta, sem calcular contagem local.
- [Dados antigos ou cobertura parcial parecerem gastos completos] → Mensagens distintas para janela confirmada, última execução, filtro vazio e ausência de histórico; proibir agregados inclusive sob rótulo “informativo”.
- [Testes locais confundidos com integração real] → Registrar testes de componente/HttpTestingController como sintéticos; API, CORS e provedor reais permanecem pendentes para os gates de publicação.
- [Mudança transversal do explorador duplicar FNR-022/023] → Esta entrega cria apenas a lista; manter tarefas da mudança antiga como pendentes até conciliação autorizada na fase de encerramento das entregas próprias.

## Migration Plan

Nenhuma migration de banco nem mudança de contrato HTTP. Liberar rota/link na entrega da UI por PR após aprovação dos artefatos, testes/builds e critérios aplicáveis. Se a lista precisar ser retirada, reverter a entrega frontend via PR preservando API, dados e anotações persistidos; não executar rollback destrutivo de dados. A revisão documental FNR-021 e seus gates permanecem independentes.
