# Financial Data Persistence Specification

## Purpose

Preservar os movimentos financeiros do titular, suas relações e anotações pessoais com integridade, identidade estável e semântica de origem explícita, sem converter ambiguidades em compras ou indicadores financeiros validados.

## Requirements

### Requirement: Grafo financeiro associado ao titular
A persistência SHALL vincular conexões, cartões, movimentos, anotações pessoais e execuções a um titular existente. SHALL rejeitar referências inexistentes e relações contraditórias de titular entre esses registros, mesmo quando identificadores individuais forem válidos.

#### Scenario: Referência inexistente
- **WHEN** é solicitada a gravação de uma conexão para titular inexistente ou de movimento para cartão inexistente
- **THEN** a gravação é rejeitada sem publicar registros órfãos

#### Scenario: Mistura de titulares
- **WHEN** um cartão, movimento, anotação ou execução é associado a um registro pai de outro titular
- **THEN** a persistência rejeita a associação sem transferir a propriedade dos dados

### Requirement: Identidade externa opaca e unicidade contextual
A persistência SHALL preservar identificadores externos sem reinterpretar formato, caixa ou espaços. SHALL impedir conexão duplicada por titular/provedor/item, cartão duplicado por conexão/identificador e movimento duplicado por cartão/identificador. SHALL não deduplicar por nome, final do cartão, descrição, data ou valor.

#### Scenario: Identidade repetida na mesma conta
- **WHEN** duas gravações tentam criar o mesmo identificador externo de movimento no mesmo cartão
- **THEN** no máximo uma é confirmada, inclusive sob concorrência, e a outra recebe conflito identificável sem duplicação

#### Scenario: Conteúdo coincidente com identidades distintas
- **WHEN** dois movimentos têm mesma descrição, data e valor, mas IDs distintos, ou o mesmo ID externo existe em cartões distintos
- **THEN** os registros podem coexistir no respectivo contexto sem fusão automática

#### Scenario: Identificador sem formato UUID
- **WHEN** um identificador externo válido contém texto sem formato UUID ou difere somente pela caixa de outro
- **THEN** seu conteúdo é preservado e não é convertido em UUID, normalizado ou confundido com o outro identificador

### Requirement: Valor individual exato e moeda preservada
A persistência SHALL preservar sinal e valor decimal individual representável em precisão 19 e escala 4, junto à moeda original. A fronteira de gravação SHALL rejeitar valores que exigiriam arredondamento ou excederiam a faixa, sem truncar, converter moeda ou substituir ausência por zero. SHALL não produzir agregados monetários.

#### Scenario: Valores de sinais e moedas diferentes
- **WHEN** são gravados movimentos sintéticos com valores positivos, negativos e zero, em BRL e outra moeda
- **THEN** a leitura posterior devolve os mesmos valores e moedas individualmente, sem conversão ou soma

#### Scenario: Valor não representável
- **WHEN** uma gravação contém valor com fração não nula além da quarta casa decimal ou fora da faixa suportada
- **THEN** a fronteira de persistência rejeita a gravação antes de o banco arredondar o valor

### Requirement: Datas originais e normalização técnica explícita
A persistência SHALL manter a data original e, quando derivável, o dia normalizado, a referência temporal e a identificação da regra aplicada. Dia de calendário SHALL não sofrer deslocamento de fuso. Data ambígua SHALL preservar origem e motivo sem criar dia derivado. Nenhuma dessas datas SHALL ser declarada competência financeira validada.

#### Scenario: Calendário e instante na virada do mês
- **WHEN** são persistidos um dia de calendário e um instante com offset próximo à virada do mês
- **THEN** o calendário permanece no mesmo dia, o instante segue a regra técnica documentada e ambos preservam o texto original

#### Scenario: Data sem offset ou previsão apenas mensal
- **WHEN** a data principal é ambígua ou um metadado opcional de previsão contém apenas ano e mês
- **THEN** o dado original é preservado; a data ambígua mantém motivo e dia derivado ausente, e a previsão mensal não recebe um dia ou competência inventados

### Requirement: Natureza e situação conservadoras independentes
A persistência SHALL preservar tipo, status, operação e metadados opcionais de origem separadamente da natureza, situação e seus motivos internos. SHALL manter desconhecidos e inconsistências explicáveis sem transformar categoria, sinal, descrição ou metadado de parcela em regra positiva de compra.

#### Scenario: Operação ambígua com situação pendente
- **WHEN** é persistido um movimento sintético `PAGAMENTO`, `DEBIT`, positivo e `PENDING`
- **THEN** a origem é preservada, a natureza continua desconhecida com motivo e a situação continua pendente conforme a regra conservadora existente

#### Scenario: Metadados ausentes ou contraditórios
- **WHEN** os metadados de parcela estão ausentes ou contêm numeração incoerente
- **THEN** a ausência permanece nula, os valores incoerentes recebidos são preservados com o motivo aplicável e nenhuma parcela adicional é criada

### Requirement: Categoria permitida e anotação única
A persistência SHALL permitir no máximo uma anotação por movimento, com categoria ausente, de sistema ou pessoal do mesmo titular. SHALL rejeitar categoria pessoal de outro titular e chaves normalizadas duplicadas dentro de um mesmo escopo de categoria. Observações SHALL aceitar texto simples de até 500 caracteres e permitir limpeza sem apagar a categoria.

