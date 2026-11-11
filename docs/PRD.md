# Finora — PRD v0

**Documento de requisitos do produto**  
**Versão:** 0.1 — primeira definição para implementação  
**Data:** 9 de novembro de 2026  
**Contexto:** TCC individual; ideia aprovada pelo professor sem alterações, conforme relato do aluno.  
**Prazo de referência:** duas semanas de desenvolvimento; data final de entrega a confirmar.  
**Responsável pelo produto e desenvolvimento:** aluno autor do TCC.

## 1. Visão do produto

O Finora é uma aplicação web de finanças pessoais que centraliza transações de cartão de crédito e as transforma em indicadores de gastos por período e categoria. O foco inicial é o cartão Nubank do próprio autor, com dados obtidos por um provedor de integração bancária.

O produto também contempla uma carteira de investimentos com valores informados manualmente e um simulador de aportes. Esses módulos são complementares e serão implementados após o funcionamento do núcleo de integração e dashboard.

**Resultado principal esperado:** o usuário abre o Finora e consegue visualizar e organizar suas compras reais, sabendo a qual período pertencem e quando os dados foram atualizados.

**Título provisório do TCC:** Desenvolvimento de um dashboard de finanças pessoais com integração de dados de cartão de crédito.

## 2. Problema

O usuário precisa acompanhar as compras do cartão e compreender sua distribuição ao longo do mês. A consulta isolada de lançamentos dificulta a leitura por categoria e a combinação dessas informações com o planejamento de investimentos.

O Finora pretende reduzir o trabalho de transcrever compras manualmente e oferecer uma visão organizada dos dados disponíveis, preservando a distinção entre gastos efetivados, lançamentos pendentes, parcelas futuras e pagamento de fatura.

**Pergunta orientadora:** como uma aplicação web pode apoiar o controle financeiro pessoal ao reunir dados de cartão de crédito, indicadores de gastos e recursos de planejamento em uma única interface?

## 3. Usuário e contexto de uso

- **Usuário inicial:** o próprio autor do TCC, titular dos dados conectados.
- **Uso principal:** consultar gastos do cartão, filtrar compras e corrigir sua classificação.
- **Uso complementar:** acompanhar valores de investimentos e explorar cenários de aportes.
- **Ambiente inicial:** aplicação executada localmente, com dados persistidos em banco local.
- **Apresentação acadêmica:** usar dados fictícios ou anonimizados por padrão; dados pessoais reais não devem compor material público.

A primeira versão não atenderá contas de terceiros. A conexão de múltiplos usuários e a oferta comercial ficam fora do escopo.

## 4. Situação atual e evidências

| Item | Situação |
|---|---|
| Ideia e protótipo | Aprovados pelo professor, conforme relato do aluno |
| Protótipo visual | Disponível com navegação e dados fictícios |
| Meu Pluggy | Conexão realizada pelo usuário |
| Cartão e lançamentos no painel do provedor | Visualizados nas capturas enviadas pelo usuário |
| Credenciais | Usuário informou ter guardado Client ID, Client Secret e itemId |
| Consulta pelo backend .NET | Ainda não implementada nem validada |
| Persistência e dashboard com dados reais | Ainda não implementados |

A visualização no painel do provedor é evidência preliminar de viabilidade. Não comprova, por si só, a leitura pela aplicação ou a disponibilidade de todos os campos desejados.

## 5. Objetivos e prioridades

**P0 — obrigatório para a primeira entrega:**

1. Consultar o cartão e suas transações pela integração no backend.
2. Persistir os dados e sincronizar novamente sem criar duplicações.
3. Listar, pesquisar, filtrar e categorizar transações.
4. Exibir indicadores coerentes com os lançamentos do período escolhido.
5. Informar atualização, falhas e necessidade de renovação da conexão.

**P1 — complementos previstos, após P0:**

1. Cadastrar e acompanhar investimentos manualmente.
2. Simular evolução de patrimônio com aportes periódicos.
3. Exportar as transações filtradas em CSV.

