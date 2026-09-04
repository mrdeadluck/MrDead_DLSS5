Prologo - Caso vc vai instalar na pasta de um jogo que vc já havia instalado) Limpar arquivos instalados na raiz da pasta:



Abri Powershell como administrador:



$jogo = "E:\\SteamLibrary\\steamapps\\common\\MGS\_PW\\mgspw cOLOCAR CAMINHO DO JOGO" 

Set-Location $jogo



\# ReShade

Remove-Item dxgi.dll,d3d11.dll,d3d12.dll,opengl32.dll,ReShade.ini,ReShade.log,

&#x20;           ReShadePreset.ini,ReShade64.json,ReShade32.json -ErrorAction SilentlyContinue

Remove-Item reshade-shaders,host64 -Recurse -Force -ErrorAction SilentlyContinue



\# Feeder e RenoDX

Remove-Item dlss5-feed.addon64,dlss5-feed.addon32,dlss5-feed.cfg,dlss5-feed.log,

&#x20;           renodx-dlss5.addon64 -ErrorAction SilentlyContinue



\# DLLs NVIDIA e Streamline

Remove-Item nvngx\_dlssnr.dll,nvngx\_dlss.dll,nvngx\_dlssg.dll -ErrorAction SilentlyContinue

Remove-Item sl.common.dll,sl.dlss.dll,sl.dlss\_g.dll,sl.dlss\_nr.dll,

&#x20;           sl.interposer.dll,sl.nis.dll,sl.pcl.dll,sl.reflex.dll -ErrorAction SilentlyContinue

Remove-Item nis.license.txt,nvngx\_dlss.license.txt,reflex.license.txt -ErrorAction SilentlyContinue

Remove-Item ReShade\_Setup\_6.8.0\_Addon.exe -ErrorAction SilentlyContinue





Passo 1 - Instalar o Reshade ReShade\_Setup\_6.8.0\_Addon no executável do jogo;

Passo 2 - Colocar a pasta reshade-shaders na pasta do executável do jogo; (Mesmo caminho onde vc instalou o reshade, )

Passo 3 - Copiar os arquivos para a pasta do executável do jogo
 dlss5-feed.addon64, renodx-dlss5.addon64, nvngx\_dlss.dll e nvngx\_dlssnr.dll.


Passo 4 - Abri o jogo, apertar tecla HOME;
Abrir a aba INICIO do resdhade;
Home → aba Início (a primeira, não a de Complementos). Na lista de efeitos:

Marque a caixa do Immerssive - MartysMods\_LAUNCHPAD

Confirme que ele está acima do DLSS 5 Feed na lista — se estiver abaixo, arraste para cima

Marque o DLSS 5 Feed;








