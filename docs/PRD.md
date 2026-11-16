# Finora — PRD v0

**Documento de requisitos do produto**  
**Versão:** 0.4 — **aprovada documentalmente pelo titular em 16/11/2026**; encerramento revisado da FNR-005 aceito nos limites registrados. Aprovação da revisão 0.3 preservada abaixo.\
**Data:** 15 de novembro de 2026 (definição inicial em 9 de novembro)\
**Contexto:** TCC individual; ideia aprovada pelo professor sem alterações, conforme relato do aluno.  
**Prazo de referência:** duas semanas de desenvolvimento; data final de entrega a confirmar.  
**Responsável pelo produto e desenvolvimento:** aluno autor do TCC.

**Origem e aprovação da revisão 0.4:** o titular aprovou a direção de substituir o objetivo pendente da FNR-005/5.3 no MVP por movimentos descritivos e recorrência assistida. O conteúdo final deste documento foi aprovado explicitamente no harness em 16/11/2026, e o titular aceitou o encerramento revisado da FNR-005 nos limites registrados. Isso não atende tecnicamente o objetivo antigo nem autoriza implementação, nova consulta real, commits, PR, sync ou arquivamento. O explorador reutiliza a [mudança existente](../openspec/changes/explorador-movimentos-cartao-sem-total/proposal.md); recorrência será especificada em mudança própria, ainda não criada.

**Origem da revisão 0.3 — preservada:** pedido do autor, nesta conversa em 13/11/2026, para alinhamento às skills atuais `generate-prd` e `generate-tdd`. Escopo exclusivamente documental; elaboração assistida. O pedido posterior de aplicação autorizava preparar aquela revisão, não a aprovava nem autorizava código ou integração.

**Aprovação da revisão 0.3 — preservada:** após a entrega do diff, o autor confirmou pelo harness a opção “Registrar minha aprovação” para PRD 0.3, TDD 0.1 e SDD, em 13/11/2026. Confirmou também os registros documentais correspondentes. Essa manifestação posterior aprova aqueles documentos com suas propostas e pendências explícitas; não comprova medições, não fecha decisões técnicas pendentes e não autoriza código, PR, merge ou arquivamento. Não se transfere à revisão 0.4.

### Histórico de revisões

| Revisão | Data | Alteração e estado |
|---|---|---|
| 0.2 — FNR-001 | 13/11/2026 | Alinhamento documental aprovado pelo autor; PR #1 integrado em `075af73`. Evidências e limites preservados no §15 e no [backlog](BACKLOG.md), §7. A aprovação continua restrita àquela revisão. |
| 0.3 | 13/11/2026 | IDs dos RNFs, métricas e encaminhamento das pendências; decisões e RF01–RF16 preservados. Entregue em revisão e posteriormente **aprovada documentalmente pelo autor**, por confirmação explícita no harness. Propostas, baselines desconhecidos e validações continuam nos estados indicados. Preparação registrada na [proposta](../openspec/changes/archive/2026-11-13-alinhar-prd-tdd-skills/proposal.md) e no [SDD](../openspec/changes/archive/2026-11-13-alinhar-prd-tdd-skills/design.md). |
| 0.4 | 15/11/2026 | **Aprovada documentalmente em 16/11/2026; encerramento revisado FNR-005 aceito.** MVP descritivo sem agregados monetários de movimentos; RF04–RF07/RF15 revisados e RF17 de recorrência assistida em mudança própria. Objetivos financeiros anteriores adiados; FNR-005/5.3 substituída por revisão de escopo, nunca atendida tecnicamente. PR #12, coletas e aprovações anteriores preservados. |

O restante do documento descreve o produto pretendido, não funcionalidades já implementadas. Nenhuma aprovação histórica se transfere às adições desta revisão.

## 1. Visão do produto

O Finora é uma aplicação web de finanças pessoais que centraliza movimentos de cartão de crédito para consulta e organização pelo titular. O MVP revisado apresenta valores individuais, natureza/situação identificáveis, cobertura e contagens descritivas; prevê detecção assistida de recorrência em entrega própria. Indicadores financeiros de gastos permanecem como objetivo posterior, condicionado à validação de compra e semântica temporal. O foco inicial é o cartão Nubank do próprio autor, com dados obtidos por um provedor de integração bancária.

O produto também contempla uma carteira de investimentos com valores informados manualmente e um simulador de aportes. Esses módulos são complementares e serão implementados após o funcionamento e aceite do núcleo revisado, sem antecipar P1.

**Resultado principal esperado:** o usuário consulta e organiza os movimentos reais disponíveis, entende a data de origem, os limites da cobertura e quando foram importados, e pode confirmar sugestões de recorrência quando essa entrega estiver pronta. Natureza desconhecida não é apresentada como compra e data de origem não é anunciada como competência financeira validada.

**Título provisório do TCC:** Desenvolvimento de um dashboard de finanças pessoais com integração de dados de cartão de crédito.

## 2. Problema

O usuário precisa acompanhar as compras do cartão e compreender sua distribuição ao longo do mês. A consulta isolada de lançamentos dificulta a leitura por categoria e a combinação dessas informações com o planejamento de investimentos.

O Finora pretende reduzir o trabalho de transcrever movimentos manualmente e oferecer uma visão organizada dos dados disponíveis, preservando a distinção entre natureza financeira, situação e recorrência. O MVP não promete transformar ambiguidades da origem em gastos confirmados; os indicadores financeiros pretendidos continuam adiados, não abandonados nem declarados validados.

