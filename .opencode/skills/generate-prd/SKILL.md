---
name: generate-prd
description: Gerar um documento de requisitos de produto (PRD) completo a partir de um prompt inicial. Use quando o usuário pedir um PRD, especificação de produto, ou invocar /generate-prd.
---

# Gerar PRD

Transforme a ideia inicial do usuário em um PRD em português, pronto para revisão por produto, design e engenharia. Este fluxo produz **somente documentação**: não escreva código, não inicie implementação e não crie uma mudança OpenSpec a menos que o usuário peça explicitamente.

## Entrada e descoberta

1. Use o prompt fornecido na conversa ou pelo comando `/generate-prd`. Se não houver uma ideia de produto identificável, pergunte o que o usuário quer criar antes de continuar.
2. Leia apenas o contexto relevante do projeto, incluindo `AGENTS.md` aplicável e **`docs/entrevista.md` quando existir e disser respeito ao produto**, além de `README.md`, requisitos existentes e documentação do domínio. Trate a entrevista como fonte de necessidades, não como especificação definitiva; identifique quem foi entrevistado, o que foi dito e o que ainda precisa de validação. Se o documento indicar que a entrevista é fictícia, registre essa limitação explicitamente no PRD. Não confunda comportamento atual com funcionalidade proposta. Não dependa de OpenSpec nem altere seus artefatos. Os caminhos de documentos do produto citados neste fluxo são relativos à raiz do projeto, não à pasta da skill.
3. Compare o prompt e as fontes com o escopo, as regras de negócio, os fluxos, os requisitos e os critérios de aceite pretendidos. Reaproveite decisões já confirmadas, com sua origem; faça uma **entrevista de esclarecimento somente sobre lacunas ou conflitos relevantes ao nível de entrega solicitado**. Não escolha silenciosamente uma interpretação que altere o comportamento do produto. Use o **sistema de questões disponível no ambiente**, como `question` ou uma ferramenta equivalente, agrupando questões relacionadas e oferecendo opções quando úteis. Faça novas rodadas apenas enquanto restarem respostas essenciais ao pedido; não pergunte novamente sobre decisões confirmadas sem conflito ou mudança de premissa. Regras pendentes na entrevista não devem ser descritas como fechadas sem validação.
4. A ausência da ferramenta de questões ou de novas respostas **não bloqueia a entrega quando o contexto já for suficiente**. Se faltar informação essencial ao pedido e não houver resposta, mantenha a pendência explícita e informe o bloqueio pelo mecanismo permitido em `AGENTS.md`; não substitua o sistema de questões por perguntas na resposta quando as instruções do projeto proibirem isso. Se o usuário solicitar um rascunho/v0 com pendências, entregue-o com hipóteses identificadas, decisão pendente, impacto e ação de validação, sem tratá-lo como aprovado ou pronto para implementação. Se o pedido exigir decisões fechadas, não declare o documento concluído enquanto houver bloqueadores. Métricas basais, dados de mercado e outros desconhecidos podem ficar como `A definir`, com ação de validação. Não invente entrevistas, dados, obrigações legais, integrações ou decisões como se fossem fatos confirmados.
5. Resolva o destino nesta ordem: caminho solicitado pelo usuário; PRD canônico identificado nos documentos e links do projeto; na ausência de ambos, `docs/prd.md` na raiz do projeto. Preserve nome, capitalização, IDs e referências do documento existente; não crie `docs/prd.md` concorrente com `docs/PRD.md`. Leia o arquivo antes de editá-lo. Um pedido explícito de atualização autoriza a edição no escopo solicitado, sem nova confirmação automática; não substitua conteúdo não relacionado. Se houver candidatos realmente ambíguos ou risco de substituir trabalho fora do pedido, esclareça pelo sistema de questões antes de gravar.

## Estrutura do documento

Use Markdown com títulos claros e detalhe proporcional ao produto. Todas as seções abaixo devem aparecer; quando uma informação não puder ser determinada, registre `A definir` e a decisão pendente, em vez de preencher com afirmações fictícias.

1. **Cabeçalho**: nome do produto/funcionalidade, status `Rascunho`, data e origem do pedido.
2. **Resumo executivo**: proposta de valor, problema e resultado pretendido.
3. **Contexto e problema**: situação atual, evidências disponíveis, dores e oportunidades; diferencie fatos observados de hipóteses.
4. **Objetivos e métricas de sucesso**: objetivos mensuráveis, indicadores, definição de medição e metas sugeridas quando justificáveis; identifique baselines e metas pendentes de validação.
5. **Público-alvo e stakeholders**: perfis, necessidades, responsáveis e interessados conhecidos ou a validar.
6. **Escopo**: funcionalidades incluídas no MVP, evoluções posteriores e itens fora de escopo; prioridades e dependências importantes.
7. **Jornadas e casos de uso**: fluxos principais, alternativas relevantes e estados de erro/ausência de dados, do ponto de vista dos usuários.
8. **Requisitos funcionais**: itens identificados (`RF-01`, etc.), priorizados, específicos e verificáveis; indique regras de negócio e dependências quando necessário.
9. **Requisitos não funcionais**: itens identificados (`RNF-01`, etc.) sobre acessibilidade, segurança/privacidade, desempenho, disponibilidade, observabilidade e demais atributos pertinentes; não invente números ou compromissos normativos sem base.
10. **Critérios de aceite**: critérios testáveis por funcionalidade ou caso de uso, associados aos IDs dos requisitos, incluindo cenários de falha relevantes.
11. **Dados e integrações**: entidades e informações necessárias, ciclo de vida, interfaces externas e restrições conhecidas; marque desconhecidos.
12. **Riscos, premissas e dependências**: impacto, mitigação ou ação de validação para cada ponto relevante.
13. **Questões em aberto e próximos passos**: decisões pendentes com responsável sugerido quando possível e forma de validá-las.

## Revisão e entrega

- Verifique se o MVP é coerente com o problema, se os requisitos são rastreáveis aos critérios de aceite e se as evidências disponíveis foram consideradas. Confirme que fatos, hipóteses e decisões pendentes estão distinguíveis: nenhuma ambiguidade pode aparecer como decisão fechada; pendências permitidas no rascunho solicitado devem ter impacto e ação de validação. Preserve os IDs e links existentes ao atualizar.
- Escreva o documento no caminho escolhido e confirme sua existência. Responda com o link do arquivo e um resumo breve das principais decisões e pendências; não apresente o texto inteiro na resposta, salvo se solicitado.
