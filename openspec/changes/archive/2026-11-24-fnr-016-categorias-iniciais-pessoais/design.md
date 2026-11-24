# Design

## Context

Ver [proposal.md](proposal.md) e [delta do catálogo](specs/category-catalog/spec.md). A tabela `Category` já possui `Id`, `UserId`, `IsSystem`, `Name`, `NormalizedName`, `ScopeId` e índice único `(ScopeId, NormalizedName)` com collation `C`; categorias do sistema usam escopo UUID vazio, pessoais usam o ID do titular. `TransactionAnnotation` já representa ausência por `CategoryId` e `CategoryScopeId` nulos. `GET /api/transactions` lê vínculos existentes, mas não há API de catálogo/criação nem seed. A API usa sessão validada, MediatR, leitores de Infrastructure e Problem Details sanitizado. PRD RF06 e TDD §15 definem o escopo, mas a política executável de nomes e a representação de “Sem categoria” ainda não estavam aprovadas no TDD canônico; as decisões abaixo são proposta para revisão.

## Goals / Non-Goals

**Goals:**

- Entregar catálogo seguro para clientes futuros, com IDs estáveis, provisionamento repetível e criação concorrente sem duplicações.
- Aproveitar a integridade atual sem aplicar migrations ou gravar catálogo na inicialização da API; explicitar o gate operacional do provisionamento.
- Fixar uma única política de normalização, ordenação e representação de ausência que os clientes possam consumir.

**Non-Goals:**

- Não categorizar movimentos nesta entrega nem modificar `GET /api/transactions` para inferir categorias; edição fica em FNR-017 e a seleção visual em FNR-022.
- Não implantar provedor externo, frontend, heurística de compra, métricas monetárias ou rotina de autoreparação silenciosa de dados conflitantes.

## Decisions

### 1. Provisionamento explícito no ciclo de migrations

Adicionar migration de dados apenas para as **sete** categorias persistidas, com UUIDs fixos e chaves normalizadas produzidas pela mesma política da aplicação; reaplicação via histórico do EF é inócua e o comando administrativo de migration continua fora do startup. Usar escrita idempotente e verificação transacional de que nomes, escopo e IDs reservados coincidem exatamente; conflito legado deve abortar com diagnóstico operacional sem sobrescrever categoria ou associação existente. Testar tanto instalação limpa como upgrade de schema já migrado, repetição e colisão preexistente. Durante publicação, aplicar a migration de forma compatível antes de disponibilizar a API que depende das categorias; nunca fazer seed na subida do host. **Alternativa rejeitada:** `HasData` ou seed a cada startup, que pode alterar dados em inicialização ou falhar health sem banco. Se a ferramenta de migration não permitir prova de repetição independente do histórico, a prova também deve executar o bloco idempotente isolado em banco descartável, sem editar migration anterior.

### 2. Chave textual única e estável

Na fronteira de criação, rejeitar caracteres de controle, aparar extremidades e colapsar espaços Unicode redundantes no nome apresentado. Limitar nome e chave resultantes a 60 caracteres; rejeitar excesso, nunca truncar. Formar a chave com decomposição Unicode, remoção de marcas diacríticas combinantes, recomposição e maiúsculas invariantes; persistir a chave pronta em `NormalizedName`. Usar a mesma função e versão de política para iniciais e pessoais; testes cobrem Saúde/Saude, caixa, espaços repetidos, Unicode precomposto/decomposto, limites e sequências que expandem a chave. A comparação armazenada usa a collation `C` já configurada; não depender de collation regional nem do provedor para a transformação. Reservar as sete chaves iniciais e a chave de “Sem categoria” para todos os titulares, mesmo antes de consultar o banco. **Alternativas rejeitadas:** distinguir acentos (contraria a escolha do titular), usar `ToLower` dependente de cultura ou trocar a collation/índice sem necessidade. Formas não equivalentes pela regra definida não são deduplicadas semanticamente por adivinhação.

### 3. Criação isolada e conflito concorrente

