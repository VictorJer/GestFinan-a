# Tasks

Escopo exclusivo: **FNR-009**. Referências: [proposta](proposal.md), [spec](specs/authenticated-sessions/spec.md), [SDD D1–D7](design.md), [BACKLOG](../../../../docs/BACKLOG.md), [PRD RF14](../../../../docs/PRD.md) e [TDD §5](../../../../docs/TDD.md).

**Gate em 18/11/2026:** proposta/spec/design D1–D7/tarefas aprovados integralmente pelo titular no harness (“Confirmo e aprovo artefatos”), após nova solicitação de apply e confirmação da seleção FNR-009. Política cinco falhas/cinco minutos preservada. Encerramento FNR-008 conferido no histórico local pelo PR #19/`0303568`, com spec consolidada e 17/17 tarefas arquivadas. Posteriormente o titular aprovou o diff TDD 0.13/BACKLOG 0.18 (“Aprovo e continue”), liberando código dependente. As caixas exigem execução real, não mera existência/aprovação de documentos. Sem autorização de commits, push, PR, merge ou publicação.

## 1. Alinhamento e composição

- [x] 1.1 Conferir evidência de encerramento FNR-008, registrar FNR-009/branch no BACKLOG e consolidar somente decisões novas D1–D7 no TDD; preservar IDs/histórico e obter aprovação das revisões pertinentes antes de código dependente. Verificar links e registro explícito de aprovação, sem reabrir política de tentativas já escolhida.
- [x] 1.2 Conferir metadados, licença, advisories e compatibilidade do JwtBearer 10.0.15 candidato e da versão IdentityModel compatível com o grafo; fixar referências/locks reais e registrar versões em AMBIENTE. Verificar restore bloqueado e build; alteração de baseline exige decisão antes de instalar.
- [x] 1.3 Criar contratos/resultados e handler MediatR de login na Application, sem EF/Identity/IdentityModel, redigindo representações textuais sensíveis. Verificar testes de delegação/cancelamento/ToString e ArchitectureTests; comentar blocos lógicos em português.
- [x] 1.4 Extrair composição compartilhada de contexto/Identity e protocolo do lock administrativo, preservando Version2/128, política de senha e serviços exclusivos dos comandos. Verificar testes DI, modelo/factory e comandos administrativos sem JWT; documentar separação HTTP/CLI no guia.

## 2. Persistência das sessões

- [x] 2.1 Adicionar entidade/mapeamento AuthSessions conforme D4, gerador de identificador criptográfico, FK Restrict e constraints/índice. Atualizar teste que exige ausência de AuthSession. Verificar modelo, unicidade, FK, datas inválidas, round-trip UTC e ausência de campos sensíveis no PostgreSQL descartável.
- [x] 2.2 Gerar migration incremental sem editar migrations antigas; testar base vazia, upgrade FNR-007 com usuário/grafo sintéticos, rollback da nova tabela e reaplicação. Documentar comandos explícitos em AMBIENTE e verificar ausência de migration/seed no startup.

## 3. Emissão e configuração JWT

- [x] 3.1 Implementar configuração validada e adaptador de emissão D5 com HS256, segredo externo, claims mínimas e expiração de 1800 segundos. Testar chave ausente/malformada/curta, issuer/audience, aleatoriedade de jti, campos/precisão temporal e ausência de segredo em exceções/logs.
- [x] 3.2 Implementar estado de autenticação indisponível e composição HTTP D1 sem impedir health ou resolver conexão inválida. Testar host sem banco/JWT/Pluggy, configuração parcial e health com headers Bearer inválidos; documentar variáveis, liveness e comportamento 503 sem exemplos de segredos reais.
- [x] 3.3 Documentar rotação coordenada e persistência por ambiente; verificar com chaves sintéticas que reinício com a mesma chave preserva validação e troca de chave rejeita tokens anteriores. Testar expiração exata e emissão futura com relógio controlado.

## 4. Login transacional e tentativas

- [x] 4.1 Implementar leitura do titular, normalização de e-mail, preservação da senha e controle Identity sob lock compartilhado. Testar conta ausente/múltipla, LockoutEnabled legado, cinco falhas/cinco minutos, não extensão durante bloqueio e reset do contador em sucesso, com estado persistido entre contextos.
- [x] 4.2 Integrar preparação do token, gravação da sessão e reset do contador numa transação; entregar resultado somente após commit. Testar duas sessões distintas, cinco falhas concorrentes sem perda, erro ao gravar contador, erro de assinatura/persistência, rollback, timeout/cancelamento e confirmação incerta do commit no banco descartável.
- [x] 4.3 Verificar concorrência login/reset sob o protocolo compartilhado, com barreiras determinísticas: login posterior ao reset revalida credencial nova, sem validar senha obsoleta carregada antes do lock. Documentar que sessões anteriores ainda não são revogadas e que a garantia completa pertence à FNR-010.
- [x] 4.4 Expor Controller login/DTO, limites 256/1024 caracteres e 16 KiB, respostas 200/400/401/413/503/500 sanitizadas e no-store conforme D2. Testar HTTP com Identity real, corpo inválido, e-mail normalizado, senha exata, conta desconhecida/bloqueada com resposta genérica e ausência de cookie/refresh. Atualizar OpenAPI e guia do contrato sem token de exemplo real.

## 5. Validação de cada chamada protegida

