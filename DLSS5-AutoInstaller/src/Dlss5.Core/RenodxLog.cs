using System.Globalization;
using System.Text.RegularExpressions;

namespace Dlss5.Core;

/// <summary>O que o addon do RenoDX registrou no ReShade.log.</summary>
/// <param name="Ativo">Neural Rendering entrou de fato na imagem.</param>
/// <param name="Avaliacoes">Frames processados, quando o log traz a contagem.</param>
/// <param name="HooksSemUso">Hooks instalados e nenhuma chamada de DLSS interceptada.</param>
/// <param name="AssinaturaRecusada">0xBAD00007: override ausente ou PC sem reiniciar.</param>
/// <param name="HooksInstalados">O addon chegou a pendurar os ganchos no NGX.</param>
/// <param name="CriouFeature">O jogo chegou a criar um DLSS que o addon interceptou.</param>
/// <param name="EnableHooks">Valor de EnableHooks que o addon disse estar usando (0, 1 ou 2).</param>
/// <param name="Streamline">O jogo carrega o Streamline da NVIDIA (sl.*.dll).</param>
/// <param name="PedeStreamlineHooks">O próprio addon pediu EnableHooks=1 para este jogo.</param>
/// <param name="SegundosAteDialogo">
/// Quanto tempo depois de o runtime do ReShade subir o jogo abriu um diálogo do Windows
/// (a tela de erro do próprio jogo). Nulo quando não houve diálogo.
/// </param>
/// <param name="Encerrou">O log termina com o processo saindo ("Exiting").</param>
/// <param name="CriouDevice">O jogo chegou a criar o device de vídeo (D3D11/D3D12).</param>
/// <param name="CriouSwapchain">
/// O jogo chegou a criar a swapchain — é ela que dá a imagem na tela e o runtime do
/// ReShade. Sem swapchain o jogo morreu antes de ter janela.
/// </param>
/// <param name="FrameGeneration">
/// O jogo ligou a geração de quadros (DLSSG, feature 11). O addon anuncia no log que
/// NÃO encosta nela: os quadros gerados saem sem o Neural Rendering.
/// </param>
/// <param name="RayReconstruction">
/// O DLSS do jogo é o Ray Reconstruction (DLSSD, feature 13) e o NR entrou depois dele
/// ("feature 18 created ... after DLSSD/RR"). Alan Wake 2: o log diz sucesso em cada
/// quadro e a imagem não muda com F6 — a saída do RR passa ainda por toda a
/// pós-produção do jogo antes da tela.
/// </param>
public sealed record RenodxStatus(
    bool Ativo,
    int Avaliacoes,
    bool HooksSemUso,
    bool AssinaturaRecusada,
    bool HooksInstalados = false,
    bool CriouFeature = false,
    int? EnableHooks = null,
    bool Streamline = false,
    bool PedeStreamlineHooks = false,
    double? SegundosAteDialogo = null,
    bool Encerrou = false,
    bool CriouDevice = false,
    bool CriouSwapchain = false,
    bool FrameGeneration = false,
    bool RayReconstruction = false)
{
    /// <summary>
    /// O jogo abriu a própria tela de erro antes de criar qualquer DLSS. Nesse quadro o
    /// gancho de NGX do RenoDX nem chegou a ser chamado — o suspeito é o que roda antes
    /// dele: o ReShade em si, ou o jogo nesta máquina.
    /// </summary>
    public bool CaiuAntesDoDlss => SegundosAteDialogo is not null && !CriouFeature && !Ativo;

    /// <summary>
    /// A assinatura do MGS V: o jogo criou o device de vídeo, os addons carregaram, e o
    /// processo saiu limpo — sem swapchain, sem janela, sem tela de erro. Não é travamento
    /// (travamento deixa diálogo ou log truncado): é o jogo se fechando por decisão
    /// própria, o que a proteção anti-adulteração da Fox Engine faz ao achar uma DLL
    /// estranha pendurada no D3D11/DXGI.
    /// </summary>
    public bool FechouSemJanela =>
        Encerrou && CriouDevice && !CriouSwapchain && SegundosAteDialogo is null && !Ativo;


    public string Resumo => AssinaturaRecusada
        ? "O NGX recusou a runtime (0xBAD00007): falta o override no registro ou falta reiniciar o PC."
        : FechouSemJanela
        ? "O jogo criou o device de vídeo, carregou os addons e SAIU sozinho, sem chegar a criar a " +
          "swapchain (a janela). Não houve travamento nem tela de erro — foi o jogo que se fechou."
        : Ativo
            ? $"Neural Rendering ATIVO — {Avaliacoes} avaliação(ões) bem-sucedida(s) registradas."
            : CaiuAntesDoDlss
                ? $"O jogo abriu a tela de erro {SegundosAteDialogo!.Value.ToString("0.0", CultureInfo.InvariantCulture)} s " +
                  "depois de o runtime do ReShade subir, sem nunca criar o DLSS (nenhum \"feature create\" no log): " +
                  "o travamento acontece ANTES de o RenoDX interceptar qualquer coisa."
                : HooksSemUso
                    ? "Hooks instalados, mas nenhuma chamada de DLSS foi interceptada."
                    : PedeStreamlineHooks
                        ? "O addon pediu EnableHooks=1: este jogo entrega depth e motion vectors pelo Streamline, fora do bloco NGX que o modo 2 enxerga."
                        : CriouFeature
                            ? "O jogo criou o DLSS e o addon interceptou; ainda sem avaliação de Neural Rendering no log."
                            : Encerrou && HooksInstalados
                                ? "O jogo fechou sem nunca criar o DLSS (ganchos instalados, nenhum \"feature create\" no log)."
                                : "Addon carregado; ainda sem avaliação de Neural Rendering no log.";
}

