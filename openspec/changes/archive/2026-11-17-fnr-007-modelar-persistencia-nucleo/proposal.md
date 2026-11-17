# Proposal

## Why

A FNR-006 preparou PostgreSQL e uma migration técnica sem entidades; o Finora ainda não consegue preservar movimentos e anotações com integridade relacional. A FNR-007 estabelece essa base antes da importação, sem depender da validação financeira de compra/competência adiada na FNR-005.

## What Changes

- Modelar conexão, cartão, movimento, categoria, anotação e registro básico de execução de importação, com identidade externa opaca, propriedade, unicidade, FKs e índices.
- Preservar valor decimal individual, moeda, dados e datas originais, normalização técnica explicitada, natureza/situação independentes e motivos conservadores; não exigir competência financeira confirmada.
- Incluir `ApplicationUser` e o armazenamento básico do Identity para permitir FKs reais ao titular. Essa fronteira foi escolhida explicitamente pelo autor no harness nesta proposta; não inclui comandos administrativos, provisionamento, senha, autenticação HTTP, login ou sessões.
- Separar dados da origem de categoria/observação pessoal, com no máximo uma anotação por movimento e impedimento de vínculos entre titulares.
- Criar migrations incrementais compatíveis com a migration técnica FNR-006 e testes de integração em PostgreSQL descartável para base vazia, atualização, precisão, unicidade, propriedade e rollback transacional.
- Manter a API e seu health independentes do banco na inicialização, sem migrations automáticas, endpoints financeiros, importação real ou agregados monetários.

## Capabilities

### New Capabilities

- `financial-data-persistence`: preservar o grafo financeiro do titular e suas anotações com identidade, semântica de origem e integridade relacional, inclusive nos limites transacionais e de evolução do schema.

### Modified Capabilities

Nenhuma. `transaction-normalization` permanece uma capacidade pura sem persistência; suas regras são consumidas, não substituídas. Diagnóstico/leitura Pluggy, explorador e recorrência mantêm seus próprios escopos.

## Impact

- Rastreabilidade: FNR-007 depende de FNR-005 revisada e FNR-006; PRD RF02/RF03/RF06/RF15 e RNF-02/04/05, com suporte estrutural a RF08/RF14/RF16; TDD §§3/5/9/10/14/15/19, DT-07/14/15/23/35/38/40/54. PRD 0.4/TDD 0.8 e aprovações anteriores permanecem preservados.
- Código futuro: entidades e invariantes puras no Domain; mapeamentos, `ApplicationUser`, contexto e migrations na Infrastructure; testes no projeto MSTest/MTP existente. Application/Domain não passam a depender de Identity ou EF.
- Dependências futuras: pacote de armazenamento Identity da linha .NET 10, proposto no patch 10.0.15 para acompanhar EF Core; conferir disponibilidade, licença, avisos e grafo real antes da instalação, sem atualização silenciosa da baseline.
- Documentação: a concretização do TDD §10 e os registros de dependência em AMBIENTE/backlog foram preparados nesta execução e aprovados pelo titular; evidências de implementação e validação serão registradas nas tarefas correspondentes. O PRD permanece inalterado.
- Estado da dependência: a regularização documental PR #15 foi integrada e a mudança OpenSpec FNR-006 foi arquivada após 11/11 tarefas. O commit técnico `c03f0c7` continua sem PR associado; o titular aceitou registrar essa lacuna como ressalva histórica, sem declará-la conforme ao fluxo de PR. A dependência fica resolvida para prosseguir à revisão canônica do TDD, não para alegar conformidade retroativa.
- Fora de escopo: FNR-008–011 (exceto armazenamento Identity delimitado), importador/upsert/reconciliação FNR-012, executor/posse/outbox/RabbitMQ FNR-013, provisionamento de categorias FNR-016, API de anotações FNR-017, frontend, recorrência FNR-038, investimentos, Neon, publicação e novas coletas Pluggy.
- Estado: proposta/spec/SDD aprovados explicitamente pelo titular em 17/11/2026, conforme registro no backlog. O TDD 0.9 também foi aprovado em 17/11/2026; a solicitação explícita de apply autoriza a implementação delimitada nesta mudança, mas não commits, PR, merge ou publicação.
