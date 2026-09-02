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
    /// Pedido explícito para usar o Feeder num jogo em que o caminho direto é o padrão
    /// (D3D12 com DLSS nativo). É escolha consciente porque a evidência foi contra:
    /// no Onimusha, o Feeder inicializa um NGX próprio dentro do processo e colide com
    /// o do jogo — com o DLSS do jogo ligado, trava depois da tela inicial (o GTA 5
    /// trava ao ligar o DLSS no menu); com o DLSS desligado, o Feeder nunca chega a
    /// "feature ready", porque o Streamline do jogo já é dono do NGX. O caminho direto,
    /// no mesmo jogo, abriu com DLSS ligado e interceptou (creates 11, NR INJECTED).
    /// </summary>
    public bool PreferirFeeder { get; set; }

    /// <summary>
    /// O RenoDX se pendura direto nas chamadas de DLSS que o jogo já faz, mas só enxerga
    /// NGX em D3D12 (spec 1: "só funciona em jogos com DLSS nativo, 64-bit, D3D12").
    /// Num jogo D3D11 ou Vulkan com DLSS nativo ele instala os hooks e nunca vê um create
    /// — daí o "HOOKS ARMED / NO DLSS CREATE SEEN". Em D3D12 com DLSS nativo é o padrão;
    /// o caminho direto foi rebaixado a "experimental" numa época em que o nvngx_dlss.dll
    /// do jogo estava transplantado e o DLSS do jogo nem funcionava — ele nunca teve
    /// chance real até o Onimusha.
    /// </summary>
    public bool UsesRenodxDirectPath => HasNativeDlss && Api == GraphicsApi.D3D12 && !PreferirFeeder;

    /// <summary>
    /// O jogo entrega o DLSS pelo Streamline da NVIDIA (sl.*.dll ao lado do exe), e não
    /// por chamadas cruas de NGX. Informativo: o RHI, que é a referência que funciona,
    /// não mexe no EnableHooks por causa disso, e o RE9 provou que o modo 1 não muda o
    /// resultado quando o runtime está errado. O que decide é o item 18 da verificação.
    /// </summary>
    public bool UsaStreamline => NativeDlss?.Clues
        .Any(c => c.Texto.StartsWith("sl.", StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>EnableHooks gravado na instalação: o padrão do addon, como o RHI faz.</summary>
    public int HooksDoRenodx => RenodxIni.Padrao;

    /// <summary>
    /// O Feeder entra sempre que o RenoDX não consegue pegar o DLSS do próprio jogo —
    /// jogo sem DLSS (os 30+ que funcionaram) ou jogo com DLSS nativo fora do D3D12,
    /// porque ele roda o NGX num device D3D12 privado e independe da API do jogo. Em
    /// jogo com DLSS nativo, o DLSS do jogo tem que ficar DESLIGADO neste caminho.
    /// </summary>
    public bool NeedsFeeder => !UsesRenodxDirectPath;

    /// <summary>Precisa do dgVoodoo2 (rota C).</summary>
    public bool NeedsDgVoodoo => Route == InstallRoute.C;

    /// <summary>
    /// Hospedar o ReShade dentro do REFramework em vez de injetá-lo como dxgi.dll.
    /// É o caminho para jogo da RE Engine com proteção anti-adulteração (o RE9 recusa a
    /// injeção direta e cai antes de criar qualquer DLSS). Ver <see cref="ReFramework"/>.
    /// </summary>
    public bool UsarReFramework { get; set; }

    /// <summary>A pasta do jogo é RE Engine — só ali o REFramework tem função.</summary>
    public bool EhReEngine => ReFramework.EhReEngine(ExeFolder);

    /// <summary>
    /// Onde a DLL do ReShade (dxgi.dll etc.) entra. Quase sempre é a pasta do exe. No
    /// Titanfall 2 (engine da Respawn) o renderizador mora em bin\x64_retail e a DLL na
    /// raiz nunca carrega — o jogo abre, o Home não faz nada e o ReShade.log nem nasce.
    /// Ali a DLL vai para a pasta do renderizador; o ReShade.ini, o log, os addons e os
    /// shaders continuam na raiz, porque o ReShade escolhe a base pelo ini ao lado do exe.
    /// Na Source clássica (bin\ com shaderapi*.dll, D3D9 via dgVoodoo) a DLL fica na raiz.
    /// </summary>
    public string PastaDoReShade
    {
        get
        {
            // Só na rota A: na rota C o dgVoodoo mora na pasta do renderizador e cria o
            // device D3D11 de lá, mas o ReShade continua entrando pela raiz (Source, DS2).
            if (IsSourceEngine || Route != InstallRoute.A || string.IsNullOrWhiteSpace(RendererFolder)) return ExeFolder;
            static string N(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(N(RendererFolder), N(ExeFolder), StringComparison.OrdinalIgnoreCase)
                ? ExeFolder : RendererFolder;
        }
    }

    /// <summary>Onde fica a configuração que o ReShade lê: ao lado do executável.</summary>
    public string ReShadeIniPath => Path.Combine(ExeFolder, "ReShade.ini");

    /// <summary>Onde fica o log que o ReShade grava.</summary>
    public string ReShadeLogPath => Path.Combine(ExeFolder, "ReShade.log");

    /// <summary>
    /// Nome com que o ReShade é instalado. O jogo carrega a DLL pelo nome da API que ele
    /// usa: um jogo OpenGL nunca vai procurar por dxgi.dll, então instalar com esse nome
    /// resulta em ReShade que jamais é carregado e num ReShade.log que nem chega a existir.
    /// </summary>
    public string ReShadeHookName => Api == GraphicsApi.OpenGL
        ? "opengl32.dll"
        : NomeDoReShadeEscolhido ?? "dxgi.dll";

    /// <summary>
    /// Nome alternativo escolhido para o ReShade, quando o dxgi.dll não serve.
    ///
    /// O dxgi.dll é o nome que funciona na esmagadora maioria dos jogos Direct3D, porque
    /// é pela DXGI que o swapchain nasce. Mas há jogo que RECUSA especificamente esse
    /// nome: o MGS V (Fox Engine, TPP e Ground Zeroes) tem uma proteção anti-adulteração
    /// que, com o dxgi.dll na pasta, faz o executável nem abrir — e o contorno que a
    /// comunidade usa há anos é entrar como d3d11.dll, o nome da própria API. O jogo
    /// carrega a DLL, o ReShade se pendura na criação do device e a proteção não reage.
    /// </summary>
    public string? NomeDoReShadeEscolhido { get; set; }

    /// <summary>
    /// Jogos que sabidamente recusam o dxgi.dll. O MGS V (Fox Engine) é o caso
    /// documentado: com o dxgi.dll na pasta o executável não abre, e trocar para o nome
    /// da própria API é o contorno que a comunidade usa. Devolve nulo quando não há
    /// motivo para fugir do padrão.
    /// </summary>
    public static string? NomeDeReShadePreferido(string? exePath, GraphicsApi api)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        if (api is not (GraphicsApi.D3D11 or GraphicsApi.D3D12)) return null;

        // Fox Engine já morou aqui pedindo d3d11.dll. Era lenda de fórum: a checagem do
        // jogo (CheckModuleHook) olha o gancho no D3D11, não o nome do arquivo — d3d11.dll
        // fechava o jogo igual. Com o patch anti-hook o ReShade entra como dxgi.dll, o
        // nome comum, e é assim que as instruções do patcher mandam instalar.
        return null;
    }

    /// <summary>Nomes oferecidos para o ReShade, na ordem em que a tela mostra.</summary>
    public IReadOnlyList<string> NomesDeReShadePossiveis => Api switch
    {
        GraphicsApi.OpenGL => new[] { "opengl32.dll" },
        GraphicsApi.D3D12 => new[] { "dxgi.dll", "d3d12.dll" },
        GraphicsApi.D3D11 => new[] { "dxgi.dll", "d3d11.dll" },
        _ => new[] { "dxgi.dll" },
    };

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
    /// <summary>Roda o patcher anti-hook da Fox Engine sobre o exe do jogo (SourcePath = patcher, TargetPath = exe).</summary>
    PatchMgsvExe,
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
