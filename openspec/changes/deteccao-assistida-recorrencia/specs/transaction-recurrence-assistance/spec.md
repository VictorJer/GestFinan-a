# Spec Delta

## Purpose

Permite ao titular revisar padrões mensais como possíveis recorrências e registrar uma decisão corrigível, sem transformar heurísticas ou dados da origem em natureza financeira, status ou competência.

## ADDED Requirements

### Requirement: Sugestão de possível recorrência
O backend SHALL sugerir somente “possível recorrência” quando houver um movimento observado distinto em cada um de três meses consecutivos, na mesma conta e moeda, e todas as ocorrências atenderem à política aprovada de valor, data e descrição. Valor igual isoladamente SHALL NOT ser suficiente. Duplicatas e parcelas explicitamente futuras SHALL NOT contar como ocorrências mensais observadas.

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

#### Scenario: Tolerâncias ainda não aprovadas
- **GIVEN** não existe política de correspondência de valor, data e descrição aprovada para o detector
- **WHEN** o backend tenta avaliar o histórico
- **THEN** não aplica limites implícitos nem emite candidatos usando valores padrão não aprovados

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
