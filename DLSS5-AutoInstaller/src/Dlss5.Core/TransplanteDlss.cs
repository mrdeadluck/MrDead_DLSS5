namespace Dlss5.Core;

/// <summary>
/// Reconhece o "transplante": um nvngx_dlss.dll na pasta do jogo que é, byte a byte,
/// o arquivo do KIT — copiado por cima (ou no lugar) do DLL do jogo por uma versão
/// antiga deste instalador.
///
/// A prova é absoluta nos dois sentidos. Bytes idênticos aos do kit não podem ter
/// vindo com o jogo: cada jogo traz a versão de DLSS casada com o resto dos arquivos
/// dele. E bytes diferentes não autorizam nada — pode ser o DLL genuíno do jogo, e
/// nele o programa não encosta. É essa prova que devolve à desinstalação o direito
/// de remover o arquivo errado (e só ele): sem ela, o transplante de uma instalação
/// antiga ficava para sempre na pasta, travando jogo que carrega o DLL na
/// inicialização — o demo do Onimusha nem abria.
/// </summary>
public static class TransplanteDlss
{
    /// <summary>O arquivo do jogo é byte a byte o nvngx_dlss.dll do kit?</summary>
    public static bool EhDoKit(string? arquivoNoJogo, string? arquivoDoKit)
    {
        if (string.IsNullOrWhiteSpace(arquivoNoJogo) || string.IsNullOrWhiteSpace(arquivoDoKit))
            return false;
        if (string.Equals(Path.GetFullPath(arquivoNoJogo), Path.GetFullPath(arquivoDoKit),
                StringComparison.OrdinalIgnoreCase))
            return false; // o próprio arquivo do kit não é um transplante
        try
        {
            if (!File.Exists(arquivoNoJogo) || !File.Exists(arquivoDoKit)) return false;
            return MesmosBytes(arquivoNoJogo, arquivoDoKit);
        }
        catch
        {
            // Sem conseguir ler um dos lados não há prova — e sem prova não se apaga.
            return false;
        }
    }

    private static bool MesmosBytes(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;

        using var sa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var bufA = new byte[128 * 1024];
        var bufB = new byte[128 * 1024];
        while (true)
        {
            int lidoA = LerCheio(sa, bufA);
            int lidoB = LerCheio(sb, bufB);
            if (lidoA != lidoB) return false;
            if (lidoA == 0) return true;
            if (!bufA.AsSpan(0, lidoA).SequenceEqual(bufB.AsSpan(0, lidoB))) return false;
        }
    }

    private static int LerCheio(Stream s, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int lido = s.Read(buffer, total, buffer.Length - total);
            if (lido == 0) break;
            total += lido;
        }
        return total;
    }
}
