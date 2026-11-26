# Proposal

## Why

O navegador bloqueia no preflight o `POST /api/connections/current/sync/on-entry` da FNR-021: a API exige `Idempotency-Key` para aceitar a intenção, mas sua política CORS permite apenas `Authorization` e `Content-Type`. A correção deve permitir a chamada contratada sem abrir CORS indiscriminadamente ou confundir preflight com autorização da operação.

## What Changes

- Permitir `Idempotency-Key` em preflights de `POST` somente nas duas rotas existentes de intenção de sincronização (`/api/connections/current/sync` e `/api/connections/current/sync/on-entry`) para origens exatas já configuradas; manter as demais rotas, métodos e cabeçalhos com a política atual.
- Preservar a ordem em que o preflight é atendido antes da autenticação, sem alterar exigência de Bearer, titularidade, validação da chave, contrato de resposta ou regras de repetição da intenção.
- Cobrir com testes de host HTTP preflight, negativas de origem/rota/método, cabeçalhos CORS da resposta efetiva e autenticação; conferir a reutilização da mesma chave em replay com os testes existentes. Separar esses testes da futura validação do fluxo no navegador.
- Atualizar, quando da implementação, a documentação canônica afetada pelo contrato CORS e registrar a evidência real e suas limitações sem antecipar aceite da FNR-021 ou do explorador.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `api-access-boundary`: delimitar o cabeçalho adicional de preflight às operações autenticadas de intenção de sincronização, preservando a allowlist de origens e a fronteira de autenticação.

## Impact

- API: `backend/Finora.Api/Cors/FinoraCors.cs`, composição/ordem em `backend/Finora.Api/Program.cs` e testes em `backend/tests/Finora.Api.Tests/CorsContractTests.cs` (com suporte dos testes existentes de sincronização).
- Interface: chamadas já existentes de FNR-021 e testes de replay; nenhuma alteração funcional de Angular, de endpoints ou de modelo de dados está prevista.
- Rastreabilidade: FNR-021 (bloqueio de integração), FNR-011 (política CORS), `docs/TDD.md` e `docs/BACKLOG.md`; sem Pluggy real, banco pessoal, migration, commits, PR ou publicação neste planejamento.