/// <summary>
/// Lê o resultado do RenoDX no ReShade.log.
///
/// Sem isto o programa consegue dizer que os arquivos estão no lugar, mas não se o DLSS 5
/// chegou a rodar — e essa é a única pergunta que interessa. O caminho direto do RenoDX
/// (D3D12 com DLSS nativo) não gera dlss5-feed.log nenhum e deixa a aba Início do ReShade
/// vazia de propósito, então "não vejo nada acontecendo" é exatamente o que se espera ver
/// numa instalação que está funcionando.
///
/// O log também conta ONDE o jogo caiu. O RE9 caiu três vezes com a mesma assinatura:
/// runtime do ReShade recriado para a resolução final, 1 a 3 segundos de silêncio, e a
/// tela de erro do próprio jogo subindo (um diálogo do Windows, classe DirectUIHWND, que
/// o ReShade registra ao interceptar RegisterClassExW) — sem nenhum "feature create"
/// antes. Isso tira o gancho de DLSS da lista de suspeitos, e é o que a leitura devolve.
/// </summary>
public static class RenodxLog
{
    // "inline feature 18 evaluation succeeded (count=60, ...)" — a prova de que a rede
    // rodou sobre a imagem, com quantas vezes.
    private static readonly Regex Avaliacao = new(
        @"feature\s+18\s+evaluation\s+succeeded\s*\(count=(\d+)", RegexOptions.IgnoreCase);

    // "| EnableHooks=2: NGX hooks only" / "SAFE MODE: EnableHooks=0, all hooks off"
    private static readonly Regex Hooks = new(@"EnableHooks=(\d)\s*[:,]", RegexOptions.IgnoreCase);

    // Cada linha do ReShade.log começa com HH:MM:SS:mmm.
    private static readonly Regex Hora = new(@"^(\d{2}):(\d{2}):(\d{2}):(\d{3})");

