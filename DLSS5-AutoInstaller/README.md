# DLSS 5 AutoInstaller

Programa que automatiza a instalação do DLSS 5 Neural Rendering em jogos sem suporte
nativo, seguindo a especificação técnica de `DLSS5EspecificacaoAutomacao.md`.

Você abre o programa, aponta **a pasta do kit** e **a pasta do jogo**, e ele faz o resto:
descobre o executável real, a arquitetura e a API, escolhe o caminho de instalação
(A, B ou C), copia cada arquivo para o lugar certo, gera as configurações do ReShade já
com os efeitos marcados na ordem correta, ajusta o dgVoodoo2 quando necessário, aplica o
override de assinatura no registro e depois verifica o que deu para verificar — te guiando
no que sobra de manual.

---

## Baixar o executável

Não precisa compilar nada. O `.exe` é gerado automaticamente pelo GitHub Actions:

**[Página de Releases → `DLSS5-AutoInstaller.exe`](../../releases/tag/installer-latest)**

É um executável único e autocontido (não precisa instalar o .NET). Ele **pede permissão de
administrador** — necessário para gravar no registro (`HKLM`) e em pastas dentro de
`Program Files`.

> O Windows SmartScreen provavelmente vai avisar que o app não é conhecido, porque o
> executável não tem assinatura digital paga. Clique em **Mais informações → Executar assim
> mesmo**.

---

## Como usar

1. **Pasta do kit** — a pasta com os arquivos do DLSS 5 (`nvngx_dlssnr.dll`,
   `renodx-dlss5.addon64`, `dlss5-feed.*`, o dgVoodoo2 e a pasta `reshade-shaders`).
   Pode estar bagunçada, com subpastas e cópias duplicadas: o programa varre tudo e acha
   cada peça pelo nome **e pela arquitetura real** lida no cabeçalho PE.
2. **Pasta do jogo** — a pasta onde o jogo está instalado.
3. **Detectar** — confira o que ele encontrou. A API gráfica é procurada em três frentes
   (imports do PE, texto dentro do binário e pistas na pasta), e a tela diz se o resultado
   é **detectado** (evidência forte e sem empate) ou apenas **provável** — nesse segundo
   caso vale confirmar. Em jogo 64-bit, errar entre D3D11 e D3D12 não muda nada: as duas
   caem na mesma rota A, com os mesmos arquivos nos mesmos lugares.
4. **Tecla do overlay** — qualquer tecla (F1–F12, letras, números, numpad, Insert, Delete,
   Pause…) com **Ctrl/Shift/Alt** opcionais. Útil quando o jogo engole a tecla escolhida:
   uma combinação como `Ctrl+Shift+Home` não colide com nada.
5. **Gerar plano** — mostra exatamente o que será copiado e gerado, antes de tocar em
   qualquer arquivo.
6. **Instalar** → **Verificar**.

No fim, a tela de verificação lista os checkpoints (o que passou, o que falhou e como
corrigir), o roteiro dos passos manuais e um diagnóstico automático lido dos logs.

### Desinstalar

Botão **Desinstalar (reverter)** na última tela. Toda instalação grava um manifesto
(`dlss5-autoinstaller-manifest.json`) na pasta do executável do jogo, com a lista do que
foi adicionado e os backups do que foi sobrescrito — a reversão desfaz tudo a partir dele.

---

## O que é automatizado

| Etapa | Como |
|---|---|
| Achar o executável real | Varre os `.exe`, pontua por profundidade/tamanho/nome, descarta launchers e redistribuíveis, reconhece o stub da engine Source |
| Arquitetura | Campo `Machine` do cabeçalho PE (nunca pelo nome da pasta) |
| API gráfica | Imports do PE, **texto dentro do binário** (pega o Direct3D carregado com `LoadLibrary`, que não aparece nos imports), DLLs ao lado do exe, nome de jogos conhecidos e detecção da Source (`bin\shaderapi*.dll`). Cada pista tem peso; a tela informa se houve empate |
| Escolha do caminho A/B/C | Árvore de decisão da spec (seção 5) |
| Layout dos arquivos | Cada rota tem o seu; em jogo 32-bit os `.addon64` vão **só** em `host64\` |
| ReShade | Copia o `dxgi.dll` da arquitetura certa ou **extrai** `ReShade32/64.dll` do instalador |
| `ReShade.ini` | Gerado com `EffectSearchPaths`, `TextureSearchPaths`, `AddonPath` e a combinação de teclas escolhida (`KeyOverlay=<vk>,<ctrl>,<shift>,<alt>`) |
| `ReShadePreset.ini` | Gerado com o provedor de motion vectors **acima** do `DLSS5_Feed`, ambos já marcados |
| `dgVoodoo.conf` | Patch consciente de seção (5 chaves) |
| Override de assinatura | 3 chaves em `HKLM`, com verificação e reversão |
| Limpeza | Remove `sl.*.dll`, `nvngx_dlssg.dll` e licenças (com backup) |
| Verificação | Checkpoints 1–16 por leitura de arquivos, registro e logs |
| Diagnóstico | Mapeia mensagens dos logs para causa e correção |

### O que continua manual (e por quê)

Desligar overlays (a Steam restaura o arquivo ao reabrir; só a UI funciona), confirmar a
marca d'água do dgVoodoo e o banner do ReShade na tela, ajustar
`RESHADE_DEPTH_INPUT_IS_REVERSED` (depende de olhar o `DisplayDepth`), reiniciar o Windows
depois do override e definir opções de inicialização da Steam. O programa lista tudo isso
como roteiro numerado, marcando o que precisa ser feito **antes** de abrir o jogo.

---

## Limitações do DLSS 5 nesse cenário

Não são erros de configuração — são do método:

- **É DLAA only:** resolução de render igual à de saída. **Não existe ganho de FPS.**
- A HUD é processada junto com a cena.
- Motion vectors estimados geram ghosting em movimento rápido.
- O override de assinatura é **global no sistema**. Anti-cheat (EAC, BattlEye) pode tratar
  como violação de integridade — **não use em jogos online com anti-cheat**.
- Em placas Ada (RTX 40) o consumo de VRAM é o pior caso. Teste em 1080p, em janela.

---

## Compilar do zero (opcional)

Precisa do [.NET 8 SDK](https://dotnet.microsoft.com/download) no Windows:

```bash
cd DLSS5-AutoInstaller
dotnet test tests/Dlss5.Core.Tests/Dlss5.Core.Tests.csproj
dotnet publish src/Dlss5.App/Dlss5.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## Estrutura

```
DLSS5-AutoInstaller/
├─ src/Dlss5.Core/     Lógica (sem interface): PE, detecção, plano, configs, verificação
├─ src/Dlss5.App/      Interface WinForms (o assistente de 5 passos)
└─ tests/              Testes da lógica — rodam no CI a cada push
```

Toda a regra de negócio fica no `Dlss5.Core`, sem dependência de interface gráfica, para
poder ser testada. O `Dlss5.App` é só a casca.