#### Scenario: Associação válida e associação cruzada
- **WHEN** uma anotação referencia categoria do sistema ou do próprio titular e outra tenta referenciar categoria pessoal de terceiro
- **THEN** as associações permitidas podem ser gravadas e a associação cruzada é rejeitada também pela integridade persistida

#### Scenario: Anotação concorrente ou observação acima do limite
- **WHEN** duas gravações tentam criar anotações para o mesmo movimento ou uma observação excede 500 caracteres
- **THEN** a duplicação e o excesso são rejeitados, sem truncar o texto

#### Scenario: Limpar observação e ausência de categoria
- **WHEN** uma observação é limpa ou nenhuma categoria foi escolhida
- **THEN** limpar a observação preserva a categoria existente, e a ausência de categoria tem representação única distinta de uma categoria financeira presumida

### Requirement: Separação entre origem e organização pessoal
A persistência SHALL armazenar a anotação separadamente do movimento de origem. Atualizar os dados externos do movimento SHALL preservar seu identificador interno e a anotação vinculada. Alterar categoria ou observação SHALL não modificar valor, moeda, descrição, datas, natureza ou situação do movimento.

#### Scenario: Atualização controlada dos dados de origem
- **GIVEN** um movimento persistido possui categoria e observação pessoais
- **WHEN** seus dados externos são atualizados na mesma identidade interna
- **THEN** a categoria e a observação continuam associadas ao movimento após nova leitura

#### Scenario: Organização sem reclassificação financeira
- **WHEN** categoria ou observação de um movimento desconhecido é alterada
- **THEN** os dados da origem e a classificação conservadora permanecem inalterados

### Requirement: Execução registrada sem completude presumida
A persistência SHALL manter intervalo solicitado, horários, estado, contadores disponíveis e código sanitizado de falha de uma execução vinculada à conexão. SHALL distinguir intervalo solicitado de cobertura observada e desconhecimento de zero. A existência do registro, sua fila ou uma falha SHALL não comprovar sucesso ou renovar a última importação completa.

#### Scenario: Execução ainda não concluída
- **WHEN** é gravada uma execução pendente ou falha com cobertura desconhecida
- **THEN** ela pode ser consultada como tal, sem cobertura completa presumida e sem avançar o último sucesso da conexão

#### Scenario: Informações de erro
- **WHEN** uma execução recebe código de falha técnica
- **THEN** é persistido somente código interno sanitizado, sem credenciais, payload de provedor ou compras completas

### Requirement: Gravação atômica e preservação do histórico
A persistência SHALL permitir confirmar ou desfazer um conjunto de alterações relacionadas em uma única transação. Falha em qualquer parte SHALL impedir publicação parcial desse conjunto. Excluir um pai referenciado SHALL ser impedido sem apagar automaticamente movimentos e anotações; ausência de resposta externa SHALL não causar exclusão.

#### Scenario: Falha após primeira gravação
- **WHEN** uma operação transacional grava um movimento e depois falha ao gravar sua anotação ou resultado relacionado
- **THEN** o rollback deixa o conjunto anterior intacto, sem o novo movimento, a nova anotação ou atualização parcial de sucesso

#### Scenario: Exclusão de pai referenciado
- **WHEN** é tentada exclusão de titular, conexão, cartão ou categoria ainda referenciada pelo grafo financeiro
- **THEN** a operação é rejeitada sem exclusão em cascata do histórico

### Requirement: Evolução administrativa e compatível do schema
A persistência SHALL suportar criação em base vazia e atualização da base técnica FNR-006 preservando seu histórico de migrations. Alterações de schema SHALL ocorrer por comando explícito, nunca ao iniciar a API. O diagnóstico HTTP existente SHALL continuar disponível sem configuração ou conexão de banco.

#### Scenario: Instalação e atualização
- **WHEN** a sequência de migrations é aplicada em banco vazio e em banco contendo somente a migration técnica FNR-006
- **THEN** ambos chegam ao mesmo schema esperado, sem reescrever a migration anterior, e reaplicá-la não duplica estruturas ou registros

#### Scenario: API sem banco configurado
- **WHEN** a API inicia sem connection string ou com o banco indisponível
- **THEN** não tenta migrar ou provisionar dados e o health mantém o contrato mínimo existente

### Requirement: Minimização e validação isolada
A persistência SHALL limitar os dados de origem ao contrato necessário e não armazenar senha bancária, número completo do cartão, credenciais Pluggy ou respostas brutas. Seus testes de integração SHALL usar somente banco local descartável explicitamente configurado e dados sintéticos, rejeitando destino não autorizado antes da conexão.

#### Scenario: Configuração de teste inválida
- **WHEN** a conexão dedicada dos testes está ausente, aponta para host remoto ou para banco diferente do descartável autorizado
- **THEN** a execução falha antes de conectar, sem fallback para banco pessoal, configuração de runtime ou Neon

#### Scenario: Conteúdo financeiro mínimo
- **WHEN** é inspecionado o modelo persistido e o conjunto de testes
- **THEN** não há campos para segredos bancários, payload bruto ou cartão completo, e as fixtures não reproduzem coletas reais
