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
| `renodx-dlss5.addon64` | **4.70** (banner `RenoDX DLSS5 Generic v4.7`, build 02/09/2026), 1.732.608 B. **Atenção ao driver:** com o NVIDIA **616.64 ou mais novo** o 4.6/4.7 cai dentro do NGX (`evaluate raised 0xC0000005 in D3D12Core.dll`, medido pelo projeto do Feeder); o **4.55** (`versoes-anteriores/renodx-dlss5-4.55.addon64`) passa 300/300. O botão "Testar o host64…" da verificação mostra isso em 15 s, sem abrir jogo. | [RankFTW/rhi-repo](https://github.com/RankFTW/rhi-repo/releases) `renodx-dlss5-4.70` (espelho do `#DLSS5` do Discord do RenoDX, autor Krish) | Generic 4.1.5 (build 30/08) → `versoes-anteriores/renodx-dlss5-4.1.5.addon64`; 4.55 → `versoes-anteriores/renodx-dlss5-4.55.addon64` |
| `dlss5-feed.addon64` / `.addon32` / `dlss5-feed-host64.exe` | **0.15.1** (09/09/2026), protocolo v9 — OptiScaler DLSS-NR como terceiro consumidor neural (dentro de `host64\` em 32-bit), `--test` do host, correção do HDR10 (`hdr_bridge`), shaders do addon64 em SM 4 (feature level 10) | [jlrouzies-fr/DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder/releases) via `feeder-desejado.txt` | 0.13.1-beta.1 → o binário anterior o workflow guarda no zip `DLSS5-Feeder-anterior-kit.zip`; 0.12.0 → `versoes-anteriores/feeder-0.12.0/` |
| `reshade-shaders/Shaders/DLSS5_Feed.fx` | 0.15.1 (51 KB) — provedor por `DLSS5_MV_PROVIDER`, validação dos vetores, máscara `DLSS5_Mask` | mesmo zip | 0.12.0 → `versoes-anteriores/feeder-0.12.0/DLSS5_Feed_0.12.0.fx` |
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

## O "x2" do DLSS 5: passadas múltiplas de Neural Rendering

A segunda camada que a comunidade mostra nos vídeos é o **Pass Count** do `renodx-dlss.addon64`
do ShortFuse: "Runs 1 to 10 sequential Neural Rendering evaluations for each source. Additional
passes consume the preceding Neural Rendering output directly". O addon do Krish (`renodx-dlss5`)
não tem isso ("one-pass-per-output"); o AIO do kibblerz também não. O OptiScaler v10 pre1 tem
`Passes=` de 1 a 5 e o Deep Fried Chicken vai a 30.

**O instalador agora tem o motor "RenoDX DLSS (ShortFuse)"** na tela de detecção, para jogos
64-bit, com o número de passadas de 1 a 10 (padrão 2). Ele copia o `renodx-dlss.addon64` no
lugar do Krish e do Feeder (os dois saem da pasta, com backup), grava no `ReShade.ini`
`[ADDON] LoadFromDllMain=renodx-dlss.addon64` e `[RENODX-DLSS] DirectNeuralRenderingPassCount=N`,
e a verificação lê o log do addon (item 14) e o ini (item 24). Dentro do jogo o controle fica na
aba **RenoDX DLSS**, seção Advanced, "Pass Count". Cada passada custa o mesmo que a primeira.

### Frame generation: nenhum motor deste kit cria quadros

A aba **DLSS-G** do painel RenoDX DLSS (ShortFuse) só rege o DLSS Frame Generation que o **jogo já
traz** (Streamline) — em jogo sem ele fica vazia, e nada muda porque não há o que reger. O que existe:
**NVIDIA Smooth Motion** (driver, RTX 40 e 50; NVIDIA App → jogo → Smooth Motion) em D3D11/D3D12, com o
Feeder convivendo desde o 0.11 — nunca em Vulkan; em jogo 32-bit atrás do dgVoodoo a apresentação é
D3D11, então deve valer (não medido). Em 64-bit, o **DLSS5-Reshade-AIO do kibblerz** (pasta do kit,
manual) traz Frame Generation próprio com o `nvngx_dlssg.dll` do kit, mas substitui o RenoDX + Feeder.
O **MFG Unlock** só serve em jogo que já tem DLSS-G. Dentro do host64 não existe frame generation: o host
devolve ao jogo um quadro por quadro. O passo manual da verificação repete isso para cada jogo.

### x2+ em jogo **32-bit** (caminhos B e C)

O `renodx-dlss.addon64` do ShortFuse é 64-bit e se pendura no processo do jogo: não serve
para exe 32-bit. Em 32-bit quem faz o Neural Rendering mora em `host64\` (o auxiliar 64-bit
do Feeder), e o Feeder 0.15.0+ aceita **três consumidores** ali — só um por vez:

| Consumidor (motor no instalador) | Passadas | Como entra | De onde vem |
|---|---|---|---|
| RenoDX DLSS5 (Krish) — `renodx-dlss5.addon64` | 1 | `host64\renodx-dlss5.addon64` | kit (4.70; 4.55 em `versoes-anteriores/`) |
| **OptiScaler DLSS-NR** (v10.0.0-pre1, nightly de 04/09/2026 do 7z do Discord — **o único build com a chave `Passes`**; o fork Dagherbou v0.2.0-patch1 do GitHub faz uma passada só e foi para `versoes-anteriores/`) | **1 a 5** (`[DlssNr] Passes=`) | `host64\winmm.dll` (é o `OptiScaler.dll` renomeado, proxy que o host já importa) + `host64\nvngx.dll_dlssnr.dll` + `host64\OptiScaler\D3D12_OptiScaler\D3D12Core.dll` + `host64\OptiScaler.ini` gerado (`Enabled=true`, `Passes=N`, `Dx12Upscaler=dlss`) | kit: `OptiScaler-DLSSNR-v10.0.0-pre1 (consumidor neural 32-bit, 1 a 5 passadas)/` (vem do seu 7z, via `repo:` no `extras-desejado.txt`) |
| **RenoDX DLSS (ShortFuse)** dentro do host64 — **VALIDADO** (09/09/2026: Silent Hill 2 EE e Enslaved: Odyssey to the West; o único motor em que o x2+ apareceu na tela; logs em `docs/DLSS5-Especificacao-Automacao.md` 6.5) | **1 a 10** (`[RENODX-DLSS] DirectNeuralRenderingPassCount`) | `host64\renodx-dlss.addon64` + `host64\ReShade.ini` com `[ADDON] LoadFromDllMain=renodx-dlss.addon64` (mesclado no ini que o host grava). O addon intercepta o `NVSDK_NGX_D3D12_EvaluateFeature` que o host faz por quadro; o Feeder **não** o reconhece como consumidor (loga "renodx-dlss5*.addon64 not found" e segue servindo DLAA), e mesmo assim as passadas saem na tela. É o motor recomendado para x2+ em 32-bit | kit (SF 0.54) |
| **Deep Fried Chicken** 1.4.8 | **1 a 30** (`layers=`) | `host64\deep-fried-chicken.addon64` + `-nvngx.dll` + `.cfg` gerado (`enabled=1`, `arm=1`, `layers=N`) | **só no Discord** (veja a tabela no fim); coloque os três arquivos em qualquer pasta do kit |

Na tela de detecção de um jogo 32-bit o combo "Motor do DLSS 5" passou a listar esses três
(antes ficava travado em "Krish + Feeder" com a nota "32-bit: só o caminho Krish"), e o número
de passadas segue o máximo do motor. O plano tira do `host64\` o consumidor que não for o
escolhido (com backup), a verificação ganhou o item 25 (log do OptiScaler/DFC: `min GPU
architecture 0x0` = o OptiScaler respondeu; `nvngx.dll_dlssnr.dll` carregado = passada neural)
e o item 26 (falha `0xC0000005` dentro do NGX = driver 616.64+ com addon 4.6/4.7). O menu do
OptiScaler abre com **Insert** na janela do host (`host_window=1` no `dlss5-feed.cfg` se quiser
vê-la). Em D3D11 o OptiScaler roda o DLSS por dx11on12, que é o que o host já usa.

**Resultado do dia 09/09 no Silent Hill 2 EE: o OptiScaler NÃO entregou x2+ visível em nenhuma tentativa; o
RenoDX DLSS (ShortFuse) dentro do host64 entregou.** O que o log do OptiScaler mostra abaixo é que as
passadas foram construídas e custaram GPU, mas na tela não houve diferença de x1 para x4 (a composição
dele blenda o resultado com `TransferStrength`, e passadas compostas assim podem se anular; não investigado).

Medido no Silent Hill 2 EE (09/09, RTX 4070 Ti, driver 616.64): com o OptiScaler v10.0.0-pre1 e
`Passes=4`, o `OptiScaler.log` mostra `pass 2/3/4 built at 1920x1080` e o custo de DLSS do host
sobe de 0,16 para 6,77 ms por quadro — as quatro passadas rodam. A linha `composition ... x1
pass(es)` sai só no primeiro quadro, antes das extras: a verificação (item 25) agora conta as
passadas construídas. Outro achado do mesmo log: o OptiScaler (`winmm.dll`) carrega o `dxgi.dll`
do System32 antes do host pedir o seu, e o ReShade x64 de `host64\dxgi.dll` nunca entra (a tecla
Home na janela do host não abre nada). O instalador passou a gravar `[Plugins] LoadReshade=true`
no `OptiScaler.ini` e a copiar o ReShade x64 como `host64\ReShade64.dll`: o próprio OptiScaler o
carrega. **As passadas do OptiScaler não se mudam no painel do Feeder** (ele só mostra o ini):
mudam neste programa (Instalar de novo), no menu do OptiScaler (Insert na janela do host) ou no
`host64\OptiScaler.ini` + "Restart the DLSS 5 host".

**Não validado em jogo por este projeto** (o ambiente de desenvolvimento não tem GPU): o
caminho entrou pelo README do Feeder 0.15.0 ("OptiScaler is 64-bit only, so a 32-bit game runs
it inside host64\") e pela tabela `--test` do autor. Se um jogo cair, primeiro `Passes=1`,
depois volte ao Krish.

## Pastas novas (alternativas — o instalador usa a do ShortFuse e a do OptiScaler DLSS-NR; as outras são instalação manual pelo README de cada uma)

| Pasta | O que é | Quando usar |
|---|---|---|
| `renodx-dlss-SF-0.54 (alternativa ShortFuse)/` | `renodx-dlss.addon64` **SF 0.54** (build 08/09/2026, 2.642.432 B; o SF 0.52 ficou em `versoes-anteriores/renodx-dlss-SF-0.52.addon64`), do ShortFuse. Fabrica a chamada de DLSS sozinho, com ou sem DLSS nativo, 64-bit D3D9/11/12, e faz **1 a 10 passadas** de Neural Rendering (Pass Count). **É o motor "ShortFuse" do instalador.** | Qualquer jogo 64-bit; é o caminho do "x2". **Nunca junto com `renodx-dlss5` nem com o Feeder** (o instalador tira os dois). Em D3D9 avalia só o backbuffer final, sem vetores. |
| `DLSS5-Reshade-AIO-v2.0.3 (alternativa kibblerz)/` | `standalone-dlssnr.addon64` + `nvngx.dll` + `DLSS5_AIO_Feed.fx` + `StandaloneBoundary.fx`. Projeto **open source** que faz Neural Rendering + **DLSS Super Resolution** (ganho de FPS ao rodar o jogo abaixo da resolução do monitor) + **Frame Generation**, em 64-bit D3D9/11/12/Vulkan, sem depender do RenoDX nem do Feeder. Presets J/K/L/M (L recomendado), modelos NR 1–3. Log em `%LOCALAPPDATA%\RHI\Logs\standalone-dlssnr.log`. | Quando quiser upscaling de verdade (o caminho RenoDX/Feeder é só DLAA). Os dois binários precisam ir juntos; `nvngx_dlssg.dll` só para Frame Generation. A 2.0 mudou a apresentação; se um jogo regredir, use a `v1.7.24` ao lado. |
| `DLSS5-Reshade-AIO-v1.7.24 (alternativa kibblerz)/` | Última versão 1.x do mesmo projeto. | Fallback da 2.0.3. |
| `MFG-Unlock-0.6.1 (multi-frame generation RTX 40, alternativa mavismmg)/` | `renodx-mfgunlock.addon64` 0.6.1 (04/09/2026). Libera **multi-frame generation 3x/4x em RTX 40** (a NVIDIA limita à RTX 50) e corrige a interpolação. Convive com o `renodx-dlss5` desde a 0.6. Configuração em `[RenoDX.MFGUnlock]` no `ReShade.ini`. | Jogo **com** DLSS Frame Generation nativo (Streamline). Precisa de `nvngx_dlssg.dll` 310.x ([TechPowerUp](https://www.techpowerup.com/download/nvidia-dlss-3-frame-generation-dll/)). Com o DLSS5 junto, a combinação conhecida boa é Streamline 2.12.129 + DLLs 310.7.129; com 2.14 + 310.9 houve lentidão de menu. Só RTX 40: o snippet não tem código para Ampere. |
| `OptiScaler-DLSSNR-v10.0.0-pre1 (consumidor neural 32-bit, 1 a 5 passadas)/` | `OptiScaler.dll`, `OptiScaler.ini`, `nvngx.dll_dlssnr.dll`, `OptiScaler/D3D12_OptiScaler/D3D12Core.dll` do **OptiScaler v10.0.0-pre1** (nightly de 04/09/2026), tirados do `OptiScaler_v10.0.0-pre1_20260904 (2).7z` que você baixou do Discord (raiz do repositório). Só as peças que o instalador cobra: o 7z inteiro tem 210 MB de FSR/XeSS. **É o build que tem `[DlssNr] Passes` ("1 to 5")** — o fork [Dagherbou v0.2.0-patch1](https://github.com/Dagherbou/OptiScaler_DLSSNR/releases) do GitHub não tem a chave e faz uma passada (ficou em `versoes-anteriores/OptiScaler-DLSSNR-fork-v0.2.0-patch1 (1 passada so)/`, nomes trocados; o instalador bloqueia 2+ passadas com ele). | **É o motor "OptiScaler DLSS-NR" do instalador em jogo 32-bit** (x2 a x5). Em 64-bit não é oferecido: lá o x2+ é o ShortFuse. |
| `DLSS5-Feeder-0.15.1/` (a 0.13.1-beta.1 foi para `versoes-anteriores/`) | O resto do zip do Feeder: `READ-ME-FIRST.txt` (o que mudou), `Install-DLSS5Feeder.ps1` e `Verify-DLSS5Feeder.ps1` (instalador e verificador oficiais em PowerShell), `layer-x64/` e `layer-x86/` (camada Vulkan de reserva). | Jogos **Vulkan** e **OpenGL**, que o nosso instalador não cobre: use o `.ps1` do próprio Feeder. |

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

## Jogo que só abre em tela cheia (congela quando o DLSS 5 sobe)

Em 32-bit o Neural Rendering roda num auxiliar, o `host64` (uma janela D3D12 atrás do jogo). Se o
jogo estiver em **tela cheia EXCLUSIVA** na hora em que essa janela aparece, os dois swapchains
brigam e o jogo congela no aperto de mão (o `dlss5-feed.log` para em `host spawned` sem
`host connected`). Visto no Enslaved (10/09/2026): `SetFullscreenState(TRUE)` no `ReShade.log`
logo antes do host subir.

A saída **geral**, para qualquer jogo — tenha ou não opção de janela no menu — é marcar
**"Forçar o jogo em janela sem borda"** na tela de detecção. O instalador grava `[APP] ForceWindowed=1`
no `ReShade.ini` do jogo **e copia `swapchain_override.addon32`** para a pasta do jogo. O ReShade 6
(6.8, o do kit) **não lê mais essa chave sozinho**: ela existia no ReShade 5.x e saiu do núcleo no
6.0; a mesma função virou o exemplo oficial `16-swapchain_override` do repositório do crosire, um
addon que lê as chaves `[APP]`, faz o swapchain nascer em janela e bloqueia o pedido de tela cheia
exclusiva (`SetFullscreenState`). O kit compila esse exemplo no GitHub Actions
(`compilar-addons.yml`, commit fixado em `swapchain-override-desejado.txt`) e o guarda em
`swapchain-override (forcar janela, addon do ReShade 6)/`. Não depende do jogo ter modo janela.
Prova de que pegou: o `ReShade.log` do jogo ganha `Registered add-on "Swap chain override"`. Prova de que pegou: depois de abrir, o `ReShade.log` do jogo não pode mais
ter `Fullscreen = TRUE`. Nos jogos antigos por trás do dgVoodoo (rota C) o instalador ainda põe
`FullScreenMode=false` no `dgVoodoo.conf` — as duas alavancas juntas. O Enslaved (UE3) congelou
igual instalado como D3D11 e como D3D9 (10/09/2026, 13:05); o `ReShade.log` do jogo mostra D3D11
nos dois casos porque o dgVoodoo, quando está, é o `d3d9.dll` local e nunca aparece no log — o
D3D11 que aparece é o que ele cria. A rota é detalhe; o que trava é a tela cheia exclusiva.

## O que mudou no Feeder de 0.13.1-beta.1 para 0.15.1

- **0.14.x:** consumidores neurais alternativos dentro de `host64\` reconhecidos pelo host, aviso
  quando há dois consumidores na mesma pasta (só um fica ativo; o outro vira inerte em silêncio).
- **0.15.0:** **OptiScaler DLSS-NR** (linha Dagherbou) como terceiro consumidor, instalado como
  `winmm.dll`; shaders do addon64 compilados em SM 4 (jogos feature level 10 nunca construíam
  recursos); `work_resolution` abaixo de 100% em swapchain sRGB; `dlss5-feed-host64.exe --test`.
- **0.15.1:** HDR10 (`R10G10B10A2` PQ) era descrito como SDR e a passada neural estourava os
  brilhos — `hdr_bridge` (-1 auto) decodifica para linear FP16 na entrada e volta a PQ na saída.
- Protocolo IPC **v9**: `addon32` e `host64\dlss5-feed-host64.exe` precisam ser do mesmo zip.
- README do Feeder (09/09): tabela driver × consumidor medida com `--test` — **616.64+ quebra o
  `renodx-dlss5` 4.6/4.7** (0/300), enquanto 4.55, o "latest" clássico, o Deep Fried Chicken e o
  OptiScaler passam 300/300. Só o 4.7 foi medido nos dois drivers (300/300 no 616.56).

## O que mudou no Feeder de 0.12.0 para 0.13.1-beta.1

Interop com o Deep Fried Chicken (0.11.0-beta.1), Smooth Motion em D3D11/12 e dumps de crash
(0.11.0-beta.2), `enabled=0` desliga tudo de verdade e caminhos de fallback na inicialização
do NGX (0.13.0), **Direct3D 10 nativo em 32-bit** (0.13.1). Suporte a Vulkan (64 e 32-bit via
DXVK) e OpenGL. Em 32-bit, `addon32` e `host64\dlss5-feed-host64.exe` precisam ser do mesmo
build. Suporta as gerações 4.5/4.6/4.7 do addon do RenoDX pelo marcador de cada build.

## O pacote que você baixou do Discord (`OptiScaler_v10.0.0-pre1_20260904 (2).7z`, raiz do repositório)

Conferido arquivo por arquivo (SHA-256):

| Dentro do 7z | O que é | Situação |
|---|---|---|
| `renodx-dlss.addon64` (2.520.576 B) | ShortFuse renodx-dlss | **Idêntico** ao SF 0.52 do rhi-repo (hoje em `versoes-anteriores/renodx-dlss-SF-0.52.addon64`; o kit usa o SF 0.54). |
| `ReShade_Setup_6.8.0_Addon.exe` | ReShade 6.8.0 | **Idêntico** ao do kit. |
| `DLSS310.8.0-Streamline2.13.zip` → `nvngx_dlss.dll` | DLSS SR 310.8.0 | **Idêntico** ao do kit. |
| `DLSS310.8.0-Streamline2.13.zip` → `nvngx_dlssnr.dll` (165.840.496 B) | DLSSNR 310.8.0 **original** do NBA 2K27 | Não entra: só roda em RTX 50. O kit usa o 310.8.SF-v2 (`runtime-desejado.txt`), que roda em RTX 20/30/40/50. |
| `DLSS310.8.0-Streamline2.13.zip` → `nvngx_dlssg.dll` (7.453.808 B) | DLSS Frame Generation 310.8.0 | **Entra** em `extras-runtime (frame generation, so para AIO e MFG Unlock)/` — é o que o AIO do kibblerz e o MFG Unlock pedem. |
| `DLSS310.8.0-Streamline2.13.zip` → `nvngx_dlssd.dll`, `sl.*.dll` | Ray Reconstruction e interposer do Streamline 2.13 | Não entram: o instalador remove `sl.*.dll` de propósito (spec 3.7); ficam no 7z se precisar. |
| `OptiScaler_v10.0.0-pre1_20260904.7z` (55 MB → 210 MB) | **OptiScaler v10.0.0-pre1** (nightly de 04/09/2026, não está nos releases do GitHub) com seção **`[DlssNr]`**: DLSS 5 Neural Rendering dentro do próprio OptiScaler (código de cor derivado do addon do RenoDX, com atribuição MIT). Precisa de `nvngx_dlssnr.dll` ao lado e do `nvngx.dll_dlssnr.dll` do pacote. Em D3D11 só no modo `dlss_12` (dx11on12). | **Entra no kit** (só as 4 peças, 26 MB, em `OptiScaler-DLSSNR-v10.0.0-pre1 (...)/`, via `repo:` no `extras-desejado.txt`): é o consumidor neural que o instalador põe em `host64\` em jogo 32-bit, e o **único build com `Passes` 1 a 5** — o fork v0.2.0-patch1 do GitHub, que entrou primeiro, faz uma passada só (o SH2 abriu em x1 com o painel dizendo x2). Os outros 180 MB (XeSS, FSR) ficam no 7z. Para jogo 64-bit à mão: extraia, rode `setup_windows.bat` na pasta do jogo, ponha o `nvngx_dlssnr.dll` do kit ao lado e ligue `Enabled=true` em `[DlssNr]`. |

## O que só existe no Discord — baixe à mão

| Arquivo | Onde | Para quê | Onde colocar |
|---|---|---|---|
| `renodx-dlss5.addon64` mais novo que 4.70 | Discord do RenoDX, canal `#DLSS5` — <https://discord.com/invite/renodx> | Se sair build depois de 01/09 (o rhi-repo espelha com atraso). Qualquer nome `renodx-dlss5*.addon64` é reconhecido pelo Feeder; deixe **um só** na pasta. | raiz do kit, substituindo `renodx-dlss5.addon64` (guarde o antigo em `versoes-anteriores/`) — ou acrescente a linha em `extras-desejado.txt` quando o rhi-repo espelhar |
| `renodx-dlss.addon64` (ShortFuse) mais novo que SF 0.54 | mesmo canal (o rhi-repo espelha: confira <https://github.com/RankFTW/rhi-repo/releases> primeiro) | alternativa ao Feeder em 64-bit, x2 a x10 | `renodx-dlss-SF-.../` |
| **Deep Fried Chicken 1.4.8+** — `deep-fried-chicken.addon64`, `deep-fried-chicken-nvngx.dll`, `deep-fried-chicken.cfg` (zip `Deep-Fried-Chicken-v1.4.8-alpha.zip`) | Discord do autor (Alexander) — <https://discord.gg/g2v2XGqvR> | Consumidor neural que o Feeder passou a **recomendar** no lugar do addon do RenoDX; **x2 a x30** (`layers=`); passa 300/300 no driver 616.64. **Só um dos dois**: se acha o RenoDX carregado, fica inerte em silêncio. O Windows Defender costuma apagar (usa Detours): crie exclusão para a pasta do kit e do jogo. **O instalador já o usa**: é o motor "Deep Fried Chicken" em jogo 32-bit — basta os três arquivos estarem em qualquer pasta do kit (sugestão: `Deep-Fried-Chicken-1.4.8/`). | qualquer pasta do kit; o instalador leva para `host64\` (32-bit). Em 64-bit ainda é manual: pasta do exe, no lugar do `renodx-dlss5.addon64` |
| `nvngx_dlssnr.dll` (se aparecer build novo) | canal `#DLSS5` | runtime do Neural Rendering | `runtime-desejado.txt` (se estiver no rhi-repo) ou raiz do kit |
| `nvngx_dlss.dll` 310.8+ | canal `#DLSS5` ou Streamline SDK | runtime do DLSS SR/DLAA | raiz do kit |
| `nvngx_dlssg.dll` 310.x | [TechPowerUp](https://www.techpowerup.com/download/nvidia-dlss-3-frame-generation-dll/) (não é Discord) | Frame Generation — só para o AIO do kibblerz e para o MFG Unlock | ao lado do addon que for usar |
| **LumeniteFX** (`lumenite_Kernel.fx`, `lumenite_QuantMotion.fx`, `Shaders/include/*.fxh`, `Textures/lumenite_bluenoise256.png`) | GitHub — <https://github.com/umar-afzaal/LumeniteFX> (Code → Download ZIP). Não é Discord, mas a licença **proíbe redistribuir** cópia, por isso não está no kit. | Provedor de motion vectors que o README do Feeder recomenda (fluxo + mapa de confiança). Único que compila em D3D10 (shader model 4). | copie `Shaders/*` para `reshade-shaders/Shaders/` do kit; o instalador detecta `lumenite_Kernel.fx` e passa a oferecer "LumeniteFX Kernel" |
| ReShade 6.8.1+ (se existir) | <https://reshade.me> | — | raiz do kit (`ReShade_Setup_*_Addon.exe`) |
