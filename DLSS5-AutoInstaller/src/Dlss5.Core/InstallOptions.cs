namespace Dlss5.Core;

/// <summary>Opções escolhidas pelo usuário antes de gerar o plano.</summary>
public sealed class InstallOptions
{
    public MvProvider MvProvider { get; set; } = MvProviders.Padrao;

    /// <summary>Tecla do overlay do ReShade (qualquer Virtual-Key Code; Home por padrão).</summary>
    public int OverlayKey { get; set; } = ReShadeConfigWriter.KeyHome;

    /// <summary>Modificadores da tecla do overlay (ex.: Ctrl+Shift+Home).</summary>
    public bool OverlayCtrl { get; set; }
    public bool OverlayShift { get; set; }
    public bool OverlayAlt { get; set; }

    /// <summary>Combinação escrita por extenso, para instruções.</summary>
    public string OverlayKeyLabel =>
        ReShadeConfigWriter.DescribeKey(OverlayKey, OverlayCtrl, OverlayShift, OverlayAlt);

    /// <summary>
    /// Tecla que liga e desliga o DLSS 5 no jogo sem abrir o painel (0 = nenhuma). É a tecla de
    /// alternância que o ReShade dá a qualquer technique, gravada no preset como
    /// Key&lt;technique&gt;; aqui ela vai na "DLSS 5 Feed". O addon do Feeder só trabalha logo
    /// depois de essa technique rodar (cabeçalho do DLSS5_Feed.fx), então desligá-la desliga o
    /// DLSS + Neural Rendering inteiro e o jogo mostra o quadro cru — a comparação antes/depois.
    /// Padrão F6: é a mesma tecla que o addon do Krish usa para o NR em jogo 64-bit (NRToggleKey),
    /// então uma tecla só vale nas duas rotas. O ShortFuse não tem tecla própria, e o F6 do Krish
    /// não chega ao host64 em jogo 32-bit — por isso a alternância precisa ser do ReShade do jogo.
    /// Só faz sentido onde o Feeder está instalado (com DLSS nativo o preset é vazio).
    /// </summary>
    public int TeclaLigaDesliga { get; set; } = ReShadeConfigWriter.KeyF6;

    public string TeclaLigaDesligaLabel =>
        TeclaLigaDesliga == 0 ? "nenhuma" : ReShadeConfigWriter.DescribeKey(TeclaLigaDesliga);

    /// <summary>Aplicar o override de assinatura no registro (precisa de admin + reboot).</summary>
    public bool ApplyRegistryOverride { get; set; } = true;

    /// <summary>Remover os arquivos proibidos (spec 3.7) da pasta do exe.</summary>
    public bool CleanForbidden { get; set; } = true;

    /// <summary>Marca d'água do dgVoodoo ligada (prova de vida; desligar depois).</summary>
    public bool DgVoodooWatermark { get; set; } = true;

    /// <summary>
    /// Força o jogo a rodar em janela (sem borda, do tamanho da tela) mesmo que ele só ofereça
    /// tela cheia exclusiva. Vale para qualquer jogo que sobe o host64 (rotas B e C, 32-bit):
    /// em tela cheia EXCLUSIVA o host64 (janela D3D12 atrás do jogo) e o painel projetado brigam
    /// com o swapchain do jogo, e o jogo congela no aperto de mão com o host (Enslaved, 10/09/2026,
    /// logo depois de SetFullscreenState(TRUE)); o README do Feeder diz "windowed is smoother".
    ///
    /// Como o feed roda como addon do ReShade DENTRO do jogo, a alavanca geral é o ReShade:
    /// [APP] ForceWindowed=1 no ReShade.ini do jogo + o addon swapchain_override (JanelaForcada),
    /// que é quem lê a chave no ReShade 6, obriga o swapchain a nascer em janela em qualquer API
    /// (D3D9/10/11/12) e independe de o jogo ter opção de janela. Na rota C (jogo
    /// antigo por trás do dgVoodoo) o dgVoodoo.conf também sai com FullScreenMode=false +
    /// WindowedAttributes — as duas alavancas juntas.
    /// </summary>
    public bool ForcarJanela { get; set; }
}
