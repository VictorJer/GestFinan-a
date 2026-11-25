# Proposal

## Why

A FNR-019 entregou a base pública Angular, mas o titular ainda não consegue entrar na aplicação nem manter uma sessão local para acessar futuras telas protegidas. A FNR-020 conecta o frontend aos contratos de login/logout e à fronteira de autorização já existentes na API, sem antecipar cartões, movimentos ou publicação.

## What Changes

- Oferecer formulário de login por e-mail e senha em português, com mensagens seguras para credencial inválida, entrada inválida, limite de tentativas e indisponibilidade, sem cadastro público ou recuperação por e-mail.
- Guardar exclusivamente em memória o token Bearer e a expiração retornados pelo backend; não restaurar sessão ao recarregar, não renovar token automaticamente e impedir navegação protegida sem sessão local válida.
- Anexar Bearer somente a requisições protegidas para a origem/base configurada da API Finora; 401 de chamada protegida encerra a sessão local, 403 indica falta de permissão e falha de rede não se confunde com credencial inválida.
- Permitir logout via `POST /api/auth/logout`; limpar a sessão local mesmo quando a confirmação remota falhar, distinguindo revogação confirmada de estado remoto desconhecido.
- Adaptar rotas e navegação públicas da base para login e uma área autenticada **sem dados financeiros**; conservar páginas inexistentes, acessibilidade e ausência de links para funcionalidades FNR-021–023/038 ainda não entregues.
- Preparar testes e evidências proporcionais, com rastreabilidade a FNR-020, PRD RF14/RNF-01/03/06, TDD §§3/5/11/14 e backlog §5. A divergência documental do PR #37/FNR-019 deve ser reconciliada e a revisão pertinente aprovada antes de código dependente; esta proposta não altera o backlog nem pressupõe sua aprovação.

## Capabilities

### New Capabilities

- `web-authenticated-session`: experiência do titular no navegador para entrada, sessão volátil, proteção de rotas e chamadas e saída confiável quanto ao estado remoto.

### Modified Capabilities

- `web-application-shell`: navegação passa a incluir login e área protegida realmente entregues, sem expor destinos financeiros futuros ou fingir dados/conexão.

## Impact

- Frontend `frontend/src/app`: composição HTTP, serviço de sessão, login, guard, interceptor, área protegida informativa, rotas, layout e testes Vitest/Angular; URLs públicas existentes permanecem estáveis.
- Reutiliza `API_BASE_URL` e os contratos atuais `POST /api/auth/login` (200 com `accessToken`, `tokenType`, `expiresIn`, `expiresAtUtc`) e `POST /api/auth/logout` (204 confirmado); não altera backend, schema, migrations, segredos, provider ou locks. A origem de produção continua não configurada até a entrega de publicação.
- FNR-010/011/019 são pré-requisitos; FNR-021/022 dependem desta sessão, FNR-023/038 seguem em mudanças próprias. Não declarar RF14 integrado/publicado nem criar dados financeiros artificiais nesta entrega.
