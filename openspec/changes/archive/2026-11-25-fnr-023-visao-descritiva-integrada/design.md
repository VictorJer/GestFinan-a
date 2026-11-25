# Design

## Context

Ver `proposal.md` para a motivação. `GET /api/dashboard` já devolve `filteredCount`, `countsByNature`, `countsBySituation`, `coverage` e `dateBasis: "TransactionDate"` sem paginação nem valores monetários; `GET /api/transactions` usa os mesmos seis filtros e paginação separada. A lista Angular existente em `/area/movimentos` guarda os filtros **aplicados**, a página e o rascunho em `TransactionListStateService`, por geração da sessão; a página de cartões/conexão fica em `/area`. O menu desktop/mobile só exibe destinos protegidos com sessão. Não há página frontend de resumo, nem necessidade de endpoint ou migration novos.

## Goals / Non-Goals

**Goals:**
- Compor a leitura do resumo existente em uma página protegida própria, aproveitando o estado de filtros da lista, com separação clara entre contagens, cobertura e execução.
- Garantir interpretação conservadora de `Unknown`, `TransactionDate`, filtro vazio e erro; manter privacidade no encerramento/troca de sessão.
- Validar que os dois destinos consultam o mesmo conjunto de filtros e que navegação não dispara importação.

**Non-Goals:**
- Refazer a lista, colocar controles de edição/paginação na visão ou copiar todos os seus filtros em uma segunda implementação.
- Alterar a semântica da API, classificar compras, implementar recorrência FNR-038, gráfico financeiro, persistência de estado de filtros após recarga ou integração real com provedor.

## Decisions

### D1. Página protegida independente ligada à lista existente

Adicionar `/area/visao` com o mesmo guard de `/area/movimentos`, link protegido no menu desktop/mobile e link textual para a lista. A visão mostra os filtros ativos e encaminha à lista para alterá-los; **não** duplica seu formulário, sua paginação ou a edição. O estado de entrada permanece não filtrado quando a sessão não aplicou filtro algum. Alternativa: expandir `/area` ou embutir outra lista no dashboard; rejeitada para não acoplar a entrada que avalia atualização automática nem criar duas experiências de organização.

### D2. Um único conjunto de filtros aplicados por geração de sessão

Ler `TransactionListStateService.view().filters` como fonte dos filtros efetivamente aplicados; ignorar rascunho ainda não submetido. O serviço já limpa estado ao mudar a geração de sessão. Uma leitura do resumo usa somente `cardId`, `categoryId`, `situation`, `from`, `toExclusive` e `search`; nunca envia `page`/`pageSize`. A lista continua a única responsável por validar/aplicar alterações e por consultar sua página ao retornar. Ajustar, se necessário, a apresentação do rascunho ao retornar para que controles e filtros aplicados não contradigam o resumo. Alternativa: duplicar formulário/validação ou persistir filtros em storage/URL; rejeitada para evitar duas semânticas e exposição ou restauração de estado de sessão que o MVP não promete.

### D3. Cliente estrito e estado de resumo independente da página

Criar cliente de leitura allowlist do resumo usando base pública da API e interceptor de sessão já configurados. Decodificar `dateBasis`, contagem inteira não negativa, eixos com códigos conhecidos e contagens válidas, e cobertura no formato já utilizado pela lista; rejeitar resposta incompatível e dados monetários inesperados, sem produzir zeros de fallback. Manter estado da visão (`idle/loading/loaded/empty/error`) associado à geração, filtros e identificador da requisição; cancelar ou descartar respostas tardias ao mudar a sessão, sair da página ou repetir leitura. Releitura explícita e retorno à visão após editar categoria consultam novamente a API; não derivar contagens dos 25 itens nem reutilizar snapshot de outro filtro. Alternativa: compartilhar o snapshot paginado ou agregar no browser; rejeitada pela semântica de universo completo e pela proibição de valores derivados.

### D4. Mensagens e consistência observável sem prometer atomicidade entre rotas

Apresentar dois eixos com rótulos em português (`Unknown` destacado), `filteredCount` e cobertura em blocos separados, com origem do dia técnico, janela confirmada e execução recente; somente números de contagens, nunca valores individuais na visão. Exibir estado vazio como “sem movimentos persistidos neste filtro” e cobertura ausente como “desconhecida”; falha tipada usa mensagens fixas, sem corpo remoto. Conferir em testes que lista e visão recebem os mesmos filtros e que contagens correspondem quando leem o **mesmo estado controlado**; não prometer igualdade instantânea entre dois GETs separados se uma importação/edição intercalar as leituras. Alternativa: copiar parte do JSON de `GET /api/transactions` para montar o resumo; rejeitada por inconsistência com a paginação e custo de leitura.

### D5. Rastreabilidade sem aceitar trabalho alheio retroativamente

A FNR-023 implementa somente os cenários de visão/navegação; requisitos da API FNR-018 e lista FNR-022 continuam em suas specs principais. Documentar a relação das tarefas 3.1–3.4 do explorador legado com a evidência produzida, mas não alterar automaticamente suas caixas, mudar a capability `card-movement-explorer` ou declarar seu aceite. Corrigir o registro factual “integração pendente” da FNR-022 no backlog durante a etapa documental, preservando revisão 0.75 e sua aprovação pendente; a aprovação dos artefatos desta mudança não aprova revisões canônicas novas. Alternativa: absorver/arquivar o explorador junto desta entrega; rejeitada por escopo e autorização distintos.

## Risks / Trade-offs

- [Dois GETs não são transação única] → comparar filtros e datasets controlados nos testes, indicar momento da releitura e não afirmar consistência temporal absoluta entre páginas.
- [Resposta parcial/enum inesperado pode fabricar resultado] → parser allowlist, erro seguro e testes de `Unknown`, zeros válidos, campos ausentes e incoerência; não ecoar payload externo.
- [Requisição após logout ou mudança de filtros] → vincular snapshot e resposta à geração/identificador e descartar resultado obsoleto; evitar cache entre sessões.
- [Entrar em `/area` para acessar resumo pode disparar avaliação automática] → rota `/area/visao` separada; só a entrada original na página de conexão mantém sua política atual.
- [Docs e mudança antiga registram estados divergentes] → reconciliar somente a FNR-022 e a matriz FNR-023 na revisão documental apropriada, sem marcar tarefas antigas nem presumir aprovação.

## Migration Plan

Nenhuma migration ou alteração de backend. Após aprovação documental dos artefatos, implementar a página e testes na feature branch, validar frontend e OpenSpec, e apresentar os resultados antes de commits/PR autorizados. Desfazer a exposição da nova rota e do link retorna ao comportamento anterior, sem alteração de dados persistidos ou contratos HTTP; publicação depende do fluxo de PR e dos gates próprios da entrega hospedada.
