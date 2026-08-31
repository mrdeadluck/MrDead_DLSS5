<#
.SINOPSE
    Monta uma pasta enxuta para compartilhar: o executável + só as peças do kit
    que o instalador realmente usa, com um LEIA-ME para quem receber.

.DESCRIÇÃO
    O kit completo tem mais de 1 GB, quase tudo duplicado: os .zip repetem o que
    já está extraído, e o zip do Streamline nem é usado (aquelas DLLs estão na
    lista do que NÃO pode ir para a pasta do jogo). O que o instalador precisa
    dá cerca de 225 MB.

.EXEMPLO
    .\Montar-Pacote.ps1
    .\Montar-Pacote.ps1 -Kit "C:\...\DLSS 5 Files" -Saida "$HOME\Desktop\DLSS5-Pacote" -Zip
#>
[CmdletBinding()]
param(
    # Pasta do kit (a "DLSS 5 Files"). Se omitida, tenta achar sozinha.
    [string]$Kit,
    # Onde montar o pacote.
    [string]$Saida = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'DLSS5-Pacote'),
    # Compacta a pasta no fim.
    [switch]$Zip,
    # Não baixar o executável da internet (usa só uma cópia local, se houver).
    [switch]$SemBaixar
)

$ErrorActionPreference = 'Stop'
$UrlExe = 'https://github.com/mrdeadluck/MrDead_DLSS5/releases/download/installer-latest/DLSS5-AutoInstaller.exe'

function Escreve($texto, $cor = 'Gray') { Write-Host $texto -ForegroundColor $cor }

# ---------------------------------------------------------------- kit de origem
if (-not $Kit) {
    $palpites = @(
        (Join-Path $PSScriptRoot '..\DLSS 5 Files'),
        (Join-Path $HOME 'Downloads\MrDead_DLSS5\DLSS 5 Files'),
        (Join-Path $HOME 'Downloads\DLSS 5 Files')
    )
    $Kit = $palpites | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Kit -or -not (Test-Path $Kit)) {
    throw "Não achei a pasta do kit. Rode de novo com: -Kit ""C:\caminho\DLSS 5 Files"""
}
$Kit = (Resolve-Path $Kit).Path
Escreve "Kit de origem: $Kit" 'Cyan'

# Índice de todos os arquivos, para achar cada peça mesmo com a pasta bagunçada.
$todos = Get-ChildItem -LiteralPath $Kit -Recurse -File -ErrorAction SilentlyContinue

function AchaArquivo([string]$nome, [string]$preferePasta) {
    $cands = $todos | Where-Object { $_.Name -ieq $nome }
    if (-not $cands) { return $null }
    if ($preferePasta) {
        $melhor = $cands | Where-Object { $_.FullName -ilike "*$preferePasta*" } | Select-Object -First 1
        if ($melhor) { return $melhor }
    }
    # Sem preferência: o maior costuma ser o arquivo de verdade, não um stub.
    return $cands | Sort-Object Length -Descending | Select-Object -First 1
}

# ---------------------------------------------------------------- destino
$destKit = Join-Path $Saida 'Kit'
if (Test-Path $Saida) {
    Escreve "A pasta $Saida ja existe e sera substituida." 'Yellow'
    Remove-Item -LiteralPath $Saida -Recurse -Force
}
New-Item -ItemType Directory -Path $destKit -Force | Out-Null

# Peças soltas: nome no kit -> nome no pacote, com dica de pasta quando ambíguo.
$pecas = @(
    @{ Nome = 'nvngx_dlssnr.dll';              Pref = $null },
    @{ Nome = 'nvngx_dlss.dll';                Pref = $null },
    @{ Nome = 'renodx-dlss5.addon64';          Pref = $null },
    @{ Nome = 'dlss5-feed.addon64';            Pref = $null },
    @{ Nome = 'dlss5-feed.addon32';            Pref = $null },
    @{ Nome = 'dlss5-feed-host64.exe';         Pref = $null },
    @{ Nome = 'dxgi.dll';                      Pref = $null },
    @{ Nome = 'ReShade_Setup_6.8.0_Addon.exe'; Pref = $null }
)