**P2 — evolução futura:** conexão de outros usuários e instituições, cotações automáticas, integração automática de investimentos, recomendações e recursos avançados de orçamento.

O protótipo é referência de experiência e apresentação. Seus indicadores fictícios não são requisitos automáticos: receitas e saldo financeiro consolidado, por exemplo, exigiriam fontes adicionais que não integram o P0.

## 6. Jornada principal

1. O usuário autoriza o Nubank no Meu Pluggy e disponibiliza essa conexão à aplicação no provedor.
2. As credenciais e o identificador da conexão são configurados localmente no backend.
3. O backend autentica-se no provedor, consulta as contas e identifica o cartão.
4. O sistema importa as transações disponíveis e persiste os dados.
5. O usuário abre o dashboard, escolhe um período e consulta seus gastos.
6. Na lista, pesquisa uma compra, altera sua categoria e observa a atualização dos indicadores.
7. Uma nova consulta atualiza os registros sem duplicar compras e preserva a categoria escolhida pelo usuário.

No P0, o fluxo de autorização acontece no ambiente do provedor. Um botão que inicia a conexão bancária dentro do Angular não é obrigatório. A aplicação deve explicar como configurar e renovar a conexão.

## 7. Requisitos funcionais e critérios de aceite

| ID | Prioridade | Requisito | Critério de aceite |
|---|---|---|---|
| RF01 | P0 | Consultar a conexão e o cartão | Com credenciais válidas, o backend identifica o cartão e recupera seus lançamentos disponíveis; segredos não são retornados ao frontend. |
| RF02 | P0 | Importar transações | Todas as páginas do intervalo solicitado são processadas. Valores, moeda, identificadores, datas, tipo e status disponíveis são preservados. |
| RF03 | P0 | Atualizar sem duplicação | Executar duas consultas sobre os mesmos dados não aumenta a quantidade de registros indevidamente. Mudanças em registros existentes são atualizadas. |
| RF04 | P0 | Exibir transações | A lista mostra descrição, data, valor, moeda, categoria e situação, com parcelamento quando informado. A ordenação padrão é pela data mais recente. |
| RF05 | P0 | Pesquisar e filtrar | Busca por descrição e filtros de período, categoria e situação funcionam em conjunto. Lista e total filtrado usam o mesmo conjunto de registros. |
| RF06 | P0 | Categorizar compras | Uma categoria manual é persistida, refletida no dashboard e preservada após nova consulta ao provedor. |
| RF07 | P0 | Apresentar dashboard | Exibir gastos efetivados líquidos, quantidade de compras, distribuição por categoria e evolução por mês. Totais coincidem com os registros elegíveis. |
| RF08 | P0 | Exibir saúde da consulta | Mostrar data da última consulta bem-sucedida e da atualização na origem, quando disponível. Erros não apagam dados já importados nem simulam sucesso. |
| RF09 | P0 | Tratar estados da interface | Cada tela de dados possui carregamento, ausência de registros e erro com orientação de recuperação. |
| RF10 | P1 | Cadastrar investimentos | Permitir criar, editar e excluir registros manuais de nome, classe, valor aplicado e valor atual; dados persistem após recarregar a aplicação. |
| RF11 | P1 | Resumir carteira | Total aplicado, valor atual, diferença e distribuição por classe correspondem aos registros cadastrados. |
| RF12 | P1 | Simular investimentos | Alterar valor inicial, aporte, taxa anual e prazo atualiza projeção, total aportado e rendimento projetado, sem alterar a carteira real. |
| RF13 | P1 | Exportar CSV | O arquivo contém somente as transações filtradas, com cabeçalhos, datas e valores consistentes com a interface. |

## 8. Regras de negócio

### Cartão e indicadores

