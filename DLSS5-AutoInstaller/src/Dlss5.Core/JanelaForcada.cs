using System.Text.RegularExpressions;

namespace Dlss5.Core;

/// <summary>
/// "Forçar o jogo em janela sem borda" para qualquer jogo 32-bit que sobe o host64.
///
/// O ReShade 5.x lia [APP] ForceWindowed do ReShade.ini sozinho; o ReShade 6 (o do kit, 6.8)
/// tirou isso do núcleo — a fonte não tem mais a string — e a mesma função virou o exemplo
/// oficial 16-swapchain_override (examples/ no repositório do crosire): um addon que lê as
/// mesmas chaves [APP] e, além de nascer o swapchain em janela (create_swapchain), devolve
/// true em set_fullscreen_state quando o jogo pede tela cheia — exatamente o
/// SetFullscreenState(TRUE) que o Enslaved grava 5 s antes de congelar no aperto de mão.
///
/// O kit compila esse exemplo no GitHub Actions (compilar-addons.yml, commit fixado em
/// swapchain-override-desejado.txt) e o instalador o copia para a pasta do jogo quando a
/// opção está marcada. Sem o addon a chave no ini é letra morta.
/// </summary>
public static class JanelaForcada
{
    public const string Addon32 = "swapchain_override.addon32";
    public const string Addon64 = "swapchain_override.addon64";

    /// <summary>O NAME que o addon exporta; é assim que o ReShade.log o registra.</summary>
    public const string NomeRegistrado = "Swap chain override";

    private static readonly Regex Registro = new(
        @"Registered add-on ""Swap chain override""", RegexOptions.IgnoreCase);

    /// <summary>O ReShade.log do jogo mostra o addon carregado.</summary>
    public static bool Registrado(string? reshadeLog)
        => !string.IsNullOrEmpty(reshadeLog) && Registro.IsMatch(reshadeLog);
}
