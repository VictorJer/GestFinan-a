# Tasks

## 1. Gate documental e contratos

- [x] 1.1 Reconciliar `docs/BACKLOG.md` 0.68 com o merge local `54514e3` do PR #38 e registrar FNR-021 como selecionada, sem inventar aceite/checks/publicação; verificar o grafo Git e a rastreabilidade FNR-014/020 → RF08/RF09/RF16 → TDD §§6/12/13 → esta mudança.
- [x] 1.2 Apresentar os quatro artefatos OpenSpec e o diff canônico afetado (backlog e TDD somente se houver decisão técnica nova) para aprovação explícita pré-código; verificar que a aprovação/pendências estão registradas e que nenhuma tarefa de implementação iniciou antes do gate.

## 2. Leitura protegida de cartão e conexão

- [x] 2.1 Criar cliente Angular de leitura para `GET /api/cards` e `GET /api/connections/current/sync-state` reutilizando base configurada, Bearer restrito e geração da sessão; testar com respostas sintéticas que 200 vazio, 404, 409, falha parcial, timestamps nulos e cobertura desconhecida não geram dados fictícios nem POST.
- [x] 2.2 Modelar no frontend estados de carga/erro/ausência e retenção de snapshot confirmado durante falha transitória, descartando respostas atrasadas de sessão anterior; verificar testes de cancelamento/logout/expiração e documentar em README ou AMBIENTE o mapeamento de campos e limites de cobertura.

## 3. Intenções de atualização e acompanhamento

- [x] 3.1 Implementar coordenador volátil por geração de login com uma única avaliação on-entry, chave idempotente estável por intenção e chave distinta para modo manual; testar retorno à rota, novo login, 200 `not_required`/`reauthorization_required`, 202 e ausência de POST em GET/navegação.
- [x] 3.2 Estender a classificação segura de erros das rotas protegidas de sincronização para 409 ativo, 429 intervalo e 503 indeterminado, com allowlist/validação de metadados e preservação de 401/403; testar corpos maliciosos, respostas sem extensões, URL externa e regressão do interceptor/logout.
- [x] 3.3 Implementar pedido manual não concorrente e replay explícito com o mesmo modo/chave após timeout, falha de rede ou aceite indeterminado; testar chave em header ASCII válido, não geração de intenção nova até reconciliação e ausência de confirmação falsa sob 409/429/503.
- [x] 3.4 Implementar acompanhamento GET limitado por `syncRunId` com cancelamento por sessão/saída, atualização somente por leitura após estado terminal e mensagem honesta em timeout/falha; testar espera, retomada, falha, sucesso corroborado pelo snapshot e nenhuma importação implícita. Documentar fluxo de retry/acompanhamento e suas limitações em AMBIENTE ou guia equivalente.

## 4. Visão acessível e integração com o shell

- [x] 4.1 Substituir o placeholder `/area` pela visão responsiva de cartões/conexão usando apenas campos disponíveis, estados em português, horários distintos e cobertura explícita; testar com componentes/HTML sanitizado ausência, carregamento, erro, atualização pendente, dados incompletos e ausência de valores fictícios/agregados.
- [x] 4.2 Integrar ação manual, replay/releitura de estado e textos de aceitação versus importação concluída com teclado/foco/aria-live; testar botão duplicado, orientação de reautorização, 401/403, falha de rede e respostas atrasadas, atualizando README/AMBIENTE com a fronteira visual aprovada.
- [x] 4.3 Ajustar rótulos/menu da área protegida sem criar links ativos para FNR-022/023 ou importação na página pública; testar guard, navegação mobile/desktop, 404, logout/expiração e que abrir início/login não envia chamadas financeiras.

## 5. Validação integrada do escopo

- [x] 5.1 Executar `npm ci`, suíte Vitest e builds de desenvolvimento/produção do frontend com regressão FNR-019/020; registrar contagens e limites, distinguindo chamadas HTTP simuladas de eventual API local real e sem afirmar CORS/Pluggy/infraestrutura hospedada sem teste apropriado.
- [x] 5.2 Validar esta mudança OpenSpec em modo estrito, verificar links/rastreabilidade PRD/TDD/BACKLOG, inspecionar segredos e `git diff --check`; apresentar implementação e evidências para aceite humano sem declarar publicação ou conclusão de RF08/RF16 além do comprovado.

## Workflow follow-up

- Após validação e aceite explícito, tratar commits locais via committer, PR e integração conforme `AGENTS.md`, sem presumir autorização nesta proposta.
- Sincronizar as três delta specs e arquivar somente após aceite do escopo; registrar o estado verdadeiro da integração/publicação no backlog. Não iniciar FNR-022 ou FNR-038 por estas tarefas.
