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

- [ ] **Etapa 0 — Base:** solução, Docker Compose com MariaDB e CI
- [ ] **Etapa 1 — Domínio:** entidades, migrations e emissão de licenças
- [ ] **Etapa 2 — Validação:** resposta assinada (ECDSA), ativações, nonce e rate limit
- [ ] **Etapa 3 — Admin:** login com JWT, revogação e estatísticas
- [ ] **Etapa 4 — Painel:** dashboard em Blazor
- [ ] **Etapa 5 — Automação:** webhook de pagamento e expiração automática
- [ ] **Etapa 6 — Vitrine:** clientes de exemplo (C# e FiveM) e release v1.0.0

## 📄 Licença

[MIT](LICENSE)
