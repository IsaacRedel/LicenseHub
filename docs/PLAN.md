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
| Painel | Blazor |
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
| **License** | produto, cliente, hash da chave, status (ativa, revogada, expirada), validade, máximo de ativações |
| **Activation** | licença, identificador da instância (`instanceId`), primeira e última validação |
| **ValidationLog** | licença (vazia quando a chave não existe), resultado, IP, data |
| **PaymentEvent** | id do evento no gateway, para não processar o mesmo pagamento duas vezes |

## Decisões técnicas

1. **A chave fica salva como hash, igual a uma senha.** O cliente recebe `LH-XXXX-XXXX-XXXX-XXXX` uma vez só, e o banco guarda apenas o hash SHA-256.
2. **A resposta da validação é assinada.** A API assina o resultado com uma chave privada (ECDSA), e o cliente tem a chave pública embutida. Ninguém consegue montar um servidor falso que responda "licença válida".
   - ECDSA P-256 com SHA-256 (ES256), assinatura no formato IEEE P1363 (`r || s`, 64 bytes).
   - A resposta é `{ payload, signature, keyId }`: `payload` é o JSON em base64url, e a assinatura cobre exatamente esses bytes, nunca um JSON re-serializado.
   - O `keyId` são os 16 primeiros caracteres do base64url do SHA-256 da chave pública, o que prepara a troca de chave.
   - A chave privada vem de configuração (`Signing:PrivateKey`), nunca do repositório.
3. **Proteção contra replay.** O cliente manda um `nonce` e um timestamp, a resposta assinada devolve os dois, e o cliente rejeita respostas antigas ou repetidas. O servidor recusa com `400` um timestamp fora de ±5 minutos do seu relógio.
4. **Rate limiting** com o middleware nativo do ASP.NET Core, particionado por IP e mais rígido em `validate`/`deactivate`, para limitar tentativas de adivinhar chaves. O `X-Forwarded-For` só é aceito de proxies configurados.
5. **Webhook idempotente.** O mesmo evento de pagamento chegando duas vezes gera uma licença só.
6. **Ativações sem corrida.** A ativação roda numa transação que trava a linha da licença (`SELECT … FOR UPDATE`), então instâncias novas validando ao mesmo tempo nunca passam do máximo de ativações.

Limite conhecido: licenciamento dificulta a pirataria casual, mas não impede quem edita o código do cliente.

## Endpoints

| Método e rota | Acesso | O que faz |
| --- | --- | --- |
| `POST /v1/licenses/validate` | público, rate limited | valida a chave e devolve a resposta assinada |
| `POST /v1/licenses/deactivate` | público, rate limited | libera uma ativação |
| `GET /v1/signing-key` | público | chave pública (PEM) e `keyId` para verificar as respostas |
| `POST /admin/products` | JWT de admin | cria um produto |
| `POST /admin/licenses` | JWT de admin | emite uma licença (a chave aparece uma única vez) |
| `POST /admin/licenses/{id}/revoke` | JWT de admin | revoga uma licença |
| `GET /admin/licenses?status=&page=` | JWT de admin | lista com filtro e paginação |
| `GET /admin/stats` | JWT de admin | validações por dia e licenças ativas |
| `POST /webhooks/payments` | assinatura do gateway | gera a licença quando o pagamento é aprovado |
| `GET /health` | público | verificação de saúde |

### Contrato da validação

- **Pedido:** `{ key, productSlug, instanceId, nonce, timestamp }`.
  - `nonce` tem de 16 a 128 caracteres base64url.
  - `timestamp` é em segundos Unix.
- **Payload assinado:** `{ valid, reason, licenseId, expiresAt, nonce, instanceId, issuedAt }`, com datas em segundos Unix.
- **Resultados de negócio:** voltam `200` com `valid=false` e um `reason`, também assinados.
  - Os valores de `reason` são `unknown_key`, `product_mismatch`, `revoked`, `expired` e `activation_limit_reached`.
  - Em `unknown_key` e `product_mismatch`, `licenseId` e `expiresAt` vêm nulos.
- **Outros status:** requisição malformada ou timestamp fora da janela dá `400` (problem details com `code`), e excesso de chamadas dá `429` com `Retry-After`.
- **Log:** toda tentativa que passa da validação de entrada gera um `ValidationLog`.
- **`deactivate`:** responde `204`, ou o mesmo `404` para chave ou instância desconhecida, para não revelar se a chave existe.

## Roteiro por etapas

Cada etapa termina com algo funcionando e publicado. A etapa 2 já é publicável: uma API pequena, assinada, testada e com CI.

| # | Etapa | Entrega |
| --- | --- | --- |
| 0 | Base | Solução e projetos criados, Docker Compose com MariaDB, CI rodando o build |
| 1 | Domínio | Entidades, migrations, emissão de licença e CRUD de produtos, com testes de unidade |
| 2 | Validação | Validação com ativações, assinatura ECDSA, nonce, rate limit e testes de integração |
| 3 | Admin | Login com JWT, revogação, listagem com paginação e estatísticas |
| 4 | Painel | Blazor com dashboard, lista de licenças e botão de revogar |
| 5 | Automação | Webhook do Mercado Pago em modo de teste e job que expira licenças vencidas |
| 6 | Vitrine | Clientes de exemplo (C# e FiveM), README com diagrama, GIF e release v1.0.0 |

## Como testar

Quem quiser testar clona, roda `docker compose up` e já tem API, banco com dados de exemplo e painel. A página do OpenAPI deixa testar a validação direto no navegador.

No README entra um GIF do fluxo completo: emitir uma licença, validar, revogar e ver a validação falhar.

- [ ] Confirmar na etapa 6 como verificar a assinatura dentro do recurso FiveM. Lua puro não tem criptografia, então a ideia é fazer essa parte em JavaScript no lado servidor do recurso.
