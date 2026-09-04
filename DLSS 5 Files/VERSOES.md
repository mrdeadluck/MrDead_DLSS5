# Versões do kit — o que tem, de onde veio, o que falta baixar à mão

Atualizado em 04/09/2026. Tudo que está aqui vem dos **releases públicos do GitHub** de cada
projeto. O que só existe no Discord está listado no fim, com o nome exato do arquivo e onde
ele entra.

**Como os binários entram no kit:** o objeto do Git LFS não sobe a partir do ambiente onde o
código é escrito, então cada binário chega por um workflow do GitHub Actions que baixa da
URL indicada, confere o SHA-256 e commita no LFS:

| Arquivo de pedido (é o que se edita) | Workflow | Registro do que foi gravado |
|---|---|---|
| `feeder-desejado.txt` | `trocar-feeder.yml` | `feeder-versao.txt` |
| `runtime-desejado.txt` (`nvngx_dlssnr.dll`) | `trocar-runtime.yml` | hash no próprio pedido |
| `reframework-desejado.txt` | `trocar-reframework.yml` | `REFramework/reframework-versao.txt` |
| `extras-desejado.txt` (RenoDX, dgVoodoo2, alternativas, textura do VORT) | `trocar-extras.yml` | `extras-versao.txt` |

Se um binário listado abaixo ainda não está na pasta, o workflow correspondente ainda não
rodou (aba Actions do repositório).

## O que está na raiz do kit (é isto que o instalador usa)

