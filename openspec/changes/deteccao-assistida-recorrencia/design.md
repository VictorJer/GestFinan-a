# Design

## Context

Ver `proposal.md` para motivação e `specs/transaction-recurrence-assistance/spec.md` para o contrato observável. A rastreabilidade aprovada é FNR-038 / PRD RF17 e VP-07 / TDD DT-35, DT-40, DT-41 e DT-55. O requisito de três meses observados, a confirmação corrigível pelo titular e a separação dos metadados de parcelas são herdados; não reabrir a FNR-005 nem duplicar a mudança existente do explorador.

O backend atualmente contém `TransactionFacts`, normalizadores/classificadores puros e campos opcionais da Pluggy para parcela (`InstallmentNumber`, `TotalInstallments`, `PaymentType`, datas de fatura). Esses componentes não persistem movimentos, não expõem APIs financeiras e não oferecem UI. Não existe ainda modelo de confirmação recorrente. A arquitetura de persistência, autenticação e frontend é pré-requisito do backlog, não evidência de implementação desta capability.

## Goals / Non-Goals

**Goals:**

- Avaliar padrões como candidatos explicáveis a partir de movimentos distintos observados por mês, no mesmo cartão/conta e moeda, aplicando em conjunto as tolerâncias aprovadas.
- Persistir a decisão do titular separada da transação e dos metadados da origem, com autorização por titular e preservação em sincronizações.
- Definir critérios reproduzíveis para falsos positivos/falsos negativos usando somente fixtures sintéticas.

**Non-Goals:**

- Reconhecer compras automaticamente, alterar natureza/situação, declarar competência, somar valores ou validar resultado financeiro.
- Tratar a sequência heurística como prova de assinatura ou substituir os metadados de parcelamento da Pluggy.
- Criar outra lista de movimentos, modificar a capability do explorador, alterar o classificador conservador da FNR-005 ou consultar a Pluggy de verdade.
- Escolher endpoint, biblioteca externa, tecnologia de armazenamento ou detalhes de schema antes das dependências de API/persistência já previstas no TDD.

## Decisions

### D1 — Fronteira e fluxo da regra

Manter a comparação determinística no domínio, sem efeitos de I/O, e deixar a camada Application coordenar a leitura do histórico persistido, a criação/consulta de candidatos e as decisões. A Infrastructure continua responsável pela persistência e pelo mapeamento da origem. Não reutilizar `TransactionNormalizer` para inferir recorrência: natureza, status, elegibilidade e recorrência são dimensões diferentes. A API deverá seguir a autenticação de sessão e propriedade definidas no TDD; a interface integrada à lista existente deve oferecer revisão, confirmação, rejeição e correção, sem criar uma segunda lista.

Alternativa considerada: implementar regra no Controller ou na UI. Rejeitada porque duplicaria a lógica, permitiria bypass da autorização/consistência e diverge do TDD, que reserva as regras de negócio ao backend.

### D2 — Predicados combinados e período observado

Tratar uma correspondência como conjunção, não como pontuação: mesma conta, mesma moeda (sem conversão), um movimento distinto observado em cada um de três meses consecutivos, valor dentro da tolerância, data compatível e descrição compatível. Valor isolado jamais produz candidato. Usar a data de transação/origem explicitamente identificada para agrupar meses; não usar `billForecastDate`, mês de fatura ou vencimento como competência. Deduplicar por identidade externa estável da transação e excluir parcelas explicitamente futuras/projetadas; manter o status recebido como dado independente e visível, sem reclassificá-lo. A política de quais status observados contam deve ser registrada junto à aprovação dos critérios antes da implementação.

Alternativa considerada: aceitar duas ocorrências, meses não consecutivos, moedas diferentes ou uma pontuação que compense sinais ausentes. Rejeitada para esta mudança porque enfraquece a direção aprovada e torna o candidato difícil de explicar; moedas não serão comparadas por conversão.

### D3 — Alternativas e recomendação provisória de tolerâncias

