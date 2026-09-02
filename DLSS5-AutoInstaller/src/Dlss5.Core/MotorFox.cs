namespace Dlss5.Core;

/// <summary>
/// O que se sabe da Fox Engine (Metal Gear Solid V: Ground Zeroes e The Phantom Pain).
///
/// O mecanismo, confirmado pelo Discord do RenoDX: a Fox Engine confere se as próprias
/// interfaces D3D11 foram enganchadas — a função é fox::gr::dg::CheckModuleHook. O ReShade
/// engancha o ID3D11DeviceContext, a checagem acusa, e o jogo SE FECHA de propósito antes
/// de criar a swapchain. Parece o ReShade travando; é o jogo se encerrando. É exatamente a
/// assinatura do ReShade.log do Ground Zeroes: device criado, addons registrados, 27
/// contextos adiados, saída limpa. E o teste "só o ReShade" confirmou: sem addon nenhum
/// o jogo também fecha. Não há nome de DLL que contorne isso — a checagem olha o gancho,
/// não o arquivo.
///
/// A cura é um patch no executável: o MGSV-ReShade-AntiHook-Patcher (Discord do RenoDX)
/// desvia o resultado do CheckModuleHook. Ele só aceita o mgsvtpp.exe 1.0.15.4 (Steam,
/// inglês), cria mgsvtpp.exe.anti-hook-backup e não toca em executável desconhecido.
/// Com o patch, o ReShade entra como dxgi.dll — o nome comum.
/// </summary>
public static class MotorFox
{
    /// <summary>Executáveis conhecidos da Fox Engine que carregam a checagem.</summary>
    private static readonly string[] Executaveis =
    {
        "mgsvtpp", "mgsvgz", "MgsGroundZeroes",
    };

    /// <summary>Sufixo do backup que o patcher cria ao lado do exe.</summary>
    public const string SufixoDoBackup = ".anti-hook-backup";

    public const string NomeDoPatcher = "MGSV-ReShade-AntiHook-Patcher.exe";

    public static bool EhFoxEngine(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        var exe = Path.GetFileNameWithoutExtension(exePath);
        return Executaveis.Any(n => n.Equals(exe, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>O patcher só cobre o The Phantom Pain (mgsvtpp.exe 1.0.15.4, Steam, inglês).</summary>
    public static bool PatcherCobre(string? exePath) =>
        !string.IsNullOrWhiteSpace(exePath) &&
        Path.GetFileNameWithoutExtension(exePath).Equals("mgsvtpp", StringComparison.OrdinalIgnoreCase);

    /// <summary>Caminho do backup que o patcher deixa: a prova de que o exe foi remendado.</summary>
    public static string CaminhoDoBackup(string exePath) => exePath + SufixoDoBackup;

    /// <summary>O executável já passou pelo patcher (o backup dele está ao lado).</summary>
    public static bool PatchAplicado(string? exePath) =>
        !string.IsNullOrWhiteSpace(exePath) && File.Exists(CaminhoDoBackup(exePath));

    /// <summary>Frase da tela de detecção.</summary>
    public const string Aviso =
        "Fox Engine (Metal Gear Solid V): o jogo confere se as interfaces D3D11 foram enganchadas " +
        "(fox::gr::dg::CheckModuleHook) e, como o ReShade engancha o ID3D11DeviceContext, ele se " +
        "FECHA de propósito antes de criar a swapchain — sem mensagem, parecendo travamento. " +
        "Nenhum nome de DLL contorna isso: a checagem olha o gancho, não o arquivo.";

    /// <summary>O que fazer: o patch no executável, uma vez.</summary>
    public const string ComoAplicarOPatch =
        "Com o jogo fechado, baixe o MGSV-ReShade-AntiHook-Patcher (Discord do RenoDX, zip com o " +
        "código-fonte), ponha o " + NomeDoPatcher + " ao lado do mgsvtpp.exe e rode UMA vez. Ele desvia o " +
        "resultado do CheckModuleHook, cria o mgsvtpp.exe" + SufixoDoBackup + " e só aceita a versão 1.0.15.4 " +
        "(Steam, inglês) — executável diferente não é tocado. O Windows pode mostrar o SmartScreen " +
        "por ele não ser assinado. Depois gere o plano de novo: com o backup na pasta, o programa " +
        "libera a instalação e o ReShade entra como dxgi.dll.";

    /// <summary>Ground Zeroes não tem patcher conhecido.</summary>
    public const string SemPatcherParaGz =
        "O patcher conhecido cobre só o The Phantom Pain (mgsvtpp.exe 1.0.15.4). Para o Ground Zeroes " +
        "não há patch publicado: o CheckModuleHook fecha o jogo com qualquer ReShade, e o programa não " +
        "vai instalar para você testar de novo o que já está provado. Se surgir um patcher para o " +
        "MgsGroundZeroes.exe, o backup .anti-hook-backup ao lado do exe libera a instalação.";
}
