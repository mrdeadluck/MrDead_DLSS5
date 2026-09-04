# DLSS 5 AutoInstaller — diagnóstico do sistema original e reengenharia

Documento gerado durante a revisão completa do aplicativo (`DLSS5-AutoInstaller/`).
A primeira parte registra o que o código **fazia de fato** antes das mudanças; a
segunda registra o que foi implementado, como validar e o que ficou pendente.

---

## Parte 1 — Diagnóstico do sistema original

### 1.1 Arquitetura

| Camada | Projeto | Conteúdo |
|---|---|---|
| Interface | `src/Dlss5.App` (.NET 8, **WinForms**, single-file, `requireAdministrator`) | `MainForm.cs` (1.686 linhas, monólito: layout, eventos, regras de navegação, chamadas ao motor), `Ui.cs` (paleta/fábricas), `Program.cs` |
| Regras | `src/Dlss5.Core` (biblioteca sem UI) | Detecção (`GameDetector`, `ApiDetector`, `NativeDlssDetector`, `PeFile`), kit (`KitResolver`), plano (`InstallPlanBuilder`), execução/reversão (`InstallerEngine`, `InstallerEngine.Faxina`), configs (`ReShadeConfigWriter`, `DgVoodooConfigurator`, `SignatureOverride`), verificação (`CheckpointVerifier`, `SymptomDiagnoser`, `RenodxLog`), utilitários (`Isolamento`, `Overlays`, `SteamGame`, `AppSettings`) |
| Testes | `tests/Dlss5.Core.Tests` (xunit, 121 testes) | Só cobrem `Dlss5.Core` |

Fluxo da interface original: **assistente linear de 5 passos** — Pastas → Detecção →
Plano → Instalação → Verificação. O botão "Avançar" executa a etapa seguinte.

### 1.2 Como cada funcionalidade funcionava (confirmado no código)

**Localização do jogo.** Não há descoberta automática de bibliotecas; o usuário aponta a
pasta (`_txtGame`). `GameDetector.Detect` varre todos os `.exe`, pontua por profundidade,
tamanho, nome (launcher/redist penalizados, `-Shipping`/`Binaries\Win64` bonificados) e
escolhe o de maior pontuação como executável real. Arquitetura vem do cabeçalho PE.

**Compatibilidade.** `ApiDetector` soma pistas (imports do PE, strings no binário, DLLs
vizinhas) e `GameProfile.Route` deriva A/B/C/Unsupported. `InstallPlanBuilder` bloqueia
quando a rota é `Unsupported` ou faltam peças do kit.

**Detecção de mod já instalado.** Não existia como estado. `InstallManifest.Find` só era
consultado dentro de `NativeDlssDetector` (para desempatar o `nvngx_dlss.dll`) e em
`RevertInstall` (passo 5). A tela inicial não sabia se o mod estava instalado.

