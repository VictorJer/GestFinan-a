# Proposal

## Why

A FNR-008 disponibilizou o titular administrativo, mas a API ainda não autentica requisições nem persiste sessões. A FNR-009 estabelece o login e a validação de acesso necessários às próximas entregas, conforme [BACKLOG FNR-009](../../../../docs/BACKLOG.md), [PRD RF14](../../../../docs/PRD.md) e [TDD §5, DT-23–30/39](../../../../docs/TDD.md).

## What Changes

- Disponibilizar `POST /api/auth/login` por e-mail/senha Identity, sem cadastro, cookies ou refresh token.
- Aplicar a decisão explícita do titular nesta sessão: cinco falhas bloqueiam novos logins por cinco minutos; sucesso zera o contador; falha de credencial e bloqueio têm resposta genérica. Sessões existentes não são revogadas pelo bloqueio.
- Persistir `AuthSessions` antes de entregar JWT de 30 minutos, sem armazenar o token completo.
- Validar assinatura, algoritmo, emissor, destinatário e validade, e consultar sessão/usuário no banco em cada requisição protegida, sem cache de autorização.
- Diferenciar autenticação inválida de indisponibilidade operacional e preservar `/health` sem dependências externas.
- Acrescentar migration explícita e testes HTTP/PostgreSQL descartáveis, incluindo concorrência, falhas e ausência de vazamento.

## Capabilities

### New Capabilities

- `authenticated-sessions`: login do titular, controle de tentativas, emissão vinculada à persistência e validação de sessões Bearer.

### Modified Capabilities

Nenhuma. `financial-data-persistence` e `api-diagnostics` mantêm seus contratos. A capability administrativa da FNR-008 mantém os comandos e o aviso de revogação pendente.

## Impact

- Api: Controller de login, composição Identity/JWT, middleware e mapeamento de falhas restrito à autenticação; OpenAPI em Development.
- Application: request/handler e contratos de login, emissão e consulta de sessão sem EF/Identity.
- Infrastructure: reutilização da composição Identity, transação de login, entidade/mapeamento de sessão e migration no contexto existente.
- Testes: `backend/tests/Finora.Api.Tests`, inclusive atualização da asserção atual de ausência de AuthSession; guia operacional e rastreabilidade canônica na futura implementação aprovada.
- Dependências propostas: JwtBearer compatível com .NET 10 e biblioteca Microsoft IdentityModel de emissão, com versões/locks verificados antes de instalar.
- Dependência FNR-008 integrada pelo PR #18 (`3e976f3`); encerramento documental, sync e arquivamento presentes no histórico Git local pelo PR #19 (`0303568`, commit `d4204a5`). Conferência desta sessão não repete testes nem constitui nova verificação GitHub.
- Fora do escopo: logout/revogação transacional no reset (FNR-010), segurança geral/propriedade/CORS da API (FNR-011), frontend (FNR-020), publicação, coleta Pluggy e encerramento de outras mudanças. RF14 permanece parcialmente atendido até as entregas dependentes.

Estado em 18/11/2026: proposta, spec, design D1–D7 e tarefas aprovados integralmente pelo titular no harness (“Confirmo e aprovo artefatos”), com confirmação da seleção FNR-009 e nova solicitação de apply. Posteriormente, o titular aprovou o diff TDD 0.13/BACKLOG 0.18 (“Aprovo e continue”), liberando código dependente após tarefa 1.1. Não declara implementação concluída nem autoriza commits, push, PR, merge ou publicação. Política de cinco falhas/cinco minutos preservada.
