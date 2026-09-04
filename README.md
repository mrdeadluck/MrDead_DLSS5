# MrDead_DLSS5

DLSS 5 Neural Rendering em jogos sem suporte nativo — os arquivos do kit, a
documentação técnica e um programa que automatiza a instalação.

---

## ⬇️ Baixar o programa

**[DLSS5-Pacote-Completo.zip](https://github.com/mrdeadluck/MrDead_DLSS5/releases/download/installer-latest/DLSS5-Pacote-Completo.zip)**
— tudo junto: o programa, o kit inteiro e o `LEIA-ME.txt`. É este que você baixa
para usar ou para mandar para alguém.

**[DLSS5-AutoInstaller.exe](https://github.com/mrdeadluck/MrDead_DLSS5/releases/download/installer-latest/DLSS5-AutoInstaller.exe)**
— só o programa, quando você já tem o kit. Executável único, não precisa
instalar o .NET.

Esse link é fixo e **sempre aponta para o build mais recente**: cada mudança no
código gera um novo executável automaticamente e substitui o anterior. Também dá
para chegar nele pela aba **[Releases](https://github.com/mrdeadluck/MrDead_DLSS5/releases/tag/installer-latest)**
do repositório.

> O Windows SmartScreen avisa que o app não é conhecido, porque o executável não
> tem assinatura digital paga. **Mais informações → Executar assim mesmo**.
> O programa pede permissão de administrador: ele grava no registro (`HKLM`) e em
> pastas dentro de `Program Files`.

<details>
<summary>Por que o .exe fica em Releases e não commitado junto do código</summary>

Ele tem ~63 MB e é regerado a cada mudança. As regras de LFS deste repositório
mandam `*.exe` para o Git LFS, então cada versão comeria 63 MB da cota gratuita
(1 GB) **para sempre** — armazenamento de LFS não é liberado ao apagar o arquivo.
Em Releases o download é ilimitado e não consome cota nenhuma.

</details>

---

## O que tem aqui

| Pasta | Conteúdo |
|---|---|
| [`DLSS 5 Files/`](DLSS%205%20Files) | O kit (~235 MB), já sem duplicatas: as DLLs `nvngx_*`, o addon do RenoDX (4.70), o Feeder (0.13.1-beta.1), o ReShade, o dgVoodoo2 (2.87.4) e a pasta `reshade-shaders` (com o VORT Motion). Versões antigas em `versoes-anteriores/`, alternativas (renodx-dlss do ShortFuse, AIO do kibblerz, MFG Unlock) em pastas próprias. Versão de cada peça, como os binários entram (workflows) e o que baixar do Discord: [`VERSOES.md`](DLSS%205%20Files/VERSOES.md). É esta pasta que você aponta no programa. |
| [`DLSS5-AutoInstaller/`](DLSS5-AutoInstaller) | Código-fonte do programa (.NET 8 / WinForms) e [seu README](DLSS5-AutoInstaller/README.md) com o passo a passo de uso. |
| [`docs/`](docs) | A especificação técnica: como o DLSS 5 funciona nesse contexto, matriz de suporte, os três caminhos de instalação, checkpoints e diagnóstico por sintoma. |

### Baixar só o kit

O kit está versionado com [Git LFS](https://git-lfs.com). Para trazer a pasta de
volta em outra máquina:

```bash
git lfs install          # uma vez por máquina (o Git for Windows já inclui)
git clone https://github.com/mrdeadluck/MrDead_DLSS5.git
```

Baixar arquivo por arquivo pelo site também funciona (o botão **Download** de
cada arquivo entrega o conteúdo real, não o ponteiro do LFS), mas para a pasta
inteira o clone é bem mais prático.

---

## Compartilhar com alguém

Mande o link do **DLSS5-Pacote-Completo.zip** lá de cima. A pessoa baixa um
arquivo só, extrai e roda — o `LEIA-ME.txt` com as instruções vem dentro.

Download de Release não consome cota de LFS, então pode compartilhar à vontade.
O que **não** convém é pedir para clonarem o repositório: aí sim o download dos
arquivos grandes sai da cota mensal de quem é dono (1 GB/mês).

### Gerar uma versão nova do pacote

O zip completo não é refeito a cada mudança de código — ele precisa baixar o kit
do LFS, e isso consome cota. Quando quiser atualizar:

1. Aba **[Actions](https://github.com/mrdeadluck/MrDead_DLSS5/actions/workflows/pacote-completo.yml)**
2. **Run workflow** → **Run workflow**
3. Em uns 5 minutos o zip novo substitui o antigo na Release, no mesmo link

(O `DLSS5-AutoInstaller.exe` sozinho continua sendo republicado a cada push,
esse é automático.)

---

## Como usar, em resumo

1. Baixe o `DLSS5-AutoInstaller.exe` acima e execute (um arquivo só, sem instalar nada).
2. **Pasta do jogo** → onde o jogo está instalado. O programa mostra o estado (não
   instalado, instalado, incompleto, vestígios de versão antiga…) e só as ações válidas.
3. Para instalar: **Pasta do kit** → a pasta `DLSS 5 Files` → **Instalar DLSS 5** →
   confira a detecção e o plano → acompanhe a execução → verificação.
4. Para desinstalar: abra o programa, aponte o jogo, **Desinstalar e restaurar arquivos
   originais**. Não precisa do kit nem de reinstalar.

O programa descobre sozinho o executável real (incluindo o binário de verdade em
jogos Unreal e o stub da engine Source), a arquitetura, a API gráfica e a rota de
instalação; copia cada peça para o lugar certo; gera as configurações do ReShade
já com os efeitos marcados na ordem correta; e no fim verifica o que dá para
verificar por arquivo, guiando você no que sobra de manual.

Detalhes completos em [`DLSS5-AutoInstaller/README.md`](DLSS5-AutoInstaller/README.md).

---

## Avisos

- **Reinicie o Windows** depois de aplicar o override de assinatura do NGX: o
  driver da NVIDIA só lê essa chave na inicialização. Sem isso o DLSS 5 falha
  com `0xBAD00007`, por mais correto que esteja o resto.
- O override é **global no sistema**. Anti-cheat (EAC, BattlEye) pode tratar
  isso como violação de integridade — não use em jogos online com anti-cheat.
- É **DLAA**: resolução de render igual à de saída. Não existe ganho de
  performance, só de imagem.
- Cota gratuita do GitHub LFS: 1 GB de armazenamento e 1 GB/mês de download.
