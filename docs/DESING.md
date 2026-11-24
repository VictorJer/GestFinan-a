# Finora — Design do protótipo

- **Arquivo:** `DESING.md`, conforme solicitado.
- **Versão:** 1.0
- **Data:** 24/11/2026
- **Natureza:** documentação visual e de interação do protótipo original.
- **Fonte principal:** `Finora-prototipo.html` (protótipo original ainda não localizado neste repositório; caminho pendente).
- **Estado:** referência extraída do código; não representa aprovação de novos requisitos ou aceite da implementação Angular.

## 1. Objetivo e limites

Este documento registra a identidade visual, a composição das telas, os componentes e as interações do protótipo navegável criado para apresentar o Finora ao professor. Ele deve permitir reaproveitar o visual na aplicação Angular sem depender de reconstruir a interface a partir da memória.

O protótipo original usa HTML, CSS e JavaScript, com dados fictícios e estado somente em memória. Não contém login, API, integração bancária, cotações ao vivo ou persistência após recarregar a página. As fontes web são carregadas pelo Google Fonts, com alternativas locais em CSS.

As seções 2–12 descrevem o que existe no protótipo, incluindo recursos da proposta inicial. A seção 13 orienta sua adaptação ao MVP revisado. Em caso de conflito de comportamento, prevalecem PRD, TDD e specs aprovados no repositório do projeto. Este arquivo não substitui esses documentos nem autoriza implementação.

### Relação com os documentos do projeto

| Documento | Responsabilidade e vínculo com esta referência |
|---|---|
| [PRD](PRD.md) | Define escopo, requisitos e critérios de aceite; RF04–RF07, RF09, RF14–RF17 e §8 delimitam o que pode ser apresentado no MVP. |
| [TDD](TDD.md) | Define decisões e contratos técnicos; §§3, 5, 11 e 13 orientam a adaptação do visual ao Angular e aos dados reais. |
| [Backlog](BACKLOG.md) | Distribui a implementação do frontend entre FNR-019 (base visual), FNR-020 (login), FNR-021 (cartão/atualização), FNR-022 (lista) e FNR-023 (visão descritiva); FNR-029–031 tratam complementos. |
| OpenSpec | Cada mudança de frontend deve referenciar as seções visuais aplicáveis deste arquivo em seu desenho/tarefas, sem substituir specs de comportamento nem presumir aprovação da interface. |

Este documento é uma referência visual histórica, não uma nova fonte de requisitos funcionais: comportamentos demonstrativos e dados fictícios só entram no produto quando contemplados pelo PRD e pela mudança OpenSpec aprovada.

## 2. Conceito visual

O Finora apresenta organização financeira com uma aparência calma, clara e pessoal. A composição evita uma tela inteira de números igualmente destacados: títulos, valores, descrições e ações recebem níveis diferentes de atenção.

Características principais:

- Fundo geral cinza muito claro, com painéis brancos.
- Navegação lateral escura e seleção roxa.
- Roxo como identidade e cor de ação principal; lavanda para superfícies de apoio.
- Verde discreto para variações positivas demonstrativas e confirmação de conexão simulada.
- Cantos arredondados, bordas finas e pouco uso de sombras.
- Espaçamento generoso entre áreas, com tabelas mais compactas.
- Ícones lineares e valores com boa hierarquia tipográfica.

A tela é organizada como um espaço de consulta, com ações pontuais. Animações não são necessárias para entender ou concluir um fluxo.

## 3. Marca e linguagem

### 3.1 Assinatura

| Elemento | Representação no protótipo |
|---|---|
| Nome | `finora.` em minúsculas |
| Símbolo | Glifo `ƒ` dentro de quadrado lavanda arredondado |
| Ponto da marca | Lavanda, em contraste com o nome branco na navegação |
| Assinatura de apoio | “Clareza para o seu amanhã” |
| Mensagem lateral | “Um olhar para o futuro.” / “Organize o presente. Planeje suas possibilidades.” |
| Perfil | Pessoa fictícia, avatar com iniciais e indicação de perfil demonstrativo |

O símbolo do protótipo é uma composição tipográfica, não um arquivo de logotipo vetorial separado. Não há manual de marca ou pacote de logos adicional no HTML.

### 3.2 Tom dos textos

Usar português brasileiro, frases diretas e instruções próximas à ação. Títulos podem ser acolhedores; rótulos de campos e estados devem ser precisos.

