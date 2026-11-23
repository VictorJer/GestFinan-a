# MQ-P01 — comparação documental dos scans RabbitMQ

## Escopo e método

Em 22/11/2026, por autorização explícita, foram comparadas as imagens Ubuntu já configurada e Alpine candidata, ambas somente para análise. Foi usado Trivy portátil **0.76.2** (ZIP Windows x64 SHA-256 `691f737a7e5566a673c3a74dab9160552ec32101e953b659e5c77443d16dddb6`). O Docker Engine estava indisponível; Trivy leu os manifests/camadas diretamente do registry público, sem importar as imagens ao daemon. Nenhum container, broker ou `rabbitmqadmin` foi executado e não foram usados credenciais nem serviços que recebam arquivos do projeto.

As análises ocorreram aproximadamente entre 01:05 e 01:09, horário local `-03:00`. Os dois scans de camadas usaram exatamente `--image-src remote`, `--platform linux/amd64`, `--scanners vuln`, `--vuln-type os,library`, `--list-all-pkgs`, saída JSON, sem filtro de severidade e sem ignorar achados sem correção. `--vuln-type` é a opção legada equivalente a `--pkg-types`; foi aceita com aviso de depreciação. Ambos reutilizaram a mesma base por `--skip-db-update`. Os scans complementares `--sbom-sources oci` e do SPDX usaram `--pkg-types` com os mesmos tipos de pacote e a mesma base.

Base Trivy v2, baixada uma vez de `mirror.gcr.io/aquasec/trivy-db:2` em 22/11/2026 e mantida no cache local. `trivy-db-metadata.json` registra `UpdatedAt=2026-11-22T01:10:45.128389923Z`, `NextUpdate=2026-11-23T01:10:45.128389399Z` e `DownloadedAt=2026-11-22T04:04:37.4031162Z`. SHA-256 do arquivo `trivy.db` no cache: `B1ACB84C7552B79E3B3458664CB1C145542DA75E5CCA35860180AA43025FF79A` (1.812.348.928 bytes). A cópia do metadata está preservada junto dos relatórios.

## Imagens e resultado do scan de camadas

| Imagem analisada | Plataforma e digest do manifesto | Inventário OS detectado | Achados Trivy: crítico / alto / médio / baixo |
|---|---|---:|---:|
| `rabbitmq:4.3.8-management` | `linux/amd64`, `sha256:2df9f514f59d7c16f142e8320bbeb61f278fde1430fbad3a7a4f4b0b677f2dc6` (índice OCI registrado: `sha256:d1150154e5bb43773513548d2b1f541bb6d63bb49377baf2e21de303c2c62bb3`) | Ubuntu 24.04, 97 pacotes | **0 / 0 / 30 / 5** (35 no total) |
| `rabbitmq:4.3.8-management-alpine` | `linux/amd64`, `sha256:cca1aab97f823e9fe7c9c7e2b8528c590a091c15ea7a53c4b290553108f7759d` (índice OCI registrado: `sha256:977a6cf07ea9065116aaa40e7dc147ad25621d71a654248d36d79e5e7500be3e`) | Alpine 3.23.8, 30 pacotes | **0 / 0 / 1 / 0** (1 no total) |

O único achado da camada Alpine foi `CVE-2026-4738` em `zlib` `1.3.2-r1`, severidade **média** no Trivy, com correção indicada `1.4-r0`. Na imagem Ubuntu, o Trivy reconheceu o pacote do sistema `gosu` versão `1.17`; na Alpine reconheceu `su-exec` `0.3-r0`. Nenhum scan de camada gerou achado alto/crítico.

Também foram tentadas análises com `--sbom-sources oci`, preservadas como `ubuntu-oci-sbom.*` e `alpine-oci-sbom.*`. O Trivy não registrou SBOM OCI encontrada: os relatórios resultantes continuam limitados aos pacotes do sistema (97 e 30, respectivamente), e os logs registram zero arquivos de linguagem. Portanto esses resultados **não** demonstram cobertura do binário Go `gosu`, do CLI Rust, do RabbitMQ compilado, do Erlang/OTP ou do OpenSSL instalado fora do gerenciador de pacotes.

