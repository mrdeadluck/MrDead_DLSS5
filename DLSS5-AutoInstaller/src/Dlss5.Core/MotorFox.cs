using System.Diagnostics;

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
        "mgsvmgo",   // Metal Gear Online: mesma engine, mesma checagem
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

    /// <summary>Quando o patcher está no kit: o programa roda por você.</summary>
    public const string PatchAutomatico =
        "O " + NomeDoPatcher + " está no kit: a instalação o copia para a pasta do jogo e o roda " +
        "sozinha, antes de qualquer outro arquivo. Ele só remenda o mgsvtpp.exe 1.0.15.4 (Steam, inglês) " +
        "e deixa o mgsvtpp.exe" + SufixoDoBackup + " ao lado; com executável diferente ele não faz nada, " +
        "e aí a instalação para sem tocar em mais nada. O Windows pode mostrar o SmartScreen (o patcher " +
        "não é assinado). Desinstalar não desfaz o patch — o jogo continua abrindo normalmente com ele; " +
        "para voltar ao original, renomeie o backup por cima do exe.";

    /// <summary>
    /// Quem executa o patcher. Trocável só para os testes: no Windows real é o processo mesmo.
    /// Devolve depois que o processo sai (ou do tempo limite).
    /// </summary>
    public static Func<string, string, TimeSpan, bool> Executor { get; set; } = RodarProcesso;

    private static bool RodarProcesso(string patcher, string pastaDoExe, TimeSpan limite)
    {
        var psi = new ProcessStartInfo(patcher)
        {
            WorkingDirectory = pastaDoExe,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Não consegui iniciar o patcher.");
        return proc.WaitForExit((int)limite.TotalMilliseconds);
    }

    /// <summary>
    /// Roda o patcher ao lado do exe e confere o resultado pelo backup que ele deixa.
    /// Sucesso é o backup existir — nada mais prova que o exe foi remendado.
    /// </summary>
    public static void RodarPatcher(string patcherNaPastaDoJogo, string exePath, Action<string>? log = null)
    {
        if (PatchAplicado(exePath))
        {
            log?.Invoke("Patch anti-hook já aplicado (backup presente); o patcher não roda de novo.");
            return;
        }
        var pasta = Path.GetDirectoryName(exePath)!;
        log?.Invoke($"Rodando {Path.GetFileName(patcherNaPastaDoJogo)} em {pasta}...");
        bool saiu = Executor(patcherNaPastaDoJogo, pasta, TimeSpan.FromMinutes(3));
        if (!PatchAplicado(exePath))
            throw new InvalidOperationException(
                (saiu ? "O patcher terminou" : "O patcher não terminou em 3 minutos") +
                $" e não deixou o {Path.GetFileName(CaminhoDoBackup(exePath))}: o exe não foi remendado. " +
                "Ele só aceita o mgsvtpp.exe 1.0.15.4 (Steam, inglês). Confira a versão do jogo.");
        log?.Invoke($"Patch aplicado: {Path.GetFileName(CaminhoDoBackup(exePath))} criado.");
    }

    /// <summary>Ground Zeroes não tem patcher conhecido.</summary>
    public const string SemPatcherParaGz =
        "O patcher conhecido cobre só o The Phantom Pain (mgsvtpp.exe 1.0.15.4). Para o Ground Zeroes " +
        "não há patch publicado: o CheckModuleHook fecha o jogo com qualquer ReShade, e o programa não " +
        "vai instalar para você testar de novo o que já está provado. Se surgir um patcher para o " +
        "MgsGroundZeroes.exe, o backup .anti-hook-backup ao lado do exe libera a instalação.";
}