- O dashboard inicial descreve **gastos no cartão**, sem apresentar saldo bancário, renda ou patrimônio líquido como se fossem calculados a partir dessas compras.
- Gastos efetivados líquidos correspondem a compras efetivadas menos estornos vinculados a compras. Pagamentos da fatura não são somados como novos gastos ou abatidos novamente dos gastos com compras.
- Juros, tarifas e ajustes devem ser classificados separadamente quando identificáveis. Lançamentos de natureza desconhecida ficam sinalizados para revisão, sem serem tratados silenciosamente como compras.
- A normalização de sinal, tipo e status deve ser validada com exemplos reais da API; não se deve interpretar o valor isoladamente.
- Lançamentos pendentes e parcelas futuras aparecem em situações distintas e não entram no indicador de gastos efetivados. Caso a origem não permita separação confiável, exibir a limitação e revisar o indicador antes da entrega.
- Filtrar por mês significa usar a data normalizada da transação. **Mês de transação não equivale a ciclo de fatura.**
- Faturas, limite e vencimento somente serão exibidos se houver dados confiáveis retornados pela integração. Na ausência, usar “Não disponível”; nunca reutilizar os valores fictícios do protótipo.
- Agregar somente valores em BRL no MVP. Outra moeda, se recebida, permanece identificada na lista e fora dos totais em reais; conversão cambial fica fora do escopo.
- Categorias iniciais: Alimentação, Transporte, Compras, Lazer, Saúde, Educação, Outros e Sem categoria.
- Uma classificação automática recebida do provedor é auxiliar. A escolha manual do usuário tem precedência e o sistema deve funcionar sem enriquecimento pago.

### Sincronização

- A consulta importa o que o provedor já disponibilizou. Não significa atualização instantânea no Nubank.
- O botão deve comunicar essa ação como “Atualizar dados disponíveis”, com horários de referência.
- A identidade da transação deve ser baseada no identificador externo, no provedor e na identidade da conta/cartão. Não deduplicar apenas por descrição, data e valor: compras legítimas podem coincidir.
- Se a renovação da autorização alterar o identificador da conexão, reconciliar a identidade do cartão e das transações antes da importação. A reconexão não deve gerar uma segunda carteira de compras.
- Uma consulta vazia não deve apagar o histórico nem ser interpretada automaticamente como ausência de gastos; verificar validade da autorização e estado da conexão.
- Falhas parciais precisam ser identificadas. Um intervalo somente pode ser apresentado como completo após o processamento de todas as páginas correspondentes.
- Preservar os dados já importados em caso de indisponibilidade externa e sinalizar que podem estar desatualizados.

### Investimentos e simulação

- Carteira real e cenário simulado são independentes.
- A carteira manual mostra diferença entre valor atual e aplicado. Não a apresentar como rentabilidade ajustada a datas, aportes ou resgates.
- A taxa da simulação é informada pelo usuário e não representa promessa ou recomendação.
- Converter a taxa anual efetiva para mensal: `i = (1 + taxaAnual / 100)^(1/12) - 1`.
- Para cada mês: `saldo = saldoAnterior × (1 + i) + aporteMensal`. O aporte ocorre no final do mês.
- Total aportado: `valorInicial + aporteMensal × quantidadeDeMeses`. Rendimento projetado: saldo final menos total aportado.
- Aceitar valor inicial e aporte não negativos, taxa anual entre 0% e 50% e prazo inteiro entre 1 e 30 anos. Rejeitar valores vazios, inválidos ou fora dos limites, com mensagem no campo.
- Informar que a projeção não desconta impostos, taxas ou inflação. Com taxa zero, o saldo final deve ser igual ao total aportado.

## 9. Requisitos não funcionais

