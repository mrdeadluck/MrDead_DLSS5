using System.Text.RegularExpressions;

namespace Dlss5.Core;

/// <summary>
/// Jogo da Steam: descobre o AppID para poder abrir pela Steam, e não pelo .exe.
///
/// Abrir o executável direto num jogo com DRM da Steam dá "Application load error
/// 5:0000065434" — o wrapper de DRM exige ter sido lançado pelo cliente. O botão "Abrir
/// o jogo" fazia exatamente isso, então falhava em boa parte dos jogos da Steam por um
/// motivo que não tem nada a ver com a instalação do DLSS 5.
/// </summary>
public static class SteamGame
{
    /// <summary>AppID do jogo, se ele estiver numa biblioteca da Steam.</summary>
    public static string? FindAppId(string? gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder)) return null;

        DirectoryInfo? dir;
        try { dir = new DirectoryInfo(gameFolder); }
        catch { return null; }

        // Sobe até casar o formato ...\steamapps\common\<Jogo>: a pasta apontada pode ser
        // uma subpasta do jogo (o binário real da Unreal, por exemplo).
        while (dir is not null)
        {
            var common = dir.Parent;
            var steamapps = common?.Parent;
            if (common is not null && steamapps is not null &&
                common.Name.Equals("common", StringComparison.OrdinalIgnoreCase) &&
                steamapps.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
            {
                return AppIdPara(steamapps.FullName, dir.Name);
            }
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Lê os appmanifest_*.acf e acha o que instalou nesta pasta.</summary>
    private static string? AppIdPara(string steamapps, string installDir)
    {
        IEnumerable<string> manifestos;
        try { manifestos = Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"); }
        catch { return null; }

        foreach (var manifesto in manifestos)
        {
            string texto;
            try { texto = File.ReadAllText(manifesto); }
            catch { continue; }

            var pasta = Valor(texto, "installdir");
            if (pasta is null || !pasta.Equals(installDir, StringComparison.OrdinalIgnoreCase)) continue;

            var appid = Valor(texto, "appid");
            if (appid is not null) return appid;

            // O nome do arquivo carrega o AppID: appmanifest_12140.acf
            var m = Regex.Match(Path.GetFileName(manifesto), @"appmanifest_(\d+)\.acf",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>Valor de uma chave no formato VDF: "chave"\t\t"valor".</summary>
    private static string? Valor(string vdf, string chave)
    {
        var m = Regex.Match(vdf, "\"" + Regex.Escape(chave) + "\"\\s+\"([^\"]*)\"",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>URL que faz a Steam lançar o jogo com o DRM satisfeito.</summary>
    public static string RunUrl(string appId) => $"steam://rungameid/{appId}";

    /// <summary>Raiz da Steam usada no lugar do registro (testes).</summary>
    public static string? RaizDaSteamParaTeste { get; set; }

    /// <summary>
    /// Pasta do cliente da Steam (onde fica userdata\), pelo registro. É a instalação
    /// principal, não a biblioteca onde o jogo está — as duas podem ser discos diferentes.
    /// </summary>
    public static string? RaizDaSteam()
    {
        if (RaizDaSteamParaTeste is { } teste) return teste;
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string p && !string.IsNullOrWhiteSpace(p))
            {
                var caminho = p.Replace('/', Path.DirectorySeparatorChar);
                if (Directory.Exists(caminho)) return caminho;
            }
        }
        catch { }
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (key?.GetValue("InstallPath") is string p && Directory.Exists(p)) return p;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// As Opções de inicialização que o usuário digitou na Steam para este AppID, lidas do
    /// userdata\*\config\localconfig.vdf (a Steam grava ao fechar a janela de propriedades).
    /// Null quando não há como ler (Steam não achada, arquivo ausente); vazio quando não há
    /// opção. Com mais de uma conta, vale a primeira que tiver opção para o jogo.
    /// </summary>
    public static string? LaunchOptions(string appId)
    {
        var raiz = RaizDaSteam();
        if (raiz is null) return null;
        IEnumerable<string> configs;
        try
        {
            configs = Directory.EnumerateDirectories(Path.Combine(raiz, "userdata"))
                .Select(u => Path.Combine(u, "config", "localconfig.vdf"))
                .Where(File.Exists)
                .ToList();
        }
        catch { return null; }

        string? vazio = null;
        foreach (var config in configs)
        {
            string texto;
            try { texto = File.ReadAllText(config); }
            catch { continue; }
            var opcao = LaunchOptionsEm(texto, appId);
            if (!string.IsNullOrWhiteSpace(opcao)) return opcao;
            if (opcao is not null) vazio = opcao;
        }
        return vazio;
    }

    /// <summary>
    /// Acha o bloco "&lt;appid&gt;" { ... } dentro do VDF e lê a chave LaunchOptions dele,
    /// desfazendo os escapes do formato (\" e \\). Null se o bloco não existe.
    /// </summary>
    public static string? LaunchOptionsEm(string vdf, string appId)
    {
        var bloco = Regex.Match(vdf, "\"" + Regex.Escape(appId) + "\"\\s*\\{");
        while (bloco.Success)
        {
            int inicio = bloco.Index + bloco.Length;
            int nivel = 1, i = inicio;
            for (; i < vdf.Length && nivel > 0; i++)
            {
                if (vdf[i] == '{') nivel++;
                else if (vdf[i] == '}') nivel--;
            }
            var corpo = vdf.Substring(inicio, Math.Max(0, i - inicio - 1));
            var m = Regex.Match(corpo, "\"LaunchOptions\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
            if (m.Success)
                return Regex.Replace(m.Groups[1].Value, "\\\\([\"\\\\])", "$1");
            // O mesmo AppID aparece noutros blocos (Broadcast, compat...): só o de apps tem
            // LaunchOptions; um bloco sem ela conta como "sem opção" apenas se for o último.
            var proximo = bloco.NextMatch();
            if (!proximo.Success) return "";
            bloco = proximo;
        }
        return null;
    }
}
