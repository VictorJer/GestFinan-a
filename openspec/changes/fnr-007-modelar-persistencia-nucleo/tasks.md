# Tasks

Estado: **planejamento para revisão**. Todas as tarefas abaixo permanecem pendentes; sua existência não autoriza código, migration, instalação ou integração. Rastreabilidade: FNR-007; PRD RF02/RF03/RF06/RF15, RNF-02/04/05; TDD DT-07/14/15/23/35/38/40/54 e §§3/5/9/10/14/15/19. A inclusão do armazenamento Identity foi escolhida pelo autor; não é aprovação automática deste SDD nem início de FNR-008/009.

## 1. Gates da implementação

- [ ] 1.1 Conferir a integração/encerramento da base FNR-006, o aceite revisado FNR-005 e a aprovação explícita de proposta/spec/SDD FNR-007 antes de código; verificar registros/evidências aplicáveis e branch de trabalho adequada, mantendo implementação bloqueada se houver dependência ou aprovação pendente, sem inferir PR/merge pela main local.
- [ ] 1.2 Concretizar no TDD canônico o desenho relacional e a fronteira de armazenamento Identity descritos em D1–D5, sem alterar PRD nem reabrir tecnologias herdadas; verificar rastreabilidade ao backlog e obter revisão/aprovação necessária da nova revisão documental, preservando integralmente o histórico e a aprovação do TDD 0.8 antes da implementação dependente.

## 2. Entidades e invariantes puras

- [ ] 2.1 Criar entidades de conexão, cartão e movimento no Domain com identidade interna/externa distinta, valor/moeda originais e interpretação conservadora independente; verificar testes unitários de campos obrigatórios, IDs opacos, faixa/escala sem arredondamento, referência/versionamento da normalização e ausência de dependência EF/Identity, comentando em português os blocos lógicos manuais.
- [ ] 2.2 Criar categoria, anotação e registro básico SyncRun conforme D2/D5, sem catálogo, normalizador de nomes de produção ou executor; verificar testes unitários de observação em 500/501 caracteres, limpeza sem apagar categoria, categoria ausente, contadores nulos/não negativos, intervalo válido, código de falha sanitizado e registro sem avanço automático do sucesso da conexão.
- [ ] 2.3 Documentar em AMBIENTE/TDD os contratos dos campos de origem versus interpretação, escopo das categorias, limite técnico de IDs em bytes e fronteiras FNR-012/013/016/017; verificar coerência com D2–D5 e que não há alegação de compra/competência, política de acentos ou importação já validadas.

## 3. Identity mínimo e mapeamentos relacionais

- [ ] 3.1 Adicionar o pacote de armazenamento Identity proposto em 10.0.15, conferir disponibilidade/licença/avisos/transitivas e atualizar somente os locks afetados com grafo real; verificar restore bloqueado e build sem divergência da baseline EF/Npgsql, interrompendo para decisão se exigir troca de versão/linha, sem configurar autenticação HTTP.
- [ ] 3.2 Incluir ApplicationUser Guid e adaptar FinoraDbContext ao modelo Identity sem roles, com configuração de base antes dos mapeamentos e opções de schema/chaves compartilhadas por factory/testes; verificar testes de metadados e criação design-time, UUID não vazio e ausência de AuthSessions/passkeys/seed de usuário, mantendo Domain/Application livres de referências Identity.
- [ ] 3.3 Configurar entidades/FKs compostas por titular, chave da conta externa, PK da anotação e escopo técnico de categoria com CHECKs para nulos/UUID vazio, conforme D3; verificar testes do modelo EF que inspecionem chaves, relações e exclusões Restrict/NoAction, inclusive a regra de sistema versus pessoal, sem depender de filtros de consulta para integridade.
- [ ] 3.4 Mapear numeric(19,4), datas originais/derivadas, UTC de auditoria, motivos independentes, collation C, limites em bytes, unicidades e índices de D4/D5, além da barreira comum de validação pré-gravação; verificar testes unitários/modelo para caminhos síncrono/assíncrono, limites multibyte e ausência de índices redundantes; atualizar AMBIENTE com as opções de design-time que FNR-008 deverá reutilizar.

## 4. Migration incremental e integridade no PostgreSQL descartável