**Arquivos alterados pela instalação** (`InstallerEngine.Execute`):
- copiados: `dxgi.dll`/`opengl32.dll` (ReShade), `renodx-dlss5.addon64`, `nvngx_dlssnr.dll`,
  `dlss5-feed.addon64|32`, `nvngx_dlss.dll` (só se não existir), pasta `reshade-shaders\`,
  em 32-bit a pasta `host64\` (host exe, dxgi x64, renodx, dlssnr, dlss), em rota C
  `D3D8.dll`/`D3D9.dll`, `dgVoodooCpl.exe`;
- gerados: `ReShade.ini`, `ReShadePreset.ini`, `dgVoodoo.conf` (patch);
- removidos com backup (`.dlss5bak`): `ReShade_Setup_*.exe` deixado na pasta;
- registro: 3 chaves DWORD em `HKLM` (override de assinatura NGX).

**Preservação dos originais.** `BackupIfExists` copia `alvo → alvo.dlss5bak` **antes de
sobrescrever**, mas **apaga um `.dlss5bak` existente** e refaz a cópia a partir do arquivo
atual. Numa reinstalação o "arquivo atual" já é o do mod.

**Configurações do usuário.** `%APPDATA%\DLSS5-AutoInstaller\settings.json` (pasta do
kit, último jogo, provedor de MV, tecla do overlay). O manifesto por jogo guardava só
rota/arquitetura/API/MV — não guardava tecla do overlay, override, versão do app nem do kit.

**Instalação.** Sequencial, sem staging, sem hash, sem checagem de permissão/espaço/arquivo
em uso; o manifesto era gravado **apenas no final** (`manifest.Save` após o loop). Uma
exceção no meio abortava com `ERRO:` no log e nada era desfeito.

**Desinstalação.** Botão "Desinstalar (reverter)" só na **tela 5**, acessível apenas depois
de Detectar → Gerar plano → **Instalar** → Verificar. Não havia caminho para a tela 5 sem
passar pela instalação. A alternativa era "Desfazer tudo nesta pasta" (faxina heurística
por nome, sem manifesto, que não remove o override do registro).

**Instalação incompleta/corrompida.** Nenhum tratamento específico; a faxina heurística
era a única saída, e o usuário precisava saber que ela existia.

**Logs.** Só um `TextBox` na tela 4, perdido ao fechar. Nenhum arquivo de log, nenhuma
rotação, exceções globais viravam `MessageBox` sem registro.

**Progresso.** Instalação em `Task.Run` com log via `BeginInvoke`; detecção, faxina e
reversão rodavam na thread da UI com `Application.DoEvents()`.

### 1.3 Problemas encontrados, por severidade

**Críticos**
1. **Desinstalar exigia reinstalar.** A reversão por manifesto só existia na tela 5, que só
   é alcançada após a instalação (e exige pasta do kit válida para o passo Detectar).
2. **Backup original destruído na reinstalação.** `BackupIfExists` apaga `X.dlss5bak` e o
   recria a partir do `X` atual — que, numa segunda instalação, já é o arquivo do mod. Ao
   reverter, o "original restaurado" é o próprio mod (explica o `dxgi.dll` do ReShade que
   "não saía" no Forza). Também sobrescreve o `ReShade.ini`/`D3D9.dll` originais do usuário.
3. **Sem rollback.** Falha no meio (arquivo em uso, antivírus, disco cheio) deixava a pasta
   parcialmente modificada, sem manifesto e sem aviso do que ficou.
4. **Backup silenciosamente pulado.** `BackupIfExists` engolia exceções e a cópia seguia
   sobrescrevendo o original sem cópia de segurança.

**Altos**
5. Manifesto sem versão do app/kit, sem hashes, sem opções, gravado só no fim → impossível
   distinguir "instalado", "desatualizado", "incompleto" e "alterado".
6. Reversão apagava `AddedFiles` sem verificar propriedade (um arquivo do jogo com o mesmo
   nome, reposto por atualização, seria apagado).
7. Layout com posições absolutas (`SetBounds`, `Bounds`), larguras fixas em botões
   (`MakeButton(..., width)`), sem `AutoScaleMode` → textos cortados em 125–200 %.
8. Sem log em arquivo, sem diagnóstico exportável, sem rotação.
9. Nenhuma checagem prévia de permissão de escrita, espaço em disco ou arquivo bloqueado.
10. Vestígios sem manifesto (instalações antigas) não eram detectados na tela inicial.

**Médios**
11. Operações na thread da UI com `DoEvents` (janela "trava").
12. Botões da tela 5 sem proteção contra clique duplo/operação simultânea.
13. Override do registro nunca removido pela faxina nem pela reversão sem manifesto.
14. Escritas não atômicas (`File.WriteAllText` direto no destino).
15. Textos espalhados pelo `MainForm` (impossível revisar/traduzir).
16. `Track` usava dicionário case-sensitive para caminhos.

**Baixos**
17. Testes com caminhos `C:\...` fixos (falham fora do Windows).
18. Exceções de `AppSettings.Save` engolidas sem registro.

### 1.4 Dependências e pontos que geravam estado inconsistente

- `RunVerification` usava `InstallManifest.Load(exeFolder)` e `RevertInstall` usava
  `InstallManifest.Find(...)` — dois critérios para "existe manifesto".
- `NativeDlssDetector` dependia do manifesto anterior para não confundir o
  `nvngx_dlss.dll` do kit com o do jogo; sem manifesto, a resposta mudava entre execuções.
- `LimpezaTotal` e `Revert` podiam ambos apagar `reshade-shaders\` inteira, inclusive
  shaders adicionados pelo usuário.

---

## Parte 2 — Reengenharia (o que foi implementado)

### 2.1 Arquivos modificados / criados

| Projeto | Arquivo | O quê |
|---|---|---|
| Core | `Manifest.cs` (novo) | `InstallManifest` v2: versão do app/kit, opções, status da operação, `Files`/`Backups` com tamanho + SHA-256, gravação atômica, leitura tolerante (v1 continua lendo), `HerdarDe` (backups nunca refeitos), `OpcoesGravadas`/`PerfilGravado` |
| Core | `FileRecord.cs` (novo) | Impressão digital de arquivo e conferência (Igual/Diferente/Ausente/Ilegível) |
| Core | `Propriedade.cs` (novo) | Regras de propriedade de arquivo num lugar só (nomes exclusivos do kit, prova por conteúdo, escolta do dgVoodoo, outros injetores, classificação com/sem manifesto, modo "para instalar" mais conservador) |
| Core | `Preflight.cs` (novo) | Pasta gravável, espaço em disco, arquivos em uso (abertura exclusiva), jogo rodando, bytes necessários; lista de `Bloqueio` com título/detalhe/o que fazer |
| Core | `EstadoDoMod.cs` (novo) | Máquina de estados (`ModState`, 12 estados) + `RelatorioDeEstado` + lista de `AcaoDoMod` válidas (a interface só obedece) |
| Core | `InstallerEngine.cs` (reescrito) | Instalação idempotente: pré-checagens, classificação prévia dos existentes, manifesto gravado antes da 1ª modificação e a cada passo, staging `.dlss5tmp` + move, `.dlss5prev` para arquivos nossos, backup só de originais (nunca sobrescrito), override só se ainda não estiver, verificação final por hash, limpeza de temporários, rollback em falha/cancelamento, progresso e cancelamento. Desinstalação: bloqueios, remoção só do que confere (hash ou heurística para v1), restauração só de backup íntegro, preservação com motivo, `NaoRestaurados` explícito, restos de execução, pastas vazias, registro, conferência final, manifesto apagado só quando limpo (senão `ReversaoIncompleta`) |
| Core | `InstallerEngine.Faxina.cs` | Modo conservador (legado) devolvendo `ResultadoDaReversao`; varredura estrita (exige peça do kit por perto) para o inspetor |
| Core | `InstallPlanBuilder.cs` | `Conflitos` (arquivos existentes que não são nossos), `OutrosMods`, `InstalacaoAnterior`, `ResumoCurto`; `nvngx_dlss.dll` nosso continua rastreado na reinstalação |
| Core | `Diario.cs` (novo) | Log em arquivo (`%LOCALAPPDATA%\DLSS5-AutoInstaller\logs`), rotação (10 × 2 MB), níveis técnico/visível, cronômetro de etapas, fallback em memória |
| Core | `Diagnostico.cs` (novo) | Relatório de estado em texto e exportação em zip |
| Core | `AppInfo.cs` (novo), `KitResolver.cs` (`Fingerprint`) | Identidade do programa e do kit |
| App | `MainForm.cs` (reescrito) | Moldura responsiva (TableLayout), `AutoScaleMode.Dpi`, fluxos (`Fluxo`) e telas (`Tela`), rodapé por estado, bloqueio de operações concorrentes, fechamento protegido, F1/Alt+← |
| App | `MainForm.Inicio.cs` (novo) | Tela inicial: só a pasta do jogo é obrigatória; cartão de estado (jogo, executável, perfil, instalado, versão, data, backup, registro, próximo passo), bloqueios, avisos, botões dinâmicos; desinstalação/remoção/restauração com resumo específico |
| App | `MainForm.Deteccao.cs`, `MainForm.Plano.cs`, `MainForm.Execucao.cs`, `MainForm.Verificacao.cs` | Uma tela por arquivo; explicação de cada opção; conflitos exigem confirmação; progresso real, cancelamento seguro, "não feche"; copiar log / abrir logs / exportar diagnóstico / copiar erro; tela de resultado |
| App | `Textos.cs` (novo), `Ui.cs` (reescrito), `Dialogos.cs` (novo), `Program.cs` | Textos centralizados; fábricas responsivas (botão AutoSize + mínimo, fila com quebra, parágrafo com quebra, cartão); diálogo redimensionável com rolagem e "Copiar texto"; exceções globais no diário |
| Tests | `EngineTests.cs` (novo, 40 testes), `CoreTests.cs` | Instalação segura, idempotência, backup preservado, rollback, cancelamento, conflitos, desinstalação (hash, backup ausente/alterado, v1), reparo, 13 estados, manifesto v2, diário, preflight; testes antigos tornados independentes do separador de caminho |

### 2.2 Novo fluxo de instalação

1. Tela inicial → `EstadoDoMod.Inspecionar` (em segundo plano) → estado + ações.
2. **Instalar / Atualizar** → Detecção (pré-preenchida da inspeção; opções da instalação
   anterior quando é atualização) → **Plano** (ações, impedimentos, conflitos com
   confirmação, avisos, resumo em números) → confirmação específica.
3. `InstallerEngine.Execute`: pré-checagens (jogo aberto, permissão, espaço, arquivos em
   uso) → classificação prévia de cada alvo existente → manifesto `InstalacaoEmAndamento`
   gravado → para cada ação: igual? pula; nosso? guarda `.dlss5prev`; do jogo/terceiro?
   backup (só se não houver) → temporário → move → registro no manifesto → save.
   Registro: só grava se ainda não estiver. Verificação final por hash → limpeza →
   `Concluida`. Exceção/cancelamento → rollback inverso → manifesto anterior restaurado ou
   removido (ou `InstalacaoIncompleta` se o rollback não conseguiu tudo).
4. **Reparar** = mesmo motor com o plano reconstruído do manifesto (perfil + opções);
   arquivos iguais são pulados, faltantes/alterados regravados. Vai direto ao Plano.
5. Verificação automática ao terminar.

### 2.3 Novo fluxo de desinstalação

1. Tela inicial, estado `Instalado`/`Desatualizado`/`Incompleta`/`Inconsistente`/
   `ReversaoIncompleta` → botão **Desinstalar e restaurar arquivos originais** (não pede o
   kit, não passa por detecção nem plano).
2. Resumo: removidos (só os que conferem), restaurados (backups íntegros), não
   restauráveis (com orientação para a loja), preservados, sempre preservados; caixa
   "remover também o override do registro" com explicação de que é global.
3. `InstallerEngine.Revert`: bloqueios → `ReversaoEmAndamento` → remove o que confere
   (hash; nome/pasta para manifesto v1) → restaura só de backup íntegro (senão
   `NaoRestaurados`) → devolve proibidos → restos de execução e backups órfãos → pastas
   vazias → registro (verificado) → conferência final (excluindo restaurados/preservados)
   → manifesto apagado se limpo, senão `ReversaoIncompleta` (e a tela inicial oferece
   "Desinstalar" de novo).
4. Tela de resultado com as listas; ao voltar, a tela inicial reinspeciona e mostra
   "DLSS 5 não instalado".
5. Sem manifesto: **Remover vestígios (modo conservador)** com lista prévia e
   confirmação; **Devolver arquivos originais** quando só há `.dlss5bak`.

### 2.4 Backup, manifesto e rollback (regras)

- Backup só de arquivo preexistente que **não** é do programa (manifesto+hash ou, sem
  manifesto, heurística que exige peça do kit por perto — um `ReShade.ini` do usuário sem
  kit ao lado é original e recebe backup).
- Backup existente = original de verdade: nunca refeito, adotado se órfão.
- Arquivos nossos regravados guardam `.dlss5prev` só para o rollback desta execução.
- Manifesto sempre reflete o disco: salvo antes, a cada passo, e após rollback.
- `nvngx_dlss.dll` do jogo nunca é sobrescrito nem apagado (regra mantida).

### 2.5 Interface e responsividade

- `AutoScaleMode.Dpi` + medidas em 96 DPI → 100–200 % sem cortes.
- Zero `SetBounds`/`Bounds` absolutos; `TableLayoutPanel`/`FlowLayoutPanel` com `AutoSize`,
  colunas `Percent`, filas de botões com `WrapContents`, rótulos `AutoSize + Dock` que
  quebram linha, telas com rolagem quando necessário, mínimo da janela ajustado à tela.
- Botões `AutoSize` com largura mínima 110 px e altura 34 px; foco visível (borda 2 px);
  símbolos ✔ ✖ ⚠ ℹ além da cor; `AccessibleName` nos campos principais; tooltips só
  como complemento (a explicação está sempre no texto visível).
- Diálogos próprios (`Dialogos`) redimensionáveis com rolagem, "Copiar texto", Esc/Enter.

### 2.6 Testes executados (ambiente Linux, .NET 8.0.130)

```
dotnet build DLSS5-AutoInstaller.sln -c Release -p:EnableWindowsTargeting=true  → OK, 0 avisos CS
dotnet test tests/Dlss5.Core.Tests                                            → 161/161 aprovados
```

A compilação do projeto WinForms no Linux usa um stub local do SDK WindowsDesktop
(apenas para validar o código C#); o build oficial continua sendo o workflow do GitHub
Actions em `windows-latest`, que também publica o executável único autocontido (sem
instalar .NET), exatamente como antes.

### 2.7 O que NÃO pôde ser validado aqui (exige Windows) — roteiro manual

1. Abrir o `.exe` em 1280×720, 1366×768, 1600×900, 1920×1080, 2560×1440, 3840×2160 e
   ultrawide, com escala 100/125/150/175/200 %: nenhum botão cortado, filas de botões
   quebrando linha, cartão de estado legível, telas rolando quando a janela é baixa.
2. Maximizar/restaurar/redimensionar à mão; mover entre monitores com escalas diferentes
   (PerMonitorV2 já está no csproj).
3. Texto grande por acessibilidade (Configurações → Acessibilidade → Tamanho do texto).
4. Navegação só por teclado: Tab pela ordem (pasta → Procurar → Verificar → ações),
   Enter no campo da pasta inspeciona, Esc/Enter nos diálogos, Alt+← volta, F1 detalhes.
5. Fluxos reais num jogo de teste: instalar → reinstalar (deve pular tudo) → desinstalar
   (pasta igual à original) → instalar → fechar o programa no meio (Gerenciador de
   Tarefas) → reabrir → "Instalação incompleta" → Reparar; apagar um arquivo à mão →
   Reparar; trocar o `dxgi.dll` por outro → Inconsistente → desinstalar preserva;
   instalação feita pela versão anterior (manifesto v1) → "atualização disponível" e
   desinstalação funcionando; pasta sem manifesto com arquivos do mod → vestígios.
6. Jogo aberto durante a operação → bloqueio antes de tocar em arquivo.
7. Pasta somente leitura / sem permissão → bloqueio; pasta de logs sem permissão → aviso
   e log em memória.
8. Override do registro: aplicar, verificar checkpoint 1, desinstalar com a caixa marcada
   e conferir que as 3 chaves sumiram.

### 2.8 Riscos restantes

- A heurística de propriedade sem manifesto continua sendo heurística: um ReShade
  instalado pelo usuário **ao lado** de uma instalação antiga do kit (sem manifesto) é
  tratado como parte do mod no modo conservador (a lista é mostrada antes; nada é
  apagado sem confirmação).
- O override de assinatura é global: a caixa na desinstalação explica, mas o usuário pode
  removê-lo com outro jogo ainda dependendo dele (o próximo "Verificar" desse jogo acusa).
- `GameDetector` lê o executável inteiro (até 192 MB) na inspeção; em jogos gigantes a
  tela inicial pode levar alguns segundos com "Analisando…".
- A verificação de layout em todas as escalas não foi executada neste ambiente.

### 2.9 Recomendações para a próxima versão

- Descoberta automática de bibliotecas (Steam `libraryfolders.vdf`, Epic, GOG) com lista
  de jogos e estado de cada um.
- Manifesto central em `%LOCALAPPDATA%` listando todos os jogos com o mod, para a
  pergunta "remover o override?" saber se outro jogo ainda usa.
- Testes de interface automatizados no CI Windows (FlaUI/WinAppDriver) cobrindo as
  resoluções e escalas da seção 2.7.
- Assinatura digital do executável (SmartScreen).