| Área | Requisito |
|---|---|
| Credenciais | Manter Client Secret e chaves da API no backend, usando configuração local de segredos ou variáveis de ambiente. Não versionar, registrar em logs ou enviar ao Angular. |
| Privacidade | Persistir somente os dados necessários. Não coletar senha, token bancário, CPF ou identidade completa para este escopo. Dados reais não entram no repositório nem no conjunto de demonstração. |
| Acesso | P0 restrito ao uso local do titular, sem exposição pública. Hospedagem acessível externamente exige autenticação e autorização antes de receber dados reais. |
| Persistência | Reiniciar a aplicação não perde transações, categorias manuais ou investimentos cadastrados. |
| Integridade | Usar precisão decimal para dinheiro, restrições de unicidade e operações de banco consistentes para importação. |
| Uso | Interface em português, formato monetário brasileiro, datas claras, navegação por teclado e identificação textual dos estados. |
| Responsividade | Dashboard e filtros utilizáveis em desktop e celular; tabelas podem oferecer rolagem horizontal. |
| Desempenho | Meta local: consultas da interface ao banco em até 2 segundos para até 5.000 transações. Tempo da API externa deve ser medido separadamente e não é garantido. |
| Falhas externas | Timeout, mensagens compreensíveis e tentativas limitadas. Respeitar limites de requisição do provedor. |
| Operação | Exibir logs técnicos sem segredos ou conteúdo financeiro completo e disponibilizar instruções de execução local. |

## 10. Direção técnica proposta

**Frontend:** Angular, com a versão 22 indicada pelo aluno; confirmar ambiente e dependências antes de fixar a versão.  
**Backend:** ASP.NET Core Web API em .NET, com versão compatível com o SDK instalado.  
**Persistência:** Entity Framework Core; SQLite como proposta inicial para execução local simples.  
**Integração inicial:** Meu Pluggy / API da Pluggy, condicionada à validação de acesso e condições do provedor.

Fluxo de leitura: `Angular → API .NET → banco local`. A integração externa é executada pelo backend para atualizar esse banco; o dashboard não depende de uma nova consulta ao provedor a cada abertura.

Separar o cliente da API externa, a normalização das transações, a persistência e os cálculos do dashboard. Usar um adaptador de dados fictícios para desenvolvimento e apresentação, com indicação visível e configuração distinta da integração real.

Não são necessários microserviços, filas ou webhooks no P0. A atualização explícita pelo usuário é suficiente para a primeira entrega.

### Entidades iniciais

| Entidade | Dados principais |
|---|---|
| Conexão | Provedor, itemId externo, situação, expiração quando disponível, última tentativa e último sucesso |
| Cartão | Identificador externo, conexão, descrição, identificação mascarada, moeda e campos opcionais de limite |
| Transação | Identificador externo, cartão, descrição, valor decimal, moeda, datas, natureza, situação e parcelamento quando disponível |
| Categoria | Identificador e nome |
| Classificação manual | Transação, categoria escolhida e data da alteração; independente da classificação externa |
| Execução de importação | Intervalo solicitado, início, fim, situação, contadores e erro técnico sem dados sensíveis |
| Investimento — P1 | Nome, classe, valor aplicado e valor atual |

## 11. Dependências, riscos e respostas

| Risco ou dependência | Resposta planejada |
|---|---|
| Painel funciona, mas leitura pela API ainda não foi testada | Executar a prova de conceito como primeira tarefa. Validar autenticação, cartão, paginação e transações. |
| Diferenças entre trial, acesso pessoal e recursos pagos | Confirmar as condições antes de depender de uma funcionalidade; manter categorias próprias. |
| Dados atrasados ou consentimento expirado | Exibir horários e estado da conexão, preservar histórico e orientar renovação. |
| Campos de parcelas, faturas ou limites ausentes | Tratar como opcionais e documentar os campos efetivamente recebidos. |
| Duplicações ou alterações nos identificadores | Testar duas importações e a reconciliação de reconexões; preservar categorias manuais. |
| Duas semanas para código e texto | Priorizar P0 e escrever o TCC desde o primeiro dia; iniciar P1 somente após o marco do núcleo integrado. |
| Exposição de finanças na apresentação | Usar conjunto fictício ou anonimizado e ocultar segredos em capturas. |

