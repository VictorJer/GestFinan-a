# Design

## Context

Ver [proposal.md](proposal.md) para a motivação. A `frontend/src/app/auth/area/` é apenas informativa e `/area` já tem guard; login redireciona para ela. `SessionService` mantém token/geração apenas em memória e o interceptor protege chamadas à base da API configurada, mas hoje reduz 409/429/503 a erros genéricos. A API já fornece `GET /api/cards` (nome, final, moeda), `GET /api/connections/current/sync-state`, as duas rotas POST com `Idempotency-Key` e `GET /api/sync-runs/{id}`; o backend decide elegibilidade, intervalo e propriedade. `docs/DESING.md` é referência estética: cartões, limites e conexão simulados não são dados reais.

## Goals / Non-Goals

**Goals:** integrar os contratos existentes com segurança da sessão, reconhecer uma intenção por entrada autenticada e acompanhar estado sem transformar aceite em sucesso; tornar legíveis os estados, horários, cobertura e erros no desktop/celular.

**Non-Goals:** criar conexão bancária/consentimento, estimar limite/disponível/gasto, alterar política do servidor ou API, adicionar polling no backend, implementar lista/visão descritiva FNR-022/023, configurar serviço hospedado ou provar comportamento da Pluggy real.

## Decisions

### 1. Evoluir `/area`, sem rota financeira paralela

Trocar o conteúdo da área protegida por uma visão de cartão/conexão e ajustar o menu público/protegido. Manter a mesma proteção de rota, sessão volátil e base de URL normalizada. Construir leitura de cartões e estado independentemente: um GET que falha não elimina um resultado válido do outro. A ausência de conexão (404) é diferente de cartões vazios (200 `[]`); 409 de conexão ambígua não será apresentado como estado conectado. Somente campos retornados pela API aparecem e horários nulos recebem rótulo de indisponibilidade. Evitar usar dados da demonstração HTML ou criar outra rota/card store concorrente. Alternativa rejeitada: novo `/card` independente com duplicação de controle de acesso e placeholder antigo em `/area`.

### 2. Coordenador por geração de login para a intenção de entrada

Criar um serviço de estado de sessão para a FNR-021, separado do componente visual, que registre a geração atual e a intenção on-entry já enviada. A primeira entrada em `/area` durante a geração faz um único POST `/sync/on-entry`; GETs de leitura não disparam POST e remontagens da página não reavaliam. A chamada vai ao servidor mesmo se `updateNeeded` de uma leitura anterior for falso: somente o servidor decide a política no instante de aceitação. Nova geração após novo login começa nova intenção; fim/expiração limpa referências, erros e dados privados. Sem persistência em browser storage. Alternativa rejeitada: disparar a atualização em cada `ngOnInit`/navegação ou decidir elegibilidade pelo relógio do cliente.

### 3. Chave por intenção e replay seguro

Gerar chave opaca ASCII visível de no máximo 128 caracteres via `crypto.randomUUID()` no navegador antes do POST. Guardar em memória o par (modo, chave) enquanto a decisão está pendente ou indeterminada; retry explícito reenvia exatamente modo/chave, inclusive após timeout. Nova intenção manual só recebe outra chave depois de resolução/abandono consciente da anterior, nunca como retry silencioso. Se a intenção de entrada ficar indeterminada, suspender novos POSTs independentes da sessão até reconciliação ou novo login; o botão oferece repetir o pedido pendente, não uma nova intenção. A referência `syncRunId` de 202/409 ativo pode ser usada para consultar estado, mas conflito não equivale a aceite da intenção atual. Alternativa rejeitada: nova chave a cada retry (duplicação sob commit incerto) ou armazená-la em `localStorage` (sessão permanece volátil).

### 4. Extrair apenas metadados de erro permitidos sem corpo bruto

