# transaction-import Specification

## Purpose

Importar os movimentos disponíveis de cartões de uma conexão financeira de forma segura e repetível, preservando a identidade, o histórico e as anotações do titular sem declarar cobertura ou semântica financeira não comprovadas.

## Requirements

### Requirement: Coleta delimitada por conexão, titular e janela
A importação SHALL operar somente para uma conexão existente pertencente ao titular informado e para os cartões dessa conexão. A primeira coleta SHALL solicitar até doze meses anteriores à referência fixada no início, respeitando a disponibilidade da origem; SHALL preservar movimentos futuros devolvidos pela origem sem criá-los artificialmente.

#### Scenario: Titular ou conexão incompatível
- **WHEN** uma importação é solicitada para conexão inexistente ou pertencente a outro titular
- **THEN** nenhuma coleta ou gravação financeira para essa conexão é realizada e o resultado não revela dados do outro titular

#### Scenario: Primeira janela com parcelas futuras
- **WHEN** a coleta inicia numa data de referência e a origem devolve movimentos históricos disponíveis e parcelas futuras
- **THEN** todas as páginas do intervalo solicitado são processadas e os movimentos futuros recebidos são preservados com sua situação própria, sem prometer doze meses de histórico observado

### Requirement: Paginação íntegra e continuação segura
A importação SHALL anexar `next` exatamente como recebido ao endpoint fixo, sem acrescentar filtros ou reconstruir `after`, até o término válido. SHALL preservar a conta e os filtros conforme o contrato da origem, sem exigir `dateFrom` literal em `next`. SHALL rejeitar origem/caminho inseguros, conta ou filtro explicitamente conflitante, continuação repetida, conta alheia, resposta inválida ou limite excedido. Página vazia ou fim da paginação não comprovam cobertura bancária integral.

#### Scenario: Várias páginas válidas
- **WHEN** a origem fornece duas ou mais páginas válidas para o mesmo cartão e filtros e encerra a continuação
- **THEN** os registros de todas as páginas são considerados para publicação, sem limitar a coleta à primeira

#### Scenario: Continuação sem `dateFrom` literal
- **GIVEN** a primeira página foi solicitada com `dateFrom` fixo
- **WHEN** `next` é uma query relativa válida para o mesmo endpoint e conta, sem `dateFrom` explícito e sem filtro conflitante
- **THEN** o cliente a segue sem alterar `next`, conforme a garantia documental de preservação dos filtros; o teste sintético não comprova o comportamento real da origem

#### Scenario: Falha na segunda página ou cursor inválido
- **WHEN** uma página posterior falha, declara outra conta ou filtro explicitamente conflitante, repete o cursor ou aponta para destino não autorizado
- **THEN** a coleta falha ou é marcada incompleta, não publica o intervalo como completo, não renova o último sucesso e não segue o destino inseguro

### Requirement: Publicação consistente do conjunto coletado
A importação SHALL validar o conjunto antes de torná-lo visível como resultado bem-sucedido e SHALL aplicar suas alterações de forma atômica. Falha de coleta, persistência ou cancelamento antes da confirmação SHALL preservar o conjunto anterior e o horário do último sucesso.

#### Scenario: Falha após páginas já recebidas
- **GIVEN** existe um histórico anteriormente confirmado
- **WHEN** a coleta é interrompida após receber parte das páginas
- **THEN** os movimentos anteriores permanecem disponíveis, as páginas parciais não são publicadas como nova importação completa e o horário de sucesso não avança

#### Scenario: Falha durante a gravação
- **WHEN** a gravação de um movimento ou cartão do conjunto completo falha antes da confirmação
- **THEN** nenhuma alteração parcial do conjunto ou avanço do último sucesso permanece confirmado

### Requirement: Identidade estável e importação repetível
A importação SHALL identificar cartão pela conexão e identificador externo, e movimento pelo cartão e identificador externo opaco. SHALL atualizar registros mutáveis com o mesmo identificador interno e preservar categoria e observação pessoais; SHALL não deduplicar por nome, final, descrição, data ou valor, nem excluir por ausência numa resposta.

#### Scenario: Repetição idêntica e conteúdo coincidente
- **WHEN** o mesmo conjunto é importado duas vezes, contendo movimentos diferentes com descrição, data e valor iguais
- **THEN** a segunda importação não cria duplicatas, os movimentos de IDs externos distintos continuam distintos e suas anotações permanecem vinculadas

#### Scenario: Pendência alterada e movimento ausente
- **GIVEN** um movimento possui anotação pessoal e outro foi importado anteriormente
- **WHEN** uma nova coleta traz nova situação ou valor para o primeiro, mas não devolve o segundo
- **THEN** o primeiro mantém sua identidade e anotação com fatos de origem atualizados e o segundo não é excluído

