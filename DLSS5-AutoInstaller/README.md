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

### Deu errado? **Desfazer tudo nesta pasta**

Esse botão está na **primeira tela**, ao lado das pastas, e também na tela de verificação
(como *Desfazer tudo (forçado)*). Ele não depende de manifesto, de detecção nem de o
programa lembrar do que fez: varre a pasta do jogo inteira, devolve ao lugar todo arquivo
do jogo que tenha sido substituído (`.dlss5bak`) e remove o que for **comprovadamente**
deste programa.

O critério é conservador de propósito:

| Arquivo | Sai? |
|---|---|
| `renodx-dlss5.addon64`, `dlss5-feed.*`, `nvngx_dlssnr.dll`, `ReShade.ini`/`.log`/`Preset.ini` | sempre — nenhum jogo traz isso |
| `dxgi.dll` | só se o texto do ReShade estiver dentro dele |
| `D3D9.dll`, `dgVoodoo.conf`, `dgVoodooCpl.exe` | só com prova **e** sinal de instalação nossa por perto (existe jogo antigo que já vem com dgVoodoo) |
| `nvngx_dlss.dll` | só quando há um arquivo do kit na mesma pasta — senão pode ser o do jogo |
| `sl.*.dll`, `nvngx_dlssg.dll` | **nunca**: são do jogo, e apagá-los faz o DLSS sumir do menu |

Antes de apagar qualquer coisa ele mostra a lista completa e pede confirmação. Se algum
arquivo resistir (quase sempre é arquivo em uso), ele diz **qual** — feche o jogo e a
Steam e repita.

### "O jogo tem DLSS nativo" não é uma pergunta

Na tela de detecção isso aparece como **veredito com o motivo do lado**, não como uma
caixinha para você marcar. A evidência é só a que a instalação não consegue forjar: as
DLLs do Streamline (`sl.*.dll`) e a de frame generation, que não existem no kit, e o texto
dentro do executável do jogo, que o programa nunca modifica.

O `nvngx_dlss.dll` sozinho **não** conta: ele existe no kit e a instalação copia ele para
a pasta do jogo — bastava instalar uma vez para o mesmo jogo passar a "ter DLSS nativo" na
detecção seguinte.

E o que essa resposta muda é pouco: **só se o Feeder é instalado**, e só em jogo D3D12.
Fora do D3D12 o Feeder entra dos dois jeitos. Nenhum arquivo do jogo é apagado em nenhum
dos casos. Dá para contrariar a detecção no botão **Ajustar**, que explica isso antes.

---

### APIs cobertas

| Arquitetura | API | Como entra |
|---|---|---|
| x64 | D3D12 / D3D11 / Vulkan | rota A — ReShade como `dxgi.dll` |
| x64 | **OpenGL** | rota A, ReShade como `opengl32.dll` — **experimental** (ver abaixo) |
| x86 | D3D11 | rota B — addon32 na raiz, resto do Feeder em `host64\` |
| x86 | **D3D8** / D3D9 | rota C — dgVoodoo2 (`D3D8.dll` ou `D3D9.dll`) traduz para D3D11 |
| x86 | Vulkan / OpenGL | sem caminho: o addon32 só aceita Direct3D 11 |
| qualquer | D3D10 | sem caminho |

**DirectX 8** (Max Payne, Mafia, Hitman 2, Splinter Cell, GTA III/Vice City e a leva
de 2001–2003) usa exatamente o mesmo mecanismo do D3D9: o dgVoodoo2 traduz para D3D11 e
o ReShade se pendura no resultado. Muda só qual wrapper é copiado — e isso importa,
porque um jogo D3D8 nunca carrega um `D3D9.dll`. Confirme a marca d'água do dgVoodoo na
tela: é o único teste confiável de que ele está interceptando.

**OpenGL** está fora da matriz validada da especificação. O ReShade é instalado com o
nome certo (`opengl32.dll`) e deve carregar e abrir o overlay, mas o addon do Feeder
anuncia D3D11/D3D12/Vulkan — o DLSS 5 pode não engatar. Se o jogo tiver um seletor de
renderizador nas configurações, **prefira DirectX**. O programa avisa isso no plano em
vez de recusar em silêncio.

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
