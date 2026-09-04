namespace Dlss5.Core;

/// <summary>
/// Jogos sob o Easy Anti-Cheat (EAC). O Gears of War Reloaded ensinou: a Steam abre o
/// start_protected_game.exe da raiz (o bootstrapper do EAC), que lê EasyAntiCheat\Settings.json
/// e só então abre Binaries_x64\GOWDE-Steam.exe sob o EAC — e sob ele
/// nenhuma DLL que ele não reconheça carrega dentro do processo — o dxgi.dll do ReShade
/// fica na pasta sem rodar, o ReShade.log nem nasce, e o jogo, que não conseguiu abrir o
/// DXGI/D3D12 pelo caminho que pediu, mostra "Your machine does not support Direct3D 12.
/// Force quitting." e fecha. Não é a placa, não é o driver: é o anticheat recusando a DLL.
///
/// Os arquivos que o instalador põe na pasta são os mesmos de sempre; o que muda é que o
/// jogo só os carrega com o EAC fora. O contorno que a comunidade usa para jogar a
/// campanha com mods está em <see cref="ComoAbrir"/>. É do usuário, não deste programa:
/// o instalador reconhece o caso, avisa, e NÃO mexe em arquivo de anticheat.
/// </summary>
public static class EasyAntiCheat
{
    public const string Pasta = "EasyAntiCheat";
    public const string Settings = "Settings.json";

    /// <summary>Arquivos que só existem em instalação com EAC, na raiz ou na pasta do exe.</summary>
    public static readonly string[] Arquivos =
    {
        "EasyAntiCheat_EOS_Setup.exe",
        "EasyAntiCheat_Setup.exe",
        "EasyAntiCheat_EOS.dll",
        "EasyAntiCheat_x64.dll",
        "EasyAntiCheat_x86.dll",
        "start_protected_game.exe",
    };

    /// <summary>
    /// Onde o EAC mora nesta instalação, ou null. Procura a pasta EasyAntiCheat\ (com o
    /// Settings.json ou o setup dentro) e os arquivos soltos na pasta do exe, na raiz do
    /// jogo e em Content\ (Gears Reloaded: EasyAntiCheat\ e start_protected_game.exe na raiz,
    /// com o exe em Binaries_x64).
    /// </summary>
    public static string? Encontrar(string? gameFolder, string? exeFolder)
    {
        foreach (var basePath in Bases(gameFolder, exeFolder))
        {
            try
            {
                var pasta = Path.Combine(basePath, Pasta);
                if (Directory.Exists(pasta))
                {
                    var conteudo = Directory.EnumerateFiles(pasta, "*", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileName)
                        .Where(n => n is not null)
                        .ToList();
                    bool temSinal = conteudo.Any(n =>
                        n!.Equals(Settings, StringComparison.OrdinalIgnoreCase) ||
                        n.StartsWith("EasyAntiCheat", StringComparison.OrdinalIgnoreCase));
                    if (temSinal) return pasta;
                }
                foreach (var nome in Arquivos)
                {
                    var arquivo = Path.Combine(basePath, nome);
                    if (File.Exists(arquivo)) return arquivo;
                }
            }
            catch
            {
                // Pasta ilegível: segue para a próxima.
            }
        }
        return null;
    }

    public static bool Presente(string? gameFolder, string? exeFolder) => Encontrar(gameFolder, exeFolder) is not null;

