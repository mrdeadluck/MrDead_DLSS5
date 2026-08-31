# assets

Imagens embutidas no executável.

## Foto do autor

Coloque aqui um arquivo chamado exatamente **`mrdead.png`**. Ele aparece
recortado em círculo no rodapé da barra lateral, ao lado de "Desenvolvido por
MrDead_".

Enquanto o arquivo não existir, o programa desenha um monograma no lugar — não
quebra nada.

**Como subir pelo site**, sem precisar de git: abra
[esta pasta no GitHub](https://github.com/mrdeadluck/MrDead_DLSS5/upload/claude/upload-large-files-02562q/DLSS5-AutoInstaller/src/Dlss5.App/assets),
arraste o PNG, renomeie para `mrdead.png` se necessário e confirme o commit. O
build automático embute a imagem e publica um executável novo.

Qualquer resolução e qualquer nome de PNG funcionam — o programa procura por
`mrdead.png` e, se não achar, usa o primeiro PNG desta pasta. Mas vale reduzir
para algo em torno de 256×256 antes de subir: a imagem é desenhada com 56px, e
o arquivo vai embutido no executável.
