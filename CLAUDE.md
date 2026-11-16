# LicenseHub — instruções para o Claude Code

## Contexto
- O plano completo está em `docs/PLAN.md`. Leia antes de qualquer tarefa.
- Siga as etapas na ordem do plano e não avance de etapa sem pedido explícito.
- Antes de escrever código, mostre um plano curto e espere aprovação.

## Stack
- .NET 10 (LTS), ASP.NET Core, EF Core + MariaDB, Blazor.
- Testes: xUnit e Testcontainers. Não usar FluentAssertions.
- Estrutura de pastas conforme `docs/PLAN.md` (src/, tests/, samples/).

## Regras
- Código, nomes e comentários em inglês.
- Um commit por passo lógico, mensagens em inglês no padrão Conventional Commits.
- Nunca commitar segredos: use variáveis de ambiente, user-secrets e um `.env.example`.
- Chaves de licença: só o hash SHA-256 vai para o banco; a chave em texto aparece uma única vez, na emissão.
- Rode `dotnet build` e `dotnet test` antes de dizer que terminou, e diga o que testar manualmente.
