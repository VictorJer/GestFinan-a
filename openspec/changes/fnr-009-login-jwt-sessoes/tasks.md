# Tasks

Escopo exclusivo: **FNR-009**. Referências: [proposta](proposal.md), [spec](specs/authenticated-sessions/spec.md), [SDD D1–D7](design.md), [BACKLOG](../../../docs/BACKLOG.md), [PRD RF14](../../../docs/PRD.md) e [TDD §5](../../../docs/TDD.md).

**Gate:** planejamento pendente de revisão integral. Política cinco falhas/cinco minutos já confirmada pelo titular; demais decisões novas precisam do aceite dos artefatos. Não iniciar implementação sem nova solicitação de apply e aprovação necessária. Reconciliar previamente o encerramento FNR-008 em seu próprio fluxo autorizado. As caixas representam execução futura, não existência de documentos.

## 1. Alinhamento e composição

- [ ] 1.1 Conferir evidência de encerramento FNR-008, registrar FNR-009/branch no BACKLOG e consolidar somente decisões novas D1–D7 no TDD; preservar IDs/histórico e obter aprovação das revisões pertinentes antes de código dependente. Verificar links e registro explícito de aprovação, sem reabrir política de tentativas já escolhida.
- [ ] 1.2 Conferir metadados, licença, advisories e compatibilidade do JwtBearer 10.0.15 candidato e da versão IdentityModel compatível com o grafo; fixar referências/locks reais e registrar versões em AMBIENTE. Verificar restore bloqueado e build; alteração de baseline exige decisão antes de instalar.
- [ ] 1.3 Criar contratos/resultados e handler MediatR de login na Application, sem EF/Identity/IdentityModel, redigindo representações textuais sensíveis. Verificar testes de delegação/cancelamento/ToString e ArchitectureTests; comentar blocos lógicos em português.
- [ ] 1.4 Extrair composição compartilhada de contexto/Identity e protocolo do lock administrativo, preservando Version2/128, política de senha e serviços exclusivos dos comandos. Verificar testes DI, modelo/factory e comandos administrativos sem JWT; documentar separação HTTP/CLI no guia.

## 2. Persistência das sessões

- [ ] 2.1 Adicionar entidade/mapeamento AuthSessions conforme D4, gerador de identificador criptográfico, FK Restrict e constraints/índice. Atualizar teste que exige ausência de AuthSession. Verificar modelo, unicidade, FK, datas inválidas, round-trip UTC e ausência de campos sensíveis no PostgreSQL descartável.
- [ ] 2.2 Gerar migration incremental sem editar migrations antigas; testar base vazia, upgrade FNR-007 com usuário/grafo sintéticos, rollback da nova tabela e reaplicação. Documentar comandos explícitos em AMBIENTE e verificar ausência de migration/seed no startup.

## 3. Emissão e configuração JWT

- [ ] 3.1 Implementar configuração validada e adaptador de emissão D5 com HS256, segredo externo, claims mínimas e expiração de 1800 segundos. Testar chave ausente/malformada/curta, issuer/audience, aleatoriedade de jti, campos/precisão temporal e ausência de segredo em exceções/logs.
- [ ] 3.2 Implementar estado de autenticação indisponível e composição HTTP D1 sem impedir health ou resolver conexão inválida. Testar host sem banco/JWT/Pluggy, configuração parcial e health com headers Bearer inválidos; documentar variáveis, liveness e comportamento 503 sem exemplos de segredos reais.
- [ ] 3.3 Documentar rotação coordenada e persistência por ambiente; verificar com chaves sintéticas que reinício com a mesma chave preserva validação e troca de chave rejeita tokens anteriores. Testar expiração exata e emissão futura com relógio controlado.

## 4. Login transacional e tentativas

