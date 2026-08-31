using System.Text;

namespace Dlss5.Core;

/// <summary>
/// Ajusta o dgVoodoo.conf de forma consciente de seção (spec 8.7 / 12.7).
/// Importante: a chave "VideoCard" existe em [Glide] E em [DirectX] — um replace
/// global (como no snippet original) estragaria a de Glide. Aqui a troca é por seção.
/// </summary>
/// <summary>Como configurar o dgVoodoo, conforme a idade do jogo.</summary>
public enum DgVoodooProfile
{
    /// <summary>D3D9 e afins — o que a especificação validou.</summary>
    Padrao,

    /// <summary>
    /// DirectX 8 e jogos da mesma época. Eles inspecionam o adaptador antes de criar o
    /// device, e o cartão virtual do dgVoodoo se identifica como ele mesmo: o Max Payne
    /// responde a isso com "requires a DirectX 8 compatible display adapter" e nem chega
    /// a abrir. Este perfil mantém o internal3D (que é o de mais capacidades) mas faz a
    /// identidade parecer uma placa NVIDIA comum, com os nomes de device padrão da
    /// Microsoft — as duas chaves que o próprio dgVoodoo documenta para esse caso.
    /// </summary>
    Legado,
}

public static class DgVoodooConfigurator
{
    /// <summary>(seção, chave) => valor. Só mexe nas linhas dessas seções.</summary>
    private static readonly (string Section, string Key, string Value)[] Base =
    {
        ("General",    "OutputAPI",          "d3d11_fl11_0"),
        ("DirectX",    "DisableAndPassThru", "false"),
        ("DirectX",    "VideoCard",          "internal3D"),
        ("DirectX",    "VRAM",               "1024"),
        ("DirectXExt", "dgVoodooWatermark",  "true"),
    };

    /// <summary>
    /// O que muda para jogo antigo. VRAM volta a 256 MB (o padrão do próprio dgVoodoo):
    /// jogo de 2001 não foi escrito esperando uma placa de 1 GB, e alguns fazem conta
    /// errada com valores grandes.
    /// </summary>
    private static readonly (string Section, string Key, string Value)[] Legado =
    {
        ("DirectX",    "VRAM",              "256"),
        ("DirectXExt", "AdapterIDType",     "nvidia"),
        ("DirectXExt", "MSD3DDeviceNames",  "true"),
    };

    /// <summary>Chaves efetivamente aplicadas para um perfil.</summary>
    public static IReadOnlyList<(string Section, string Key, string Value)> TargetsFor(DgVoodooProfile perfil)
    {
        if (perfil == DgVoodooProfile.Padrao) return Base;

        var lista = Base.ToList();
        foreach (var extra in Legado)
        {
            int i = lista.FindIndex(t =>
                t.Section.Equals(extra.Section, StringComparison.OrdinalIgnoreCase) &&
                t.Key.Equals(extra.Key, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) lista[i] = extra;
            else lista.Add(extra);
        }
        return lista;
    }

    /// <summary>Perfil adequado à API do jogo.</summary>
    public static DgVoodooProfile ProfileFor(GraphicsApi api) =>
        api == GraphicsApi.D3D8 ? DgVoodooProfile.Legado : DgVoodooProfile.Padrao;

    /// <summary>Aplica os ajustes ao texto do .conf, preservando formatação/alinhamento.</summary>
    public static string Patch(string confText, DgVoodooProfile perfil = DgVoodooProfile.Padrao)
    {
        var Targets = TargetsFor(perfil);
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
    public static IReadOnlyList<string> MissingKeys(string confText, DgVoodooProfile perfil = DgVoodooProfile.Padrao)
    {
        var Targets = TargetsFor(perfil);
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
