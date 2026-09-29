namespace Dlss5.Core;

/// <summary>
/// Um D3D8.dll na pasta do jogo que é um CARREGADOR, não quem desenha: ele sobe a mod de
/// verdade (outra DLL da pasta) e passa o Direct3DCreate8 adiante — para um d3d8R.dll da
/// própria pasta, se existir, ou para o d3d8.dll do System32.
///
/// O caso que motivou: Silent Hill 3 com o Silent Hill 3 PC Fix (Steam006). A desmontagem do
/// DllMain do d3d8.dll do fix: pega o próprio caminho (GetModuleFileNameW), testa se existe
/// "&lt;pasta&gt;\d3d8R.dll", carrega "&lt;pasta&gt;\Silent_Hill_3_PC_Fix.dll" — é ali que moram a
/// resolução personalizada, a janela sem borda e o conserto do menu de opções — e só então
/// carrega o d3d8R.dll (ou o do System32) e tira dele o Direct3DCreate8, que o carregador
/// exporta só repassando. Com o d3d8R.dll presente, o próprio fix desliga os remendos que só
/// valem para o d3d8 do Windows (janela maximizada e DirectX 12).
///
/// Logo o dgVoodoo não precisa (nem pode) ser o D3D8.dll: entra como d3d8R.dll ao lado, o fix
/// continua inteiro, e o Direct3D 8 do jogo cai no dgVoodoo → D3D11, onde o ReShade (dxgi.dll)
/// e o Feeder entram como em qualquer rota C. Trocar o d3d8.dll do fix pelo do dgVoodoo (o que
/// sobrava fazer à mão enquanto o plano recusava o nome ocupado) deixa o fix de fora: o jogo
/// abre na resolução mínima e o menu de opções não abre.
/// </summary>
public static class CarregadorD3d8R
{
    public const string Arquivo = "D3D8.dll";

    /// <summary>O nome que o carregador procura ao lado dele — e o nome com que o dgVoodoo entra.</summary>
    public const string D3d8R = "d3d8R.dll";

    /// <summary>A mod que o carregador do Silent Hill 3 PC Fix sobe; o nome dela está, em UTF-16, dentro dele.</summary>
    public const string ModSh3 = "Silent_Hill_3_PC_Fix.dll";

    private const long Orcamento = 32L * 1024 * 1024;

    /// <summary>O D3D8.dll desta pasta é um carregador que encadeia o d3d8R.dll (e não o próprio dgVoodoo)?</summary>
    public static bool Presente(string pasta)
    {
        var marcas = Marcas(Path.Combine(pasta, Arquivo));
        return marcas.Contains(D3d8R) && !marcas.Contains("dgVoodoo");
    }

    /// <summary>O arranjo está montado: o carregador no D3D8.dll e um d3d8R.dll ao lado dele.</summary>
    public static bool Encadeado(string pasta) =>
        File.Exists(Path.Combine(pasta, D3d8R)) && Presente(pasta);

    /// <summary>
    /// O Silent_Hill_3_PC_Fix.dll está na pasta, mas o D3D8.dll não é o carregador dele — então
    /// ninguém sobe o fix. É a pasta de quem trocou o d3d8.dll do fix pelo do dgVoodoo.
    /// </summary>
    public static bool FixSemCarregador(string pasta) =>
        File.Exists(Path.Combine(pasta, ModSh3)) && !Presente(pasta);

    /// <summary>Quem é o carregador, para as mensagens do plano.</summary>
    public static string Descrever(string pasta) =>
        Marcas(Path.Combine(pasta, Arquivo)).Contains(ModSh3)
            ? "o carregador do Silent Hill 3 PC Fix (é ele que sobe o Silent_Hill_3_PC_Fix.dll: resolução, janela e o conserto do menu de opções)"
            : "um carregador que passa o Direct3D 8 para um d3d8R.dll da própria pasta";

    private static HashSet<string> Marcas(string caminho)
    {
        if (!File.Exists(caminho)) return new HashSet<string>();
        try { return ApiDetector.ScanForMarkers(caminho, new[] { D3d8R, ModSh3, "dgVoodoo" }, Orcamento); }
        catch { return new HashSet<string>(); }
    }
}
