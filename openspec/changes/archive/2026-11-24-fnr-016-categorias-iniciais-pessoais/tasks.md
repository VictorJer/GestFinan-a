# Tasks

## 1. Aprovação e alinhamento documental

- [x] 1.1 Apresentar proposta, delta `category-catalog`, design e tarefas da FNR-016 e obter aprovação explícita antes de código; verificar registro do aceite e preservar 0/ tarefas executadas até haver implementação real.
- [x] 1.2 Reconciliar o estado factual da FNR-015 em `docs/BACKLOG.md` e `docs/TDD.md` com o merge `a858677` observado na `main` local, sem afirmar review/check GitHub, publicação ou regressão PostgreSQL ampla verde; conferir diff, links e histórico de versões/aprovações.
- [x] 1.3 Alinhar TDD §15/DT-44 e o registro de escopo FNR-016 no backlog com nomes equivalentes por acento/caixa/espaços, sete categorias persistidas, opção virtual e migration explícita; apresentar os diffs canônicos e obter aprovação própria antes de qualquer código dependente, conferindo rastreabilidade RF06/§8.

## 2. Política de nomes e dados iniciais

- [x] 2.1 Definir política compartilhada de nome apresentado e chave normalizada no backend, rejeitando controles, vazio e comprimento maior que 60 sem truncamento; verificar testes unitários de acentos, caixa, espaços, Unicode composto/decomposto e expansão da chave.
- [x] 2.2 Fixar os sete UUIDs de sistema e chaves reservadas (incluindo “Sem categoria” virtual) com teste de invariantes que impeça criação pessoal de nome equivalente, sem persistir um oitavo registro.
- [x] 2.3 Criar migration de dados explícita e idempotente, transacional, para as sete categorias de sistema com verificação de conflito de ID/escopo/nome, sem seed no startup nem alteração de migrations antigas; provar em PostgreSQL descartável instalação limpa, upgrade, reaplicação e rollback por conflito com preservação de categorias pessoais/anotações.
- [x] 2.4 Documentar como executar/verificar o provisionamento administrativo e os IDs estáveis sem aplicação automática na API; conferir que o roteiro permite detectar catálogo incompatível antes de liberar as rotas e não publica dados de teste como reais.

## 3. Criação protegida de categoria pessoal

- [x] 3.1 Implementar caso de uso de criação com titular derivado da sessão, contrato de nome-only e rejeição de campos adicionais; verificar testes de Application/HTTP para corpo ausente/inválido, nome reservado, nome normalizado e ausência de escolha de titular/ID/tipo pelo cliente.
- [x] 3.2 Persistir categoria pessoal com escopo do titular e índice único já existente, fazendo precheck de duplicidade e mapeando somente a constraint pertinente para 409; verificar testes PostgreSQL/Application com IDs sintéticos de titulares diferentes (sem segundo login HTTP), barreira de criação concorrente, conflito no mesmo escopo e erro de banco não confundido com conflito.
- [x] 3.3 Expor `POST /api/categories` com 201 e DTO allowlist pessoal, 400/401/409/429/500/503 sanitizados; verificar HTTP de sessão revogada, campos permitidos, criação única e inexistência de efeito em movimentos/`SyncRun`, e documentar contrato em README/OpenAPI.

## 4. Catálogo de leitura

- [x] 4.1 Implementar leitura somente das sete categorias de sistema e das categorias pessoais do titular, detectando catálogo não provisionado/incompatível sem leitura de conexão financeira; verificar PostgreSQL/Application por dois IDs sintéticos sem segundo login HTTP, nenhum titular com conexão e catálogo ausente produzindo indisponibilidade sanitizada.
- [x] 4.2 Expor `GET /api/categories` com ordem fixa de sistema, pessoais por chave/ID e “Sem categoria” virtual ao final com `id: null`/`kind=unassigned`; verificar HTTP com sessão única válida/revogada para 200/401/429/503, ausência de UUID vazio, sem oitavo registro e sem chamadas de importação/provedor.
- [x] 4.3 Documentar em README/OpenAPI a forma `{ id, name, kind }`, a opção virtual, a ordenação e a fronteira com FNR-017/022; conferir exemplos sintéticos e que contrato atual de `GET /api/transactions` e representação nula de anotação permanecem intactos.

## 5. Validação integrada e apresentação

- [x] 5.1 Executar restore bloqueado, build e suítes afetadas sem PostgreSQL e com PostgreSQL descartável; conferir contratos de login/health/cartões/movimentos, migração explícita, propriedade, 429 e ausência de somas, registrando resultados reais e separando as duas falhas históricas da suíte ampla se persistirem. Evidências: restore bloqueado e build aprovados; 327 testes sem PostgreSQL e 15 testes PostgreSQL focados aprovados; suíte PostgreSQL ampla 192/195, com duas fixtures antigas que aplicam apenas a migration core antes de gravar o modelo atual de `SyncRun` e o teste RabbitMQ bloqueado pela ausência da configuração MQ-P01.
- [x] 5.2 Validar OpenSpec estrito, diff/whitespace, destinos de links e inspeção limitada de segredos; revisar rastreabilidade FNR-016 ↔ RF06 ↔ TDD §15/DT-44 ↔ delta e apresentar evidências/limites para revisão e aceite, sem declarar Pluggy/Neon, checks GitHub ou publicação não verificados. Evidências: OpenSpec estrito 1/1; `git diff --check` sem problemas nos hunks; 17 arquivos novos sem espaços finais; 105 destinos locais verificados; padrões limitados de segredo sem correspondências em 27 arquivos alterados; rastreabilidade conferida em PRD, backlog, TDD e delta.

## Workflow follow-up

- Somente após aprovação da implementação e validações aplicáveis, encaminhar commits locais pelo committer e PR na feature branch com autorização específica; merge exige os gates reais do repositório, não é consequência da existência deste plano.
- Sincronizar a delta e arquivar após aceite e validação do escopo, preservando a separação da FNR-017/022 e o processo de migration em destino antes da integração que dispare deploy dependente.
