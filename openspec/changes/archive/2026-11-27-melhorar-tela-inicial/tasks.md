# Tasks

## 1. Alinhamento visual e estrutura da visão

- [x] 1.1 Antes de alterar templates, conferir a referência `docs/DESING.md` §§2–5/7/9/11/13 e a disponibilidade de `outputs/Finora-prototipo.html`; registrar por escrito quais elementos da composição são reaproveitados e quais são excluídos pelo PRD. Se o HTML aparecer e revelar mudança de escopo, revisar artefatos e obter aprovação antes do código; verificar o registro contra proposta/spec.
- [x] 1.2 Recompor `dashboard-overview-page.html` e `dashboard-applied-filters.html` com cabeçalho, contagem do universo persistido filtrado, eixos independentes, cobertura/execução e link à lista em níveis de leitura distintos; verificar em testes de componente rótulos, ordem semântica, filtros aplicados, `Unknown`, estados sem janela e ausência de valores/agregados monetários na visão.
- [x] 1.3 Ajustar estilos/tokens estritamente necessários em `frontend/src/styles.css` e classes da visão para colunas em desktop e reflow em ~900 px, ~560 px e celular estreito; verificar em revisão visual que filtros, cobertura, execução e link continuam visíveis sem corte ou rolagem lateral desnecessária.

## 2. Navegação e estados seguros

- [x] 2.1 Se o contexto da visão exigir ajuste no `frontend/src/app/app.html`, manter marca, skip link, destinos condicionados à sessão, item ativo, logout e barra móvel existentes; verificar com `app.spec.ts`/`app.routes.spec.ts` que `/`, `/login`, `/area`, `/area/movimentos` e `/area/visao` conservam acessos/autorização e que navegação não cria POST de atualização.
- [x] 2.2 Cobrir em `dashboard-overview-page.spec.ts`/`dashboard-applied-filters.spec.ts` carregamento, vazio, falha, retry só GET, resposta anterior da mesma sessão, sessão encerrada e cobertura desconhecida/janela solicitada; verificar mensagens textuais, ausência de fallback fictício, ausência de valor individual na visão e link para a lista única com filtros preservados.
- [x] 2.3 Conferir manualmente foco/teclado, títulos, zoom, contraste, alvos de toque e preferência de movimento reduzido na visão e na navegação móvel, com ensaio de leitor de tela se disponível; registrar resultados e limites concretos sem declarar auditoria não executada.

## 3. Verificação integrada do frontend

- [x] 3.1 Executar suíte frontend e builds de desenvolvimento/produção pelos scripts existentes de `frontend/package.json`, além de `openspec validate melhorar-tela-inicial --strict`; registrar comandos, resultados e limitações reais, incluindo ausência de API/provedor reais e de comparação direta com o HTML se persistir.
- [x] 3.2 Inspecionar diff final e estados visuais usando somente fixtures sintéticas em testes isolados (não como dados da aplicação), conferir que apenas arquivos autorizados foram alterados e que PRD/TDD/BACKLOG, backend, dependências e rotas permaneceram intocados; registrar checklist de aceite dos três requisitos desta delta e apresentar o resultado para revisão humana.

## Evidências da tarefa 2.3

- **Ambiente visual:** rota `/area/visao` aberta em contexto isolado de navegador, com respostas sintéticas interceptadas somente para inspeção; nenhuma API, banco ou provedor real foi consultado.
- **Reflow:** 1280 px exibiu composição principal em duas colunas; 900, 560, 390 e 320 px refluíram em uma coluna. Filtros, cobertura, execução e link permaneceram no DOM e legíveis; `documentElement`/`body` não apresentaram rolagem horizontal. A 560 px e abaixo, o link da lista ocupa a largura disponível.
- **Teclado e semântica:** foco percorreu skip link, navegação e lista com contorno sólido visível; títulos mantiveram H1, H2 e H3 em ordem, e os menus mantiveram nomes acessíveis e `aria-current="page"`. O snapshot de acessibilidade do navegador conferiu landmarks/nomes, mas não substitui leitor de tela.
- **Ampliação:** viewport de 640 px foi usado como equivalente de layout de 200% sobre 1280 px; não foi possível acionar zoom real do navegador pelo harness. Não houve rolagem horizontal nessa largura.
- **Contraste calculado:** texto principal 15,46:1; auxiliar 5,56:1; marca sobre superfície 5,46:1; texto branco sobre item ativo 4,67:1; navegação branca sobre fundo escuro 15,77:1. Os pares medidos superam 4,5:1.
- **Alvos e movimento:** navegação móvel manteve altura mínima de 44 px inclusive a 320 px; rótulos foram ampliados de 11 para 12 px sem overflow. Com `prefers-reduced-motion: reduce`, visão e navegação não tiveram animações nem transformações; permanece somente transição de cor de 150 ms no menu, sem deslocamento espacial.
- **Limite assistivo:** leitor de tela não estava disponível neste ambiente; não foi executado nem declarado ensaio assistivo.

## Validação da tarefa 3.1

- `npm test -- --watch=false`: passou; 19 arquivos de teste, 205 testes.
- `npm run build -- --configuration development`: passou.
- `npm run build -- --configuration production`: passou.
- `openspec validate melhorar-tela-inicial --strict`: mudança válida.
- **Limites:** não houve chamada a API/provedor reais. `outputs/Finora-prototipo.html` continua ausente, portanto não houve comparação direta com o HTML.

## Checklist final de aceite da delta

- **Atende — hierarquia visual e estados honestos:** contexto dos filtros, contagem do universo persistido, natureza e situação independentes, cobertura limitada à janela e acesso à lista única estão em níveis distinguíveis; testes verificam ordem dos títulos, `Unknown`, estados sem janela, conjunto vazio e ausência de dados/valores monetários inventados.
- **Atende — adaptação responsiva e navegação:** a revisão isolada cobriu 1280, 900, 560, 390 e 320 px; conteúdo essencial e link permaneceram legíveis, sem rolagem horizontal. Testes preservam filtros no destino e confirmam que abrir/repetir a visão não inicia POST de atualização.
- **Atende com limites registrados — interação acessível:** títulos e estados têm identificação textual; foco por teclado ficou visível, os contrastes medidos superaram 4,5:1, alvos móveis mantiveram 44 px e movimento reduzido removeu animações/deslocamentos. O harness não permitiu zoom real nem ensaio com leitor de tela; 640 px foi apenas verificação equivalente de reflow, e o leitor de tela não foi testado.
- **Escopo conferido:** `git status`/diff final mostram somente sete arquivos de frontend e os quatro artefatos desta mudança OpenSpec; PRD, TDD, BACKLOG, backend, dependências e definição de rotas não foram alterados. Fixtures sintéticas permaneceram isoladas em testes e na inspeção visual, sem consulta a API, banco ou provedor real.
- `git diff --check` passou; o Git emitiu apenas avisos de normalização LF/CRLF nos arquivos de frontend.

## Encerramento e revisão

- Resultado e limitações apresentados para revisão humana; isso não declara aprovação humana nem substitui os gates aplicáveis.
- O titular autorizou separadamente a sincronização da delta, o arquivamento, os commits locais e a abertura do PR; esta autorização não inclui merge.