$faltando = @()
foreach ($p in $pecas) {
    $achado = AchaArquivo $p.Nome $p.Pref
    if ($achado) {
        Copy-Item -LiteralPath $achado.FullName -Destination (Join-Path $destKit $p.Nome) -Force
        Escreve ("  + {0,-34} {1,8:N1} MB" -f $p.Nome, ($achado.Length / 1MB)) 'Green'
    } else {
        $faltando += $p.Nome
        Escreve ("  ! {0,-34} NAO ENCONTRADO" -f $p.Nome) 'Red'
    }
}

# dgVoodoo2: o D3D9.dll tem que ser o de MS\x86 (existem versoes arm e x64 no zip).
$d3d9 = AchaArquivo 'D3D9.dll' '\MS\x86\'
if ($d3d9) {
    $dgv = Join-Path $destKit 'dgVoodoo\MS\x86'
    New-Item -ItemType Directory -Path $dgv -Force | Out-Null
    Copy-Item -LiteralPath $d3d9.FullName -Destination (Join-Path $dgv 'D3D9.dll') -Force
    foreach ($extra in @('dgVoodoo.conf', 'dgVoodooCpl.exe')) {
        $e = AchaArquivo $extra $null
        if ($e) { Copy-Item -LiteralPath $e.FullName -Destination (Join-Path $destKit "dgVoodoo\$extra") -Force }
    }
    Escreve "  + dgVoodoo2 (D3D9.dll de MS\x86 + conf + cpl)" 'Green'
} else {
    $faltando += 'D3D9.dll (dgVoodoo2, de MS\x86)'
    Escreve "  ! dgVoodoo2 NAO ENCONTRADO (jogos D3D9 nao vao funcionar)" 'Red'
}

# reshade-shaders inteira (shaders + texturas do Launchpad).
$shaders = Get-ChildItem -LiteralPath $Kit -Recurse -Directory -Filter 'reshade-shaders' -ErrorAction SilentlyContinue |
    Sort-Object { (Get-ChildItem $_.FullName -Recurse -File | Measure-Object).Count } -Descending |
    Select-Object -First 1
if ($shaders) {
    Copy-Item -LiteralPath $shaders.FullName -Destination (Join-Path $destKit 'reshade-shaders') -Recurse -Force
    Escreve "  + reshade-shaders" 'Green'
} else {
    $faltando += 'pasta reshade-shaders'
    Escreve "  ! reshade-shaders NAO ENCONTRADA" 'Red'
}

# ---------------------------------------------------------------- executável
$destExe = Join-Path $Saida 'DLSS5-AutoInstaller.exe'
$exeLocal = AchaArquivo 'DLSS5-AutoInstaller.exe' $null
if ($exeLocal) {
    Copy-Item -LiteralPath $exeLocal.FullName -Destination $destExe -Force
    Escreve "  + DLSS5-AutoInstaller.exe (copia local)" 'Green'
} elseif (-not $SemBaixar) {
    Escreve "Baixando o DLSS5-AutoInstaller.exe..." 'Cyan'
    try {
        Invoke-WebRequest -Uri $UrlExe -OutFile $destExe -UseBasicParsing
        Escreve ("  + DLSS5-AutoInstaller.exe {0,8:N1} MB" -f ((Get-Item $destExe).Length / 1MB)) 'Green'
    } catch {
        $faltando += 'DLSS5-AutoInstaller.exe (falha no download)'
        Escreve "  ! Nao consegui baixar. Pegue em: $UrlExe" 'Red'
    }
} else {
    $faltando += 'DLSS5-AutoInstaller.exe'
}

# ---------------------------------------------------------------- LEIA-ME
$leiaMe = @"
DLSS 5 Neural Rendering - pacote pronto
=======================================

