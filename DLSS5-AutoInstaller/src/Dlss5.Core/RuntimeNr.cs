using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Dlss5.Core;

/// <summary>Um build conhecido do nvngx_dlssnr.dll: o que é e em que placa roda.</summary>
/// <param name="Nome">Como o RHI o chama.</param>
/// <param name="SoRtx50">Só roda na série RTX 50 (Blackwell).</param>
/// <param name="Remendo">Bytes trocados sobre o original, sem ser um build do ShortFuse.</param>
public sealed record BuildNr(string Nome, string Sha256, bool SoRtx50, bool Remendo = false)
{
    public string Leitura => Remendo
        ? $"{Nome} — um remendo do original, não é build do ShortFuse. O próprio addon o marca no log " +
          "como \"custom runtime accepted; untested build, NR failures may be specific to it\". Em RTX 40/30/20 " +
          "ele diz que aplicou e não muda nada na imagem."
        : SoRtx50
            ? $"{Nome} — o original que veio no NBA 2K27. Só roda em RTX 50; nas outras séries não aplica nada."
            : $"{Nome} — build do ShortFuse para RTX 20, 30, 40 e 50. É o que o RHI instala.";
}

/// <summary>
/// Identifica o nvngx_dlssnr.dll pelo hash, porque o nome e a versão não contam a história.
///
/// O caso que ensinou isto: o RE9 com "ACTIVE — NR INJECTED", 7633 quadros processados
/// "com sucesso", e a imagem sem mudar um fio — em qualquer estilo, intensidade ou modo de
/// gancho. O arquivo do kit tinha o MESMO tamanho do original da NBA 2K27 (que só roda em
/// RTX 50) e bytes diferentes: um remendo. O addon avisava no log, em letra miúda, "custom
/// runtime accepted; untested build". Na RTX 4070 Ti ele rodava, dizia OK e não desenhava
/// nada. O RHI — que funcionou em outro PC — instala o build do ShortFuse (310.8.SF-v2),
/// compilado nove minutos antes do addon v4.1.5, para RTX 20 a 50.
///
/// Os hashes abaixo são dos zips publicados em github.com/RankFTW/rhi-repo (dlssnr-*).
/// </summary>
public static class RuntimeNr
{
    public const string Arquivo = "nvngx_dlssnr.dll";

    /// <summary>O que o RHI instala por padrão (primeira entrada do manifesto dele).</summary>
    public const string Recomendado = "310.8.SF-v2";

    public const string UrlRecomendado =
        "https://github.com/RankFTW/rhi-repo/releases/download/dlssnr-310.8.SF-v2/nvngx_dlssnr_310.8.SF-v2.zip";

    public static readonly IReadOnlyList<BuildNr> Conhecidos = new[]
    {
        new BuildNr("310.8.SF-v2", "6eb209e764f39872625debd6abaf45e2bb6322f6f270f781f70c059ae30b3927", SoRtx50: false),
        new BuildNr("310.8.SF",    "4c5bd1171c7336b4b04fb394de51da285ab6ead6f922d7afdec163f71c319d74", SoRtx50: false),
        new BuildNr("310.8.0",     "e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e", SoRtx50: true),
        new BuildNr("310.8.0 remendado (kit antigo)",
                                   "368911e6865534edb9b82d803c1e5d3fa3292d9c832ee0a9ee3444ac58c96b82", SoRtx50: true, Remendo: true),
    };

    /// <summary>O build correspondente ao hash, ou nulo se for desconhecido.</summary>
    public static BuildNr? PorHash(string? sha256) =>
        string.IsNullOrWhiteSpace(sha256)
            ? null
            : Conhecidos.FirstOrDefault(b => b.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase));

    /// <summary>Hash do arquivo, ou nulo se não der para ler. Leva ~1 s nos 158 MB.</summary>
    public static string? HashDe(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return null;
        try
        {
            if (!File.Exists(caminho)) return null;
            using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    public static BuildNr? Identificar(string? caminho) => PorHash(HashDe(caminho));

    // "Running on NVIDIA GeForce RTX 4070 Ti Driver 616.56." — o ReShade escreve a placa
    // no log quando cria o runtime. É o único lugar de onde o programa consegue tirar isso.
    private static readonly Regex Placa = new(@"Running on NVIDIA GeForce RTX\s+(\d{2})(\d{2})", RegexOptions.IgnoreCase);

    /// <summary>Série da RTX que o ReShade.log registrou (20, 30, 40, 50), ou nulo.</summary>
    public static int? SerieRtxNoLog(string? logText)
    {
        if (string.IsNullOrEmpty(logText)) return null;
        var m = Placa.Match(logText);
        return m.Success && int.TryParse(m.Groups[1].Value, out var s) ? s : null;
    }

    /// <summary>O addon aceitou um runtime que ele não conhece.</summary>
    public static bool AddonMarcouComoDesconhecido(string? logText) =>
        logText is not null &&
        logText.Contains("custom runtime accepted", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Veredito sobre um runtime para a placa do log: (falha?, texto). Sem placa no log,
    /// o remendo e o original continuam acusados — o remendo por ser remendo, o original
    /// porque a maioria das placas não é RTX 50 e o custo de avisar à toa é baixo.
    /// </summary>
    public static (bool Falha, string Texto) Avaliar(BuildNr? build, int? serieRtx)
    {
        if (build is null)
            return (true,
                $"O {Arquivo} não é nenhum build conhecido (nem o original da NBA 2K27, nem os do ShortFuse " +
                "que o RHI instala). Sem saber o que ele é, não dá para confiar no \"ACTIVE\" do addon.");
        if (build.Remendo)
            return (true, build.Leitura);
        if (build.SoRtx50)
            return serieRtx == 50
                ? (false, build.Leitura + " A placa registrada no log é RTX 50, então ele serve aqui.")
                : (true, build.Leitura + (serieRtx is { } s ? $" A placa registrada no log é RTX {s}xx." : ""));
        return (false, build.Leitura);
    }

    public const string ComoTrocar =
        "Baixe " + UrlRecomendado + ", extraia o " + Arquivo + " de dentro do zip e ponha no lugar do " +
        "arquivo de mesmo nome na pasta do kit (DLSS 5 Files). Depois Instalar de novo (Atualizar), " +
        "ou copie direto por cima do que está na pasta do jogo. É o mesmo arquivo que o RHI instala.";
}
