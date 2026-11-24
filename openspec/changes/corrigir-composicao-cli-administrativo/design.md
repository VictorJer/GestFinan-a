# Design

## Context

Ver [proposal.md](proposal.md). O ramo de `user-create`/`user-reset-password` usa `CreateHostBuilder` e `BuildHost` em `backend/Finora.Api/AdministrativeUsers/AdministrativeUserCommand.cs`; o builder carrega defaults do Generic Host antes de qualquer limpeza posterior da configuração. `AddAdministrativeUserHandlers` varre o assembly Application inteiro, mas `BuildHost` registra somente a infraestrutura administrativa. `RunAsync` captura falhas de composição e devolve código 1 com `failure=operational`, sem exceção pública. O teste existente `AdministrativeHostDoesNotRegisterHttpPluggyOrLoggingProviders` constrói o host somente em Production; os testes de Identity isolada não exercitam a varredura MediatR. A spec principal `administrative-user-access` já descreve isolamento e saída sanitizada; não há delta de comportamento.

## Goals / Non-Goals

**Goals:** demonstrar a falha de composição original e sua correção no host real; garantir que a reprodução jamais leia configuração pessoal; verificar o mesmo contêiner com `ValidateOnBuild` e `ValidateScopes` verdadeiros nos dois ambientes e preservar o contrato de saída.

**Non-Goals:** exercitar prompts reais, banco, usuário, migrations, serviços de HTTP/provedor/broker, alterar configuração efetiva do comando ou silenciar validações para contornar dependências ausentes.

## Decisions

1. **Isolamento anterior à criação do host:** introduzir, somente se necessário à reprodução, um ponto de configuração interno de `CreateHostBuilder` que aceite `HostApplicationBuilderSettings` ou opção equivalente para testes, deixando o caminho de produção com defaults inalterados. No teste, chamar o caminho administrativo com `EnvironmentName` explícito (`Development` ou `Production`) e `DisableDefaults = true` **antes** de `Host.CreateApplicationBuilder`; fornecer apenas configuração em memória e uma string sintética sem senha para `BuildHost`. Com `DisableDefaults`, não configurar providers de User Secrets, JSON/appsettings, argumentos nem variáveis de ambiente; conferir a lista de fontes e o valor sintético, sem enumerar valores externos. Configurar explicitamente uma `DefaultServiceProviderFactory` com `ValidateOnBuild = true` e `ValidateScopes = true` no builder isolado; a desativação dos defaults não deve desligar a validação. Alternativas rejeitadas: criar builder normal e chamar `Sources.Clear()` depois (fontes já podem ter sido lidas), trocar ambiente para Production ou executar o comando real.
2. **Reprodução como gate de correção:** no estado anterior à alteração de registro, exercitar `CreateHostBuilder(Development)` pelo caminho isolado e `BuildHost` com conexão sintética; capturar no teste somente o tipo/nome das dependências ausentes e a etapa da exceção, sem segredos. Repetir com Production e validação explícita para distinguir a falta de cobertura anterior do comportamento de DI. Se a falha não vier da varredura, não adicionar serviços por tentativa nem mudar validação; registrar a limitação e revisar o plano antes de editar código fora do escopo.
3. **Registro mínimo MediatR:** após reprodução, conservar `AddMediatR` e restringir `TypeEvaluator` aos tipos concretos `CheckAdministrativeUserPreflightHandler`, `CreateAdministrativeUserHandler` e `ResetAdministrativeUserPasswordHandler`. Manter `ISender` e sua resolução em escopos existentes, sem registrar handlers HTTP/financeiros/Pluggy nem componentes RabbitMQ. Alternativa rejeitada: preencher DI com dependências de outros handlers, porque mistura contextos e pode provocar efeitos externos. Conferir que a allowlist permanece suficiente para os três requests administrativos e que a composição não inclui serviços proibidos.
4. **Regressão e privacidade:** testes MSTest em `AdministrativeUserCommandTests` (ou arquivo adjacente) constroem e descartam o host em ambos os ambientes, verificam os três handlers, ausência dos demais handlers e de HTTP/Pluggy/RabbitMQ, ausência de conexão aberta, serviços e logs externos. Para a borda sanitizada, injetar falha sintética de `BuildHost` em `RunAsync` com builder isolado e console falso não invocado; esperar código 1, stdout vazio e stderr exatamente `User admin: failure=operational`, sem texto da exceção ou sentinelas. Preservar os testes existentes de argumentos e configuração inválida sem ler segredos pessoais.

## Risks / Trade-offs

- [O construtor do host em Development lê fontes reais mesmo no teste] → usar `DisableDefaults` na configuração inicial, nunca limpar providers após o builder, e inspecionar fontes **sem seus valores** antes da execução.
- [O isolamento de defaults reduz serviços ou validação usados pelo runtime] → comparar composição com o host administrativo real e habilitar explicitamente ambas as validações em testes; manter o caminho operacional padrão inalterado.
- [O filtro MediatR omite handler administrativo necessário] → resolver os três `IRequestHandler<,>` em escopo e inspecionar descritores para ausência de handlers não administrativos; rodar somente regressões cuja configuração esteja isolada antes do builder, sem banco ou segredos reais.
- [Outra falha é responsável por `failure=operational`] → parar no gate de reprodução, registrar a causa observada de forma sanitizada e submeter revisão do escopo antes de qualquer solução adicional.

## Migration Plan

Não há migration ou mudança de contrato. A entrega só poderá seguir o fluxo normal de revisão/PR após aprovação da implementação e validação local; eventual rollback é reversão via PR da alteração de composição/testes, sem tocar em dados, configuração pessoal ou schema.