Os valores abaixo são **recomendações para revisão, não decisões aprovadas nem defaults autorizados**. Implementação permanece bloqueada até o titular confirmar a política e os casos de borda. A regra final deve ser versionada para que candidatos existentes não mudem silenciosamente quando os limites evoluírem.

| Dimensão | Alternativas para revisão | Recomendação conservadora provisória | Principal risco |
|---|---|---|---|
| Valor | Exato (0%); variação relativa de até 1%; variação relativa de até 5% | No máximo 1% da mediana das três ocorrências, na mesma moeda e sem conversão; considerar a mesma direção/sinal apenas como condição de correspondência, nunca como natureza financeira | Limite amplo pode agrupar compras independentes; limite estreito perde reajustes pequenos. O sinal não comprova compra nem efetivação. |
| Data | Mesmo dia do mês; janela de ±3 dias; janela de ±7 dias | Dia nominal do mês com janela de ±3 dias, considerando datas no fim do mês como um padrão de fim de mês (ex.: dia 31/28/31) | Janela ampla aproxima compras semanais/ocasionais; janela estreita perde mudança de calendário ou processamento bancário. |
| Descrição | Texto bruto idêntico; texto normalizado idêntico; similaridade aproximada | Igualdade após normalização segura de Unicode/caixa, espaços e pontuação; não remover números nem aceitar descrição genérica por fuzzy matching nesta primeira política | Normalização muito agressiva funde lojistas; igualdade estrita pode perder variações de adquirente/sufixo. Similaridade exige algoritmo e limiar aprovados. |

Alternativa mais permissiva para texto — similaridade mínima de 90% — deve ser discutida separadamente: sem algoritmo de similaridade definido, “90%” não é critério executável e não será assumido. Similarmente, a janela de datas deve declarar se conta dia civil ou diferença de dias corridos; a recomendação acima usa dia civil do mês, com a regra explícita de fim de mês.

Exemplos exclusivamente fictícios para avaliação:

- **Candidato plausível com a recomendação:** “NuvemFictícia”, BRL 39,90 em 05/jan, BRL 40,20 em 07/fev e BRL 40,00 em 06/mar. Conta/moeda e descrição normalizada coincidem; a diferença máxima fica abaixo de 1% da mediana e as datas ficam a até dois dias do padrão. O resultado continua “possível recorrência”, sujeito à decisão do titular.
- **Falso positivo mesmo com os sinais combinados:** “Café Central Fictício”, BRL 31,80 em 04/jan, BRL 32,00 em 06/fev e BRL 31,95 em 07/mar. Podem ser três compras independentes feitas em visitas habituais. O sistema pode sugerir candidato; o titular deve poder marcar compras independentes ou rejeitar e corrigir a decisão.
- **Coincidência de valor que não deve gerar candidato:** BRL 59,90 no dia 12 em três meses, mas as descrições fictícias são “Livraria Horizonte”, “Farmácia das Palmeiras” e “Cinema Aurora”. O valor e a data parecidos não substituem a correspondência da descrição.
- **Possível falso negativo da recomendação estreita:** a mesma assinatura fictícia muda de BRL 99,90 para BRL 103,50, ou a descrição varia de “Vídeo Fictício” para “VIDEO*FICTICIO 8472”. A primeira excede a tolerância proposta; a segunda não deve ser unida por fuzzy matching automático. Registrar esse limite é preferível a ampliar tolerâncias sem aprovação.

### D4 — Candidato, decisão e metadados como registros separados

Persistir o candidato e suas ocorrências/referência de política separados de `FinancialTransaction`; guardar a decisão explícita do titular como dado próprio ligado às ocorrências correspondentes. Reutilizar a identidade externa da transação e a conta para reencontrar os registros nas sincronizações. A decisão do titular não deve ser recalculada a partir do texto atualizado da origem nem descartada quando a importação repetir os mesmos movimentos. Mudanças de política não reclassificam decisões anteriores automaticamente.

Metadados de parcelamento recebidos da origem permanecem inalterados e apresentados separadamente. Um `PaymentType`/número de parcela não cria sozinho um candidato nem substitui três observações; parcelas previstas não contam como observações. A confirmação “parcela” é uma decisão do usuário, não uma edição desses metadados.