Exemplos existentes:

- “Uma visão clara do seu dinheiro.”
- “Seu cartão, mais perto.”
- “Cada movimento conta.”
- “Imagine seu próximo capítulo.”
- “Nenhuma transação encontrada para este filtro.”

No protótipo, “Simular conexão”, “Sincronizar demo” e os avisos de dados fictícios explicam o caráter demonstrativo. Na aplicação real, o texto precisa refletir a operação efetivamente disponível e seu resultado, sem anunciar sucesso antes da confirmação.

## 4. Tokens visuais

### 4.1 Cores principais

Valores extraídos do CSS original:

| Token original | Valor | Uso |
|---|---|---|
| `--bg` | `#F6F7FB` | Fundo geral |
| `--ink` | `#202431` | Texto principal |
| `--muted` | `#7D8192` | Texto secundário |
| `--line` | `#EAECF2` | Bordas e divisórias |
| `--purple` | `#7850CF` | Botão principal, identidade |
| `--light` | `#F0EAFA` | Superfície lavanda |
| `--green` | `#268975` | Variação positiva demonstrativa |
| `--nav` | `#20222F` | Navegação lateral |
| Superfície | `#FFFFFF` | Painéis, campos e cabeçalho |

Variações relevantes:

| Elemento | Valor |
|---|---|
| Item ativo da navegação | `#8060CF` |
| Hover do botão principal | `#6640B9` |
| Foco de botão/link | `#B2A1ED`, contorno de 3 px |
| Gradiente do cartão | `#8660D4` → `#6633A8`, a 120° |
| Fundo do resultado do simulador | `#F3EDF9` |
| Conexão simulada ativa | Fundo `#EAF7F1`, texto `#3C9478` |
| Overlay de modal | `#1C1B32A6`, com desfoque de 5 px |

Os gráficos de categorias usam uma sequência de roxos e lavandas: `#8C6ACB`, `#AE91D5`, `#C6AFE4`, `#D9C7EE`, `#E6DAF4` e `#EEE6F8`.

Esses valores documentam a referência visual. O contraste de cada combinação deve ser verificado antes de reutilizá-la, principalmente nos textos pequenos e cinzas; este documento não declara conformidade de acessibilidade da paleta.

### 4.2 Tipografia

| Papel | Família | Tamanho observado |
|---|---|---|
| Texto-base | DM Sans, fallback `system-ui, sans-serif` | 14 px |
| Títulos e números destacados | Manrope, fallback `system-ui, sans-serif` | Conforme hierarquia |
| Título principal | Manrope, peso 800 | 29 px; 26 px no mobile |
| Título de painel | Manrope | 17 px; 16 px no mobile |
| Subtítulo | DM Sans | 12 px |
| Valor de indicador | Manrope, peso 750 | 25 px; 21/20 px em telas menores |
| Valor principal do simulador | Manrope, peso 750 | 33 px; 29 px no mobile |
| Botão | DM Sans, peso 600 | 12 px |
| Tabela | DM Sans | 11 px; cabeçalho de 10 px |
| Rótulos de apoio | DM Sans | Geralmente 10–12 px |

Títulos usam espaçamento entre letras levemente negativo. Valores da tabela usam algarismos tabulares, facilitando comparar casas decimais. Moeda é formatada como BRL com localidade `pt-BR`; percentuais demonstrativos têm até uma casa decimal.

Os tamanhos de 8–11 px presentes em detalhes do protótipo são uma limitação da referência: não devem ser copiados sem avaliação de legibilidade na interface definitiva.

### 4.3 Formas, espaçamento e efeitos

| Elemento | Referência |
|---|---|
| Raio principal de painel | 18 px |
| Indicadores | 14 px |
| Botões e campos | 9 px |
| Item de navegação | 11 px |
| Cartão visual | 15 px |
| Modal | 22 px |
| Badge demonstrativo | 20 px |
| Ícone-base | 20 × 20 px, traço de 1,7 px |
| Ícone de botão | 16 × 16 px |
| Padding de painel desktop | 23 px vertical / 25 px horizontal |
| Distância entre painéis | Aproximadamente 22–23 px |
| Padding de botão | 11 px vertical / 15 px horizontal |

O CSS original usa medidas próximas, não uma escala de espaçamento rigorosamente única. Ao traduzir para Tailwind, consolidar valores repetidos sem alterar a hierarquia visual.

