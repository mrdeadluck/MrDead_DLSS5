using System.Text.RegularExpressions;

namespace Dlss5.Core;

/// <summary>O que o dlss5-feed.log diz — inclusive o FIM dele.</summary>
/// <param name="FeaturePronta">Alguma "feature ready" apareceu.</param>
/// <param name="FramesEntregues">Maior "frame N delivered".</param>
/// <param name="Travou">O log termina em falha: "stopped:", "CRASH RECORDED" ou exceção no CreateFeature.</param>
/// <param name="ResolucaoQueFuncionou">A resolução da última "feature ready".</param>
/// <param name="ResolucaoQueFalhou">A resolução do "building:" imediatamente anterior à falha.</param>
/// <param name="UltimaAcao">O "this add-on was last doing: ..." do registro de crash.</param>
/// <param name="Motivo">A linha "stopped: ..." ou "failure: ...".</param>
public sealed record FeedStatus(
    bool FeaturePronta,
    int FramesEntregues,
    bool Travou,
    string? ResolucaoQueFuncionou,
    string? ResolucaoQueFalhou,
    string? UltimaAcao,
    string? Motivo)
{
    /// <summary>Funcionou numa resolução e caiu ao reconstruir noutra (a janela inicial → tela cheia).</summary>
    public bool CaiuNaTrocaDeResolucao =>
        Travou && ResolucaoQueFuncionou is not null && ResolucaoQueFalhou is not null &&
        !ResolucaoQueFuncionou.Equals(ResolucaoQueFalhou, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Lê o dlss5-feed.log inteiro, não só o começo. O NFS ensinou: "feature ready" e
/// "frame 2 delivered" a 1280x720 na janela inicial, e vinte linhas depois, ao
/// reconstruir em 3840x2160, "CreateFeature raised exception 0xC0000005" e
/// "### CRASH RECORDED ###". A verificação lia o começo e dizia OK — o jogo travado.
/// </summary>
public static class FeedLog
{
    private static readonly Regex Entregue = new(@"frame\s+(\d+)\s+delivered", RegexOptions.IgnoreCase);
    private static readonly Regex Pronta = new(@"feature ready:\s*(\d+x\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex Construindo = new(@"building:\s*(\d+x\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex Ultima = new(@"last doing:\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex Parou = new(@"^\S*\s+(stopped:.+?|\[feed\] failure:.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static FeedStatus? Ler(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        int frames = 0;
        foreach (Match m in Entregue.Matches(text))
            if (int.TryParse(m.Groups[1].Value, out var n) && n > frames) frames = n;

        var prontas = Pronta.Matches(text);
        string? funcionou = prontas.Count > 0 ? prontas[^1].Groups[1].Value : null;

        bool travou = text.Contains("### CRASH RECORDED ###", StringComparison.OrdinalIgnoreCase)
                      || text.Contains("CreateFeature raised exception", StringComparison.OrdinalIgnoreCase)
                      || Regex.IsMatch(text, @"^\S*\s+stopped:", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // O "building:" que precede a falha — o último do log, quando houve falha.
        string? falhou = null;
        if (travou)
        {
            var builds = Construindo.Matches(text);
            if (builds.Count > 0) falhou = builds[^1].Groups[1].Value;
        }

        var ultima = Ultima.Match(text);
        var parou = Parou.Match(text);

        return new FeedStatus(
            FeaturePronta: prontas.Count > 0,
            FramesEntregues: frames,
            Travou: travou,
            ResolucaoQueFuncionou: funcionou,
            ResolucaoQueFalhou: falhou,
            UltimaAcao: ultima.Success ? ultima.Groups[1].Value.Trim() : null,
            Motivo: parou.Success ? parou.Groups[1].Value.Trim() : null);
    }
}

/// <summary>
/// O dlss5-feed.cfg: key=value por linha, relido pelo Feeder com o jogo aberto. A chave
/// que interessa aqui é work_resolution (só D3D11: 50–100% dos eixos do backbuffer),
/// o alívio de VRAM e de custo que o próprio Feeder oferece — o NFS em 4K estourou
/// exatamente na reconstrução das texturas de trabalho.
/// </summary>
public static class FeedCfg
{
    public const string Arquivo = "dlss5-feed.cfg";
    public const string ChaveResolucao = "work_resolution";
    public const int ResolucaoPadrao = 100;

    /// <summary>Na ordem em que a tela oferece.</summary>
    public static IReadOnlyList<int> ResolucoesDeTrabalho { get; } = new[] { 100, 75, 67, 50 };

    public static string Descricao(int valor) => valor switch
    {
        100 => "100% — resolução cheia (padrão)",
        75 => "75% — alívio moderado de VRAM",
        67 => "67% — alívio grande (recomendado para 4K)",
        50 => "50% — mínimo",
        _ => $"{valor}%",
    };

    public static int? Ler(string? cfg, string chave = ChaveResolucao)
    {
        if (string.IsNullOrWhiteSpace(cfg)) return null;
        foreach (var bruta in cfg.Replace("\r\n", "\n").Split('\n'))
        {
            var linha = bruta.Trim();
            if (linha.Length == 0 || linha[0] is '#' or ';' or '[') continue;
            int eq = linha.IndexOf('=');
            if (eq <= 0) continue;
            if (!linha[..eq].Trim().Equals(chave, StringComparison.OrdinalIgnoreCase)) continue;
            var valor = linha[(eq + 1)..].Trim();
            int fim = valor.IndexOfAny(new[] { ' ', '#', ';' });
            if (fim > 0) valor = valor[..fim];
            return int.TryParse(valor, out var v) ? v : null;
        }
        return null;
    }

    /// <summary>Troca (ou acrescenta) a chave, preservando todo o resto do arquivo.</summary>
    public static string Gravar(string? cfg, int valor, string chave = ChaveResolucao)
    {
        var texto = cfg ?? "";
        var quebra = texto.Contains("\r\n") || texto.Length == 0 ? "\r\n" : "\n";
        var linhas = texto.Replace("\r\n", "\n").Split('\n').ToList();
        if (linhas.Count > 0 && linhas[^1].Length == 0) linhas.RemoveAt(linhas.Count - 1);

        for (int i = 0; i < linhas.Count; i++)
        {
            var linha = linhas[i].Trim();
            if (linha.Length == 0 || linha[0] is '#' or ';' or '[') continue;
            int eq = linha.IndexOf('=');
            if (eq <= 0 || !linha[..eq].Trim().Equals(chave, StringComparison.OrdinalIgnoreCase)) continue;
            linhas[i] = $"{chave}={valor}";
            return string.Join(quebra, linhas) + quebra;
        }
        linhas.Add($"{chave}={valor}");
        return string.Join(quebra, linhas) + quebra;
    }
}
