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

Abra o programa e aponte **a pasta do jogo**. Ele verifica sozinho o estado e mostra só
as ações que fazem sentido:

| Estado detectado | Botão principal | O que faz |
|---|---|---|
| DLSS 5 não instalado | **Instalar DLSS 5** | Detecção → plano → execução → verificação |
| Instalado e íntegro | **Desinstalar e restaurar arquivos originais** | Remove só o que o programa gravou e devolve os backups. **Não precisa do kit nem de reinstalar.** |
| Instalado por versão anterior / kit diferente | **Atualizar ou reconfigurar** | Regrava com o kit atual sem refazer backups dos originais |
| Instalação incompleta ou inconsistente | **Reparar instalação** | Compara o registro com a pasta e repõe só o que falta ou mudou |
| Arquivos do mod sem registro (versão antiga) | **Remover vestígios (modo conservador)** | Lista o que é comprovadamente do mod, pede confirmação e remove |
| Só backups `.dlss5bak` sobrando | **Devolver arquivos originais** | Move cada backup de volta ao nome original |
| Pasta não existe / sem executável | **Selecionar outro jogo** | — |
| Registro ilegível ou situação estranha | **Ver detalhes do problema** | Relatório arquivo por arquivo, exportável |

A **pasta do kit** (`DLSS 5 Files`) só é pedida para instalar, atualizar ou reparar.

### Instalar

1. **Detecção** — confira executável real, arquitetura e API. A API é o único palpite que
   pode errar; em 64-bit, D3D11 e D3D12 dão no mesmo. Cada opção (motion vectors, tecla do
   painel, override no registro) tem a explicação do que muda logo abaixo do campo.
2. **Plano** — tudo que será copiado, gerado e alterado, antes de tocar em qualquer
   arquivo. Arquivos preexistentes que não são do programa aparecem como **conflito** e
   exigem que você marque que entendeu que serão substituídos (com backup).
3. **Execução** — etapa atual, barra de progresso e log. Dá para cancelar entre etapas; o
   que já foi alterado é desfeito. Qualquer falha (arquivo em uso, antivírus, disco cheio)
   também desfaz tudo e diz em que etapa parou e o que fazer.
4. **Verificação** — checkpoints por arquivo, registro e logs, roteiro dos passos manuais
   e diagnóstico automático.

Instalar de novo por cima de uma instalação válida é seguro: arquivos iguais são pulados,
nada é duplicado e o backup de um original **nunca** é sobrescrito por um arquivo do mod.

### Desinstalar

Abra o programa, aponte o jogo, clique em **Desinstalar e restaurar arquivos originais**.
Antes de fazer qualquer coisa ele mostra o resumo: o que será removido, o que será
restaurado do backup, o que será preservado e o que **não** poderá ser restaurado (backup
ausente ou inválido — nesse caso ele não finge que restaurou: diz o arquivo e orienta a
usar a verificação de integridade da loja). Também pergunta se o override de assinatura
do registro deve sair (ele é global: desmarque se usa o DLSS 5 em outro jogo).

No fim, a tela de resultado lista exatamente o que foi removido, restaurado e preservado,
e a tela inicial volta ao estado **"DLSS 5 não instalado"**.

### Como o programa garante que dá para voltar atrás

- **Manifesto** (`dlss5-autoinstaller-manifest.json`, na pasta do executável): versão do
  programa e do kit, opções escolhidas, e **tamanho + SHA-256** de cada arquivo gravado e
  de cada original guardado. É gravado **antes** da primeira modificação e atualizado a
  cada passo — se o programa cair no meio, a próxima abertura mostra "instalação
  incompleta" e oferece reparar ou desinstalar.
- **Backups** `.dlss5bak` só dos arquivos que já existiam e não eram do programa. Um
  backup existente é o original de verdade e não é refeito.
- **Troca atômica**: cada arquivo vai para um temporário ao lado e só então é movido por
  cima.
- **Rollback**: falha ou cancelamento desfaz tudo que a execução fez, na ordem inversa.
- **Remoção só do que confere**: na desinstalação um arquivo só é apagado se ainda é o que
  o manifesto diz ter gravado (hash). O que foi trocado por outro programa é preservado e
  listado.
- **Modo conservador** para instalações antigas sem manifesto: só sai o que não tem como
  ser do jogo (nomes exclusivos do kit, ReShade identificado pelo conteúdo).

### Logs e diagnóstico

Tudo vai para `%LOCALAPPDATA%\DLSS5-AutoInstaller\logs\` (10 arquivos, 2 MB cada, os
mais antigos saem sozinhos): versão, sistema, escala da tela, estado antes e depois de
cada operação, cada arquivo criado/alterado/restaurado/removido, duração das etapas e
stack traces. Botões **Copiar log**, **Abrir pasta de logs** e **Exportar diagnóstico**
(zip com log, manifesto e relatório de estado — sem senhas nem dados de conta) ficam nas
telas de execução, verificação e resultado. Se a pasta de logs não puder ser criada, o
programa continua funcionando e mantém o log em memória.

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
├─ src/Dlss5.App/      Interface WinForms (tela de estado + fluxos separados de instalar/reparar/desinstalar)
└─ tests/              Testes da lógica — rodam no CI a cada push
```

Toda a regra de negócio fica no `Dlss5.Core`, sem dependência de interface gráfica, para
poder ser testada: `EstadoDoMod` (máquina de estados do mod), `InstallerEngine`
(instalação idempotente com rollback, desinstalação por manifesto, remoção conservadora),
`Manifest`, `Propriedade` (de quem é cada arquivo), `Preflight`, `Diario` (logs) e
`Diagnostico`. O `Dlss5.App` é só a casca: `MainForm.*.cs` (uma tela por arquivo),
`Textos.cs` (todos os textos), `Ui.cs` (paleta e fábricas responsivas), `Dialogos.cs`.

Detalhes da revisão, problemas encontrados e decisões técnicas:
[`docs/Diagnostico-e-Reengenharia.md`](../docs/Diagnostico-e-Reengenharia.md).