- [ ] 4.1 Implementar leitura do titular, normalização de e-mail, preservação da senha e controle Identity sob lock compartilhado. Testar conta ausente/múltipla, LockoutEnabled legado, cinco falhas/cinco minutos, não extensão durante bloqueio e reset do contador em sucesso, com estado persistido entre contextos.
- [ ] 4.2 Integrar preparação do token, gravação da sessão e reset do contador numa transação; entregar resultado somente após commit. Testar duas sessões distintas, cinco falhas concorrentes sem perda, erro ao gravar contador, erro de assinatura/persistência, rollback, timeout/cancelamento e confirmação incerta do commit no banco descartável.
- [ ] 4.3 Verificar concorrência login/reset sob o protocolo compartilhado, com barreiras determinísticas: login posterior ao reset revalida credencial nova, sem validar senha obsoleta carregada antes do lock. Documentar que sessões anteriores ainda não são revogadas e que a garantia completa pertence à FNR-010.
- [ ] 4.4 Expor Controller login/DTO, limites 256/1024 caracteres e 16 KiB, respostas 200/400/401/413/503/500 sanitizadas e no-store conforme D2. Testar HTTP com Identity real, corpo inválido, e-mail normalizado, senha exata, conta desconhecida/bloqueada com resposta genérica e ausência de cookie/refresh. Atualizar OpenAPI e guia do contrato sem token de exemplo real.

## 5. Validação de cada chamada protegida

- [ ] 5.1 Configurar JwtBearer/ordem de middleware/claims sem remapeamento conforme D5; rejeitar assinatura, algoritmo, emissor/destinatário, expiração, claims duplicadas/ausentes e identificadores inválidos. Testar HTTP com Controller protegido somente no assembly de testes, comprovando que rejeições não executam a ação.
- [ ] 5.2 Consultar sessão/usuário por jti/sub sem cache após validação criptográfica e conferir prazos/claims correspondentes. Testar sessão ausente/revogada/expirada/de outro usuário, identidade confiável, segunda chamada após revogação de fixture e preservação de sessão existente durante bloqueio de login.
- [ ] 5.3 Integrar falha operacional tipada ao pipeline para 503, preservando 401 para token inválido verificável antes do banco. Testar banco/schema indisponível, timeout e cancelamento sem dados protegidos ou challenge enganoso; documentar a distinção e o limite da prova de revogação por fixture.
- [ ] 5.4 Capturar logs e erros de login/Bearer/configuração com sentinelas em senha, e-mail, JWT, Authorization, hash, conexão e exceções. Verificar ausência de vazamento, Problem Details/traceId e ausência da rota de teste/cadastro/logout/reset no OpenAPI de produção; revisar comentários e documentação de autenticação.

## 6. Validação integrada e aceite

- [ ] 6.1 Executar restore bloqueado, build e suíte MSTest/MTP pertinente, separando testes sem banco dos que usam PostgreSQL descartável protegido pela fixture. Registrar comandos/resultados reais e limpeza em AMBIENTE; verificar regressões health/OpenAPI/Swagger/CLI/Pluggy, persistência financeira, arquitetura e modelo sem acessar provedor/banco pessoal/Neon.
- [ ] 6.2 Validar OpenSpec estrito, links, diff e inspeção de segredos; revisar cada cenário e atualizar BACKLOG com evidências reais. Apresentar escopo para aceite humano mantendo FNR-010/011/020 e publicação pendentes; registrar limitações sem presumir aprovação por testes ou ausência de checks remotos.

## Workflow follow-up

- Quando autorizado, delegar commits locais ao committer com branch, caminhos, escopo e evidências; conferir hashes/conteúdo/estado restante. Esta proposta não autoriza commit/push/PR/merge.
- Abrir/revisar/integrar via PR sob autorizações aplicáveis e checks realmente exigidos; distinguir validação local de resultado GitHub.
- Após aceite, sincronizar a delta spec e arquivar proporcionalmente ao fluxo, preferencialmente no PR da entrega. Sync/arquivo não comprovam integração/publicação.
- Não publicar autenticação antes de revogação FNR-010 e demais gates aplicáveis; migrations no destino continuam explícitas e anteriores ao deploy dependente.
