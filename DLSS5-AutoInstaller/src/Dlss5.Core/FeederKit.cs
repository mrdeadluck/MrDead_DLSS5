using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Dlss5.Core;

/// <summary>
/// A versão do Feeder (dlss5-feed.addon64 / .addon32 / dlss5-feed-host64.exe) — no kit e no jogo.
///
/// O que ensinou isto: Mafia Definitive Edition, Crysis, Titanfall 2 e Metro Exodus, todos
/// caindo ao trocar uma configuração dentro do jogo com o DLSS 5 ligado — e rodando sem ele.
/// Trocar resolução, tela cheia ou qualidade recria a swapchain; o ReShade destrói e recria o
/// runtime; e o Feeder 0.5.0 que o kit trazia derrubava a sessão INTEIRA nessa hora (device
/// D3D12 privado incluído) e criava a feature de novo no exato instante em que o addon do
/// RenoDX rearma os hooks. O próprio autor do Feeder descreve o resultado: "EXEC em 0x0
/// dentro do addon, às vezes fatal numa thread estranha". O 0.12.0 mantém texturas e feature
/// vivas quando o runtime é recriado, só recria a feature (e segura a criação pelo
/// create_delay), tenta até três vezes e, se a criação falhar, fica com a feature antiga —
/// o feed se desliga em vez de levar o jogo junto.
/// </summary>
public static class FeederKit
{
    public const string Addon64 = "dlss5-feed.addon64";
    public const string Addon32 = "dlss5-feed.addon32";
    public const string Host64 = "dlss5-feed-host64.exe";
    public const string Shader = "DLSS5_Feed.fx";
    /// <summary>Desde 0.11.0-beta.2 o Feeder grava um minidump ao lado do log quando o jogo cai.</summary>
    public const string CrashDump = "dlss5-feed-crash.dmp";

    /// <summary>A versão que o kit deve trazer (é a que o workflow trocar-feeder baixa).</summary>
    public const string VersaoDoKit = "0.12.0";

    /// <summary>Abaixo disto a recriação do runtime derruba o jogo.</summary>
    public static readonly Version Minima = new(0, 12, 0);

    // "dlss5-feed 0.5.0 (built Aug 30 2026 12:38:05) attached." — primeira linha do dlss5-feed.log.
    private static readonly Regex Banner = new(
        @"dlss5-feed\s+(\d+\.\d+(?:\.\d+)?(?:-[\w.]+)?)\s*\(built", RegexOptions.IgnoreCase);

    /// <summary>A versão que o log do Feeder anuncia na primeira linha; null se o log não a traz.</summary>
    public static string? VersaoNoLog(string? feedLog)
    {
        if (string.IsNullOrWhiteSpace(feedLog)) return null;
        var m = Banner.Match(feedLog);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// A versão gravada no recurso VERSIONINFO do arquivo ("0.12.0.0" → "0.12.0"); null quando
    /// o arquivo não tem versão — o 0.5.0 não tinha.
    /// </summary>
    public static string? VersaoDoArquivo(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var bruta = info.FileVersion ?? info.ProductVersion;
            if (string.IsNullOrWhiteSpace(bruta)) return null;
            if (!Version.TryParse(Numerica(bruta), out var v)) return null;
            return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sem versão, ou abaixo da mínima: é o Feeder que cai na troca de configuração.</summary>
    public static bool Antiga(string? versao)
    {
        if (string.IsNullOrWhiteSpace(versao)) return true;
        if (!Version.TryParse(Numerica(versao), out var v)) return true;
        // Version("0.12") compara como 0.12.-1: normaliza para três casas.
        var tres = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        return tres < Minima;
    }

    /// <summary>"0.10.0-beta.3" → "0.10.0"; "0.12.0.0" → "0.12.0.0".</summary>
    private static string Numerica(string s)
    {
        var m = Regex.Match(s.Trim(), @"^v?(\d+(?:\.\d+){1,3})");
        return m.Success ? m.Groups[1].Value : s.Trim();
    }

    public const string PorQueCai =
        "Trocar resolução, tela cheia ou qualidade dentro do jogo recria a swapchain, e o ReShade recria o " +
        "runtime; o Feeder antigo (0.5.0) derrubava a sessão inteira nessa hora e criava a feature de novo " +
        "bem quando o addon do RenoDX rearma os hooks — é aí que o jogo cai. O 0.12.0 mantém a feature viva " +
        "na recriação, tenta de novo com calma e, se falhar, fica com a anterior.";

    /// <summary>Aviso do plano: o kit apontado ainda traz o Feeder antigo.</summary>
    public static string AvisoKitAntigo(string? versaoNoKit) =>
        $"{Addon64} do kit é o {versaoNoKit ?? "0.5.0 (sem versão gravada)"}; o pacote atual traz o {VersaoDoKit}. " +
        PorQueCai + " Baixe o pacote novo (ou o zip só do Feeder) e aponte o kit para ele antes de instalar.";

    /// <summary>Verificação: o jogo rodou com o Feeder antigo.</summary>
    public static string LeituraJogoAntigo(string versaoNoJogo) =>
        $"O jogo rodou com o Feeder {versaoNoJogo}; o kit atual traz o {VersaoDoKit}. " + PorQueCai;

    public const string ComoAtualizar =
        "Aponte o kit para o pacote novo e clique em Instalar de novo (o programa vê que os arquivos " +
        "instalados diferem dos do kit e troca só o que mudou). Enquanto isso: mude as configurações do jogo " +
        "ANTES de marcar os efeitos, ou com o DLSS 5 desligado.";
}
