# Spec Delta

## Purpose

Permitir ao titular verificar localmente a leitura de seu cartão e lançamentos pela Pluggy, sem expor dados financeiros por HTTP ou divulgar segredos na evidência.

## ADDED Requirements

### Requirement: Execução local explícita
O backend SHALL oferecer um comando explícito de leitura local sem iniciar listener HTTP. Iniciar a API normalmente ou consultar seu health SHALL não executar consultas à Pluggy. O comando SHALL não persistir dados financeiros nem solicitar atualização na instituição.

#### Scenario: Comando de leitura
- **WHEN** o titular executa o comando local com configuração válida
- **THEN** o backend consulta os dados disponíveis na Pluggy sem abrir porta HTTP, gravar compras ou forçar atualização bancária

#### Scenario: Execução normal da API
- **WHEN** a API inicia sem o comando de leitura e atende `/health`
- **THEN** não solicita credenciais da Pluggy nem realiza chamadas externas
- **AND** nenhum endpoint financeiro novo é disponibilizado

### Requirement: Configuração protegida
O comando SHALL obter credenciais e identificador da conexão de configuração externa ao repositório, sem recebê-los como argumentos de linha de comando. Configuração obrigatória ausente SHALL impedir chamadas externas e produzir falha sanitizada com código de saída diferente de zero.

#### Scenario: Segredo ausente
- **WHEN** falta uma credencial obrigatória ou o identificador da conexão
- **THEN** o processo termina sem chamar o provedor e informa apenas a chave de configuração ausente, nunca valores

### Requirement: Verificação de conexão e cartão
O comando SHALL autenticar a aplicação, consultar o item configurado e identificar uma conta de cartão antes de ler lançamentos. Autorização expirada, conexão não apta, cartão ausente ou seleção ambígua SHALL produzir bloqueio explícito, não sucesso de integração.

#### Scenario: Cartão único disponível
- **GIVEN** uma conexão válida com exatamente uma conta de cartão
- **WHEN** o comando consulta as contas
- **THEN** usa o identificador dessa conta para consultar lançamentos, sem confundir conta corrente com cartão

#### Scenario: Consentimento expirado apesar do último status
- **GIVEN** o item informa autorização expirada, mesmo com último status de sucesso
- **WHEN** o comando verifica a conexão
- **THEN** termina com bloqueio sanitizado e orienta renovação no provedor, sem interpretar retorno vazio como ausência de gastos

#### Scenario: Seleção ausente ou ambígua
- **WHEN** não existe cartão ou há vários cartões sem seleção externa inequívoca
- **THEN** o comando informa o bloqueio sem escolher arbitrariamente uma conta

### Requirement: Leitura delimitada e resultado verdadeiro
O comando SHALL recuperar lançamentos do cartão em uma página da consulta e informar explicitamente se existe continuação. Somente uma resposta válida com pelo menos um lançamento SHALL comprovar a leitura real. A prova SHALL não alegar cobertura completa do histórico, importação ou normalização financeira.

#### Scenario: Lançamentos recuperados
- **WHEN** a consulta retorna uma página válida com lançamentos pertencentes ao cartão selecionado
- **THEN** o resumo informa leitura comprovada, quantidade recebida e existência de continuação sem apresentar compras individuais
- **AND** o processo retorna código zero sem declarar o histórico completo

#### Scenario: Página vazia
- **WHEN** a consulta válida não retorna lançamentos
- **THEN** o comando informa leitura inconclusiva e termina com código diferente de zero, sem afirmar inexistência de gastos

### Requirement: Falhas limitadas e evidência sanitizada
O comando SHALL limitar o tempo das chamadas, aceitar cancelamento e terminar com código diferente de zero em falha HTTP, de transporte ou de contrato. Saída e logs SHALL omitir credenciais, tokens, identificadores reais, URLs sensíveis e conteúdo financeiro individual. Respostas externas e exceções brutas SHALL não compor a evidência.

#### Scenario: Falha externa ou resposta inválida
- **WHEN** ocorre timeout, HTTP 401/403/429/5xx, falha de transporte ou resposta incompatível
- **THEN** a saída identifica a etapa e a categoria sanitizada da falha, sem sucesso ou repetição ilimitada

#### Scenario: Cancelamento pelo titular
- **WHEN** o titular cancela a execução em andamento
- **THEN** as operações são canceladas e o processo termina sem declarar leitura comprovada

#### Scenario: Inspeção de saída
- **GIVEN** respostas de teste contendo segredos, identificadores e descrições sentinelas
- **WHEN** a execução termina com sucesso ou falha
- **THEN** stdout, stderr e logs não contêm essas sentinelas nem corpos completos das respostas