Sombras são reservadas principalmente ao item ativo da navegação, modal e toast. Painéis comuns são separados por borda, em vez de sombra intensa.

## 5. Estrutura da aplicação

### 5.1 Desktop

```text
┌──────────────────┬──────────────────────────────────────────────┐
│ Marca            │ Breadcrumb         Demo / Privacidade / Perfil│
│                  ├──────────────────────────────────────────────┤
│ Navegação        │ Título + descrição              Ações / mês  │
│                  │                                              │
│                  │ Conteúdo da tela: indicadores, painéis, lista│
│                  │                                              │
│ Mensagem         │                                              │
│ Perfil fictício  │ Rodapé                                       │
└──────────────────┴──────────────────────────────────────────────┘
```

- Sidebar fixa de 232 px; conteúdo deslocado pela mesma largura.
- Cabeçalho superior de 80 px, branco, com borda inferior.
- Área de conteúdo com largura máxima de 1.480 px e padding desktop de 34 px vertical superior / 42 px horizontal.
- Cabeçalho de página com título e descrição à esquerda, controles à direita.
- Indicadores em quatro colunas.
- Painéis em duas colunas com proporções assimétricas; na visão geral, o gráfico recebe mais espaço que o cartão.

### 5.2 Navegação original

| Ordem | Tela | Identificador no HTML |
|---|---|---|
| 1 | Visão geral | `overview` |
| 2 | Meu cartão | `card` |
| 3 | Transações | `transactions` |
| 4 | Investimentos | `investments` |
| 5 | Simulador | `simulator` |
| 6 | Sobre o projeto | `about` |

A navegação altera o conteúdo dentro da mesma página. O protótipo não tem rotas de URL ou histórico de navegação próprio. O item ativo recebe fundo roxo e `aria-current="page"`; o breadcrumb acompanha a tela.

Ao mudar de tela, busca e filtro de categoria são reiniciados. O mês selecionado e os demais dados da sessão permanecem em memória.

## 6. Telas do protótipo original

### 6.1 Visão geral

**Título:** “Uma visão clara do seu dinheiro.”

Composição, de cima para baixo:

1. Saudação ao perfil fictício, seletor de mês e botão de conexão simulada.
2. Quatro indicadores: saldo do mês, receitas do mês, gastos no cartão e patrimônio investido.
3. Gráfico de barras de histórico mensal à esquerda e resumo visual do cartão à direita.
4. Distribuição de gastos por categoria em rosca e tabela das quatro transações mais recentes do mês.
5. Rodapé acadêmico e indicação de dados fictícios.

No estado inicial fictício de novembro de 2026, o HTML calcula R$ 1.154,00 em transações, usa receita demonstrativa fixa de R$ 6.500,00 e mostra patrimônio de R$ 24.850,00. Esses números descrevem a demonstração; não são dados do titular, metas ou valores de fallback da aplicação.

O gráfico mensal mistura valores fictícios fixos de junho a setembro com valores calculados das listas de outubro e novembro. Novembro é identificado como parcial. Há também uma função de gráfico por faixas de dias no código, mas ela não compõe a visão geral renderizada; não deve ser tratada como uma tela adicional.

**Limite de reutilização:** os indicadores e gráficos monetários originais não integram automaticamente o MVP revisado. Ver seção 13.

### 6.2 Meu cartão

**Título:** “Seu cartão, mais perto.”

Layout desktop em duas colunas:

- Esquerda: identificação do cartão fictício, badge de conexão, ilustração roxa, gastos demonstrativos, disponível calculado, barra de utilização, limite fictício, vencimento demonstrativo e horário da última simulação.
- Direita: explicação em três etapas — autorizar compartilhamento, sincronizar transações e visualizar informações — e chamada para experimentar a demonstração.

O cartão usa final fictício `4821`, nome fictício e dois círculos decorativos. O limite demonstrativo é R$ 5.000,00. Esses detalhes não devem ser reutilizados como dados reais ou como marca de bandeira confirmada.

Estados implementados:

| Estado | Apresentação |
|---|---|
| Não conectado | Badge “Não conectado · demo” e ação “Simular conexão” |
| Modal aberto | Aviso de que nenhuma instituição será acessada; checkbox de confirmação |
| Conexão simulada ativa | Badge verde, horário em memória e ação “Sincronizar demo” |
| Desconexão | Modal de confirmação; remove status e horário simulados |

