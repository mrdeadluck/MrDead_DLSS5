namespace Dlss5.Core;

/// <summary>
/// O Feeder dos jogos 32-bit (rotas B e C): addon32, host64 e .fx do 0.15.1, fixos.
///
/// Em 30/09/2026 o kit passou para o Feeder 1.17.0 e o Batman: Arkham Asylum (D3D9 → dgVoodoo,
/// com "forçar janela") passou a fechar sozinho; com o kit anterior, o mesmo jogo voltou a rodar.
/// O 1.17 mudou justamente o arranque do host64 em jogo 32-bit: com o swapchain em tela cheia
/// exclusiva ele sobe o host sem janela (#109) — e o "forçar janela" faz o jogo se achar em tela
/// cheia. Até haver log que prove o conserto, os jogos 32-bit ficam no conjunto que já funcionava.
/// addon32 e host64 precisam ser do mesmo zip (0.15.1 = protocolo IPC v9); o .fx vai junto.
///
/// Os três vivem numa pasta própria do kit, renomeados para não disputar o nome com os do Feeder
/// principal na busca do kit; o plano grava no jogo com os nomes certos. Kit sem o conjunto: o plano
/// usa os da raiz e avisa.
/// </summary>
public static class FeederX86
{
    public const string Versao = "0.15.1";
    public const string Addon32NoKit = "dlss5-feed_" + Versao + ".addon32";
    public const string HostNoKit = "dlss5-feed-host64_" + Versao + ".exe";
    public const string FxNoKit = "DLSS5_Feed_" + Versao + ".fx";
}
