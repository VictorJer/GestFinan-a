# Design

## Context

Ver [proposal.md](proposal.md) e a [delta spec](specs/administrative-connection-provisioning/spec.md). O comando `pluggy-read` já lê o item configurado sem persistir dados; `PluggyConnectionInspector` valida estado do item e seleção de cartão. O roteamento em `Program.cs` já separa comandos de um host HTTP, e a CLI `user-create` usa configuração protegida e saída sanitizada. No banco, `FinancialConnection` referencia `AspNetUsers`; `FinancialConnectionItemIdentity` tem chave global por provedor/item e índice único da identidade ativa por conexão. A promoção da importação comum exige essa identidade ativa. A verificação local encontrou um usuário, zero conexões e credenciais Pluggy configuradas em User Secrets; isso não autoriza acessar o provedor nem altera o resultado futuro da execução.

## Goals / Non-Goals

**Goals:**

- Criar uma fronteira administrativa de primeira vinculação que possa ser repetida sem produzir duplicação ou contornar a identidade ativa exigida pela importação.
- Separar checagem do item fora de transação de banco, confirmação do operador e commit curto; falhar fechado se a configuração ou as evidências não forem suficientes.
- Viabilizar teste sintético reprodutível sem acessar o banco de desenvolvimento, Pluggy real ou outro destino durante testes automatizados.

**Non-Goals:**

- Reconectar/trocar um item existente; esse caso conserva o fluxo interno conservador FNR-012, com prova de continuidade dos cartões.
- Cadastrar novo usuário, criar cartões manualmente, importar transações nessa operação, configurar o broker, fornecer UI pública de conexão ou autorizar deploy.

## Decisions

### D1 — Comando isolado de primeira vinculação

Introduzir `connection-link-local` como token único aceito pelo roteador administrativo antes do host Web. O comando usa `Development` explicitamente escolhido, `ApplicationName`/User Secrets do projeto API e serviços restritos a persistência e à leitura mínima Pluggy. Rejeita argumentos extras e stdin não interativo antes de resolver conexões; não aceita ID, e-mail, senha ou connection string pela linha de comando. Uma confirmação terminal após apresentar **somente** o tipo de destino e o estado validado do item impede que o login no navegador seja interpretado como autorização para gravar. O item vem exclusivamente de `Pluggy:ItemId` e credenciais protegidas; a seleção opcional `Pluggy:AccountId` reaproveita o contrato do inspector, sem expor IDs ao frontend. Alternativas rejeitadas: SQL manual (não cria identidade ativa com as mesmas invariantes) e `POST` autenticado (amplia a superfície pública e confunde FNR-034).

### D2 — Barreira de destino antes de qualquer acesso

Analisar `ConnectionStrings:Finora` sem abrir conexão: aceitar somente host literal `127.0.0.1`, porta explícita, database não vazio e diferente do nome allowlisted de integração `finora_integration_test`; recusar multi-host, socket, destino remoto ou ambíguo. Exigir que o operador informe interativamente o nome esperado do database e confirme que ele é o contêiner/volume de desenvolvimento sob seu controle; comparar exatamente com a configuração já carregada. Esse procedimento não garante sozinho que um túnel em loopback não aponte para destino remoto: o roteiro obriga conferência do mapeamento do Compose antes de autorizar uso com dados pessoais. Nunca usar `FINORA_TEST_CONNECTION_STRING`, arquivo `.env` como fonte alternativa ou reset automático. Falta/inconsistência bloqueia sem conexão. Alternativa de confiar só no modo Development foi rejeitada porque User Secrets podem apontar a outro banco.

### D3 — Prova externa sem escrita nem transação aberta

