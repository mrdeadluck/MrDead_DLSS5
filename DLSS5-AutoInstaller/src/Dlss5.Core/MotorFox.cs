namespace Dlss5.Core;

/// <summary>
/// O que se sabe da Fox Engine (Metal Gear Solid V: Ground Zeroes e The Phantom Pain).
///
/// Este motor tem proteção anti-adulteração e é o único caso, até aqui, em que o jogo
/// não trava: ele se FECHA. A prova está no ReShade.log do Ground Zeroes: o ReShade
/// carrega como d3d11.dll, o jogo cria o device de vídeo, os dois addons registram, o
/// renderizador chega a criar 27 contextos adiados — e o processo sai limpo, sem nunca
/// criar a swapchain. Nenhum "crash", nenhuma tela de erro, nenhum log truncado. Só a
/// saída. Sem os arquivos o mesmo jogo abre normalmente.
///
/// Por isso o programa para de vender esperança aqui: trocar o nome do ReShade entre
/// dxgi.dll e d3d11.dll é trocar de porta na mesma casa — a proteção olha as duas. O que
/// separa "a proteção recusa qualquer ReShade" de "são os nossos addons" é um teste só,
/// o "Testar só o ReShade" da tela de verificação, e é ele que a orientação pede.
/// </summary>
public static class MotorFox
{
    /// <summary>Executáveis conhecidos da Fox Engine que carregam a proteção.</summary>
    private static readonly string[] Executaveis =
    {
        "mgsvtpp", "mgsvgz", "MgsGroundZeroes",
    };

    public static bool EhFoxEngine(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        var exe = Path.GetFileNameWithoutExtension(exePath);
        return Executaveis.Any(n => n.Equals(exe, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Frase da tela de detecção.</summary>
    public const string Aviso =
        "Fox Engine (Metal Gear Solid V): este motor tem proteção anti-adulteração e ela olha " +
        "tanto o dxgi.dll quanto o d3d11.dll. Quando ela recusa, o jogo não trava — ele fecha " +
        "sozinho, sem mensagem nenhuma. Trocar o nome do ReShade não contorna isso.";

    /// <summary>O que fazer, na ordem, quando o jogo fecha sozinho com os arquivos.</summary>
    public const string Ladeira =
        "Antes de mexer em mais nada, rode o teste que separa as duas causas: na tela de " +
        "verificação, \"Testar só o ReShade\" (desliga os dois addons e deixa só o ReShade). " +
        "Se o jogo ABRIR, quem derruba são os addons e o conserto é aqui dentro. Se NÃO abrir, " +
        "é a proteção do jogo recusando o ReShade em si, e aí o caminho é outro: fazer o jogo " +
        "rodar em Vulkan pelo DXVK, com o ReShade entrando como camada do Vulkan em vez de DLL " +
        "na pasta — é assim que a comunidade usa ReShade no MGS V.";
}
