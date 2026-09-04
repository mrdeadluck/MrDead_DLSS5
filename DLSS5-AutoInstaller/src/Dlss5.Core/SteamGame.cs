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
}
