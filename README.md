<h1 align="center">🔑 LicenseHub</h1>

<p align="center">
  API de licenciamento de software em <b>.NET 10</b>: emissão de licenças, validação com resposta assinada (ECDSA), proteção contra replay, rate limiting e painel de administração.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/status-em%20constru%C3%A7%C3%A3o-orange?style=for-the-badge" alt="Status: em construção">
  <img src="https://img.shields.io/badge/.NET_10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/C%23-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt="C#">
  <img src="https://img.shields.io/badge/MariaDB-003545?style=for-the-badge&logo=mariadb&logoColor=white" alt="MariaDB">
  <img src="https://img.shields.io/badge/Docker-2496ED?style=for-the-badge&logo=docker&logoColor=white" alt="Docker">
</p>

---

## 🧭 Sobre

O LicenseHub emite chaves de licença para softwares e valida essas chaves pela internet. A resposta de cada validação é assinada criptograficamente, então um servidor falso não consegue se passar pela API. O primeiro caso de uso real é proteger e vender scripts de FiveM.

O plano completo (arquitetura, modelo de dados, endpoints e decisões técnicas) está em [`docs/PLAN.md`](docs/PLAN.md).

## 🗺️ Roteiro

- [x] **Etapa 0 — Base:** solução, Docker Compose com MariaDB e CI
- [x] **Etapa 1 — Domínio:** entidades, migrations e emissão de licenças
- [x] **Etapa 2 — Validação:** resposta assinada (ECDSA), ativações, nonce e rate limit
- [x] **Etapa 3 — Admin:** login com JWT, revogação e estatísticas
- [ ] **Etapa 4 — Painel:** dashboard em Blazor
- [ ] **Etapa 5 — Automação:** webhook de pagamento e expiração automática
- [ ] **Etapa 6 — Vitrine:** clientes de exemplo (C# e FiveM) e release v1.0.0

## 🚀 Como rodar

Pré-requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) e [Docker](https://www.docker.com/products/docker-desktop/).

```bash
# 1. Configure as senhas locais (o .env não vai para o Git)
cp .env.example .env

# 2. Suba a API e o MariaDB
docker compose up -d --build

# 3. Confira a saúde da API
curl http://localhost:8080/health

# 4. Crie um produto demo e uma licença (só em Development; a chave aparece uma única vez)
docker compose run --rm api seed
```

Em Development, a documentação interativa (Scalar) fica em <http://localhost:8080/scalar>, e o arquivo [`LicenseHub.Api.http`](src/LicenseHub.Api/LicenseHub.Api.http) tem exemplos de todas as chamadas.

As portas ficam presas a `127.0.0.1`: API em `8080` e MariaDB em `3307`. As duas podem ser trocadas no `.env`.

Para compilar e rodar os testes (os de integração sobem um MariaDB com Testcontainers, então o Docker precisa estar rodando):

```bash
dotnet build
dotnet test
```

No Docker Compose, a API aplica as migrations ao iniciar. Para criar uma nova migration:

```bash
dotnet tool restore
dotnet ef migrations add NomeDaMigration --project src/LicenseHub.Infrastructure --startup-project src/LicenseHub.Infrastructure --output-dir Persistence/Migrations
```

### Chaves

A API usa duas chaves separadas:

- **Chave de assinatura:** a chave privada ECDSA P-256 que assina as respostas de validação.
- **Chave dos tokens de admin:** uma chave HMAC de no mínimo 256 bits.

Em Development, se uma delas não estiver configurada, a API gera uma temporária a cada inicialização e avisa no log. Com a chave de admin temporária, os tokens deixam de valer quando a API reinicia. Em qualquer outro ambiente, a API não inicia sem as duas.

Para gerar as chaves:

```bash
# Com Docker (não precisa do banco)
docker compose run --rm --no-deps api keygen

# Ou com o SDK
dotnet run --project src/LicenseHub.Api -- keygen
```

O comando imprime a linha pronta para cada forma de configuração:

- **Docker Compose:** `SIGNING_PRIVATE_KEY=...` e `ADMIN_JWT_KEY=...` no `.env`;
- **Variáveis de ambiente:** `Signing__PrivateKey=...` e `AdminAuth__JwtKey=...`;
- **`dotnet run` local:** `dotnet user-secrets set "Signing:PrivateKey" "..."` e `dotnet user-secrets set "AdminAuth:JwtKey" "..."`.

As chaves privadas nunca vão para o repositório. A chave pública de assinatura é servida em `GET /v1/signing-key` e deve ser embutida nos clientes.

## 🛠️ Área de admin

As rotas `/admin/*` gerenciam produtos e licenças e exigem um token de admin, exceto o login.

### 1. Crie o primeiro admin

Não existe usuário nem senha padrão. O comando funciona em qualquer ambiente e pede a senha duas vezes, sem mostrar na tela (mínimo de 12 caracteres):

```bash
# Com Docker
docker compose run --rm api admin create --email voce@exemplo.com

# Ou com o SDK
dotnet run --project src/LicenseHub.Api -- admin create --email voce@exemplo.com

# Em automação, a senha pode vir da entrada padrão
printf '%s\n' "$ADMIN_PASSWORD" | docker compose run --rm -T api admin create --email voce@exemplo.com --password-stdin
```

### 2. Faça login

```bash
curl -s http://localhost:8080/admin/auth/login \
  -H 'Content-Type: application/json' \
  -d '{ "email": "voce@exemplo.com", "password": "sua-senha" }'
```

```json
{ "accessToken": "eyJhbGciOi...", "tokenType": "Bearer", "expiresAt": "2026-11-16T12:30:00Z" }
```

- **Validade:** o token vale 30 minutos e não há refresh token. Quando expirar, faça login de novo.
- **Mensagem única:** email inexistente, senha errada e conta bloqueada recebem o mesmo `401`, para a resposta não revelar quais contas existem.
- **Bloqueio:** 5 falhas seguidas bloqueiam a conta por 15 minutos.
- **Rate limit:** o login aceita 5 tentativas por minuto por IP.

### 3. Use o token

Envie o token no cabeçalho `Authorization`:

```bash
TOKEN="eyJhbGciOi..."

# Criar um produto
curl -s http://localhost:8080/admin/products -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{ "name": "FiveM Garage", "slug": "fivem-garage", "defaultMaxActivations": 2 }'

# Emitir uma licença (a chave aparece só nesta resposta)
curl -s http://localhost:8080/admin/licenses -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{ "productId": "<id do produto>", "customerEmail": "cliente@exemplo.com" }'
```

No Scalar (<http://localhost:8080/scalar>), faça o login e cole o `accessToken` no campo de autenticação Bearer, que vale para as rotas de admin. No arquivo `.http`, o login preenche o token das chamadas seguintes automaticamente.

| Rota | O que faz |
| --- | --- |
| `POST /admin/auth/login` | troca email e senha por um token de 30 minutos |
| `POST` / `GET /admin/products` | cria e lista produtos |
| `GET` / `PATCH /admin/products/{id}` | detalha um produto ou muda nome e limite padrão de ativações (o `slug` não muda) |
| `POST /admin/licenses` | emite uma licença; a chave aparece só nesta resposta |
| `GET /admin/licenses?status=&productSlug=&page=&pageSize=` | lista com filtro e paginação: `status` aceita `active`, `revoked` ou `expired`, e `pageSize` vai até 100 |
| `GET /admin/licenses/{id}` | detalhes, ativações e revogação; nunca a chave nem o hash completo |
| `POST /admin/licenses/{id}/revoke` | revoga com `{ "reason" }`; repetir não muda a revogação original |
| `DELETE /admin/licenses/{id}/activations/{instanceId}` | libera uma vaga de ativação |
| `GET /admin/stats` | licenças por status, validações por dia (30 dias) e produtos mais validados |

Na listagem, o `status` é o status efetivo: uma licença ativa com `expiresAt` vencido aparece como `expired`.

## 🔐 Como validar uma licença

```bash
curl -s http://localhost:8080/v1/licenses/validate \
  -H 'Content-Type: application/json' \
  -d "{
    \"key\": \"LH-XXXX-XXXX-XXXX-XXXX\",
    \"productSlug\": \"demo\",
    \"instanceId\": \"meu-servidor-01\",
    \"nonce\": \"$(openssl rand -hex 16)\",
    \"timestamp\": $(date +%s)
  }"
```

| Campo | Regra |
| --- | --- |
| `key` | Chave da licença. Maiúsculas, minúsculas e espaços são normalizados. |
| `productSlug` | Produto para o qual a chave deve valer. |
| `instanceId` | Identifica o servidor ou a máquina. Cada instância nova ocupa uma ativação. |
| `nonce` | De 16 a 128 caracteres base64url, gerados pelo cliente a cada chamada. |
| `timestamp` | Relógio do cliente em segundos Unix. Fora de ±5 minutos do servidor, a resposta é `400`. |

Quando a requisição é válida, a resposta é `200`, inclusive quando a licença não vale, e vem assinada:

```json
{ "payload": "eyJ2YWxpZCI6dHJ1ZSwi...", "signature": "dF1uO8I4y7J1hJry5ApN...", "keyId": "q1Zr8xT0aJ3mVb2c" }
```

O `payload` é o JSON abaixo codificado em base64url:

```json
{ "valid": true, "reason": null, "licenseId": "019a…", "expiresAt": null,
  "productSlug": "demo", "keyFingerprint": "vnWY8h7rOBEVyrIY",
  "nonce": "…", "instanceId": "meu-servidor-01", "issuedAt": 1763300002 }
```

- `productSlug`: o slug pedido, em minúsculas.
- `keyFingerprint`: os 16 primeiros caracteres do base64url do SHA-256 da chave **exatamente como foi enviada** (UTF-8).
- `instanceId`: o valor enviado, sem espaços nas pontas.

Esses campos ligam a resposta ao pedido. Sem eles, um servidor falso poderia validar uma licença barata de outro produto e repassar a resposta assinada.

| `reason` | Significado |
| --- | --- |
| `null` | Licença válida; a instância foi ativada ou já estava ativa. |
| `unknown_key` | A chave não existe ou está em formato inválido. |
| `product_mismatch` | A chave é de outro produto (o `licenseId` não é revelado). |
| `revoked` | A licença foi revogada. |
| `expired` | A licença venceu. |
| `activation_limit_reached` | A licença já está ativa no número máximo de instâncias. |

Outros status:
- `400`: requisição malformada ou timestamp fora da janela, com o `code` do erro no corpo.
- `429`: excesso de chamadas por IP; o cabeçalho `Retry-After` diz quantos segundos esperar.

Para liberar uma ativação, envie `POST /v1/licenses/deactivate` com `{ "key", "instanceId" }`. A resposta é `204` quando a ativação é liberada e `404` quando a chave ou a instância não estão ativas.

### Como verificar a assinatura

O cliente precisa fazer, nesta ordem:

1. Decodificar o `payload` de base64url para bytes.
2. Verificar a `signature` (ES256 no formato IEEE P1363: `r || s`, 64 bytes) sobre **esses bytes**, com a chave pública **embutida no cliente**. Não confie numa chave baixada na hora do mesmo servidor, porque um servidor falso serviria a própria chave.
3. Só então ler o JSON. Confira:
   - se `productSlug`, `keyFingerprint`, `nonce` e `instanceId` correspondem ao que você enviou;
   - se `issuedAt` é recente.

   Só depois disso confie em `valid`. Essas checagens impedem que uma resposta de outro pedido, de outro produto ou antiga seja reaproveitada.

Node.js (servidor de um recurso FiveM, por exemplo):

```js
const { createHash, createPublicKey, verify } = require('node:crypto');

const bytes = Buffer.from(response.payload, 'base64url');
const ok = verify('sha256', bytes,
  { key: createPublicKey(PUBLIC_KEY_PEM), dsaEncoding: 'ieee-p1363' },
  Buffer.from(response.signature, 'base64url'));
if (!ok) throw new Error('Assinatura inválida');

const result = JSON.parse(bytes.toString('utf8'));
const fingerprint = createHash('sha256').update(sentKey, 'utf8').digest('base64url').slice(0, 16);
if (result.productSlug !== PRODUCT_SLUG || result.keyFingerprint !== fingerprint
    || result.nonce !== sentNonce || result.instanceId !== instanceId) {
  throw new Error('Resposta não corresponde ao pedido');
}
if (Math.abs(Date.now() / 1000 - result.issuedAt) > 300) throw new Error('Resposta antiga');
// A partir daqui, result.valid e result.reason são confiáveis.
```

C#:

```csharp
using var publicKey = ECDsa.Create();
publicKey.ImportFromPem(PublicKeyPem);

var bytes = Base64Url.DecodeFromChars(response.Payload);
var ok = publicKey.VerifyData(bytes, Base64Url.DecodeFromChars(response.Signature),
    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

// Depois de verificar: compare productSlug, nonce, instanceId e o keyFingerprint abaixo com o pedido.
var fingerprint = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(sentKey)))[..16];
```

### Proteções

- **Rate limit:**
  - por IP (IPv6 agrupado pelo prefixo /64), com 20 chamadas por minuto em `validate`/`deactivate` e 100 por minuto nas demais rotas (configurável em `RateLimiting`);
  - atrás de um proxy reverso, liste o proxy em `ForwardedHeaders:KnownProxies` ou `KnownNetworks`. Sem essa configuração, o `X-Forwarded-For` é ignorado.
- **Concorrência:**
  - a ativação acontece numa transação que trava a linha da licença (`SELECT … FOR UPDATE`);
  - por isso, instâncias novas validando ao mesmo tempo nunca passam de `MaxActivations`, e um teste de integração cobre esse caso.

## 📄 Licença

[MIT](LICENSE)
