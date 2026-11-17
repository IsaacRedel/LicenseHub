# LicenseHub — Plano do projeto

## A ideia

**LicenseHub** é uma API em .NET para emitir e validar licenças de software, com respostas assinadas criptograficamente, painel de administração e integração com pagamento.

O primeiro caso de uso real é proteger e vender scripts de FiveM.

## Stack

O projeto usa .NET 10, a versão LTS com suporte até novembro de 2028 ([política de suporte da Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy)).

| Camada | Tecnologia |
| --- | --- |
| API | ASP.NET Core (.NET 10) |
| Banco | EF Core + MariaDB, com migrations |
| Painel | Blazor Web App (Interactive Server), com login no próprio servidor do painel |
| Testes | xUnit + Testcontainers (MariaDB real nos testes de integração) |
| Infra | Docker Compose (API + MariaDB + painel), GitHub Actions |
| Documentação | OpenAPI com interface para testar os endpoints |

## Estrutura

Os clientes (recurso FiveM ou app C#, painel Blazor e webhook do Mercado Pago) só falam com a API. As regras ficam no Domain, e o banco e a criptografia ficam na Infrastructure.

```
LicenseHub/
├─ src/
│  ├─ LicenseHub.Domain/          # entidades e regras (sem dependências)
│  ├─ LicenseHub.Application/     # casos de uso: emitir, validar, revogar
│  ├─ LicenseHub.Infrastructure/  # EF Core, MariaDB, criptografia
│  ├─ LicenseHub.Api/             # endpoints, rate limit, autenticação
│  └─ LicenseHub.Admin/           # painel Blazor
├─ tests/
│  ├─ LicenseHub.UnitTests/
│  └─ LicenseHub.IntegrationTests/
├─ samples/
│  ├─ csharp-client/              # app de console que valida uma licença
│  └─ fivem-resource/             # exemplo de recurso FiveM usando a API
└─ docker-compose.yml
```

## Modelo de dados

| Entidade | Principais campos |
| --- | --- |
| **Product** | nome, slug, limite padrão de ativações |
| **License** | produto, cliente, hash da chave, status (ativa, revogada, expirada), validade, máximo de ativações; na revogação, data, motivo e admin |
| **Activation** | licença, identificador da instância (`instanceId`), primeira e última validação |
| **ValidationLog** | licença (vazia quando a chave não existe), resultado, IP, data |
| **PaymentEvent** | id do evento no gateway, para não processar o mesmo pagamento duas vezes |
| **AdminUser** | email, hash da senha, criado em, último login, falhas seguidas e fim do bloqueio |

## Decisões técnicas

1. **A chave fica salva como hash, igual a uma senha.** O cliente recebe `LH-XXXX-XXXX-XXXX-XXXX` uma vez só, e o banco guarda apenas o hash SHA-256.
2. **A resposta da validação é assinada.** A API assina o resultado com uma chave privada (ECDSA), e o cliente tem a chave pública embutida. Ninguém consegue montar um servidor falso que responda "licença válida".
   - ECDSA P-256 com SHA-256 (ES256), assinatura no formato IEEE P1363 (`r || s`, 64 bytes).
   - A resposta é `{ payload, signature, keyId }`: `payload` é o JSON em base64url, e a assinatura cobre exatamente esses bytes, nunca um JSON re-serializado.
   - O `keyId` são os 16 primeiros caracteres do base64url do SHA-256 da chave pública, o que prepara a troca de chave.
   - A chave privada vem de configuração (`Signing:PrivateKey`), nunca do repositório.
3. **Proteção contra replay.** O cliente manda um `nonce` e um timestamp. A resposta assinada devolve o `nonce`, o horário de emissão (`issuedAt`), o produto e a impressão da chave, e o cliente rejeita respostas antigas, repetidas ou de outro pedido. O servidor recusa com `400` um timestamp fora de ±5 minutos do seu relógio.
4. **Rate limiting** com o middleware nativo do ASP.NET Core, particionado por IP e mais rígido em `validate`/`deactivate`, para limitar tentativas de adivinhar chaves. O `X-Forwarded-For` só é aceito de proxies configurados.
5. **Webhook idempotente.** O mesmo evento de pagamento chegando duas vezes gera uma licença só.
6. **Ativações sem corrida.** A ativação roda numa transação que trava a linha da licença (`SELECT … FOR UPDATE`), então instâncias novas validando ao mesmo tempo nunca passam do máximo de ativações. Revogar e liberar uma ativação usam o mesmo lock.
7. **Admin com JWT curto, sem Identity completo.**
   - O login devolve um JWT HS256 de 30 minutos, sem refresh token.
   - A chave HMAC (`AdminAuth:JwtKey`, 256 bits ou mais) é separada da chave ECDSA das licenças.
   - A senha usa só o `PasswordHasher` do ASP.NET Core (PBKDF2, salt aleatório, formato versionado). Tabelas, cookies e fluxos do Identity não fazem falta num painel com poucos admins.
   - Email inexistente, senha errada e conta bloqueada recebem a mesma resposta. Cinco falhas seguidas bloqueiam a conta por 15 minutos.
   - O login tem rate limit próprio, e não existe admin padrão: o primeiro é criado com o comando `admin create`.
8. **Painel como BFF: o navegador nunca vê o JWT.**
   - O login é uma página renderizada no servidor, com formulário e antiforgery. O painel chama a API e guarda o JWT como claim dentro do próprio cookie, criptografado pelo Data Protection.
   - O cookie é HttpOnly, Secure e SameSite=Strict e expira junto com o JWT, sem renovação. As chaves do Data Protection ficam num volume, para a sessão sobreviver a um reinício.
   - As telas usam Interactive Server com prerender desligado. Um `CircuitHandler` lê o token do usuário quando o circuito abre, e um `DelegatingHandler` o envia como `Bearer`, acessando os serviços do circuito pelo padrão `CircuitServicesAccessor` da documentação.
   - Um `401` da API marca o token como expirado e leva a `/sessao-expirada?sessao=<id>`, que faz o `POST /logout` com antiforgery e volta ao login com "Sessão expirada". O `id` são 16 caracteres do SHA-256 do token. Se o cookie já for de outra sessão (um login mais novo em outra aba), a página só recarrega o painel.
   - As rotas interativas usam `AuthorizeRouteView`, então o `[Authorize]` vale também na navegação dentro do circuito.
   - Um registro em memória liga cada sessão aos circuitos abertos. O logout tira o token de todas as abas e recusa o mesmo cookie até ele expirar. Ele é local a uma instância do painel e se perde num reinício.
   - O painel mostra "Muitas tentativas" quando a API responde `429`. O bloqueio da conta continua indistinguível de senha errada, como na decisão 7.
   - CSP sem exceções: o Blazor funcionou com `script-src 'self'` e `style-src 'self'`, sem scripts nem estilos inline. Por isso o painel não usa o `<ImportMap>` do template, e tamanhos dinâmicos ficam em atributos SVG.
9. **Rate limit por admin e proxy confiável.**
   - Chamadas autenticadas em `/admin/*` são limitadas por admin (`sub` do token), não por IP. Assim, todos os admins atrás do painel não dividem a mesma cota.
   - No login, o painel repassa o IP do navegador em `X-Forwarded-For`. A API só aceita esse cabeçalho do IP fixo do painel numa sub-rede fixa do compose, então o limite de login vale por IP de quem acessa o painel. Atrás de um proxy reverso, o painel usa as mesmas regras de `ForwardedHeaders` da API.
10. **Dashboard calculado no banco, no fuso do painel.**
    - `GET /admin/stats/overview` agrega no MariaDB os números do dia, os 30 dias, os motivos de recusa e o uso por produto. O dia do calendário segue `tzOffsetMinutes`, que o painel calcula a partir de `Dashboard:TimeZone` (padrão `America/Sao_Paulo`). Limite conhecido: um único deslocamento vale para os 30 dias, então, em fusos com horário de verão, dias anteriores à mudança ficam com uma hora de diferença. O padrão não tem horário de verão desde 2019.
    - `GET /admin/validations` devolve as últimas validações, também filtradas por licença no detalhe.
    - O rank do dia (D, C, B, A, S, SSS) é calculado no painel por uma classe pura, com os limites numa constante. Sem validações no dia, mostra "—".
    - O id curto exibido nas listas usa os 8 últimos dígitos do GUID. Os primeiros são o timestamp do GUID v7 e se repetiam entre licenças criadas no mesmo minuto.

Limite conhecido: licenciamento dificulta a pirataria casual, mas não impede quem edita o código do cliente.

## Endpoints

| Método e rota | Acesso | O que faz |
| --- | --- | --- |
| `POST /v1/licenses/validate` | público, rate limited | valida a chave e devolve a resposta assinada |
| `POST /v1/licenses/deactivate` | público, rate limited | libera uma ativação |
| `GET /v1/signing-key` | público | chave pública (PEM) e `keyId` para verificar as respostas |
| `POST /admin/auth/login` | público, rate limited | troca email e senha por um JWT de 30 minutos |
| `POST /admin/products` | JWT de admin | cria um produto |
| `GET /admin/products` | JWT de admin | lista os produtos |
| `GET /admin/products/{id}` | JWT de admin | detalha um produto |
| `PATCH /admin/products/{id}` | JWT de admin | muda nome e limite padrão de ativações (o slug é imutável) |
| `POST /admin/licenses` | JWT de admin | emite uma licença (a chave aparece uma única vez) |
| `GET /admin/licenses?status=&productSlug=&page=&pageSize=` | JWT de admin | lista com filtro por status efetivo e produto, paginação e ordenação estável |
| `GET /admin/licenses/{id}` | JWT de admin | detalhes e ativações, sem a chave nem o hash completo |
| `POST /admin/licenses/{id}/revoke` | JWT de admin | revoga uma licença com motivo (idempotente) |
| `DELETE /admin/licenses/{id}/activations/{instanceId}` | JWT de admin | libera uma vaga de ativação |
| `GET /admin/stats` | JWT de admin | licenças por status, validações por dia e produtos mais validados |
| `GET /admin/stats/overview?tzOffsetMinutes=` | JWT de admin | números do dashboard do painel, com o dia no fuso informado |
| `GET /admin/validations?limit=&licenseId=` | JWT de admin | últimas validações, opcionalmente de uma licença |
| `POST /webhooks/payments` | assinatura do gateway | gera a licença quando o pagamento é aprovado |
| `GET /health` | público | verificação de saúde |

### Contrato da validação

- **Pedido:** `{ key, productSlug, instanceId, nonce, timestamp }`.
  - `nonce` tem de 16 a 128 caracteres base64url.
  - `timestamp` é em segundos Unix.
- **Payload assinado:** `{ valid, reason, licenseId, expiresAt, productSlug, keyFingerprint, nonce, instanceId, issuedAt }`, com datas em segundos Unix.
  - `productSlug` e `keyFingerprint` ligam a resposta ao produto e à chave do pedido. `keyFingerprint` são os 16 primeiros caracteres do base64url do SHA-256 da chave como foi enviada.
  - Sem esses campos, um servidor falso poderia validar uma licença de outro produto repassando o nonce do cliente. O cliente deve comparar todos esses campos com o pedido antes de confiar em `valid`.
- **Resultados de negócio:** voltam `200` com `valid=false` e um `reason`, também assinados.
  - Os valores de `reason` são `unknown_key`, `product_mismatch`, `revoked`, `expired` e `activation_limit_reached`.
  - Em `unknown_key` e `product_mismatch`, `licenseId` e `expiresAt` vêm nulos.
- **Outros status:** requisição malformada ou timestamp fora da janela dá `400` (problem details com `code`), e excesso de chamadas dá `429` com `Retry-After`.
- **Log:** toda tentativa que passa da validação de entrada gera um `ValidationLog`.
- **`deactivate`:** responde `204`, ou o mesmo `404` para chave ou instância desconhecida, para não revelar se a chave existe.

## Roteiro por etapas

Cada etapa termina com algo funcionando e publicado. A etapa 2 já é publicável: uma API pequena, assinada, testada e com CI.

| # | Etapa | Entrega | Status |
| --- | --- | --- | --- |
| 0 | Base | Solução e projetos criados, Docker Compose com MariaDB, CI rodando o build | concluída |
| 1 | Domínio | Entidades, migrations, emissão de licença e CRUD de produtos, com testes de unidade | concluída |
| 2 | Validação | Validação com ativações, assinatura ECDSA, nonce, rate limit e testes de integração | concluída |
| 3 | Admin | Login com JWT, revogação, listagem com paginação e estatísticas | concluída |
| 4 | Painel | Blazor com login próprio, dashboard, produtos, licenças, emissão e revogação, em dois temas | concluída |
| 5 | Automação | Webhook do Mercado Pago em modo de teste e job que expira licenças vencidas | pendente |
| 6 | Vitrine | Clientes de exemplo (C# e FiveM), README com diagrama, GIF e release v1.0.0 | pendente |

## Como testar

Quem quiser testar clona, roda `docker compose up` e já tem API, banco com dados de exemplo e painel. A página do OpenAPI deixa testar a validação direto no navegador.

No README entra um GIF do fluxo completo: emitir uma licença, validar, revogar e ver a validação falhar.

- [ ] Confirmar na etapa 6 como verificar a assinatura dentro do recurso FiveM. Lua puro não tem criptografia, então a ideia é fazer essa parte em JavaScript no lado servidor do recurso.