O QUE E ISSO
   Aplica o passo neural do DLSS 5 em jogos que nao tem suporte nativo.
   O programa faz quase tudo sozinho: voce aponta as duas pastas e ele
   descobre o executavel real do jogo, a arquitetura, a API grafica, copia
   cada arquivo para o lugar certo e ja deixa o ReShade configurado.

ANTES DE COMECAR - confira se seu PC atende
   - Placa NVIDIA RTX (testado em RTX 40 series / Ada)
   - Driver NVIDIA 616.56 ou mais novo
   - Windows 64-bit

COMO USAR
   1. Execute DLSS5-AutoInstaller.exe
      O Windows vai avisar que o app e desconhecido (ele nao tem assinatura
      digital paga): clique em "Mais informacoes" e depois "Executar assim
      mesmo". Ele pede permissao de administrador - precisa, porque grava no
      registro e em pastas dentro de Arquivos de Programas.

   2. Pasta do kit  -> a pasta "Kit" que veio aqui dentro
      Pasta do jogo -> onde o jogo esta instalado

   3. Detectar > Gerar plano > Instalar > Verificar

   4. REINICIE O WINDOWS depois da primeira instalacao.
      O programa grava uma chave no registro que libera a assinatura do
      modulo neural, e o driver da NVIDIA so le essa chave ao ligar o PC.
      Sem reiniciar, o DLSS 5 falha com erro 0xBAD00007 - mesmo com todo o
      resto certo. E o motivo numero 1 de "nao funcionou".

   5. Abra o jogo e aperte Home para ver o painel do ReShade.
      A tela de verificacao do programa le os logs e diz exatamente em que
      ponto parou, se parar.

O QUE ESPERAR - leia, evita frustracao
   - E DLAA, nao upscaling: a resolucao de renderizacao continua igual a de
     saida. A imagem melhora, mas NAO existe ganho de FPS. Na verdade custa
     desempenho.
   - A interface do jogo (HUD) e processada junto com a cena.
   - Em movimento rapido pode aparecer fantasma/rastro, porque os vetores de
     movimento sao estimados por shader, nao vem do jogo.
   - Consumo de VRAM e alto. Se o jogo travar, teste primeiro em 1080p e em
     modo janela.

AVISOS SERIOS
   - NAO use em jogos online com anti-cheat (EAC, BattlEye, Vanguard).
     A liberacao de assinatura vale para o sistema todo e pode ser
     interpretada como violacao de integridade.
   - Desligue as sobreposicoes (Steam e NVIDIA App) do jogo que for usar:
     elas disputam o mesmo ponto de interceptacao e podem impedir o
     funcionamento.
   - Desligue MSAA/SSAA nas opcoes graficas do jogo. FXAA e SMAA podem ficar.

DEU ERRADO?
   O botao "Desinstalar (reverter)" na ultima tela desfaz tudo o que foi
   colocado no jogo, inclusive restaurando arquivos que tenham sido movidos.

Projeto e codigo: https://github.com/mrdeadluck/MrDead_DLSS5
"@
Set-Content -LiteralPath (Join-Path $Saida 'LEIA-ME.txt') -Value $leiaMe -Encoding UTF8

# ---------------------------------------------------------------- resumo
$tamanho = (Get-ChildItem -LiteralPath $Saida -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ''
Escreve ("Pacote montado: {0}  ({1:N1} MB)" -f $Saida, $tamanho) 'Cyan'

if ($faltando.Count -gt 0) {
    Escreve "Faltou:" 'Yellow'
    $faltando | ForEach-Object { Escreve "   - $_" 'Yellow' }
    Escreve "Confira se apontou a pasta certa do kit." 'Yellow'
}

if ($Zip) {
    $zipPath = "$Saida.zip"
    if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $Saida '*') -DestinationPath $zipPath
    Escreve ("Zip: {0}  ({1:N1} MB)" -f $zipPath, ((Get-Item $zipPath).Length / 1MB)) 'Cyan'
}