## CLI Management e dependências Rust

Os Dockerfiles oficiais da variante Management instalam `rabbitmqadmin` v2.35.0: o [Dockerfile Alpine](https://github.com/docker-library/rabbitmq/blob/master/4.3/alpine/management/Dockerfile) fixa o artefato musl por SHA-256 e o [Dockerfile Ubuntu](https://github.com/docker-library/rabbitmq/blob/master/4.3/ubuntu/management/Dockerfile) fixa o artefato GNU. A release upstream disponibiliza `rabbitmqadmin-2.35.0.spdx.json`; o arquivo foi preservado e seu SHA-256 conferido (`267b6eeccab1188a63baca1fa150cc332066a10287c4dacf1eaf2a247c2162cb`). Esse SPDX é inventário da release/projeto, não SBOM OCI encontrado nas imagens nem prova da seleção de features de cada binário.

O Trivy analisou esse SPDX, com a mesma base local e sem atualizá-la: 249 pacotes Cargo, **0 críticos, 0 altos, 2 médios e 2 baixos**, todos em `rustls 0.23.43`:

| Advisory | Componente/versão | Severidade Trivy | Correção indicada | Natureza/condição descrita |
|---|---|---:|---|---|
| [GHSA-2mjx-qc3c-rqvc](https://osv.dev/vulnerability/GHSA-2mjx-qc3c-rqvc) / RUSTSEC-2026-0285 | `rustls 0.23.43` | Média | `0.23.45` | Mensagens TLS 1.3 aceitas no nível de criptografia incorreto. |
| [GHSA-c8m5-c952-6cm4](https://osv.dev/vulnerability/GHSA-c8m5-c952-6cm4) / RUSTSEC-2026-0346 | `rustls 0.23.43` | Média | `0.23.46` | `Debug` de valores de sessão pode expor segredos quando usado por armazenamento customizado. |
| [GHSA-h2v8-394m-p7vv](https://osv.dev/vulnerability/GHSA-h2v8-394m-p7vv) / RUSTSEC-2026-0348 | `rustls 0.23.43` | Baixa | `0.23.46` | DoS por alocação na descompressão de certificado; requer feature Brotli não habilitada por padrão. |
| [GHSA-w44r-93j7-qv28](https://osv.dev/vulnerability/GHSA-w44r-93j7-qv28) / RUSTSEC-2026-0349 | `rustls 0.23.43` | Baixa | `0.23.46` | Downgrade condicional de key exchange em conexões posteriores, conforme política do servidor. |

Essas são correspondências de pacote/versão e advisory; nenhuma alcançabilidade no `rabbitmqadmin` foi demonstrada. O [manifesto](https://github.com/rabbitmq/rabbitmqadmin-ng/blob/v2.35.0/Cargo.toml) declara `rustls`, mas as opções TLS, dependências opcionais e caminhos de execução do binário não foram ensaiados. O workflow de release constrói o alvo musl sem features padrão; o manifesto declara HTTP/2, não HTTP/3.

Uma consulta metadata-only adicional ao OSV já havia identificado outras correspondências no SPDX que **não aparecem no JSON do Trivy**. São triagem complementar, não resultados contados na tabela Trivy:

- `rustls-webpki 0.103.14`, RUSTSEC-2026-0382, correção `0.103.16`; e `webpki-root-certs 1.0.9`, RUSTSEC-2026-0383, correção `1.0.10`: cadeia/âncora de confiança em certificados.
- `tokio 1.53.1`, RUSTSEC-2026-0386, correção `1.53.3`: soundness em construtores Unix `AsyncFd`; uso vulnerável não comprovado.
- `rustix 1.1.4`, RUSTSEC-2026-0486, correção `1.1.6`: funções específicas de xattr com buffer vazio; caminho alcançável não comprovado.
- `quinn-proto 0.11.17`, RUSTSEC-2026-0338/0340/0343 corrigidas em `0.11.18` e 0523/0524 em `0.11.19`; `quinn-udp 0.5.15`, RUSTSEC-2026-0538 corrigida em `0.5.16`. São dependências QUIC; HTTP/3 não consta nas features selecionadas do manifesto, e a advisory de `quinn-udp` exclui Linux.
- `proc-macro-error2 2.0.1`, RUSTSEC-2026-0173, e `chrono 0.4.45`, RUSTSEC-2026-0543, têm advisories de falta de manutenção, não CVEs com versão corretiva. `proc-macro-error2` aparece via macro procedural e é dependência de build; inclusão no executável não é inferida.

O inventário SPDX/Cargo.lock não classifica com segurança tudo como runtime, opcional ou build para o artefato musl; exceções acima são as que podem ser inferidas do manifesto/dependências sem compilar. O Trivy não encontrou dependências Cargo nas camadas das imagens. A ausência de uma dependência em qualquer uma dessas SBOMs/relatórios não é garantia de ausência de vulnerabilidade.

## Achados Scout anteriores e limites da comparação

O registro anterior do Docker Scout v1.26.0 para o Ubuntu atribuiu **6 críticos e 34 altos** a `golang/stdlib 1.22.2` em `/usr/sbin/gosu`; os seis críticos registrados são CVE-2025-68121, CVE-2026-56857, CVE-2024-24790, CVE-2026-39821, CVE-2026-78663 e CVE-2025-22871. A lista bruta dos 34 altos não foi preservada. O Trivy desta execução reconheceu `gosu` como pacote OS, mas não identificou arquivos/dependências Go (log: zero arquivos de linguagem) nem reproduziu esses CVEs. **Isso não invalida nem fecha os achados Scout**: são scanners/bases diferentes e a detecção do binário Go não foi confirmada.

Como referência separada, foram consultados os registros upstream do Go Vulnerability Database/OSV para os seis críticos. As versões abaixo são correções upstream reportadas, não prova de que o `gosu` instalado as alcance ou de que exista rebuild compatível da linha Go 1.22.2:

| Advisory | Componente/superfície identificada | Versões corrigidas reportadas | Classificação e alcance no `gosu` Linux |
|---|---|---|---|
| [CVE-2025-68121](https://pkg.go.dev/vuln/GO-2026-4337) | `stdlib 1.22.2`, `crypto/tls`, retomada de sessão com configuração de CAs mutável | Go `1.24.13`, `1.25.7`, `1.26.0-rc.3` | Dependência de runtime Go em helper de sistema; não há uso de TLS/session resumption demonstrado no caminho de troca de usuário. |
| [CVE-2026-56857](https://pkg.go.dev/vuln/GO-2026-6604) | `stdlib 1.22.2`, `os.Root.Mkdir(All)` seguindo junctions | Go `1.26.9`, `1.27.2` | **Específica de Windows** segundo o registro Go; não se aplica ao binário executado em Linux. |
| [CVE-2024-24790](https://pkg.go.dev/vuln/GO-2024-2887) | `stdlib 1.22.2`, `net/netip.Addr.Is*` em IPv4-mapped IPv6 | Go `1.21.11`, `1.22.4` | Helper de sistema; o caminho de troca de usuário não demonstra essas chamadas. |
| [CVE-2026-39821](https://pkg.go.dev/vuln/GO-2026-5026) | `stdlib 1.22.2`, tratamento IDNA usado por `net/http` | Go `1.25.13`, `1.26.6`, `1.27.0-rc.3`; `golang.org/x/net` `0.55.0` | Superfície de HTTP/IDNA; nenhuma chamada HTTP/IDNA foi comprovada no `gosu`. |
| [CVE-2026-78663](https://pkg.go.dev/vuln/GO-2026-6612) | `stdlib 1.22.2`, flow-control de streams HTTP/2 | Go `1.26.9`, `1.27.2` | Superfície de servidor HTTP/2; não há servidor HTTP/2 demonstrado no `gosu`. |
| [CVE-2025-22871](https://pkg.go.dev/vuln/GO-2025-3563) | `stdlib 1.22.2`, parsing de chunks HTTP com request smuggling | Go `1.23.8`, `1.24.2` | Superfície de parsing HTTP; não há serviço HTTP demonstrado no `gosu`. |

As descrições/funções tornam várias críticas aparentemente não relacionadas à troca de usuário observada, mas não foi feita análise de chamadas ou VEX do binário do digest. O achado de versão continua registrado como não reconciliado. Para os **34 altos**, sem IDs/componentes brutos não é possível indicar correções por advisory.

Consequentemente, “0 alto/crítico” significa somente zero achados dessa severidade **nos inventários e na base Trivy utilizados**; não é atestado de segurança do servidor RabbitMQ nem do binário Go. Alcançabilidade de código, VEX, configuração efetiva e compatibilidade operacional continuam sem prova.

## Management e recomendação

Documentalmente, `MqP01CompatibilityTests` usa PostgreSQL/outbox e AMQP no loopback (porta 5672), declara endpoint MassTransit e não acessa a porta 15672, API de Management ou `rabbitmqadmin`. Portanto Management não é requisito funcional desse teste de compatibilidade tal como escrito. Pode servir para diagnóstico manual e inspeção operacional; a topologia `MQ-P02` aprovada, porém, escolheu explicitamente `-management` e expõe o painel apenas em loopback.

**Recomendação:** não aprovar nem trocar para Alpine com base somente nestes scans. A camada Alpine tem menos achados OS no banco Trivy utilizado e não contém o pacote `gosu`, mas os achados Go anteriores permanecem não reconciliados, o scan não cobriu o binário/servidor, existem achados no CLI incluído em Management e o Dockerfile oficial avisa que Alpine não é suportado oficialmente pela equipe RabbitMQ. Não houve ensaio Alpine. Manter o digest configurado e o gate MQ-P01 pausado; qualquer variante sem Management ou troca de digest exige decisão explícita/revisão do desenho e validação de compatibilidade autorizada.

## Arquivos preservados

Os relatórios JSON completos e logs estão neste diretório: `ubuntu-layers.*`, `alpine-layers.*`, `ubuntu-oci-sbom.*`, `alpine-oci-sbom.*`, `rabbitmqadmin-2.35.0-spdx-trivy.*`, além do SPDX upstream e `trivy-db-metadata.json`. SHA-256 dos principais artefatos:

| Arquivo | SHA-256 |
|---|---|
| `ubuntu-layers.json` | `F7F799745A79C496E55F9F0EE729D7D06C7F7AFEBDD4944B3E978AABBCAAA8C0` |
| `alpine-layers.json` | `B6FD92491093D70665EBF8C2C625B919587783952D1A4C24D29D902583FC2CC4` |
| `ubuntu-oci-sbom.json` | `E476F5661E82F20E207B3032035AA6F41556130896F7E155E638C1A3E23A00F6` |
| `alpine-oci-sbom.json` | `7F62CDCDB0ABBB6484D230C1C97C9FE22A264A1000AF3C94C2CA0C4DDC01B53E` |
| `rabbitmqadmin-2.35.0.spdx.json` | `267B6EECCAB1188A63BACA1FA150CC332066A10287C4DACF1EAF2A247C2162CB` |
| `rabbitmqadmin-2.35.0-spdx-trivy.json` | `B895A4D6FBBAB04CF44C69A6434AC6CE1EABB67246AB554C2AC40CC0CE9A876E` |
| `trivy-db-metadata.json` | `B2180A1DBEE063490794CFDC1930544CD3E7DBB03BCB081982ECA6A26A7F48B6` |

Os resultados locais não alteram o digest nem aceitam risco residual. Nenhum teste, broker, banco, Pluggy, hospedagem, commit ou PR foi executado/criado nesta etapa.
