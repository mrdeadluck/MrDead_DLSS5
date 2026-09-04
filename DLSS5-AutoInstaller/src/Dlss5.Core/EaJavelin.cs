namespace Dlss5.Core;

/// <summary>
/// Jogos da EA sob o EA Javelin Anticheat (FC 24/25/26, Madden, Battlefield 2042/6, F1,
/// WRC...). A Steam não abre o exe do jogo: chama o EAAntiCheat.GameServiceLauncher.exe,
/// que sobe o anticheat e só então lança o jogo — e sob o anticheat nenhuma DLL estranha
/// entra, nem o dxgi.dll do ReShade. Os arquivos que o instalador põe na pasta são os
/// mesmos de sempre; o que muda é QUEM abre o jogo.
///
/// O que funciona na prática (vídeo do FC 26 e o log do próprio Live Editor): abrir o
/// jogo pelo Launcher do FC Live Editor, que troca o launcher do anticheat por um falso
/// enquanto roda (BackupAnticheat → InstallFakeAnticheat → ... → RestoreAnticheat) e o
/// devolve ao fechar. Esse contorno é do Live Editor, não deste programa: ele vale para
/// jogar offline, e a EA bane conta que entra no multiplayer assim. O programa reconhece
/// o caso, avisa, e abre o jogo pelo Launcher que o usuário apontar.
/// </summary>
public static class EaJavelin
{
    public const string Launcher = "EAAntiCheat.GameServiceLauncher.exe";

    /// <summary>O Launcher do FC Live Editor e a DLL que mora ao lado dele.</summary>
    public const string LauncherDoLiveEditor = "Launcher.exe";
    public const string DllDoLiveEditor = "FCLiveEditor.DLL";

    /// <summary>A pasta do jogo traz o launcher do EA Javelin.</summary>
    public static bool EhJavelin(string? exeFolder) =>
        !string.IsNullOrWhiteSpace(exeFolder) && File.Exists(Path.Combine(exeFolder, Launcher));

    /// <summary>O caminho apontado é mesmo o Launcher do Live Editor (tem a DLL ao lado).</summary>
    public static bool PareceLiveEditor(string? launcherPath)
    {
        if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath)) return false;
        var pasta = Path.GetDirectoryName(launcherPath) ?? "";
        return File.Exists(Path.Combine(pasta, DllDoLiveEditor));
    }

    public const string Aviso =
        "EA Javelin Anticheat: a Steam abre este jogo pelo " + Launcher + ", que sobe o anticheat antes " +
        "do jogo — e sob ele o dxgi.dll do ReShade não carrega (o ReShade.log nem aparece). Os arquivos " +
        "instalados estão certos; o que muda é quem abre o jogo.";

    public const string ComoAbrir =
        "Abra o jogo pelo Launcher do FC Live Editor (Launcher.exe, na pasta do Live Editor): ele roda o " +
        "jogo sem o anticheat enquanto está aberto e devolve o launcher original ao fechar. O botão " +
        "\"Abrir o jogo\" da verificação faz isso quando você aponta o Launcher.exe uma vez. Só para " +
        "jogar OFFLINE: entrar no multiplayer sem o anticheat dá banimento de conta na EA.";
}
