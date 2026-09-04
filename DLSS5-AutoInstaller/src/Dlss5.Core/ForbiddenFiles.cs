namespace Dlss5.Core;

/// <summary>
/// Arquivos que não deveriam estar na pasta do jogo (spec 3.7).
///
/// A regra da spec é NÃO LEVAR estes arquivos do kit para o jogo. Ela não diz para
/// apagar os que já estão lá — e a diferença é enorme: as sl.*.dll e a nvngx_dlssg.dll,
/// quando existem numa pasta de jogo, são arquivos DO PRÓPRIO JOGO. Nenhuma delas sequer
/// existe no kit. Apagá-las derruba o DLSS e o frame generation do jogo, que é
/// exatamente o que aconteceu num teste real em Forza.
///
/// Por isso só sai daqui o que não tem como ser do jogo. O resto vira aviso.
/// </summary>
public static class ForbiddenFiles
{
    /// <summary>
    /// Do jogo, nunca do kit. Aparecem como aviso no plano, e o programa não encosta
    /// nelas: se o jogo tem DLSS nativo é por elas que ele passa.
    /// </summary>
    public static readonly string[] GameOwnedNames =
    {
        "sl.common.dll", "sl.dlss.dll", "sl.dlss_g.dll", "sl.dlss_nr.dll",
        "sl.interposer.dll", "sl.nis.dll", "sl.pcl.dll", "sl.reflex.dll",
        "nvngx_dlssg.dll",
    };

    /// <summary>Instalador não tem o que fazer dentro da pasta de um jogo.</summary>
    public static readonly string[] Names =
    {
        "ReShade_Setup_6.8.0_Addon.exe",
    };

    /// <summary>Arquivos que a instalação vai remover (com backup).</summary>
    public static IReadOnlyList<string> FindPresent(string exeFolder)
    {
        var found = new List<string>();
        foreach (var name in Names)
        {
            var path = Path.Combine(exeFolder, name);
            if (File.Exists(path)) found.Add(path);
        }
        return found;
    }

    /// <summary>Arquivos do jogo que existem na pasta — só para avisar, nunca para apagar.</summary>
    public static IReadOnlyList<string> FindGameOwned(string exeFolder)
    {
        var found = new List<string>();
        foreach (var name in GameOwnedNames)
        {
            var path = Path.Combine(exeFolder, name);
            if (File.Exists(path)) found.Add(Path.GetFileName(path));
        }
        return found;
    }
}