**Pergunta orientadora:** como uma aplicação web pode apoiar o controle financeiro pessoal ao reunir dados de cartão de crédito, indicadores de gastos e recursos de planejamento em uma única interface?

## 3. Usuário e contexto de uso

- **Usuário inicial:** o próprio autor do TCC, titular dos dados conectados.
- **Uso principal:** consultar e filtrar movimentos do cartão, organizar categorias/observações e confirmar candidatos a recorrência na entrega própria. Isso não inclui editar a natureza financeira da origem.
- **Uso complementar:** acompanhar valores de investimentos e explorar cenários de aportes.
- **Validação inicial:** prova de conceito local para ler transações reais pelo backend, sem exposição pública, antes do dashboard.
- **Ambiente de entrega:** aplicação web hospedada, com acesso autenticado restrito ao titular; desenvolvimento e validações locais separados do ambiente hospedado.
- **Apresentação acadêmica:** usar dados fictícios ou anonimizados por padrão; dados pessoais reais não devem compor material público.

A primeira versão não atenderá contas de terceiros. A conexão de múltiplos usuários e a oferta comercial ficam fora do escopo.

### Stakeholders conhecidos

| Interessado | Necessidade / participação conhecida | Limite da evidência |
|---|---|---|
| Autor do TCC / titular | Responsável pelo produto e desenvolvimento; precisa consultar e organizar suas compras e demonstrar o trabalho com privacidade. | As necessidades vêm da conversa de definição do produto, não de pesquisa com múltiplos usuários. |
| Professor | Interessado na avaliação acadêmica e na proposta do TCC. | Aprovação da ideia/protótipo relatada pelo aluno; não comprova aprovação das revisões atuais nem define prazo final. |

Não há entrevista registrada em `docs/entrevista.md` nas fontes disponíveis desta revisão. Provedores são dependências externas (§11), não aprovadores do produto. Redução de esforço é resultado pretendido, ainda sem medição.

## 4. Situação atual e evidências