| Arquivo | Versão | Origem | Substitui |
|---|---|---|---|
| `renodx-dlss5.addon64` | **4.70** (banner `RenoDX DLSS5 Generic v4.7`, build 02/09/2026), 1.732.608 B | [RankFTW/rhi-repo](https://github.com/RankFTW/rhi-repo/releases) `renodx-dlss5-4.70` (espelho do `#DLSS5` do Discord do RenoDX, autor Krish) | Generic 4.1.5 (build 30/08) → `versoes-anteriores/renodx-dlss5-4.1.5.addon64` |
| `dlss5-feed.addon64` / `.addon32` / `dlss5-feed-host64.exe` | **0.13.1-beta.1** (04/09/2026), protocolo v7 | [jlrouzies-fr/DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder/releases) via `feeder-desejado.txt` | 0.12.0 → `versoes-anteriores/feeder-0.12.0/` |
| `reshade-shaders/Shaders/DLSS5_Feed.fx` | 0.13.1-beta.1 (50 KB) — provedor por `DLSS5_MV_PROVIDER`, validação dos vetores, máscara `DLSS5_Mask` | mesmo zip | 0.12.0 → `versoes-anteriores/feeder-0.12.0/DLSS5_Feed_0.12.0.fx` |
| `reshade-shaders/Shaders/vort_Motion.fx` + `Includes/vort_*.fxh` + `Textures/vort_BlueNoise.png` | VORT Motion (MIT), commit `b410b9f` | [vortigern11/vort_Shaders](https://github.com/vortigern11/vort_Shaders) | novo — provedor de motion vectors que o Feed.fx 0.13 recomenda; **padrão do instalador** |
| `dgVoodoo2/` | **2.87.4** (corrige o crash em builds 26H1+ do Windows 11) | [dege-diosg/dgVoodoo2](https://github.com/dege-diosg/dgVoodoo2/releases) | 2.87.3 → `versoes-anteriores/dgVoodoo2-2.87.3/` |
| `nvngx_dlssnr.dll` | 310.8.SF-v2 (build do ShortFuse para RTX 20/30/40/50) | rhi-repo via `runtime-desejado.txt` — **não mudou** nesta rodada | — |
| `nvngx_dlss.dll` | 310.8.0.0 (Streamline SDK 2.13) | **não mudou** (o `streamline_2.14.0.0.zip` do rhi-repo não traz `nvngx_dlss.dll`, só os `sl.*.dll`) | — |
| `dxgi.dll`, `ReShade_Setup_6.8.0_Addon.exe` | ReShade 6.8.0 | reshade.me — **não mudou** (o site está bloqueado daqui; confira se saiu 6.8.1+) | — |
| `REFramework/`, `MGSV/` | como estavam | `reframework-desejado.txt` / patcher do MGS V | — |
| `MartysMods_LAUNCHPAD.fx` | iMMERSE Launchpad | já estava — segunda opção de provedor | — |

**O DRME (`MotionEstimation.fx` + `.fxh`) saiu da pasta `reshade-shaders`** e foi para
`versoes-anteriores/DRME-nao-compila-no-ReShade-6.8/`. Ele não compila no ReShade 6.8
(erro X3020): aparecia como ligado, mas não escrevia nada, e o DLSS rodava sem vetor de
movimento. O instalador não o oferece mais como padrão (fica como último item da lista,
marcado).

## Pastas novas (alternativas — o instalador ainda NÃO usa; instalação manual pelo README de cada uma)

| Pasta | O que é | Quando usar |
|---|---|---|
| `renodx-dlss-SF-0.52 (alternativa ShortFuse)/` | `renodx-dlss.addon64` **SF 0.52** (04/09/2026), do ShortFuse. Fabrica a chamada de DLSS sozinho: em jogo **64-bit D3D9/11/12 sem DLSS nativo** substitui o Feeder inteiro (um addon só). | Jogo 64-bit sem DLSS. **Nunca junto com `renodx-dlss5` nem com o Feeder.** Em D3D9 avalia só o backbuffer final, sem vetores. |
| `DLSS5-Reshade-AIO-v2.0.3 (alternativa kibblerz)/` | `standalone-dlssnr.addon64` + `nvngx.dll` + `DLSS5_AIO_Feed.fx` + `StandaloneBoundary.fx`. Projeto **open source** que faz Neural Rendering + **DLSS Super Resolution** (ganho de FPS ao rodar o jogo abaixo da resolução do monitor) + **Frame Generation**, em 64-bit D3D9/11/12/Vulkan, sem depender do RenoDX nem do Feeder. Presets J/K/L/M (L recomendado), modelos NR 1–3. Log em `%LOCALAPPDATA%\RHI\Logs\standalone-dlssnr.log`. | Quando quiser upscaling de verdade (o caminho RenoDX/Feeder é só DLAA). Os dois binários precisam ir juntos; `nvngx_dlssg.dll` só para Frame Generation. A 2.0 mudou a apresentação; se um jogo regredir, use a `v1.7.24` ao lado. |
| `DLSS5-Reshade-AIO-v1.7.24 (alternativa kibblerz)/` | Última versão 1.x do mesmo projeto. | Fallback da 2.0.3. |
| `MFG-Unlock-0.6.1 (multi-frame generation RTX 40, alternativa mavismmg)/` | `renodx-mfgunlock.addon64` 0.6.1 (04/09/2026). Libera **multi-frame generation 3x/4x em RTX 40** (a NVIDIA limita à RTX 50) e corrige a interpolação. Convive com o `renodx-dlss5` desde a 0.6. Configuração em `[RenoDX.MFGUnlock]` no `ReShade.ini`. | Jogo **com** DLSS Frame Generation nativo (Streamline). Precisa de `nvngx_dlssg.dll` 310.x ([TechPowerUp](https://www.techpowerup.com/download/nvidia-dlss-3-frame-generation-dll/)). Com o DLSS5 junto, a combinação conhecida boa é Streamline 2.12.129 + DLLs 310.7.129; com 2.14 + 310.9 houve lentidão de menu. Só RTX 40: o snippet não tem código para Ampere. |
| `DLSS5-Feeder-0.13.1-beta.1/` | O resto do zip do Feeder: `READ-ME-FIRST.txt` (o que mudou), `Install-DLSS5Feeder.ps1` e `Verify-DLSS5Feeder.ps1` (instalador e verificador oficiais em PowerShell), `layer-x64/` e `layer-x86/` (camada Vulkan de reserva). | Jogos **Vulkan** e **OpenGL**, que o nosso instalador não cobre: use o `.ps1` do próprio Feeder. |

## O que mudou no addon do RenoDX de 4.1.5 para 4.70 (fonte: tabela de compatibilidade do Feeder, checada em 01/09)

- **4.5 / 4.55:** reescaneia a cada present e adota features de DLSS criadas antes dos ganchos
  ("registering lazily from evaluate contract"; a verificação do programa já lê isso); o
  Feeder pula o warm-up.
- **4.60:** hotkeys globais (`NRToggleKey`), upscaling experimental (`NREnableUpscaling`),
  diagnósticos de recusa. **`NRStyle=2` derruba o jogo na abertura seguinte** — se acontecer,
  volte `NRStyle=0` em `[RenoDX.DLSS5]` no `ReShade.ini`.
- **4.70:** ponte de cor reversível (SDR sRGB / HDR linear BT.709 / PQ BT.2020, `NRGlobalTone`)
  no lugar do codec paper-white, pool de worksets D3D12 com fence.
- Chaves da seção `[RenoDX.DLSS5]` que o 4.70 lê: `EnableHooks`, `NeuralUplift`,
  `NREnableUpscaling`, `NRIntensity`, `NRStyle`, `NRLocalStructure`, `NRLocalTone`,
  `NRAutoMask`, `NRUICorrection`, `NRToggleKey`, `NRGlobalTone`. O instalador passa a
  gravar `NeuralUplift=1` e `NREnableUpscaling=0` (o mesmo que o Feeder grava quando faltam).
- Nenhum jogo foi verificado de ponta a ponta pelo projeto do Feeder com 4.6/4.7 ainda: é
  compatibilidade estática. Se um jogo que rodava com o 4.1.5 parar, o addon antigo está em
  `versoes-anteriores/` — copie de volta para a raiz com o nome `renodx-dlss5.addon64`.

## O que mudou no Feeder de 0.12.0 para 0.13.1-beta.1

Interop com o Deep Fried Chicken (0.11.0-beta.1), Smooth Motion em D3D11/12 e dumps de crash
(0.11.0-beta.2), `enabled=0` desliga tudo de verdade e caminhos de fallback na inicialização
do NGX (0.13.0), **Direct3D 10 nativo em 32-bit** (0.13.1). Suporte a Vulkan (64 e 32-bit via
DXVK) e OpenGL. Em 32-bit, `addon32` e `host64\dlss5-feed-host64.exe` precisam ser do mesmo
build. Suporta as gerações 4.5/4.6/4.7 do addon do RenoDX pelo marcador de cada build.

## O que só existe no Discord — baixe à mão

| Arquivo | Onde | Para quê | Onde colocar |
|---|---|---|---|
| `renodx-dlss5.addon64` mais novo que 4.70 | Discord do RenoDX, canal `#DLSS5` — <https://discord.com/invite/renodx> | Se sair build depois de 01/09 (o rhi-repo espelha com atraso). Qualquer nome `renodx-dlss5*.addon64` é reconhecido pelo Feeder; deixe **um só** na pasta. | raiz do kit, substituindo `renodx-dlss5.addon64` (guarde o antigo em `versoes-anteriores/`) — ou acrescente a linha em `extras-desejado.txt` quando o rhi-repo espelhar |
| `renodx-dlss.addon64` (ShortFuse) mais novo que SF 0.52 | mesmo canal | alternativa ao Feeder em 64-bit | `renodx-dlss-SF-.../` |
| **Deep Fried Chicken 1.4.8+** — `deep-fried-chicken.addon64`, `deep-fried-chicken-nvngx.dll`, `deep-fried-chicken.cfg` (zip `Deep-Fried-Chicken-v1.4.8-alpha.zip`) | Discord do autor (Alexander) — <https://discord.gg/g2v2XGqvR> | Consumidor neural que o Feeder passou a **recomendar** no lugar do addon do RenoDX. **Só um dos dois**: se acha o RenoDX carregado, fica inerte em silêncio. O Windows Defender costuma apagar (usa Detours). | pasta do exe (ou `host64\` em 32-bit), no lugar do `renodx-dlss5.addon64` |
| `nvngx_dlssnr.dll` (se aparecer build novo) | canal `#DLSS5` | runtime do Neural Rendering | `runtime-desejado.txt` (se estiver no rhi-repo) ou raiz do kit |
| `nvngx_dlss.dll` 310.8+ | canal `#DLSS5` ou Streamline SDK | runtime do DLSS SR/DLAA | raiz do kit |
| `nvngx_dlssg.dll` 310.x | [TechPowerUp](https://www.techpowerup.com/download/nvidia-dlss-3-frame-generation-dll/) (não é Discord) | Frame Generation — só para o AIO do kibblerz e para o MFG Unlock | ao lado do addon que for usar |
| **LumeniteFX** (`lumenite_Kernel.fx`, `lumenite_QuantMotion.fx`, `Shaders/include/*.fxh`, `Textures/lumenite_bluenoise256.png`) | GitHub — <https://github.com/umar-afzaal/LumeniteFX> (Code → Download ZIP). Não é Discord, mas a licença **proíbe redistribuir** cópia, por isso não está no kit. | Provedor de motion vectors que o README do Feeder recomenda (fluxo + mapa de confiança). Único que compila em D3D10 (shader model 4). | copie `Shaders/*` para `reshade-shaders/Shaders/` do kit; o instalador detecta `lumenite_Kernel.fx` e passa a oferecer "LumeniteFX Kernel" |
| ReShade 6.8.1+ (se existir) | <https://reshade.me> | — | raiz do kit (`ReShade_Setup_*_Addon.exe`) |
