# Spec Delta

## Purpose

Disponibilizar ao titular autenticado um catálogo previsível de categorias iniciais e pessoais, com criação protegida e tratamento inequívoco da ausência de categoria.

## ADDED Requirements

### Requirement: Categorias iniciais estáveis e sem duplicação
O sistema SHALL provisionar de forma explícita e idempotente as categorias de sistema Alimentação, Transporte, Compras, Lazer, Saúde, Educação e Outros, com identidade estável e independentes de titular. SHALL não criar categoria persistida especial para “Sem categoria” nem alterar registros pessoais durante o provisionamento.

#### Scenario: Repetir provisionamento
- **WHEN** o provisionamento é executado novamente após inicialização ou atualização do schema
- **THEN** as sete categorias mantêm os mesmos IDs e não são duplicadas, e as categorias pessoais existentes permanecem intactas

#### Scenario: Dados de sistema conflitantes
- **WHEN** já existe uma categoria de sistema incompatível com nome ou ID reservado
- **THEN** o provisionamento rejeita o conflito sem substituir silenciosamente dados existentes ou alterar associações

### Requirement: Ausência de categoria como opção virtual
A API SHALL representar “Sem categoria” na lista como opção com `id` nulo e tipo `unassigned`, distinta das categorias persistidas de sistema e pessoais. A ausência de categoria SHALL continuar representada por identificadores nulos na anotação; SHALL não expor UUID vazio como identificador de opção.

#### Scenario: Listar opções e ausência
- **WHEN** o titular solicita a lista após provisionar as categorias iniciais
- **THEN** recebe as sete categorias persistidas e a opção “Sem categoria” com `id` nulo, sem oitavo registro de sistema

#### Scenario: Movimento não categorizado
- **WHEN** um movimento não possui categoria escolhida
- **THEN** sua ausência permanece distinta de uma categoria persistida; listar opções não cria anotação nem atribui categoria automaticamente

### Requirement: Listagem protegida e isolada
A API SHALL oferecer `GET /api/categories` somente a sessão válida, listando as categorias de sistema e apenas as pessoais do titular, sem depender da existência de conexão financeira. SHALL ordenar a opção virtual, as iniciais e as pessoais de forma previsível, sem disparar importação ou contato com provedor.

#### Scenario: Titulares com categorias diferentes
- **WHEN** duas identidades sintéticas distintas têm categorias pessoais com nomes coincidentes ou distintos no PostgreSQL descartável
- **THEN** a consulta Application/Infrastructure por cada ID retorna as sete categorias de sistema, a opção virtual e somente as categorias daquele ID; no HTTP, uma sessão válida única não pode escolher titular nem acessar dados alheios, sem exigir dois logins simultâneos que o contrato administrativo atual recusa

#### Scenario: Sem sessão válida
- **WHEN** a listagem é solicitada sem sessão ativa ou com sessão revogada
- **THEN** recebe 401 com challenge Bearer e não retorna categorias

### Requirement: Criação pessoal com nome válido
A API SHALL oferecer `POST /api/categories` com somente `name` no corpo, derivando o titular da sessão e criando uma categoria pessoal para ele. O nome de apresentação SHALL ser obrigatório após aparar espaços das extremidades, ter até 60 caracteres e não conter caracteres de controle; o servidor SHALL rejeitar campos que tentem escolher ID, titular ou tipo de categoria.

#### Scenario: Criar categoria válida
- **WHEN** um titular autenticado cria uma categoria com nome válido não reservado
- **THEN** recebe 201 com ID interno, nome e tipo pessoal; a categoria aparece em suas listagens posteriores sem modificar movimentos ou classificações existentes

#### Scenario: Entrada inválida ou tentativa de alterar escopo
- **WHEN** o nome é vazio, excede o limite, contém controle ou o corpo tenta escolher titular, ID ou tipo
- **THEN** recebe 400 sanitizado sem criar categoria de sistema ou de outro titular

### Requirement: Chave consistente e duplicidade evitada
O sistema SHALL comparar nomes de categoria por chave que ignora caixa, acentos e espaços redundantes. SHALL impedir repetição pessoal no mesmo titular e nomes equivalentes a qualquer categoria inicial ou “Sem categoria”; titulares diferentes SHALL poder criar o mesmo nome não reservado. Colisões concorrentes SHALL produzir no máximo uma criação e conflito sanitizado para a outra.

#### Scenario: Acentos, caixa e espaços
- **WHEN** um titular tenta criar “SAUDE”, “ saude ” ou “saúde”, ou tenta recriar um nome pessoal alterando caixa, acentos e espaços internos
- **THEN** recebe 409 e nenhuma segunda categoria equivalente é criada

#### Scenario: Reserva da opção virtual
- **WHEN** um titular tenta criar nome equivalente a “Sem categoria”
- **THEN** recebe 409 e a opção virtual continua sendo a única representação de ausência

#### Scenario: Mesmo nome para titulares diferentes
- **WHEN** a criação Application/Infrastructure recebe dois IDs de titulares sintéticos para “Projetos” em escopos distintos
- **THEN** ambos os escopos persistem categorias pessoais próprias e cada consulta isolada por ID não vê a outra; isso não modifica a política de login administrativo de conta única

#### Scenario: Criações concorrentes no mesmo escopo
- **WHEN** duas requisições do mesmo titular criam simultaneamente nomes equivalentes
- **THEN** exatamente uma é persistida e a outra recebe 409 sem erro bruto do banco

### Requirement: Falhas sanitizadas e sem efeitos financeiros
As rotas de categorias SHALL usar erros HTTP sanitizados existentes e cumprir limites de requisições protegidas; SHALL não criar sincronização, não alterar movimentos ou anotações e não calcular nem retornar indicadores monetários. Indisponibilidade conhecida da persistência SHALL produzir 503; erro inesperado SHALL produzir 500.

#### Scenario: Banco indisponível ou excesso de requisições
- **WHEN** a persistência está indisponível ou o limite de requisições protegidas é excedido
- **THEN** a chamada recebe respectivamente 503 ou 429 sanitizado, sem criação parcial

#### Scenario: Catálogo sem modificar histórico
- **WHEN** o titular lista ou cria categoria enquanto movimentos já foram importados
- **THEN** os movimentos e suas anotações permanecem inalterados e nenhuma execução de sincronização é iniciada