Alternativa considerada: gravar a decisão em categoria/observação ou sobrescrever natureza da transação. Rejeitada porque conflita com RF15/DT-35, seria vulnerável a sobrescrita na sincronização e mistura classificação pessoal com semântica financeira.

### D5 — Contratos protegidos e compatibilidade

As operações futuras de consulta e decisão devem obter o titular da sessão autenticada, validar propriedade das transações/candidatos e nunca aceitar `UserId` livremente do cliente, conforme TDD §§5/11. Nomes de rotas, DTOs e mapeamentos de persistência serão fechados no incremento executável após FNR-012/015/020/022; não se cria contrato paralelo de movimentos nem se expõe a origem diretamente.

Alternativa considerada: decidir endpoints e migration nesta mudança antes das APIs de histórico e sessão existirem. Rejeitada porque FNR-038 depende desses contratos e o TDD reserva detalhes de modelo/contrato para sua mudança própria, com compatibilidade validada.

### D6 — Validação sem dados reais

Usar fixtures sintéticas cobrindo variação dentro/fora dos limites candidatos, mês ausente, fronteira de ano, fevereiro/fim de mês, repetição de identificador externo, moedas diferentes, descrição genérica, parcelas previstas, metadados ausentes, estado pendente/desconhecido, confirmação/correção, isolamento por titular e sincronização repetida. Testes de integração devem verificar preservação da decisão e imutabilidade dos campos originais; UI/API devem verificar que nenhum agregado monetário surgiu. Não executar novas consultas reais nem usar históricos reais como fixtures.

Alternativa considerada: validar limites com uma nova coleta Pluggy. Rejeitada para este planejamento e desnecessária para criar dados sintéticos; coleta real requer autorização específica e não foi solicitada.

## Risks / Trade-offs

- [Falsos positivos em compras habituais no mesmo estabelecimento] → Exigir simultaneamente conta/moeda, sequência de três meses, valor, data e descrição; mostrar “possível”, permitir rejeição/correção e medir casos com fixtures, sem prometer taxa de acerto.
- [Falsos negativos por reajuste, atraso de processamento ou variação textual] → Expor a recomendação e alternativas ao titular antes de aprovar; não ampliar limites por conveniência nem ocultar que a política conservadora perde alguns padrões.
- [Mudança do histórico ou reimportação apagar a decisão] → Separar registros da origem, candidato e decisão; testar importações idempotentes e preservar as confirmações por identidade estável.
- [Confusão entre recorrência, parcela, natureza e competência] → Manter dimensões separadas, rotular metadados como dados da origem, não usar datas de fatura como competência e não liberar agregados.
- [FNR-012/015/020/022 ainda indisponíveis] → Implementar FNR-038 somente após essas dependências; não simular persistência, sessão ou lista neste incremento de planejamento.

## Migration Plan

Não há migration ou implantação nesta etapa de planejamento. Quando as dependências e aprovações estiverem satisfeitas, detalhar entidades/índices e contratos na mudança executável, validar migration em PostgreSQL descartável e aplicá-la por comando administrativo antes do deploy dependente, nunca na inicialização da API. A adição deve ser compatível com a versão anterior. Rollback funcional pode desabilitar a sugestão sem apagar decisões; eventual reversão de schema exige plano explícito de preservação/recuperação dos registros do titular.

## Aprovações pendentes que bloqueiam implementação

- Aprovar a política numérica de valor, janela de datas e normalização de descrição; os números recomendados na D3 são apenas proposta.
- Confirmar quais situações de origem contam como ocorrência observada, além das exclusões já previstas para duplicatas e parcelas explicitamente futuras. A recomendação é não inferir natureza/status e manter tais campos intactos.
- Revisar falsos positivos/falsos negativos sintéticos e confirmar a regra de fim de mês, identidade de conta/moeda e sentido do valor.

Nenhuma dessas pendências altera a direção aprovada de três meses, candidato não definitivo, decisão corrigível e preservação dos dados originais; não se deve iniciar implementação até o aceite explícito dos critérios.