`POST /api/categories` aceita apenas `{ "name": "..." }`; deserialização/validação estrita rejeita campos extras, ID ou titular explícito. O controller extrai `UserId` da sessão, Application valida nome/chave e Infrastructure grava `Category.Create` com escopo pessoal. Verificar chave reservada e existente para retornar 409; manter o índice único como árbitro final da corrida e mapear **somente** a violação de `(ScopeId, NormalizedName)` para 409, deixando outras falhas como 500/503 sanitizados conforme tipo. Não transformar duplicidade do banco em 400 nem vazar texto de constraint. 201 retorna somente ID interno, nome e `kind=personal`, sem `Location` que prometa GET individual inexistente. **Alternativa rejeitada:** confiar apenas em checagem prévia sem índice, que permite duplicata concorrente.

### 4. Leitura e contrato de opções

`GET /api/categories` consulta sete categorias de sistema e categorias do titular diretamente (sem depender de `FinancialConnection` ou Pluggy). Responder array de `{ id, name, kind }`: `kind=system`/`personal` têm UUID interno e `kind=unassigned` tem `id: null`; ordenar categorias iniciais pela ordem fixa do PRD, depois pessoais por chave/ID, terminando na opção virtual “Sem categoria”. A opção virtual é construída no DTO, nunca na tabela; os campos nulos de anotação existentes continuam significando ausência. Uma ausência ou inconsistência no catálogo provisionado deve produzir 503 sanitizado em vez de lista aparentemente completa; health permanece independente. **Alternativa rejeitada:** persistir oitava categoria, que perderia a distinção atual entre ausência e seleção real. Não incluir campo de categoria externa ou valores monetários na resposta.

O login administrativo vigente responde indisponível quando há mais de uma conta cadastrada. Para provar isolamento sem mudar essa decisão, testes PostgreSQL/Application exercitam dois titulares sintéticos e consulta/criação pelo ID de cada um; HTTP exercita a sessão única, revogação e ausência de parâmetro de titular. Não emitir dois tokens por caminho de teste que contorne a política real nem declarar login multiusuário entregue. Esta precisão de prova mantém o requisito de isolamento de dados e a fronteira de FNR-016.

### 5. Revisão de contratos antes do código dependente

Na fase de execução, reconciliar BACKLOG 0.53/TDD 0.23 com o merge `a858677` observado em `main` local, sem inferir checks ou reviews da plataforma, e propor revisão canônica de TDD §15/DT-44 para a política de nomes, opção virtual e provisionamento. Apresentar diff e obter aprovação documental e dos quatro artefatos desta mudança antes de alterar comportamento dependente. Não estender aprovação de PRD/TDD anterior a esta nova política, nem declarar corrigidas as duas falhas de fixtures PostgreSQL registradas na FNR-015.

## Risks / Trade-offs

- [IDs/nome de sistema legados incompatíveis] → migration aborta integralmente com diagnóstico, sem sobrescrever vínculos; testar banco atualizado e vazio antes da implantação em destino.
- [Chaves Unicode amplificam comprimento ou deixam equivalências linguísticas não capturadas] → documentar algoritmo determinístico, rejeitar chave acima de 60 e cobrir entradas precompostas/decompostas; mudar política futura requer migração/versão e revisão de colisões.
- [Consulta sem migration aplicada] → 503 claro e sanitizado, sem seed implícito; operacionalizar aplicação explícita antes da disponibilização das novas rotas.
- [Falha entre precheck e INSERT, ou identificação errada de erro SQL] → prova PostgreSQL com barreira concorrente, inspeção específica de constraint e tratamento de falhas não relacionadas sem converter tudo em 409.
- [Suíte PostgreSQL ampla histórica 179/181] → executar regressão focal da FNR-016 e registrar separadamente as duas fixtures incompatíveis até reparo autorizado; não afirmar suíte completa verde nem usar banco pessoal/Neon.

## Migration Plan

1. Revisar e aprovar artefatos/documentos canônicos pertinentes; gerar migration aditiva de dados para catálogo, sem alterar migrations anteriores ou o schema das anotações.
2. Validar migration em PostgreSQL descartável vazio e atualizado, com testes de conflito, reaplicação, rollback da transação e preservação de IDs/anotações; não aplicar automaticamente no startup.
3. Antes de integrar/publicar uma API que dependa do catálogo, executar a migration compatível no destino via processo administrativo do TDD, conferindo versão/estado. Se houver conflito, parar o rollout e reconciliar com autorização; não apagar dados existentes.
4. Para reverter código, manter os sete registros referenciáveis: não executar exclusão automática de categorias, especialmente quando já ligadas a anotações. Uma eventual reversão de dados requer plano explícito e conferência de referências.
