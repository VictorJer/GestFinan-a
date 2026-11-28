# Spec Delta

## Purpose

Permitir ao titular autenticado explorar projeções nominais de aportes mensais e comparar cenários temporários, sem modificar sua carteira manual nem prometer retorno financeiro.

## ADDED Requirements

### Requirement: Projeção mensal nominal calculada no backend
O sistema SHALL calcular uma projeção mensal a partir de valor inicial, aporte mensal ao fim do mês, taxa anual efetiva informada e prazo em anos. SHALL usar `i = (1 + taxaAnual / 100)^(1/12) - 1` e, para cada mês, `saldo = saldoAnterior × (1 + i) + aporteMensal`, sem arredondar o saldo usado pelo mês seguinte.

#### Scenario: Aporte ao final do mês e taxa efetiva
- **WHEN** o titular simula um prazo válido com taxa anual positiva
- **THEN** recebe pontos mensais em ordem cronológica, começando no mês zero, calculados com a taxa mensal efetiva e o aporte depois da incidência dos juros de cada mês

#### Scenario: Taxa anual zero
- **WHEN** o titular simula taxa anual zero com valor inicial e aporte mensal válidos
- **THEN** o saldo projetado do mês N corresponde exatamente ao valor inicial mais N aportes e o rendimento projetado é zero

### Requirement: Entradas explícitas e limitadas do cenário
O sistema SHALL aceitar somente valor inicial entre `0.00` e `10000000.00`, aporte mensal entre `0.00` e `1000000.00`, taxa anual efetiva entre `0.00` e `50.00` por cento e prazo inteiro entre 1 e 30 anos. Valores monetários e taxa SHALL trafegar como strings decimais canônicas invariáveis de duas casas; prazo SHALL ser inteiro JSON.

#### Scenario: Limites inclusivos
- **WHEN** o titular informa os quatro limites mínimos ou máximos em seus formatos permitidos
- **THEN** a simulação é aceita, inclusive para valores iniciais e aportes zero

#### Scenario: Entrada inválida
- **WHEN** falta campo, há campo extra ou duplicado, número JSON no lugar de string decimal, escala não canônica, sinal negativo, taxa ou valor acima do teto, ou prazo fracionário/fora de 1 a 30
- **THEN** a API recusa a simulação sem produzir projeção; o formulário identifica o campo inválido para correção, sem apresentar resultado antigo como se fosse o novo

### Requirement: Série e totais coerentes sem perda de centavos na resposta
O sistema SHALL devolver por mês o total aportado acumulado e o patrimônio projetado, além de total aportado, patrimônio final e rendimento final, em strings monetárias decimais de duas casas. SHALL manter precisão interna entre meses e arredondar somente os valores publicados; rendimento SHALL corresponder a patrimônio final menos total aportado.

#### Scenario: Comparação com cálculo independente
- **WHEN** o titular consulta uma simulação de taxa positiva com dados conhecidos
- **THEN** a série e os totais apresentados concordam com a fórmula do PRD e com um cálculo independente dentro da precisão de centavos definida para a apresentação

#### Scenario: Valor não representável
- **WHEN** o cálculo não pode produzir uma resposta monetária finita e representável
- **THEN** a API recusa a projeção com erro sanitizado em vez de retornar valores truncados, infinitos ou aparentemente exatos

### Requirement: Simulador autenticado, independente e honesto
O sistema SHALL disponibilizar a simulação somente ao titular autenticado. SHALL rotular resultados como projeções nominais baseadas na taxa informada, sem promessa/recomendação nem desconto de impostos, taxas ou inflação; calcular ou consultar SHALL não escrever na carteira nem importar dados bancários.

#### Scenario: Acesso sem sessão
- **WHEN** alguém tenta calcular sem sessão válida ou abre diretamente a página protegida sem login
- **THEN** não obtém resultados protegidos e é orientado a entrar, sem expor cenário de outra sessão

#### Scenario: Cenário calculado
- **WHEN** o titular altera uma entrada válida da página
- **THEN** vê resultado atualizado, série e comparação entre patrimônio e aportes, com aviso de premissas; a carteira real permanece inalterada

#### Scenario: Falha ao atualizar
- **WHEN** a API falha, a sessão expira ou a entrada fica inválida enquanto outra simulação aguarda resposta
- **THEN** a página não exibe resposta tardia como projeção das entradas atuais e apresenta estado textual recuperável

### Requirement: Cenários temporários comparáveis na sessão
O sistema SHALL permitir ao titular guardar até cinco cenários confirmados, comparar seus totais e séries com o cenário atual e limpar a lista. SHALL manter esses cenários apenas na memória da sessão autenticada atual, sem banco, armazenamento persistente ou reutilização após logout, expiração, recarga ou troca de sessão.

#### Scenario: Guardar e comparar
- **WHEN** o titular guarda dois cenários calculados validamente na mesma sessão
- **THEN** pode identificá-los por suas entradas e comparar valores projetados e aportados com o cenário atual, sem alterar seus investimentos

#### Scenario: Limpar ou encerrar sessão
- **WHEN** o titular limpa os cenários ou a sessão termina, expira, muda de geração ou a página é recarregada
- **THEN** as cópias guardadas deixam de aparecer e não são apresentadas em sessão posterior

#### Scenario: Limite de cenários
- **WHEN** já existem cinco cenários guardados
- **THEN** uma nova tentativa é recusada com orientação para remover/limpar antes de adicionar outro, sem descartar silenciosamente um cenário anterior

### Requirement: Preenchimento opcional a partir da carteira própria
O sistema SHALL oferecer uma ação explícita para ler o total atual da carteira manual do titular autenticado e copiar esse valor somente para o campo inicial do cenário. A leitura SHALL usar o resumo protegido da carteira; não SHALL usar movimentos de cartão, escrever investimentos nem fazer transferência automática.

#### Scenario: Carteira própria disponível
- **WHEN** o titular aciona o preenchimento e a leitura da carteira da sessão atual retorna um total dentro do limite do simulador
- **THEN** apenas o valor inicial é atualizado, com indicação da origem, e a simulação passa a usar esse valor na próxima atualização válida

#### Scenario: Erro, limite ou sessão trocada
- **WHEN** a leitura falha, o total supera `10000000.00` ou a sessão muda enquanto a leitura está pendente
- **THEN** nenhum valor é truncado, nenhuma resposta antiga preenche o formulário e a página informa por que não foi possível usar a carteira
