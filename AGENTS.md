# Geral

- Sempre utilizar o sistema de questões do ambiente (harness) ao invés de fazer perguntas na resposta.
- Reaproveitar decisões e autorizações explícitas já registradas. Fazer perguntas apenas sobre lacunas relevantes, conflitos ou mudanças de premissa; não repetir a seleção de tecnologias ou aprovações sem motivo.
- Preservar trabalho existente e limitar as alterações ao pedido. Não tratar uma solicitação de planejamento ou revisão como autorização para implementar, fazer merge ou publicar.

## Fluxo de trabalho

Seguir **PRD → TDD → SDD com OpenSpec → implementação → validação → commits locais com o subagente committer → Pull Request → revisão e integração na main → publicação aplicável e encerramento**. Trabalhar em feature branch; não implementar nem enviar alterações diretamente à `main`.

### 1. Selecionar e delimitar a mudança

- Ler `docs/BACKLOG.md`, conferir dependências e selecionar um item ou um conjunto pequeno e coeso. Manter uma mudança principal em implementação por vez no trabalho individual.
- Conferir o estado do repositório e preservar alterações não relacionadas. Criar ou reutilizar uma branch adequada à mudança, sem misturar silenciosamente outro trabalho em andamento.
- Registrar o item do backlog e o escopo da mudança. Não avançar para tarefas seguintes apenas porque seus documentos já existem.

### 2. Usar PRD e TDD como referências

- **PRD:** problema, escopo, requisitos e critérios de aceite. **TDD:** decisões técnicas, arquitetura e roadmap de implementação.
- Usar os documentos canônicos `docs/PRD.md` e `docs/TDD.md`, preservando nomes, IDs e links. Não gerar novos documentos concorrentes nem refazer entrevistas a cada tarefa.
- Atualizá-los somente quando a mudança afetar requisitos, decisões técnicas ou revelar inconsistência relevante. Mudança de conteúdo em documento aprovado cria revisão pendente, preservando a aprovação e o histórico da versão anterior.
- Decisões não alteradas continuam herdadas de sua origem. Correções exclusivamente editoriais podem manter o estado existente, conforme as regras das skills.

### 3. Especificar com OpenSpec antes de implementar

- Usar a configuração e as skills instaladas no projeto. O SDD é representado pelo desenho técnico da mudança no OpenSpec, com proposta, requisitos/cenários e tarefas conforme o schema aplicável.
- Manter rastreabilidade entre item do backlog, requisitos do PRD, decisões do TDD e artefatos da mudança. Não presumir comandos, artefatos ou opções ausentes da instalação.
- Apresentar os artefatos para revisão e obter a aprovação necessária **antes de implementar**. Artefatos completos ou validação do CLI não significam aprovação humana.
- Para mudanças exclusivamente documentais, usar artefatos proporcionais; dispensar specs de comportamento somente quando o schema permitir e houver justificativa explícita. Não criar requisitos artificiais da aplicação.
- Se o plano mudar durante a implementação, atualizar os artefatos afetados e revisar a alteração relevante antes de continuar. Não reabrir decisões aprovadas que permaneceram inalteradas.

### 4. Implementar e validar

- Implementar somente o escopo aprovado na feature branch e atualizar as tarefas OpenSpec com evidências reais. Não marcar um item concluído apenas por ter gerado arquivos.
- Executar as validações proporcionais: testes e builds dos componentes afetados, validação OpenSpec e verificações documentais quando aplicáveis. Diferenciar resultados locais dos checks reportados pelo GitHub.
- Informar falhas, limitações e bloqueios concretos. Não apresentar dados fictícios como prova de integração real nem ocultar pendências que bloqueiam os critérios de aceite.

### 5. Criar e conferir commits com o committer

- Usar o subagente `committer`, definido em `.opencode/agents/committer.md`, para organizar e criar commits locais das alterações autorizadas. O agente principal continua responsável pela implementação, validação e revisão do resultado.
- Acioná-lo quando houver uma mudança lógica pronta e validada; não é necessário aguardar toda a funcionalidade se um incremento tiver utilidade independente. Não impor um commit por arquivo nem fragmentar código, testes e configurações dependentes.
- Enviar ao subagente a raiz do repositório, branch, objetivo, caminhos explícitos autorizados, referência ao backlog/OpenSpec, restrições e evidências das validações relevantes. Ele não deve depender de acesso à conversa do agente principal.
- Só delegar criação de commits quando essa ação estiver autorizada no escopo da tarefa. Pedido de revisão ou sugestão de agrupamento não autoriza staging/commit; reaproveitar autorização existente sem solicitá-la novamente.
- Durante a execução do committer, não alterar o índice Git nem delegar commits concorrentes no mesmo checkout. Mudanças inesperadas no índice ou nos arquivos do grupo devem ser esclarecidas antes de continuar.
- Preservar alterações não relacionadas e staged de terceiros. O committer não edita código, não troca branch, não faz push, PR, merge, amend, reset, rebase ou descarte de alterações e não contorna hooks/proteções.
- O agente principal deve conferir hashes, mensagens, conteúdo dos commits, resultados reportados e alterações restantes antes do push ou PR. Commit local não significa requisito aceito, tarefa concluída ou publicação autorizada.
- Se o subagente estiver indisponível, informar a limitação; o agente principal pode executar os mesmos passos somente dentro da autorização existente, respeitando as mesmas verificações, sem afirmar que houve delegação.