| Item | Situação |
|---|---|
| Ideia e protótipo | Aprovados pelo professor, conforme relato do aluno |
| Protótipo visual | Disponível com navegação e dados fictícios |
| Meu Pluggy | Conexão realizada pelo usuário |
| Cartão e lançamentos no painel do provedor | Visualizados nas capturas enviadas pelo usuário |
| Credenciais | Usuário informou ter guardado Client ID, Client Secret e itemId |
| Consulta pelo backend .NET | FNR-004 demonstrou leitura real local limitada a uma página; evidência no backlog e em AMBIENTE.md. Não comprova importação persistente. |
| Contrato técnico conservador — FNR-005 | Incremento integrado pelo [PR #12](https://github.com/VictorJer/GestFinan-a/pull/12), merge `adee6998342722e905a1db98ed1423620ce77ee4`. Preserva análises sanitizadas e regras conservadoras; não valida regra positiva de compra ou competência. |
| Objetivo original FNR-005/5.3 | Substituído por revisão de escopo, não tecnicamente atendido. O titular aprovou o conteúdo revisado e aceitou o encerramento técnico conservador em 16/11/2026; compra/competência e indicadores financeiros continuam não validados e adiados. |
| Persistência, explorador e recorrência assistida | Ainda não implementados; proposta do explorador reaproveitada, recorrência a especificar em mudança própria. |
| Indicadores financeiros de gastos | Adiados até validação própria; não estão entregues pelo incremento técnico ou pelo planejamento descritivo. |

A visualização no painel do provedor não comprova, por si só, a leitura pela aplicação ou a disponibilidade de todos os campos desejados. As provas locais posteriores e seus limites estão em [AMBIENTE.md](AMBIENTE.md) e [PLUGGY-CONTRACT.md](PLUGGY-CONTRACT.md). Preservam-se as observações históricas de uma página e o resultado inconclusivo do parser antigo de `billForecastDate`; nenhuma coleta ou reclassificação foi feita nesta revisão. O PR #12 consultado não reporta reviews formais ou checks; ausência não equivale a aprovação.

## 5. Objetivos e prioridades

**P0 — obrigatório para a primeira entrega:**

1. Consultar o cartão e suas transações pela integração no backend.
2. Persistir os dados e sincronizar novamente sem criar duplicações.
3. Listar, pesquisar, filtrar e categorizar movimentos, sem inferir compra a partir da categoria escolhida.
4. Exibir cobertura e contagens descritivas coerentes com o universo consultado, sem somas ou gráficos monetários de movimentos.
5. Informar atualização, falhas e necessidade de renovação da conexão.
6. Proteger o acesso do titular por login, sem cadastro público, e permitir organizar movimentos com categorias pessoais e observações.
7. Entregar recorrência assistida em mudança própria, após suas dependências e decisões pendentes: sugestão de candidato e confirmação pelo titular, sem reconhecimento automático de compra.

**P1 — complementos previstos, após P0:**

1. Cadastrar e acompanhar investimentos manualmente.
2. Simular evolução de patrimônio com aportes periódicos.
3. Exportar as transações filtradas em CSV.

**P2 — evolução futura:** conexão de outros usuários e instituições, cotações automáticas, integração automática de investimentos, recomendações e recursos avançados de orçamento.

O protótipo é referência de experiência e apresentação. Seus indicadores fictícios não são requisitos automáticos: receitas e saldo financeiro consolidado, por exemplo, exigiriam fontes adicionais que não integram o P0.

### Métricas de sucesso e método de medição

Os métodos abaixo são propostas de verificação, não resultados novos desta revisão. As coletas históricas têm evidência própria e limitada em AMBIENTE.md; não comprovam métricas do produto integrado, desempenho de consultas ao banco ou ganho de produtividade. Critérios anteriores não atendidos são preservados como adiados, nunca convertidos em resultados por revisão de escopo. Não se pressupõe telemetria automática ou envio de dados financeiros para serviços analíticos.

| ID / objetivo | Indicador e método proposto | Baseline | Meta ou critério de referência | Encaminhamento / estado |
|---|---|---|---|---|
| M-01 — integração utilizável | Distinguir prova local, páginas processadas e cobertura observada da futura importação persistente, com evidência sanitizada. | Provas locais limitadas registradas em AMBIENTE.md; produto integrado não medido | RF01/RF02; não prometer 12 meses disponíveis nem semântica financeira validada. | FNR-004 e incremento técnico FNR-005 integrado pelo PR #12; importação FNR-012/013 ainda pendente. Sem nova coleta nesta revisão. |
| M-02 — integridade | Comparar IDs, quantidade de registros e anotações antes/depois de duas importações iguais e de uma atualização controlada. Registrar diferenças indevidas. | Não medido | RF03/RF06/RF15: sem duplicações indevidas ou perda de anotações; alterações legítimas da origem conciliadas. | Autor sugerido; FNR-012/017/024. Pendente. |
| M-03 — consistência descritiva | Comparar lista, filtros, contagens de todo conjunto e cobertura em dados controlados; verificar ausência de qualquer agregado monetário em API/UI. | Não medido | RF04/RF05/RF07: contagens e estados correspondem ao universo declarado; incompleto não aparece como completo. | FNR-015/018/022/023/028. Critério original de conciliação financeira preservado como adiado abaixo. |
| M-04 — acesso e falhas compreensíveis | Registrar resultado por cenário de login, recarga, expiração, logout/reset, falha parcial/externa e atualização antiga; observar mensagens e preservação do histórico. | Não medido | RF08/RF09/RF14/RF16 e §13, sem taxa numérica de sucesso inventada. | Autor sugerido; FNR-010/013/014/020/021/028. Pendente. |
| M-05 — desempenho local | Medir duração das consultas da interface ao banco com até 5.000 transações fictícias, identificando ambiente, volume e filtros; medir API externa separadamente. Amostra, ferramenta e estatística a definir antes da execução. | Não medido | Meta local já existente de RNF-08: até 2 segundos. Não define percentil nem SLA hospedado. | Autor sugerido; propor roteiro em FNR-024 e validar em FNR-028. Protocolo pendente (§15, VP-05). |
| M-06 — esforço do titular | Proposta: comparar tempo e intervenções manuais para uma tarefa equivalente de organizar compras com e sem Finora, em dados seguros e condições registradas. | A definir por medição | A definir; não há redução percentual ou duração prometida. | Autor sugerido, com orientação acadêmica se pertinente; validar utilidade/método antes da avaliação no TCC. Pendente (§15, VP-05). |

**M-03 anterior — adiada, não atendida:** comparar total filtrado de todas as páginas e dashboard com cálculo independente de um conjunto controlado, mesmos filtros, situações e BRL, registrando diferenças e casos elegíveis. Esse objetivo depende da futura validação financeira; consistência de contagens não o satisfaz. Para RF17, o plano de testes deverá distinguir candidatos, confirmações e falsos positivos, com tolerâncias explicitadas antes da implementação; não há taxa de acerto prometida ou medida.

## 6. Jornada principal

1. O usuário autoriza o Nubank no Meu Pluggy e disponibiliza essa conexão à aplicação no provedor.
2. As credenciais e o identificador da conexão são configurados de forma protegida no backend do ambiente utilizado.
3. No MVP integrado, o titular entra com e-mail e senha; o backend autentica-se no provedor, consulta as contas e identifica o cartão.
4. O sistema importa as transações disponíveis e persiste os dados.
5. O usuário abre a visão descritiva, escolhe o período da data de origem explicitada e consulta movimentos, cobertura e contagens, sem total de gasto.
6. Na lista, pesquisa um movimento, escolhe uma categoria inicial ou cria uma pessoal e registra uma observação; filtros/contagens descritivas refletem a organização, sem mudar a natureza financeira.
7. Uma nova consulta atualiza os registros sem duplicá-los e preserva a categoria e a observação escolhidas pelo usuário.
8. Na entrega própria de recorrência, o backend sugere um candidato após padrão em três meses consecutivos; o titular confirma assinatura, parcela ou compras independentes. Essa confirmação fica separada dos dados de origem e não transforma uma pendência em gasto efetivado.

No P0, o fluxo de autorização acontece no ambiente do provedor. Um botão que inicia a conexão bancária dentro do Angular não é obrigatório. A aplicação deve explicar como configurar e renovar a conexão.

### Alternativas e recuperação na jornada

| Situação | Experiência já prevista / critério relacionado |
|---|---|
| Sessão perdida ao recarregar ou expirada | Orientar novo login; recuperação administrativa de acesso, sem cadastro público ou e-mail no MVP (RF14). |
| Filtro sem registros ou tela carregando | Exibir o estado correspondente; ausência de registros filtrados não prova ausência de histórico na origem (RF05/RF09; §8). |
| Falha, importação incompleta ou autorização expirada | Preservar histórico, informar falha/desatualização e orientar renovação no provedor, sem simular sucesso ou zerar gastos (RF08/RF09/RF16). |
| Histórico/campos indisponíveis | Informar cobertura e campos não disponíveis; não preencher com dados fictícios nem concluir integração real por demonstração (RF02/RF04; §8). |
| Categoria ou observação alterada | Refletir a organização pessoal nos filtros/contagens descritivas e preservá-la após nova importação, sem confirmar natureza financeira (RF06/RF15). |
| Sugestão de recorrência não corresponde ao caso | Permitir confirmar compras independentes, sem converter o padrão em assinatura/parcela nem alterar dados originais, situação ou competência (RF17). |

## 7. Requisitos funcionais e critérios de aceite

| ID | Prioridade | Requisito | Critério de aceite |
|---|---|---|---|
| RF01 | P0 | Consultar a conexão e o cartão | Com credenciais válidas, o backend identifica o cartão e recupera seus lançamentos disponíveis; segredos não são retornados ao frontend. |
| RF02 | P0 | Importar transações | Na primeira importação, solicitar até 12 meses anteriores, limitado à disponibilidade do provedor, preservando parcelas futuras retornadas em situação separada. Todas as páginas do intervalo solicitado são processadas. Valores, moeda, identificadores, datas, tipo e status disponíveis são preservados; limitações de cobertura são informadas. |
| RF03 | P0 | Atualizar sem duplicação | Executar duas consultas sobre os mesmos dados não aumenta a quantidade de registros indevidamente. Mudanças em registros existentes são atualizadas. |
| RF04 | P0 | Exibir movimentos | A lista mostra descrição disponível, data de origem explicitada, valor individual, moeda, categoria pessoal, natureza/motivo e situação separados, com metadados de parcelamento quando informados. Desconhecidos permanecem visíveis; cobertura e completude são informadas. Ordenação estável pela data mais recente. |
| RF05 | P0 | Pesquisar e filtrar | Busca por descrição e filtros de período da data explicitada, categoria e situação funcionam em conjunto. Lista e contagem de movimentos consideram o mesmo conjunto filtrado, não só a página; não há total monetário. |
| RF06 | P0 | Categorizar movimentos | O titular pode escolher categorias iniciais ou criar categorias pessoais, com nome validado e sem duplicidade. A categoria manual é persistida, utilizável nos filtros/contagens descritivas e preservada após nova consulta; não confirma compra, efetivação ou competência. |
| RF07 | P0 | Apresentar visão descritiva | Exibir cobertura, completude e contagens não monetárias de movimentos por natureza/situação, com universo declarado. Não calcular, retornar ou apresentar somas, subtotais, médias, séries ou gráficos monetários de movimentos, mesmo com rótulo parcial/informativo. Objetivo financeiro anterior adiado conforme §8. |
| RF08 | P0 | Exibir saúde da consulta | Mostrar data da última consulta bem-sucedida e da atualização na origem, quando disponível. Erros não apagam dados já importados nem simulam sucesso. |
| RF09 | P0 | Tratar estados da interface | Cada tela de dados possui carregamento, ausência de registros e erro com orientação de recuperação. |
| RF10 | P1 | Cadastrar investimentos | Permitir criar, editar e excluir registros manuais de nome, classe, valor aplicado e valor atual; dados persistem após recarregar a aplicação. |
| RF11 | P1 | Resumir carteira | Total aplicado, valor atual, diferença e distribuição por classe correspondem aos registros cadastrados. |
| RF12 | P1 | Simular investimentos | Alterar valor inicial, aporte, taxa anual e prazo atualiza projeção, total aportado e rendimento projetado, sem alterar a carteira real. |
| RF13 | P1 | Exportar CSV | O arquivo contém somente as transações filtradas, com cabeçalhos, datas e valores consistentes com a interface. |
| RF14 | P0 | Controlar acesso do titular | Login por e-mail e senha, sem cadastro público. Dados financeiros só são acessíveis ao titular autenticado. A sessão dura 30 minutos, sem renovação automática; recarregar ou expirar exige novo login. Logout confirmado invalida a sessão atual; redefinição administrativa de senha invalida todas as sessões. Criação e recuperação de acesso são administrativas, sem envio de e-mail no MVP. |
| RF15 | P0 | Anotar movimentos | Permitir editar e limpar observação pessoal em texto simples de até 500 caracteres, preservada após sincronização. A anotação altera somente categoria e observação; valor, data, descrição, natureza e situação da origem não são editados. A confirmação de recorrência de RF17 é um registro separado, não classificação manual geral da natureza financeira. |
| RF16 | P0 | Atualizar dados disponíveis | Ao entrar autenticado, solicitar importação se nunca houve sucesso ou se a última importação completa bem-sucedida tiver mais de 24 horas, com intervalo configurável. Permitir atualização manual; navegar e filtrar não importam dados. Falhas ou execuções incompletas não renovam o horário de sucesso. |
| RF17 | P0 — entrega própria planejada | Sugerir e confirmar recorrência | Após padrão em três meses consecutivos, sugerir possível recorrência para confirmação pelo titular como assinatura, parcela ou compras independentes. Valor igual sozinho não basta. Metadados de parcelamento da origem são tratados separadamente. Tolerâncias de valor, datas e descrição são decisões pendentes obrigatórias antes da implementação; não há detector ou confirmação implementados nesta revisão. |

## 8. Regras de negócio

### Movimentos descritivos — MVP revisado

- A visão inicial descreve **movimentos disponíveis**, não gasto total. Somente valores individuais e contagens não monetárias com universo/cobertura explícitos podem aparecer. A proibição de agregar movimentos vale no backend e no frontend, independentemente do rótulo ou da moeda. Não apresentar saldo bancário, renda ou patrimônio líquido derivados desses movimentos.
- Natureza, situação, categoria pessoal e recorrência são dimensões distintas. `PAGAMENTO` permanece desconhecido sob a regra conservadora atual; `PAGAMENTO_FATURA` é separado quando seus demais sinais forem coerentes. Não reconhecer compra automaticamente por descrição, sinal, categoria pessoal ou confirmação de recorrência.
- Juros, tarifas e ajustes devem ser classificados separadamente quando identificáveis. Lançamentos de natureza desconhecida ficam sinalizados para revisão, sem serem tratados silenciosamente como compras.
- A normalização de sinal, tipo e status deve ser validada com exemplos reais da API; não se deve interpretar o valor isoladamente.
- Lançamentos pendentes e parcelas futuras aparecem em situações distintas quando identificáveis pelas regras conservadoras; ausência ou contradição é explicitada, sem fabricar parcelas ou efetivação. Não há indicador de gastos efetivados no MVP revisado.
- Filtrar por mês usa a data de origem ou sua normalização técnica explicitada. **Mês da data exibida não equivale a ciclo de fatura ou competência financeira.** Preservar o campo original; não promover `billForecastDate` a competência.
- Faturas, limite e vencimento somente serão exibidos se houver dados confiáveis retornados pela integração. Na ausência, usar “Não disponível”; nunca reutilizar os valores fictícios do protótipo.
- Toda moeda recebida permanece identificada por movimento; não somar moedas nem converter valores para produzir totais. Conversão cambial fica fora do escopo.
- Categorias iniciais: Alimentação, Transporte, Compras, Lazer, Saúde, Educação, Outros e Sem categoria.
- O titular pode criar categorias pessoais e aplicá-las a movimentos existentes, inclusive de natureza desconhecida. Criar ou aplicar uma categoria não reclassifica a natureza financeira; renomear, arquivar ou excluir categorias fica fora do MVP.
- Categoria e observação pessoal são independentes dos dados da origem e não são apagadas por uma nova consulta.
- Uma categoria automática recebida do provedor é auxiliar. A escolha manual de categoria tem precedência e o sistema deve funcionar sem enriquecimento pago. Isso não autoriza edição manual geral da natureza financeira.

### Objetivos financeiros anteriores — adiados, não atendidos

O critério original de RF07 na revisão 0.3 era: **“Exibir gastos efetivados líquidos, quantidade de compras, distribuição por categoria e evolução por mês. Totais coincidem com os registros elegíveis.”** RF05 também exigia lista e total filtrado sobre o mesmo conjunto. Esses objetivos permanecem rastreáveis, mas não integram o aceite do MVP revisado e não são satisfeitos por contagens descritivas.

- Reconhecimento automático de compras depende de regra discriminatória sustentada por evidência e revisão própria. A confirmação local de um caso não é regra universal.
- A futura apuração de gastos líquidos exige compras efetivadas menos estornos com vínculo confiável. Pagamentos de fatura não são novas compras nem abatimento duplicado; encargos/ajustes ficam separados.
- Indicadores por competência dependem de confirmação da semântica temporal. BRL será a moeda dos futuros agregados de gasto; não haverá conversão inventada.
- O objetivo original de FNR-005/5.3 é **substituído por revisão de escopo — não atendido tecnicamente**. Seu texto, evidências e aprovações anteriores devem permanecer nos artefatos históricos; o encerramento revisado exige alinhamento, validação documental e aceite explícito, não implementação do explorador/recorrência ou nova prova financeira.

### Recorrência assistida — mudança própria

- O backend procurará padrões em três meses consecutivos de histórico observado e sugerirá **possível recorrência**, não assinatura/parcela automaticamente. O usuário confirma assinatura, parcela ou compras independentes.
- Duplicatas da mesma transação e parcelas apenas previstas não constituem três cobranças mensais observadas. Valor igual isoladamente é insuficiente; critérios de correspondência e tolerâncias de valor, datas e descrição devem ser definidos e aprovados na mudança própria antes de implementar.
- `paymentType`, numeração e total de parcelas fornecidos pela origem são evidência separada de parcelamento; não precisam ser substituídos pela heurística temporal. Ausências e incoerências permanecem explícitas.
- A confirmação do usuário deve ser preservada separadamente dos dados da origem nas sincronizações. Não altera valor, data, natureza financeira, situação ou competência e não libera indicadores adiados.

### Sincronização

- A consulta importa o que o provedor já disponibilizou. Não significa atualização instantânea no Nubank.
- O botão deve comunicar essa ação como “Atualizar dados disponíveis”, com horários de referência.
- A atualização ocorre na entrada autenticada quando os dados estão antigos, conforme RF16, ou por ação manual. Não há rotina agendada com a aplicação fechada no MVP.
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

| ID | Área | Requisito |
|---|---|---|
| RNF-01 | Credenciais | Manter Client Secret e chaves da API no backend, usando configuração local de segredos ou variáveis de ambiente. Não versionar, registrar em logs ou enviar ao Angular. |
| RNF-02 | Privacidade | Persistir somente os dados necessários. Não coletar senha, token bancário, CPF ou identidade completa para este escopo. Dados reais não entram no repositório nem no conjunto de demonstração. |
| RNF-03 | Acesso | MVP hospedado restrito ao titular, com autenticação e autorização antes de receber dados reais. A prova de conceito inicial é local e não pode expor dados publicamente. A tela de login pública não torna os dados financeiros públicos. |
| RNF-04 | Persistência | Reiniciar a aplicação não perde transações, categorias manuais ou investimentos cadastrados. |
| RNF-05 | Integridade | Usar precisão decimal para dinheiro, restrições de unicidade e operações de banco consistentes para importação. |
| RNF-06 | Uso | Interface em português, formato monetário brasileiro, datas claras, navegação por teclado e identificação textual dos estados. |
| RNF-07 | Responsividade | Dashboard e filtros utilizáveis em desktop e celular; tabelas podem oferecer rolagem horizontal. |
| RNF-08 | Desempenho | Meta local: consultas da interface ao banco em até 2 segundos para até 5.000 transações. Tempo da API externa deve ser medido separadamente e não é garantido. |
| RNF-09 | Falhas externas | Timeout, mensagens compreensíveis e tentativas limitadas. Respeitar limites de requisição do provedor. |
| RNF-10 | Operação | Exibir logs técnicos sem segredos ou conteúdo financeiro completo e disponibilizar instruções de execução local e acesso ao ambiente hospedado, com limitações conhecidas. |

Os IDs apenas identificam requisitos já existentes. Encaminhamento proposto de verificação: inspeção de configuração/logs e demonstração para RNF-01/02/10; testes de autorização para RNF-03; reinicialização e importação repetida/transacional para RNF-04/05; roteiro manual em português, teclado e tamanhos de tela para RNF-06/07; medição M-05 (§5) para RNF-08; simulação de falhas externas para RNF-09. A matriz do TDD §19 relaciona componentes, verificações e itens do backlog. Não se estabelece SLA de disponibilidade: limites operacionais continuam pendentes no TDD §17.

## 10. Fronteiras do produto e informações necessárias

A interface consulta os dados persistidos pela aplicação; abrir o dashboard ou mudar filtros não exige nova leitura externa. A integração inicial é Meu Pluggy / API da Pluggy, condicionada à validação do acesso e das condições do provedor.

O modo de demonstração usa dados fictícios, com indicação visível e configuração distinta da integração real. Não substitui a validação com transações reais.

As escolhas de tecnologias, versões, arquitetura, banco de desenvolvimento e hospedagem estão no [TDD](TDD.md), §§1–3 e 9. Este PRD define resultados e critérios de aceite, não os mecanismos técnicos.

### Conceitos do domínio

| Entidade | Dados principais |
|---|---|
| Conexão | Provedor, itemId externo, situação, expiração quando disponível, última tentativa e último sucesso |
| Cartão | Identificador externo, conexão, descrição, identificação mascarada, moeda e campos opcionais de limite |
| Transação | Identificador externo, cartão, descrição, valor decimal, moeda, datas, natureza, situação e parcelamento quando disponível |
| Categoria | Nome e origem inicial ou pessoal |
| Anotação pessoal | Transação, categoria escolhida, observação e data da alteração; independente dos dados da origem |
| Candidato/confirmação de recorrência — planejado | Padrão sugerido e confirmação do titular como assinatura, parcela ou compras independentes, separados dos metadados da origem; modelo detalhado e tolerâncias na mudança própria |
| Execução de importação | Intervalo solicitado, início, fim, situação, contadores e erro técnico sem dados sensíveis |
| Investimento — P1 | Nome, classe, valor aplicado e valor atual |

## 11. Dependências, riscos e respostas

| Risco ou dependência | Resposta planejada |
|---|---|
| Leitura local funciona, mas a semântica não sustenta indicadores de gastos | Preservar provas limitadas da FNR-004/005 e PR #12; expor movimentos com incerteza e adiar reconhecimento automático de compra/competência, sem alegar atendimento de 5.3. |
| Diferenças entre trial, acesso pessoal e recursos pagos | Confirmar as condições antes de depender de uma funcionalidade; manter categorias próprias. |
| Dados atrasados ou consentimento expirado | Exibir horários e estado da conexão, preservar histórico e orientar renovação. |
| Campos de parcelas, faturas ou limites ausentes | Tratar como opcionais e documentar os campos efetivamente recebidos. |
| Duplicações ou alterações nos identificadores | Testar duas importações e a reconciliação de reconexões; preservar categorias manuais. |
| Duas semanas para código e texto | Priorizar P0 e escrever o TCC desde o primeiro dia; iniciar P1 somente após o marco do núcleo integrado. |
| Exposição de finanças na apresentação | Usar conjunto fictício ou anonimizado e ocultar segredos em capturas. |
| Valores repetidos serem confundidos com assinatura ou parcela | Usar padrão em três meses apenas como candidato; confirmar com o titular, tratar metadados separadamente e fechar tolerâncias antes de implementar RF17. |

Se o acesso real pela aplicação falhar, registrar o bloqueio, a tentativa e a alternativa para avaliação com o professor. Dados fictícios permitem continuar o desenvolvimento, mas **não substituem o critério de aceite da integração real**.

## 12. Marcos e plano de entrega

| Marco | Janela sugerida | Resultado verificável |
|---|---|---|
| M1 — Integração | Dias 1–2 | Backend consulta o cartão e transações reais; formato, cobertura e estados documentados. |
| M2 — Dados | Dias 3–5 | Banco persistente, importação paginada e repetível sem duplicações. |
| M3 — Núcleo revisado | Dias 6–9 — referência original, não nova estimativa | Lista, categorias, visão descritiva, filtros e estados integrados; recorrência em entrega própria após histórico e tolerâncias. Sem indicadores financeiros adiados. |
| M4 — Complementos | Dias 10–11, se P0 estável | Carteira manual e simulador; exportação CSV se couber. |
| M5 — Entrega | Dias 12–14 | Correções, documentação, demonstração com dados seguros e apresentação. |

O texto do TCC acompanha todos os marcos. Datas de calendário e disponibilidade diária devem ser registradas no planejamento de execução, sem presumir que o prazo foi renovado com a aprovação.

As janelas acima são sugestões do planejamento inicial, não um prazo reiniciado. O [backlog](BACKLOG.md), §§3–4, detalha as dependências e ordena a entrega P0 antes dos complementos; o roadmap do TDD §16 explicita os critérios de passagem. Os nomes M1–M5 desta visão resumida não equivalem aos M0–M6 do backlog. Nada nesta revisão inicia FNR-002 ou autoriza publicação.

## 13. Validação e definição de pronto

O núcleo revisado está pronto quando suas entregas próprias tiverem sido implementadas, validadas e aceitas:

- A API .NET consulta dados reais do cartão e o procedimento é reproduzível com configuração local do titular.
- Duas importações dos mesmos dados não duplicam registros e não apagam categorias manuais.
- Alterações vindas da origem são conciliadas; falha parcial não é apresentada como importação completa.
- Lista, filtros e contagens descritivas conciliam com o mesmo universo, data explicitada e cobertura; nenhuma camada calcula, retorna ou exibe agregados monetários de movimentos.
- Natureza identificável/desconhecida, situação pendente/efetivada/futura e dados ausentes possuem tratamentos conservadores verificados com dados controlados. Desconhecidos não são descartados ou promovidos a compra; ausência não vira zero.
- Pesquisa, filtros e estados sem dados funcionam; falhas do provedor mantêm os dados anteriores acessíveis.
- Credenciais e finanças reais não estão no repositório ou nos arquivos de demonstração.
- O ambiente hospedado exige login e restringe os dados ao titular; expiração, recarga, logout e redefinição de senha respeitam RF14.
- Categorias pessoais e observações persistem após nova importação; a atualização na entrada respeita RF16.
- RF17, em sua mudança própria, sugere candidato após padrão em três meses consecutivos e permite confirmação do titular; testes com dados sintéticos demonstram separação de metadados, duplicatas, projeções e falsos positivos conforme tolerâncias previamente aprovadas. Confirmação não libera natureza/competência ou somas financeiras.
- O projeto possui instruções de execução, configuração e limitações conhecidas.

**Critérios anteriores preservados como adiados:** conciliação de gastos entre dashboard e lista; reconhecimento geral de compras efetivadas e estornos elegíveis para indicadores. Não são declarados atendidos pelo MVP descritivo. O aceite do núcleo revisado não encerra automaticamente a investigação financeira.

**Encerramento específico FNR-005:** depende de preservar e identificar o incremento técnico integrado pelo PR #12 e suas limitações; registrar 5.3 como substituída, nunca tecnicamente atendida; alinhar/aprovar o conteúdo documental final, validar documentos/links/diff e obter aceite explícito do resultado revisado. Não exige implementar RF04/RF07/RF17, não reclassifica coletas e não implica sync/arquivamento nesta etapa.

Para P1, verificar persistência da carteira, separação entre valores reais e simulados, taxa zero, conversão da taxa anual e aporte no final do mês. A fórmula do simulador deve ser conferida com resultados independentes.

As verificações do MVP concentram-se em integração, integridade, incerteza explícita, acesso, organização e recorrência assistida. Verificações financeiras e de cálculos de gastos ficam para a entrega futura correspondente; cálculos independentes de P1 permanecem conforme seus critérios originais. A aplicação é um protótipo acadêmico; não se pretende declarar auditoria financeira ou prontidão para operação comercial.

## 14. Fora do escopo desta entrega

Pagamentos, transferências, compra ou venda de ativos; recomendação automática de investimentos; integração com múltiplos usuários; aplicativo móvel nativo; cotações em tempo real; cálculo tributário; orçamento completo de receitas e despesas de outras fontes; previsão de saldo bancário; autenticação bancária implementada pelo próprio Finora.

No MVP revisado, também ficam adiados reconhecimento automático de compras e indicadores monetários de movimentos, incluindo quantidade presumida de compras, gasto líquido, distribuição monetária e evolução por competência. A proibição não altera os cálculos próprios da carteira manual e do simulador P1, que continuam posteriores ao núcleo e sujeitos a seus aceites.

## 15. Validações pendentes

1. Data exata da entrega e tempo diário disponível.
2. Histórico efetivamente disponível e cobertura de faturas, parcelas e limites da origem.
3. Regra positiva de compra e semântica de competência para indicadores financeiros adiados; não bloquear persistência/listagem descritiva por sua ausência.
4. Limitações operacionais e cobertura da leitura real já demonstrada em escopo limitado, sem presumir validação de importação persistente ou ambiente hospedado. Referências: TDD §7, FNR-004/005 e dependências futuras no [backlog](BACKLOG.md).
5. Tolerâncias de valor, datas e descrição e critérios executáveis de RF17, obrigatórios antes da implementação da mudança própria de recorrência.

### Encaminhamento das pendências

Responsáveis abaixo são **sugeridos**, sem atribuição aceita ou prazo inventado. O autor deve registrar evidência sanitizada e revisar os artefatos afetados se a validação alterar escopo, custo ou comportamento.

| ID / pendência | Impacto | Responsável sugerido | Ação e condição de validação | Referência |
|---|---|---|---|---|
| VP-01 — data e capacidade diária (item 1) | Risco de P0 não caber no prazo; janelas não viram compromisso. | Autor, confirmando entrega com professor | Registrar data final e disponibilidade ao planejar a execução, antes de assumir cronograma. | §12; backlog §3; TDD §16 |
| VP-02 — histórico e campos (item 2) | Cobertura limitada e informações de parcelas/faturas/limites podem estar ausentes. | Autor | Registrar amostras sanitizadas, paginação e cobertura observadas antes de fechar importação/indicadores. | FNR-004/005; RF02/RF04 |
| VP-03 — datas, sinais e situações (item 3) | Indicadores incorretos se naturezas ou competência forem inferidas indevidamente. | Autor | Preservar os limites da FNR-005 e a substituição do objetivo 5.3; usar contrato conservador para persistência/listagem. Validar regra discriminatória e semântica temporal em mudança financeira futura antes de indicadores, não antes da importação descritiva. | Objetivos adiados de RF05/RF07; FNR-015/018/023; TDD §§10/13 |
| VP-04 — leitura e limites operacionais (item 4) | Integração real ou publicação podem permanecer bloqueadas. | Autor | Validar ambiente/compatibilidade, PoC e condições dos serviços antes das fases dependentes; registrar falha e alternativa para avaliação, sem substituir aceite real. | FNR-002/004/005/025–028; TDD §§7/17 |
| VP-05 — protocolo e baselines M-01–M-06 | Sem medição não se pode afirmar desempenho ou ganho do titular. | Autor; professor como consultado para método acadêmico, se pertinente | Validar métodos/amostra e coletar resultados antes de declarar sucesso; metas desconhecidas continuam a definir. | §5; proposta de verificação em FNR-024/028; TDD §14 |
| VP-06 — recuperação e retenção | Perda de dados e tratamento inadequado do histórico se a operação não for definida. | Autor | Avaliar e decidir políticas/procedimentos, registrar limitações e validar recuperação antes de declarar operação validada; descarte depende de decisão explícita. | Pendências OP-01–OP-07 do TDD §17; encaminhamento proposto FNR-025/026/028, sem mudar critérios do backlog |
| VP-07 — correspondência de recorrência (item 5) | Falsos candidatos se valor igual, descrição genérica ou projeções forem tratados como assinatura/parcela. | Autor | Definir/aprovar tolerâncias de valor, datas e descrição, critérios de meses consecutivos e cenários de confirmação na mudança própria antes de implementar. Não escolher números implicitamente nem usar recorrência como prova de compra. | RF17; item próprio no backlog; explorador não implementa detector |

### Histórico de aprovação anterior

Esses pontos refinam o PRD v0; não alteram a ideia aprovada. A revisão FNR-001 reflete decisões já registradas no TDD, sem comprovar implementação ou validação funcional. O autor aprovou explicitamente os documentos e o SDD da FNR-001 em 13 de novembro de 2026, nesta conversa, e confirmou a revisão do PR #1. A conclusão documental combina essa aprovação com as validações locais, a verificação das exigências aplicáveis e a integração no commit `075af73`; não se baseia apenas na aprovação ou no merge. Não há revisão formal ou checks reportados no GitHub; evidências e limites estão no backlog e nas tarefas OpenSpec. Não iniciar código ou FNR-002 nesta etapa.

O parágrafo anterior preserva o registro da revisão 0.2/FNR-001, que não aprova a revisão 0.3. A revisão 0.3 recebeu aprovação documental própria e posterior do autor, registrada no cabeçalho; métodos propostos, baselines e validações pendentes não se tornam resultados ou decisões técnicas fechadas por essa aprovação.

## 16. Referências do projeto

- Conversa de definição do escopo e aprovação relatada pelo aluno.
- Protótipo Finora e documento “Finora — proposta e roteiro”, entregues nesta conversa.
- Documentação de uso pessoal do provedor: https://docs.pluggy.ai/en/docs/guides/meu-pluggy-personal-use
- Autenticação: https://docs.pluggy.ai/en/reference/authentication
- Cobertura de instituições: https://docs.pluggy.ai/en/docs/open-finance/institutions-coverage

As condições operacionais e a versão dos endpoints devem ser conferidas na implementação; este documento não as assume como contrato imutável.
