namespace Dlss5.Core;

/// <summary>Arquitetura de um executável PE.</summary>
public enum PeArchitecture
{
    Unknown,
    X86,
    X64,
}

/// <summary>API gráfica que o jogo usa (a que o renderizador final fala).</summary>
public enum GraphicsApi
{
    Unknown,
    D3D8,
    D3D9,
    D3D10,
    D3D11,
    D3D12,
    Vulkan,
    OpenGL,
}

/// <summary>Caminho de instalação, conforme a especificação (seção 6).</summary>
public enum InstallRoute
{
    /// <summary>Não foi possível determinar / combinação sem suporte.</summary>
    Unsupported,
    /// <summary>A — 64-bit D3D11/D3D12/Vulkan.</summary>
    A,
    /// <summary>B — 32-bit D3D11 nativo.</summary>
    B,
    /// <summary>C — 32-bit D3D8/D3D9 traduzido para D3D11 pelo dgVoodoo2.</summary>
    C,
}

/// <summary>Provedor de motion vectors.</summary>
public enum MvProvider
{
    /// <summary>iMMERSE Launchpad (MartysMods_LAUNCHPAD.fx) — validado em RE2/Tomb Raider.</summary>
    Launchpad,
    /// <summary>DRME (MotionEstimation.fx) — recomendado pelo projeto do Feeder.</summary>
    Drme,
}

/// <summary>Perfil do jogo: detecção automática + ajustes do usuário (spec 11.4).</summary>
public sealed class GameProfile
{
    /// <summary>Pasta raiz que o usuário apontou.</summary>
    public required string GameFolder { get; set; }

    /// <summary>Caminho completo do executável REAL do jogo.</summary>
    public string? RealExePath { get; set; }

    /// <summary>Pasta do executável real (onde vão ReShade, addons, host64).</summary>
    public string ExeFolder => RealExePath is null
        ? GameFolder
        : Path.GetDirectoryName(RealExePath)!;

    public PeArchitecture Architecture { get; set; } = PeArchitecture.Unknown;

    public GraphicsApi Api { get; set; } = GraphicsApi.Unknown;

    /// <summary>Como a API foi descoberta (pistas e confiança), para explicar na tela.</summary>
    public ApiDetection? ApiDetection { get; set; }

    /// <summary>O jogo já tem DLSS nativo? (dispensa o Feeder)</summary>
    public bool HasNativeDlss { get; set; }

    /// <summary>Como o DLSS nativo foi detectado (pistas), para mostrar na tela.</summary>
    public NativeDlssDetection? NativeDlss { get; set; }

    /// <summary>O usuário contrariou a detecção à mão (só então o palpite dele vale).</summary>
    public bool NativeDlssOverridden { get; set; }

    /// <summary>Pasta onde o módulo que chama Direct3D vive (dgVoodoo vai aqui).
    /// Igual à pasta do exe, exceto engines tipo Source (bin\).</summary>
    public string? RendererFolder { get; set; }

    /// <summary>Engine Source detectada (exe-stub + bin\shaderapidx9.dll).</summary>
    public bool IsSourceEngine { get; set; }

    /// <summary>Launcher separado detectado (informativo — nunca é o alvo da instalação).</summary>
    public string? LauncherExePath { get; set; }

    public MvProvider MvProvider { get; set; } = MvProvider.Launchpad;

    /// <summary>Rota derivada de arch + api (árvore de decisão, spec 5).</summary>
    public InstallRoute Route
    {
        get
        {
            if (Architecture == PeArchitecture.X64)
            {
                // 64-bit cobre D3D11/D3D12/Vulkan. OpenGL entra pelo mesmo caminho, só
                // trocando o nome com que o ReShade é instalado. D3D10 fica de fora.
                return Api switch
                {
                    GraphicsApi.D3D11 or GraphicsApi.D3D12 or GraphicsApi.Vulkan => InstallRoute.A,
                    GraphicsApi.OpenGL => InstallRoute.A,
                    _ => InstallRoute.Unsupported,
                };
            }
            if (Architecture == PeArchitecture.X86)
            {
                // D3D8 e D3D9 caem os dois no dgVoodoo2, que traduz para D3D11: muda só
                // qual wrapper é copiado (D3D8.dll ou D3D9.dll).
                return Api switch
                {
                    GraphicsApi.D3D11 => InstallRoute.B,
                    GraphicsApi.D3D9 or GraphicsApi.D3D8 => InstallRoute.C,
                    _ => InstallRoute.Unsupported,
                };
            }
            return InstallRoute.Unsupported;
        }
    }

