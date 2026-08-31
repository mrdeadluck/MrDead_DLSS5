namespace Dlss5.Core;

/// <summary>
/// Arquivos que NÃO devem ficar na pasta do jogo (spec 3.7) e o prólogo de limpeza
/// do "passo a passo". Interposer do Streamline, frame-gen e restos de instalação.
/// </summary>
public static class ForbiddenFiles
{
    /// <summary>
    /// DLLs do Streamline. A spec 3.7 manda não LEVAR estas para a pasta do jogo (elas vêm
    /// do zip do Streamline no kit e o sl.interposer disputa o DXGI com o ReShade). Mas num
    /// jogo que tem DLSS nativo elas são arquivos DO PRÓPRIO JOGO: apagá-las derruba o DLSS
    /// que o RenoDX precisa justamente enxergar. Por isso elas só saem quando o jogo não tem
    /// DLSS nativo, caso em que são sobra de tentativa anterior.
    /// </summary>
    public static readonly string[] StreamlineNames =
    {
        "sl.common.dll", "sl.dlss.dll", "sl.dlss_g.dll", "sl.dlss_nr.dll",
        "sl.interposer.dll", "sl.nis.dll", "sl.pcl.dll", "sl.reflex.dll",
    };

    /// <summary>Nomes exatos a remover da raiz do exe (case-insensitive).</summary>
    public static readonly string[] Names =
    {
        // Interposer do Streamline — disputa o DXGI com o ReShade (spec 3.7)
        "sl.common.dll", "sl.dlss.dll", "sl.dlss_g.dll", "sl.dlss_nr.dll",
        "sl.interposer.dll", "sl.nis.dll", "sl.pcl.dll", "sl.reflex.dll",
        // Frame generation — só consome VRAM
        "nvngx_dlssg.dll",
        // Licenças inúteis
        "nis.license.txt", "nvngx_dlss.license.txt", "reflex.license.txt",
        // Instalador não fica no jogo
        "ReShade_Setup_6.8.0_Addon.exe",
    };

    /// <summary>
    /// Lista os arquivos proibidos realmente presentes na pasta do exe.
    /// Não inclui os arquivos que a instalação vai (re)colocar de propósito.
    /// </summary>
    public static IReadOnlyList<string> FindPresent(string exeFolder, bool keepGameStreamline = false)
    {
        var found = new List<string>();
        foreach (var name in Names)
        {
            if (keepGameStreamline &&
                StreamlineNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            var path = Path.Combine(exeFolder, name);
            if (File.Exists(path)) found.Add(path);
        }
        return found;
    }
}
