# Tasks

## 1. Aprovação e delimitação da entrega

- [x] 1.1 Conferir PRD RF01/RF02/RF14/RF16, TDD §§5/10/12/14/19/20, BACKLOG §§3/4/5/7, specs consolidadas e estado integrado/arquivado da FNR-024; registrar FNR-039 P0/M com dependências FNR-006/007/008/012, distinta de FNR-034 e sem renumerar IDs. Revisar o diff e obter aprovação explícita da proposta/spec/design/tarefas OpenSpec e de BACKLOG 0.90/TDD 0.42 antes de código dependente; não alterar o PRD. A FNR-024 está reconciliada como gate de CI concluído, não dependência funcional.

## 2. CLI isolada e barreira de destino

- [x] 2.1 Acrescentar roteamento do token único `connection-link-local` sem host HTTP, sem argumentos extras e sem composição de handlers financeiros/HTTP não necessários; cobrir invocação inválida, modo sem terminal e ausência de efeitos com testes de roteamento/host.
- [x] 2.2 Validar a configuração protegida `ConnectionStrings:Finora` antes da conexão, respeitar o ambiente explicitamente selecionado (Development somente quando escolhido), recusar destinos não locais/ambíguos, database de integração, porta ausente e nome esperado divergente informado no terminal; testar rejeição sem acesso a DB/Pluggy e aceitação de configuração local sintética, sem ler `.env` como fallback.
- [x] 2.3 Documentar no guia de ambiente a conferência manual do contêiner/volume e o limite de um túnel em loopback, sem registrar valores de configuração; conferir por revisão de segurança e pelo teste de saída que nenhuma string sensível é impressa.

## 3. Verificação do titular e do item sem mutação

- [x] 3.1 Implementar pré-consulta limitada a exatamente um titular e verificação Pluggy de item e cartão por cliente existente, com prazo/cancelamento e nenhuma leitura de transações; testar com respostas sintéticas estados válido, indisponível, autorização bloqueada, cartão ausente/ambíguo e titular ausente/múltiplo, comprovando zero gravações nas falhas.
- [x] 3.2 Solicitar confirmação interativa somente após as verificações, sem mostrar item, cartão ou e-mail; cobrir recusa, EOF, entrada redirecionada e Ctrl+C em testes do terminal com sentinelas, verificando zero consulta mutável e zero vazamento em stdout/stderr.

## 4. Vínculo atômico e convergência

- [x] 4.1 Persistir em uma transação curta `FinancialConnection` e `FinancialConnectionItemIdentity` ativa para o titular/`pluggy`, com nova leitura das precondições sob bloqueio e sem horário de sucesso; validar no PostgreSQL 18 descartável ambas as linhas, a propriedade e a ausência de cartão, movimento e SyncRun criados por esta operação.
- [x] 4.2 Tratar repetição íntegra como `already_linked` sem UPDATE e rejeitar conflito por item diferente, identidade aposentada/alheia, identidade ativa ausente e conexão ambígua; testar no banco descartável que IDs, vínculos e histórico permanecem idênticos antes/depois.
- [x] 4.3 Cobrir duas invocações concorrentes, rollback antes de commit e perda simulada de confirmação após commit sem sucesso fictício; verificar no banco descartável no máximo um vínculo novo e ausência de conjunto parcial, mantendo a repetição reconciliadora.
- [x] 4.4 Mapear os estados fixos a códigos 0/1/2/4/130 com logging sanitizado; testar saídas com sentinelas de URL, e-mail, item, senha, respostas e exceções, sem dados privados em stdout, stderr ou logs.

## 5. Integração, evidência e aceite

- [x] 5.1 Atualizar `README.md` e `docs/AMBIENTE.md` com o comando e as pré-condições, distinguindo `linked` de importação bem-sucedida e apontando RabbitMQ/MQ-P01, consulta protegida e autorização separada para prova real; verificar o roteiro sem copiar IDs, tokens, dumps ou credenciais.
- [x] 5.2 Executar restore bloqueado, build, testes focais e regressão pertinente com banco exclusivamente descartável e Pluggy simulada; validar a mudança OpenSpec em modo estrito, `git diff --check` e conferir que não há migration, endpoint público, alteração de frontend ou segredo versionado. Apresentar resultados reais e pendências para aceite humano sem executar `connection-link-local` com dados pessoais.

## Workflow follow-up

- Após validação local, o titular solicitou explicitamente em 27/11/2026 os commits locais e o encaminhamento por PR; o escopo validado foi tratado como aceito localmente para esse encaminhamento. O commit de implementação foi criado pelo committer; o PR ainda será aberto. Isso não autoriza merge ou publicação.
- Uma execução do comando com o item real e o banco persistente local exige confirmação própria do titular após conferir destino/contêiner; uma sincronização real exige ainda configuração e gates próprios do transporte. Não tratar esse teste operacional como implícito no aceite do código.
- Por solicitação explícita do titular em 27/11/2026, a delta `administrative-connection-provisioning` foi sincronizada na spec principal e esta mudança foi arquivada localmente antes da integração. O arquivo não comprova merge/publicação e não fecha automaticamente FNR-024, FNR-028 ou FNR-034.