#### Scenario: ID repetido de forma contraditória na mesma coleta
- **WHEN** duas páginas da mesma coleta retornam o mesmo identificador externo e conta com conteúdo contraditório
- **THEN** o conjunto não é publicado por arbitragem silenciosa entre versões

### Requirement: Reconexão conservadora
A importação SHALL exigir prova de continuidade entre identidades de conexão/cartão antes de reutilizar movimentos após reconexão. SHALL interromper a reconciliação ambígua sem fundir contas por nome, final do cartão ou coincidência de valores; SHALL preservar o histórico confirmado e as anotações.

#### Scenario: Continuidade demonstrável
- **WHEN** um identificador de conexão muda e há vínculo verificável entre a identidade anterior e a nova para o mesmo titular, provedor e cartão
- **THEN** movimentos já conhecidos mantêm identidade e anotações, sem criar segunda carteira

#### Scenario: Mesmo final de cartão sem prova de continuidade
- **WHEN** duas contas compartilham nome ou últimos dígitos, mas não há vínculo confiável entre as identidades externas
- **THEN** a importação não as funde nem publica uma reconciliação presumida

#### Scenario: Reconexão solicitada por chamador interno confiável
- **GIVEN** um chamador interno confiável solicita reconectar a conexão existente a um novo item e a leitura completa do novo item retorna exatamente o conjunto não vazio de IDs externos de cartões já persistidos
- **WHEN** a promoção confirma o mesmo titular/provedor e que o item candidato não pertence a outra conexão/titular nem consta como identidade aposentada
- **THEN** a troca do item ativo, o registro do item anterior como aposentado e a reconciliação de cartões/movimentos ocorrem atomicamente, preservando UUIDs e anotações; nenhum endpoint HTTP aceita o item candidato

#### Scenario: Repetição de item aposentado
- **GIVEN** o item anterior de uma conexão já foi registrado como identidade aposentada
- **WHEN** uma reconexão posterior tenta reativar esse mesmo ID externo
- **THEN** a promoção é recusada sem alterar conexão, histórico, movimentos, anotações ou último sucesso

### Requirement: Interpretação conservadora e campos fiéis à origem
A importação SHALL preservar IDs, valor decimal individual representável, moeda, data original, tipo, status, operação e metadados opcionais permitidos. SHALL aplicar somente as regras conservadoras de natureza, situação e data existentes; campos essenciais inválidos SHALL causar falha do conjunto, sem preencher ausências com zero ou derivar compra, competência ou soma.

#### Scenario: Operação ambígua e parcela futura
- **WHEN** a origem devolve um `PAGAMENTO` ambíguo e uma parcela futura sustentada pelos metadados permitidos
- **THEN** o primeiro conserva natureza desconhecida com motivo e a segunda sua situação própria, sem classificar ambos como compra ou gerar parcelas não recebidas

#### Scenario: Valor ou dado obrigatório inválido
- **WHEN** um registro não traz conta, identidade, valor, moeda ou data de origem obrigatórios, ou seu valor não cabe sem arredondamento na precisão persistida
- **THEN** a importação não grava substitutos nem publica o conjunto como sucesso

### Requirement: Resultado e cobertura verificáveis
A importação SHALL distinguir janela solicitada, término da paginação, extremos e contagens observados, cobertura conhecida ou desconhecida e falha técnica sanitizada. Sucesso SHALL significar somente coleta e gravação das páginas solicitadas, nunca histórico bancário integral, compra validada ou competência financeira.

#### Scenario: Janela vazia com fim de paginação
- **WHEN** todas as páginas foram processadas sem movimentos
- **THEN** o término da consulta é informado, mas a cobertura bancária anterior permanece desconhecida e nenhum mês sem dados é convertido em gasto zero

#### Scenario: Interrupção e contadores ausentes
- **WHEN** a coleta termina por timeout, autorização expirada ou cancelamento
- **THEN** o resultado indica falha sanitizada e cobertura incompleta ou desconhecida; contadores não calculáveis permanecem ausentes e o último sucesso não avança

### Requirement: Privacidade e teste isolado
A importação SHALL manter credenciais, payload bruto, descrições, valores individuais, identificadores e cursores fora de logs e resultados operacionais. Testes automatizados SHALL usar somente resposta simulada e banco local descartável autorizado, distinguindo fixtures sintéticas de evidência real.

#### Scenario: Falha externa com conteúdo sensível
- **WHEN** a origem responde com erro que contém dados privados ou credenciais
- **THEN** somente código interno permitido e informação operacional sanitizada são registrados, sem replicar o conteúdo externo

#### Scenario: Destino de teste não autorizado
- **WHEN** a configuração de banco de integração aponta para destino remoto, pessoal ou não aprovado
- **THEN** o teste recusa a conexão antes de acessar o banco e não tenta consultar a origem real
