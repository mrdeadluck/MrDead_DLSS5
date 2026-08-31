using System.Text;

namespace Dlss5.Core;

/// <summary>Gera ReShade.ini e ReShadePreset.ini já prontos (spec 12.5 / 12.6 / 8.4).</summary>
public static class ReShadeConfigWriter
{
    /// <summary>Home = 36, Insert = 45 (spec 8.3: alternativa quando o jogo captura Home).</summary>
    public const int KeyHome = 36;
    public const int KeyInsert = 45;

    public static string BuildReShadeIni(int overlayKey = KeyHome, string presetFile = "ReShadePreset.ini")
    {
        var sb = new StringBuilder();
        sb.AppendLine("[GENERAL]");
        sb.AppendLine(@"EffectSearchPaths=.\reshade-shaders\Shaders\**");
        sb.AppendLine(@"TextureSearchPaths=.\reshade-shaders\Textures\**");
        sb.AppendLine($"PresetPath=.\\{presetFile}");
        sb.AppendLine();
        sb.AppendLine("[INPUT]");
        sb.AppendLine($"KeyOverlay={overlayKey},0,0,0");
        sb.AppendLine();
        sb.AppendLine("[ADDON]");
        sb.AppendLine(@"AddonPath=.\");
        sb.AppendLine();
        sb.AppendLine("[DEPTH]");
        // Generic Depth costuma acertar sozinho; deixamos os overrides zerados e visíveis.
        sb.AppendLine("DepthCopyBeforeClears=1");
        return sb.ToString();
    }

    /// <summary>
    /// Preset com o provedor de MV ACIMA do DLSS 5 Feed e ambos marcados,
    /// eliminando o passo manual de marcar/reordenar (spec 8.4).
    /// </summary>
    public static string BuildPresetIni(MvProvider provider)
    {
        // Nomes reais das techniques (lidos dos .fx do kit):
        //   MotionEstimation.fx      -> DRME
        //   MartysMods_LAUNCHPAD.fx  -> MartysMods_Launchpad
        //   DLSS5_Feed.fx            -> DLSS5_Feed
        string mv = provider == MvProvider.Drme
            ? "DRME@MotionEstimation.fx"
            : "MartysMods_Launchpad@MartysMods_LAUNCHPAD.fx";
        const string feed = "DLSS5_Feed@DLSS5_Feed.fx";

        // A ordem da lista = ordem de execução; o provedor vem primeiro.
        string list = $"{mv},{feed}";

        var sb = new StringBuilder();
        sb.AppendLine($"Techniques={list}");
        sb.AppendLine($"TechniqueSorting={list}");
        return sb.ToString();
    }
}