Para os endpoints de sincronização, acrescentar ao transporte protegido uma classificação estrita de `code` (`SYNC_RUN_ALREADY_ACTIVE`, `SYNC_RUN_MINIMUM_INTERVAL_ACTIVE`, `ACCEPTANCE_RESULT_INDETERMINATE`) e dos campos tipados/validados `nextAllowedAtUtc`, `syncRunId` e `Retry-After` se aplicáveis. Não passar `HttpErrorResponse`, títulos, mensagens, payloads externos ou chaves para o componente/logs. Preservar reação existente do interceptor a 401/403 e proteção da geração; falha de rede em POST é resultado potencialmente indeterminado, não rejeição comprovada. 503 sem código específico também não prova rollback: consultar estado/permitir replay da mesma chave, sem afirmar certeza. Alternativa rejeitada: mostrar `error.error.title` ou tratar todo 503 como recusa definitiva.

### 5. Polling finito e atualização de leitura

Após 202 válido, consultar `GET /api/sync-runs/{id}` com intervalo limitado (por exemplo, 2 segundos por até 60 segundos) somente enquanto há sessão/geração e a página está ativa; após limite, erro de consulta ou estado terminal, parar timers. Para estado terminal, reler `sync-state` e cartões sem novo POST; mostrar importação concluída somente quando a execução consultada confirma sucesso e o novo snapshot associa a ela o último sucesso (mesmo `syncRunId`, estado bem-sucedido e horário de sucesso presente), ou sinalizar confirmação pendente se a leitura falhar. Ação manual de atualizar estado usa só GET; retorno a `/area` pode reler GET sem recomeçar on-entry nem polling infinito. Comparar a geração em cada resposta e cancelar no destroy/logout/expiração; resultados tardios não podem repovoar dados. Alternativa rejeitada: polling contínuo e novo POST ao expirar o timeout. Intervalo/duração são parâmetros de UX/testes, não alteram a janela nem os retries do executor do servidor.

### 6. Fronteira visual e provas

Usar HTML sem métricas monetárias, textos de execução/ausência distintos de cobertura bancária, aria-live para mudanças importantes, foco/teclado e disposição responsiva já praticada. Testes de serviço/componente com HttpTestingController e relógio simulado cobrem GET independente, on-entry por geração, idempotência/replay, 200/202/409/429/503/401/403, transições da SyncRun, cancelamento, dados antigos e privacidade. Regressão de guard/interceptor/logout e builds mantêm a fronteira da FNR-020. Teste com API local, quando viável em ambiente isolado, deve ser identificado separadamente dos mocks; CORS/infraestrutura real não são comprovados por testes de unidade.

## Risks / Trade-offs

- [Interceptor hoje esconde metadados da política] → extensão mínima com allowlist por endpoint, sem propagar corpo bruto, e testes de não regressão para respostas de sessão e URLs externas.
- [Resposta 202 pode preceder processamento e confirmação da importação] → acompanhamento por ID e verificação do estado persistido; sem transformar timeout/falha em sucesso.
- [Sessão expira no meio do GET/POST e respostas chegam depois] → cancelar e comparar geração antes de atualizar UI, sem expor dados de geração anterior.
- [POST já aceito perde a resposta; retry pode parecer nova intenção] → reter chave em memória e oferecer replay explícito; recarga encerra sessão e não garante reconciliação automática.
- [Conexão ausente, broker desligado, provedor exige reautorização] → informar limites e preservar histórico exibido; consulta GET continua independente de broker quando API/banco disponíveis.
- [Contrato testado localmente não garante CORS, limites Pluggy nem broker hospedado] → registrar validações como locais e manter publicação/integração real nos gates próprios.

## Migration Plan

Sem migration de banco ou rota nova. Substituir o placeholder `/area` e ajustar rótulos de navegação, mantendo login/guard/logout; rollback da entrega pelo PR restaura a área informativa anterior sem mudar dados persistidos no servidor. A revisão do `docs/BACKLOG.md` 0.68 é pendente e precisa ser reconciliada com o merge local do PR #38 e submetida a revisão como gate documental antes de implementar; não presumir aprovação deste plano nem da integração hospedada.
