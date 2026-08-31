using System.Text;

namespace Dlss5.Core;

/// <summary>
/// Ajusta o dgVoodoo.conf de forma consciente de seção (spec 8.7 / 12.7).
/// Importante: a chave "VideoCard" existe em [Glide] E em [DirectX] — um replace
/// global (como no snippet original) estragaria a de Glide. Aqui a troca é por seção.
/// </summary>
public static class DgVoodooConfigurator
{
    /// <summary>(seção, chave) => valor. Só mexe nas linhas dessas seções.</summary>
    private static readonly (string Section, string Key, string Value)[] Targets =
    {
        ("General",    "OutputAPI",          "d3d11_fl11_0"),
        ("DirectX",    "DisableAndPassThru", "false"),
        ("DirectX",    "VideoCard",          "internal3D"),
        ("DirectX",    "VRAM",               "1024"),
        ("DirectXExt", "dgVoodooWatermark",  "true"),
    };

    /// <summary>Aplica os ajustes ao texto do .conf, preservando formatação/alinhamento.</summary>
    public static string Patch(string confText)
    {
        var lines = confText.Replace("\r\n", "\n").Split('\n');
        string currentSection = "";
        var applied = new HashSet<(string, string)>();

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.TrimStart();

            if (trimmed.StartsWith('['))
            {
                int end = trimmed.IndexOf(']');
                if (end > 1)
                    currentSection = trimmed.Substring(1, end - 1).Trim();
                continue;
            }

            int eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            string key = raw[..eq].Trim();

            foreach (var (section, tKey, value) in Targets)
            {
                if (!currentSection.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
                if (!key.Equals(tKey, StringComparison.OrdinalIgnoreCase)) continue;

                // Mantém tudo até o '=' (inclui o alinhamento em espaços) e troca só o valor.
                lines[i] = raw[..(eq + 1)] + " " + value;
                applied.Add((section, tKey));
            }
        }

        var sb = new StringBuilder();
        foreach (var line in lines) sb.Append(line).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Lista os alvos que NÃO foram encontrados no texto (para diagnóstico).</summary>
    public static IReadOnlyList<string> MissingKeys(string confText)
    {
        var lines = confText.Replace("\r\n", "\n").Split('\n');
        string currentSection = "";
        var found = new HashSet<(string, string)>();
        foreach (var raw in lines)
        {
            var trimmed = raw.TrimStart();
            if (trimmed.StartsWith('['))
            {
                int end = trimmed.IndexOf(']');
                if (end > 1) currentSection = trimmed.Substring(1, end - 1).Trim();
                continue;
            }
            int eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            string key = raw[..eq].Trim();
            foreach (var (section, tKey, _) in Targets)
                if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase) &&
                    key.Equals(tKey, StringComparison.OrdinalIgnoreCase))
                    found.Add((section, tKey));
        }
        return Targets
            .Where(t => !found.Contains((t.Section, t.Key)))
            .Select(t => $"[{t.Section}] {t.Key}")
            .ToList();
    }
}
