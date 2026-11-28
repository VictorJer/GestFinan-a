# Tasks

## 1. Rastreabilidade e aprovação pré-código

- [x] 1.1 Reconciliar em `docs/BACKLOG.md` o merge local do PR #52 sem inferir checks/review remoto e registrar FNR-030 como mudança principal; preparar revisões rastreáveis de PRD §8/RF12 e TDD §11/§13 para tetos, cenários de sessão e arredondamento confirmados, preservando estados históricos. Verificar diff documental e links locais, mantendo as revisões novas como pendentes de aceite próprio.
- [x] 1.2 Apresentar proposta, duas delta specs, design, tarefas e revisões documentais afetadas para aprovação explícita do gate pré-código; verificar que a decisão e eventuais ressalvas ficaram registradas antes de iniciar 2.x/3.x (artefatos completos ou validação CLI não contam como aceite).

## 2. Cálculo e contrato HTTP protegido

- [x] 2.1 Implementar calculador puro com série mês 0..N, taxa anual efetiva, aporte ao final do mês, acumulado e retorno, sem arredondamento intermediário e com limites de representação; verificar testes unitários com vetores independentes, taxa zero, bordas 0/50%, 1/30 anos e reconciliação dos centavos exibidos.
- [x] 2.2 Implementar `POST /api/simulations/calculate` com autorização, JSON estrito, limites 10 milhões/1 milhão, percentuais/prazo, DTOs monetários textuais e erros sanitizados, sem leitura/escrita de carteira; verificar testes HTTP de 200/400/401/429 quando aplicável, campos extras/duplicados/tipos inválidos e ausência de dados sensíveis nas respostas.
- [x] 2.3 Conectar o cálculo à composição autenticada sem ativar handlers de CLI/Pluggy na composição sem configuração e documentar contrato na API/OpenAPI conforme padrão do repositório; verificar teste de composição isolada e contrato OpenAPI/rota protegida com configuração válida e inválida.

## 3. Interface e comparação efêmera

- [x] 3.1 Criar cliente Angular para resposta allowlisted do simulador e leitura protegida opcional da carteira existente, com base configurada e Bearer restrito; verificar testes de URL, formatos decimais, respostas inválidas, 401 e ausência de POST quando falta sessão.
- [x] 3.2 Implementar estado do cálculo com debounce de entradas válidas, cancelamento/identificador de requisição e filtragem por geração de sessão, mantendo cenário confirmado separado de formulário inválido; verificar testes de alterações rápidas, falha de rede, expiração, resposta tardia e nenhuma publicação em outra sessão.
- [x] 3.3 Implementar guarda de até cinco cenários em memória, comparação de totais e séries e limpeza individual/total, e ação explícita “Usar valor da carteira” sem truncamento nem mutação; verificar testes de limite, releitura somente sob clique, total acima do teto, edição concorrente do campo, logout/recarga e carteira inalterada.
- [x] 3.4 Construir página protegida com validação por campo, projeção nominal, gráfico de patrimônio/aportes com alternativa textual de série, estados de erro/vazio e avisos de premissas; verificar testes de teclado, mensagens, comparação e largura móvel/desktop sem cálculo monetário no navegador.
- [x] 3.5 Conectar `/area/simulador` lazy ao guard e menus desktop/mobile sem antecipar recursos futuros ou disparar importação/leitura da carteira ao navegar; verificar testes de rota direta sem sessão, links após login/logout e navegação sem POST de sincronização.

## 4. Conferência integrada do escopo

- [x] 4.1 Exercitar jornada integrada local com sessão sintética: editar entradas, receber projeção da API, salvar/comparar cenários, copiar total de carteira de teste via `GET /api/investments` autorizado, expirar sessão e confirmar isolamento; verificar teste de integração frontend/API ou registrar claramente quais trechos foram simulados, sem chamar fixture de prova hospedada ou de pessoa real.

**Evidência 4.1:** `frontend/src/app/auth/area/investment-simulation-journey.spec.ts` exercita a página, o estado, o cliente HTTP e o interceptor reais do frontend com sessão/token sintéticos. `HttpTestingController` substitui a API nas respostas do cálculo e do GET `/api/investments`; o teste valida verbos, corpos, Bearer, cópia, comparação e expiração. Nenhum servidor de API, banco, carteira de pessoa real ou ambiente hospedado foi consultado; as fixtures não comprovam integração remota.
- [x] 4.2 Executar builds e testes afetados de backend/frontend, validação OpenSpec estrita, formatter, `git diff --check` e links documentais; registrar resultados reais, ressalvas e estado de 0/pendências em tarefas/backlog sem marcar concluído por mera geração de artefatos.

**Evidência 4.2 (28/11/2026):** `dotnet build backend/Finora.Api/Finora.Api.csproj --no-restore` passou com 0 avisos/0 erros; os testes backend focados `InvestmentSimulation|AuthenticationAvailability` passaram 21/21. `npm test -- --watch=false` passou 283/283 em 26 arquivos; builds Angular de desenvolvimento e produção passaram, incluindo chunk lazy do simulador. `dotnet format whitespace` em arquivos backend afetados e Prettier nos 12 arquivos frontend conferidos passaram sem mudanças pendentes. `openspec validate fnr-030-simulador-aportes --strict` declarou a mudança válida; `git diff --check` não encontrou erros (apenas avisos informativos LF→CRLF) e 187 destinos de links Markdown locais em oito documentos/artefatos existem. Testes HTTP da jornada usam fixtures do `HttpTestingController`, conforme ressalva 4.1: não demonstram serviço hospedado, API em execução, banco ou carteira real. Resultado local: 12/12 tarefas executadas; aceite da implementação e revisão BACKLOG 0.110 continuam pendentes, sem autorização para commit, push, PR, sync/arquivo, merge ou publicação.

## Aprovação do gate pré-código

- Em 28/11/2026, o titular aprovou explicitamente no harness a proposta, as duas delta specs, o design, as tarefas e as revisões PRD 0.7/TDD 0.48 como gate pré-código da FNR-030, autorizando iniciar as tarefas de código. Não foram registradas ressalvas. A decisão não é aceite da implementação nem autorização para commit, push, PR, merge, sync/arquivo ou publicação; a revisão documental BACKLOG 0.110 permanece pendente de aceite próprio.

## Workflow follow-up

- Após implementação validada e aceite explícito, planejar sync das delta specs e arquivo conforme fluxo do repositório; arquivo não comprova merge nem publicação.
- Solicitar autorização específica para commits locais, push e PR; verificar checks/revisões aplicáveis antes de qualquer integração. FNR-031 e FNR-028 continuam gates separados da primeira publicação.
