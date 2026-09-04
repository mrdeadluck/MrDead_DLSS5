namespace Dlss5.Core;

/// <summary>O que o re2_framework_log.txt diz sobre a rodada.</summary>
/// <param name="Entrou">"REFramework entry": ele carregou.</param>
/// <param name="BypassFalhou">
/// O IntegrityCheckBypass não achou os padrões deste jogo ("Could not find conditional_jmp",
/// "second_conditional_jmp", "stack destroyer"): a checagem do jogo continua armada, e o
/// próprio REFramework, que mexe na memória do jogo, é quem a dispara.
/// </param>
/// <param name="Caiu">"Exception occurred": o REFramework registrou o crash e gravou o dump.</param>
/// <param name="CaiuDentroDele">O endereço da exceção está no próprio dinput8.dll (_storage_\dinput8.dll).</param>
/// <param name="Jogo">O nome que o bypass usou ("Scanning DD2..." → DD2).</param>
public sealed record EstadoDoReFramework(
    bool Entrou,
    bool BypassFalhou,
    bool Caiu,
    bool CaiuDentroDele,
    string? Jogo)
{
    /// <summary>
    /// O quadro do Dragon's Dogma 2: entrou, não desarmou, e o processo caiu com o crash
    /// registrado pelo próprio REFramework — é ele derrubando um jogo que aceitaria o
    /// ReShade sozinho.
    /// </summary>
    public bool DerrubouOJogo => Entrou && BypassFalhou && Caiu;
}

public static class ReFrameworkLog
{
    public static EstadoDoReFramework? Ler(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        bool entrou = text.Contains("REFramework entry", StringComparison.OrdinalIgnoreCase);
        bool falhou = text.Contains("Could not find conditional_jmp", StringComparison.OrdinalIgnoreCase)
                      || text.Contains("Could not find second_conditional_jmp", StringComparison.OrdinalIgnoreCase)
                      || text.Contains("Could not find stack destroyer", StringComparison.OrdinalIgnoreCase);
        bool caiu = text.Contains("Exception occurred:", StringComparison.OrdinalIgnoreCase);
        bool dentro = caiu && text.Contains("_storage_\\dinput8.dll", StringComparison.OrdinalIgnoreCase);

        string? jogo = null;
        var m = System.Text.RegularExpressions.Regex.Match(text, @"\[IntegrityCheckBypass\]: Scanning (\w+)\.\.\.");
        if (m.Success) jogo = m.Groups[1].Value;

        return new EstadoDoReFramework(entrou, falhou, caiu, dentro, jogo);
    }
}
