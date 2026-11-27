# Design

## Context

Ver `proposal.md` para motivação e `specs/web-card-movement-overview/spec.md` para os critérios observáveis. A comparação direta com `outputs/Finora-prototipo.html` não foi possível: a pasta/arquivo não existem neste checkout; a referência comparável é `docs/DESING.md`, que também registra essa ausência. O contraste abaixo é entre código atual e **descrição documentada** do HTML, não uma inspeção do HTML original.

| Aspecto | Implementação observada | Referência documentada e adaptação segura |
|---|---|---|
| Identidade e shell | `app.html` já contém `finora.`, sidebar escura, roxo/lavanda, topbar, skip link e barra móvel; `styles.css` já define tokens, fontes com fallback e breakpoints ~560/~900 px. | DESING §§2–5/9: preservar a identidade existente; refinar contexto da página/posição do conteúdo sem duplicar o shell nem criar perfil demonstrativo. |
| Hierarquia | `dashboard-overview-page.html` usa cabeçalho em painel, seguido de filtros e seções inteiras empilhadas; `app.html` limita o conteúdo a `max-w-5xl`. | DESING §§5/6.1/7: cabeçalho de página separado, ritmo entre painéis e agrupamento assimétrico no desktop; adaptar largura e colunas para **contagens**, cobertura e filtros, não para os quatro indicadores financeiros fictícios. |
| Dados | `/area/visao` lê `GET /api/dashboard`, exibe `filteredCount`, natureza/situação, janela/último sucesso, execução e filtros aplicados; link leva à lista única com valores individuais. | DESING §13 e PRD §8: manter somente esses fatos/estados, distinguir janela solicitada de histórico integral, preservar `Unknown` e não replicar gráfico de gasto, subtotal ou transações na visão. |
| Responsividade e interação | Navegação lateral compacta e barra inferior já dão acesso às rotas; filtro tem componente próprio e a visão usa texto/HTML e retry GET. | DESING §§9/11: preservar acesso, ordem de leitura e foco em desktop/intermediário/mobile, sem usar tamanhos auxiliares de 8–11 px nem ocultar rotas necessárias. |

### Conferência da referência visual — tarefa 1.1

Em 27/11/2026, foram conferidas as seções 2–5, 7, 9, 11 e 13 de `docs/DESING.md`. O arquivo `outputs/Finora-prototipo.html` e a pasta `outputs/` não existem neste checkout, portanto esta decisão usa somente a referência documentada e não declara comparação direta com o HTML.

- **Reaproveitar:** identidade Finora roxa/lavanda, superfícies claras, navegação compartilhada já existente, cabeçalho de página, painéis e hierarquia tipográfica; adaptar a composição em colunas para contagem descritiva, filtros aplicados, distribuições independentes e cobertura/execução, mantendo o acesso à lista única.
- **Excluir:** saldo, receitas, gastos, patrimônio, limites e perfil demonstrativos; gráficos e agregados monetários; amostra de movimentos/valores individuais na visão; seletor de mês, conexão ou sincronização simuladas e indicadores sem fonte autorizada. Valores individuais continuam restritos à lista existente.
- **Conferência de escopo:** a separação acima corresponde à proposta (somente dados descritivos autorizados e sem indicadores fictícios) e à delta `web-card-movement-overview` (hierarquia de filtros, conjunto filtrado, eixos, cobertura e link, sem agregados monetários). Não foi identificado requisito novo nem conflito que exija revisão dos artefatos.

Base de comportamento herdada: `openspec/specs/web-card-movement-overview/spec.md`, `web-application-shell`, `web-authenticated-session` e `card-movement-explorer`; PRD RF04/05/07/09/14, RNF-02/03/06/07, TDD DT-10/18/28/56 e §13. BACKLOG FNR-019/FNR-023 são a base visual/funcional já entregue, com limites de FNR-020/021/022. A revisão histórica não transfere aprovação para este desenho.

## Goals / Non-Goals

**Goals:**
- Usar os tokens já instalados para criar um percurso de leitura claro: cabeçalho/contexto, filtros aplicados, contagem do universo filtrado, natureza e situação como eixos distintos, janela/cobertura/execução, link para a lista única.
- Dar à visão uma composição responsiva próxima do protótipo documentado, com acessibilidade e limites dos dados mais fáceis de localizar do que hoje.
- Preservar rotas, estado de sessão e consulta existentes, inclusive retorno à lista com filtros aplicados, GET independente e apresentação de erro/resposta anterior.

**Non-Goals:**
- Converter a visão em painel de saldo/gasto, destacar valores individuais nela, criar dashboard público, gráficos, nova fonte de dados, nova chamada à API ou alterar a lógica de importação e filtros.
- Implementar funcionalidades de investimentos/CSV, autoconexão bancária ou um modo de demonstração na aplicação real.

## Decisions

