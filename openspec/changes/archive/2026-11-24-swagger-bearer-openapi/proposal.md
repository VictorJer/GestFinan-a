# Proposal

## Why

O OpenAPI existente descreve rotas públicas e protegidas, mas não informa quais operações exigem Bearer; o Swagger UI não oferece a experiência de autorização para testar chamadas protegidas em Development. A documentação deve refletir a política real da API sem modificar a proteção HTTP.

## What Changes

- Declarar no documento OpenAPI de Development um esquema HTTP `bearer` com formato `JWT` e referências de segurança somente nas operações que efetivamente exigem autenticação.
- Manter login e health sem exigência de segurança documentada; permitir inserir manualmente o `accessToken` no botão Authorize para que o Swagger UI o envie como Bearer nas operações protegidas.
- Preservar a disponibilidade de `/swagger` e `/openapi/v1.json` apenas em Development, a autenticação/autorização existente e os contratos HTTP; validar o documento e a distinção entre operações públicas e protegidas sem banco nem Pluggy.
- Fora do escopo: emissão automática de token pela UI, novos endpoints, mudanças no JWT/sessões/políticas, migrações, publicação e uso de tokens reais em código, exemplos, arquivos ou logs.

## Capabilities

### New Capabilities

- Nenhuma.

### Modified Capabilities

- `api-exploration`: o documento e a interface interativa passam a representar corretamente Bearer JWT e a permitir autorização manual para testar as operações protegidas.

## Impact

- Composição OpenAPI da `backend/Finora.Api` e testes HTTP de exploração em `backend/tests/Finora.Api.Tests`; sem alterar a configuração JwtBearer, a política fallback, os Controllers ou a lógica de autorização.
- Contratos de referência: `docs/BACKLOG.md` (diagnóstico FNR-003 e fronteira de acesso FNR-011), `docs/TDD.md` §§3/5/11 e specs principais `api-exploration` e `api-access-boundary`. Incremento focal de documentação interativa, sem declarar novo item de backlog ou reabrir decisões aprovadas.
- Compatibilidade: somente metadados OpenAPI/uso local do Swagger UI em Development; nenhuma alteração de respostas ou exposição de rotas fora de Development.