    /// <summary>O Settings.json do EAC (o arquivo do contorno), quando a pasta foi achada.</summary>
    public static string? SettingsDe(string? gameFolder, string? exeFolder)
    {
        var onde = Encontrar(gameFolder, exeFolder);
        if (onde is null) return null;
        var pasta = Directory.Exists(onde) ? onde : Path.GetDirectoryName(onde);
        if (pasta is null) return null;
        try
        {
            return Directory.EnumerateFiles(pasta, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => Path.GetFileName(f).Equals(Settings, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> Bases(string? gameFolder, string? exeFolder)
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidato in new[]
                 {
                     exeFolder,
                     gameFolder,
                     Pai(exeFolder),
                     gameFolder is null ? null : Path.Combine(gameFolder, "Content"),
                     exeFolder is null ? null : Path.Combine(exeFolder, "Content"),
                 })
        {
            if (string.IsNullOrWhiteSpace(candidato)) continue;
            string normal;
            try { normal = Path.GetFullPath(candidato); } catch { continue; }
            if (vistos.Add(normal)) yield return normal;
        }
    }

    private static string? Pai(string? pasta)
    {
        try { return string.IsNullOrWhiteSpace(pasta) ? null : Path.GetDirectoryName(Path.GetFullPath(pasta)); }
        catch { return null; }
    }

    public const string Aviso =
        "Easy Anti-Cheat: este jogo sobe o EAC junto com o exe, e sob ele nenhuma DLL estranha carrega " +
        "dentro do processo — o dxgi.dll do ReShade fica na pasta sem rodar e o ReShade.log nem nasce. " +
        "No Gears of War Reloaded isso aparece como \"Your machine does not support Direct3D 12. Force " +
        "quitting.\": não é a placa nem o driver, é o anticheat recusando a DLL. Os arquivos instalados " +
        "estão certos; o jogo só os carrega com o EAC fora.";

    public const string ComoAbrir =
        "O que a comunidade faz para jogar a CAMPANHA com mods: abra o Settings.json da pasta EasyAntiCheat " +
        "(no Gears Reloaded fica na raiz do jogo: EasyAntiCheat\\Settings.json) no Bloco de Notas e troque UMA letra do " +
        "valor de \"productid\" (por exemplo o último bloco ...f03 → ...g03); salve. Com o id inválido o EAC " +
        "não sobe, o jogo abre normalmente pela Steam e o ReShade carrega. Só para jogar OFFLINE: o " +
        "multiplayer recusa a entrada sem o EAC (\"rejected by the anti cheat server\"), e entrar online " +
        "com mods pode marcar a conta. Para voltar ao normal, desfaça a edição ou use Steam → Verificar " +
        "integridade dos arquivos. Este programa não mexe nesse arquivo.";

    /// <summary>
    /// O "productid" do Settings.json: 32 hexadecimais quando é o original — aí o EAC sobe
    /// junto com o jogo. Uma letra fora do hexadecimal (o contorno da comunidade) e o EAC
    /// não sobe. A verificação lê isto para dizer se o passo foi feito, sem adivinhar.
    /// </summary>
    public static (EstadoDoProductId Estado, string? Valor) LerProductId(string? settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
            return (EstadoDoProductId.SemArquivo, null);
        string texto;
        try { texto = File.ReadAllText(settingsPath); }
        catch { return (EstadoDoProductId.SemArquivo, null); }

        var m = System.Text.RegularExpressions.Regex.Match(texto,
            "\"productid\"\\s*:\\s*\"([^\"]*)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return (EstadoDoProductId.Desconhecido, null);
        var valor = m.Groups[1].Value.Trim();
        bool hex32 = valor.Length == 32 && valor.All(Uri.IsHexDigit);
        return (hex32 ? EstadoDoProductId.Valido : EstadoDoProductId.Invalido, valor);
    }

    /// <summary>Uma linha para a tela de detecção, com o caminho encontrado.</summary>
    public static string Nota(string encontradoEm, string? gameFolder)
    {
        string rel;
        try { rel = gameFolder is null ? encontradoEm : Path.GetRelativePath(gameFolder, encontradoEm); }
        catch { rel = encontradoEm; }
        return $"Easy Anti-Cheat na instalação ({rel}). " + Aviso + " " + ComoAbrir;
    }
}

/// <summary>O que o Settings.json do EAC diz sobre o productid.</summary>
public enum EstadoDoProductId
{
    /// <summary>Não há Settings.json (ou não deu para ler).</summary>
    SemArquivo,
    /// <summary>32 hexadecimais: é o original, o EAC sobe junto com o jogo.</summary>
    Valido,
    /// <summary>Fora do formato: o contorno foi aplicado, o EAC não sobe.</summary>
    Invalido,
    /// <summary>Arquivo sem a chave "productid".</summary>
    Desconhecido,
}