1. **Recompor o template da visão, não o contrato de estado.** `dashboard-overview-page.html` e `dashboard-applied-filters.html` concentram o refinamento; `DashboardOverviewStateService` e `DashboardOverviewQueryService` continuam como fonte de GET e proteção de respostas tardias. Alternativa rejeitada: criar endpoint, view model paralelo ou recalcular contagens a partir da lista, o que duplicaria dados e poderia enganar sobre universo/cobertura.
2. **Uma área principal de leitura e um apoio operacional em layout adaptável.** Cabeçalho com título/descrição e acesso à lista; destacar contagem com rótulo de universo; agrupar naturezas e situações sem equivalência financeira; apresentar janela/sucesso/execução em seções próprias. Em desktop, aproveitar colunas assimétricas ou agrupamentos laterais legíveis; em intermediário e celular, empilhar sem mudar a ordem semântica ou ocultar a cobertura. Alternativa rejeitada: repetir a grade de quatro KPIs do protótipo, pois três dos indicadores não têm fonte autorizada e o quarto não representa gasto validado. Não transformar contagens em percentuais ou gráficos de gasto.
3. **Aproveitar os tokens sem copiar CSS/JavaScript do protótipo.** `styles.css` já define cores, fontes com fallback e breakpoints alinhados à referência; ajustar só tokens/estilos necessários, com contrastes e tamanhos legíveis. A largura `max-w-5xl` do shell poderá ser ampliada **apenas se** a composição precisar e as demais páginas continuarem legíveis; preferir regra localizada na visão. Alternativa rejeitada: nova biblioteca, fontes externas obrigatórias, iconografia sem texto ou transformação do protótipo de arquivo único em arquitetura Angular.
4. **Navegação contextual sem mudar destinos ou autorização.** Preservar sidebar/barra móvel, estados ativos e skip link existentes em `app.html`; se houver ajuste visual compartilhado, conferir `/`, `/login`, `/area`, `/area/movimentos` e `/area/visao` com e sem sessão, sem novo redirecionamento, CTA falso, POST ou alteração no guard/interceptor. Alternativa rejeitada: usar `/` como dashboard financeiro ou promover o protótipo demonstrativo a página pública; isso viola a fronteira de sessão.
5. **Estados e acessibilidade têm a mesma prioridade do layout carregado.** Manter `aria`/títulos, foco, mensagens textuais e retry GET, preservar a indicação de resposta anterior e não resolver vazio/falha com números de fallback; no mobile, controles com alvos utilizáveis, reflow com zoom e sem navegação essencial encoberta. Alternativa rejeitada: cards puramente cromáticos, valores placeholder e transições indispensáveis para compreender estado. Se houver animação decorativa, respeitar preferência de movimento reduzido.

## Risks / Trade-offs

- [Fonte HTML ausente e `docs/DESING.md` desatualizado quanto ao caminho] → usar somente características descritas, identificar explicitamente a comparação indireta e conferir o HTML quando acessível; se revelar conflito de escopo, revisar artefatos e pedir nova revisão antes de implementar a mudança de premissa.
- [Shell compartilhado afeta login/página pública e navegação móvel] → alterações mínimas em `app.html`/`styles.css`, regressão de rotas/guard/logout/skip link e revisão em 560/900 px, telas estreitas e zoom.
- [Visual sugerir gasto ou histórico completo] → rótulos explícitos do universo persistido filtrado e da janela solicitada, sem agregados, gráficos monetários, valores individuais na visão ou preenchimento fictício.
- [Contraste da referência e fontes remotas indisponíveis] → usar fallback atual, verificar contraste e legibilidade dos estados/foco, inclusive com leitor de tela quando disponível; não declarar conformidade sem ensaio.
- [Duas consultas GET em instantes diferentes] → não prometer snapshot atômico entre visão e lista, preservar o aviso existente e filtros aplicados, e manter falhas/estado anterior claramente identificados.

## Migration Plan

Mudança de apresentação frontend, sem migration de banco, deploy de backend, dependência nova ou alteração de contrato. Após aprovação humana e implementação futura na feature branch, validar testes/build e revisão visual antes de PR; reversão possível pelo PR que restabeleça o template/estilo anteriores sem tocar estado ou dados. A publicação continua sujeita aos gates próprios do projeto.

## Open Questions

- Quando `outputs/Finora-prototipo.html` estiver disponível, há detalhes visuais relevantes não registrados em `docs/DESING.md` que possam ser adaptados **sem** ampliar comportamento ou conteúdo? A referência direta não foi verificada; qualquer novo requisito exigirá revisão dos artefatos, não adoção silenciosa.
- O titular quer registrar esta melhoria como novo item no backlog ou como acompanhamento visual de FNR-023? Este planejamento rastreia as entregas existentes sem criar ID/status nem editar o backlog; a decisão administrativa não altera o contrato visual aqui descrito.