- [x] 5.1 Configurar JwtBearer/ordem de middleware/claims sem remapeamento conforme D5; rejeitar assinatura, algoritmo, emissor/destinatário, expiração, claims duplicadas/ausentes e identificadores inválidos. Testar HTTP com Controller protegido somente no assembly de testes, comprovando que rejeições não executam a ação.
- [x] 5.2 Consultar sessão/usuário por jti/sub sem cache após validação criptográfica e conferir prazos/claims correspondentes. Testar sessão ausente/revogada/expirada/de outro usuário, identidade confiável, segunda chamada após revogação de fixture e preservação de sessão existente durante bloqueio de login.
- [x] 5.3 Integrar falha operacional tipada ao pipeline para 503, preservando 401 para token inválido verificável antes do banco. Testar banco/schema indisponível, timeout e cancelamento sem dados protegidos ou challenge enganoso; documentar a distinção e o limite da prova de revogação por fixture.
- [x] 5.4 Capturar logs e erros de login/Bearer/configuração com sentinelas em senha, e-mail, JWT, Authorization, hash, conexão e exceções. Verificar ausência de vazamento, Problem Details/traceId e ausência da rota de teste/cadastro/logout/reset no OpenAPI de produção; revisar comentários e documentação de autenticação.

## 6. Validação integrada e aceite

- [x] 6.1 Executar restore bloqueado, build e suíte MSTest/MTP pertinente, separando testes sem banco dos que usam PostgreSQL descartável protegido pela fixture. Registrar comandos/resultados reais e limpeza em AMBIENTE; verificar regressões health/OpenAPI/Swagger/CLI/Pluggy, persistência financeira, arquitetura e modelo sem acessar provedor/banco pessoal/Neon.
- [x] 6.2 Validar OpenSpec estrito, links, diff e inspeção de segredos; revisar cada cenário e atualizar BACKLOG com evidências reais. Apresentar escopo para aceite humano mantendo FNR-010/011/020 e publicação pendentes; registrar limitações sem presumir aprovação por testes ou ausência de checks remotos.

## Workflow follow-up

**Autorização posterior do titular:** commits locais pelo subagente committer, push de `feature/fnr-009-login-jwt-sessoes` e abertura do PR FNR-009 contra `main` foram explicitamente autorizados no harness após a apresentação do resultado local. Escopo exclusivo FNR-009, preservando alterações não relacionadas. Aceite humano e revisão continuam pendentes; não registrar aceite em nome do titular. FNR-010/011/020 não estão entregues, e publicação autenticada permanece bloqueada até FNR-010. Não fazer merge, publicação, sync ou arquivamento. Os registros de ausência de autorização/encaminhamento na etapa anterior permanecem históricos, superados somente por esta autorização limitada.

- Quando autorizado, delegar commits locais ao committer com branch, caminhos, escopo e evidências; conferir hashes/conteúdo/estado restante. Esta proposta não autoriza commit/push/PR/merge.
- Abrir/revisar/integrar via PR sob autorizações aplicáveis e checks realmente exigidos; distinguir validação local de resultado GitHub.
- Após aceite, sincronizar a delta spec e arquivar proporcionalmente ao fluxo, preferencialmente no PR da entrega. Sync/arquivo não comprovam integração/publicação.
- Não publicar autenticação antes de revogação FNR-010 e demais gates aplicáveis; migrations no destino continuam explícitas e anteriores ao deploy dependente.

## Resultado local apresentado para aceite — 18/11/2026

19/19 tarefas executadas localmente, sem declarar aceite humano da implementação. Evidências detalhadas e matriz dos 8 requisitos/23 cenários estão em `docs/AMBIENTE.md`; estado e revisão documental pendente em `docs/BACKLOG.md` (0.19), ambos vinculados nas referências acima. Restore bloqueado/build sem avisos/erros; 225/225 testes sem PostgreSQL e 64/64 no PostgreSQL descartável, incluindo expiração durante espera SQL. A revisão reabriu e revalidou 5.2/6.1 para esse caso, sem alterar o desenho aprovado. OpenSpec estrito, diff/whitespace e 67/67 destinos de links passaram; inspeção limitada de segredos descrita no guia, com gitleaks indisponível. Serviço descartável encerrado e variáveis restauradas. Sem banco pessoal/Neon/provedor real, novos commits, push, PR, integração, deploy, sync ou arquivo. FNR-010/011/020, revisão/aceite e publicação permanecem pendentes; o follow-up acima exige as autorizações aplicáveis.

## Encerramento após integração — 18/11/2026

O titular aprovou explicitamente a implementação FNR-009 e o BACKLOG 0.20 no harness após revisão somente leitura do PR; aceite fora do GitHub não é review formal da plataforma. [PR #20](https://github.com/VictorJer/GestFinan-a/pull/20) integrado à `main` pelo merge `ad3a1cb74f3537163f5567ab085a3f173a5a0f41` (head `cec3b722952715e6fbb5dbd41fd0b1f2eec2fdf9`), com `reviews: []` e `statusCheckRollup: []` na consulta; ausência não é aprovação de checks. Por autorização expressa posterior, a delta foi consolidada em [authenticated-sessions](../../../specs/authenticated-sessions/spec.md), preservando 8 requisitos/23 cenários, e esta mudança foi arquivada com 19/19 tarefas. O registro documental de encerramento será apresentado em PR separado, sem merge automático. Testes locais anteriores 225/225 sem PostgreSQL e 64/64 com PostgreSQL não foram repetidos para sync/arquivo. FNR-010/011/020 permanecem não entregues; publicação autenticada bloqueada até FNR-010 e demais gates aplicáveis. Nenhum deploy, migration em destino, coleta, sync de outras mudanças ou implementação de nova FNR ocorreu neste encerramento.