    public static RenodxStatus? Ler(string? logText)
    {
        if (string.IsNullOrWhiteSpace(logText)) return null;
        if (!logText.Contains("DLSS 5 Neural Rendering", StringComparison.OrdinalIgnoreCase) &&
            !logText.Contains("DLSS5 Generic", StringComparison.OrdinalIgnoreCase))
            return null;

        int avaliacoes = 0;
        foreach (Match m in Avaliacao.Matches(logText))
            if (int.TryParse(m.Groups[1].Value, out var n) && n > avaliacoes) avaliacoes = n;

        bool ativo = avaliacoes > 0
                     || logText.Contains("NR INJECTED", StringComparison.OrdinalIgnoreCase)
                     || logText.Contains("Neural Rendering is live", StringComparison.OrdinalIgnoreCase);

        bool hooksSemUso = !ativo &&
                           (logText.Contains("NO DLSS CREATE SEEN", StringComparison.OrdinalIgnoreCase) ||
                            logText.Contains("HOOKS ARMED", StringComparison.OrdinalIgnoreCase));

        bool recusada = logText.Contains("0xBAD00007", StringComparison.OrdinalIgnoreCase);

        bool hooksInstalados = logText.Contains("NGX hooks installed", StringComparison.OrdinalIgnoreCase);
        bool criouFeature = logText.Contains("feature create intercepted", StringComparison.OrdinalIgnoreCase);

        int? enableHooks = null;
        var h = Hooks.Match(logText);
        if (h.Success && int.TryParse(h.Groups[1].Value, out var eh)) enableHooks = eh;

        bool streamline = logText.Contains("sl.interposer.dll", StringComparison.OrdinalIgnoreCase)
                          || logText.Contains("sl.common.dll", StringComparison.OrdinalIgnoreCase)
                          || logText.Contains("sl.dlss.dll", StringComparison.OrdinalIgnoreCase)
                          || logText.Contains("Streamline hooks installed", StringComparison.OrdinalIgnoreCase);

        bool pedeStreamline = !ativo &&
                              logText.Contains("set EnableHooks=1", StringComparison.OrdinalIgnoreCase);

        bool encerrou = logText.Contains("| Exiting", StringComparison.OrdinalIgnoreCase);

        bool criouDevice = logText.Contains("D3D11CreateDevice", StringComparison.OrdinalIgnoreCase)
                           || logText.Contains("D3D12CreateDevice", StringComparison.OrdinalIgnoreCase)
                           || logText.Contains("vkCreateDevice", StringComparison.OrdinalIgnoreCase);

        // "::CreateSwapChain" com os dois-pontos duplos casa só o método da interface —
        // "D3D11CreateDeviceAndSwapChain" também contém a palavra, e nesse jogo ele é
        // chamado com ppSwapChain nulo, ou seja, sem criar swapchain nenhuma.
        bool criouSwapchain = logText.Contains("::CreateSwapChain", StringComparison.OrdinalIgnoreCase)
                              || logText.Contains("CreateSwapChainForHwnd", StringComparison.OrdinalIgnoreCase)
                              || logText.Contains("CreateSwapChainForCoreWindow", StringComparison.OrdinalIgnoreCase)
                              || logText.Contains("vkCreateSwapchainKHR", StringComparison.OrdinalIgnoreCase)
                              || logText.Contains("Recreated runtime environment", StringComparison.OrdinalIgnoreCase);

        // "feature 11 (DLSSG/FrameGeneration)": o addon diz, com todas as letras, que
        // pula o NR nos quadros gerados. Quem está com geração de quadros ligada vê o
        // efeito em metade dos quadros — e é aí que nasce o "não mudou nada".
        bool frameGen = logText.Contains("DLSSG/FrameGeneration", StringComparison.OrdinalIgnoreCase);
        // "feature=13 (DLSSD/RR)" e "feature 18 created ... after DLSSD/RR": o NR está
        // pendurado no Ray Reconstruction, não no DLSS comum.
        bool rayRec = logText.Contains("after DLSSD/RR", StringComparison.OrdinalIgnoreCase)
                      || logText.Contains("feature=13 (DLSSD/RR)", StringComparison.OrdinalIgnoreCase);

        return new RenodxStatus(ativo, avaliacoes, hooksSemUso, recusada,
            hooksInstalados, criouFeature, enableHooks, streamline, pedeStreamline,
            SegundosAteDialogo(logText), encerrou, criouDevice, criouSwapchain, frameGen, rayRec);
    }

    /// <summary>
    /// Tempo entre o runtime do ReShade subir pela última vez e o jogo registrar a janela
    /// de diálogo do Windows (DirectUIHWND). Nulo sem diálogo. Quando o runtime nunca
    /// subiu, conta a partir do swapchain ou do registro do addon.
    /// </summary>
    public static double? SegundosAteDialogo(string logText)
    {
        var linhas = logText.Replace("\r\n", "\n").Split('\n');

        int dialogo = -1;
        for (int i = 0; i < linhas.Length; i++)
        {
            if (linhas[i].Contains("RegisterClassExW", StringComparison.OrdinalIgnoreCase) &&
                linhas[i].Contains("DirectUIHWND", StringComparison.OrdinalIgnoreCase))
            {
                dialogo = i;
                break;
            }
        }
        if (dialogo < 0) return null;

        int referencia = UltimaAntes(linhas, dialogo, "Recreated runtime environment");
        if (referencia < 0) referencia = UltimaAntes(linhas, dialogo, "CreateSwapChain");
        if (referencia < 0) referencia = UltimaAntes(linhas, dialogo, "Registered add-on");
        if (referencia < 0) return null;

        var t0 = HoraDe(linhas[referencia]);
        var t1 = HoraDe(linhas[dialogo]);
        if (t0 is null || t1 is null) return null;

        var delta = (t1.Value - t0.Value).TotalSeconds;
        if (delta < 0) delta += 24 * 3600;   // virou o dia entre as duas linhas
        return Math.Round(delta, 1);
    }

    private static int UltimaAntes(string[] linhas, int limite, string marca)
    {
        for (int i = limite - 1; i >= 0; i--)
            if (linhas[i].Contains(marca, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static TimeSpan? HoraDe(string linha)
    {
        var m = Hora.Match(linha);
        if (!m.Success) return null;
        return new TimeSpan(0,
            int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture));
    }
}