Desconectar não remove os dados fictícios, que continuam disponíveis para explorar o protótipo. A simulação não modela os estados reais de consentimento, falha de provedor ou execução persistente.

### 6.3 Transações

**Título:** “Cada movimento conta.”

Elementos:

- Seletor de mês e botão “Exportar CSV”.
- Busca por estabelecimento, sem diferenciar maiúsculas e minúsculas.
- Filtro por categoria.
- Tabela com descrição, categoria, data, parcelamento e valor.
- Categoria editável em select por linha.
- Contagem encontrada e total monetário filtrado, ambos demonstrativos.
- Mensagem de que alterações duram somente enquanto a página estiver aberta.

O conjunto original tem 18 transações fictícias, distribuídas entre outubro e novembro de 2026. As categorias são Alimentação, Transporte, Compras, Lazer, Saúde e Educação. Não há criação de categoria, edição de observação, seleção de situação ou paginação implementadas nesse HTML.

Quando o filtro não encontra registros, a área da tabela informa: “Nenhuma transação encontrada para este filtro.” A tabela permite rolagem horizontal quando não cabe na largura disponível.

A exportação usa os registros filtrados, formato CSV com separador `;`, BOM UTF-8 e colunas Data, Descrição, Categoria, Parcela e Valor. O nome do arquivo indica dados fictícios e o mês. Esse comportamento é demonstrativo, não um contrato aprovado do endpoint de exportação.

### 6.4 Investimentos

A tela reúne indicadores da carteira fictícia, tabela de ativos, distribuição por classe e acesso ao simulador.

Ativos iniciais de demonstração: Tesouro IPCA+, CDB Reserva, ETF Brasil e Fundo Imobiliário. O modal “Adicionar investimento” solicita nome, classe, valor aplicado e valor atual. As classes disponíveis são renda fixa, renda variável e fundos imobiliários.

A variação é calculada pela diferença entre valor aplicado e valor atual informado. A tela avisa que não considera datas dos aportes, resgates, taxas ou impostos; portanto, não representa rentabilidade financeira ajustada.

No protótipo é possível adicionar um ativo durante a sessão. Não há edição, exclusão, integração com corretora ou cotação em tempo real.

### 6.5 Simulador

**Título:** “Imagine seu próximo capítulo.”

Layout desktop: formulário à esquerda e resultado à direita. Abaixo, lista de cenários guardados na sessão.

| Campo | Estado inicial | Faixa no HTML |
|---|---|---|
| Valor inicial | R$ 10.000,00 | 0 a R$ 10.000.000,00 |
| Aporte mensal | R$ 500,00 | 0 a R$ 1.000.000,00 |
| Taxa anual | 10% | 0% a 50% |
| Prazo | 5 anos | 1 a 30 anos |

O resultado mostra patrimônio projetado, total aportado, rendimento projetado e gráfico comparando patrimônio com aportes. Os campos atualizam a projeção durante a interação, respeitando a validação HTML dos inputs.

Premissas implementadas:

- Taxa anual efetiva convertida para mensal por `i_m = (1 + i_a)^(1/12) - 1`.
- Atualização mensal por `saldo seguinte = saldo atual × (1 + i_m) + aporte`.
- Aportes ao final de cada mês.
- Projeção nominal, sem impostos, taxas ou inflação; sem garantia de retorno.

“Usar valor da carteira” transfere o patrimônio fictício atual para o valor inicial da simulação. “Guardar cenário nesta sessão” adiciona uma cópia das entradas à lista comparativa. “Limpar cenários” esvazia essa lista. Não há gravação em banco nem seleção ou recomendação automática de ativos.

### 6.6 Sobre o projeto

Apresenta a proposta acadêmica: tema do TCC, problema, objetivo geral, núcleo da entrega, tecnologias propostas, roteiro original de duas semanas e limites da demonstração.

Essa tela é um registro da apresentação inicial. Seu cronograma, textos técnicos e afirmações de status não devem ser usados como fonte do estado atual da implementação.

## 7. Componentes reutilizáveis

