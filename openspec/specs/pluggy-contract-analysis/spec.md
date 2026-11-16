# pluggy-contract-analysis Specification

## Purpose

Permitir ao titular investigar localmente o contrato e a cobertura dos lançamentos disponibilizados pela Pluggy, com paginação limitada e evidência sem conteúdo financeiro individual.

## Requirements

### Requirement: Análise local isolada
O sistema SHALL oferecer análise explicitamente iniciada pelo titular, sem listener HTTP, persistência financeira ou atualização forçada na instituição. SHALL obter a configuração protegida fora do repositório e não alterar o comportamento do comando de leitura de uma página.

#### Scenario: Execução explícita
- **WHEN** o titular inicia a análise com configuração válida
- **THEN** o sistema autentica, verifica conexão e seleciona o cartão antes da coleta, sem abrir porta, gravar compras ou atualizar a instituição

#### Scenario: Configuração ou conexão inválida
- **WHEN** falta configuração, o consentimento está expirado ou a seleção do cartão é ambígua
- **THEN** a análise termina com bloqueio sanitizado sem coletar transações nem selecionar arbitrariamente outro cartão

### Requirement: Paginação delimitada e segura
O sistema SHALL percorrer as páginas da consulta preservando conta e filtros, aceitando continuação somente no endpoint e origem autorizados. SHALL limitar páginas e duração, detectar cursor repetido e rejeitar registros de outra conta. Falha ou limite atingido SHALL produzir resultado incompleto, nunca sucesso de coleta.

#### Scenario: Continuação válida
- **WHEN** uma página válida contém continuação e a execução está dentro dos limites
- **THEN** o sistema consulta a próxima página para a mesma conta e filtros até encontrar continuação nula

#### Scenario: Continuação insegura ou cíclica
- **WHEN** a continuação muda origem, endpoint, conta ou filtros, ou repete cursor já processado
- **THEN** o sistema interrompe a coleta sem enviar credenciais ao destino indicado e informa falha sanitizada

#### Scenario: Falha intermediária ou limite
- **WHEN** ocorre erro numa página posterior, timeout, cancelamento ou esgotamento de páginas
- **THEN** o resultado identifica a interrupção e não apresenta as páginas já lidas como conjunto completo

### Requirement: Cobertura e medição verdadeiras
A análise SHALL informar término da paginação, páginas, registros recebidos e únicos, duração e disponibilidade dos campos permitidos. SHALL separar janela solicitada de histórico efetivamente observado e preservar parcelas futuras retornadas. Página vazia ou cursor final SHALL não comprovar doze meses de cobertura bancária.

#### Scenario: Janela histórica e lançamentos futuros
- **WHEN** a coleta solicita desde doze meses antes da referência, sem limite superior que elimine futuros, e termina a paginação
- **THEN** o relatório distingue registros históricos e futuros, informa cobertura observada ou indeterminada e não promete meses ausentes

#### Scenario: Duplicação entre páginas
- **WHEN** o mesmo identificador externo aparece em mais de uma página
- **THEN** o resumo distingue recebidos de únicos e aponta a repetição sem contar o registro duas vezes como evidência financeira independente

### Requirement: Cruzamentos de contrato em buckets fixos
A análise SHALL emitir contagens allowlisted por código conhecido de `operationType`, operação × direção × sinal × status, status × presença de `billId` e categoria conhecida de `paymentType`. Cada distribuição SHALL distinguir ocorrências recebidas de IDs únicos; valores externos inesperados SHALL ser agrupados em `other` sem texto bruto.

#### Scenario: Código conhecido e código inesperado
- **WHEN** a coleta contém códigos documentados e um código externo não reconhecido
- **THEN** cada código conhecido tem bucket interno próprio e o valor não reconhecido conta somente em `other`, sem aparecer na saída

#### Scenario: Duplicata idêntica entre páginas
- **WHEN** o mesmo ID e conteúdo surgem novamente em outra página
- **THEN** os buckets de recebidos contam ambas as ocorrências e os buckets de únicos contam o ID uma só vez

#### Scenario: Status e fatura
- **WHEN** registros têm estados distintos e `billId` presente ou ausente/nulo
- **THEN** a distribuição agrega a combinação de status com presença de `billId`, sem exibir o próprio identificador

### Requirement: Consistência agregada de parcelas e datas
A análise SHALL agrupar a completude e coerência estrutural dos metadados opcionais de parcela e as relações temporais entre `date`, `purchaseDate`, `billPostDate` e `billForecastDate`. SHALL publicar apenas buckets fixos, sem datas individuais nem afirmar competência financeira.

#### Scenario: Relações temporais comparáveis
- **WHEN** dois campos de data são válidos e podem ser comparados pelas regras diagnósticas documentadas
- **THEN** a saída conta somente categorias como mesma data/mês, anterior, posterior ou diferente, sem revelar os valores comparados

#### Scenario: Data ou metadado ausente/inválido
- **WHEN** falta um dos campos ou o metadado de parcela é parcial ou incoerente
- **THEN** a relação ou consistência é contada no bucket fixo apropriado, sem inferir natureza, efetivação ou competência

### Requirement: Validade diagnóstica de billForecastDate
A análise SHALL aceitar `yyyy-MM` estrito e formatos de calendário/instante já aceitos pelo normalizador (`yyyy-MM-dd` e instante com offset explícito). SHALL distinguir previsão ausente/nula, inválida e válida. Relações SHALL comparar somente ano/mês, sem fabricar dia/horário nem inferir competência.

#### Scenario: Período mensal estrito de previsão
- **WHEN** `billForecastDate` contém um período `yyyy-MM`, uma data-calendário ou um instante com offset explicitamente aceitos
- **THEN** o valor é contado como válido e relações são comparadas somente por ano/mês, inclusive na virada do ano, sem sintetizar dia/horário ou competência

#### Scenario: Previsão ausente ou período malformado
- **WHEN** `billForecastDate` é ausente/nulo, ou contém mês fora de `01`–`12`/formato não aceito
- **THEN** a validade distingue `missing` de `invalid`, e a relação usa `missing_or_invalid` sem revelar o conteúdo

### Requirement: Evidência sanitizada e rastreável
O sistema SHALL omitir de saída e logs credenciais, tokens, IDs, cursores, URLs sensíveis, descrições e valores individuais. A evidência SHALL distinguir casos observados, documentados e apenas simulados; campos ou naturezas não observados SHALL permanecer limitações explícitas, sem dados fictícios apresentados como prova real.

#### Scenario: Resumo de análise real
- **WHEN** a execução termina com sucesso ou falha
- **THEN** somente etapas, códigos técnicos permitidos, contagens, duração e indicadores agregados de disponibilidade são apresentados, sem respostas brutas

#### Scenario: Natureza não encontrada
- **WHEN** a amostra real não contém estorno ou metadados opcionais
- **THEN** o registro de validação informa não observado e separa os testes fictícios da evidência real
