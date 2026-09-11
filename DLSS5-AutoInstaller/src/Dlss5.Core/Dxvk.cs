namespace Dlss5.Core;

/// <summary>
/// DXVK do próprio jogo. O Black Mesa (11/09/2026) traz o DXVK em bin\thirdparty\dxvk-windows-x86\
/// e a Steam oferece duas entradas: "Play Default" (DXVK: Direct3D 9 traduzido para Vulkan) e
/// "Play Direct3D 9 Fallback". No modo padrão o Direct3D 9 do jogo é o DXVK, o dgVoodoo fica
/// fora (ou pior: carrega junto, como no log), o feed 32-bit não tem caminho (Vulkan) e a
/// engine Source mostra "failed to lock vertex buffer in CMeshDX8::LockVertexBuffer" — o mesmo
/// erro que o Black Mesa dá no Linux/Proton, onde o D3D9 também é o DXVK.
///
/// Prova de que o DXVK rodou: ele grava &lt;exe&gt;_d3d9.log (ou _dxgi.log / _d3d11.log) ao lado do
/// exe, começando por "info:  Game: ..." e "info:  DXVK: v...".
/// </summary>
public static class Dxvk
{
    /// <summary>Pasta relativa ao renderizador onde o Black Mesa guarda o DXVK dele.</summary>
    public static readonly string PastaEmbutidaSource = Path.Combine("thirdparty", "dxvk-windows-x86");

    /// <summary>O jogo traz o próprio DXVK (Source: bin\thirdparty\dxvk-windows-x86\d3d9.dll).</summary>
    public static bool EmbutidoNaSource(string? rendererFolder)
    {
        try
        {
            return rendererFolder is not null
                && File.Exists(Path.Combine(rendererFolder, PastaEmbutidaSource, "d3d9.dll"));
        }
        catch { return false; }
    }

    /// <summary>Caminhos de log que o DXVK grava ao lado do exe, para este exe.</summary>
    public static IEnumerable<string> LogsPossiveis(string exePath)
    {
        var pasta = Path.GetDirectoryName(exePath) ?? "";
        var nome = Path.GetFileNameWithoutExtension(exePath);
        foreach (var api in new[] { "d3d9", "dxgi", "d3d11", "d3d10", "d3d8" })
            yield return Path.Combine(pasta, $"{nome}_{api}.log");
    }

    /// <summary>
    /// O DXVK esteve ativo neste jogo: existe um &lt;exe&gt;_&lt;api&gt;.log que se anuncia como DXVK.
    /// Devolve o caminho do log e a versão ("v2.6.2"), ou null.
    /// </summary>
    public static (string Log, string Versao)? Ativo(string? exePath)
    {
        if (exePath is null) return null;
        foreach (var log in LogsPossiveis(exePath))
        {
            try
            {
                if (!File.Exists(log)) continue;
                using var sr = new StreamReader(log);
                string? linha; int n = 0; string versao = "?";
                bool ehDxvk = false;
                while ((linha = sr.ReadLine()) is not null && n++ < 20)
                {
                    var t = linha.Trim();
                    if (t.StartsWith("info:", StringComparison.OrdinalIgnoreCase) && t.Contains("DXVK:", StringComparison.OrdinalIgnoreCase))
                    {
                        ehDxvk = true;
                        int i = t.IndexOf("DXVK:", StringComparison.OrdinalIgnoreCase);
                        versao = t[(i + 5)..].Trim();
                    }
                }
                if (ehDxvk) return (log, versao);
            }
            catch { }
        }
        return null;
    }
}