| Componente | Anatomia e comportamento de referência |
|---|---|
| Estrutura da aplicação | Sidebar, topbar e conteúdo; adapta navegação à largura |
| Cabeçalho de página | Eyebrow, título, subtítulo e área de ações |
| Painel | Superfície branca, borda fina, cantos arredondados e cabeçalho opcional |
| Indicador | Rótulo, ícone discreto, valor principal e descrição; variante lavanda |
| Botão principal | Roxo, texto branco, ícone opcional e hover mais escuro |
| Botão secundário | Branco com borda; variante suave lavanda |
| Badge | Texto curto de estado, nunca a única forma de identificar seu significado |
| Aviso contextual | Faixa lavanda, ícone e explicação próxima ao conteúdo |
| Tabela | Cabeçalho discreto, divisórias leves, valor alinhado à direita |
| Categoria | Pill em leitura ou select compacto na tela de transações |
| Campo | Rótulo visível, input/select com borda, foco destacado |
| Modal | Overlay escuro, superfície branca, título, fechar e ações no rodapé |
| Toast | Mensagem curta, região de status, duração aproximada de 4,5 s |
| Estado vazio | Texto contextual dentro da área em que os resultados apareceriam |
| Ilustração de cartão | Gradiente roxo, identificação mascarada e detalhes decorativos |

O hover da linha de tabela apenas altera o fundo; não existe navegação ao clicar na linha. Uma implementação futura não deve sugerir que a linha é clicável se não houver ação correspondente.

## 8. Comportamentos globais

### 8.1 Privacidade visual

O botão de olho aplica desfoque de 7 px aos elementos marcados como valores privados, bloqueia sua seleção e altera seu rótulo entre “Ocultar valores” e “Mostrar valores”.

Esse recurso é conveniência visual, não proteção de dados. O conteúdo continua no DOM; valores em gráficos, descrições acessíveis, percentuais ou outras áreas podem continuar disponíveis. Não prometer ocultação completa ou confidencialidade com esse mecanismo. Na aplicação final, revisar todos os canais de exposição antes de definir seu comportamento.

### 8.2 Modais e feedback

- Abrir um modal bloqueia a rolagem do fundo e direciona foco ao botão de fechar.
- `Tab` e `Shift+Tab` circulam pelos elementos focáveis do modal.
- `Escape` fecha o modal; ao fechar, o foco volta ao acionador quando ele ainda existe.
- Clicar no fundo não tem fechamento implementado.
- A conexão simulada exige marcar a confirmação; caso contrário, exibe toast e foca o checkbox.
- Ações como exportar, alterar categoria, adicionar ativo e guardar cenário geram feedback textual.

### 8.3 Movimento e impressão

A troca de tela usa animação breve de aproximadamente 250 ms, com opacidade e deslocamento vertical de 4 px. Não existe tratamento específico de `prefers-reduced-motion` no CSS original.

Há estilos de impressão que ocultam navegação, cabeçalho e controles, mantêm os avisos e tentam evitar quebra dentro de painéis. Existe tratamento JavaScript para uma ação de impressão, mas não há botão visível correspondente nas seis telas principais. O CSS é a referência disponível, não evidência de um fluxo completo de impressão validado.

## 9. Responsividade

Breakpoints extraídos do CSS, sem convertê-los automaticamente aos nomes padrão do Tailwind:

| Condição | Comportamento |
|---|---|
| A partir de 1.550 px | Aumenta padding de conteúdo, painéis e indicadores; cartão mais alto |
| Até 1.180 px | Sidebar passa a 205 px; reduz espaçamentos e reequilibra colunas |
| Até 900 px | Sidebar compacta de 76 px com ícones; indicadores em duas colunas; painéis e formulários principais empilhados |
| Até 560 px | Navegação inferior fixa de 65 px; remove deslocamento lateral; marca no topo; conteúdo em coluna; filtros empilhados |

No mobile:

- Indicadores permanecem em duas colunas, com valores de 20 px.
- A tabela usa rolagem horizontal, em vez de converter linhas em cartões.
- Ações ficam abaixo do título.
- Modal mantém largura máxima da viewport, margem externa e rolagem interna.
- Toast passa para o centro horizontal, acima da navegação inferior.
- O item “Sobre o projeto” é ocultado na barra inferior sem alternativa equivalente implementada; corrigir essa lacuna caso a tela permaneça na aplicação.

Não há menu hambúrguer ou drawer implementado. Não descrevê-los como parte do protótipo original.

## 10. Estados existentes e estados a projetar

