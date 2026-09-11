# DLSS 5 Neural Rendering em jogos sem suporte nativo
## Especificação técnica para automação

Consolidado da sessão de 30–31/08/2026. Cobre tudo que foi feito, os arquivos envolvidos, as variações encontradas e o que pode ser automatizado.

Hardware de referência: RTX 4070 Ti (Ada), driver 616.56.

---

## 1. Como funciona, em uma página

O DLSS 5 Neural Rendering (DLSSNR, `nvngx_dlssnr.dll` v310.8.0.0) vazou num build de early access do NBA 2K27. A comunidade construiu duas camadas para usá-lo:

**RenoDX DLSS5** (`renodx-dlss5.addon64`) — addon de ReShade que se pendura nas chamadas de DLSS que o jogo já faz e injeta o passo neural. Só funciona em jogos com DLSS nativo, 64-bit, D3D12.

**DLSS5-Feeder** (`dlss5-feed.*`, jlrouzies-fr) — para jogos **sem** DLSS. Fabrica o contrato DLAA que o DLSS espera: pega o frame, o depth buffer (via Generic Depth do ReShade) e motion vectors estimados por shader, e alimenta o addon do RenoDX como se o jogo tivesse DLSS. Roda o NGX num device D3D12 privado, então a API do jogo importa menos.

Como o NGX e o addon do DLSS 5 só existem em x64, jogos de 32 bits usam um **processo auxiliar** (`host64\dlss5-feed-host64.exe`): o addon32 dentro do jogo manda os frames por named pipe e texturas compartilhadas, o host faz o trabalho, e devolve.

Jogos DirectX 9 precisam ser **traduzidos para D3D11** antes, com o **dgVoodoo2**, porque o ReShade em D3D9 trava no Shader Model 3 e nenhum provedor de motion vectors compila.

Limitações estruturais (não são bugs de configuração):
- É DLAA only: resolução de render = saída. **Não há ganho de performance.**
- HUD é processada junto com a cena.
- Motion vectors estimados dão ghosting em movimento rápido.
- `nvngx_dlssnr.dll` patcheado para Ada tem assinatura quebrada e exige override no registro.

---

## 2. Matriz de suporte

