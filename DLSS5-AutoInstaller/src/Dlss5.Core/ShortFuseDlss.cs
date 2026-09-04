using System.Text;

namespace Dlss5.Core;

/// <summary>
/// O renodx-dlss.addon64 do ShortFuse ("RenoDX DLSS"): um addon só que fabrica a chamada
/// de DLSS sozinho — com ou sem DLSS nativo, 64-bit, D3D9/D3D11/D3D12 — e roda o Neural
/// Rendering em 1 a 10 passadas sequenciais ("Pass Count"). É o "x2" que a comunidade
/// mostra: a segunda passada consome a saída da primeira. Substitui o par Krish + Feeder e
/// não convive com nenhum dos dois na mesma pasta. Não está no GitHub do ShortFuse: sai no
/// #DLSS5 do Discord do RenoDX e é espelhado em RankFTW/rhi-repo.
/// </summary>
public static class ShortFuseDlss
{
    public const string Addon = "renodx-dlss.addon64";
    /// <summary>Nome com que o ReShade registra o addon ("Registered add-on \"RenoDX DLSS\"").</summary>
    public const string NomeNoReShade = "RenoDX DLSS";
    /// <summary>Seção do ReShade.ini onde o addon guarda as opções (o global_name do RenoDX).</summary>
    public const string Secao = "RENODX-DLSS";
    public const string ChavePassadas = "DirectNeuralRenderingPassCount";
    public const int PassesMin = 1;
    public const int PassesMax = 10;
    public const int PassesPadrao = 2;

    public static int Limitar(int passes) => Math.Clamp(passes, PassesMin, PassesMax);

    public static string Rotulo(NeuralEngine e) => e switch
    {
        NeuralEngine.RenodxDlssShortFuse => "RenoDX DLSS (ShortFuse) — passadas múltiplas de Neural Rendering",
        _ => "RenoDX DLSS5 (Krish) + Feeder — uma passada (padrão até aqui)",
    };

    public static string AvisoDoPlano(int passes) =>
        $"Motor ShortFuse: o {Addon} substitui o renodx-dlss5 e o Feeder nesta pasta (os dois saem, com backup). " +
        $"Neural Rendering em {passes} passada(s): cada passada custa o mesmo que a primeira em GPU e VRAM. " +
        "Na primeira abertura o addon se adiciona a [ADDON] LoadFromDllMain no ReShade.ini e pede para " +
        "reiniciar o jogo uma vez; o ini gerado já traz a linha, então normalmente ele não pede. " +
        "É beta e fechado: se um jogo cair, volte ao motor Krish + Feeder na tela de detecção.";

    public static string PassoManual(int passes) =>
        $"Abra o jogo, aperte a tecla do painel do ReShade e vá na aba \"RenoDX DLSS\" (não é a \"DLSS 5 Neural " +
        "Rendering\" do addon antigo). A linha verde de estado deve dizer \"Active\". Em Advanced, \"Pass Count\" " +
        $"tem que estar em {passes} — foi o que o instalador gravou no ReShade.ini; se o addon mostrar outro valor, " +
        "ajuste ali (a mudança vale na hora e zera o histórico do NR). \"Require DLSS\" em Auto funciona com o " +
        "DLSS do jogo ligado ou desligado. Se o jogo travar, baixe o Pass Count para 1 antes de culpar o resto.";

