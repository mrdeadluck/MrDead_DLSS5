namespace Dlss5.Core;

/// <summary>
/// "Modo helper 64-bit" do DLSS5-Feeder: o jogo 64-bit roda só a metade de dentro do jogo
/// (dlss5-feed-helper.addon64, a mesma do addon32 compilada em x64) e manda quadro, depth e
/// vetores para o host64\dlss5-feed-host64.exe — exatamente como um jogo 32-bit. O NGX e o
/// consumidor neural moram no host64, que é Direct3D 12.
///
/// Serve aqui para o jogo OpenGL 64-bit com o motor ShortFuse: o renodx-dlss.addon64 só
/// trabalha em D3D9/11/12 e, carregado no próprio jogo OpenGL, derrubava o processo (Amnesia:
/// The Bunker, 29/09/2026). Dentro do host64 ele é o mesmo arranjo do ShortFuse em jogo 32-bit,
/// validado no SH2 EE com passadas múltiplas.
///
/// É um conjunto separado de propósito: o addon helper só existe a partir do Feeder
/// 1.18.0-beta.1, e metade de dentro e host precisam ser do mesmo zip. O kit continua com o
/// Feeder estável (FeederKit.VersaoDoKit, 1.17.0 desde 29/09/2026) para todo o resto; este conjunto
/// vive numa pasta própria, com o host e o .fx renomeados para não disputar o nome com os do
/// Feeder principal na busca do kit, e só entra no jogo
/// que usa o modo helper (o plano grava com o nome certo).
/// </summary>
public static class FeederHelper64
{
    public const string Versao = "1.18.0-beta.1";
    public const string Addon = "dlss5-feed-helper.addon64";
    /// <summary>O host do mesmo zip, renomeado no kit; no jogo vai como host64\dlss5-feed-host64.exe.</summary>
    public const string HostNoKit = "dlss5-feed-host64_" + Versao + ".exe";
    /// <summary>O DLSS5_Feed.fx do mesmo zip, renomeado no kit; no jogo substitui reshade-shaders\Shaders\DLSS5_Feed.fx.</summary>
    public const string FxNoKit = "DLSS5_Feed_" + Versao + ".fx";
}