- [ ] 4.1 Gerar Fnr007CorePersistence e revisar Up/Down, snapshot e SQL, preservando a migration Fnr006PersistenceSmoke; verificar que há somente schema Identity básico/financeiro, constraints e índices previstos, sem seed, payload bruto, AuthSessions, outbox, recorrência ou investimento, e documentar os comandos administrativos e avisos de downgrade em AMBIENTE.
- [ ] 4.2 Adaptar o teste histórico da FNR-006 para aplicar apenas o alvo smoke e criar fixture serializada exclusiva para migrations atuais; verificar base vazia, atualização de smoke para FNR-007, reaplicação idempotente e downgrade/reaplicação somente no destino descartável validado antes de qualquer conexão/DDL/limpeza, sem manter a asserção histórica sobre um MigrateAsync sem alvo.
- [ ] 4.3 Adicionar testes de integração para FKs inexistentes e cada vínculo entre titulares, conta externa divergente, categoria sistema/própria/alheia e combinações nulas inválidas; verificar rejeição pelo PostgreSQL também por inserção direta das relações inválidas e confirmar que usuário com UUID vazio e exclusão de pais financeiros referenciados são impedidos sem cascata de histórico.
- [ ] 4.4 Adicionar testes de unicidade sequencial e concorrente para conexão/cartão/movimento, anotação única e chave normalizada por escopo; verificar com contextos independentes que há apenas uma gravação confirmada em conflito e que IDs distintos com conteúdo coincidente, mesmo ID em contas diferentes e IDs com caixa/espaços distintos não são fundidos.

## 5. Preservação de dados e atomicidade

- [ ] 5.1 Adicionar round-trips sintéticos de decimais positivos/negativos/zero e moedas distintas, incluindo extremos representáveis e rejeição de overflow/fração além de quatro casas; verificar igualdade individual após nova leitura e nenhuma linha gravada em falha, tanto via SaveChanges quanto SaveChangesAsync, sem somas ou conversão.
- [ ] 5.2 Adicionar round-trips de calendário, instante na virada do mês, data sem offset e metadados mensais/ausentes/incoerentes; verificar origem intacta, dia técnico nullable/normalizado pela regra existente, referência/versão preservadas e motivos de natureza/situação/data separados, incluindo PAGAMENTO desconhecido e PENDING sem ativar regra positiva.
- [ ] 5.3 Testar atualização controlada da origem na mesma identidade interna e edição/limpeza da anotação, sem implementar importador ou endpoint; verificar por novo contexto que categoria/observação são preservadas na atualização externa e que editar anotação não altera os fatos ou classificação do movimento.
- [ ] 5.4 Testar operação com várias gravações dentro da mesma transação curta, provocando falha de constraint depois da primeira gravação e executando rollback; verificar em contexto independente que não ficaram movimento/anotação novos nem sucesso parcial e que registros anteriores permanecem intactos; documentar os limites dessa prova sem chamá-la sincronização ponta a ponta.

## 6. Validação integrada e entrega para revisão

- [ ] 6.1 Executar restore bloqueado, build e suíte MSTest/MTP com PostgreSQL descartável; verificar regressões health HTTP 200/corpo original, OpenAPI/Swagger e comandos locais existentes, mantendo host sem conexão de banco obrigatória e sem Database.Migrate no startup; registrar resultados reais e limitações, não checks remotos presumidos.
- [ ] 6.2 Executar validação OpenSpec estrita, verificações documentais/links/diff e inspeção de segredos/dados pessoais no escopo; atualizar AMBIENTE e registro FNR-007 no backlog com evidências verificadas, revisões documentais e aceite explícito necessário, preservando dependências e sem declarar PR, integração, publicação ou tarefas posteriores concluídas antes de ocorrerem.

## Workflow follow-up

- A implementação só começa mediante nova solicitação explícita de apply e após os gates; esta proposta não instala dependências, cria tabelas ou aplica migrations.
- Commits locais, quando autorizados e após validação, devem usar o subagente committer com caminhos/evidências explícitos. Nenhum commit, push, PR ou merge é autorizado apenas pela geração destes artefatos.
- Integração exclusivamente por PR revisado e checks aplicáveis. Não há publicação ou migration Neon neste escopo; eventual deploy dependente futuro segue TDD §9.
- Após validação e aceite do escopo, planejar sync da capability e arquivamento no mesmo PR quando possível, informando o estado real de integração. Não executar fechamento da FNR-006 ou avanço para FNR-008/012 implicitamente.
