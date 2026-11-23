# Tasks

## 1. Aprovação e rastreabilidade

- [ ] 1.1 Apresentar proposta, delta `card-movement-query`, desenho e tarefas FNR-015 ao titular e registrar aprovação explícita antes de implementar; verificar que a aprovação não é inferida de `openspec status` nem do aceite FNR-014.
- [ ] 1.2 Reconciliar em `docs/BACKLOG.md` o registro factual FNR-014/0.50 com o commit local já integrado e iniciar a linha FNR-015 com dependências 011/012 e limites do explorador; conferir diff/links e deixar qualquer revisão substantiva pendente de aceite próprio, sem reescrever aprovações históricas.
- [ ] 1.3 Alinhar o contrato ainda proposto em `docs/TDD.md` §11/DT-41 aos DTOs, filtros, cobertura e respostas desta mudança e obter aprovação explícita do diff canônico e do registro de backlog afetado antes de código dependente; conferir rastreabilidade RF04/RF05 e preservar aprovações anteriores.

## 2. Cartões e fronteira de titularidade

- [ ] 2.1 Criar query/reader para conexão própria única e lista de cartões por `UserId`, distinguindo conexão ausente e ambígua; testar com PostgreSQL descartável que conexões e cartões alheios nunca entram no resultado.
- [ ] 2.2 Expor `GET /api/cards` autenticado com DTO de ID interno, nome/final opcionais e moeda, sem ID externo/segredo; testar HTTP de lista vazia, 401/404/409 e 429 aplicável, mantendo Problem Details/health existentes.
- [ ] 2.3 Documentar resposta e erros da rota de cartões no contrato associado ao TDD/README, sem descrever campo opcional ausente como valor inventado; verificar exemplos somente sintéticos e coerência com testes HTTP.

## 3. Consulta de movimentos e filtros

- [ ] 3.1 Definir query e validação de `cardId`, `categoryId`, situação, `from`/`toExclusive`, busca literal, `page`/`pageSize` (1–100, padrão 25), com 400 sanitizado para valores inválidos; testar limites unilaterais, datas/números inválidos, busca longa e filtro de cartão alheio/inexistente com 404 indistinguível.
- [ ] 3.2 Implementar projeção EF de movimentos do próprio titular e da conexão própria com categoria/nota de anotação própria, sem importar ou reclassificar; testar naturezas desconhecidas, `PENDING`, metadados de parcela nulos, datas técnicas ausentes e isolamento de anotação/categoria alheia em PostgreSQL descartável.
- [ ] 3.3 Combinar filtros e busca de substring case-insensitive literal (incluindo `%`, `_` e escape), ordenando dia técnico decrescente com nulos por último e desempate por ID; testar interseção dos filtros, datas-limite, empates, nulos e ausência de SQL/ordenação arbitrários.
- [ ] 3.4 Contar o filtro inteiro antes da paginação e projetar apenas os itens da página em leitura coerente; testar duas páginas, página além do fim, `filteredCount` igual em ausência de gravação concorrente e comportamento seguro durante promoção concorrente.
- [ ] 3.5 Expor `GET /api/transactions` protegido com DTOs allowlist, incluindo valor/moeda individual e motivos internos, nunca agregados; testar HTTP 200/400/401/404/409, 429 aplicável, 503 tipado/500 sanitizado, serialização sem ID externo/enum bruto e sem total monetário em moedas/sinais mistos.
- [ ] 3.6 Documentar em README/OpenAPI os parâmetros, ordenação, intervalos da data técnica, casos nulos, respostas paginadas e erros conforme contrato TDD previamente aprovado; verificar exemplos sintéticos e que o total monetário histórico permanece adiado.

## 4. Cobertura honesta e leitura sem efeitos

- [ ] 4.1 Projetar último sucesso confirmado e janela da `SyncRun` correspondente separadamente da execução mais recente, com código de falha sanitizado; testar sem sucesso, sucesso legado sem run correlata, janela completa, falha subsequente e estado pendente, sem alegar histórico bancário integral.
- [ ] 4.2 Anexar contexto de cobertura à resposta de transações e distinguir `filteredCount=0` de cobertura desconhecida/incompleta; testar HTTP conjunto vazio, falha de página intermediária, atualização posterior do histórico e ausência de zero financeiro fictício.
- [ ] 4.3 Provar em testes HTTP/PostgreSQL que cartões, filtros e paginação não criam `SyncRun`, não chamam Pluggy e permanecem disponíveis com broker indisponível; documentar a distinção entre leitura, sincronização explícita e limitação de cobertura.
- [ ] 4.4 Medir consulta/contagem e inspecionar o plano para conjunto sintético de até 5.000 movimentos no PostgreSQL descartável; registrar custo e decidir justificadamente se os índices existentes bastam. Se precisar de novo índice/migration, revisar desenho/artefatos e obter aceite do delta antes de executar código dependente; não aplicar migration ao iniciar a API.

## 5. Validação integrada da entrega

- [ ] 5.1 Executar restore bloqueado, build e testes afetados com/sem PostgreSQL descartável, OpenSpec estrito, diff check, links e inspeção limitada de dados sensíveis; registrar resultados reais em `docs/AMBIENTE.md` e nas tarefas sem alegar Pluggy real, Neon, checks GitHub, publicação ou aceite humano.
- [ ] 5.2 Revisar a matriz FNR-015 ↔ RF04/RF05/M-03 ↔ TDD §11 ↔ delta, distinguindo entregas FNR-016/017/018/022/023/038 e tarefas sobrepostas do explorador; apresentar diff e evidências para revisão/aceite, sem marcar esta mudança ou a mudança do explorador como aceita só porque os artefatos existem.

## Workflow follow-up

- Após aceite da implementação e verificações aplicáveis, encaminhar commits locais com `committer` somente mediante autorização explícita, abrir PR da feature branch e integrar somente pelo PR revisado; não há push, PR, merge ou publicação autorizados pela geração deste plano.
- Após integração, validação e aceite, sincronizar a delta com a spec principal e arquivar a mudança conforme o fluxo do projeto, preservando a separação da capability do explorador e seus próprios critérios de aceite.
