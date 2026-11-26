# transaction-recurrence-assistance Specification

## Purpose

Permite ao titular revisar padrões mensais como possíveis recorrências e registrar uma decisão corrigível, sem transformar heurísticas ou dados da origem em natureza financeira, status ou competência.

## Requirements

### Requirement: Sugestão de possível recorrência
O backend SHALL sugerir “possível recorrência” somente para três movimentos elegíveis e distintos em meses-calendário consecutivos, do mesmo titular, `CardAccountId` e moeda, quando todos atenderem à política versionada `recurrence-v1`. Igualdade de valor isolada SHALL NOT bastar nem confirmar a natureza financeira.

#### Scenario: Predicados obrigatórios da política recurrence-v1
- **GIVEN** há exatamente um movimento distinto por mês em três meses-calendário consecutivos, para o mesmo titular, cartão/conta e moeda
- **WHEN** a política avalia valores não zero e de mesmo sinal, cada magnitude com desvio relativo de até 1% da mediana; meses derivados somente de `TransactionDate`, último dia do mês representado por 31 e dias normalizados a até três dias da mediana; e descrições iguais após Unicode NFC, caixa invariável, pontuação convertida em espaço, espaços reduzidos/aparados, preservando números, acentos e símbolos
- **THEN** somente `Situation=Effective` pode formar o trio; `TransactionDate` nula, valor zero, sinal diferente, conta/cartão ou moeda diferente, duplicatas e parcelas futuras/projetadas não o formam, e valor igual isoladamente não basta

#### Scenario: Padrão mensal atende à política aprovada
- **GIVEN** existem movimentos observados distintos em três meses consecutivos, na mesma conta e moeda, e a política de tolerâncias foi aprovada
- **WHEN** valor, data e descrição de todas as ocorrências satisfazem essa política
- **THEN** o backend apresenta um candidato rotulado “possível recorrência”, sem confirmar automaticamente sua natureza

#### Scenario: Valor igual sem outros sinais suficientes
- **GIVEN** três movimentos fictícios têm o mesmo valor, mas descrições não correspondentes ou datas fora da política aprovada
- **WHEN** o backend avalia as ocorrências
- **THEN** não sugere recorrência com base apenas na igualdade dos valores

#### Scenario: Mês ausente, duplicata ou parcela futura
- **GIVEN** o histórico tem um mês sem movimento observado, duas cópias da mesma transação ou apenas uma parcela explicitamente futura em um dos meses
- **WHEN** o backend avalia a sequência
- **THEN** esses dados não completam três meses consecutivos de ocorrências observadas e nenhum candidato é emitido com base neles

#### Scenario: Política aprovada indisponível
- **GIVEN** a versão aprovada `recurrence-v1` não está disponível para avaliação
- **WHEN** o backend tenta avaliar o histórico
- **THEN** não aplica limites implícitos nem emite candidatos usando valores padrão não aprovados

#### Scenario: Limites inclusivos de valor e data
- **GIVEN** três movimentos `Effective` em meses consecutivos, com mesma conta/moeda, sinal e descrição normalizada
- **WHEN** as magnitudes são 99,00/100,00/101,00 e os dias normalizados são 5/6/8
- **THEN** os desvios de valor (até 1%) e de dia (até 3) são aceitos, inclusive na fronteira

#### Scenario: Valor ou data excede o limite
- **GIVEN** três movimentos que atendem às demais condições da versão `recurrence-v1`
- **WHEN** o desvio relativo de uma magnitude ultrapassa 1% ou um dia normalizado fica a mais de três dias da mediana
- **THEN** o backend não emite candidato

#### Scenario: Fim de mês, data nula e meses consecutivos
- **GIVEN** `TransactionDate` é 31/jan, 28/fev e 31/mar para um trio, ou falta uma data técnica em outro trio
- **WHEN** o backend determina os meses e dias para a política
- **THEN** 31/28/31 é comparado como dia 31 em três meses consecutivos, enquanto a data nula não é substituída por vencimento, data de fatura ou projeção e não completa o trio

