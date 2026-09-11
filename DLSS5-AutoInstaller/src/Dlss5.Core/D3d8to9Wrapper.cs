namespace Dlss5.Core;

/// <summary>
/// Um D3D8.dll na pasta do jogo que NÃO é o dgVoodoo mas converte DirectX 8 em DirectX 9
/// (d3d8to9) e, na hora de criar o Direct3D 9, prefere um d3d9.dll LOCAL ao do Windows.
///
/// O caso que motivou: Silent Hill 2 Enhanced Edition. O d3d8.dll da pasta é o módulo
/// "Silent Hill 2 Enhancements" — é a própria mod (60 fps, widescreen, texturas), não um
/// wrapper sobrando — e com `d3d8to9 = 1` (padrão, exigido pelos shaders dela) o
/// Direct3DCreate9Wrapper dela tenta, nesta ordem: 9On12 (se ligado), o d3d9.dll da
/// PRÓPRIA PASTA (GetLocalDirect3DCreate9) e só então o do System32. O crosire/d3d8to9
/// avulso faz LoadLibrary("d3d9.dll"), que também acha primeiro o da pasta do exe.
///
/// Logo o dgVoodoo não precisa (nem pode) ser o D3D8.dll: entra como D3D9.dll ao lado, a
/// mod continua inteira, e o dgVoodoo traduz o D3D9 que ela produz para D3D11 — onde o
/// ReShade (dxgi.dll) e o Feeder entram como em qualquer rota C. Sobrescrever o D3D8.dll
/// (o que a instalação fazia antes de existir a checagem de ocupante) tirava a mod do
/// caminho; recusar (o que a checagem fazia) deixava o jogo sem DLSS 5.
/// </summary>
public static class D3d8to9Wrapper
{
    public const string Arquivo = "D3D8.dll";

    /// <summary>Marcador do SH2 Enhancements (string de log do módulo) e do d3d8to9 genérico.</summary>
    public const string MarcaSh2 = "Silent Hill 2 Enhancements";
    public const string MarcaD3d8to9 = "d3d8to9";
    private static readonly string[] Marcadores = { "dgVoodoo", MarcaSh2, MarcaD3d8to9 };
    private const long Orcamento = 32L * 1024 * 1024;

    /// <summary>Qual marcador identifica o D3D8.dll da pasta (null = não há, ou é o dgVoodoo, ou é outro wrapper).</summary>
    public static string? Qual(string pasta)
    {
        var caminho = Path.Combine(pasta, Arquivo);
        if (!File.Exists(caminho)) return null;
        HashSet<string> marcas;
        try { marcas = ApiDetector.ScanForMarkers(caminho, Marcadores, Orcamento); }
        catch { return null; }
        if (marcas.Contains("dgVoodoo")) return null;
        if (marcas.Contains(MarcaSh2)) return MarcaSh2;
        if (marcas.Contains(MarcaD3d8to9)) return MarcaD3d8to9;
        return null;
    }

    public static bool Presente(string pasta) => Qual(pasta) is not null;

    public static string Descrever(string marca) => marca == MarcaSh2
        ? "o módulo do Silent Hill 2 Enhanced Edition (a própria mod: 60 fps, widescreen, texturas)"
        : "um wrapper com d3d8to9 (converte DirectX 8 em DirectX 9)";
}
