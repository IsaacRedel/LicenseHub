# LicenseHub — Guia de design do painel

Este guia é a fonte da verdade do visual do painel de admin (`src/LicenseHub.Admin`). Ele descreve o mockup aprovado: **um layout, dois temas**, com troca de tema pelo usuário.

## Conceito

| Tema | Modo | Inspiração | Clima |
| --- | --- | --- | --- |
| **Arquivo** | claro | menus de NieR:Automata | bege, linhas finas, títulos leves e espaçados |
| **Tático** | escuro | HUD de Metal Gear | console verde, fonte de terminal, scanlines |

Dois toques aparecem nos dois temas:
- **Rank do dia:** o ranking de estilo de Devil May Cry (D, C, B, A, S, SSS), aplicado à taxa de validações aceitas.
- **Teia decorativa:** um detalhe discreto num canto, em referência ao Homem-Aranha.

As referências são só de **linguagem visual**. Nada de logos, personagens, fontes oficiais ou elementos de interface copiados dos jogos.

## Tokens

Todas as cores, fontes e efeitos são variáveis de CSS. As telas nunca usam valores fixos, só `var(--token)`.

| Token | Arquivo | Tático | Uso |
| --- | --- | --- | --- |
| `--bg` | `#D9D3BE` | `#070B08` | fundo da página |
| `--side` | `#CFC9B2` | `#0A110C` | barra lateral |
| `--panel` | `#E2DDC9` | `#0B140E` | cards e seções |
| `--line` | `#8C8670` | `#1F3A2A` | bordas e divisórias |
| `--soft` | `#CFC9B2` | `#13261A` | trilhos de barras e linhas de tabela |
| `--ink` | `#2E2B24` | `#BFEFCB` | texto padrão |
| `--strong` | `#2E2B24` | `#DFFFE9` | títulos, números e valores |
| `--muted` | `#55513F` | `#6F9C80` | rótulos e texto secundário |
| `--primary` | `#3A372E` | `#5BE38C` | item ativo, validações aceitas, destaque principal |
| `--on-primary` | `#E4DFCB` | `#04170B` | texto sobre `--primary` |
| `--refused` | `#9E3B32` | `#F2B23C` | validações recusadas, expiradas |
| `--danger` | `#9E3B32` | `#FF7A70` | revogadas e erros |
| `--rank-bg` | `#3A372E` | `#160B0A` | fundo do card de rank |
| `--rank-border` | `#3A372E` | `#FF5A4F` | borda do card de rank |
| `--rank-ink` | `#E4DFCB` | `#FFE1DD` | texto do card de rank |
| `--rank-accent` | `#E8836F` | `#FF5A4F` | a letra do rank |
| `--rank-on-bg` / `--rank-on-ink` | `#E4DFCB` / `#2E2B24` | `#FF5A4F` / `#1A0503` | degrau atual da escada de rank |
| `--rank-off-border` / `--rank-off-ink` | `#8C8670` / `#CFC9B2` | `#5A2A26` / `#FFB3AC` | demais degraus |
| `--font-head` | Jost | Rajdhani | títulos, menu, textos |
| `--font-mono` | IBM Plex Mono | Share Tech Mono | números, rótulos, tabelas, horários |
| `--head-weight` | `300` | `700` | peso do título da página |
| `--head-spacing` | `0.16em` | `0.10em` | espaçamento do título da página |
| `--web` | `#8C8670` | `#5BE38C` | traço da teia decorativa |
| `--grid` | `rgba(85,81,63,0.25)` | `rgba(91,227,140,0.12)` | linhas de grade do gráfico |
| `--scanlines` | `0` | `1` | opacidade da camada de scanlines |

Todos os pares de texto e fundo da tabela têm contraste de pelo menos 4,5:1. A exceção é a letra do rank no tema Arquivo (4,48:1), que passa por ser texto grande, onde o mínimo é 3:1.

## Troca de tema

- **Como aplicar:** os tokens ficam em `:root[data-theme="arquivo"]` e `:root[data-theme="tatico"]`, num CSS do painel, e o atributo `data-theme` vai no `<html>`.
- **Tema inicial:** segue o sistema do usuário. `prefers-color-scheme: light` abre no Arquivo, `dark` abre no Tático.
- **Escolha salva:** num cookie `lh-theme`, lido pelo servidor na renderização. Assim a página já nasce no tema certo, sem piscar.
- **Botão de troca:** fica no cabeçalho, com ícone de meio círculo e o texto `TEMA: ARQUIVO` ou `TEMA: TÁTICO`. O `aria-label` é "Trocar para o tema …".
- **Transição:** 0,25s em `background-color`, `color` e `border-color`, desligada quando o usuário tem `prefers-reduced-motion: reduce`.

## Tipografia

As fontes ficam hospedadas no próprio painel, em `wwwroot/fonts`, sem chamar o Google Fonts. Assim o painel não depende de nada externo e o Content-Security-Policy fica mais simples. Todas são de licença livre (OFL).

