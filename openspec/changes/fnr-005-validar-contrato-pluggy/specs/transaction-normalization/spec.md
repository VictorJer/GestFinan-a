# Spec Delta

## Purpose

Interpretar lançamentos de cartão de forma explícita e verificável, preservando os dados necessários da origem e sinalizando ambiguidades antes da importação e dos indicadores financeiros.

## ADDED Requirements

### Requirement: Contrato monetário e identidade preservados
A normalização SHALL preservar identificador externo opaco, conta, descrição original, valor decimal, moeda e campos originais necessários à interpretação. SHALL não deduplicar por descrição, data ou valor, converter moedas ou substituir ausência de dado obrigatório por zero ou texto fictício.

#### Scenario: Compras coincidentes e moeda estrangeira
- **WHEN** dois IDs distintos têm a mesma descrição, data e valor, sendo um lançamento em moeda diferente de BRL
- **THEN** permanecem registros distintos, com moeda e precisão preservadas, e o lançamento estrangeiro não é elegível para total em reais

#### Scenario: Campo obrigatório inválido
- **WHEN** falta identidade, valor, moeda ou data válida
- **THEN** o registro é sinalizado como inválido e não se torna compra elegível por preenchimento artificial

### Requirement: Natureza financeira conservadora
A normalização SHALL distinguir compra, estorno, pagamento de fatura, encargo, ajuste e desconhecido usando evidência contratual além do sinal. Operação ausente, nova ou contraditória SHALL permanecer desconhecida quando não houver regra validada. Estorno sem vínculo confiável SHALL não ser abatido automaticamente de compras.

#### Scenario: Crédito não é automaticamente estorno
- **WHEN** um crédito negativo representa pagamento de fatura ou não possui natureza identificável
- **THEN** é classificado respectivamente como pagamento ou desconhecido, nunca como estorno apenas pelo sinal

#### Scenario: Código PAGAMENTO ambíguo nesta conexão
- **WHEN** um lançamento apresenta `operationType=PAGAMENTO`, direção `DEBIT`, valor positivo e status `PENDING`, combinação cuja natureza de compra foi confirmada pelo titular em um único caso local
- **THEN** a confirmação daquele caso é distinguida da classificação automática; sem condição discriminatória validada, a natureza automática permanece desconhecida, `PENDING` permanece não efetivado e nenhum outro `PAGAMENTO` é promovido a compra ou pagamento de fatura por equivalência, descrição ou padrão textual

#### Scenario: Encargo ou operação desconhecida
- **WHEN** um lançamento informa tarifa identificável ou uma operação não reconhecida
- **THEN** recebe respectivamente natureza de encargo ou desconhecida, sem inclusão silenciosa entre compras

#### Scenario: Estorno sem vínculo verificável
- **WHEN** a origem identifica estorno mas não permite associá-lo confiavelmente à compra
- **THEN** a natureza é preservada e sua elegibilidade para abatimento permanece bloqueada com motivo explícito

### Requirement: Situação e parcelas independentes da natureza
A normalização SHALL separar efetivado, pendente, parcela futura e situação desconhecida, preservando metadados de parcelamento opcionais. SHALL não considerar toda pendência parcela futura, nem efetivar lançamento apenas por ter data passada ou fatura associada. Casos ambíguos SHALL ficar fora dos gastos confirmados.

#### Scenario: Compra pendente e parcela futura
- **WHEN** uma compra tem status pendente e outra possui evidência validada de parcela futura
- **THEN** mantém situações distintas e ambas ficam fora dos gastos efetivados

#### Scenario: Metadado ausente ou incoerente
- **WHEN** parcelas, fatura ou limite não estão disponíveis, ou a numeração de parcelas é incoerente
- **THEN** não são inventados valores e a inconsistência é sinalizada sem gerar novas parcelas artificiais

### Requirement: Semântica explícita de datas
A normalização SHALL preservar a data original e explicitar a regra da data de transação usada em filtros, sem confundir compra, lançamento e vencimento de fatura. Datas somente de calendário SHALL não mudar de dia por conversão de fuso. Semântica não confirmada SHALL permanecer limitação visível.

#### Scenario: Limite de mês
- **WHEN** um instante próximo à virada de mês é normalizado
- **THEN** o dia e mês seguem a regra documentada e testada para aquele campo, sem usar o vencimento da fatura como competência

#### Scenario: Dia sem horário
- **WHEN** um campo válido representa somente uma data de calendário
- **THEN** o mesmo dia é preservado, sem tratá-lo como instante UTC sujeito a deslocamento

### Requirement: Elegibilidade explicável sem agregação antecipada
A normalização SHALL produzir motivo verificável de elegibilidade ou exclusão segundo natureza, situação, moeda e validade. Pagamento de fatura, pendência, parcela futura, encargo, ajuste não elegível e desconhecido SHALL não compor compras efetivadas; esta capacidade SHALL não calcular dashboard nem persistir transações.

#### Scenario: Conjunto controlado
- **WHEN** são normalizados exemplos fictícios de compra efetivada em BRL, pagamento, pendência, estorno sem vínculo e compra em USD
- **THEN** somente a compra válida e efetivada em BRL é elegível como compra para gastos em reais, e os demais têm motivos de exclusão distintos
