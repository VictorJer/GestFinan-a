# Design

## Context

Ver `proposal.md` para a motivação. O BACKLOG §5 exige evidência sanitizada e aceite humano do P0 local antes de P1; PRD §13 define o núcleo revisado, e TDD §16 distingue checkpoint local de validação hospedada. As specs principais já cobrem vínculo administrativo, coleta paginada, persistência, execução recuperável, acesso e interface; nenhuma delas precisa mudar para avaliar o checkpoint.

O registro em `docs/AMBIENTE.md` contém provas PostgreSQL/RabbitMQ com leitor financeiro sintético, testes de UI com HTTP simulado e um ensaio Chromium com preflight corrigido e POST 503 deliberado. FNR-039 vincula o item sem importar; a saída `linked` não comprova movimentos. A integração com origem real, banco persistente e fluxo completo no navegador não foi demonstrada por esses ensaios. A prova MQ-P01 de broker local tem limites e ressalva de imagem próprios, que não devem ser encobertos por CI.

## Goals / Non-Goals

**Goals:**

- Produzir matriz auditável de critérios P0 com requisito/ID, fonte, tipo da prova (real autorizada, sintética, documental ou hospedada), resultado, limites, lacuna e decisão separada.
- Estabelecer uma sequência segura de verificação local, incluindo parada honesta diante de autorização, destino ou infraestrutura ausente; apresentar ao titular evidência suficiente para decidir o checkpoint.

**Non-Goals:**

- Alterar comportamento, implementar correções, criar migration, aprovar risco de segurança, criar dados reais artificiais ou operar ambiente hospedado.
- Declarar aceite FNR-020, FNR-021/M-03, MQ-P01–04 ou FNR-028 por consequência automática do checkpoint.

## Decisions

### 1. Matriz de provas, não checklist binário

Cruzar BACKLOG §5 e PRD §13 com FNR-008–023/038/039 e specs principais, separando: autenticação/revogação e propriedade; vínculo de item; importação paginada/repetida e persistência; falha parcial e retomada; leitura/lista/filtros/contagens/cobertura; anotações após reimportação; recorrência assistida; apresentação e atualização de entrada/manual no navegador. Para cada critério, registrar a fonte verificável e se a prova trata a aplicação real, somente uma fixture sintética ou uma observação histórica. Contagens, horários, estado e códigos sanitizados podem ser evidência; payload bruto, ID externo, token, credenciais, descrições e valores pessoais não.

**Alternativa rejeitada:** usar apenas `13/13` tarefas, checks de CI ou merge como conclusão. Esses indicadores não cobrem integração com a origem nem aceites específicos.

### 2. Dois trilhos de verificação, com autorização antes do trilho real

Trilho controlado: reutilizar resultados já registrados e, somente após aprovação dos artefatos e autorização de execução, repetir testes seletivos em ambiente isolado e sintético para bordas difíceis de provocar com dados reais (falha em página intermediária, rollback, replay, duplicatas, natureza desconhecida e retomada). Trilho operacional: **somente após autorização explícita adicional**, verificar item Pluggy e destino local persistente, configuração/segredos protegidos, titular e migrations já aprovadas; observar vínculo e executar sincronização elegível pelo fluxo previsto, acompanhar a mesma `SyncRun` até resultado confirmado e comparar duas importações com leitura persistida, cobertura e anotações. Confirmar antes de cada ação escopo, destino, efeito esperado e possibilidade de retorno; nunca reaproveitar a connection string de teste como prova de persistência nem executar migration automaticamente. Se o ambiente não estiver apto, parar e registrar o motivo, sem substituir origem real por mock.

**Alternativa rejeitada:** rodar experimentos com Pluggy ou banco pessoal a partir da aprovação do plano. A autorização de planejamento não cobre consulta/gravação operacional, segredos ou efeitos externos.

### 3. Gates independentes e decisão sem atalhos

Comparar estado de execução, cobertura e leitura API/UI em um mesmo recorte identificado; a aceitação HTTP `202` prova intenção durável, não importação concluída. Registrar separadamente evidência de POST 200/202 e conclusão/erro visível no navegador, além de FNR-021/M-03; registrar o alcance exato do ensaio MQ-P01 e suas ressalvas sem tratá-lo como aprovação de produção. Ausência de prova exigida pelo P0 mantém o checkpoint **bloqueado**. Mesmo com matriz completa, somente o titular pode aceitar explicitamente o escopo P0 local; aceites separados não surgem por inferência. FNR-029–031 só passam a ser elegíveis depois desse aceite, em mudanças próprias.

**Alternativa rejeitada:** agregar resultados de GETs, 503 CORS e testes de broker sintéticos em uma única afirmação de sucesso end-to-end.

### 4. Documentação prospectiva e lacunas fora do escopo

Na execução futura, acrescentar provas sanitizadas e suas limitações em `docs/AMBIENTE.md` e estado factual no BACKLOG §7, sem reescrever fotografias históricas nem marcar aceite antecipadamente. Se uma prova revelar defeito ou mudança de requisito, abrir mudança própria e revisar o plano afetado antes de qualquer correção. O checkpoint permanece pendente até nova prova e decisão.

**Alternativa rejeitada:** corrigir código ou ajustar critérios silenciosamente durante a coleta de evidências; isso prejudica rastreabilidade e o gate pré-código.

## Risks / Trade-offs

- [Origem indisponível ou autorização não concedida] → Registrar bloqueio concreto; não substituir prova real exigida por fixture, nem liberar P1.
- [Dado sensível na evidência] → Manter somente indicadores e resultados sanitizados; conferir anexos/logs antes de registrar ou compartilhar.
- [Broker/serviço parado, outbox pendente ou imagem com ressalvas] → Inspecionar estado e gate aplicável antes de ensaios autorizados; não remover volume, purgar fila ou trocar digest como atalho.
- [Janela real não contém bordas necessárias] → Complementar com testes sintéticos rotulados; não afirmar que tais bordas foram observadas na origem.
- [Importação aceita mas não concluída] → Acompanhar estado persistido e leitura posterior; sem confirmação de promoção, classificar como pendência/falha, não sucesso.

## Migration Plan

Não há deploy nem migration nesta mudança de validação. Caso o destino operacional autorizado requeira schema ainda não aplicado, interromper a prova e tratar a migration pelo processo administrativo do TDD, com validação local e autorização própria antes do uso; jamais aplicá-la no startup. Em falha de execução, preservar dados/obrigações, interromper novas intenções e documentar a recuperação segura, sem reset/purge de recursos preexistentes.
