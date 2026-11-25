# Proposal

## Why

A sessão no navegador já funciona, e a API já oferece consulta protegida de cartões, estado da conexão e aceitação/acompanhamento de sincronizações, mas a área autenticada ainda é apenas informativa. A FNR-021 conecta essas peças para que o titular veja seus dados disponíveis e solicite atualização sem confundir aceitação, sucesso, cobertura e dados fictícios.

## What Changes

- Transformar a área autenticada em uma visão de cartão e conexão próprios, com identificação mascarada quando disponível, horários de sucesso/tentativa/origem, cobertura conhecida e estados de carregamento, ausência, falha e reautorização.
- Na entrada autenticada, enviar uma única intenção automática de avaliação da atualização; oferecer botão manual separado e acompanhar a execução aceita até estado terminal ou saída/expiração da sessão. Respeitar decisão do servidor, intervalo compartilhado e idempotência sem criar nova intenção ao navegar, filtrar ou consultar estado.
- Distinguir `202` (intenção persistida), importação concluída, falha e confirmação indeterminada; preservar informações já importadas quando uma atualização falhar, sem anunciar conclusão ou ausência de histórico por uma lista vazia.
- Adaptar o contrato da área autenticada anteriormente apenas informativa para permitir essa visão financeira restrita, mantendo proteção da sessão e ausência de dados simulados no fluxo real.
- Não criar conexão bancária pelo navegador, não solicitar credenciais da instituição, não gerar transações/indicadores monetários, não implementar a lista FNR-022 nem alterar backend, banco, broker ou política de atualização FNR-014.

## Capabilities

### New Capabilities

- `web-card-connection`: apresentação protegida de cartões e estado da conexão, intenção de atualização na entrada e pelo botão, acompanhamento e recuperação honesta da execução.

### Modified Capabilities

- `web-authenticated-session`: substituir a restrição da área autenticada somente informativa por acesso à visão própria de cartão/conexão durante sessão válida, sem enfraquecer guard, Bearer volátil ou proteção do conteúdo.
- `web-application-shell`: atualizar a navegação para refletir a nova visão protegida de cartão/conexão sem anunciar páginas de movimentos ou outras entregas futuras.

## Impact

- FNR-021 (dependências FNR-014/020), PRD RF08/RF09/RF16, TDD §§6/12/13 e contrato HTTP FNR-014, `docs/DESING.md` §13. Reconciliar o registro do PR #38/FNR-020 no backlog antes do gate pré-código; o merge `54514e3` foi observado no Git local, sem inferir aprovação documental, checks ou publicação.
- Frontend Angular: `frontend/src/app/auth/area/`, serviços de HTTP/estado, navegação e testes; reutilizar base de URL, interceptor e sessão existentes. API existente: `GET /api/cards`, `GET /api/connections/current/sync-state`, `POST /api/connections/current/sync/on-entry`, `POST /api/connections/current/sync` e `GET /api/sync-runs/{syncRunId}`; nenhuma rota nova planejada.
- Os dados reais e a conclusão operacional dependem de conexão/importação já provisionadas e de infraestrutura disponível. Testes com respostas sintéticas não comprovam acesso ao provedor, CORS, limites externos, broker hospedado nem publicação.