| Estado | Situação no protótipo |
|---|---|
| Navegação ativa | Implementado |
| Filtro com/sem resultados | Implementado |
| Conexão demo ativa/inativa | Implementado |
| Modal com confirmação pendente | Implementado |
| Validação básica de formulário | Implementada com recursos HTML e verificações locais |
| Carteira sem novos ativos | Usa exemplos iniciais; não é um carregamento remoto |
| Cenários vazios/preenchidos | Implementado |
| Valores visualmente ocultos | Implementado parcialmente por desfoque |
| Carregamento HTTP / skeleton | Não implementado |
| Erro de API, timeout ou indisponibilidade | Não implementado |
| Sessão expirada / login / logout | Não implementado |
| Importação aceita, pendente, executando ou falha | Não implementado |
| Consentimento expirado ou conexão ambígua | Não implementado |
| Cobertura parcial e contadores desconhecidos | Não implementado como contrato real |

Na versão integrada, novos estados devem vir de contratos aprovados. Não inventar progresso percentual nem usar dados demonstrativos para preencher erros, ausência ou carregamento.

## 11. Acessibilidade

Recursos presentes no HTML:

- Região `main`, navegação com nome acessível e identificação do item ativo.
- Botões e campos com labels ou nomes acessíveis nos controles principais.
- Contorno de foco para botões/links e estilo de foco de inputs.
- Modal com `role="dialog"`, `aria-modal`, título associado e controle de foco.
- Região de toast com `aria-live="polite"` e mensagens com papel de status.
- Gráficos com descrição textual por atributos acessíveis e alguns valores em elementos `title`.

Cuidados para a aplicação definitiva:

- Verificar contraste e ampliar textos auxiliares quando necessário.
- Garantir áreas de toque adequadas; alguns botões pequenos da referência precisam de revisão.
- Fornecer resumo textual/tabela equivalente para cada gráfico autorizado.
- Usar texto e ícone além de cor para comunicar estados.
- Respeitar redução de movimento e testar zoom e teclado.
- Revisar a ocultação visual para não manter valores anunciados indevidamente por leitores de tela.
- Não esconder uma tela no mobile sem outro caminho de acesso, quando ela for necessária.

Esses cuidados são critérios de adaptação, não resultados de auditoria ou testes de acessibilidade já executados.

## 12. Organização sugerida no Angular e Tailwind

A aplicação definitiva pode conservar a aparência usando componentes pequenos e templates HTML com Tailwind. Os nomes abaixo são sugestões, não arquivos já implementados nem uma nova decisão de arquitetura:

| Área | Componentes sugeridos |
|---|---|
| Estrutura | `AppShell`, `SidebarNavigation`, `MobileNavigation`, `TopBar` |
| Base visual | `PageHeader`, `Panel`, `StatusBadge`, `Notice`, `EmptyState`, `Toast` |
| Dados | `MovementTable`, `MovementFilters`, `CategorySelector`, `CoverageSummary` |
| Cartão/conexão | `CardSummary`, `ConnectionStatus`, `SyncRunStatus` |
| Formulários | `FormField`, `ConfirmationDialog` |
| Complementos futuros | `InvestmentTable`, `SimulationForm`, `SimulationResult`, `ScenarioList` |

Orientações:

1. Centralizar cores, tipografia, raios e espaçamentos recorrentes em tokens reutilizáveis.
2. Manter validações e estado de carregamento próximos ao componente responsável, sem duplicar regras do backend.
3. Reaproveitar a mesma lista de movimentos em navegação e visão geral, evitando duas implementações concorrentes.
4. Usar o contrato real da API para identidade, autorização, dados e estados; não transportar os arrays fictícios para o fluxo real.
5. Na aplicação final, gráficos autorizados podem usar Chart.js conforme a escolha do projeto. O protótipo original usa SVG inline e gradiente CSS, não Chart.js.
6. Não copiar a arquitetura de arquivo único, manipulação direta de `innerHTML` e listeners globais do HTML como estrutura da aplicação Angular.
7. Aplicar as regras de comentários e desenvolvimento do `AGENTS.md` do repositório ao implementar.

## 13. Adaptação obrigatória ao MVP revisado

O protótipo foi criado antes da revisão do escopo financeiro. Sua estética pode orientar o frontend; seus indicadores não autorizam cálculos que o PRD atual adiou.