| Elemento | Estilo |
| --- | --- |
| Título da página | 40px, caixa alta, `--font-head`, `--head-weight`, `--head-spacing`, cor `--strong` |
| Título de seção | 14px, caixa alta, `letter-spacing: 0.14em`, prefixo `■ `, cor `--strong` |
| Rótulo / migalha | 12–13px, `--font-mono`, caixa alta, cor `--muted` |
| Número de KPI | 40px, `--font-mono`, cor `--strong` (ou `--danger` para revogadas) |
| Texto | 14–15px, `--font-head` |
| Tabela | 13px, `--font-mono`; cabeçalho 12px, caixa alta, `--muted` |
| Letra do rank | 104px, Rajdhani 700 itálico nos dois temas, cor `--rank-accent` |

## Layout

- **Estrutura geral:** barra lateral de 232px e conteúdo ao lado, numa linha com `flex-wrap`. No celular, a barra vai para cima do conteúdo.
- **Conteúdo:** padding de 36px em cima e 44px nas laterais, com 24px entre blocos e 16px entre cards.
- **Cards de KPI:** `grid-template-columns: repeat(auto-fit, minmax(200px, 1fr))`.
- **Segunda linha:** gráfico (`flex: 2 1 560px`) e, ao lado, uma coluna com o rank e os motivos de recusa (`flex: 1 1 280px`).
- **Terceira linha:** tabela de produtos (`flex: 1 1 380px`) e registro de validações (`flex: 1.4 1 460px`).
- **Tabelas largas:** ficam dentro de um bloco com `overflow-x: auto`.
- **Formas:** cantos retos (sem `border-radius`), bordas de 1px e nenhuma sombra ou degradê.

## Componentes

- **Menu lateral:** cada item tem pelo menos 44px de altura e caixa alta. O item ativo fica invertido (fundo `--primary`, texto `--on-primary`, quadradinho preenchido). Os demais têm quadradinho vazado com borda `--muted`. O item ativo leva `aria-current="page"`.
- **Botão principal ("+ EMITIR LICENÇA"):** borda `--primary`, fundo transparente, texto `--strong` em `--font-mono`.
- **Card de KPI:** fundo `--panel`, borda de 1px `--line` e borda esquerda de 3px na cor do significado. `--primary` para ativas e validações, `--danger` para revogadas, `--refused` para expiradas.
- **Seção / painel:** fundo `--panel`, borda de 1px `--line`, padding de 22×24px e título de seção no topo.
- **Gráfico de validações (30 dias):**
  - barras empilhadas, com as aceitas embaixo em `--primary` e as recusadas em cima em `--refused`;
  - altura de 210px, linhas de grade a cada 50px em `--grid` e a linha da base em `--line`;
  - rótulo de data (`dd/mm`) a cada 5 dias e um tooltip por barra;
  - feito em HTML/CSS ou SVG próprio, sem biblioteca;
  - um `<figure>` com `aria-label` resumindo os dados, mais uma tabela equivalente escondida visualmente para leitores de tela.
- **Card de rank:**
  - fundo `--rank-bg` e borda `--rank-border`;
  - a letra grande ao lado da frase "X% das validações foram aceitas hoje";
  - abaixo, a escada D·C·B·A·S·SSS com o degrau atual destacado.
- **Barra de progresso:** 4px de altura, trilho `--soft` e preenchimento na cor do significado. Na tabela de produtos, tem 3px.
- **Status em tabela:** aceita = `■ VÁLIDA` em `--strong`; problema = `▲ MOTIVO` em `--danger`. A cor nunca aparece sozinha, sempre com símbolo e texto.
- **Teia decorativa:** um SVG `aria-hidden` no canto inferior da barra lateral, com traço de 1px `--web`, opacidade 0,45 e cortado pela borda.
- **Scanlines:** uma camada `aria-hidden` sobre a página, com `pointer-events: none` e opacidade `--scanlines`. Só aparecem no Tático.

## Rank do dia

O rank vem da taxa de validações aceitas no dia (aceitas ÷ total):

| Rank | Taxa |
| --- | --- |
| SSS | ≥ 99,5% |
| S | ≥ 95% |
| A | ≥ 90% |
| B | ≥ 80% |
| C | ≥ 70% |
| D | < 70% |

Sem nenhuma validação no dia, o card mostra `—` e "Nenhuma validação hoje". Os limites ficam numa constante, fácil de ajustar.

## Textos

- Toda a interface fica em português, com rótulos em caixa alta.
- O tom é técnico e direto ("Sistema online", "Registro de validações"). O clima de jogo fica no visual, não nos textos de funcionalidade. Mensagens de erro e confirmações precisam ser claras para qualquer pessoa.

## Acessibilidade

- **Contraste:** mínimo de 4,5:1 para texto (os pares da tabela de tokens já passam).
- **Foco visível em tudo que é clicável:** `outline: 2px solid var(--strong); outline-offset: 2px`.
- **Áreas de clique:** pelo menos 44×44px.
- **Elementos decorativos:** teia, scanlines e marcadores levam `aria-hidden="true"`.
- **Responsividade:** o painel funciona a partir de 320px de largura.
