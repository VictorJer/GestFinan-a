# Tasks

## 1. Aprovação e delimitação da entrega

- [ ] 1.1 Conferir PRD RF01/RF02/RF14/RF16, TDD §§5/10/12/20, BACKLOG §3/§7, specs consolidadas e o estado real da FNR-024; registrar no backlog um item próprio para o vínculo local, distinto de FNR-034, sem renumerar IDs existentes. Verificar diff e obter aprovação explícita dos artefatos OpenSpec e das revisões canônicas necessárias antes de código dependente; não iniciar segunda implementação principal enquanto a FNR-024 estiver aberta sem reconciliação.

## 2. CLI isolada e barreira de destino

- [ ] 2.1 Acrescentar roteamento do token único `connection-link-local` sem host HTTP, sem argumentos extras e sem composição de handlers financeiros/HTTP não necessários; cobrir invocação inválida, modo sem terminal e ausência de efeitos com testes de roteamento/host.
- [ ] 2.2 Validar a configuração protegida `ConnectionStrings:Finora` antes da conexão, recusando destinos não locais/ambíguos, database de integração, porta ausente e nome esperado divergente informado no terminal; testar rejeição sem acesso a DB/Pluggy e aceitação de configuração local sintética, sem ler `.env` como fallback.
- [ ] 2.3 Documentar no guia de ambiente a conferência manual do contêiner/volume e o limite de um túnel em loopback, sem registrar valores de configuração; conferir por revisão de segurança e pelo teste de saída que nenhuma string sensível é impressa.

## 3. Verificação do titular e do item sem mutação

- [ ] 3.1 Implementar pré-consulta limitada a exatamente um titular e verificação Pluggy de item e cartão por cliente existente, com prazo/cancelamento e nenhuma leitura de transações; testar com respostas sintéticas estados válido, indisponível, autorização bloqueada, cartão ausente/ambíguo e titular ausente/múltiplo, comprovando zero gravações nas falhas.
- [ ] 3.2 Solicitar confirmação interativa somente após as verificações, sem mostrar item, cartão ou e-mail; cobrir recusa, EOF, entrada redirecionada e Ctrl+C em testes do terminal com sentinelas, verificando zero consulta mutável e zero vazamento em stdout/stderr.

## 4. Vínculo atômico e convergência

- [ ] 4.1 Persistir em uma transação curta `FinancialConnection` e `FinancialConnectionItemIdentity` ativa para o titular/`pluggy`, com nova leitura das precondições sob bloqueio e sem horário de sucesso; validar no PostgreSQL 18 descartável ambas as linhas, a propriedade e a ausência de cartão, movimento e SyncRun criados por esta operação.
- [ ] 4.2 Tratar repetição íntegra como `already_linked` sem UPDATE e rejeitar conflito por item diferente, identidade aposentada/alheia, identidade ativa ausente e conexão ambígua; testar no banco descartável que IDs, vínculos e histórico permanecem idênticos antes/depois.
- [ ] 4.3 Cobrir duas invocações concorrentes, rollback antes de commit e perda simulada de confirmação após commit sem sucesso fictício; verificar no banco descartável no máximo um vínculo novo e ausência de conjunto parcial, mantendo a repetição reconciliadora.
- [ ] 4.4 Mapear os estados fixos a códigos 0/1/2/4/130 com logging sanitizado; testar saídas com sentinelas de URL, e-mail, item, senha, respostas e exceções, sem dados privados em stdout, stderr ou logs.

## 5. Integração, evidência e aceite

- [ ] 5.1 Atualizar `README.md` e `docs/AMBIENTE.md` com o comando e as pré-condições, distinguindo `linked` de importação bem-sucedida e apontando RabbitMQ/MQ-P01, consulta protegida e autorização separada para prova real; verificar o roteiro sem copiar IDs, tokens, dumps ou credenciais.
- [ ] 5.2 Executar restore bloqueado, build, testes focais e regressão pertinente com banco exclusivamente descartável e Pluggy simulada; validar a mudança OpenSpec em modo estrito, `git diff --check` e conferir que não há migration, endpoint público, alteração de frontend ou segredo versionado. Apresentar resultados reais e pendências para aceite humano sem executar `connection-link-local` com dados pessoais.

## Workflow follow-up

- Após aceite e autorização separados, usar o committer para commits locais coesos e encaminhar a mudança por PR; nenhuma aprovação dos artefatos autoriza commit, merge ou publicação por si só.
- Uma execução do comando com o item real e o banco persistente local exige confirmação própria do titular após conferir destino/contêiner; uma sincronização real exige ainda configuração e gates próprios do transporte. Não tratar esse teste operacional como implícito no aceite do código.
- Após integração e aceite efetivos, sincronizar a delta `administrative-connection-provisioning` e arquivar a mudança. Isso não fecha automaticamente FNR-024, FNR-028 ou FNR-034.