| Elemento original | Tratamento no MVP revisado |
|---|---|
| Sidebar, topbar, painéis e linguagem visual | Reaproveitar, ajustando navegação às funcionalidades entregues |
| “Saldo do mês” e “Receitas do mês” fictícios | Não apresentar como indicadores reais derivados dos movimentos |
| Gastos totais, subtotal filtrado, médias e gráficos monetários | Retirar da visão real de movimentos enquanto estiverem fora do escopo aprovado |
| Rosca monetária por categoria e histórico de gastos | Substituir, quando aprovado, por contagens descritivas com universo e cobertura explícitos |
| Tabela de transações | Evoluir para movimentos com valor individual, moeda, data identificada, natureza/motivo e situação separados |
| Texto que chama qualquer movimento de compra | Usar “movimento” ou “lançamento” quando não houver natureza comprovada |
| `PAGAMENTO` | Preservar como natureza desconhecida segundo a regra conservadora vigente |
| Seleção de categoria | Manter organização pessoal sem alterar natureza financeira ou situação; incorporar observação conforme contrato próprio |
| Ausência de metadados | Informar “não disponível”; não preencher com zero ou valor fictício |
| Data exibida | Identificar a origem/regra de normalização; não chamar de competência ou ciclo da fatura sem validação |
| Modal de autorização bancária simulada | Manter somente na demonstração; no fluxo real, seguir a configuração/autorização de integração aprovada |
| Botão de sincronização | Exibir aceitação e acompanhar a execução persistida; `202` não significa dados atualizados |
| Cobertura e última atualização | Mostrar estados verdadeiros da importação, sem confundir página da lista com histórico integral |
| Limite, disponível e vencimento fictícios | Não calcular a partir de somas proibidas nem inventar; expor apenas dados e contratos efetivamente aprovados |
| Investimentos, simulador e CSV | Entregas próprias, conforme backlog; a presença na referência não confirma disponibilidade no MVP |
| Recorrência assistida | Não existe no HTML original; planejar em sua mudança própria, sem inferir compra/competência |
| Login | Não existe no HTML original; a tela deve ser desenhada para a autenticação já entregue, sem cadastro público |

Contagens descritivas podem incluir movimentos por situação, natureza e categoria conforme requisitos aprovados, mas devem declarar o conjunto considerado. Quantidade zero comprovada e informação desconhecida são estados distintos.

A confirmação pessoal de assinatura ou parcela também não libera agregados monetários, não reclassifica automaticamente a natureza da origem e não altera status. Para recorrência, preservar a separação entre sugestão, decisão do titular e metadados de parcelamento.

## 14. Checklist de revisão visual da implementação

- [ ] Identidade roxa/lavanda, superfícies claras e navegação escura preservadas.
- [ ] Hierarquia de títulos, valores individuais e textos auxiliares legível.
- [ ] Componentes compartilhados evitam duplicação de listas e estados.
- [ ] Layout conferido em desktop, faixa intermediária e mobile, incluindo 560 e 900 px.
- [ ] Tabelas mantêm leitura e rolagem previsíveis em telas pequenas.
- [ ] Todos os caminhos necessários continuam acessíveis no mobile.
- [ ] Navegação por teclado, foco de modal e retorno ao acionador verificados.
- [ ] Contraste, zoom, redução de movimento e áreas de toque avaliados.
- [ ] Carregamento, vazio, erro, indisponibilidade e sessão expirada têm apresentação própria.
- [ ] Dados desconhecidos e cobertura parcial permanecem explícitos.
- [ ] Não há somas ou gráficos monetários de movimentos fora do escopo aprovado.
- [ ] Datas, natureza, situação e categoria pessoal permanecem dimensões distintas.
- [ ] Nenhum dado fictício aparece como fallback de uma consulta real.
- [ ] Tokens, credenciais, IDs externos e dados sensíveis não aparecem em exemplos ou mensagens indevidas.
- [ ] Aprovação visual é registrada separadamente de validação funcional e integração Git.

## 15. Referência e manutenção

O arquivo original `Finora-prototipo.html` é a fonte da aparência e das interações documentadas aqui, mas não foi localizado neste repositório. Quando sua localização for definida, registrar o caminho verificável e acrescentar um link válido; até lá, usar esta descrição sem afirmar que o HTML está disponível em `docs/`.

Este documento descreve uma referência histórica e recomendações claramente identificadas. A evolução do produto deve continuar rastreada nos documentos canônicos e no OpenSpec; não atualizar status de funcionalidades apenas porque elas aparecem em uma tela demonstrativa.
