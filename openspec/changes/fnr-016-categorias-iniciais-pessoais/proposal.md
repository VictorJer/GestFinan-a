# Proposal

## Why

A FNR-015 já oferece leitura protegida de movimentos e suas categorias previamente associadas, mas ainda não há catálogo de categorias iniciais nem operação para criar categorias pessoais. A FNR-016 fornece esse catálogo sem antecipar a edição de movimentos da FNR-017 ou a interface da FNR-022.

## What Changes

- Provisionar de modo explícito e idempotente sete categorias persistidas de sistema com IDs estáveis: Alimentação, Transporte, Compras, Lazer, Saúde, Educação e Outros. Expor também “Sem categoria” como opção virtual de ausência, com ID nulo; nunca persistir um registro especial nem usar UUID vazio como ID público.
- Oferecer `GET /api/categories` para listar categorias de sistema e do titular, em ordem previsível, e `POST /api/categories` para criar categoria pessoal a partir de nome validado, derivando o titular exclusivamente da sessão.
- Definir duplicidade por chave que ignora caixa, acentos e espaços redundantes. Bloquear nomes pessoais equivalentes aos iniciais ou a “Sem categoria”, inclusive sob concorrência, sem impedir que titulares diferentes usem o mesmo nome pessoal.
- Preservar rotas e contratos existentes; não adicionar renomeação, exclusão, arquivamento, edição de anotação, interface ou agregados financeiros. A FNR-017 aplicará categorias a movimentos e a FNR-022 integrará o seletor visual.
- Registrar como preparação documental da futura implementação a reconciliação do estado histórico FNR-015: `main` local contém o merge do PR #29, enquanto BACKLOG 0.53 e TDD 0.23 ainda registram pré-integração. Aprovações anteriores e as duas falhas históricas da suíte PostgreSQL ampla permanecem explícitas; não declarar publicação ou aprovação de checks GitHub.

## Capabilities

### New Capabilities

- `category-catalog`: provisionamento de opções iniciais e consulta/criação protegida de categorias pessoais com normalização e isolamento por titular.

### Modified Capabilities

Nenhuma. `financial-data-persistence` já exige unicidade por escopo, categoria ausente distinta e proteção contra associação alheia; `card-movement-query` já lê categoria existente, sem CRUD. Esta mudança acrescenta o catálogo e a criação, sem alterar esses contratos.

## Impact

- Rastreabilidade: FNR-016 (depende de FNR-007/011), PRD RF06 e §8, TDD §15/DT-44; habilita FNR-017 e, posteriormente, FNR-018/022. O aceite destes itens posteriores não é antecipado.
- Backend: políticas de nome no Domain/Application, consulta/criação em Infrastructure e rotas em Api, testes de contrato/concorrência com PostgreSQL descartável. A tabela `Category` já existe com chave única `(ScopeId, NormalizedName)` e `TransactionAnnotation` já aceita ausência por campos nulos; não provisionar dados nem aplicar migration automaticamente no startup da API.
- O login administrativo atual recusa mais de uma conta cadastrada; a FNR-016 não altera essa política. A capacidade de escopo para dois titulares distintos é verificada em PostgreSQL/Application com dados sintéticos, enquanto HTTP prova sessão válida única, revogação e ausência de escolha de titular pelo cliente. Isso não simula nem afirma dois logins simultâneos operacionais.
- Documentação canônica: um eventual alinhamento de BACKLOG/TDD será apresentado e aprovado em revisão própria antes de código dependente; esta proposta não edita esses documentos, não aprova a implementação nem autoriza integração/publicação.