#### Scenario: Descrição normalizada preserva números
- **GIVEN** descrições diferem somente por caixa, pontuação e espaços, mas uma quarta descrição fictícia difere por número ou acento
- **WHEN** as descrições são normalizadas pela versão `recurrence-v1`
- **THEN** somente as descrições normalizadas exatamente iguais correspondem; não há similaridade aproximada

#### Scenario: Conta, moeda, sinal e situação elegível
- **GIVEN** movimentos têm valores próximos, mas diferem em `CardAccountId`, moeda, sinal ou `Situation` (`Pending`, `FutureInstallment` ou `Unknown`)
- **WHEN** o backend avalia o trio
- **THEN** as ocorrências incompatíveis não formam candidato, sem alterar `Status` da origem ou `Situation`

#### Scenario: Política nova não substitui decisão anterior
- **GIVEN** um candidato/decisão está vinculado à versão `recurrence-v1`
- **WHEN** outra versão de política reavalia o histórico
- **THEN** o sistema não reclassifica nem sobrescreve silenciosamente a decisão já registrada

### Requirement: Decisão explícita e corrigível pelo titular
O sistema SHALL permitir ao titular autorizado registrar para um candidato uma decisão de assinatura, parcela, compras independentes ou rejeição do padrão, e SHALL permitir corrigir posteriormente essa decisão. A sugestão não SHALL ser tratada como confirmação automática.

#### Scenario: Titular confirma a interpretação
- **GIVEN** há um candidato de possível recorrência acessível ao titular autenticado
- **WHEN** o titular o confirma como assinatura, parcela ou compras independentes
- **THEN** o sistema registra a escolha como decisão do titular, separada do candidato e dos dados de origem

#### Scenario: Titular rejeita e corrige uma decisão
- **GIVEN** o titular rejeitou um candidato ou já registrou uma interpretação
- **WHEN** ele revisa o caso e altera sua decisão
- **THEN** a decisão vigente passa a refletir a escolha explícita mais recente, sem converter a sugestão original em classificação automática

#### Scenario: Acesso de outro titular
- **GIVEN** um candidato ou decisão pertence ao titular autenticado
- **WHEN** outro usuário tenta consultar ou alterar esse registro
- **THEN** o sistema nega o acesso e não revela nem modifica a decisão

### Requirement: Metadados de parcelamento separados da heurística
O sistema SHALL preservar e apresentar metadados de parcelamento fornecidos pela origem separadamente do candidato e da decisão do titular. Esses metadados, por si só, SHALL NOT confirmar recorrência nem substituir ocorrências observadas.

#### Scenario: Origem fornece número e total de parcelas
- **GIVEN** um movimento possui metadados de parcelamento fornecidos pela origem
- **WHEN** o candidato e seus detalhes são apresentados
- **THEN** os metadados aparecem como informação da origem, separados da sugestão e da decisão do titular

#### Scenario: Metadado ou projeção não prova padrão observado
- **GIVEN** a origem informa parcelamento ou uma parcela prevista sem três movimentos mensais observados distintos
- **WHEN** o backend avalia a recorrência
- **THEN** a informação é preservada separadamente e não é tratada como prova suficiente para emitir candidato

### Requirement: Preservação da decisão sem alterar dados financeiros
O sistema SHALL preservar a decisão do titular após sincronizações e SHALL manter candidato, decisão e dados originais em dimensões separadas. Registrar ou corrigir uma decisão SHALL NOT alterar valor, data, descrição, natureza financeira, status ou competência, nem habilitar agregados monetários.

#### Scenario: Sincronização posterior
- **GIVEN** o titular registrou uma decisão para ocorrências já persistidas
- **WHEN** uma sincronização atualiza os movimentos correspondentes ou acrescenta novos dados
- **THEN** a decisão permanece vinculada às ocorrências correspondentes e não é sobrescrita pelos dados da origem

#### Scenario: Decisão não modifica a transação
- **GIVEN** o titular confirma, rejeita ou corrige a interpretação de um candidato
- **WHEN** o sistema persiste a decisão
- **THEN** os valores originais da transação, sua natureza, status e competência permanecem inalterados, e nenhum agregado monetário é calculado ou liberado