| Arquitetura | API | Suportado | Caminho | Validado nesta sessão |
|---|---|---|---|---|
| x64 | D3D12 | Sim — melhor caminho, zero-copy | A | RE2 Remake, RE7 |
| x64 | D3D11 | Sim | A | — |
| x64 | Vulkan | Sim (README) | A + layer | — |
| x86 | D3D11 | Sim | B | Tomb Raider 2013 |
| x86 | D3D9 | Sim, via dgVoodoo2 → D3D11 | C | Castlevania: Lords of Shadow (variante simples), Half-Life 2 (variante Source) |
| x86 | D3D9 | idem | C | GTA IV (parcial: dgVoodoo ok, ReShade pendente) |
| x86 | D3D8 | Sim, via dgVoodoo2 → D3D11 (D3D9.dll atrás de mod com d3d8to9) | C | Silent Hill 2 Enhanced Edition (OptiScaler x4 no host64) |
| x64 | OpenGL | Sim (Feeder: em processo; MX Bikes relatado) — ReShade como opengl32.dll | A | — |
| x86 | OpenGL | Sim (Feeder 0.9+: Worms Ultimate Mayhem, KOTOR, pelo host64) — ReShade como opengl32.dll | B | — |
| x86 | D3D10 | Sim (Feeder 0.13.1+, nativo) — só o LumeniteFX compila como provedor | B | — |
| **x86** | **Vulkan** | **NÃO** neste instalador — o Feeder faz via DXVK (`layer-x86\`), fora deste fluxo | — | HL2 (confirmado no log) |
| x64 | D3D10 | Não | — | — |

Regra derivada: em 32 bits, D3D11/D3D10/OpenGL vão direto ao host64; D3D9/D3D8 passam pelo dgVoodoo. Se o jogo x86 oferece Vulkan e D3D9, escolha D3D9 + dgVoodoo. As passadas múltiplas (OptiScaler no host64) valem para todos os caminhos B e C, porque o host é o mesmo.

---

## 3. Inventário completo de arquivos

### 3.1 Obrigatórios em todos os caminhos

| Arquivo | Origem | Tamanho | Arch | Observações |
|---|---|---|---|---|
| `nvngx_dlssnr.dll` | vazamento NBA 2K27 + patch Ada (Uncle Burrito) | 165.840.496 B | x64 | **Assinatura quebrada.** Dois builds circulam com mesmo tamanho: SHA256 `3973aaee…` e `368911e6…`. Use sempre o mesmo. |
| `nvngx_dlss.dll` | Streamline SDK 2.13 | 58.956.400 B | x64 | Assinatura NVIDIA íntegra, v310.8.0.0 |
| `renodx-dlss5.addon64` | RenoDX Discord `#DLSS5` (Krish); espelho em github.com/RankFTW/rhi-repo (`extras-desejado.txt`) | 1.732.608 B | x64 | **4.70** (banner `RenoDX DLSS5 Generic v4.7`, build 02/09/2026). Lê `nvngx_dlssnr.dll`. Anterior (Generic 4.1.5, 1.694.720 B) em `versoes-anteriores/`. Não está no GitHub do ShortFuse: o addon é fechado. |
| `DLSS5_Feed.fx` | github.com/jlrouzies-fr/DLSS5-Feeder 0.13.1-beta.1 (`feeder-desejado.txt`) | 50.240 B | shader | Provedor escolhido em compilação por `DLSS5_MV_PROVIDER` (ver 3.4); valida vetores e gera `DLSS5_Mask`. Inclui `ReShade.fxh`. |
| `ReShade.fxh`, `ReShadeUI.fxh`, `Macros.fxh` | instalador do ReShade (pacote padrão) | — | shader | Sem eles o `DLSS5_Feed.fx` não compila |
| Provedor de motion vectors | ver 3.4 | — | shader | Um deles, marcado **acima** do Feed |
| ReShade com add-on support | reshade.me | instalador 4.318.424 B | — | v6.8.0.2156. Contém `ReShade32.dll` e `ReShade64.dll` internamente. |

### 3.2 Caminho A (64-bit) — adicionais

| Arquivo | Tamanho | Observações |
|---|---|---|
| `dlss5-feed.addon64` | 246.272 B | **0.13.1-beta.1** (04/09/2026). Suporta D3D11/D3D12/Vulkan/OpenGL. Fica na pasta do executável. Anterior (0.12.0) em `versoes-anteriores/feeder-0.12.0/`. |
| `dxgi.dll` (ReShade x64) | 5.592.064 B | Extraído do instalador (`ReShade64.dll` renomeado) ou de instalação x64 |

### 3.3 Caminho B e C (32-bit) — adicionais

| Arquivo | Tamanho | Arch | Local |
|---|---|---|---|
| `dlss5-feed.addon32` | 176.640 B (0.15.1) | x86 | pasta do exe (única peça do Feeder fora de `host64\`). 0.13.1: D3D10 nativo. |
| `dlss5-feed-host64.exe` | 146.944 B (0.15.1) | x64 | `host64\` — protocolo v9; **precisa ser do mesmo build do addon32**. `--test` = 300 avaliações sem jogo (botão "Testar o host64…") |
| `dxgi.dll` (ReShade x86) | 4.398.080 B | x86 | pasta do exe |
| `dxgi.dll` (ReShade x64) | 5.592.064 B | x64 | `host64\` |
| `renodx-dlss5.addon64` | | x64 | `host64\` (**não** na raiz) — consumidor neural padrão, 1 passada. **Ou, no lugar dele** (nunca dois): `winmm.dll` (= `OptiScaler.dll` v10.0.0-pre1 com DLSS-NR) + `nvngx.dll_dlssnr.dll` + `OptiScaler\D3D12_OptiScaler\D3D12Core.dll` + `OptiScaler.ini` gerado (1–5 passadas), ou `deep-fried-chicken.addon64` + `-nvngx.dll` + `.cfg` gerado (1–30). Ver 6.5. |
| `nvngx_dlssnr.dll` | | x64 | `host64\` (**não** na raiz) |
| `nvngx_dlss.dll` | | x64 | `host64\` (**não** na raiz) |

### 3.4 Provedores de motion vectors

| Provedor | Arquivos | Licença | Status observado |
|---|---|---|---|
| Provedor | Arquivos | Licença | `DLSS5_MV_PROVIDER` | Technique | Status |
|---|---|---|---|---|---|
| **VORT Motion** (vortigern11) — **padrão do instalador** | `vort_Motion.fx` + `Includes\vort_*.fxh` + `Textures\vort_BlueNoise.png` | MIT — vem no kit | `2` | `vort_MotionEffects` | O que o Feed.fx 0.13 recomenda. |
| **Launchpad** (iMMERSE, Pascal Gilcher) | `MartysMods_LAUNCHPAD.fx` + `MartysMods\mmx_*.fxh` + texturas `iMMERSE_bluenoise_*.png` | Proprietário — repack circulando viola a licença | `1` | `MartysMods_Launchpad` | Compila. Funcionou no RE2 e Tomb Raider. O Feed 0.13 dispara o pedido de fluxo por frame que o Launchpad exige. |
| **LumeniteFX Kernel** (umar-afzaal) | `lumenite_Kernel.fx` + `include\lumenite_*.fxh` | AGNYA — **proíbe redistribuir**, o usuário baixa do GitHub | `3` | `Lumenite_Kernel` | Recomendado pelo README do Feeder (fluxo 1/8 + mapa de confiança). Único que compila em D3D10 (SM4). O instalador oferece se `lumenite_Kernel.fx` estiver no kit. |
| **DRME** (ReshadeMotionEstimation, JakobPCoder) | `MotionEstimation.fx` + 3 `.fxh` (em `versoes-anteriores/`) | CC BY-NC | `0` | `DRME` | **Não compila no ReShade 6.8** (X3020) em nenhuma API: aparece ligado, escreve nada, DLSS roda sem vetores. O Feeder 0.6.1+ detecta e avisa. |
| qUINT motionvectors, dh_uber_motion | escrevem `texMotionVectors` | — | `0` | — | Não testados |

O instalador grava a definição na seção `[DLSS5_Feed.fx]` do `ReShadePreset.ini` junto com a
lista de techniques (checkpoint 13 confere). Sem a definição o Feed lê `texMotionVectors`,
que nenhum provedor do kit escreve.

### 3.5 Caminho C (D3D9) — dgVoodoo2

| Arquivo | Origem | Tamanho | Observações |
|---|---|---|---|
| `D3D9.dll` | dgVoodoo2 **v2.87.4**, pasta **`MS\x86\`** (`extras-desejado.txt`) | 482.304 B | Corrige o crash em Windows 11 builds 26H1+. A 2.87.3 (485.888 B) ficou em `versoes-anteriores/`. |
| `dgVoodoo.conf` | mesmo zip | ~21 KB | Editado pelo `dgVoodooCpl.exe` ou por script |
| `dgVoodooCpl.exe` | mesmo zip | 450.560 B | Só edita o `.conf` da própria pasta |

**Não confundir com dgVoodoo 1.x** (`dgVoodoo1_50Beta2.zip`, 2007): é wrapper Glide/3dfx, tem `glide2x.dll` e `.vxd`, não tem pasta `MS`. Não serve.

### 3.6 Utilitários

| Arquivo | Uso |
|---|---|
| `DisplayDepth.fx` | Verificar visualmente se o depth buffer está correto |
| `dlss5-feed.cfg` | Criado automaticamente ao lado do addon; relido com o jogo rodando |
| `Verificar-GTA4-DLSS5.ps1` | Valida layout 32-bit + dgVoodoo, patcheia o `.conf` |
| `DLSS5-Kit.ps1` | Monta kit em `C:\DLSS5-Kit` e instala por arquitetura |

### 3.7 Arquivos que **NÃO** devem ir para a pasta do jogo

| Arquivo | Por quê |
|---|---|
| `sl.common.dll`, `sl.dlss.dll`, `sl.dlss_g.dll`, `sl.dlss_nr.dll`, `sl.interposer.dll`, `sl.nis.dll`, `sl.pcl.dll`, `sl.reflex.dll` | Interposer do Streamline — para jogos compilados contra Streamline. `sl.interposer.dll` disputa o mesmo ponto de interceptação do DXGI que o ReShade. |
| `nvngx_dlssg.dll` | Frame generation. Não usado, só consome VRAM. |
| `*.license.txt` | Inúteis |
| `ReShade_Setup_*.exe` | O instalador não fica no jogo |
| `dlss5-feed.addon64` em jogo x86 | Não carrega; ReShade ignora em silêncio |
| `dlss5-feed.addon32` em jogo x64 | Idem |
| Qualquer `.fx` solto na raiz | O ReShade busca em `reshade-shaders\Shaders\` |

### 3.8 Layout do kit reutilizável

```
C:\DLSS5-Kit\
├─ host64\                  ← copiar inteira para jogos x86
│  ├─ dlss5-feed-host64.exe
│  ├─ dxgi.dll              (x64)
│  ├─ renodx-dlss5.addon64
│  ├─ nvngx_dlssnr.dll
│  └─ nvngx_dlss.dll
├─ jogo32\
│  └─ dlss5-feed.addon32
├─ jogo64\
│  ├─ dlss5-feed.addon64
│  ├─ renodx-dlss5.addon64
│  ├─ nvngx_dlssnr.dll
│  └─ nvngx_dlss.dll
└─ shaders\
   ├─ DLSS5_Feed.fx
   ├─ vort_Motion.fx + Includes\vort_*.fxh (+ Textures\vort_BlueNoise.png)
   └─ (ReShade.fxh, ReShadeUI.fxh — do pacote padrão)
```

---

## 4. Pré-requisitos de sistema

| Item | Valor | Verificação |
|---|---|---|
| Driver NVIDIA | ≥ 616.56 | `nvidia-smi --query-gpu=driver_version --format=csv` |
| Override de assinatura NGX | DWORD 1 em 3 chaves | ver 12.1 |
| Reinício após o override | obrigatório | driver só lê na inicialização |
| NVIDIA Smooth Motion | desligado | incompatível com o Feeder |
| OptiScaler | não instalado no jogo | incompatível com o Feeder |
| MSAA/SSAA do jogo | desligado | Generic Depth não vê buffer multisampled; SSAA conflita com DLAA |

---

## 5. Árvore de decisão

**Regra de localização (vale para o documento inteiro):** onde se lê "raiz", leia **pasta do executável real**. E são duas perguntas distintas, não uma:

1. **Onde está o exe real?** → ReShade (`dxgi.dll`), `ReShade.ini`, addons, `reshade-shaders\` e `host64\` vão TODOS lá. O `LoadLibrary("dxgi.dll")` em runtime busca na pasta do exe, não na pasta do módulo que chamou.
2. **Onde está o módulo que chama Direct3D?** → o dgVoodoo (`D3D9.dll`) vai lá.

Na maioria dos jogos as respostas coincidem (RE2, Tomb Raider, GTA IV, Castlevania). No Source elas divergem: exe-stub na raiz, renderizador em `bin\` → dgVoodoo em `bin\`, ReShade na pasta do exe.

```
1. Localizar o executável REAL
   ├─ Pasta do exe ≠ raiz do jogo?         (Fable: Binaries\Win32\)
   ├─ Existe launcher separado?            (GTA IV: PlayGTAIV.exe vs GTAIV.exe)
   └─ O exe é um stub que carrega DLLs?    (Source: hl2.exe → bin\)

2. Arquitetura do exe (PE Machine: 0x14C = x86, 0x8664 = x64)
   ├─ x64 → CAMINHO A
   └─ x86 → passo 3

3. API gráfica disponível (módulos no processo, ou DLLs em bin\)
   ├─ D3D11 nativo         → CAMINHO B
   ├─ D3D9 (ou D3D9+Vulkan) → CAMINHO C (forçar D3D9, dgVoodoo)
   ├─ Vulkan apenas        → NÃO SUPORTADO em x86
   └─ D3D10                → NÃO SUPORTADO

4. DLSS nativo?
   ├─ Sim → renodx-dlss5.addon64 direto, sem Feeder
   └─ Não → Feeder (dlss5-feed.*)

5. Onde o renderizador chama DXGI?
   ├─ No próprio exe / DLL ao lado         → ReShade dxgi.dll na pasta do exe
   └─ Numa subpasta (Source: bin\)         → dgVoodoo em bin\, ReShade na RAIZ

6. Overlays pré-carregam DXGI? (gameoverlayrenderer, nvspcap, NvCamera)
   ├─ ReShade vence mesmo assim (exe ou dgVoodoo pede dxgi antes) → ok
   └─ ReShade perde → desligar overlays (única alavanca confiável)
```

---

## 6. Os três caminhos

### CAMINHO A — 64-bit (D3D11/D3D12)

Validado: **RE2 Remake** (D3D12), **RE7** (D3D12).

```
<pasta do exe>\
├─ jogo.exe
├─ dxgi.dll                  ← ReShade x64 (instalador, Direct3D 10/11/12, add-ons)
├─ ReShade.ini
├─ renodx-dlss5.addon64
├─ dlss5-feed.addon64        ← só se o jogo não tem DLSS nativo
├─ nvngx_dlssnr.dll
├─ nvngx_dlss.dll
└─ reshade-shaders\Shaders\
   ├─ DLSS5_Feed.fx
   ├─ <provedor MV>
   └─ ReShade.fxh, ReShadeUI.fxh
```

Sequência:
1. Instalar ReShade no exe, API Direct3D 10/11/12, add-ons habilitados, aceitar download dos efeitos padrão.
2. Copiar os 4 (ou 3, se DLSS nativo) arquivos para a pasta do exe.
3. Copiar shaders para `reshade-shaders\Shaders\`.
4. In-game: Add-ons → confirmar **DLSS 5 Feed** e **DLSS 5 Neural Rendering** listados.
5. Generic Depth ativo, buffer principal não-multisampled.
6. Efeitos: provedor MV **acima** do DLSS 5 Feed, ambos marcados.
7. Painel DLSS 5 Neural Rendering: marcar as duas caixas; `NGX hooks: creates 1`, `Successful NR frames` subindo.

Problema encontrado no RE2: `WAITING FOR NGX MODULES` + warning `no known texMotionVectors provider found`. Causa: Launchpad compilado mas **não marcado** na lista. Compilar ≠ ativar.

### CAMINHO B — 32-bit D3D11

Validado: **Tomb Raider 2013**.

```
<pasta do exe>\
├─ jogo.exe                  (x86)
├─ dxgi.dll                  ← ReShade x86 (~4,4 MB)
├─ ReShade.ini
├─ dlss5-feed.addon32        ← única peça do Feeder na raiz
├─ reshade-shaders\Shaders\  ← DLSS5_Feed.fx + provedor + .fxh padrão
└─ host64\
   ├─ dlss5-feed-host64.exe
   ├─ dxgi.dll               ← ReShade x64
   ├─ renodx-dlss5.addon64
   ├─ nvngx_dlssnr.dll
   └─ nvngx_dlss.dll
```

Sequência: igual ao A, com estas diferenças:
- **Nenhum** `.addon64` nem `nvngx_*` na pasta do exe — só dentro de `host64\`.
- `host64\` funciona como jogo falso: sobe swapchain D3D12 mínimo para o addon do DLSS 5.
- No Add-ons **não existe** entrada "DLSS 5 Neural Rendering". As opções de neural ficam **dentro do painel do DLSS 5 Feed**, grupo "on the host", com botão **Apply**.
- Primeiro frame alimentado abre janela "32-bit DLSS 5 Feeder". Normal. `host_window=0` esconde depois.
- Logs: `dlss5-feed.log` (raiz), `host64\dlss5-feed-host.log`, `host64\ReShade.log`.

Problema encontrado: `.addon64` copiados para a raiz → aba Add-ons vazia. Depois: `Motion vectors: none (not installed)` → DRME não instalado.

### CAMINHO C — 32-bit D3D9 via dgVoodoo2

Validado: **Castlevania: Lords of Shadow** (variante simples), **Half-Life 2** (variante Source, com stub). Parcial: **GTA IV**.

Adicionais em relação ao B:

```
<pasta onde o renderizador chama Direct3D>\   ← geralmente = pasta do exe; no Source é bin\
├─ D3D9.dll                  ← dgVoodoo2, de MS\x86\
├─ dgVoodoo.conf
└─ dgVoodooCpl.exe
```

`dgVoodoo.conf`:

| Chave | Valor | Motivo |
|---|---|---|
| `DisableAndPassThru` | `false` | Padrão de fábrica é `true` — dgVoodoo não faz nada |
| `VRAM` | `1024` | 256 causa "ran out of video memory"; 2048 estoura int32 |
| `VideoCard` | `internal3D` | |
| `dgVoodooWatermark` | `true` | Temporário, prova de vida |
| `[General] OutputAPI` | `d3d11_fl11_0` | |

Sequência:
1. Forçar D3D9 no jogo (Source: `-dxlevel 95`, remover depois da primeira execução).
2. dgVoodoo primeiro, ReShade depois (dgVoodoo é dono do nome `d3d9.dll`).
3. **Portão:** watermark visível na tela = dgVoodoo interceptando.
4. ReShade como `dxgi.dll` na **pasta do exe** (não em `bin\`), API Direct3D 10/11/12.
5. **Portão:** `ReShade.log` mostra `CreateSwapChain` + `Recreated runtime environment` (e não `Direct3DCreate9`).
6. Resto igual ao B.

As duas variantes do Caminho C:

| | Variante simples (Castlevania, GTA IV) | Variante Source (HL2) |
|---|---|---|
| Onde vai o dgVoodoo | pasta do exe | `bin\` (junto do `shaderapidx9.dll`) |
| Onde vai o ReShade `dxgi.dll` | pasta do exe | pasta do exe — **nunca** em `bin\` |
| Forçar D3D9 | já é D3D9 | `-dxlevel 95` (remover após 1ª execução) |
| Detecção da variante | não existe `bin\shaderapi*.dll` | existe `bin\shaderapi*.dll`; exe < 200 KB (stub) |
| Overlays | normalmente irrelevantes | venceu mesmo com overlays presentes, com `dxgi.dll` na pasta do exe |

---

### 6.4 Motor ShortFuse (64-bit, passadas múltiplas)

Alternativa ao par Krish + Feeder para jogo 64-bit, com ou sem DLSS nativo, D3D9/D3D11/D3D12:
o `renodx-dlss.addon64` do ShortFuse (`RenoDX DLSS`, seção `RENODX-DLSS` no `ReShade.ini`).
Ele fabrica a chamada de DLSS sozinho (`Require DLSS` em Auto: com o DLSS do jogo ligado usa os
guias do jogo; desligado, avalia o swapchain) e roda o Neural Rendering em **1 a 10 passadas**
sequenciais (`DirectNeuralRenderingPassCount`, "Pass Count" no painel) — a passada N consome a
saída da N-1. É o "DLSS 5 x2" dos vídeos.

Layout: igual ao caminho A sem Feeder e sem shader de vetores: `dxgi.dll` (ReShade x64),
`renodx-dlss.addon64`, `nvngx_dlssnr.dll`, `nvngx_dlss.dll` (regra do jogo com DLSS próprio
igual à do caminho A), `reshade-shaders\` (só para a aba Início não reclamar). `ReShade.ini`:
`[ADDON] LoadFromDllMain=renodx-dlss.addon64` (o addon pede carga no DllMain; sem a linha ele a
grava sozinho e pede reinício), `DisabledAddons=Generic Depth`, `EffectLoadSkipping=1`,
`[RENODX-DLSS] DirectNeuralRenderingPassCount=N`. Preset vazio. O `renodx-dlss5.addon64` e o
`dlss5-feed.addon64` não podem ficar na mesma pasta: o plano os remove com backup (e o inverso
ao voltar para o Krish).

Verificação: item 14 lê `Registered add-on "RenoDX DLSS"`, `RenoDX DLSS attached`,
`RenoDX DLSS-NR source evaluation completed` (OK), `could not attach the direct nvngx_dlssnr.dll
runtime` / `Neural Rendering device binding failed` (falha), `Added ... to ADDON.LoadFromDllMain
... Restart required` (reiniciar o jogo uma vez); item 24 confere o Pass Count do ini. O log do
Krish (`DLSS5 Generic`, `feature 18 evaluation succeeded`) não existe neste motor.

Não validado em jogo por este projeto: o motor entrou pelo que o binário declara e pelo que a
comunidade mostra. Se um jogo cair, o primeiro teste é Pass Count 1; o segundo é voltar ao Krish.

### 6.5 Passadas múltiplas em 32-bit: consumidor neural dentro de `host64\`

O addon do ShortFuse é 64-bit e vive no processo do jogo, logo não existe em caminho B/C. Em
32-bit o Neural Rendering acontece no `host64\dlss5-feed-host64.exe`, e o Feeder 0.15.0+
reconhece **três consumidores** ali (README: "OptiScaler is 64-bit only, so a 32-bit game runs
it inside host64\"; "exactly one may be present, or one of them goes inert while every check
still looks fine"). O perfil guarda `Engine` + `PassCount`; `MotorEfetivo` ignora ShortFuse em
x86 e OptiScaler/DFC em x64 (cai no Krish).

| Motor (`NeuralEngine`) | Arquivos em `host64\` | Passadas | Config gerada |
|---|---|---|---|
| `RenodxDlss5Feeder` (Krish) | `renodx-dlss5.addon64` | 1 | — |
| `OptiScalerNr` (OptiScaler v10.0.0-pre1 de 04/09/2026, do 7z do Discord: o build com `Passes`; o fork Dagherbou v0.2.0-patch1 não tem a chave e faz uma passada — `OptiScalerNr.SuportaPassadas` lê o ini do kit e o plano bloqueia 2+ passadas com ele) | `winmm.dll` (cópia de `OptiScaler.dll`: proxy que o host já importa), `nvngx.dll_dlssnr.dll` (shim que o OptiScaler carrega para a passada neural), `OptiScaler\D3D12_OptiScaler\D3D12Core.dll` (Agility SDK próprio), `OptiScaler.ini` | 1–5 | a partir do ini do kit: `[Upscalers] Dx12Upscaler=dlss` (o host é D3D12), `[DlssNr] Enabled=true`, `ScanExposure=false`, `Passes=N`, `[Log] LogToFile=true`. Menu do OptiScaler na tecla Insert (janela do host) |
| `DeepFriedChicken` 1.4.8 | `deep-fried-chicken.addon64`, `deep-fried-chicken-nvngx.dll`, `deep-fried-chicken.cfg` | 1–30 | a partir do cfg do kit: `enabled=1`, `arm=1`, `layers=N` |
| `RenodxDlssShortFuse` em x86 (`UsesShortFuseNoHost64`) — **validado** (SH2 EE, 09/09/2026: o único motor com x2+ visível; o OptiScaler construía passadas sem diferença na tela) | `renodx-dlss.addon64` + `ReShade.ini` do host mesclado (`ShortFuseNoHost64.GerarIni`: `[ADDON] LoadFromDllMain`, `[RENODX-DLSS] DirectNeuralRenderingPassCount=N`) | 1–10 | o addon intercepta o `NVSDK_NGX_D3D12_EvaluateFeature` do host; o Feeder não o reconhece (host: "renodx-dlss5*.addon64 not found", segue servindo DLAA); verificação item 25 lê `host64\ReShade.log` com `ShortFuseLog` |

Plano (32-bit): o bloco `host64\` copia o consumidor escolhido e agenda `DeleteForbiddenFile`
(com backup) para os arquivos dos outros dois — inclusive `renodx-dlss5-*.addon64` de qualquer
nome, que o Feeder também carregaria. O `KitResolver` acha `OptiScaler.dll` pelo marcador
`OptiScaler.ini` na mesma pasta (o `winmm.dll` de um jogo pode ser outra coisa; a remoção só
apaga o `winmm.dll` que contém o texto `OptiScaler.ini`) e os três do DFC pelo nome exato. Kit
sem o OptiScaler → bloqueio "Falta no kit: OptiScaler.dll…"; sem o DFC → bloqueio apontando o
Discord. Em 64-bit nenhum dos dois é oferecido (lá o x2+ é o ShortFuse).

Verificação: item 25 lê `host64\OptiScaler.log` (`min GPU architecture 0x0` = o OptiScaler
respondeu à sondagem do DLSS, `nvngx.dll_dlssnr.dll` carregado após o primeiro evaluate =
passada neural rodou) ou o log do DFC; item 26 lê `host64\dlss5-feed-host.log` à procura de
`evaluate raised 0xC0000005 in D3D12Core.dll` — o defeito **driver 616.64+ × renodx-dlss5
4.6/4.7** medido pelo autor do Feeder (0/300; 4.55, DFC e OptiScaler 300/300). Item 25b: as
passadas que rodaram = maior entre `composition ... xN pass(es)` (sai uma vez, no 1º quadro) e
`pass N built at` (as extras chegam depois; SH2 EE: x4 com DLSS do host a 6,77 ms/quadro). O
OptiScaler como `winmm.dll` carrega o `dxgi.dll` do System32 antes do host e o ReShade de
`host64\dxgi.dll` fica de fora; o ini gerado leva `[Plugins] LoadReshade=true` e o plano copia o
ReShade x64 como `host64\ReShade64.dll`. O botão
"Testar o host64…" roda `dlss5-feed-host64.exe --test` (DLAA sintético + NR, 300 avaliações,
~15 s, sem jogo) e interpreta `--test finished: N/300 evaluates succeeded`.

**Validado em 09/09/2026 (RTX 4070 Ti, driver 616.64) — ShortFuse dentro do host64:**

- **Silent Hill 2 Enhanced Edition** (D3D8 → mod d3d8to9 → dgVoodoo D3D9 → host64): `host64\ReShade.log`
  mostra `Registered add-on "RenoDX DLSS"`, `RenoDX DLSS attached`, `DLSS-NR direct: attached snippet
  host64\nvngx_dlssnr.dll`, `CreateFeature(Reserved18) succeeded ... size=1920x1080` (uma feature por
  passada; recriadas ao mudar Pass Count ou escala — 45 creates numa sessão de 7 min) e `EvaluateFeature
  succeeded: evaluation=N` até N=2859+ (1717 linhas). A primeira avaliação foi no swapchain do próprio
  host (`source=3`, 900x1064 = a janela do painel), as seguintes na saída do DLSS do host. **É o único
  motor em que o x2+ apareceu na tela**; o OptiScaler construía as passadas sem diferença visível.
- **Enslaved: Odyssey to the West** (32-bit, D3D9 via dgVoodoo → host64): `source=1` a 2560x1440
  (`replace_source=true`), 4 features Reserved18 (4 passadas), 101 avaliações registradas.

O host segue dizendo `renodx-dlss5*.addon64 not found next to the host` e "serving plain DLAA" — ele não
sabe que o ShortFuse está lá, e mesmo assim as passadas saem, porque o addon intercepta o
`NVSDK_NGX_D3D12_EvaluateFeature` do host (hooks `loaded-module hooks active 3/4` em `_nvngx.dll` e em
`host64\nvngx_dlss.dll`). `ShortFuseLog.Ler` conta avaliações, features e o snippet para o item 25.

## 7. O que fizemos em cada jogo

### RE2 Remake — x64, D3D12, sem DLSS nativo
- Branch padrão da Steam (DX12, ray tracing). **Não** rolar para `dx11_non-rt`.
- Caminho A completo.
- Bloqueio: `Motion vectors: none` — Launchpad compilado, não marcado. Fix: marcar Launchpad acima do Feed.
- Sintoma `0xBAD00007` (NGX failure) era consequência de nunca receber evaluate válido.

### RE7 — x64, D3D12
- Mesmo caminho do RE2. Não depurado em detalhe nesta sessão.

### Tomb Raider 2013 — x86, D3D11
- Caminho B.
- Bloqueio 1: `.addon64` na raiz → Add-ons vazio. Fix: `addon32` na raiz, `.addon64` só em `host64\`.
- Bloqueio 2: `Motion vectors: none (not installed)`. Fix: DRME em `reshade-shaders\Shaders\`.
- Resultado: `Feed: built`, `Host process: running`, `Frames delivered: 1635+`.

### Half-Life 2 — x86, D3D9 + Vulkan disponível, Source engine
Caso mais difícil. Cinco obstáculos independentes:

1. **Vulkan 32-bit não suportado.** Log: `only Direct3D 11 games are supported by the 32-bit add-on`. Solução: `-dxlevel 95` + dgVoodoo.
2. **`hl2.exe` é stub de 127 KB.** O renderizador é `bin\shaderapidx9.dll`. `D3D9.dll` do dgVoodoo na raiz não carrega; tem que estar em **`bin\`** (verificado por módulos do processo: `d3d9.dll` de `bin\` após mover).
3. **ReShade `dxgi.dll` em `bin\` não carrega.** O `LoadLibrary("dxgi.dll")` do dgVoodoo busca na pasta do **exe** (raiz), não em `bin\`. ReShade tem que ficar na **raiz**.
4. **Overlays pré-carregam DXGI.** `gameoverlayrenderer.dll`, `nvspcap.dll`, `NvCamera32.dll` no processo. Suspeita: bloqueariam o ReShade. Na prática, com `dxgi.dll` na raiz o ReShade venceu **mesmo com os overlays presentes**. O bloqueio real era o item 3.
5. **`dinput8.dll` como porta lateral não serve.** ReShade carrega e instala hooks nas funções do sistema, mas **não vê** o swapchain do dgVoodoo (log sem `CreateSwapChain`). Para ver, precisa **ser** o `dxgi.dll` que o dgVoodoo carrega.

Pós-Home: `ReShade.ini` gerado por script sem `[GENERAL]` → efeitos não encontrados; desinstalador do ReShade **apaga** `reshade-shaders\`; MSAA ligado → depth multisampled. Tudo resolvido; resultado funcional.

Estado final HL2: dgVoodoo em `bin\`, ReShade `dxgi.dll` na raiz, overlays desligados (Steam por jogo, NVIDIA global), `-dxlevel 95`, MSAA off.

### GTA IV — x86, D3D9
- Caminho C. Exe (`GTAIV.exe`) na mesma pasta que as DLLs — sem a complicação do `bin\`.
- Launcher separado: `PlayGTAIV.exe`. ReShade e dgVoodoo apontam para `GTAIV.exe`.
- Atingido: watermark do dgVoodoo visível. Pendente: reinstalar ReShade em D3D10/11/12, provedor MV.
- Aviso: medidor de VRAM do menu do jogo lê o valor virtual do dgVoodoo (1 GB).

### Silent Hill 2 Enhanced Edition — x86, D3D8 atrás da mod (d3d8to9)
- O `d3d8.dll` da pasta é o módulo **Silent Hill 2 Enhancements** — a própria mod (60 fps, widescreen, texturas), não um wrapper sobrando. Com `d3d8to9 = 1` (padrão, exigido pelos shaders dela) ele converte o jogo para DirectX 9 e, ao criar o Direct3D 9, tenta nesta ordem: 9On12 (se ligado no ini), o **`d3d9.dll` da própria pasta** (`GetLocalDirect3DCreate9`) e só então o do System32.
- Logo o dgVoodoo **não** entra como `D3D8.dll` (sobrescrever tira a mod; foi o que a instalação antiga fazia, e a checagem de ocupante passou a recusar): entra como **`D3D9.dll`** ao lado (`GameProfile.D3d8ViaD3D9`, decidido pelo marcador `Silent Hill 2 Enhancements` ou `d3d8to9` no `D3D8.dll`; ver `D3d8to9Wrapper`). O `dgVoodoo.conf` sai no perfil padrão, não no "Legado": a mod quer a resolução do monitor. O resto é a rota C normal: dgVoodoo → D3D11 → `dxgi.dll` (ReShade x86) → Feeder addon32 + `host64\`.
- O manifesto guarda a decisão; verificação (item 5), isolamento (só desliga o `D3D8.dll`/`D3D9.dll` que tenha o marcador `dgVoodoo`) e desinstalação olham o `D3D9.dll`.

### Enslaved: Odyssey to the West — x86, D3D9 (Unreal Engine 3)
- Caminho C (dgVoodoo D3D9 em `Binaries\Win32\`), ShortFuse dentro do host64 com 4 passadas a 2560x1440: `source=1`, 4 features Reserved18, avaliando. Validado em 09/09/2026, segundo jogo do arranjo depois do SH2 EE.

### Castlevania: Lords of Shadow Ultimate Edition — x86, D3D9
- Caminho C, **variante simples**: exe, dgVoodoo e ReShade na mesma pasta. Funcionou de primeira seguindo a sequência padrão — primeira validação limpa do Caminho C do início ao fim.
- AA do jogo é **FXAA** (pós-processo): não conflita com o Generic Depth, não precisa desligar. A regra "desligar AA" vale para MSAA/SSAA, não para FXAA/SMAA.
- Steam DRM com exe ofuscado: irrelevante para ReShade/dgVoodoo, que entram por DLL e não tocam o executável.

---

## 8. Variações e armadilhas identificadas

### 8.1 Arquitetura
- Detectar pelo cabeçalho PE, nunca pelo nome da pasta.
- `Get-Process.Modules` de um PowerShell x64 **retorna vazio** para processo x86. Usar `C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe`.
- Dois `dxgi.dll` de arquiteturas diferentes: x86 no jogo, x64 em `host64\`. Trocar = nada acontece, sem erro.

### 8.2 Localização — a regra das duas perguntas
- **Pergunta 1:** onde está o exe real? → ReShade `dxgi.dll`, `ReShade.ini`, addons, `reshade-shaders\` e `host64\` vão todos lá.
- **Pergunta 2:** onde está o módulo que chama Direct3D? → dgVoodoo (`D3D9.dll`) vai lá.
- `LoadLibrary("dxgi.dll")` em runtime resolve pela pasta do **exe**, não pela pasta do chamador — por isso ReShade em `bin\` não carrega no Source, mas o `D3D9.dll` do dgVoodoo em `bin\` carrega (o Source resolve as próprias DLLs a partir de `bin\`).
- Pasta do exe ≠ raiz do jogo em alguns títulos (Fable: `Binaries\Win32\`).
- Launcher vs exe real (GTA IV: `PlayGTAIV.exe` vs `GTAIV.exe`) — instalador do ReShade e dgVoodoo apontam sempre para o exe real.
- Exe-stub (< 200 KB) + renderizador em subpasta = variante Source.

### 8.3 ReShade
- Instalador: escolher API pelo **device final** que o ReShade vai enganchar. Com dgVoodoo, é Direct3D 10/11/12 mesmo num jogo de 2004.
- Nunca instalar como `d3d9.dll` quando dgVoodoo está presente.
- Vulkan: ReShade é layer global, sem DLL na pasta; `ReShade.ini` precisa de `[ADDON] AddonPath=.\`.
- `ReShade.ini` gerado manualmente precisa de `[GENERAL] EffectSearchPaths=.\reshade-shaders\Shaders\**` e `TextureSearchPaths=.\reshade-shaders\Textures\**`.
- **Desinstalar o ReShade apaga `reshade-shaders\`.** Manter cópia no kit.
- `ReShade.log` de 982 bytes = placeholder "não fui carregado".
- Tecla Home pode ser engolida por captura de teclado (Source/SDL). Alternativa: `[INPUT] KeyOverlay=45,0,0,0` (Insert), rodar em janela.
- ReShade compila **todos** os `.fx` da pasta. Compilar ≠ ativar. Só técnicas marcadas rodam.

### 8.4 Ordem de efeitos
- Provedor de motion vectors **acima** do DLSS 5 Feed, sempre.
- Tudo abaixo do Feed é aplicado por cima da saída neural.
- Automatizável via `ReShadePreset.ini`: `Techniques=vort_MotionEffects@vort_Motion.fx,DLSS5_Feed@DLSS5_Feed.fx` (ordem da lista = ordem de execução) mais `[DLSS5_Feed.fx]` / `PreprocessorDefinitions=DLSS5_MV_PROVIDER=2`.

### 8.5 Depth
- Generic Depth precisa estar ativo e pegando o buffer da cena.
- Buffer marcado "Multisampled" = indisponível → desligar MSAA no jogo.
- RE Engine e Source às vezes exigem `RESHADE_DEPTH_INPUT_IS_REVERSED` / `IS_UPSIDE_DOWN`.
- `DisplayDepth.fx` para verificar.

### 8.6 Motion vectors
- DRME: erro X3020 no ReShade 6.8 em qualquer API — não é só Vulkan. Fora do kit.
- VORT (MIT, no kit) é o padrão; Launchpad funciona, mas é proprietário; LumeniteFX é o recomendado pelo Feeder, mas o usuário precisa baixar.
- O Feed.fx só lê o provedor certo se `DLSS5_MV_PROVIDER` estiver definido para ele; provedor marcado ≠ provedor lido.
- Sintoma de MV zerado: imagem nítida parada, borra em movimento.
- Sintoma de MV com sinal errado: imagem duplica/arrasta. Fix: inverter componente do `MV_SIGN` no `DLSS5_Feed.fx`.

### 8.7 dgVoodoo2
- `DisableAndPassThru=true` de fábrica é a causa nº 1 de "não faz nada".
- `VRAM=256MB` de fábrica causa crash de memória.
- Watermark é o único teste confiável de que está ativo.
- Versão 1.x é outro produto (Glide). Teste: o zip tem pasta `MS`?
- Tela cheia exclusiva: o host64 (janela atrás do jogo, D3D12) e o painel projetado (thumbnail do compositor) brigam com o swapchain do jogo. Enslaved (10/09/2026): `SetFullscreenState(TRUE)` no `ReShade.log`, 1,2 s depois o addon32 lança o host, o host vê o jogo conectar, e o jogo congela antes de mandar o build (`host spawned` sem `host connected`). Opção `ForcarJanela` (`InstallOptions`), para QUALQUER jogo que sobe o host64 (rotas B e C), tenha ou não opção de janela: como o feed é addon do ReShade dentro do jogo, o `ReShade.ini` do jogo recebe `[APP] ForceWindowed=1` (+`ForceFullscreen=0`) **e** a pasta do jogo recebe `swapchain_override.addon32`. **O ReShade 6 não lê mais essa chave sozinho** — conferido na fonte (`crosire/reshade`, commit 358c345, 03/09/2026): `ForceWindowed` existe no 5.9.2 (`dxgi.cpp`, `modify_swapchain_desc`) e some no 6.x; a mesma função virou o exemplo oficial `examples/16-swapchain_override`, um addon que lê as chaves `[APP]`, zera `desc.fullscreen_state` no `create_swapchain` e devolve `true` no `set_fullscreen_state` quando o jogo pede tela cheia — o `SetFullscreenState(TRUE)` do log do Enslaved é exatamente essa chamada. O kit compila o exemplo no Actions (`compilar-addons.yml`, commit fixado em `swapchain-override-desejado.txt`) e o plano o copia (`JanelaForcada`); com o addon carregado, a linha `SetFullscreenState(TRUE)` do log passa a ser um pedido bloqueado (o ReShade loga antes de consultar os addons), e a prova é `Registered add-on "Swap chain override"`. Sem o addon a chave é letra morta — foi por isso que "já havia marcado" e nada mudou. Atenção ao ler o log do jogo: o `ReShade.log` 32-bit mostra `D3D11CreateDevice` e um swapchain DXGI tanto num jogo D3D11 nativo (rota B) quanto num jogo D3D9 atrás do dgVoodoo (rota C) — o dgVoodoo é o `d3d9.dll` LOCAL, o ReShade só engancha o `d3d9.dll` do System32, então o dgVoodoo nunca aparece no log e o D3D11 que aparece é o dele. O Enslaved (UE3, `LaunchUnrealUWindowsClient`) é tido como D3D9 na internet; o instalador foi testado nas duas rotas (10/09/2026, 13:05) e o congelamento foi idêntico nas duas. Por isso a opção `ForcarJanela` puxa as DUAS alavancas de uma vez, e a rota certa fica sendo um detalhe. Na rota C (jogo antigo por trás do dgVoodoo) o `dgVoodoo.conf` também sai com `FullScreenMode=false`, `ScalingMode=stretched_ar`, `WindowedAttributes=borderless, fullscreensize` — as duas alavancas juntas. Item 16b da verificação reconhece a assinatura e lê o `dgVoodoo.conf` do renderizador: com `FullScreenMode=true` diz que a opção NÃO está valendo (o quarto log do Enslaved, 01:46, ainda tinha `SetFullscreenState(TRUE)`) e dá a edição manual das três chaves; com `false` propõe o isolamento (motor Krish). O host da sessão que funcionou (09/09, 13:16) também tinha janela 900x1332 — a área de trabalho não é a diferença. O README do Feeder: "windowed is smoother"; painel via "Show as texture" (`cast_mode=1`) em tela cheia.
- Nome ocupado: `D3D9.dll` do DxWrapper → encadeia por `RealDllPath` (6.x, Dead Space 2); `D3D8.dll` de uma mod com d3d8to9 (SH2 Enhanced Edition) → o dgVoodoo entra como `D3D9.dll` atrás dela; qualquer outro wrapper com o nome → o plano recusa, porque sobrescrever é apostar com o jogo.

### 8.8 Overlays
- Injetam na criação do processo: `gameoverlayrenderer.dll` (Steam), `nvspcap.dll` (NVIDIA ShadowPlay), `NvCamera32.dll` (Ansel), `DiscordHook`, `RTSSHooks`.
- Pré-carregam DXGI/D3D11 do sistema.
- Não desativáveis por script de forma confiável: Steam restaura `gameoverlayrenderer.dll` ao reabrir; NVIDIA App ignora chaves do GeForce Experience; `NvCameraEnable.exe` pode não existir.
- Alavanca real: Steam → Propriedades do jogo → sobreposição; NVIDIA App → Sobreposição no jogo.
- Na prática, com o ReShade `dxgi.dll` na pasta do exe, o ReShade venceu mesmo com overlays presentes (HL2). Overlays só são bloqueio quando o pedido de DXGI do jogo chega depois do deles.

### 8.9 Source engine (caso especial)
- `-dxlevel 95` força D3D9; remover após primeira execução (senão reseta config a cada abertura).
- `-vulkan` força Vulkan (inútil para x86).
- Módulos de render: `bin\shaderapidx9.dll`, `bin\shaderapivk.dll`, `bin\dxvk_d3d9.dll`.
- `hl2.exe` = 127 KB, stub.

### 8.10 Assinatura / registro
- `nvngx_dlssnr.dll` patcheado → Authenticode inválida (HashMismatch) → NGX recusa sem override.
- Override é **global** (sistema inteiro). Anti-cheat (EAC, BattlEye) pode tratar como violação de integridade.
- `.reg` mesclado sem elevação ou salvo como `.reg.txt` não aplica. Verificar com `reg query`.
- Sem reinício, não vale.

### 8.11 VRAM
- Ada não tem Neural Texture Compression. DLAA only = pior caso de consumo.
- Crashes em RTX 40 com < 16 GB são quase sempre VRAM. Testar em 1080p janela primeiro.

### 8.12 Anticheat que sobe junto com o exe (EA Javelin, Easy Anti-Cheat)
- Os arquivos instalados são os de sempre; o que muda é que o processo não carrega DLL que o anticheat não reconhece. Sintoma fixo: `ReShade.log` **nem nasce** (não é overlay, não é nome de DLL).
- **EA Javelin** (FC, Battlefield, F1…): a Steam chama `EAAntiCheat.GameServiceLauncher.exe`. O programa reconhece pelo launcher na pasta e explica o caminho do Live Editor (offline).
- **Easy Anti-Cheat** (Gears of War Reloaded): a Steam abre `start_protected_game.exe` (raiz), o bootstrapper lê `EasyAntiCheat\\Settings.json` (raiz; `"executable": "Binaries_x64/GOWDE-Steam.exe"`) e só então sobe o jogo sob o EAC. Com o kit na pasta o jogo fecha com **"Your machine does not support Direct3D 12. Force quitting."** — é o EAC recusando o `dxgi.dll`, não a placa. O programa reconhece a pasta `EasyAntiCheat` (ou `start_protected_game.exe`, `EasyAntiCheat_EOS_Setup.exe`…) e diz o contorno para jogar a campanha offline na Steam: **Opções de inicialização** = `"<caminho>\\Binaries_x64\\GOWDE-Steam.exe" %command%` — a Steam abre o exe real e o bootstrapper fica de fora. Trocar o `productid` do `Settings.json` (o contorno do Game Pass) **não funciona na Steam**: o bootstrapper responde "No anti-cheat module has been found for this game and platform" e o jogo não abre. Multiplayer recusa sem o EAC. **O programa não edita arquivo de anticheat** — reconhece, avisa (detecção, plano, item 7, passos manuais, "Isolar a causa", botão Abrir o jogo) e deixa a decisão com o usuário.
- A verificação ganhou o **item 23**: lê as Opções de inicialização da Steam (`userdata\\*\\config\\localconfig.vdf`, bloco do AppID) — exe real + `%command%` = OK; sem opção ou outra opção = FALHA com a linha exata a colar; `productid` alterado = FALHA ("devolva a letra"). O botão "Copiar opção de inicialização (EAC)" põe a linha na área de transferência; colar na Steam continua sendo do usuário.
- O exe do Gears é cifrado (25 MB sem uma string de API). O `ApiDetector` dizia "Vulkan" por causa de `vulkan-1.dll` dentro do `nvngx_dlss.dll`. Regra nova: DLL de fornecedor (nvngx*, sl.*, XeSS, FidelityFX, d3dcompiler) e proxies (dxgi/d3d11/d3d12/dinput8…) não entram na varredura de renderizador; `GOWDE-*` é D3D12 pelo nome; exe que exporta `D3D12SDKVersion` ou traz `D3D12\D3D12Core.dll` (Agility SDK) é D3D12; `D3D12CreateDevice` no `ReShade.log` conta como D3D12 quando não há Feeder na pasta. Exe sem pista nenhuma vira a nota "exe cifrado" na detecção.

---

## 9. Checkpoints de verificação (em ordem)

| # | Checkpoint | Como verificar | Se falhar |
|---|---|---|---|
| 1 | Override no registro = 1 nas 3 chaves | `reg query` | Reaplicar como admin |
| 2 | Reiniciou após o override | `LastBootUpTime` > hora do merge | Reiniciar |
| 3 | Driver ≥ 616.56 | `nvidia-smi` | Atualizar |
| 4 | Exe real identificado, arch conhecida | PE Machine | — |
| 5 | (C) Watermark do dgVoodoo na tela | visual | dgVoodoo no lugar errado ou passthru=true |
| 6 | `dxgi.dll` do jogo com arch = exe; `host64\dxgi.dll` = x64 | PE Machine | Trocar |
| 7 | ReShade carregou | `ReShade.log` > 982 B, `Initializing crosire's ReShade` | Arquitetura ou local errado |
| 8 | ReShade viu o swapchain | `ReShade.log`: `CreateSwapChain` + `Recreated runtime environment` | ReShade não é a factory (não é o `dxgi.dll` que o renderizador carrega) |
| 9 | Banner do ReShade na tela | visual | = item 8 |
| 10 | Add-ons lista DLSS 5 Feed (e Neural Rendering se x64) | in-game | addon na pasta errada / arch errada / `AddonPath` |
| 11 | Efeitos encontrados | aba Início não diz "Nenhum .fx" | `EffectSearchPaths` no `.ini` / pasta `reshade-shaders` apagada |
| 12 | Generic Depth ativo, buffer principal não-multisampled | aba Add-ons | MSAA off |
| 13 | Provedor MV marcado acima do Feed | lista de efeitos | Marcar / reordenar |
| 14 | Painel Feed: `Feed: built`, `Host: running`, `Motion vectors → <nome>` | aba Add-ons | Ver diagnóstico |
| 15 | `dlss5-feed.log`: `feature ready … DLAA`, `frame N delivered` | arquivo | — |
| 16 | (x64) `NGX hooks: creates 1`, `Successful NR frames` > 0 | painel RenoDX | STANDBY: esperar 10 s (warm-up frame 180) |
| 24 | (ShortFuse) Pass Count do `ReShade.ini` = o do perfil | `[RENODX-DLSS] DirectNeuralRenderingPassCount` | Instalar de novo |
| 25 | (32-bit, OptiScaler/DFC) consumidor alternativo rodou em `host64\` | `host64\OptiScaler.log`: `min GPU architecture 0x0` + `nvngx.dll_dlssnr.dll`; DFC: log próprio | dois consumidores na pasta / `Enabled=false` / Defender apagou o DFC |
| 26 | (32-bit) nenhuma falha dentro do NGX do driver | `host64\dlss5-feed-host.log` sem `evaluate raised 0xC0000005 in D3D12Core.dll` | driver 616.64+ com addon 4.6/4.7: usar 4.55, DFC ou OptiScaler; ou driver 616.56. "Testar o host64…" reproduz em 15 s |

---

## 10. Diagnóstico por sintoma

| Sintoma | Causa provável | Fix |
|---|---|---|
| Aba Add-ons vazia | addon com arch errada, ou fora da raiz, ou `AddonPath` errado (Vulkan) | addon32 em x86 / addon64 em x64; `AddonPath=.\` |
| `ReShade.log` = 982 B | ReShade nunca carregou | arch do `dxgi.dll`, local, ou nome disputado |
| ReShade carregou, sem banner, sem `CreateSwapChain` no log | ReShade não é a factory que o renderizador usa | `dxgi.dll` na pasta do exe; overlays |
| Home não abre com banner visível | tecla capturada | `KeyOverlay=45`, janela |
| "Nenhum arquivo de efeito (.fx)" | `EffectSearchPaths` ausente ou pasta apagada | recriar `.ini` e `reshade-shaders\` |
| `Motion vectors: none (not installed)` | provedor não está na pasta | copiar DRME/Launchpad |
| `provider … installed but DISABLED` | provedor não marcado | marcar acima do Feed, Re-enable |
| `no known texMotionVectors provider found` | idem, ou provedor com erro de compilação | ver log por `error X` |
| `Feed: disabled`, `Host: not running` | consequência de MV ausente | resolver MV |
| Caixa "Engine Error: failed to lock vertex buffer in CMeshDX8::LockVertexBuffer" ao abrir/carregar (engine Source: Black Mesa, 11/09/2026) | Memória do processo 32-bit acabou: sem a flag LAA são 2 GB para jogo + dgVoodoo + ReShade + feed + driver (PCGamingWiki/dgVoodoo: "must be patched to be 4GB aware") | Item 5 da verificação lê a flag no exe (`PeFile.IsLargeAddressAware`); aplicar o 4GB Patch (NTCore) no exe-stub; se persistir, "Isolar a causa" sem o Feeder |
| Jogo congela ao ligar o DLSS 5; `dlss5-feed.log` termina em `host spawned` sem `host connected`; host log tem `game pid connected` e nenhum `build:` | Tela cheia exclusiva: o jogo trava no aperto de mão quando o host sobe (Enslaved, 10/09/2026) | Jogo em janela/sem borda; rota C: opção "dgVoodoo em janela sem borda" + Instalar de novo (ou `FullScreenMode = false`, `ScalingMode = stretched_ar`, `WindowedAttributes = borderless, fullscreensize` à mão no dgVoodoo.conf); conferir no `ReShade.log` do jogo que não há mais `Fullscreen = TRUE`; painel via "Show as texture" |
| `Stopped: the 64-bit host went away` repetido; `host64\dlss5-feed-host.log` diz `the D3D12 device was removed (0x887A0001) during an evaluate` logo após `feature ready` | Host REINICIADO pelo painel ("Restart"/"Apply to the DLSS 5 host") com o ShortFuse dentro dele: o primeiro host roda, cada host reiniciado morre no primeiro quadro (Silent Hill Homecoming, 10/09/2026). A seção "neural-rendering settings (on the host)" do painel é do Krish e o ShortFuse a ignora — mexer ali só reinicia o host | Fechar e abrir o jogo; nunca o botão de restart com o ShortFuse no host. Item 26b da verificação decodifica o código DXGI |
| `device was removed (0x887A0006 DEVICE_HUNG)` depois de minutos | GPU hang na avaliação (Feeder issue #57) | baixar `work_resolution`/passadas; trocar consumidor para isolar |
| `only Direct3D 11 games are supported by the 32-bit add-on` | jogo x86 em Vulkan | D3D9 + dgVoodoo |
| `WAITING FOR NGX MODULES` (x64) | Feed não entregou frame válido | MV / depth |
| `0xBAD00007` | NGX nunca recebeu evaluate válido, ou override/reboot faltando | itens 1–2 + MV |
| STANDBY/FAILED persistente | assinatura / override | `reg query` + reboot |
| Buffer "Multisampled", "Not all depth buffers available" | MSAA ligado | desligar |
| Watermark do dgVoodoo ausente | `DisableAndPassThru=true`, `D3D9.dll` x64, ou pasta errada (Source: `bin\`) | corrigir |
| Jogo em D3D9 carrega `d3d9.dll` de System32 | dgVoodoo não está onde o renderizador busca | mover para a pasta do módulo que chama D3D9 |
| Trava/fecha sozinho | VRAM (Ada), `sl.*.dll` na pasta, `create_delay` baixo | 1080p, limpar, não mexer no delay |
| Imagem borra em movimento | MV zerado | provedor |
| Imagem duplica/arrasta | sinal do MV invertido | `MV_SIGN` |
| Warping em poeira/fumaça | preset | `preset=5` ou `6` (CNN E/F) |
| Rename de `gameoverlayrenderer.dll` não pega | Steam restaura ao reabrir | usar a UI |

---

## 11. O que é automatizável

### 11.1 Totalmente automatizável
- Detecção de arquitetura do exe (PE header).
- Detecção de API pelos módulos do processo (com PowerShell x86 para jogos x86) ou por DLLs presentes (`shaderapi*.dll` no Source).
- Extração do `ReShade32.dll`/`ReShade64.dll` do instalador (é um ZIP embutido) → `dxgi.dll`.
- Colocação de todos os arquivos por caminho (A/B/C).
- Geração do `ReShade.ini` completo (`[GENERAL]`, `[INPUT]`, `[ADDON]`).
- Geração do `ReShadePreset.ini` com `Techniques=` na ordem correta → elimina o passo manual de marcar/reordenar.
- Patch do `dgVoodoo.conf` (5 chaves).
- Override de assinatura no registro (com verificação).
- Verificação de todos os checkpoints por leitura de logs e PE headers.
- Limpeza de arquivos proibidos (3.7).
- Backup/restauração (lista de arquivos adicionados por jogo).

### 11.2 Parcialmente automatizável
- Instalação do ReShade: o instalador é GUI, mas extrair a DLL direto e gerar o `.ini` substitui ele por completo. Falta apenas o pacote de shaders padrão (`ReShade.fxh` etc.), que pode ser mantido no kit.
- Detecção de pasta do renderizador (Source): heurística — se existe `bin\shaderapidx9.dll`, dgVoodoo vai para `bin\`.
- Opções de inicialização da Steam (`-dxlevel 95`): editar `localconfig.vdf` com a Steam fechada. Frágil.

### 11.3 Não automatizável de forma confiável
- Desligar overlays (Steam por jogo, NVIDIA App). Só a UI funciona.
- Confirmação visual (watermark, banner).
- Ajuste de `RESHADE_DEPTH_INPUT_*` — depende de olhar o `DisplayDepth`.
- Escolha entre DRME e Launchpad quando um não compila — precisa ler o log e trocar.

### 11.4 Arquitetura sugerida para o programa

```
1. Perfil de jogo (JSON): pasta, exe, arch, API, dlss_nativo, pasta_renderizador,
   launcher, precisa_dgvoodoo, provedor_mv, launch_options
2. Detecção automática → preenche o perfil → usuário confirma/ajusta
3. Plano de instalação derivado do perfil (lista de cópias + arquivos gerados)
4. Execução com log próprio e manifesto do que foi adicionado (para reverter)
5. Verificação pós-instalação: checkpoints 1–8 por leitura de arquivos;
   9–16 exigem o jogo aberto (opcional: lançar, esperar N s, ler logs, fechar)
6. Diagnóstico: mapear sintomas da tabela 10 a partir dos logs
7. Reversão: apagar manifesto, restaurar .conf.bak, remover override
```

---

## 12. Snippets reutilizáveis

### 12.1 Override de assinatura NGX

```powershell
$guid = "{41FCC608-8496-4DEF-B43E-7D9BD675A6FF}"
$alvos = @("HKLM:\SOFTWARE\NVIDIA Corporation\Global",
           "HKLM:\SYSTEM\ControlSet001\Services\nvlddmkm",
           "HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm")
# ativar
foreach ($p in $alvos) {
  if (-not (Test-Path $p)) { New-Item -Path $p -Force | Out-Null }
  New-ItemProperty -Path $p -Name $guid -Value 1 -PropertyType DWord -Force | Out-Null }
# verificar
foreach ($p in $alvos) { (Get-ItemProperty $p -Name $guid -ErrorAction SilentlyContinue).$guid }
# reverter
foreach ($p in $alvos) { Remove-ItemProperty -Path $p -Name $guid -ErrorAction SilentlyContinue }
```

### 12.2 Arquitetura de um PE

```powershell
function Get-PEArch([string]$p) {
    $fs = [IO.File]::OpenRead($p); $br = New-Object IO.BinaryReader($fs)
    $fs.Position = 0x3C; $off = $br.ReadInt32()
    $fs.Position = $off + 4; $m = $br.ReadUInt16()
    $br.Close(); $fs.Close()
    switch ($m) { 0x014C {'x86'} 0x8664 {'x64'} default {'?'} }
}
```

### 12.3 Módulos de um processo x86

```powershell
& "C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -Command `
  "(Get-Process <nome>).Modules | Where-Object ModuleName -match 'd3d9|d3d11|dxgi|shaderapi|vulkan|GameOverlay|nvspcap|NvCamera' | Select-Object ModuleName, FileName"
```

### 12.4 Extrair ReShade x64 do instalador

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($setup)
$e = $zip.Entries | Where-Object Name -eq 'ReShade64.dll'   # ou ReShade32.dll
[IO.Compression.ZipFileExtensions]::ExtractToFile($e, "$destino\dxgi.dll", $true)
$zip.Dispose()
```

### 12.5 ReShade.ini mínimo funcional

```ini
[GENERAL]
EffectSearchPaths=.\reshade-shaders\Shaders\**
TextureSearchPaths=.\reshade-shaders\Textures\**

[INPUT]
KeyOverlay=36,0,0,0

[ADDON]
AddonPath=.\
```

### 12.6 ReShadePreset.ini com ordem pré-definida

```ini
Techniques=DRME@MotionEstimation.fx,DLSS5_Feed@DLSS5_Feed.fx
TechniqueSorting=DRME@MotionEstimation.fx,DLSS5_Feed@DLSS5_Feed.fx
- Tecla liga/desliga (`InstallOptions.TeclaLigaDesliga`, F6 por padrão): o preset ganha `KeyDLSS5_Feed@DLSS5_Feed.fx=tecla,0,0,0` (a alternância de technique do ReShade, lida na raiz do preset). O addon só trabalha depois de a technique rodar, então a tecla desliga o DLSS 5 inteiro — comparação antes/depois sem abrir o painel. O ShortFuse não tem tecla; o F6 do Krish não chega ao host64 em 32-bit.
```

### 12.7 Patch do dgVoodoo.conf

```powershell
$ajustes = @{ DisableAndPassThru='false'; VRAM='1024'; VideoCard='internal3D';
              dgVoodooWatermark='true'; OutputAPI='d3d11_fl11_0' }
$txt = Get-Content $conf -Raw
foreach ($k in $ajustes.Keys) {
  $txt = [regex]::Replace($txt, "(?m)^(\s*$k\s*=\s*).*$", "`${1}$($ajustes[$k])") }
Set-Content $conf $txt -Encoding ASCII
```

### 12.8 Verificação de assinatura Authenticode

```powershell
Get-AuthenticodeSignature $dll | Format-List Status, SignerCertificate
(Get-Item $dll).VersionInfo | Format-List FileVersion, CompanyName, ProductName
Get-FileHash $dll -Algorithm SHA256
```

---

## 13. Reversão

**Jogo:** apagar os arquivos adicionados (manifesto). Verificar integridade na Steam **depois** (ela não remove arquivos estranhos, só repõe os originais). Desinstalador do ReShade remove `dxgi.dll`, `ReShade.ini`, `reshade-shaders\`.

**Sistema:** override de assinatura (12.1, bloco "reverter") + reinício. Overlays religados na UI.

**Source:** remover `-dxlevel 95` das opções de inicialização.

---

## 14. Chaves úteis do `dlss5-feed.cfg` (Feeder 0.15.1)

O kit traz o **dlss5-feed 0.15.1** (desde 09/09; 0.13.1-beta.1 de 04/09 a 09/09; antes 0.12.0, guardado em `versoes-anteriores/feeder-0.12.0/`) (`DLSS 5 Files/feeder-versao.txt` registra a release e os
hashes; `feeder-desejado.txt` é o que se muda para trocar). Até 02/09 o kit trazia o 0.5.0,
que derrubava a sessão inteira quando o jogo recriava a swapchain — trocar resolução, tela
cheia ou qualidade dentro do jogo — e criava a feature de novo bem quando o addon do RenoDX
rearma os hooks: Mafia DE, Crysis, Titanfall 2 e Metro Exodus caíam. O 0.12.0 mantém texturas
e feature vivas na recriação do runtime, só recria a feature (segurada pelo `create_delay`),
tenta até três vezes e fica com a anterior se falhar.

O `DLSS5_Feed.fx` 0.12.0 escolhe o provedor de MV por `DLSS5_MV_PROVIDER`, definição de
pré-processador **por efeito** — na seção `[DLSS5_Feed.fx]` do `ReShadePreset.ini`, não no
`[GENERAL]` do `ReShade.ini`. O instalador grava `2` (VORT, padrão), `1` (Launchpad), `3` (LumeniteFX Kernel) ou `0`
(DRME/texMotionVectors); o checkpoint 13 confere.

| Chave | Padrão | Uso |
|---|---|---|
| `enabled` | 1 | 0 desliga |
| `mode` | 2 | 1 = teste de transporte sem NGX (isola Feeder de addon) |
| `work_resolution` | 100 | **só D3D11**: 50–100% de cada eixo do backbuffer para as texturas de trabalho (custo/VRAM; a saída continua nativa). A barra da verificação grava esta chave |
| `work_upscale` | 0 | D3D11: como o resultado volta ao tamanho nativo — 0 bilinear, 1 FSR 1 (mais nítido a 50–75%) |
| `hdr` | -1 | auto; 0 força SDR para teste |
| `preset` | 0 | 5/6 CNN E/F (transparências); 10/11 transformer J/K |
| `create_delay` | 60 | não baixar — hooks assíncronos, chamar cedo trava |
| `warmup_rebuild` | 180 | recria feature uma vez (contorna STANDBY); pulado nos addons "v45+" |
| `gpu_timeout_ms` | 2000 | quanto um frame espera a GPU; três seguidos estourados param o feed |
| `mv_scale_x/y` | 1.0 | multiplicador extra |
| `host_window` | 0 | jogos 32-bit: 0 esconde a janela do auxiliar (o painel é projetado no jogo); 1 dá janela própria |
| `async_home` | 1 | 32-bit: handoff em pipeline (tira o teto de ~35 fps); 0 = mesmo frame |
| `enabled` | 1 | 0.13.0+: 0 desliga tudo de verdade (antes só parava o trabalho neural) |
| `hdr_bridge` | -1 | 0.15.1: em swapchain HDR10 (R10G10B10A2 PQ) decodifica para linear FP16 antes do consumidor e volta a PQ depois (os brilhos estouravam); 0 desliga, 1 força |

---

*Fontes: repositório DLSS5-Feeder (jlrouzies-fr), RenoDX, dgVoodoo2 (dege), ReShade (crosire), e os logs e verificações desta sessão.*