    /// <summary>
    /// Pedido explícito para usar o caminho direto do RenoDX em vez do Feeder.
    /// Desligado por padrão por um motivo empírico: em mais de 30 jogos testados, o
    /// Feeder funcionou com diferença visível; o caminho direto registrou "evaluation
    /// succeeded" no log com a imagem inalterada (Onimusha, GTA 5) — a rede roda e o
    /// resultado não chega na tela. Enquanto isso for verdade, o caminho comprovado é
    /// o padrão e o direto é escolha consciente.
    /// </summary>
    public bool PreferirCaminhoDireto { get; set; }

    /// <summary>
    /// O RenoDX se pendura direto nas chamadas de DLSS que o jogo já faz, mas só enxerga
    /// NGX em D3D12 (spec 1: "só funciona em jogos com DLSS nativo, 64-bit, D3D12").
    /// Num jogo D3D11 ou Vulkan com DLSS nativo ele instala os hooks e nunca vê um create
    /// — daí o "HOOKS ARMED / NO DLSS CREATE SEEN". E mesmo em D3D12 só entra a pedido.
    /// </summary>
    public bool UsesRenodxDirectPath => PreferirCaminhoDireto && HasNativeDlss && Api == GraphicsApi.D3D12;

    /// <summary>
    /// O Feeder entra sempre que o RenoDX não consegue pegar o DLSS do próprio jogo —
    /// inclusive em jogo COM DLSS nativo que não seja D3D12, porque ele roda o NGX num
    /// device D3D12 privado e por isso independe da API do jogo.
    /// </summary>
    public bool NeedsFeeder => !UsesRenodxDirectPath;

    /// <summary>Precisa do dgVoodoo2 (rota C).</summary>
    public bool NeedsDgVoodoo => Route == InstallRoute.C;

    /// <summary>
    /// Nome com que o ReShade é instalado. O jogo carrega a DLL pelo nome da API que ele
    /// usa: um jogo OpenGL nunca vai procurar por dxgi.dll, então instalar com esse nome
    /// resulta em ReShade que jamais é carregado e num ReShade.log que nem chega a existir.
    /// </summary>
    public string ReShadeHookName => Api == GraphicsApi.OpenGL ? "opengl32.dll" : "dxgi.dll";

    /// <summary>
    /// Wrapper do dgVoodoo2 correspondente à API do jogo (rota C). O dgVoodoo traz um
    /// arquivo por API, e o jogo só carrega o que tem o nome certo.
    /// </summary>
    public string DgVoodooWrapperName => Api == GraphicsApi.D3D8 ? "D3D8.dll" : "D3D9.dll";

    /// <summary>
    /// Nome do dgVoodoo quando o nome original já é de outro wrapper (DxWrapper) e os
    /// dois são encadeados. Ver <see cref="DxWrapperChain"/>.
    /// </summary>
    public string DgVoodooChainedName => DxWrapperChain.NomeEncadeado(DgVoodooWrapperName);

    /// <summary>
    /// API fora da matriz validada da especificação (seção 2). Instala, mas avisando:
    /// o addon do Feeder anuncia D3D11/D3D12/Vulkan, então OpenGL é tentativa.
    /// </summary>
    public bool IsExperimentalApi => Api == GraphicsApi.OpenGL;

    /// <summary>Opções de inicialização sugeridas (Source: -dxlevel 95).</summary>
    public string? SuggestedLaunchOptions =>
        IsSourceEngine && Api == GraphicsApi.D3D9 ? "-dxlevel 95" : null;
}

/// <summary>Tipos de ação num plano de instalação.</summary>
public enum PlanActionKind
{
    CopyFile,
    ExtractReShadeDll,
    WriteGeneratedFile,
    PatchDgVoodooConf,
    DeleteForbiddenFile,
    RegistryOverride,
}

/// <summary>Uma ação do plano de instalação, exibível e executável.</summary>
public sealed record PlanAction(
    PlanActionKind Kind,
    string Description,
    string? SourcePath,
    string? TargetPath);

/// <summary>Resultado de um checkpoint de verificação (spec 9).</summary>
public enum CheckStatus
{
    Pass,
    Fail,
    Warning,
    /// <summary>Só verificável com o jogo aberto / manualmente.</summary>
    Manual,
    NotApplicable,
}

public sealed record CheckResult(
    int Number,
    string Title,
    CheckStatus State,
    string Detail,
    string? FixHint = null);
