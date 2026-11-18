# Proposal

## Why

O armazenamento Identity já está integrado pela FNR-007, mas o titular ainda não pode criar nem recuperar seu acesso por comando. A FNR-008 entrega esse pré-requisito da autenticação sem abrir cadastro público nem introduzir senha em código, argumentos ou logs.

## What Changes

- Adicionar os comandos explícitos `user-create` e `user-reset-password`, sem listener HTTP, com e-mail solicitado interativamente e senha/nova senha confirmada em entrada sem eco.
- Criar o único titular pelos serviços Identity; repetir a criação do mesmo e-mail normalizado informa existência sem alterar senha ou identificador. Recusar segundo titular, inclusive em execuções concorrentes.
- Redefinir a senha de usuário existente pelo Identity, preservando identificador e vínculos financeiros; não exigir senha antiga nem enviar e-mail.
- Aplicar a política **escolhida pelo titular no harness em 17/11/2026**: padrão do Identity, mínimo de 6 caracteres, maiúscula, minúscula, número e caractere não alfanumérico; mínimo de um caractere distinto.
- Rejeitar argumentos adicionais e entrada redirecionada; apresentar resultados/códigos de saída sanitizados, sem dados pessoais, hash, tokens ou configuração sensível.
- Documentar execução, testes isolados e o limite da entrega: FNR-009 implementará login/sessões e FNR-010 integrará a revogação transacional ao reset antes da publicação.

Fora do escopo: JWT, `AuthSessions`, logout, desbloqueio de login, mudança de e-mail, exclusão de usuário, roles, passkeys, cadastro/recuperação HTTP, envio de e-mail, automação não interativa, categorias, coleta Pluggy e publicação. Não criar usuário automaticamente no startup nem aplicar migrations pelo comando. Não alterar as outras mudanças OpenSpec em andamento.

## Capabilities

### New Capabilities

- `administrative-user-access`: provisionamento idempotente e recuperação administrativa de acesso do único titular, por comandos com entrada protegida e resultados seguros.

### Modified Capabilities

Nenhuma. `financial-data-persistence` mantém seus requisitos: a FNR-008 reutiliza `ApplicationUser`, o contexto e o schema existentes, sem redefinir a propriedade dos dados ou a evolução administrativa do banco.

## Impact

- **Rastreabilidade:** [BACKLOG FNR-008 e critérios](../../../../docs/BACKLOG.md), [PRD RF14 e RNF-01–03](../../../../docs/PRD.md), [TDD §§3, 5, 7, 10, 14; DT-23–26/30/38/48–49](../../../../docs/TDD.md). A dependência formal FNR-006 está integrada; a base Identity FNR-007 também está integrada e será reutilizada, sem reabrir essas entregas.
- **Áreas previstas:** roteamento e console em `backend/Finora.Api`; casos de uso/contratos em `backend/Finora.Application`; serviços Identity e composição de persistência em `backend/Finora.Infrastructure`; testes em `backend/tests/Finora.Api.Tests`; instruções e evidências em `docs/AMBIENTE.md`/README e registro FNR-008 no backlog durante a execução autorizada.
- **Compatibilidade:** sem nova rota HTTP ou alteração financeira; preservar health sem banco, OpenAPI/Swagger por ambiente, comandos Pluggy e opções compartilhadas Identity Version2/128. Nenhuma migration prevista.
- **Dependências:** reutilizar .NET 10, Identity/EF Core 10.0.15, Npgsql EF 10.0.3, MediatR 12.5.0 e MSTest/MTP da baseline. Nenhuma instalação ou acesso a banco/provedor integra este planejamento.
- **Aprovação:** o titular aprovou explicitamente a proposta, delta spec, desenho e tarefas no harness em 17/11/2026, selecionando “Aprovar tudo e continuar” após a apresentação do diff documental do BACKLOG 0.10 e TDD 0.12. A aprovação autoriza implementar o escopo aprovado nesta branch, mas não significa aceite da implementação nem autoriza commit, PR, integração ou publicação.
