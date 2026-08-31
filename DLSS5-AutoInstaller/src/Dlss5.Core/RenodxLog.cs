using System.Text.RegularExpressions;

namespace Dlss5.Core;

/// <summary>O que o addon do RenoDX registrou no ReShade.log.</summary>
/// <param name="Ativo">Neural Rendering entrou de fato na imagem.</param>
/// <param name="Avaliacoes">Frames processados, quando o log traz a contagem.</param>
/// <param name="HooksSemUso">Hooks instalados e nenhuma chamada de DLSS interceptada.</param>
/// <param name="AssinaturaRecusada">0xBAD00007: override ausente ou PC sem reiniciar.</param>
public sealed record RenodxStatus(bool Ativo, int Avaliacoes, bool HooksSemUso, bool AssinaturaRecusada)
{
    public string Resumo => AssinaturaRecusada
        ? "O NGX recusou a runtime (0xBAD00007): falta o override no registro ou falta reiniciar o PC."
        : Ativo
            ? $"Neural Rendering ATIVO — {Avaliacoes} avaliação(ões) bem-sucedida(s) registradas."
            : HooksSemUso
                ? "Hooks instalados, mas nenhuma chamada de DLSS foi interceptada."
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
/// </summary>
public static class RenodxLog
{
    // "inline feature 18 evaluation succeeded (count=60, ...)" — a prova de que a rede
    // rodou sobre a imagem, com quantas vezes.
    private static readonly Regex Avaliacao = new(
        @"feature\s+18\s+evaluation\s+succeeded\s*\(count=(\d+)", RegexOptions.IgnoreCase);

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

        return new RenodxStatus(ativo, avaliacoes, hooksSemUso, recusada);
    }
}