    /// <summary>Valor gravado em [RENODX-DLSS] DirectNeuralRenderingPassCount, ou nulo.</summary>
    public static int? LerPassadas(string? ini)
    {
        if (string.IsNullOrWhiteSpace(ini)) return null;
        bool dentro = false;
        foreach (var bruta in ini.Replace("\r\n", "\n").Split('\n'))
        {
            var linha = bruta.Trim();
            if (linha.StartsWith('['))
            {
                dentro = linha.Equals("[" + Secao + "]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!dentro) continue;
            int eq = linha.IndexOf('=');
            if (eq < 0) continue;
            if (!linha[..eq].Trim().Equals(ChavePassadas, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(linha[(eq + 1)..].Trim(), out var v) ? v : null;
        }
        return null;
    }

    /// <summary>Bloco de ini que o instalador grava para o addon.</summary>
    public static void Gravar(StringBuilder sb, int passes)
    {
        sb.AppendLine("[" + Secao + "]");
        sb.AppendLine($"{ChavePassadas}={Limitar(passes)}");
    }
}

/// <summary>O que o RenoDX DLSS do ShortFuse registrou no ReShade.log.</summary>
public sealed record ShortFuseStatus(
    bool Registrado, bool Anexou, bool Avaliou, bool Falhou, bool PedeReinicio, string Falha)
{
    public CheckResult Checkpoint14(int passesPedidas, bool reinicioPendente)
    {
        const string titulo = "DLSS 5 aplicado na imagem (RenoDX DLSS do ShortFuse)";
        if (!Registrado)
            return new CheckResult(14, titulo, CheckStatus.Fail,
                $"O ReShade não registrou o addon \"{ShortFuseDlss.NomeNoReShade}\".",
                $"Confira se o {ShortFuseDlss.Addon} está na pasta do exe e se o AddonPath do ReShade.ini aponta para ela.");
        if (Falhou)
            return new CheckResult(14, titulo, CheckStatus.Fail, Falha,
                "Confira o nvngx_dlssnr.dll ao lado do exe (item 18) e o override no registro (item 1). Se persistir, " +
                "baixe o Pass Count para 1 e teste de novo.");
        if (Avaliou)
        {
            var detalhe = $"O addon avaliou o Neural Rendering na imagem (\"DLSS-NR source evaluation completed\"), com {passesPedidas} passada(s) pedida(s) no ini.";
            if (reinicioPendente)
                return new CheckResult(14, titulo, CheckStatus.Warning,
                    detalhe + " Porém o PC não foi reiniciado desde o override: esse \"ativo\" pode ser vazio.",
                    "Se a imagem não mudou, reinicie o PC quando puder e teste de novo.");
            return new CheckResult(14, titulo, CheckStatus.Pass, detalhe,
                "Compare parado: painel RenoDX DLSS, Options Mode DLSS-NR. Cada passada a mais custa o mesmo que a primeira.");
        }
        if (PedeReinicio)
            return new CheckResult(14, titulo, CheckStatus.Warning,
                "Primeira abertura: o addon se adicionou a LoadFromDllMain no ReShade.ini e pediu reinício do jogo. Ainda não avaliou nada.",
                "Feche e abra o jogo de novo; depois clique em Verificar de novo.");
        return new CheckResult(14, titulo, Anexou ? CheckStatus.Warning : CheckStatus.Fail,
            Anexou ? "O addon carregou (\"RenoDX DLSS attached\"), mas o log ainda não tem nenhuma avaliação de Neural Rendering."
                   : "O addon foi registrado pelo ReShade, mas nunca chegou a anexar (\"attached\").",
            "Abra o jogo, jogue alguns segundos e verifique de novo. No painel, a linha de estado deve ficar verde (\"Active\").");
    }
}

public static class ShortFuseLog
{
    public static ShortFuseStatus Ler(string? text)
    {
        text ??= "";
        bool registrado = text.Contains("Registered add-on \"" + ShortFuseDlss.NomeNoReShade + "\"", StringComparison.OrdinalIgnoreCase)
                          || text.Contains("RenoDX DLSS attached", StringComparison.OrdinalIgnoreCase)
                          || text.Contains("RenoDX DLSS controller", StringComparison.OrdinalIgnoreCase);
        bool anexou = text.Contains("RenoDX DLSS attached", StringComparison.OrdinalIgnoreCase)
                      || text.Contains("RenoDX DLSS first present", StringComparison.OrdinalIgnoreCase);
        bool avaliou = text.Contains("DLSS-NR source evaluation completed", StringComparison.OrdinalIgnoreCase);
        string falha = "";
        if (text.Contains("could not attach the direct nvngx_dlssnr.dll runtime", StringComparison.OrdinalIgnoreCase))
            falha = "O addon não conseguiu anexar o nvngx_dlssnr.dll (\"could not attach the direct nvngx_dlssnr.dll runtime\").";
        else if (text.Contains("Neural Rendering device binding failed", StringComparison.OrdinalIgnoreCase))
            falha = "O addon não conseguiu ligar o Neural Rendering ao device do jogo (\"Neural Rendering device binding failed\").";
        else if (text.Contains("DLSS-NR source evaluation failed", StringComparison.OrdinalIgnoreCase) && !avaliou)
            falha = "A avaliação do Neural Rendering falhou (\"DLSS-NR source evaluation failed\") e nenhuma outra completou.";
        bool pedeReinicio = text.Contains("LoadFromDllMain in ReShade.ini. Restart required", StringComparison.OrdinalIgnoreCase);
        return new ShortFuseStatus(registrado, anexou, avaliou, falha.Length > 0, pedeReinicio, falha);
    }
}