Após verificar o destino e a cardinalidade do titular, reutilizar o cliente/inspector Pluggy para autenticar e ler **somente** metadados do item e páginas de contas, com orçamento finito e cancelamento. Exigir estado autorizado/atualizável e cartão creditício elegível; se houver múltiplos, exigir seleção protegida não ambígua conforme o inspector atual. Não consultar `/v2/transactions` e não gravar payloads, IDs ou respostas brutas na saída. Falha externa encerra antes da confirmação de escrita; a verificação não demonstra que a importação subsequente dará certo. Alternativa de confiar apenas no ItemId já configurado foi rejeitada porque pode estar vencido, incorreto ou não conter cartão.

### D4 — Mutação curta e convergência idempotente

Depois da confirmação, abrir transação no banco já validado, serializar a vinculação por titular (bloqueio da linha do usuário e/ou advisory lock dedicado, sem reaproveitar chave de login para lock em toda chamada Pluggy), reler a cardinalidade e as identidades de item sob proteção e aplicar a fábrica de domínio. Se não existe vínculo, inserir `FinancialConnection` com `Provider=pluggy`, timestamps de sucesso nulos, e `FinancialConnectionItemIdentity.CreateActive` no mesmo `SaveChanges`/commit. Se há exatamente uma conexão e identidade ativa íntegra do mesmo item, retornar `already_linked` sem `UPDATE`; se há múltiplas, falta de identidade, item divergente, registro aposentado ou conflito, recusar sem chamar a operação de reconexão. A PK global e o índice ativo são barreiras adicionais contra corrida; testes de concorrência devem comprovar que no máximo um sucesso novo ocorre. Falha pré-commit implica rollback; confirmação perdida permanece incerta e a repetição deverá inspecionar o estado, não anunciar rollback. Alternativa de criar só `FinancialConnection` foi rejeitada porque a promoção da importação exige a identidade ativa.

### D5 — Contrato de saída e fronteiras de validação

Usar estados fixos `linked` e `already_linked` com código 0, argumentos/configuração/terminal inválidos com código 2, conflitos com código 4, falha operacional/commit incerto com código 1 e cancelamento pré-commit com código 130. Nenhuma saída inclui e-mail, item, conta/cartão, URI, SQL, credencial, token ou exceção original; desabilitar logs de HTTP/EF/host que possam expor entradas sensíveis. Testes unitários usam cliente Pluggy falso e sentinelas; integração usa PostgreSQL 18 descartável allowlisted e fixtures de titular, colisão, concorrência e rollback, nunca o volume de desenvolvimento. Uma prova real é uma etapa operacional distinta, com autorização própria para leitura externa e escrita no banco selecionado; `linked` não significa cartões ou movimentos disponíveis. O frontend existente só mostrará dados depois da sincronização pelos gates atuais (broker, execução e coleta).

## Risks / Trade-offs

- [User Secrets apontam ao banco errado ou loopback encaminha para outro destino] → validação de host/database/porta, confirmação interativa e conferência externa do contêiner/volume antes da operação real; em dúvida, bloquear.
- [Item muda entre verificação remota e gravação] → registrar somente a identidade opaca validada; falha posterior na importação continua observável, nunca alegar cobertura garantida.
- [Corrida de provisionamento ou commit incerto] → bloqueio e constraints do banco, releitura no replay, sem delete/retry de mutação automática.
- [O usuário espera movimentos imediatamente] → explicitar que o comando não importa; MQ-P01, configuração de transporte e execução real são gates separados, sem antecipar sua aprovação.
- [O banco persistente tem dados pessoais] → nenhum teste automatizado aponta para ele; prova real exige consentimento específico, inspeção de destino e evidência sanitizada.

## Migration Plan

Não há migration nova. Na implementação futura, validar o schema existente e testar o comando somente em banco descartável; atualizar guias e submeter pela feature branch após aprovações e checks aplicáveis. A execução real no banco local deverá ser autorizada separadamente após a revisão do procedimento e da configuração, sem publicar dados em logs. Se ocorrer vínculo incorreto, parar antes de sincronizar e inspecionar o estado com o titular; não oferecer exclusão, downgrade ou troca automática que apague histórico. Integração por PR não executa o comando nem aplica migration em qualquer destino.
