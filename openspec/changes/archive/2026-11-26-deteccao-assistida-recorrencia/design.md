# Design

## Context

Ver `proposal.md` para motivação e `specs/transaction-recurrence-assistance/spec.md` para o contrato observável. A rastreabilidade aprovada é FNR-038 / PRD RF17 e VP-07 / TDD DT-35, DT-40, DT-41 e DT-55. O requisito de três meses observados, a confirmação corrigível pelo titular e a separação dos metadados de parcelas são herdados; não reabrir a FNR-005 nem duplicar a mudança existente do explorador.

Estado-base observado ao preparar a mudança: `FinancialTransaction` tinha identidade contextual (`UserId`, `CardAccountId`, `ExternalId`), `TransactionDate` anulável, `Situation` e metadados de parcela; o importador atualizava fatos da mesma identidade. `GET /api/transactions` publicava movimentos persistidos do titular com cobertura e a lista Angular tinha filtros, paginação e edição de anotações com isolamento por geração de sessão. FNR-012/015/020/022 estavam presentes; o merge da FNR-023 foi observado no Git local. Naquele estado não havia candidato/decisão de recorrência. O estado local posterior e seus limites estão nas tarefas e em [AMBIENTE.md](../../../../docs/AMBIENTE.md#fnr-038--preservacao-e-validacao-integrada-local); isso não comprova integração hospedada ou provedor real.

## Goals / Non-Goals

**Goals:**

- Avaliar padrões como candidatos explicáveis a partir de movimentos distintos observados por mês, no mesmo cartão/conta e moeda, aplicando em conjunto as tolerâncias aprovadas.
- Persistir a decisão do titular separada da transação e dos metadados da origem, com autorização por titular e preservação em sincronizações.
- Definir critérios reproduzíveis para falsos positivos/falsos negativos usando somente fixtures sintéticas.

**Non-Goals:**

- Reconhecer compras automaticamente, alterar natureza/situação, declarar competência, somar valores ou validar resultado financeiro.
- Tratar a sequência heurística como prova de assinatura ou substituir os metadados de parcelamento da Pluggy.
- Criar outra lista de movimentos, modificar a capability do explorador, alterar o classificador conservador da FNR-005 ou consultar a Pluggy de verdade.
- Criar um segundo contrato de lista ou decidir uma biblioteca externa sem necessidade demonstrada; os novos contratos de candidato/decisão e seu esquema exigem revisão antes da implementação.

## Decisions

### D1 — Fronteira e fluxo da regra

Manter a comparação determinística no domínio, sem efeitos de I/O, e deixar a camada Application coordenar a leitura do histórico persistido, a criação/consulta de candidatos e as decisões. A Infrastructure continua responsável pela persistência e pelo mapeamento da origem. Integrar com `FinancialTransaction` e com a leitura protegida já existentes; não reutilizar `TransactionNormalizer` para inferir recorrência: natureza, situação, elegibilidade e recorrência são dimensões diferentes. A API deverá derivar o titular da sessão e adotar a sanitização e o isolamento já usados em `TransactionsController`. A interface deve oferecer revisão, confirmação, rejeição e correção na lista existente, sem criar uma segunda lista, descartando respostas de gerações de sessão anteriores.

Alternativa considerada: implementar regra no Controller ou na UI. Rejeitada porque duplicaria a lógica, permitiria bypass da autorização/consistência e diverge do TDD, que reserva as regras de negócio ao backend.

### D2 — Predicados combinados e período observado

Tratar uma correspondência como conjunção, não como pontuação: mesmo titular/cartão/conta e moeda, um movimento distinto em cada um de três meses consecutivos, valor dentro da política `recurrence-v1`, data compatível e descrição compatível. Valor isolado jamais produz candidato. Usar somente `TransactionDate`; uma data técnica nula não forma mês e `BillForecastDate`, mês de fatura ou vencimento nunca a substituem. Deduplicar pela identidade contextual (`CardAccountId`/`ExternalId`), sem fundir cartões diferentes. Somente `Situation=Effective` é elegível; `Pending`, `FutureInstallment`, `Unknown` e parcelas explicitamente futuras/projetadas não contam. Preservar `Status` da origem e `Situation` sem alteração.

Alternativa considerada: aceitar duas ocorrências, meses não consecutivos, moedas diferentes ou uma pontuação que compense sinais ausentes. Rejeitada para esta mudança porque enfraquece a direção aprovada e torna o candidato difícil de explicar; moedas não serão comparadas por conversão.

### D3 — Política de correspondência aprovada (`recurrence-v1`)

O titular aprovou explicitamente esta política e o plano no harness em 25/11/2026. A versão é identificada como `recurrence-v1`; qualquer mudança futura de critérios deve criar outra versão e não reclassificar nem sobrescrever decisões existentes. Os limites são inclusivos e não há conversão cambial ou arredondamento intermediário.

| Dimensão | Critério reproduzível v1 | Limite |
|---|---|---|
| Identidade | Mesmo titular, `CardAccountId` e código de moeda; exatamente uma ocorrência distinta por mês no trio avaliado. Identidade/deduplicação contextual por (`CardAccountId`, `ExternalId`); o mesmo `ExternalId` em cartões diferentes não é a mesma ocorrência. | Igualdade exata; sem conversão de moeda. |
| Valor e sinal | Os três valores devem ser não zero e ter o mesmo sinal. Seja `m` a mediana das magnitudes absolutas; cada ocorrência deve satisfazer `abs(abs(valor) - m) / m <= 0.01`. | Até 1% da mediana, inclusive. Valor zero não é elegível. |
| Mês e data | Os meses-calendário de `TransactionDate` devem ser consecutivos. Para cada data, usar o dia civil, exceto o último dia do mês, que é representado por 31; a mediana dos três dias representados é o dia nominal e cada um deve ficar a até 3 dias dele. | ±3 dias, inclusive; 31/28/31 é padrão de fim de mês. |
| Descrição | Recusar descrição nula/vazia. Normalizar Unicode NFC, caixa invariável, substituir caracteres de pontuação Unicode por espaço, reduzir sequências de espaços e aparar. Preservar números, acentos e símbolos; exigir igualdade exata do resultado. | Sem similaridade aproximada ou remoção de números/acentos. |
| Situação | Elegível somente quando `Situation=Effective`; excluir `Pending`, `FutureInstallment`, `Unknown` e parcelas explicitamente futuras/projetadas. | `TransactionDate` nula não forma ocorrência; não usar data de fatura/projeção como substituta. |

Casos sintéticos de fronteira usados para conferir a política antes do código:

- **Valor dentro, no limite:** magnitudes BRL 99,00 / 100,00 / 101,00; mediana 100,00 e desvio máximo 1%, portanto passa. **Fora:** 99,00 / 100,00 / 101,01; desvio de 1,01%, portanto falha. Mesma moeda e mesmo sinal são pré-condições nos dois casos.
- **Data dentro, inclusive fim de mês:** 31/jan, 28/fev e 31/mar representam os dias 31/31/31 e passam; 05/jan, 06/fev e 10/mar representam 5/6/10, mediana 6 e desvio máximo 4, portanto falha. Mês ausente ou `TransactionDate` nula também falha.
- **Descrição dentro/fora:** “Nuvem-Fictícia 8472”, “NUVEM FICTÍCIA 8472” e “Nuvem*Fictícia 8472” normalizam para a mesma descrição. “Nuvem Fictícia 8473” não corresponde porque os números são preservados; não se usa similaridade.
- **Outras condições negativas:** valores com sinais diferentes, moeda ou `CardAccountId` distintos, valor zero, situação `Pending`/`Unknown`, duas cópias do mesmo (`CardAccountId`, `ExternalId`) ou parcela futura não completam o trio elegível.
- **Falso positivo intencionalmente preservado:** três compras independentes fictícias em “Café Central Fictício”, no mesmo cartão, mês consecutivo, dias 4/6/7, valores 31,80/32,00/31,95 BRL e situação `Effective` podem satisfazer os sinais; o resultado ainda é apenas “possível recorrência” e o titular pode escolher compras independentes/rejeitar.
- **Falso negativo conhecido:** BRL 99,00 / 100,00 / 101,01 excede a tolerância; a variação “Vídeo Fictício 8472”/“Vídeo Fictício 8473” não corresponde. A política prefere perder esses casos a ampliar limites sem nova aprovação.

### D4 — Candidato, decisão e metadados como registros separados

Persistir o candidato e suas ocorrências/referência de política separados de `FinancialTransaction`; guardar a decisão explícita do titular como dado próprio ligado às ocorrências correspondentes. O importador já preserva o ID interno ao atualizar fatos na mesma identidade externa contextual (`CardAccountId`/`ExternalId`): vincular e testar decisões com esse caminho, inclusive reimportação e reconexão, sem presumir que texto, data ou valor sejam chaves estáveis. A decisão não deve ser recalculada a partir do texto atualizado nem descartada na sincronização. Guardar a versão da política usada em cada candidato; mudanças de política não reclassificam decisões anteriores automaticamente e não sobrescrevem confirmação, rejeição ou correção do titular.

Metadados de parcelamento recebidos da origem permanecem inalterados e apresentados separadamente. Um `PaymentType`/número de parcela não cria sozinho um candidato nem substitui três observações; parcelas previstas não contam como observações. A confirmação “parcela” é uma decisão do usuário, não uma edição desses metadados.

Alternativa considerada: gravar a decisão em categoria/observação ou sobrescrever natureza da transação. Rejeitada porque conflita com RF15/DT-35, seria vulnerável a sobrescrita na sincronização e mistura classificação pessoal com semântica financeira.

### D5 — Contratos protegidos e compatibilidade

As operações de consulta e decisão devem obter o titular da sessão autenticada, validar propriedade de transações/candidatos e nunca aceitar `UserId` livremente do cliente, conforme TDD §§5/11. Seguir as respostas sanitizadas e a autorização das rotas financeiras existentes; definir rotas/DTOs próprios de candidato e decisão sem alterar `GET /api/transactions` nem o contrato de anotação, salvo adição compatível explicitamente revisada. A lista Angular já vinculada à geração de sessão é o ponto de integração visual. Detalhes da nova persistência e do contrato devem ser fechados e aprovados antes de código, não inferidos dos DTOs de movimentos.

No gate da tarefa 4.1, o titular aprovou explicitamente no harness em 25/11/2026, antes do código, um fluxo de avaliação por `POST` separado da consulta. `GET /api/transactions/recurrence-candidates` é somente leitura e `POST /api/transactions/recurrence-candidates/evaluate` avalia os movimentos persistidos do titular autenticado apenas com políticas suportadas, sincroniza os candidatos idempotentemente e devolve a projeção atualizada. Ambos retornam os candidatos persistidos, atuais e inativos, com `candidateId`, `policyVersion`, os três IDs internos de movimento, `isCurrent`, decisão vigente e instante da decisão; essa lista mantém decisões históricas visíveis e permite associação com a lista existente sem duplicar descrição, valor ou metadados financeiros no DTO.

`PUT /api/transactions/recurrence-candidates/{candidateId}/decision` aceita somente o corpo `{"decision":"Subscription|Installment|IndependentPurchases|Rejected"}` e grava ou corrige a decisão explícita vigente, devolvendo a decisão e seu instante de atualização. O titular vem da sessão validada, não do corpo ou query string. Sessão ausente/revogada resulta em 401; candidato inexistente ou de outro titular, em 404 indistinguível; decisão/corpo inválido, em 400. Respostas e falhas seguem a sanitização existente, sem reutilizar `PATCH` de anotação, sem alterar movimentos e sem fornecer agregados monetários. A interface solicita a avaliação explícita para obter a projeção atualizada e não depende de efeitos colaterais em `GET`.

Alternativa considerada: sobrecarregar a anotação ou criar outra lista de movimentos. Rejeitada porque misturaria uma decisão corrigível com organização pessoal e duplicaria filtros, paginação e isolamento já existentes.

### D6 — Validação sem dados reais

Usar fixtures sintéticas cobrindo variação dentro/fora dos limites candidatos, mês ausente, data técnica nula, fronteira de ano, fevereiro/fim de mês, repetição de identificador externo no mesmo cartão e em cartões diferentes, moedas diferentes, descrição genérica, parcelas previstas, metadados ausentes, situações pendente/desconhecida, confirmação/correção, isolamento por titular e sincronização repetida. Testes de integração devem verificar preservação da decisão após atualização de fatos e reconexão, além da imutabilidade dos campos originais por decisões; UI deve rejeitar respostas tardias de outra geração de sessão. API/UI devem verificar ausência de agregados monetários. Não executar novas consultas reais nem usar históricos reais como fixtures.

Alternativa considerada: validar limites com uma nova coleta Pluggy. Rejeitada para este planejamento e desnecessária para criar dados sintéticos; coleta real requer autorização específica e não foi solicitada.

### D7 — Modelo persistente revisado para candidatos e decisões

O titular aprovou explicitamente este modelo no harness em 25/11/2026, antes da implementação da tarefa 3.1. `RecurrenceCandidate` guarda `Id`, `UserId`, `PolicyVersion`, os três IDs internos das ocorrências em ordem cronológica, `CreatedAtUtc`, `LastEvaluatedAtUtc` e `IsCurrent`. As três referências ficam na mesma linha, em vez de uma tabela filha: a política aprovada requer exatamente três ocorrências, e uma linha não pode representar candidato incompleto. Uma restrição exige três IDs distintos.

Cada referência usa FK composta (`OccurrenceTransactionId`, `UserId`) para (`FinancialTransaction.Id`, `FinancialTransaction.UserId`), com exclusão restrita. A identidade idempotente do candidato é (`UserId`, `PolicyVersion`, `Occurrence1TransactionId`, `Occurrence2TransactionId`, `Occurrence3TransactionId`), impondo unicidade sem usar descrição, valor ou identidade externa mutável. Índices atendem a consulta por titular/versão/estado/data e a revalidação por cada ocorrência. Conta e moeda não são copiadas para o candidato: são verificadas a partir dos movimentos pela regra do domínio.

`RecurrenceDecision` mantém uma única decisão vigente por (`CandidateId`, `UserId`), com escolha persistida como texto (`Subscription`, `Installment`, `IndependentPurchases` ou `Rejected`) e `UpdatedAtUtc`. A FK composta para o candidato impede associação cruzada de titular. Uma correção explícita substitui a escolha vigente; reavaliações do histórico só atualizam a vigência do candidato e nunca gravam na tabela de decisão. Candidatos não elegíveis ficam inativos, não são apagados, e podem ser reativados se o mesmo trio voltar a satisfazer a mesma versão.

`PolicyVersion` é armazenada em cada candidato. O avaliador só emite candidatos para versões realmente suportadas; uma versão indisponível não cria candidato. A persistência exige texto não vazio e preserva identificadores de versões históricas, sem tabela de configuração dinâmica nem versão aceita implicitamente por valor padrão. Uma versão nova cria identidade de candidato distinta e não migra nem altera decisões anteriores. A migration e os testes usam a cadeia EF existente e a fixture PostgreSQL descartável; sua aplicação operacional permanece administrativa, fora do startup da API.

O `Down` da migration só pode remover o esquema enquanto as duas tabelas estiverem vazias; com qualquer candidato ou decisão persistidos, ele falha sem apagar dados. A reversão funcional deve desabilitar novas sugestões, e a reversão estrutural exige plano explícito de preservação/recuperação.

## Risks / Trade-offs

- [Falsos positivos em compras habituais no mesmo estabelecimento] → Exigir simultaneamente conta/moeda, sequência de três meses, valor, data e descrição; mostrar “possível”, permitir rejeição/correção e medir casos com fixtures, sem prometer taxa de acerto.
- [Falsos negativos por reajuste, atraso de processamento ou variação textual] → Expor a recomendação e alternativas ao titular antes de aprovar; não ampliar limites por conveniência nem ocultar que a política conservadora perde alguns padrões.
- [Mudança do histórico ou reimportação apagar a decisão] → Separar registros da origem, candidato e decisão; testar importações idempotentes e preservar as confirmações por identidade estável.
- [Confusão entre recorrência, parcela, natureza e competência] → Manter dimensões separadas, rotular metadados como dados da origem, não usar datas de fatura como competência e não liberar agregados.
- [Artefatos antigos pressupõem ausência de FNR-012/015/020/022] → Usar os contratos entregues como pontos de integração, sem confundir presença no Git com aprovação da política de recorrência ou prova de ambiente hospedado.

## Migration Plan

O plano inicial não previa migration ou implantação antes dos gates de aprovação. Na implementação local foi criada a migration `20261126003137_Fnr038RecurrenceAssistance`, compatível com `FinoraDbContext`, e validada em PostgreSQL 18 descartável; ela não foi aplicada em banco de destino nem no startup da API. O `Down` recusa remover tabelas com candidatos/decisões. Aplicação futura permanece administrativa, precede qualquer deploy dependente e requer verificar compatibilidade do destino; reversão estrutural exige plano de preservação/recuperação.

## Aprovação e gates restantes

A política `recurrence-v1`, os critérios de borda sintéticos e este plano foram aprovados explicitamente pelo titular no harness em 25/11/2026, antes do código. O modelo/índices (3.1) e contratos protegidos (4.1) receberam as revisões próprias registradas nas tarefas antes da implementação. Em 26/11/2026, o titular aceitou explicitamente a implementação local e aprovou PRD 0.5/TDD 0.35; esse aceite não se transfere à FNR-020, cujo registro explícito não foi encontrado, nem comprova integração ou publicação. As evidências e limites locais constam em `tasks.md` e [AMBIENTE.md](../../../../docs/AMBIENTE.md#fnr-038--preservacao-e-validacao-integrada-local).
