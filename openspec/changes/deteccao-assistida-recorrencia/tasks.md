# Tasks

## 1. Gates e critérios aprovados

- [ ] 1.1 Confirmar que FNR-012, FNR-015, FNR-020 e FNR-022 foram aceitas e estão disponíveis, e obter aprovação explícita da política de valor, datas, descrição e situações que contam como observação; verificar registros de aceite e manter implementação bloqueada enquanto algum gate estiver pendente.
- [ ] 1.2 Consolidar a política aprovada em critérios reproduzíveis, incluindo moeda/conta, sinal, fim de mês, duplicatas, projeções, versão da política e exemplos sintéticos de falsos positivos/negativos; verificar que cada limite possui casos dentro e fora da tolerância antes de codificar.

## 2. Correspondência no domínio

- [ ] 2.1 Implementar avaliação determinística no backend para três meses consecutivos, uma ocorrência distinta por mês, mesma conta/moeda e todos os predicados aprovados; verificar testes unitários para padrão positivo, valor igual isolado, mês ausente, moeda/conta diferente e tolerâncias dentro/fora dos limites.
- [ ] 2.2 Separar movimentos observados de duplicatas e parcelas explicitamente futuras, preservando natureza e status originais; verificar testes sintéticos de identidade repetida, projeção, status pendente/desconhecido e limites de mês/ano, sem usar consulta real.
- [ ] 2.3 Representar metadados Pluggy de parcelamento independentemente do candidato e da decisão; verificar testes para campos presentes, ausentes e incoerentes, confirmando que metadados sozinhos não geram candidato.

## 3. Persistência e sincronização

- [ ] 3.1 Modelar candidato, ocorrências vinculadas, versão da política e decisão do titular sem alterar `FinancialTransaction`; verificar migration em PostgreSQL descartável, propriedade/índices e execução administrativa fora da inicialização da API.
- [ ] 3.2 Persistir e consultar candidatos/decisões com vínculo estável às transações importadas; verificar testes de integração para importação repetida, atualização de dados externos e preservação de rejeições/confirmações/correções.
- [ ] 3.3 Garantir que mudança de política ou reavaliação de histórico não sobrescreva silenciosamente decisões existentes; verificar teste de regressão com decisões associadas a uma versão anterior da política.

## 4. API protegida e revisão pelo titular

- [ ] 4.1 Expor operações protegidas para consultar candidatos e registrar/corrigir decisão como assinatura, parcela, compras independentes ou rejeição, derivando titular da sessão; verificar testes HTTP de sessão ausente, propriedade cruzada, validação e resposta sanitizada.
- [ ] 4.2 Integrar a revisão à lista existente de FNR-022, distinguindo sugestão, metadados Pluggy e decisão do titular; verificar testes de UI para confirmar, rejeitar, corrigir, carregar, erro e navegação acessível sem criar uma segunda lista.
- [ ] 4.3 Preservar a transação original durante qualquer decisão e sincronização, sem somas ou novos indicadores; verificar comparação antes/depois de valor, data, descrição, natureza, status e competência, além de testes de API/UI sem agregados monetários.

## 5. Validação integrada

- [ ] 5.1 Executar testes unitários, integração com PostgreSQL descartável, HTTP e UI do fluxo completo com fixtures sintéticas; verificar três meses consecutivos, falsos positivos, ausência de mês, parcelas previstas, isolamento por titular e preservação após sincronização.
- [ ] 5.2 Atualizar documentação e rastreabilidade de FNR-038/RF17/VP-07/DT-55 com os limites aprovados e evidências dos testes; verificar links, consistência com a capability e que nenhuma aprovação ou coleta real foi alegada sem evidência.
- [ ] 5.3 Executar validação OpenSpec e verificações de diff/build/testes dos componentes afetados; verificar critérios de aceite e registrar bloqueios sem marcar a mudança como pronta enquanto dependências, aprovações ou checks aplicáveis estiverem pendentes.
