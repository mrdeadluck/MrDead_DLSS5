using System.Text;

namespace Dlss5.Core;

/// <summary>Uma tecla oferecida para abrir o overlay, com o Virtual-Key Code do Windows.</summary>
public sealed record OverlayKeyOption(string Label, int VirtualKey);

/// <summary>Gera ReShade.ini e ReShadePreset.ini já prontos (spec 12.5 / 12.6 / 8.4).</summary>
public static class ReShadeConfigWriter
{
    /// <summary>Home = 36, Insert = 45 (spec 8.3: alternativa quando o jogo captura Home).</summary>
    public const int KeyHome = 36;
    public const int KeyInsert = 45;

    /// <summary>
    /// Teclas oferecidas para o overlay. O ReShade aceita qualquer Virtual-Key Code no
    /// formato KeyOverlay=&lt;vk&gt;,&lt;ctrl&gt;,&lt;shift&gt;,&lt;alt&gt;, então a lista é só
    /// conveniência — com os modificadores dá para montar combinações que nenhum jogo usa.
    /// </summary>
    public static IReadOnlyList<OverlayKeyOption> OverlayKeys { get; } = BuildKeyList();

    private static List<OverlayKeyOption> BuildKeyList()
    {
        var list = new List<OverlayKeyOption>
        {
            new("Home", KeyHome),
            new("Insert", KeyInsert),
            new("Delete", 46),
            new("End", 35),
            new("Page Up", 33),
            new("Page Down", 34),
            new("Pause/Break", 19),
            new("Scroll Lock", 145),
            new("Print Screen", 44),
            new("Tab", 9),
            new("Backspace", 8),
            new("Espaço", 32),
            new("Caps Lock", 20),
            new("Num Lock", 144),
            new("Seta para cima", 38),
            new("Seta para baixo", 40),
            new("Seta para esquerda", 37),
            new("Seta para direita", 39),
            new("' \" ` ~ (tecla à esquerda do 1)", 192),
            new("- _ (menos)", 189),
            new("= + (igual)", 187),
            new("[ {", 219),
            new("] }", 221),
            new("; :", 186),
            new(", <", 188),
            new(". >", 190),
            new("/ ?", 191),
            new("\\ |", 220),
        };

        for (int i = 1; i <= 12; i++)
            list.Add(new OverlayKeyOption($"F{i}", 111 + i));
        for (char c = 'A'; c <= 'Z'; c++)
            list.Add(new OverlayKeyOption($"Tecla {c}", c));
        for (int d = 0; d <= 9; d++)
            list.Add(new OverlayKeyOption($"Número {d}", 48 + d));
        for (int d = 0; d <= 9; d++)
            list.Add(new OverlayKeyOption($"Numpad {d}", 96 + d));

        list.Add(new OverlayKeyOption("Numpad *", 106));
        list.Add(new OverlayKeyOption("Numpad +", 107));
        list.Add(new OverlayKeyOption("Numpad -", 109));
        list.Add(new OverlayKeyOption("Numpad .", 110));
        list.Add(new OverlayKeyOption("Numpad /", 111));
        return list;
    }

    /// <summary>Nome legível da combinação, para instruções e para a tela.</summary>
    /// <summary>
    /// Lê a linha KeyOverlay de um ReShade.ini já gravado. O que vale para o jogo é o que
    /// está no arquivo, não o que a tela mostra — e a tecla é lembrada entre execuções.
    /// </summary>
    public static (int VirtualKey, bool Ctrl, bool Shift, bool Alt)? LerTeclaDoOverlay(string iniText)
    {
        foreach (var linha in iniText.Replace("\r\n", "\n").Split('\n'))
        {
            var t = linha.Trim();
            if (!t.StartsWith("KeyOverlay", StringComparison.OrdinalIgnoreCase)) continue;

            int eq = t.IndexOf('=');
            if (eq < 0) continue;

            var partes = t[(eq + 1)..].Split(',', StringSplitOptions.TrimEntries);
            if (partes.Length == 0 || !int.TryParse(partes[0], out var vk)) continue;

            bool Bit(int i) => partes.Length > i && partes[i] == "1";
            return (vk, Bit(1), Bit(2), Bit(3));
        }
        return null;
    }

    public static string DescribeKey(int virtualKey, bool ctrl = false, bool shift = false, bool alt = false)
    {
        var name = OverlayKeys.FirstOrDefault(k => k.VirtualKey == virtualKey)?.Label
                   ?? $"tecla {virtualKey}";
        var parts = new List<string>(4);
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        parts.Add(name);
        return string.Join("+", parts);
    }

    /// <param name="feederUsed">
    /// Com o Feeder, o Generic Depth é peça da cadeia (o Feed.fx lê o depth por ele) e
    /// a cópia antes dos clears garante que ele veja a cena inteira. No caminho direto
    /// nada disso serve: o RenoDX recebe depth e motion vectors do contrato NGX do
    /// próprio jogo. E não é só inútil — a cópia antes de cada clear insere cópias e
    /// barreiras no meio do frame, e foi o que derrubou o RE9 (crash dentro do exe um
    /// segundo depois do runtime do ReShade subir, com ou sem RenoDX), enquanto uma
    /// instalação manual do ReShade, que não liga isso, roda. No direto o Generic
    /// Depth fica desligado por inteiro.
    /// </param>
    public static string BuildReShadeIni(
        int overlayKey = KeyHome,
        bool ctrl = false,
        bool shift = false,
        bool alt = false,
        string presetFile = "ReShadePreset.ini",
        bool feederUsed = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[GENERAL]");
        sb.AppendLine(@"EffectSearchPaths=.\reshade-shaders\Shaders\**");
        sb.AppendLine(@"TextureSearchPaths=.\reshade-shaders\Textures\**");
        sb.AppendLine($"PresetPath=.\\{presetFile}");
        sb.AppendLine();
        sb.AppendLine("[INPUT]");
        sb.AppendLine($"KeyOverlay={overlayKey},{Bit(ctrl)},{Bit(shift)},{Bit(alt)}");
        sb.AppendLine();
        sb.AppendLine("[ADDON]");
        sb.AppendLine(@"AddonPath=.\");
        if (!feederUsed)
            sb.AppendLine("DisabledAddons=Generic Depth");
        if (feederUsed)
        {
            sb.AppendLine();
            sb.AppendLine("[DEPTH]");
            // Generic Depth costuma acertar sozinho; deixamos os overrides zerados e visíveis.
            sb.AppendLine("DepthCopyBeforeClears=1");
        }
        return sb.ToString();

        static int Bit(bool b) => b ? 1 : 0;
    }

    /// <summary>
    /// Preset com o provedor de MV ACIMA do DLSS 5 Feed e ambos marcados, eliminando o
    /// passo manual de marcar/reordenar (spec 8.4).
    ///
    /// Com DLSS nativo o Feeder não é instalado: o RenoDX se pendura direto na chamada de
    /// DLSS que o jogo já faz, e nenhum efeito do ReShade participa — por isso o preset
    /// sai vazio, em vez de ligar shaders que não teriam função.
    /// </summary>
    public static string BuildPresetIni(MvProvider provider, bool feederUsed = true)
    {
        var sb = new StringBuilder();
        if (!feederUsed)
        {
            sb.AppendLine("Techniques=");
            sb.AppendLine("TechniqueSorting=");
            return sb.ToString();
        }

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

        sb.AppendLine($"Techniques={list}");
        sb.AppendLine($"TechniqueSorting={list}");
        return sb.ToString();
    }
}