### 6. Abrir PR e integrar

- Apresentar a mudança no PR, vinculando backlog, PRD, TDD e SDD/OpenSpec, com resumo, testes, riscos e pendências relevantes. Abertura ou aprovação do PR não substitui aprovações documentais ainda necessárias.
- Integrar exclusivamente pelo PR após revisão, aprovações e verificações exigidas. Não fazer merge com bloqueios pendentes, contornar proteções ou presumir autorização de merge a partir de um pedido de revisão.
- Verificar quais checks e revisões são realmente obrigatórios. Distinguir não configurado, pendente, falho e aprovado: ausência de checks não é aprovação, mas também não cria uma exigência inexistente. Revisão explícita registrada fora do GitHub pode ser evidência quando as regras aplicáveis permitirem; não descrevê-la como review formal da plataforma.
- Quando houver migration, preservar o processo do TDD: validar localmente e aplicar a alteração compatível no ambiente de destino antes da integração que dispara o deploy dependente. Não executar migration automaticamente na inicialização da API.
- Publicar somente a versão aprovada pelos checks aplicáveis, sem deploy paralelo que os contorne. Mudanças exclusivamente documentais não exigem um deploy de aplicação fictício.

### 7. Encerrar a mudança

- Conferir os critérios de aceite, a evidência de integração e a publicação quando aplicável. Atualizar o backlog com status verdadeiro e referências ao PR/commit; manter bloqueios explícitos enquanto existirem.
- Sincronizar as delta specs com as especificações consolidadas e arquivar a mudança após validação e aceite do escopo. Sincronizar specs não faz merge no Git; arquivar não comprova integração ou publicação.
- Planejar sync/arquivamento no mesmo PR da entrega quando possível, após validação e aceite. Se realizados após a integração, registrar o encerramento de forma proporcional ao fluxo do repositório, sem criar uma cadeia de mudanças apenas para registrar registros anteriores.
- Não apagar histórico de aprovação nem declarar aprovada uma revisão nova sem evidência correspondente. Se não for possível concluir, informar o estado e o impedimento específico.

## Comentários nos blocos de código

- Comentar em português cada bloco lógico de código criado ou alterado, explicando sua finalidade. Considerar como blocos lógicos métodos/funções, componentes, etapas de processamento e trechos relevantes de configuração; não exigir um comentário para cada linha, declaração ou par de chaves.
- Descrever a intenção, as regras de negócio, as premissas e o motivo de decisões que não sejam evidentes pela leitura. Em métodos longos, comentar também as etapas internas distintas, especialmente validação, transformação, persistência e tratamento de falhas.
- Usar comentários curtos e específicos, próximos do trecho correspondente. Evitar apenas traduzir a sintaxe ou repetir o nome da função. Quando útil, documentar contratos públicos com XML documentation em C# ou JSDoc em TypeScript.
- Aplicar a regra ao backend, frontend, testes, scripts e configurações manuais dentro do escopo da tarefa. Usar sintaxe válida para cada formato; quando ele não aceitar comentários, como JSON estrito, registrar a explicação na documentação associada, sem invalidar o arquivo.
- Atualizar os comentários quando o comportamento mudar. Não apresentar comportamento planejado como implementado nem incluir segredos ou dados pessoais em comentários.
- Preservar arquivos gerados automaticamente e dependências de terceiros; documentar seu uso nos pontos de integração ou na documentação, sem editar saídas geradas apenas para acrescentar comentários. Não comentar código antigo não relacionado à mudança.

## Comandos OpenSpec no OpenCode

Usar os comandos abaixo **no chat do OpenCode**, conforme os arquivos instalados em `.opencode/commands/`:

- `/opsx-explore`: investigar dúvidas e alternativas, quando necessário.
- `/opsx-propose`: preparar os artefatos de uma mudança delimitada.
- `/opsx-update`: revisar artefatos de planejamento existentes e manter sua coerência.
- `/opsx-apply`: executar as tarefas aprovadas da mudança.
- `/opsx-sync`: incorporar delta specs às especificações consolidadas; não integra branches Git.
- `/opsx-archive`: arquivar a mudança validada e aceita, com o estado de integração informado.

Comandos `openspec ...` do CLI são executados no terminal. Consultar as instruções da instalação para a sintaxe exata. Não presumir disponibilidade de `/opsx-verify` ou de outros comandos opcionais.