Se o acesso real pela aplicação falhar, registrar o bloqueio, a tentativa e a alternativa para avaliação com o professor. Dados fictícios permitem continuar o desenvolvimento, mas **não substituem o critério de aceite da integração real**.

## 12. Marcos e plano de entrega

| Marco | Janela sugerida | Resultado verificável |
|---|---|---|
| M1 — Integração | Dias 1–2 | Backend consulta o cartão e transações reais; formato, cobertura e estados documentados. |
| M2 — Dados | Dias 3–5 | Banco persistente, importação paginada e repetível sem duplicações. |
| M3 — Núcleo | Dias 6–9 | Lista, categorias, dashboard, filtros e estados integrados. |
| M4 — Complementos | Dias 10–11, se P0 estável | Carteira manual e simulador; exportação CSV se couber. |
| M5 — Entrega | Dias 12–14 | Correções, documentação, demonstração com dados seguros e apresentação. |

O texto do TCC acompanha todos os marcos. Datas de calendário e disponibilidade diária devem ser registradas no planejamento de execução, sem presumir que o prazo foi renovado com a aprovação.

## 13. Validação e definição de pronto

O núcleo está pronto quando:

- A API .NET consulta dados reais do cartão e o procedimento é reproduzível com configuração local do titular.
- Duas importações dos mesmos dados não duplicam registros e não apagam categorias manuais.
- Alterações vindas da origem são conciliadas; falha parcial não é apresentada como importação completa.
- Gastos do dashboard e listagem conciliam para o mesmo período e critérios.
- Compra efetivada, estorno, pagamento de fatura, lançamento pendente e parcela futura possuem tratamentos verificados em conjunto de teste controlado.
- Pesquisa, filtros e estados sem dados funcionam; falhas do provedor mantêm os dados anteriores acessíveis.
- Credenciais e finanças reais não estão no repositório ou nos arquivos de demonstração.
- O projeto possui instruções de execução, configuração e limitações conhecidas.

Para P1, verificar persistência da carteira, separação entre valores reais e simulados, taxa zero, conversão da taxa anual e aporte no final do mês. A fórmula do simulador deve ser conferida com resultados independentes.

As verificações devem concentrar-se na integração, normalização financeira, duplicações e cálculos. A aplicação é um protótipo acadêmico; não se pretende declarar auditoria financeira ou prontidão para operação comercial.

## 14. Fora do escopo desta entrega

Pagamentos, transferências, compra ou venda de ativos; recomendação automática de investimentos; integração com múltiplos usuários; aplicativo móvel nativo; cotações em tempo real; cálculo tributário; orçamento completo de receitas e despesas de outras fontes; previsão de saldo bancário; autenticação bancária implementada pelo próprio Finora.

## 15. Decisões a confirmar no início da implementação

1. Data exata da entrega e tempo diário disponível.
2. Versões instaladas de .NET, Node e Angular, além do repositório de trabalho.
3. Aceite de SQLite para a primeira execução local ou preferência por banco já dominado pelo aluno.
4. Histórico efetivamente disponível, cobertura de faturas/parcelas e contrato de paginação da API selecionada.
5. Regra de datas e situações observada no retorno real, antes de fechar os indicadores financeiros.

Esses pontos refinam o PRD v0; não alteram a ideia aprovada. As decisões técnicas propostas ainda não representam funcionalidades implementadas.

## 16. Referências do projeto

- Conversa de definição do escopo e aprovação relatada pelo aluno.
- Protótipo Finora e documento “Finora — proposta e roteiro”, entregues nesta conversa.
- Documentação de uso pessoal do provedor: https://docs.pluggy.ai/en/docs/guides/meu-pluggy-personal-use
- Autenticação: https://docs.pluggy.ai/en/reference/authentication
- Cobertura de instituições: https://docs.pluggy.ai/en/docs/open-finance/institutions-coverage

As condições operacionais e a versão dos endpoints devem ser conferidas na implementação; este documento não as assume como contrato imutável.
