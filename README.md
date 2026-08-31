# MrDead_DLSS5

Repositório com os arquivos do DLSS 5 e, futuramente, o código do app.

Os arquivos grandes (150 MB+) são versionados com [Git LFS](https://git-lfs.com).
O `.gitattributes` já está configurado: **qualquer arquivo dentro da pasta
`DLSS 5 Files/` vai automaticamente para o LFS**, além de `*.dll`, `*.bin` e
`*.onnx` em qualquer lugar do repositório.

## Como subir os arquivos (no seu PC)

### Opção 1 — GitHub Desktop (mais fácil)

1. Instale o [GitHub Desktop](https://desktop.github.com) — ele já vem com Git LFS.
2. **File → Clone repository** e escolha `mrdeadluck/MrDead_DLSS5`.
3. Copie a pasta `DLSS 5 Files` para dentro da pasta do repositório clonado
   (mantendo o nome da pasta).
4. Volte ao GitHub Desktop, escreva uma mensagem de commit, clique em
   **Commit** e depois em **Push origin**.

### Opção 2 — Linha de comando (Git Bash / PowerShell)

```bash
git lfs install          # uma vez por máquina (o Git for Windows já inclui o LFS)
git clone https://github.com/mrdeadluck/MrDead_DLSS5.git
cd MrDead_DLSS5
# copie a pasta "DLSS 5 Files" para dentro desta pasta, mantendo o nome
git add .
git commit -m "Adiciona arquivos DLSS 5"
git lfs ls-files         # confira: os arquivos grandes devem aparecer nesta lista
git push
```

## Avisos importantes

- Rode `git lfs install` (ou use o GitHub Desktop) **antes** do `git add`.
  Sem isso os arquivos entram no repositório sem LFS e o push de arquivos
  acima de 100 MB é bloqueado pelo GitHub.
- Se errar a ordem e o push for bloqueado, o jeito mais simples é apagar a
  pasta clonada e refazer os passos na ordem certa.
- Cota gratuita do GitHub LFS: **1 GB de armazenamento** e **1 GB/mês de
  download**. Se a pasta inteira passar disso, considere anexar os arquivos
  em um [Release](https://github.com/mrdeadluck/MrDead_DLSS5/releases)
  (até 2 GB por arquivo, sem gastar cota de LFS).
