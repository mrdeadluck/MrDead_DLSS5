using Dlss5.Core;
using Xunit;

namespace Dlss5.Core.Tests;

public class DgVoodooConfiguratorTests
{
    // Trecho fiel ao dgVoodoo.conf real: VideoCard aparece em [Glide] E em [DirectX].
    private const string Sample = """
        [General]
        OutputAPI                            = bestavailable
        Adapter                              = 0

        [Glide]
        VideoCard                           = voodoo_2
        Memory                              = 8

        [DirectX]
        DisableAndPassThru                  = true
        VideoCard                           = internal3D
        VRAM                                = 256
        dgVoodooWatermark                   = false

        [DirectXExt]
        AdapterIDType                       = 
        MSD3DDeviceNames                    = false
        DefaultEnumeratedResolutions        = all
        """;

    [Fact]
    public void Patch_SetsDirectXKeys()
    {
        var result = DgVoodooConfigurator.Patch(Sample);
        Assert.Contains("DisableAndPassThru                  = false", result);
        Assert.Contains("VRAM                                = 1024", result);
        Assert.Contains("dgVoodooWatermark                   = true", result);
        Assert.Contains("OutputAPI                            = d3d11_fl11_0", result);
    }

    [Fact]
    public void Patch_DoesNotTouchGlideVideoCard()
    {
        var result = DgVoodooConfigurator.Patch(Sample);
        // A VideoCard do Glide tem que continuar voodoo_2; só a de [DirectX] é internal3D.
        Assert.Contains("VideoCard                           = voodoo_2", result);

        var directXSection = result[result.IndexOf("[DirectX]", StringComparison.Ordinal)..];
        Assert.Contains("VideoCard                           = internal3D", directXSection);
    }

    [Fact]
    public void Patch_PreservesUnrelatedLines()
    {
        var result = DgVoodooConfigurator.Patch(Sample);
        Assert.Contains("Adapter                              = 0", result);
        Assert.Contains("Memory                              = 8", result);
    }

    [Fact]
    public void PerfilLegado_FazOAdaptadorParecerUmaPlacaComum()
    {
        // Jogo de DirectX 8 checa a placa antes de criar o device e recusa o cartão
        // virtual do dgVoodoo: o Max Payne responde "requires a DirectX 8 compatible
        // display adapter" e nem abre. As duas chaves abaixo são o que o próprio
        // dgVoodoo documenta para esse caso.
        var result = DgVoodooConfigurator.Patch(Sample, DgVoodooProfile.Legado);

        Assert.Contains("AdapterIDType                       = nvidia", result);
        Assert.Contains("MSD3DDeviceNames                    = true", result);
        // VRAM volta ao padrão do dgVoodoo: jogo de 2001 não espera placa de 1 GB.
        Assert.Contains("VRAM                                = 256", result);
        // O resto continua igual ao perfil padrão.
        Assert.Contains("DisableAndPassThru                  = false", result);
        Assert.Contains("VideoCard                           = internal3D", result);
    }

    [Fact]
    public void PerfilLegado_EnumeraSoAsResolucoesDaEpoca()
    {
        // Com "all" o dgVoodoo lista toda resolução do monitor nas três profundidades de
        // cor, e jogo de 2001 guarda isso em vetor fixo — estourando, ele não acha modo
        // válido nenhum e conclui que a placa não serve.
        var result = DgVoodooConfigurator.Patch(Sample, DgVoodooProfile.Legado);
        Assert.Contains("DefaultEnumeratedResolutions        = classics", result);
    }

    [Fact]
    public void TrocaDePlacaSobrescreveApenasOVideoCardDoDirectX()
    {
        var result = DgVoodooConfigurator.Patch(Sample, DgVoodooProfile.Legado, "geforce_ti_4800");

        Assert.Contains("VideoCard                           = geforce_ti_4800", result);
        // A VideoCard do [Glide] é outra chave, com o mesmo nome, e não pode ser tocada.
        Assert.Contains("VideoCard                           = voodoo_2", result);
    }

    [Fact]
    public void TodaPlacaOferecidaExisteNaListaDoDgVoodoo()
    {
        // Nome fora da lista documentada o dgVoodoo ignora em silêncio, e o jogo segue
        // recusando o adaptador sem que se descubra por quê.
        string[] validos =
        {
            "svga", "internal3D", "geforce_ti_4800", "ati_radeon_8500",
            "matrox_parhelia-512", "geforce_fx_5700_ultra", "geforce_9800_gt",
        };
        Assert.All(DgVoodooConfigurator.Placas, p => Assert.Contains(p.Valor, validos));
    }

    [Fact]
    public void MarcaDaguaEhEscritaNaSecaoCerta()
    {
        // Ela mora em [DirectX]. Enquanto o alvo apontava para [DirectXExt] a chave nunca
        // era escrita, e só não fez falta porque o conf do kit já vem com ela ligada.
        var result = DgVoodooConfigurator.Patch(Sample);
        Assert.Contains("dgVoodooWatermark                   = true", result);
        Assert.DoesNotContain("[DirectXExt] dgVoodooWatermark", DgVoodooConfigurator.MissingKeys(Sample));
    }

    [Fact]
    public void PerfilPadrao_NaoMexeNasChavesDeCompatibilidade()
    {
        var result = DgVoodooConfigurator.Patch(Sample);

        Assert.Contains("VRAM                                = 1024", result);
        Assert.DoesNotContain("= nvidia", result);
        Assert.Contains("MSD3DDeviceNames                    = false", result);
    }

    [Fact]
    public void PerfilSegueAApiDoJogo()
    {
        Assert.Equal(DgVoodooProfile.Legado, DgVoodooConfigurator.ProfileFor(GraphicsApi.D3D8));
        Assert.Equal(DgVoodooProfile.Padrao, DgVoodooConfigurator.ProfileFor(GraphicsApi.D3D9));
    }

    [Fact]
    public void MissingKeys_ReportsAbsentTargets()
    {
        var missing = DgVoodooConfigurator.MissingKeys("[General]\nOutputAPI = x\n");
        Assert.Contains("[DirectX] VRAM", missing);
        Assert.DoesNotContain("[General] OutputAPI", missing);
    }
}

public class ReShadeConfigWriterTests
{
    [Fact]
    public void Preset_PutsMvProviderBeforeFeed()
    {
        foreach (var provider in new[] { MvProvider.Launchpad, MvProvider.Drme })
        {
            var preset = ReShadeConfigWriter.BuildPresetIni(provider);
            var line = preset.Split('\n').First(l => l.StartsWith("Techniques=", StringComparison.Ordinal));

            int feedIdx = line.IndexOf("DLSS5_Feed@", StringComparison.Ordinal);
            int mvIdx = provider == MvProvider.Drme
                ? line.IndexOf("DRME@", StringComparison.Ordinal)
                : line.IndexOf("MartysMods_Launchpad@", StringComparison.Ordinal);

            Assert.True(mvIdx >= 0, $"provedor ausente para {provider}");
            Assert.True(feedIdx > mvIdx, $"DLSS5_Feed precisa vir depois do provedor ({provider})");
        }
    }

    [Fact]
    public void Preset_UsesRealTechniqueNames()
    {
        // Nomes lidos dos .fx do kit — se mudarem, o preset silenciosamente não ativa nada.
        Assert.Contains("DRME@MotionEstimation.fx",
            ReShadeConfigWriter.BuildPresetIni(MvProvider.Drme));
        Assert.Contains("MartysMods_Launchpad@MartysMods_LAUNCHPAD.fx",
            ReShadeConfigWriter.BuildPresetIni(MvProvider.Launchpad));
    }

    [Fact]
    public void Ini_HasSearchPathsAndAddonPath()
    {
        var ini = ReShadeConfigWriter.BuildReShadeIni();
        Assert.Contains(@"EffectSearchPaths=.\reshade-shaders\Shaders\**", ini);
        Assert.Contains(@"TextureSearchPaths=.\reshade-shaders\Textures\**", ini);
        Assert.Contains(@"AddonPath=.\", ini);
        Assert.Contains("[GENERAL]", ini);
    }

    [Fact]
    public void Ini_UsesChosenOverlayKey()
    {
        Assert.Contains("KeyOverlay=45,0,0,0",
            ReShadeConfigWriter.BuildReShadeIni(ReShadeConfigWriter.KeyInsert));
        Assert.Contains("KeyOverlay=36,0,0,0",
            ReShadeConfigWriter.BuildReShadeIni(ReShadeConfigWriter.KeyHome));
    }

    [Fact]
    public void Ini_WritesModifiers()
    {
        // O ReShade lê KeyOverlay=<vk>,<ctrl>,<shift>,<alt>.
        Assert.Contains("KeyOverlay=36,1,1,0",
            ReShadeConfigWriter.BuildReShadeIni(ReShadeConfigWriter.KeyHome, ctrl: true, shift: true));
        Assert.Contains("KeyOverlay=118,0,0,1",
            ReShadeConfigWriter.BuildReShadeIni(118, alt: true));
    }

    [Fact]
    public void DescribeKey_NamesTheCombination()
    {
        Assert.Equal("Home", ReShadeConfigWriter.DescribeKey(ReShadeConfigWriter.KeyHome));
        Assert.Equal("Ctrl+Shift+Insert",
            ReShadeConfigWriter.DescribeKey(ReShadeConfigWriter.KeyInsert, ctrl: true, shift: true));
        Assert.Equal("Alt+F7", ReShadeConfigWriter.DescribeKey(118, alt: true));
    }

    [Fact]
    public void OverlayKeys_AreUniqueAndCoverTheUsualSuspects()
    {
        var keys = ReShadeConfigWriter.OverlayKeys;
        Assert.Equal(keys.Count, keys.Select(k => k.VirtualKey).Distinct().Count());
        Assert.Contains(keys, k => k.VirtualKey == ReShadeConfigWriter.KeyHome);
        Assert.Contains(keys, k => k.VirtualKey == ReShadeConfigWriter.KeyInsert);
        Assert.Contains(keys, k => k.Label == "F1" && k.VirtualKey == 112);
        Assert.Contains(keys, k => k.Label == "F12" && k.VirtualKey == 123);
        Assert.Contains(keys, k => k.Label == "Tecla K" && k.VirtualKey == 75);
    }

    [Fact]
    public void Preset_IsEmptyWhenFeederNotInstalled()
    {
        // DLSS nativo: quem trabalha é o RenoDX; nenhum efeito do ReShade participa.
        var preset = ReShadeConfigWriter.BuildPresetIni(MvProvider.Launchpad, feederUsed: false);
        Assert.Contains("Techniques=", preset);
        Assert.DoesNotContain("DLSS5_Feed@", preset);
        Assert.DoesNotContain("MartysMods_Launchpad@", preset);
    }
}

public class ReShadeIniPorCaminhoTests
{
    [Fact]
    public void ComFeederOGenericDepthCopiaAntesDosClears()
    {
        var ini = ReShadeConfigWriter.BuildReShadeIni(feederUsed: true);
        Assert.Contains("[DEPTH]", ini);
        Assert.Contains("DepthCopyBeforeClears=1", ini);
        Assert.DoesNotContain("DisabledAddons", ini);
    }

    [Fact]
    public void NoCaminhoDiretoOGenericDepthFicaFora()
    {
        // RE9: crash dentro do exe um segundo depois do runtime do ReShade subir, com ou
        // sem RenoDX. A diferença para uma instalação manual (que roda) era a cópia do
        // depth antes de cada clear — inútil no caminho direto, onde o RenoDX recebe
        // depth e motion vectors do contrato NGX do jogo.
        var ini = ReShadeConfigWriter.BuildReShadeIni(feederUsed: false);
        Assert.DoesNotContain("DepthCopyBeforeClears", ini);
        Assert.DoesNotContain("[DEPTH]", ini);
        Assert.Contains("DisabledAddons=Generic Depth", ini);
        // O resto continua igual: addons da pasta e preset no lugar.
        Assert.Contains(@"AddonPath=.\", ini);
        Assert.Contains("PresetPath=", ini);
    }

    [Fact]
    public void OsDoisCaminhosGravamOEnableHooksDoRenodxExplicito()
    {
        // A tela de verificação troca a chave depois; para isso ela nasce explícita.
        foreach (var feeder in new[] { true, false })
        {
            var ini = ReShadeConfigWriter.BuildReShadeIni(feederUsed: feeder);
            Assert.Contains("[RenoDX.DLSS5]", ini);
            Assert.Equal(2, RenodxIni.Ler(ini));
        }

        var custom = ReShadeConfigWriter.BuildReShadeIni(feederUsed: false, renodxHooks: 1);
        Assert.Equal(1, RenodxIni.Ler(custom));
    }

    [Fact]
    public void NoCaminhoDiretoSoEfeitosMarcadosCarregam()
    {
        // Preset vazio + EffectLoadSkipping: nenhum .fx que sobrou na pasta é compilado.
        Assert.Contains("EffectLoadSkipping=1", ReShadeConfigWriter.BuildReShadeIni(feederUsed: false));
        Assert.DoesNotContain("EffectLoadSkipping", ReShadeConfigWriter.BuildReShadeIni(feederUsed: true));
    }
}

public class ApiDetectorTests
{
    private static string WriteFakeBinary(byte[] content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-apitest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "game.exe");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Bury(params (string Text, bool Utf16)[] markers)
    {
        var buffer = new byte[64 * 1024];
        int offset = 1024;
        foreach (var (text, utf16) in markers)
        {
            var bytes = utf16
                ? System.Text.Encoding.Unicode.GetBytes(text)
                : System.Text.Encoding.ASCII.GetBytes(text);
            Array.Copy(bytes, 0, buffer, offset, bytes.Length);
            offset += 4096;
        }
        return buffer;
    }

    [Fact]
    public void Detect_FindsApiFromAsciiString()
    {
        // O import table não mostra carga dinâmica; o nome da função fica no binário.
        var exe = WriteFakeBinary(Bury(("D3D12CreateDevice", false)));
        var result = ApiDetector.Detect(exe, Path.GetDirectoryName(exe)!);
        Assert.Equal(GraphicsApi.D3D12, result.Api);
    }

    [Fact]
    public void Detect_FindsApiFromWideString()
    {
        // LoadLibraryW guarda o nome em UTF-16: o scanner precisa enxergar os dois formatos.
        var exe = WriteFakeBinary(Bury(("vulkan-1.dll", true)));
        var result = ApiDetector.Detect(exe, Path.GetDirectoryName(exe)!);
        Assert.Equal(GraphicsApi.Vulkan, result.Api);
    }

    [Fact]
    public void Detect_FindsDirectX8()
    {
        var exe = WriteFakeBinary(Bury(("Direct3DCreate8", false)));
        var result = ApiDetector.Detect(exe, Path.GetDirectoryName(exe)!);
        Assert.Equal(GraphicsApi.D3D8, result.Api);
    }

    [Fact]
    public void Detect_ReportsUnknownWhenNothingFound()
    {
        var exe = WriteFakeBinary(new byte[32 * 1024]);
        var result = ApiDetector.Detect(exe, Path.GetDirectoryName(exe)!);
        Assert.Equal(GraphicsApi.Unknown, result.Api);
        Assert.False(result.Confident);
    }

    [Fact]
    public void Detect_IsNotConfidentWhenTwoApisAppear()
    {
        // Jogo que cita D3D11 e D3D12: a decisão volta para o usuário, sem fingir certeza.
        var exe = WriteFakeBinary(Bury(("D3D11CreateDevice", false), ("D3D12CreateDevice", false)));
        var result = ApiDetector.Detect(exe, Path.GetDirectoryName(exe)!);
        Assert.False(result.Confident);
    }
}

public class RouteTests
{
    private static GameProfile Profile(PeArchitecture arch, GraphicsApi api) =>
        new() { GameFolder = @"C:\game", RealExePath = @"C:\game\g.exe", Architecture = arch, Api = api };

    [Theory]
    [InlineData(PeArchitecture.X64, GraphicsApi.D3D12, InstallRoute.A)]
    [InlineData(PeArchitecture.X64, GraphicsApi.D3D11, InstallRoute.A)]
    [InlineData(PeArchitecture.X64, GraphicsApi.Vulkan, InstallRoute.A)]
    [InlineData(PeArchitecture.X86, GraphicsApi.D3D11, InstallRoute.B)]
    [InlineData(PeArchitecture.X86, GraphicsApi.D3D9, InstallRoute.C)]
    // DirectX 8 é o mesmo caminho do D3D9: quem traduz é o dgVoodoo2.
    [InlineData(PeArchitecture.X86, GraphicsApi.D3D8, InstallRoute.C)]
    // OpenGL em 64-bit segue a rota A; muda só o nome com que o ReShade é instalado.
    [InlineData(PeArchitecture.X64, GraphicsApi.OpenGL, InstallRoute.A)]
    // Regra derivada da spec: 32-bit fora do D3D11 depende do dgVoodoo, e o addon32 só
    // aceita Direct3D 11 — então Vulkan e OpenGL não têm caminho em x86.
    [InlineData(PeArchitecture.X86, GraphicsApi.Vulkan, InstallRoute.Unsupported)]
    [InlineData(PeArchitecture.X86, GraphicsApi.OpenGL, InstallRoute.Unsupported)]
    [InlineData(PeArchitecture.X64, GraphicsApi.D3D10, InstallRoute.Unsupported)]
    [InlineData(PeArchitecture.X86, GraphicsApi.D3D10, InstallRoute.Unsupported)]
    [InlineData(PeArchitecture.Unknown, GraphicsApi.D3D11, InstallRoute.Unsupported)]
    public void Route_FollowsDecisionTree(PeArchitecture arch, GraphicsApi api, InstallRoute expected)
    {
        Assert.Equal(expected, Profile(arch, api).Route);
    }

    [Fact]
    public void NomeDoHookSegueAApi()
    {
        // Um jogo OpenGL nunca procura por dxgi.dll: instalado com esse nome, o ReShade
        // simplesmente nunca é carregado e nem chega a existir um ReShade.log.
        Assert.Equal("opengl32.dll", Profile(PeArchitecture.X64, GraphicsApi.OpenGL).ReShadeHookName);
        Assert.Equal("dxgi.dll", Profile(PeArchitecture.X64, GraphicsApi.D3D12).ReShadeHookName);

        Assert.Equal("D3D8.dll", Profile(PeArchitecture.X86, GraphicsApi.D3D8).DgVoodooWrapperName);
        Assert.Equal("D3D9.dll", Profile(PeArchitecture.X86, GraphicsApi.D3D9).DgVoodooWrapperName);
    }

    [Fact]
    public void NeedsDgVoodoo_OnlyOnRouteC()
    {
        Assert.True(Profile(PeArchitecture.X86, GraphicsApi.D3D8).NeedsDgVoodoo);
        Assert.True(Profile(PeArchitecture.X86, GraphicsApi.D3D9).NeedsDgVoodoo);
        Assert.False(Profile(PeArchitecture.X86, GraphicsApi.D3D11).NeedsDgVoodoo);
        Assert.False(Profile(PeArchitecture.X64, GraphicsApi.D3D12).NeedsDgVoodoo);
    }

    [Fact]
    public void ExeFolder_IsDirectoryOfRealExe()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "game");
        var p = new GameProfile
        {
            GameFolder = raiz,
            RealExePath = Path.Combine(raiz, "Binaries", "Win32", "g.exe"),
        };
        Assert.Equal(Path.Combine(raiz, "Binaries", "Win32"), p.ExeFolder);
    }
}

public class PlanBuilderTests
{
    internal static KitInventory KitCompleto() => FullKit();

    private static KitInventory FullKit() => new()
    {
        KitRoot = @"C:\kit",
        NvngxDlssnr = @"C:\kit\nvngx_dlssnr.dll",
        NvngxDlss = @"C:\kit\nvngx_dlss.dll",
        RenodxAddon64 = @"C:\kit\renodx-dlss5.addon64",
        FeedAddon64 = @"C:\kit\dlss5-feed.addon64",
        FeedAddon32 = @"C:\kit\dlss5-feed.addon32",
        FeedHost64Exe = @"C:\kit\dlss5-feed-host64.exe",
        DxgiX64 = @"C:\kit\dxgi64.dll",
        DxgiX86 = @"C:\kit\dxgi32.dll",
        ShadersDir = @"C:\kit\reshade-shaders",
        DgVoodooD3D9X86 = @"C:\kit\MS\x86\D3D9.dll",
        DgVoodooD3D8X86 = @"C:\kit\MS\x86\D3D8.dll",
        DgVoodooConf = @"C:\kit\dgVoodoo.conf",
        DgVoodooCpl = @"C:\kit\dgVoodooCpl.exe",
        HasLaunchpad = true,
        HasDrme = true,
    };

    // Pasta com separador do sistema (o plano usa Path.Combine); o nome "game" é o que
    // as asserções procuram no fim do caminho.
    private static readonly string GameRoot = Path.Combine(Path.GetTempPath(), "game");

    private static GameProfile Profile(PeArchitecture arch, GraphicsApi api) => new()
    {
        GameFolder = GameRoot,
        RealExePath = Path.Combine(GameRoot, "g.exe"),
        Architecture = arch,
        Api = api,
        RendererFolder = GameRoot,
    };

    // Os caminhos do plano usam o separador do sistema; o teste compara sempre com '\\'
    // para rodar igual no Windows e no Linux (CI local).
    private static string Norm(string? p) => (p ?? "").Replace('/', '\\');

    private static bool Targets(InstallPlan plan, string relative) =>
        plan.Actions.Any(a => Norm(a.TargetPath).EndsWith(relative, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void NativeDlss_OutsideD3D12_StillInstallsTheFeeder()
    {
        // O RenoDX só enxerga NGX em D3D12. Num jogo D3D11 com DLSS nativo ele fica em
        // "HOOKS ARMED / NO DLSS CREATE SEEN", então o Feeder continua sendo necessário.
        var profile = Profile(PeArchitecture.X64, GraphicsApi.D3D11);
        profile.HasNativeDlss = true;

        Assert.True(profile.NeedsFeeder);
        Assert.False(profile.UsesRenodxDirectPath);

        var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());
        Assert.True(Targets(plan, "dlss5-feed.addon64"));
        // Com o Feeder num jogo que tem DLSS próprio, o DLSS do jogo tem que ficar
        // desligado: o Feeder roda um NGX dele e, com o do jogo ativo, os dois colidem.
        Assert.Contains(plan.Warnings, w => w.Contains("DLSS desligado", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeDlss_InD3D12_SkipsTheFeeder()
    {
        // Em D3D12 com DLSS nativo o caminho direto é o padrão: no Onimusha só ele abriu
        // com o DLSS do jogo ligado e interceptou (creates 11, NR INJECTED).

        var profile = Profile(PeArchitecture.X64, GraphicsApi.D3D12);
        profile.HasNativeDlss = true;

        Assert.False(profile.NeedsFeeder);
        Assert.True(profile.UsesRenodxDirectPath);

        var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());
        Assert.False(Targets(plan, "dlss5-feed.addon64"));
        Assert.True(Targets(plan, "renodx-dlss5.addon64"));
    }

    [Fact]
    public void D3D12ComDlssNativo_PorPadraoUsaOCaminhoDireto()
    {
        // A decisão é empírica, e a evidência mudou de lado no Onimusha: o Feeder
        // inicializa um NGX próprio e colide com o do jogo (trava com DLSS ligado; sem
        // feature com DLSS desligado). O caminho direto abriu e interceptou. Os 30+
        // jogos do Feeder não tinham DLSS nativo — continuam no Feeder.
        var profile = Profile(PeArchitecture.X64, GraphicsApi.D3D12);
        profile.HasNativeDlss = true;

        Assert.True(profile.UsesRenodxDirectPath);
        Assert.False(profile.NeedsFeeder);

        var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());
        Assert.False(Targets(plan, "dlss5-feed.addon64"));
        Assert.True(Targets(plan, "renodx-dlss5.addon64"));
        Assert.Contains(plan.Warnings, w => w.Contains("Caminho direto", StringComparison.Ordinal));
    }

    [Fact]
    public void OsDoisCaminhosInstalamAPastaDeShaders()
    {
        // A pasta já ficou de fora do caminho direto, na suspeita de que compilar os .fx
        // derrubava o RE9. A suspeita caiu (era a proteção anti-adulteração do jogo), e
        // sem a pasta o ReShade abre reclamando "nenhum arquivo de efeito encontrado" —
        // parece defeito e não é. Com EffectLoadSkipping e preset vazio nada é compilado.
        var direto = Profile(PeArchitecture.X64, GraphicsApi.D3D12);
        direto.HasNativeDlss = true;
        Assert.True(Targets(InstallPlanBuilder.Build(direto, FullKit(), new InstallOptions()), "reshade-shaders"));

        var feeder = Profile(PeArchitecture.X64, GraphicsApi.D3D12);
        Assert.True(Targets(InstallPlanBuilder.Build(feeder, FullKit(), new InstallOptions()), "reshade-shaders"));
    }

    [Fact]
    public void PreferirFeederDevolveOFeederComAvisoDeDlssDesligado()
    {
        // Opt-in consciente: o Feeder entra, e o plano avisa que o DLSS do jogo precisa
        // ficar desligado — com ele ligado, os dois NGX colidem.
        var profile = Profile(PeArchitecture.X64, GraphicsApi.D3D12);
        profile.HasNativeDlss = true;
        profile.PreferirFeeder = true;

        Assert.False(profile.UsesRenodxDirectPath);
        Assert.True(profile.NeedsFeeder);

        var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());
        Assert.True(Targets(plan, "dlss5-feed.addon64"));
        Assert.Contains(plan.Warnings, w => w.Contains("DLSS desligado", StringComparison.Ordinal));
    }

    [Fact]
    public void JogoSemDlssNativoContinuaNoFeeder()
    {
        var profile = Profile(PeArchitecture.X64, GraphicsApi.D3D12);
        Assert.False(profile.UsesRenodxDirectPath);
        Assert.True(profile.NeedsFeeder);
    }

    [Fact]
    public void NaoSobrescreveONvngxDlssDoProprioJogo()
    {
        // A causa recorrente do "sumiram as opções de DLSS do jogo": o kit copiava seu
        // nvngx_dlss.dll por cima do do jogo. O do jogo é casado com o resto do Streamline
        // dele; trocar quebra o menu e faz o NGX recusar com 0xBAD00007. Agora fica o do jogo.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5nat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll"), "o do jogo");

            var profile = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "jogo.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                RendererFolder = dir,
            };

            var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());

            // Nenhuma ação copia por cima do nvngx_dlss.dll que já existe.
            Assert.DoesNotContain(plan.Actions, a =>
                a.Kind == PlanActionKind.CopyFile &&
                a.TargetPath?.EndsWith("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase) == true);
            Assert.Contains(plan.Warnings, w => w.Contains("é do jogo", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SemNvngxDlssNaPastaOKitTrazODele()
    {
        // Jogo sem DLSS próprio: aí sim o kit traz o seu nvngx_dlss.dll.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5nat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var profile = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "jogo.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                RendererFolder = dir,
            };

            var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());
            Assert.Contains(plan.Actions, a =>
                a.Kind == PlanActionKind.CopyFile &&
                a.TargetPath?.EndsWith("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase) == true);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void JogoComDlssNativoSemODllOKitNaoTransplanta()
    {
        // O caso Onimusha: uma desinstalação antiga apagou o nvngx_dlss.dll do jogo, e o
        // kit "ajudava" pondo o dele na pasta vazia — o motor do jogo carrega essa versão
        // errada na inicialização e trava antes de criar o swapchain. Em jogo com DLSS
        // nativo o kit NUNCA traz o dele; o conserto é a verificação de integridade.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5nat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var profile = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "jogo.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                RendererFolder = dir,
                HasNativeDlss = true,
            };

            var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());

            Assert.DoesNotContain(plan.Actions, a =>
                a.Kind == PlanActionKind.CopyFile &&
                a.TargetPath?.EndsWith("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase) == true);
            Assert.Contains(plan.Warnings, w =>
                w.Contains("Verificar integridade", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PlanoDoMgsvSemOPatchBloqueia()
    {
        // Está provado (log + teste "só o ReShade") que sem o patch o jogo fecha com
        // qualquer ReShade. Instalar de novo é rodada perdida: o plano bloqueia e diz o
        // que fazer — o patcher, para o Phantom Pain; "não há patch", para o Ground Zeroes.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5fox_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            GameProfile Perfil(string exe) => new()
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, exe),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                RendererFolder = dir,
            };

            var tpp = InstallPlanBuilder.Build(Perfil("mgsvtpp.exe"), FullKit(), new InstallOptions());
            Assert.False(tpp.CanRun);
            Assert.Contains(tpp.Blockers, b => b.Contains("CheckModuleHook", StringComparison.Ordinal)
                                               && b.Contains("não é o 1.0.15.4", StringComparison.Ordinal));

            var gz = InstallPlanBuilder.Build(Perfil("MgsGroundZeroes.exe"), FullKit(), new InstallOptions());
            Assert.False(gz.CanRun);
            Assert.Contains(gz.Blockers, b => b.Contains("não há patch publicado", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Um "mgsvtpp.exe" pequeno com hashes reais, no lugar do alvo de 166 MB.</summary>
    private static (string exe, AlvoDoPatch alvo) ExeFalsoDoPhantomPain(string dir)
    {
        var conteudo = new byte[4096];
        new Random(7).NextBytes(conteudo);
        const int offset = 0x123;
        conteudo[offset] = 0x75; conteudo[offset + 1] = 0x2D;
        var exe = Path.Combine(dir, "mgsvtpp.exe");
        File.WriteAllBytes(exe, conteudo);
        var remendado = (byte[])conteudo.Clone();
        remendado[offset] = 0xEB;
        static string Sha(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant();
        return (exe, new AlvoDoPatch(conteudo.Length, offset, new byte[] { 0x75, 0x2D }, new byte[] { 0xEB, 0x2D },
            Sha(conteudo), Sha(remendado)));
    }

    [Fact]
    public void ComOExeOriginalOPlanoAplicaOPatchPrimeiroEmVezDeBloquear()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5fox_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var alvoOriginal = MotorFox.Alvo;
        try
        {
            var (exe, alvo) = ExeFalsoDoPhantomPain(dir);
            MotorFox.Alvo = alvo;
            var profile = new GameProfile
            {
                GameFolder = dir,
                RealExePath = exe,
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                RendererFolder = dir,
            };

            var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());

            Assert.True(plan.CanRun);
            Assert.Equal(PlanActionKind.PatchMgsvExe, plan.Actions[0].Kind);
            Assert.Equal(exe, plan.Actions[0].TargetPath);
            Assert.Contains(plan.Warnings, w => w.Contains("0x2B90AB", StringComparison.Ordinal));

            // Exe de outra versão: bloqueia com tamanho e hash, sem tocar em nada.
            File.WriteAllBytes(exe, new byte[100]);
            var outro = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());
            Assert.False(outro.CanRun);
            Assert.Contains(outro.Blockers, b => b.Contains("não é o 1.0.15.4", StringComparison.Ordinal)
                                                 && b.Contains("100 bytes", StringComparison.Ordinal));
        }
        finally { MotorFox.Alvo = alvoOriginal; Directory.Delete(dir, true); }
    }

    [Fact]
    public void AplicarPatchRemendaComBackupEEhIdempotente()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5patch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var alvoOriginal = MotorFox.Alvo;
        try
        {
            var (exe, alvo) = ExeFalsoDoPhantomPain(dir);
            MotorFox.Alvo = alvo;
            var log = new List<string>();

            Assert.Equal(EstadoDoExeFox.Original, MotorFox.EstadoDoExe(exe));
            Assert.False(MotorFox.PatchAplicado(exe));

            MotorFox.AplicarPatch(exe, log.Add);

            Assert.Equal(EstadoDoExeFox.Remendado, MotorFox.EstadoDoExe(exe));
            Assert.True(MotorFox.PatchAplicado(exe));
            var backup = MotorFox.CaminhoDoBackup(exe);
            Assert.True(File.Exists(backup));
            Assert.Equal(EstadoDoExeFox.Original, MotorFox.EstadoDoExe(backup));
            var bytes = File.ReadAllBytes(exe);
            Assert.Equal(0xEB, bytes[alvo.Offset]);
            Assert.Equal(0x2D, bytes[alvo.Offset + 1]);
            Assert.Contains(log, l => l.Contains("Patch anti-hook aplicado", StringComparison.Ordinal));

            // De novo: nada muda, nada explode.
            MotorFox.AplicarPatch(exe, log.Add);
            Assert.Contains(log, l => l.Contains("já aplicado", StringComparison.Ordinal));

            // Exe desconhecido: recusa sem tocar.
            var outro = Path.Combine(dir, "outro.exe");
            File.WriteAllBytes(outro, new byte[300]);
            var ex = Assert.Throws<InvalidOperationException>(() => MotorFox.AplicarPatch(outro));
            Assert.Contains("1.0.15.4", ex.Message);
            Assert.False(File.Exists(MotorFox.CaminhoDoBackup(outro)));
        }
        finally { MotorFox.Alvo = alvoOriginal; Directory.Delete(dir, true); }
    }

    [Fact]
    public void OAlvoRealEhODoPatcherV10()
    {
        // Os números do MGSV-ReShade-AntiHook-Patcher v1.0 (fonte no kit, DLSS 5 Files\MGSV\).
        var a = MotorFox.PhantomPain;
        Assert.Equal(166_517_760, a.Tamanho);
        Assert.Equal(0x2B90AB, a.Offset);
        Assert.Equal(new byte[] { 0x75, 0x2D }, a.BytesOriginais);
        Assert.Equal(new byte[] { 0xEB, 0x2D }, a.BytesRemendados);
        Assert.Equal("085c2f82d1c963c40b3d2d55786661dfee2b18cbbf388a710c00fa76c5e9bb45", a.Sha256Original);
        Assert.Equal("184e0d1abec30561eee4650cb7f913e838692ba30233e8aab5dcbce522d8c297", a.Sha256Remendado);
    }

    [Fact]
    public void PlanoDoMgsvComOPatchInstalaComoDxgi()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5fox_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "mgsvtpp.exe" + MotorFox.SufixoDoBackup), "exe original");
            var profile = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "mgsvtpp.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                RendererFolder = dir,
            };

            var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());

            Assert.DoesNotContain(plan.Blockers, b => b.Contains("CheckModuleHook", StringComparison.Ordinal));
            Assert.Contains(plan.Actions, a =>
                a.Kind == PlanActionKind.CopyFile &&
                a.TargetPath?.EndsWith("dxgi.dll", StringComparison.OrdinalIgnoreCase) == true);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static GameProfile PerfilRotaC(string dir) => new()
    {
        GameFolder = dir,
        RealExePath = Path.Combine(dir, "deadspace2.exe"),
        Architecture = PeArchitecture.X86,
        Api = GraphicsApi.D3D9,
        RendererFolder = dir,
    };

    [Fact]
    public void RotaC_EncadeiaODgVoodooAtrasDoDxWrapper()
    {
        // Dead Space 2: o jogo não abria em CPU com mais de 10 núcleos, o DxWrapper
        // (d3d9.dll + dxwrapper.dll) resolvia, e a instalação copiou o dgVoodoo por cima
        // em silêncio — o conserto sumiu e o jogo voltou a não abrir. O nome é o mesmo,
        // então o DxWrapper FICA e o dgVoodoo entra ao lado com outro nome, apontado pelo
        // RealDllPath do d3d9.ini — o ini que o STUB lê (nome do próprio stub).
        var dir = Path.Combine(Path.GetTempPath(), "dlss5dxw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "D3D9.dll"), "stub que carrega dxwrapper.dll");
            File.WriteAllText(Path.Combine(dir, "dxwrapper.dll"), "DxWrapper");

            var plan = InstallPlanBuilder.Build(PerfilRotaC(dir), FullKit(), new InstallOptions());

            Assert.True(plan.CanRun);
            // O D3D9.dll do DxWrapper não é alvo de nada.
            Assert.DoesNotContain(plan.Actions, a =>
                Path.GetFileName(a.TargetPath ?? "").Equals("D3D9.dll", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.Actions, a =>
                a.Kind == PlanActionKind.CopyFile &&
                Path.GetFileName(a.TargetPath ?? "").Equals("dgVoodoo_D3D9.dll", StringComparison.OrdinalIgnoreCase));
            var ini = Assert.Single(plan.Actions, a =>
                a.Kind == PlanActionKind.WriteGeneratedFile &&
                Path.GetFileName(a.TargetPath ?? "").Equals("D3D9.ini", StringComparison.OrdinalIgnoreCase));
            Assert.EndsWith("dgVoodoo_D3D9.dll", ini.SourcePath!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(plan.Warnings, w => w.Contains("RealDllPath", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RotaC_DgVoodooJaInstaladoPodeSerSobrescrito()
    {
        // Reinstalar por cima do próprio dgVoodoo é o caso normal — e tem backup.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5dxw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "D3D9.dll"), "...dgVoodoo 2.8...");

            var plan = InstallPlanBuilder.Build(PerfilRotaC(dir), FullKit(), new InstallOptions());

            Assert.Empty(plan.Blockers);
            Assert.True(Targets(plan, "D3D9.dll"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RotaC_WrapperDesconhecidoTambemBloqueia()
    {
        // Sem saber de onde veio, sobrescrever é apostar com o jogo do usuário.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5dxw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "D3D9.dll"), "ENB ou outra coisa qualquer");

            var plan = InstallPlanBuilder.Build(PerfilRotaC(dir), FullKit(), new InstallOptions());

            Assert.False(plan.CanRun);
            Assert.Contains(plan.Blockers, b => b.Contains("não é o dgVoodoo", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PlanoDenunciaOTransplanteAntesDeInstalar()
    {
        // Pior que o slot vazio: o nvngx_dlss.dll PRESENTE, mas byte a byte igual ao do
        // kit — transplante de instalação antiga. O checkpoint antigo via o arquivo e
        // dizia "tudo certo"; instalar por cima não conserta e o jogo segue sem abrir.
        // O plano precisa denunciar e mandar para a rota de recuperação.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5nat_" + Guid.NewGuid().ToString("N"));
        var kitDir = Path.Combine(Path.GetTempPath(), "dlss5kit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(kitDir);
        try
        {
            var kitDll = Path.Combine(kitDir, "nvngx_dlss.dll");
            File.WriteAllText(kitDll, "bytes do kit");
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll"), "bytes do kit");

            var profile = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "jogo.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                RendererFolder = dir,
                HasNativeDlss = true,
            };
            var kit = FullKit();
            kit.NvngxDlss = kitDll;

            var plan = InstallPlanBuilder.Build(profile, kit, new InstallOptions());

            Assert.DoesNotContain(plan.Actions, a =>
                a.Kind == PlanActionKind.CopyFile &&
                a.TargetPath?.EndsWith("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase) == true);
            Assert.Contains(plan.Warnings, w =>
                w.Contains("é o DO KIT", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kitDir, true); }
    }

    [Fact]
    public void RouteA_PutsEverythingInExeFolder()
    {
        var plan = InstallPlanBuilder.Build(
            Profile(PeArchitecture.X64, GraphicsApi.D3D12), FullKit(), new InstallOptions());

        Assert.Empty(plan.Blockers);
        Assert.True(Targets(plan, @"game\dxgi.dll"));
        Assert.True(Targets(plan, @"game\dlss5-feed.addon64"));
        Assert.True(Targets(plan, @"game\renodx-dlss5.addon64"));
        Assert.True(Targets(plan, @"game\nvngx_dlssnr.dll"));
        // Nada de host64 em jogo 64-bit.
        Assert.DoesNotContain(plan.Actions,
            a => a.TargetPath?.Contains(@"host64", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void RouteA_SkipsFeederWhenGameHasNativeDlss()
    {
        var profile = Profile(PeArchitecture.X64, GraphicsApi.D3D12);
        profile.HasNativeDlss = true;
        var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());

        Assert.False(Targets(plan, "dlss5-feed.addon64"));
        Assert.True(Targets(plan, "renodx-dlss5.addon64"));
    }

    [Fact]
    public void RouteB_KeepsAddon64OutOfRoot()
    {
        var plan = InstallPlanBuilder.Build(
            Profile(PeArchitecture.X86, GraphicsApi.D3D11), FullKit(), new InstallOptions());

        Assert.True(Targets(plan, @"game\dlss5-feed.addon32"));
        Assert.True(Targets(plan, @"host64\renodx-dlss5.addon64"));
        Assert.True(Targets(plan, @"host64\nvngx_dlssnr.dll"));
        Assert.True(Targets(plan, @"host64\dxgi.dll"));

        // A armadilha do Tomb Raider: nenhum .addon64 nem nvngx_* na raiz.
        Assert.False(Targets(plan, @"game\renodx-dlss5.addon64"));
        Assert.False(Targets(plan, @"game\nvngx_dlssnr.dll"));
        Assert.False(Targets(plan, @"game\dlss5-feed.addon64"));
    }

    [Fact]
    public void RouteC_AddsDgVoodooToRendererFolder()
    {
        var profile = Profile(PeArchitecture.X86, GraphicsApi.D3D9);
        profile.RendererFolder = Path.Combine(GameRoot, "bin");   // variante Source
        var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());

        Assert.True(Targets(plan, @"bin\D3D9.dll"));
        Assert.Contains(plan.Actions, a => a.Kind == PlanActionKind.PatchDgVoodooConf
                                        && Norm(a.TargetPath).EndsWith(@"bin\dgVoodoo.conf", StringComparison.OrdinalIgnoreCase));
        // ReShade continua na pasta do EXE, nunca em bin\.
        Assert.True(Targets(plan, @"game\dxgi.dll"));
    }

    [Fact]
    public void RotaC_EmDirectX8_CopiaOWrapperD3D8()
    {
        // Max Payne 1 e a leva de jogos de 2001-2003 são DirectX 8. O dgVoodoo traz um
        // wrapper por API, e o jogo só carrega o que tem o nome certo: copiar D3D9.dll
        // num jogo D3D8 não intercepta nada, e o resultado é "instalou e não aconteceu".
        var plan = InstallPlanBuilder.Build(
            Profile(PeArchitecture.X86, GraphicsApi.D3D8), FullKit(), new InstallOptions());

        Assert.Empty(plan.Blockers);
        Assert.True(Targets(plan, @"\D3D8.dll"));
        Assert.False(Targets(plan, @"\D3D9.dll"));
        Assert.Contains(plan.Actions, a => a.SourcePath == @"C:\kit\MS\x86\D3D8.dll");
    }

    [Fact]
    public void DirectX8SemOWrapperNoKitViraBloqueio()
    {
        var kit = FullKit();
        kit.DgVoodooD3D8X86 = null;

        var plan = InstallPlanBuilder.Build(
            Profile(PeArchitecture.X86, GraphicsApi.D3D8), kit, new InstallOptions());

        Assert.Contains(plan.Blockers, b => b.Contains("D3D8.dll"));
    }

    [Fact]
    public void OpenGL_InstalaOReShadeComoOpengl32()
    {
        var plan = InstallPlanBuilder.Build(
            Profile(PeArchitecture.X64, GraphicsApi.OpenGL), FullKit(), new InstallOptions());

        Assert.Empty(plan.Blockers);
        Assert.True(Targets(plan, @"\opengl32.dll"));
        Assert.False(Targets(plan, @"\dxgi.dll"));

        // Está fora da matriz validada da spec: instala, mas dizendo isso na cara.
        Assert.Contains(plan.Warnings, w => w.Contains("OpenGL"));
    }

    [Fact]
    public void UnsupportedCombination_IsBlocked()
    {
        var plan = InstallPlanBuilder.Build(
            Profile(PeArchitecture.X86, GraphicsApi.Vulkan), FullKit(), new InstallOptions());

        Assert.NotEmpty(plan.Blockers);
        Assert.False(plan.CanRun);
    }

    [Fact]
    public void MissingKitPieces_BecomeBlockers()
    {
        var kit = FullKit();
        kit.NvngxDlssnr = null;
        var plan = InstallPlanBuilder.Build(
            Profile(PeArchitecture.X64, GraphicsApi.D3D12), kit, new InstallOptions());

        Assert.Contains(plan.Blockers, b => b.Contains("nvngx_dlssnr", StringComparison.OrdinalIgnoreCase));
        Assert.False(plan.CanRun);
    }

    [Fact]
    public void RegistryOverride_IsOptional()
    {
        var kit = FullKit();
        var withOverride = InstallPlanBuilder.Build(
            Profile(PeArchitecture.X64, GraphicsApi.D3D12), kit,
            new InstallOptions { ApplyRegistryOverride = true });
        var without = InstallPlanBuilder.Build(
            Profile(PeArchitecture.X64, GraphicsApi.D3D12), kit,
            new InstallOptions { ApplyRegistryOverride = false });

        Assert.Contains(withOverride.Actions, a => a.Kind == PlanActionKind.RegistryOverride);
        Assert.DoesNotContain(without.Actions, a => a.Kind == PlanActionKind.RegistryOverride);
    }
}

public class PeFileTests
{
    [Fact]
    public void GetArchitecture_ReadsRealWindowsBinaries()
    {
        // notepad.exe é x64 em qualquer Windows x64 moderno; SysWOW64 guarda os x86.
        var sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        if (File.Exists(sys32))
            Assert.Equal(PeArchitecture.X64, PeFile.GetArchitecture(sys32));

        var wow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "notepad.exe");
        if (File.Exists(wow))
            Assert.Equal(PeArchitecture.X86, PeFile.GetArchitecture(wow));
    }

    [Fact]
    public void GetArchitecture_ReturnsUnknownForNonPe()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "isso não é um executável");
            Assert.Equal(PeArchitecture.Unknown, PeFile.GetArchitecture(tmp));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void GetImportedDlls_FindsKnownImports()
    {
        var sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        if (!File.Exists(sys32)) return;

        var imports = PeFile.GetImportedDlls(sys32);
        Assert.NotEmpty(imports);
        Assert.All(imports, i => Assert.Equal(i.ToLowerInvariant(), i));
    }
}

public class KitResolverTests
{
    [Fact]
    public void Resolve_FindsPiecesInMessyLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "dlss5kit_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Reproduz a bagunça real: pastas aninhadas, cópias duplicadas, variantes arm64.
            var nested = Path.Combine(root, "DLSS5 - Arquivos ATUALIZADOS", "Nova pasta");
            var shaders = Path.Combine(root, "DLSS5 - Arquivos ATUALIZADOS", "reshade-shaders", "Shaders");
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(shaders);
            Directory.CreateDirectory(Path.Combine(nested, "dgVoodoo2_87_3", "MS", "x86"));
            Directory.CreateDirectory(Path.Combine(nested, "dgVoodoo2_87_3", "MS", "arm64x"));
            Directory.CreateDirectory(Path.Combine(nested, "dgVoodoo2_87_3", "3Dfx", "x86"));

            File.WriteAllText(Path.Combine(root, "DLSS5 - Arquivos ATUALIZADOS", "nvngx_dlssnr.dll"), "x");
            File.WriteAllText(Path.Combine(root, "DLSS5 - Arquivos ATUALIZADOS", "nvngx_dlss.dll"), "x");
            File.WriteAllText(Path.Combine(root, "DLSS5 - Arquivos ATUALIZADOS", "renodx-dlss5.addon64"), "x");
            File.WriteAllText(Path.Combine(root, "DLSS5 - Arquivos ATUALIZADOS", "dlss5-feed.addon64"), "x");
            File.WriteAllText(Path.Combine(nested, "dlss5-feed.addon32"), "x");
            File.WriteAllText(Path.Combine(nested, "dlss5-feed-host64.exe"), "x");
            File.WriteAllText(Path.Combine(shaders, "DLSS5_Feed.fx"), "technique DLSS5_Feed");
            File.WriteAllText(Path.Combine(shaders, "MotionEstimation.fx"), "technique DRME");
            File.WriteAllText(Path.Combine(shaders, "MartysMods_LAUNCHPAD.fx"), "technique MartysMods_Launchpad");
            File.WriteAllText(Path.Combine(shaders, "ReShade.fxh"), "x");
            File.WriteAllText(Path.Combine(nested, "dgVoodoo2_87_3", "dgVoodoo.conf"), "[DirectX]\nVRAM = 256\n");
            File.WriteAllText(Path.Combine(nested, "dgVoodoo2_87_3", "dgVoodooCpl.exe"), "x");

            var inv = KitResolver.Resolve(root);

            Assert.NotNull(inv.NvngxDlssnr);
            Assert.NotNull(inv.NvngxDlss);
            Assert.NotNull(inv.RenodxAddon64);
            Assert.NotNull(inv.FeedAddon64);
            Assert.NotNull(inv.FeedAddon32);
            Assert.NotNull(inv.FeedHost64Exe);
            Assert.NotNull(inv.ShadersDir);
            Assert.True(inv.HasDrme);
            Assert.True(inv.HasLaunchpad);
            Assert.NotNull(inv.DgVoodooConf);
            Assert.EndsWith("reshade-shaders", inv.ShadersDir!);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsLfsPointer_DetectsPointerFiles()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp,
                "version https://git-lfs.github.com/spec/v1\noid sha256:abc\nsize 123\n");
            Assert.True(KitResolver.IsLfsPointer(tmp));

            File.WriteAllText(tmp, "conteúdo real qualquer");
            Assert.False(KitResolver.IsLfsPointer(tmp));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void MissingFor_RouteC_RequiresDgVoodoo()
    {
        var inv = new KitInventory
        {
            KitRoot = @"C:\kit",
            NvngxDlssnr = "a", NvngxDlss = "b", RenodxAddon64 = "c",
            FeedAddon32 = "d", FeedHost64Exe = "e",
            DxgiX86 = "f", DxgiX64 = "g",
            ShadersDir = "h", HasLaunchpad = true,
        };
        var missing = inv.MissingFor(InstallRoute.C, nativeDlss: false);
        Assert.Contains(missing, m => m.Contains("D3D9", StringComparison.OrdinalIgnoreCase));
    }
}

public class GameDetectorTests
{
    /// <summary>PE mínimo: só o que o GetArchitecture lê (MZ, offset em 0x3C, "PE\0\0", machine).</summary>
    private static void WriteFakePe(string path, PeArchitecture arch, int totalSize)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[Math.Max(totalSize, 0x100)];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        const int peOffset = 0x80;
        BitConverter.GetBytes(peOffset).CopyTo(bytes, 0x3C);
        bytes[peOffset] = (byte)'P';
        bytes[peOffset + 1] = (byte)'E';
        ushort machine = arch == PeArchitecture.X64 ? (ushort)0x8664 : (ushort)0x014C;
        BitConverter.GetBytes(machine).CopyTo(bytes, peOffset + 4);
        File.WriteAllBytes(path, bytes);
    }

    private static string NewGameDir() =>
        Path.Combine(Path.GetTempPath(), "dlss5-game-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PicksTheUnrealShippingBinary_NotTheRootShim()
    {
        // O exe da raiz de um jogo Unreal só relança o binário real. Instalar ao lado dele
        // deixa o ReShade numa pasta que o processo que renderiza nunca consulta — e aí o
        // ReShade.log nem chega a existir.
        var dir = NewGameDir();
        try
        {
            WriteFakePe(Path.Combine(dir, "Duskfade.exe"), PeArchitecture.X64, 300 * 1024);
            var shipping = Path.Combine(dir, "Duskfade", "Binaries", "Win64", "Duskfade-Win64-Shipping.exe");
            WriteFakePe(shipping, PeArchitecture.X64, 2 * 1024 * 1024);
            WriteFakePe(Path.Combine(dir, "Engine", "Binaries", "Win64", "CrashReportClient.exe"),
                PeArchitecture.X64, 5 * 1024 * 1024);

            var detection = GameDetector.Detect(dir);

            Assert.Equal(shipping, detection.Profile.RealExePath);
            Assert.Contains(detection.Notes, n => n.Contains("Unreal", StringComparison.OrdinalIgnoreCase));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void KeepsTheRootExeWhenThereIsNoEngineLayout()
    {
        // Caso comum (Unity e afins): o exe da raiz é o certo, e um instalador enterrado
        // em _CommonRedist não pode roubar a escolha.
        var dir = NewGameDir();
        try
        {
            var root = Path.Combine(dir, "Jogo.exe");
            WriteFakePe(root, PeArchitecture.X64, 600 * 1024);
            WriteFakePe(Path.Combine(dir, "_CommonRedist", "vcredist", "vcredist_x64.exe"),
                PeArchitecture.X64, 10 * 1024 * 1024);

            var detection = GameDetector.Detect(dir);

            Assert.Equal(root, detection.Profile.RealExePath);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class ReShadeExtractorTests
{
    /// <summary>Monta um "instalador": bytes de lixo (como um PE) + um ZIP anexado no fim.</summary>
    private static string WriteSetupWithAppendedZip(string entryName, byte[] entryContent, int prefixSize)
    {
        byte[] zipBytes;
        using (var mem = new MemoryStream())
        {
            using (var zip = new System.IO.Compression.ZipArchive(
                       mem, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry(entryName);
                using var w = entry.Open();
                w.Write(entryContent, 0, entryContent.Length);
            }
            zipBytes = mem.ToArray();
        }

        var prefix = new byte[prefixSize];
        new Random(1234).NextBytes(prefix);
        // Uma assinatura falsa de local file header no meio do "PE", como no instalador real.
        prefix[100] = 0x50; prefix[101] = 0x4B; prefix[102] = 0x03; prefix[103] = 0x04;

        var dir = Path.Combine(Path.GetTempPath(), "dlss5-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "ReShade_Setup_Fake.exe");
        using (var fs = File.Create(path))
        {
            fs.Write(prefix, 0, prefix.Length);
            fs.Write(zipBytes, 0, zipBytes.Length);
        }
        return path;
    }

    [Fact]
    public void ExtractTrailingZip_ReadsZipAppendedAfterAPrefix()
    {
        // Reproduz o instalador do ReShade: o offset do diretório central gravado no EOCD é
        // relativo ao início do ZIP, não ao do arquivo. Sem corrigir esse deslocamento, o
        // ZipArchive falha com "number of entries ... does not correspond".
        var content = System.Text.Encoding.ASCII.GetBytes("conteudo da dll do reshade");
        var setup = WriteSetupWithAppendedZip("ReShade32.dll", content, prefixSize: 4096);

        try
        {
            using var stream = ReShadeExtractor.ExtractTrailingZip(setup);
            Assert.NotNull(stream);

            using var zip = new System.IO.Compression.ZipArchive(
                stream!, System.IO.Compression.ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => e.Name == "ReShade32.dll");
            Assert.NotNull(entry);

            using var reader = new StreamReader(entry!.Open());
            Assert.Equal("conteudo da dll do reshade", reader.ReadToEnd());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(setup)!, true);
        }
    }

    [Fact]
    public void PlainZipFileOpen_DoesNotHandleTheAppendedZip()
    {
        // A armadilha que causou o bug: abrir o arquivo direto falha, mas em .NET 8 o erro
        // só aparece ao TOCAR nas entradas, porque o diretório central é lido sob demanda —
        // e aí ele escapa de um catch posto só em volta do open. Não fixamos em qual dos dois
        // pontos a exceção sai; o que importa é que o caminho direto não serve.
        var setup = WriteSetupWithAppendedZip("ReShade32.dll", new byte[] { 1, 2, 3 }, prefixSize: 4096);
        try
        {
            Assert.Throws<InvalidDataException>(() =>
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(setup);
                _ = zip.Entries.Count;
            });
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(setup)!, true);
        }
    }
}

public class ForbiddenFilesTests
{
    private static string PastaComArquivos(params string[] nomes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5game_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var n in nomes) File.WriteAllText(Path.Combine(dir, n), "x");
        return dir;
    }

    [Fact]
    public void NuncaRemoveArquivoDoProprioJogo()
    {
        // Um teste real em Forza mostrou o estrago: apagar as sl.*.dll fez as opções de
        // DLSS sumirem do menu do jogo. Elas nem existem no kit — quando estão na pasta,
        // são do jogo. O mesmo vale para a nvngx_dlssg.dll (frame generation).
        var dir = PastaComArquivos("sl.interposer.dll", "sl.dlss.dll", "nvngx_dlssg.dll",
                                   "ReShade_Setup_6.8.0_Addon.exe", "jogo.exe");
        try
        {
            var remover = ForbiddenFiles.FindPresent(dir).Select(Path.GetFileName).ToList();

            Assert.DoesNotContain("sl.interposer.dll", remover);
            Assert.DoesNotContain("sl.dlss.dll", remover);
            Assert.DoesNotContain("nvngx_dlssg.dll", remover);
            Assert.DoesNotContain("jogo.exe", remover);

            // O instalador do ReShade não tem o que fazer na pasta do jogo.
            Assert.Contains("ReShade_Setup_6.8.0_Addon.exe", remover);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ListaOsArquivosDoJogoParaAvisar()
    {
        var dir = PastaComArquivos("sl.interposer.dll", "nvngx_dlssg.dll", "jogo.exe");
        try
        {
            var doJogo = ForbiddenFiles.FindGameOwned(dir);
            Assert.Contains("sl.interposer.dll", doJogo);
            Assert.Contains("nvngx_dlssg.dll", doJogo);
            Assert.DoesNotContain("jogo.exe", doJogo);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class ReversaoTests
{
    [Fact]
    public void DevolveBackupOrfaoSemPrecisarDoManifesto()
    {
        // O caso do amigo: instalação feita com outro exe apontado, manifesto não
        // encontrado, e o jogo ficou sem os arquivos movidos para .dlss5bak.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5rev_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var alvo = Path.Combine(dir, "sl.interposer.dll");
            File.WriteAllText(alvo + InstallerEngine.BackupSuffix, "conteudo original");

            new InstallerEngine(_ => { }).RestaurarBackupsOrfaos(dir);

            Assert.True(File.Exists(alvo));
            Assert.Equal("conteudo original", File.ReadAllText(alvo));
            Assert.False(File.Exists(alvo + InstallerEngine.BackupSuffix));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ConfereEDenunciaOQueNaoSaiu()
    {
        // Forza: o dxgi.dll continuou na pasta depois de desinstalar e o overlay do
        // ReShade seguiu abrindo no jogo. A falha existia no log como um aviso solto —
        // agora ela é devolvida para a interface poder mostrar.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5rev_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "...ReShade 6.8.0...");
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");
            File.WriteAllText(Path.Combine(dir, "forzahorizon6.exe"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "reshade-shaders"));

            var sobras = new InstallerEngine(_ => { }).ConferirSobras(dir);

            Assert.Contains(sobras, f => f.EndsWith("dxgi.dll", StringComparison.Ordinal));
            Assert.Contains(sobras, f => f.EndsWith("renodx-dlss5.addon64", StringComparison.Ordinal));
            Assert.Contains(sobras, f => f.Contains("reshade-shaders", StringComparison.Ordinal));

            // O executável do jogo não é nosso e não pode entrar na lista.
            Assert.DoesNotContain(sobras, f => f.EndsWith("forzahorizon6.exe", StringComparison.Ordinal));

            // Um dxgi.dll que NÃO é o ReShade (do jogo ou de outro mod) não é sobra nossa.
            File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "wrapper de outro mod");
            Assert.DoesNotContain(new InstallerEngine(_ => { }).ConferirSobras(dir),
                f => f.EndsWith("dxgi.dll", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NaoAcusaSobraNumaPastaLimpa()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5rev_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "forzahorizon6.exe"), "x");
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "sl.interposer.dll"), "x");

            Assert.Empty(new InstallerEngine(_ => { }).ConferirSobras(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReversaoRemoveOTransplanteQueNaoEstaNoManifesto()
    {
        // O transplante é obra de instalação ANTIGA: não consta no manifesto atual, não
        // tem backup para devolver, e ficava na pasta para sempre — o motor do Onimusha
        // carrega esse DLL na inicialização e congela antes da janela. Com o gabarito do
        // kit, a reversão o reconhece e remove.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5rev_" + Guid.NewGuid().ToString("N"));
        var kit = Path.Combine(Path.GetTempPath(), "dlss5kit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(kit);
        try
        {
            File.WriteAllText(Path.Combine(kit, "nvngx_dlss.dll"), "bytes do kit");
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll"), "bytes do kit");

            var engine = new InstallerEngine(_ => { })
            {
                NvngxDlssDoKit = Path.Combine(kit, "nvngx_dlss.dll"),
            };
            var manifesto = new InstallManifest { GameFolder = dir, ExeFolder = dir };
            var sobras = engine.Revert(manifesto, removeRegistryOverride: false).Sobras;

            Assert.False(File.Exists(Path.Combine(dir, "nvngx_dlss.dll")));
            Assert.Empty(sobras);
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void ReversaoDevolveOBackupEDeixaOOriginalQuieto()
    {
        // Quando existe backup, a própria reversão repõe o original do jogo — e o
        // gabarito não pode apagar o que acabou de voltar: os bytes já não são do kit.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5rev_" + Guid.NewGuid().ToString("N"));
        var kit = Path.Combine(Path.GetTempPath(), "dlss5kit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(kit);
        try
        {
            File.WriteAllText(Path.Combine(kit, "nvngx_dlss.dll"), "bytes do kit");
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll"), "bytes do kit");
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll" + InstallerEngine.BackupSuffix),
                "original do jogo");

            var engine = new InstallerEngine(_ => { })
            {
                NvngxDlssDoKit = Path.Combine(kit, "nvngx_dlss.dll"),
            };
            var manifesto = new InstallManifest { GameFolder = dir, ExeFolder = dir };
            engine.Revert(manifesto, removeRegistryOverride: false);

            Assert.True(File.Exists(Path.Combine(dir, "nvngx_dlss.dll")));
            Assert.Equal("original do jogo", File.ReadAllText(Path.Combine(dir, "nvngx_dlss.dll")));
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void ConferirSobrasDenunciaOTransplanteQueResistiu()
    {
        // Se o arquivo estava em uso e a remoção falhou, ele precisa aparecer como
        // sobra — é o que liga o aviso e a oferta de faxina completa na interface.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5rev_" + Guid.NewGuid().ToString("N"));
        var kit = Path.Combine(Path.GetTempPath(), "dlss5kit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(kit);
        try
        {
            File.WriteAllText(Path.Combine(kit, "nvngx_dlss.dll"), "bytes do kit");
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll"), "bytes do kit");

            var engine = new InstallerEngine(_ => { })
            {
                NvngxDlssDoKit = Path.Combine(kit, "nvngx_dlss.dll"),
            };
            var sobras = engine.ConferirSobras(dir);

            Assert.Contains(sobras, f => f.EndsWith("nvngx_dlss.dll", StringComparison.Ordinal));

            // Sem gabarito (ou com o DLL do jogo) o mesmo arquivo não é sobra.
            Assert.DoesNotContain(new InstallerEngine(_ => { }).ConferirSobras(dir),
                f => f.EndsWith("nvngx_dlss.dll", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void ApagaOsArquivosQueOReShadeCriaAoRodar()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5rev_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var n in new[] { "ReShade.log", "dlss5-feed.log", "dlss5-feed.cfg" })
                File.WriteAllText(Path.Combine(dir, n), "x");
            File.WriteAllText(Path.Combine(dir, "jogo.exe"), "x");

            new InstallerEngine(_ => { }).LimparRestosDeExecucao(dir);

            Assert.False(File.Exists(Path.Combine(dir, "ReShade.log")));
            Assert.False(File.Exists(Path.Combine(dir, "dlss5-feed.log")));
            Assert.False(File.Exists(Path.Combine(dir, "dlss5-feed.cfg")));
            Assert.True(File.Exists(Path.Combine(dir, "jogo.exe")));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class ManifestTests
{
    [Fact]
    public void Manifest_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5man_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var m = new InstallManifest
            {
                GameFolder = @"C:\game",
                ExeFolder = dir,
                Route = "B",
                RegistryOverrideApplied = true,
                RegistryOverrideAppliedUtc = DateTime.UtcNow,
            };
            m.AddedFiles.Add(@"C:\game\dxgi.dll");
            m.BackedUpFiles[@"C:\game\ReShade.ini"] = @"C:\game\ReShade.ini.dlss5bak";
            m.Save(dir);

            var loaded = InstallManifest.Load(dir);
            Assert.NotNull(loaded);
            Assert.Equal("B", loaded!.Route);
            Assert.True(loaded.RegistryOverrideApplied);
            Assert.Contains(@"C:\game\dxgi.dll", loaded.AddedFiles);
            Assert.True(loaded.BackedUpFiles.ContainsKey(@"C:\game\ReShade.ini"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_ReturnsNullWhenAbsent()
    {
        Assert.Null(InstallManifest.Load(Path.GetTempPath() + Guid.NewGuid().ToString("N")));
    }
}

public class SymptomDiagnoserTests
{
    [Fact]
    public void Diagnose_MapsKnownLogStrings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5diag_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "dlss5-feed.log"),
                "info: no known texMotionVectors provider found\nwarn: WAITING FOR NGX MODULES\n");

            var profile = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "g.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
            };

            var diags = SymptomDiagnoser.Diagnose(profile);
            Assert.Contains(diags, d => d.Symptom.Contains("motion vectors", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(diags, d => d.Symptom.Contains("NGX MODULES", StringComparison.OrdinalIgnoreCase));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Diagnose_FlagsAddon64InRootFor32BitGame()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5diag2_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");

            var profile = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "g.exe"),
                Architecture = PeArchitecture.X86,
                Api = GraphicsApi.D3D11,
            };

            var diags = SymptomDiagnoser.Diagnose(profile);
            Assert.Contains(diags, d => d.Symptom.Contains("Add-ons", StringComparison.OrdinalIgnoreCase));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class NativeDlssDetectorTests
{
    private static string Pasta(params string[] nomes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5nat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var n in nomes) File.WriteAllText(Path.Combine(dir, n), "x");
        return dir;
    }

    [Fact]
    public void NvngxDlssJuntoDaNossaInstalacaoSemManifestoNaoDecide()
    {
        // Instalação nossa presente (ReShade.ini) e manifesto sumido: o arquivo tanto
        // pode ser o que copiamos quanto o do jogo — peso reduzido, não decide sozinho.
        var dir = Pasta("nvngx_dlss.dll", "ReShade.ini", "jogo.exe");
        try
        {
            Assert.False(NativeDlssDetector.Detect(dir, dir, null).Present);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NvngxDlssSozinhoSemInstalacaoNossaEhDoJogo()
    {
        // Caso GTA 5: nvngx_dlss.dll na pasta e NENHUM arquivo nosso por perto. O
        // instalador nunca deixa esse arquivo sozinho para trás, então ele veio com o
        // jogo. Errar para o lado "não tem DLSS" custa caro: o Feeder entra junto do
        // DLSS do jogo e mexer no menu TRAVA (Forza, GTA 5). Errar para o lado "tem"
        // só deixa o RenoDX esperando — não quebra nada.
        var dir = Pasta("nvngx_dlss.dll", "jogo.exe");
        try
        {
            var d = NativeDlssDetector.Detect(dir, dir, null);
            Assert.True(d.Present);
            Assert.Contains(d.Clues, c => c.Texto.Contains("veio com o jogo"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ArquivoListadoNoManifestoNaoViraPista()
    {
        var dir = Pasta("nvngx_dlss.dll", "sl.interposer.dll", "jogo.exe");
        try
        {
            // Sem manifesto, os dois juntos passariam do limiar.
            Assert.True(NativeDlssDetector.Detect(dir, dir, null).Present);

            // Com o manifesto dizendo que fomos nós que pusemos o nvngx_dlss.dll ali,
            // sobra só o Streamline — que pode estar presente só pelo Reflex.
            var manifesto = new InstallManifest { GameFolder = dir, ExeFolder = dir };
            manifesto.AddedFiles.Add(Path.Combine(dir, "nvngx_dlss.dll"));
            Assert.False(NativeDlssDetector.Detect(dir, dir, null, manifesto).Present);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReconheceStreamlineDoJogo()
    {
        // sl.dlss.dll não existe no kit: se está na pasta, veio com o jogo.
        var dir = Pasta("sl.dlss.dll", "sl.interposer.dll", "jogo.exe");
        try
        {
            Assert.True(NativeDlssDetector.Detect(dir, dir, null).Present);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReconhecePeloTextoDoExe()
    {
        // O exe do jogo é a única evidência que a instalação nunca altera.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5nat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var exe = Path.Combine(dir, "jogo.exe");
            var buffer = new byte[32 * 1024];
            var marcador = System.Text.Encoding.ASCII.GetBytes("NVSDK_NGX_D3D12_Init");
            Array.Copy(marcador, 0, buffer, 4096, marcador.Length);
            File.WriteAllBytes(exe, buffer);

            var d = NativeDlssDetector.Detect(dir, dir, exe);
            Assert.True(d.Present);
            Assert.Contains(d.Clues, c => c.Texto.Contains("NVSDK_NGX_D3D12_Init"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IgnoraOQueEstaDentroDeHost64()
    {
        // host64\ é pasta nossa: o nvngx_dlss.dll de lá é sempre o do kit.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5nat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "host64"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "host64", "nvngx_dlss.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "host64", "sl.dlss.dll"), "x");

            Assert.False(NativeDlssDetector.Detect(dir, dir, null).Present);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class FaxinaTests
{
    private static string NovaPasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5faxina_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Escrever(string dir, string nome, string conteudo = "x") =>
        File.WriteAllText(Path.Combine(dir, nome), conteudo);

    [Fact]
    public void RemoveOQueEhNossoESoOQueEhNosso()
    {
        // O botão de socorro: funciona sem manifesto, varrendo a pasta pelo nome dos
        // arquivos. O critério tem que ser conservador — arquivo do jogo fica.
        var dir = NovaPasta();
        try
        {
            Escrever(dir, "renodx-dlss5.addon64");
            Escrever(dir, "nvngx_dlssnr.dll");
            Escrever(dir, "ReShade.ini");
            Escrever(dir, "nvngx_dlss.dll");          // fica: pode ser do jogo (o kit não sobrescreve mais)
            Escrever(dir, "dxgi.dll", "...ReShade 6.8.0...");
            Escrever(dir, "sl.dlss.dll");             // do jogo
            Escrever(dir, "nvngx_dlssg.dll");         // do jogo
            Escrever(dir, "jogo.exe");

            var sobras = new InstallerEngine(_ => { }).LimpezaTotal(dir);

            Assert.Empty(sobras);
            Assert.False(File.Exists(Path.Combine(dir, "renodx-dlss5.addon64")));
            Assert.False(File.Exists(Path.Combine(dir, "nvngx_dlssnr.dll")));
            Assert.False(File.Exists(Path.Combine(dir, "ReShade.ini")));
            Assert.False(File.Exists(Path.Combine(dir, "dxgi.dll")));

            Assert.True(File.Exists(Path.Combine(dir, "nvngx_dlss.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "sl.dlss.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "nvngx_dlssg.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "jogo.exe")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RemoveOpengl32DoReShadeMasNaoODoWindows()
    {
        // Em jogo OpenGL o ReShade é instalado como opengl32.dll — o mesmo nome de uma
        // DLL do sistema, então o critério continua sendo o texto dentro do arquivo.
        var dir = NovaPasta();
        try
        {
            Escrever(dir, "opengl32.dll", "...ReShade 6.8.0...");
            new InstallerEngine(_ => { }).LimpezaTotal(dir);
            Assert.False(File.Exists(Path.Combine(dir, "opengl32.dll")));

            Escrever(dir, "opengl32.dll", "driver de verdade");
            new InstallerEngine(_ => { }).LimpezaTotal(dir);
            Assert.True(File.Exists(Path.Combine(dir, "opengl32.dll")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NaoApagaDxgiQueNaoEhDoReShade()
    {
        // dxgi.dll é nome genérico. Sem o texto do ReShade dentro, não é nosso.
        var dir = NovaPasta();
        try
        {
            Escrever(dir, "dxgi.dll", "outra coisa qualquer");
            Escrever(dir, "jogo.exe");

            new InstallerEngine(_ => { }).LimpezaTotal(dir);

            Assert.True(File.Exists(Path.Combine(dir, "dxgi.dll")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FaxinaSemGabaritoDoKitNaoApagaONvngxDlss()
    {
        // Sem o nvngx_dlss.dll do kit para comparar não existe prova — e sem prova o
        // arquivo é tratado como do JOGO e fica, mesmo cercado dos nossos addons.
        var dir = NovaPasta();
        try
        {
            Escrever(dir, "renodx-dlss5.addon64");
            Escrever(dir, "nvngx_dlssnr.dll");
            Escrever(dir, "nvngx_dlss.dll", "o do jogo");

            var sobras = new InstallerEngine(_ => { }).LimpezaTotal(dir);

            Assert.Empty(sobras);
            Assert.True(File.Exists(Path.Combine(dir, "nvngx_dlss.dll")));
            Assert.Equal("o do jogo", File.ReadAllText(Path.Combine(dir, "nvngx_dlss.dll")));
            Assert.False(File.Exists(Path.Combine(dir, "renodx-dlss5.addon64")));
            Assert.False(File.Exists(Path.Combine(dir, "nvngx_dlssnr.dll")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FaxinaRemoveOTransplanteProvadoPeloGabarito()
    {
        // O beco do Onimusha: o nvngx_dlss.dll da pasta é byte a byte o DO KIT —
        // transplante de instalação antiga — e a verificação de integridade nem sempre
        // repõe o original (em demo o depot pode não cobrir o arquivo). Com o gabarito
        // do kit a prova é absoluta, e aí (e só aí) a faxina o remove.
        var dir = NovaPasta();
        var kit = NovaPasta();
        try
        {
            Escrever(kit, "nvngx_dlss.dll", "bytes do kit");
            Escrever(dir, "nvngx_dlss.dll", "bytes do kit");
            Escrever(dir, "renodx-dlss5.addon64");

            var engine = new InstallerEngine(_ => { })
            {
                NvngxDlssDoKit = Path.Combine(kit, "nvngx_dlss.dll"),
            };
            var sobras = engine.LimpezaTotal(dir);

            Assert.Empty(sobras);
            Assert.False(File.Exists(Path.Combine(dir, "nvngx_dlss.dll")));
            Assert.False(File.Exists(Path.Combine(dir, "renodx-dlss5.addon64")));
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void FaxinaComGabaritoMantemODllDoJogo()
    {
        // Bytes diferentes do kit não provam nada: pode ser o DLL genuíno do jogo,
        // e nele o programa não encosta.
        var dir = NovaPasta();
        var kit = NovaPasta();
        try
        {
            Escrever(kit, "nvngx_dlss.dll", "bytes do kit");
            Escrever(dir, "nvngx_dlss.dll", "o do jogo, outra versão");
            Escrever(dir, "renodx-dlss5.addon64");

            var engine = new InstallerEngine(_ => { })
            {
                NvngxDlssDoKit = Path.Combine(kit, "nvngx_dlss.dll"),
            };
            var sobras = engine.LimpezaTotal(dir);

            Assert.Empty(sobras);
            Assert.True(File.Exists(Path.Combine(dir, "nvngx_dlss.dll")));
            Assert.Equal("o do jogo, outra versão", File.ReadAllText(Path.Combine(dir, "nvngx_dlss.dll")));
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void BackupDevolvidoGanhaDoGabarito()
    {
        // Ordem importa: primeiro os backups voltam ao lugar, depois a prova do
        // gabarito decide. Se o backup devolveu o original do jogo, os bytes já não
        // batem com o kit e o arquivo fica.
        var dir = NovaPasta();
        var kit = NovaPasta();
        try
        {
            Escrever(kit, "nvngx_dlss.dll", "bytes do kit");
            Escrever(dir, "nvngx_dlss.dll", "bytes do kit"); // transplante por cima
            Escrever(dir, "nvngx_dlss.dll" + InstallerEngine.BackupSuffix, "original do jogo");

            var engine = new InstallerEngine(_ => { })
            {
                NvngxDlssDoKit = Path.Combine(kit, "nvngx_dlss.dll"),
            };
            var sobras = engine.LimpezaTotal(dir);

            Assert.Empty(sobras);
            Assert.True(File.Exists(Path.Combine(dir, "nvngx_dlss.dll")));
            Assert.Equal("original do jogo", File.ReadAllText(Path.Combine(dir, "nvngx_dlss.dll")));
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void ArquivoDevolvidoDoBackupNaoEhApagadoEmSeguida()
    {
        // O jogo tinha o próprio nvngx_dlss.dll e a instalação o sobrescreveu. A faxina
        // devolve o original — e depois não pode apagá-lo só porque o nome está na lista.
        var dir = NovaPasta();
        try
        {
            Escrever(dir, "renodx-dlss5.addon64");
            Escrever(dir, "nvngx_dlss.dll", "copia do kit");
            Escrever(dir, "nvngx_dlss.dll" + InstallerEngine.BackupSuffix, "original do jogo");

            var sobras = new InstallerEngine(_ => { }).LimpezaTotal(dir);

            Assert.Empty(sobras);
            Assert.True(File.Exists(Path.Combine(dir, "nvngx_dlss.dll")));
            Assert.Equal("original do jogo", File.ReadAllText(Path.Combine(dir, "nvngx_dlss.dll")));
            Assert.False(File.Exists(Path.Combine(dir, "renodx-dlss5.addon64")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AchaInstalacaoEmSubpastaDoJogo()
    {
        // Jogo Unreal: a instalação foi para Binaries\Win64, não para a raiz.
        var raiz = NovaPasta();
        var alvo = Path.Combine(raiz, "Jogo", "Binaries", "Win64");
        Directory.CreateDirectory(alvo);
        try
        {
            Escrever(alvo, "dlss5-feed.addon64");
            Escrever(alvo, "ReShade.ini");

            var achados = new InstallerEngine(_ => { }).EncontrarInstalacao(raiz);
            Assert.Equal(2, achados.Count);

            new InstallerEngine(_ => { }).LimpezaTotal(raiz);
            Assert.False(File.Exists(Path.Combine(alvo, "dlss5-feed.addon64")));
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void RemovePastasNossasPorInteiro()
    {
        var dir = NovaPasta();
        Directory.CreateDirectory(Path.Combine(dir, "host64"));
        Directory.CreateDirectory(Path.Combine(dir, "reshade-shaders", "Shaders"));
        try
        {
            Escrever(dir, "dlss5-feed.addon32");
            File.WriteAllText(Path.Combine(dir, "host64", "dlss5-feed-host64.exe"), "x");
            File.WriteAllText(Path.Combine(dir, "reshade-shaders", "Shaders", "DLSS5Feed.fx"), "x");

            var sobras = new InstallerEngine(_ => { }).LimpezaTotal(dir);

            Assert.Empty(sobras);
            Assert.False(Directory.Exists(Path.Combine(dir, "host64")));
            Assert.False(Directory.Exists(Path.Combine(dir, "reshade-shaders")));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class SteamGameTests
{
    private static string BibliotecaSteam(string installDir, string appId, out string pastaDoJogo)
    {
        var raiz = Path.Combine(Path.GetTempPath(), "dlss5steam_" + Guid.NewGuid().ToString("N"));
        var steamapps = Path.Combine(raiz, "steamapps");
        pastaDoJogo = Path.Combine(steamapps, "common", installDir);
        Directory.CreateDirectory(pastaDoJogo);
        File.WriteAllText(Path.Combine(steamapps, $"appmanifest_{appId}.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\t\"" + appId + "\"\n\t\"installdir\"\t\t\"" + installDir + "\"\n}\n");
        return raiz;
    }

    [Fact]
    public void AchaOAppIdPelaPastaDoJogo()
    {
        // Abrir o .exe direto num jogo com DRM da Steam dá "Application load error
        // 5:0000065434" — quem tem que lançar é o cliente da Steam.
        var raiz = BibliotecaSteam("Max Payne", "12140", out var jogo);
        try
        {
            Assert.Equal("12140", SteamGame.FindAppId(jogo));
            Assert.Equal("steam://rungameid/12140", SteamGame.RunUrl("12140"));
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void AchaOAppIdAPartirDeUmaSubpastaDoJogo()
    {
        // Jogo Unreal: o binário real mora em Binaries\Win64, vários níveis abaixo.
        var raiz = BibliotecaSteam("Duskfade", "999", out var jogo);
        try
        {
            var fundo = Path.Combine(jogo, "Duskfade", "Binaries", "Win64");
            Directory.CreateDirectory(fundo);
            Assert.Equal("999", SteamGame.FindAppId(fundo));
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void ForaDaSteamNaoInventaAppId()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5nosteam_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(SteamGame.FindAppId(dir));
            Assert.Null(SteamGame.FindAppId(null));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class IsolamentoTests
{
    private static string PastaInstalada()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5iso_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "D3D8.dll"), "dgvoodoo");
        File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "reshade");
        File.WriteAllText(Path.Combine(dir, "MaxPayne.exe"), "jogo");
        return dir;
    }

    [Fact]
    public void DesligaSoOSuspeitoDaVezESempreReligaOAnterior()
    {
        var dir = PastaInstalada();
        var iso = new Isolamento(_ => { });
        try
        {
            iso.Aplicar(EstadoIsolamento.SemDgVoodoo, dir, dir);
            Assert.False(File.Exists(Path.Combine(dir, "D3D8.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "dxgi.dll")));

            // Passar ao teste seguinte religa o dgVoodoo antes de desligar o ReShade,
            // senão os dois ficariam fora e o resultado não diria nada.
            iso.Aplicar(EstadoIsolamento.SemReShade, dir, dir);
            Assert.True(File.Exists(Path.Combine(dir, "D3D8.dll")));
            Assert.False(File.Exists(Path.Combine(dir, "dxgi.dll")));

            iso.Aplicar(EstadoIsolamento.Tudo, dir, dir);
            Assert.True(File.Exists(Path.Combine(dir, "D3D8.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "dxgi.dll")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NuncaApagaNada()
    {
        // Renomear é o ponto: o conteúdo tem que voltar intacto, e o exe do jogo nem é tocado.
        var dir = PastaInstalada();
        var iso = new Isolamento(_ => { });
        try
        {
            iso.Aplicar(EstadoIsolamento.SemDgVoodoo, dir, dir);
            Assert.True(File.Exists(Path.Combine(dir, "D3D8.dll" + Isolamento.Sufixo)));
            Assert.True(File.Exists(Path.Combine(dir, "MaxPayne.exe")));

            iso.ReligarTudo(dir);
            Assert.Equal("dgvoodoo", File.ReadAllText(Path.Combine(dir, "D3D8.dll")));
            Assert.Empty(Directory.EnumerateFiles(dir, "*" + Isolamento.Sufixo));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SemRenodxDesligaSoOAddonEDeixaOResto()
    {
        // O teste do GTA 5: jogo abre, mas ligar o DLSS no MENU trava — o suspeito é o
        // gancho do RenoDX na chamada de NGX do jogo. O degrau que faltava desliga SÓ
        // ele (raiz e host64), mantendo ReShade e Feeder vivos para o teste dizer algo.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5iso_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "host64"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "reshade");
            File.WriteAllText(Path.Combine(dir, "dlss5-feed.addon64"), "feeder");
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "renodx");
            File.WriteAllText(Path.Combine(dir, "host64", "renodx-dlss5.addon64"), "renodx");

            var iso = new Isolamento(_ => { });
            iso.Aplicar(EstadoIsolamento.SemRenodx, dir, dir);

            Assert.False(File.Exists(Path.Combine(dir, "renodx-dlss5.addon64")));
            Assert.False(File.Exists(Path.Combine(dir, "host64", "renodx-dlss5.addon64")));
            Assert.True(File.Exists(Path.Combine(dir, "dxgi.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "dlss5-feed.addon64")));

            iso.Aplicar(EstadoIsolamento.Tudo, dir, dir);
            Assert.Equal("renodx", File.ReadAllText(Path.Combine(dir, "renodx-dlss5.addon64")));
            Assert.Equal("renodx", File.ReadAllText(Path.Combine(dir, "host64", "renodx-dlss5.addon64")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DgVoodooEReShadePodemEstarEmPastasDiferentes()
    {
        // Engine Source: o dgVoodoo vai em bin\ e o ReShade fica na raiz.
        var raiz = Path.Combine(Path.GetTempPath(), "dlss5iso_" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(raiz, "bin");
        Directory.CreateDirectory(bin);
        try
        {
            File.WriteAllText(Path.Combine(raiz, "dxgi.dll"), "reshade");
            File.WriteAllText(Path.Combine(bin, "D3D9.dll"), "dgvoodoo");

            new Isolamento(_ => { }).Aplicar(EstadoIsolamento.SemDgVoodoo, raiz, bin);

            Assert.False(File.Exists(Path.Combine(bin, "D3D9.dll")));
            Assert.True(File.Exists(Path.Combine(raiz, "dxgi.dll")));
        }
        finally { Directory.Delete(raiz, true); }
    }
}

public class VereditoTests
{
    [Fact]
    public void JogoQueNemSemODgVoodooAbreNaoEhProblemaDaInstalacao()
    {
        var v = Isolamento.Veredito(abriuSemDgVoodoo: false, abriuSemReShade: null);
        Assert.Contains("NÃO é a instalação", v);
    }

    [Fact]
    public void OsDoisSozinhosFuncionandoApontaConflitoDeCarregamento()
    {
        // dgVoodoo fala com o D3D11 pelo dxgi.dll, que é justamente onde o ReShade entra.
        var v = Isolamento.Veredito(abriuSemDgVoodoo: true, abriuSemReShade: true);
        Assert.Contains("conflito de carregamento", v);
        Assert.Contains("dxgi.dll", v);
    }

    [Fact]
    public void SoOdgVoodooDerrubandoApontaOAdaptador()
    {
        var v = Isolamento.Veredito(abriuSemDgVoodoo: true, abriuSemReShade: false);
        Assert.Contains("dgVoodoo é rejeitado", v);
        Assert.Contains("D3D Software T&L", v);
    }

    [Fact]
    public void SemAsDuasRespostasNaoInventaConclusao()
    {
        Assert.Contains("Faltou responder", Isolamento.Veredito(null, null));
        Assert.Contains("Faltou responder", Isolamento.Veredito(true, null));
    }
}

public class TnLTests
{
    private const string Conf = """
        [DirectX]
        DisableAndPassThru                  = true
        VideoCard                           = internal3D
        VRAM                                = 256
        DisableD3DTnLDevice                 = false
        dgVoodooWatermark                   = false
        """;

    [Theory]
    [InlineData(true, "false")]
    [InlineData(false, "true")]
    public void AChaveEhEscritaAoContrarioDoNome(bool hardwareTnL, string esperado)
    {
        // O jogo escolhe entre "D3D Software T&L" e "D3D Hardware T&L" e grava a escolha;
        // pedindo hardware num adaptador que não oferece, recusa antes de abrir.
        var r = DgVoodooConfigurator.Patch(Conf, DgVoodooProfile.Legado, null, hardwareTnL);
        Assert.Contains("DisableD3DTnLDevice                 = " + esperado, r);
    }

    [Fact]
    public void SemPedirNadaAChaveNaoEhTocada()
    {
        var r = DgVoodooConfigurator.Patch(Conf, DgVoodooProfile.Legado);
        Assert.Contains("DisableD3DTnLDevice                 = false", r);
    }
}

public class ChaveAvulsaTests
{
    private const string Conf = """
        [Glide]
        DisableAndPassThru                  = true

        [DirectX]
        DisableAndPassThru                  = false
        VideoCard                           = internal3D
        """;

    [Fact]
    public void EscreveSoNaSecaoPedida()
    {
        // DisableAndPassThru existe em [Glide] e em [DirectX]: mexer na errada troca o
        // significado do teste inteiro.
        var r = DgVoodooConfigurator.DefinirChave(Conf, "DirectX", "DisableAndPassThru", "true");

        var glide = r.Split("[DirectX]")[0];
        var directx = r.Split("[DirectX]")[1];
        Assert.Contains("DisableAndPassThru                  = true", glide);
        Assert.Contains("DisableAndPassThru                  = true", directx);
        Assert.Contains("VideoCard                           = internal3D", r);
    }

    [Fact]
    public void LeOValorDaSecaoCerta()
    {
        Assert.Equal("false", DgVoodooConfigurator.LerChave(Conf, "DirectX", "DisableAndPassThru"));
        Assert.Equal("true", DgVoodooConfigurator.LerChave(Conf, "Glide", "DisableAndPassThru"));
        Assert.Null(DgVoodooConfigurator.LerChave(Conf, "DirectX", "NaoExiste"));
        Assert.Null(DgVoodooConfigurator.LerChave(Conf, "SecaoNenhuma", "VideoCard"));
    }

    [Fact]
    public void IdaEVoltaPreservaOResto()
    {
        var ligado = DgVoodooConfigurator.DefinirChave(Conf, "DirectX", "DisableAndPassThru", "true");
        var voltou = DgVoodooConfigurator.DefinirChave(ligado, "DirectX", "DisableAndPassThru", "false");

        Assert.Equal("false", DgVoodooConfigurator.LerChave(voltou, "DirectX", "DisableAndPassThru"));
        Assert.Equal("internal3D", DgVoodooConfigurator.LerChave(voltou, "DirectX", "VideoCard"));
    }
}

public class TeclaDoOverlayTests
{
    [Fact]
    public void LeDeVoltaOQueFoiGravado()
    {
        // A tecla fica guardada entre execuções: se mudou uma vez, vale para todo jogo
        // instalado depois. Ler do ini é o que permite dizer isso na tela em vez de
        // deixar o usuário achando que "o Home parou de funcionar".
        var ini = ReShadeConfigWriter.BuildReShadeIni(
            ReShadeConfigWriter.KeyHome, ctrl: true, shift: false, alt: true, "ReShadePreset.ini");

        var tecla = ReShadeConfigWriter.LerTeclaDoOverlay(ini);

        Assert.NotNull(tecla);
        Assert.Equal(ReShadeConfigWriter.KeyHome, tecla!.Value.VirtualKey);
        Assert.True(tecla.Value.Ctrl);
        Assert.False(tecla.Value.Shift);
        Assert.True(tecla.Value.Alt);
    }

    [Fact]
    public void IniSemALinhaNaoInventaTecla()
    {
        Assert.Null(ReShadeConfigWriter.LerTeclaDoOverlay("[GENERAL]\nEffectSearchPaths=.\\reshade-shaders\n"));
        Assert.Null(ReShadeConfigWriter.LerTeclaDoOverlay("KeyOverlay=nao-e-numero,0,0,0"));
    }

    [Fact]
    public void HomePuroEhReconhecidoComoHomePuro()
    {
        var ini = ReShadeConfigWriter.BuildReShadeIni(
            ReShadeConfigWriter.KeyHome, false, false, false, "ReShadePreset.ini");
        var tecla = ReShadeConfigWriter.LerTeclaDoOverlay(ini);

        Assert.NotNull(tecla);
        Assert.Equal(ReShadeConfigWriter.KeyHome, tecla!.Value.VirtualKey);
        Assert.False(tecla.Value.Ctrl || tecla.Value.Shift || tecla.Value.Alt);
    }
}

public class LogEmOutraPastaTests
{
    [Fact]
    public void LogNoutraPastaAponta_OExeErrado_EmVezDe_NaoCarregou()
    {
        // O ReShade grava o log ao lado da DLL carregada. Se ele existe em outra pasta do
        // jogo, o ReShade carregou — só que num processo que roda de lá, e é ali que a
        // instalação deveria ter ido. Dizer "abra o jogo uma vez" nesse caso é enganoso.
        var raiz = Path.Combine(Path.GetTempPath(), "dlss5log_" + Guid.NewGuid().ToString("N"));
        var real = Path.Combine(raiz, "Jogo", "Binaries", "Win64");
        Directory.CreateDirectory(real);
        try
        {
            File.WriteAllText(Path.Combine(real, "ReShade.log"), new string('x', 4000));

            var perfil = new GameProfile
            {
                GameFolder = raiz,
                RealExePath = Path.Combine(raiz, "atalho.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
            };

            var c7 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 7);

            Assert.Equal(CheckStatus.Fail, c7.State);
            Assert.Contains("outra pasta", c7.Title);
            Assert.Contains("Win64", c7.Detail);
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void SemLogNenhumContinuaPedindoParaAbrirOJogo()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "dlss5log_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(raiz);
        try
        {
            var perfil = new GameProfile
            {
                GameFolder = raiz,
                RealExePath = Path.Combine(raiz, "jogo.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
            };

            var c7 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 7);

            Assert.Equal(CheckStatus.Manual, c7.State);
            // A causa mais comum quando o jogo JÁ foi aberto tem que estar na dica.
            Assert.Contains("EA App", c7.FixHint!);
        }
        finally { Directory.Delete(raiz, true); }
    }
}

public class TransplanteTests
{
    private static (string Dir, string Kit) Pastas()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5tx_" + Guid.NewGuid().ToString("N"));
        var kit = Path.Combine(Path.GetTempPath(), "dlss5tk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(kit);
        return (dir, kit);
    }

    private static GameProfile PerfilComDlssNativo(string dir) => new()
    {
        GameFolder = dir,
        RealExePath = Path.Combine(dir, "jogo.exe"),
        Architecture = PeArchitecture.X64,
        Api = GraphicsApi.D3D12,
        RendererFolder = dir,
        HasNativeDlss = true,
    };

    [Fact]
    public void Checkpoint3AcusaOTransplante()
    {
        // O estado que enganava a verificação: o arquivo EXISTE, então o checkpoint
        // dizia "como você preferir" — mas ele é o DO KIT, e é por isso que o jogo nem
        // abre. Byte a byte igual = FALHA, com a rota de recuperação na dica.
        var (dir, kit) = Pastas();
        try
        {
            var kitDll = Path.Combine(kit, "nvngx_dlss.dll");
            File.WriteAllText(kitDll, "bytes do kit");
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll"), "bytes do kit");

            var c3 = CheckpointVerifier.Verify(PerfilComDlssNativo(dir), null, kitDll)
                .First(c => c.Number == 3);

            Assert.Equal(CheckStatus.Fail, c3.State);
            Assert.Contains("DO KIT", c3.Title);
            Assert.Contains("Verificar integridade", c3.FixHint!);
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void Checkpoint3AceitaODllDoJogo()
    {
        // Bytes diferentes do kit: é o DLL do jogo, e vale a regra "como você preferir".
        var (dir, kit) = Pastas();
        try
        {
            var kitDll = Path.Combine(kit, "nvngx_dlss.dll");
            File.WriteAllText(kitDll, "bytes do kit");
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll"), "o do jogo");

            var c3 = CheckpointVerifier.Verify(PerfilComDlssNativo(dir), null, kitDll)
                .First(c => c.Number == 3);

            // Caminho direto (o padrão em D3D12): o DLSS do jogo é a chamada interceptada.
            Assert.Equal(CheckStatus.Manual, c3.State);
            Assert.Contains("LIGADO", c3.Title);

            // Feeder por opção: com DLSS do jogo ligado os dois NGX colidem.
            var comFeeder = PerfilComDlssNativo(dir);
            comFeeder.PreferirFeeder = true;
            var c3f = CheckpointVerifier.Verify(comFeeder, null, kitDll).First(c => c.Number == 3);
            Assert.Equal(CheckStatus.Manual, c3f.State);
            Assert.Contains("DESLIGADO", c3f.Title);
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void Checkpoint3AcusaOTransplanteTambemNoCaminhoDireto()
    {
        // No caminho direto o DLL do jogo importa AINDA mais: é a chamada dele que o
        // RenoDX intercepta. Um transplante ou um DLL sumido tem que acusar nos dois.
        var (dir, kit) = Pastas();
        try
        {
            var kitDll = Path.Combine(kit, "nvngx_dlss.dll");
            File.WriteAllText(kitDll, "bytes do kit");
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll"), "bytes do kit");

            var perfil = PerfilComDlssNativo(dir);
            Assert.True(perfil.UsesRenodxDirectPath);
            var c3 = CheckpointVerifier.Verify(perfil, null, kitDll).First(c => c.Number == 3);
            Assert.Equal(CheckStatus.Fail, c3.State);
            Assert.Contains("DO KIT", c3.Title);

            File.Delete(Path.Combine(dir, "nvngx_dlss.dll"));
            var semArquivo = CheckpointVerifier.Verify(perfil, null, kitDll).First(c => c.Number == 3);
            Assert.NotEqual(CheckStatus.Fail, semArquivo.State);
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void Checkpoint3NaoAcusaSumicoDeArquivoQueOJogoNuncaTrouxe()
    {
        // O RE9 foi desinstalado e reinstalado do zero, e continuou sem nvngx_dlss.dll na
        // pasta — abrindo normalmente. Prova de que o depot não traz esse arquivo: em jogo
        // de Streamline o runtime de DLSS vem do driver. O veredito antigo dizia "SUMIU,
        // uma desinstalação antiga apagou" e mandava verificar integridade para repor um
        // arquivo que a Steam não tem para devolver.
        var (dir, kit) = Pastas();
        try
        {
            var kitDll = Path.Combine(kit, "nvngx_dlss.dll");
            File.WriteAllText(kitDll, "bytes do kit");
            File.WriteAllText(Path.Combine(dir, "sl.dlss.dll"), "streamline");

            var perfil = PerfilComDlssNativo(dir);
            var c3 = CheckpointVerifier.Verify(perfil, null, kitDll).First(c => c.Number == 3);

            Assert.NotEqual(CheckStatus.Fail, c3.State);
            Assert.DoesNotContain("SUMIU", c3.Title);
            Assert.DoesNotContain("Verificar integridade", c3.FixHint ?? "");
            // E explica por que não falta nada, para o usuário não sair procurando.
            Assert.Contains("driver", c3.Detail);
            // A orientação que importa no caminho direto continua de pé.
            Assert.Contains("LIGADO", c3.Title);
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void Checkpoint3AcusaSumicoQuandoOBackupProvaQueFomosNos()
    {
        // O outro lado: se existe .dlss5bak do arquivo, ele ESTAVA na pasta e este
        // programa o tirou de lá. Aí sim é falha nossa — e a saída é reverter, não
        // verificar integridade.
        var (dir, kit) = Pastas();
        try
        {
            var kitDll = Path.Combine(kit, "nvngx_dlss.dll");
            File.WriteAllText(kitDll, "bytes do kit");
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll" + Propriedade.BackupSuffix), "o do jogo");

            var c3 = CheckpointVerifier.Verify(PerfilComDlssNativo(dir), null, kitDll)
                .First(c => c.Number == 3);

            Assert.Equal(CheckStatus.Fail, c3.State);
            Assert.Contains("Desinstalar", c3.FixHint!);
            Assert.DoesNotContain("Verificar integridade", c3.FixHint!);
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void SemGabaritoNaoHaProva()
    {
        var (dir, kit) = Pastas();
        try
        {
            var noJogo = Path.Combine(dir, "nvngx_dlss.dll");
            File.WriteAllText(noJogo, "bytes do kit");

            // Sem o caminho do kit, ou com o kit inexistente, nada é transplante.
            Assert.False(TransplanteDlss.EhDoKit(noJogo, null));
            Assert.False(TransplanteDlss.EhDoKit(noJogo, Path.Combine(kit, "nvngx_dlss.dll")));

            // E o próprio arquivo do kit nunca é transplante de si mesmo.
            Assert.False(TransplanteDlss.EhDoKit(noJogo, noJogo));
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kit, true); }
    }
}

public class DxWrapperChainTests
{
    private static string NovaPasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5chain_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>PE mínimo com a arquitetura pedida, para o checkpoint 5 ler.</summary>
    private static void PeFalso(string path, PeArchitecture arch)
    {
        var bytes = new byte[0x200];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        const int peOffset = 0x80;
        BitConverter.GetBytes(peOffset).CopyTo(bytes, 0x3C);
        bytes[peOffset] = (byte)'P';
        bytes[peOffset + 1] = (byte)'E';
        ushort machine = arch == PeArchitecture.X64 ? (ushort)0x8664 : (ushort)0x014C;
        BitConverter.GetBytes(machine).CopyTo(bytes, peOffset + 4);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>A pasta do Dead Space 2 depois da instalação encadeada.</summary>
    private static string PastaEncadeada(out string dgVoodoo)
    {
        var dir = NovaPasta();
        File.WriteAllText(Path.Combine(dir, "D3D9.dll"), "stub do DxWrapper");
        File.WriteAllText(Path.Combine(dir, "dxwrapper.dll"), "DxWrapper");
        dgVoodoo = Path.Combine(dir, "dgVoodoo_D3D9.dll");
        PeFalso(dgVoodoo, PeArchitecture.X86);
        File.WriteAllText(Path.Combine(dir, "D3D9.ini"), DxWrapperChain.GerarIni(null, dgVoodoo));
        return dir;
    }

    [Fact]
    public void IniNovoTemMarcaSecaoEChave()
    {
        var alvo = Path.Combine(Path.GetTempPath(), "jogo", "dgVoodoo_D3D9.dll");
        var ini = DxWrapperChain.GerarIni(null, alvo);

        Assert.True(DxWrapperChain.IniEhNosso(ini));
        Assert.Contains("[General]", ini);
        Assert.Equal(alvo, DxWrapperChain.LerRealDllPath(ini));
        Assert.True(DxWrapperChain.ApontaParaODgVoodoo(ini));
        // Comentário com '=' viraria "chave" para o parser do DxWrapper.
        foreach (var linha in ini.Split("\r\n"))
            if (linha.StartsWith(';')) Assert.DoesNotContain('=', linha);
    }

    [Fact]
    public void IniDoUsuarioSoTemALinhaTrocada()
    {
        // Um dxwrapper.ini que o usuário já tinha é dele: o resto fica intacto.
        var original = "[General]\r\nRealDllPath = \r\nHandleExceptions = 1\r\n\r\n[Compatibility]\r\nDisableGameUX = 1\r\n";

        var ini = DxWrapperChain.GerarIni(original, @"C:\jogo\dgVoodoo_D3D9.dll");

        Assert.False(DxWrapperChain.IniEhNosso(ini));
        Assert.Equal(@"C:\jogo\dgVoodoo_D3D9.dll", DxWrapperChain.LerRealDllPath(ini));
        Assert.Contains("HandleExceptions = 1", ini);
        Assert.Contains("DisableGameUX = 1", ini);
        Assert.Single(ini.Split("\r\n"), l => l.TrimStart().StartsWith("RealDllPath", StringComparison.OrdinalIgnoreCase));

        // Sem a chave, ela entra logo abaixo de [General].
        var semChave = DxWrapperChain.GerarIni("[General]\r\nHandleExceptions = 1\r\n", @"C:\x\dgVoodoo_D3D9.dll");
        var linhas = semChave.Split("\r\n");
        Assert.Equal("[General]", linhas[0]);
        Assert.StartsWith("RealDllPath", linhas[1]);

        // Desencadear devolve a linha em branco e não mexe no resto.
        var solto = DxWrapperChain.Desencadear(ini);
        Assert.Null(DxWrapperChain.LerRealDllPath(solto));
        Assert.False(DxWrapperChain.ApontaParaODgVoodoo(solto));
        Assert.Contains("DisableGameUX = 1", solto);
    }

    [Fact]
    public void EngineGravaOIniPreservandoODoUsuario()
    {
        var dir = NovaPasta();
        try
        {
            var ini = Path.Combine(dir, "D3D9.ini");
            File.WriteAllText(ini, "[General]\r\nRealDllPath = \r\nHandleExceptions = 1\r\n");
            var dgVoodoo = Path.Combine(dir, "dgVoodoo_D3D9.dll");

            var profile = new GameProfile
            {
                GameFolder = dir, RealExePath = Path.Combine(dir, "deadspace2.exe"),
                Architecture = PeArchitecture.X86, Api = GraphicsApi.D3D9, RendererFolder = dir,
            };
            var plan = new InstallPlan { Profile = profile, Options = new InstallOptions() };
            plan.Actions.Add(new PlanAction(PlanActionKind.WriteGeneratedFile, "encadear", dgVoodoo, ini));

            var engine = new InstallerEngine(_ => { });
            var manifesto = engine.Execute(plan, new KitInventory { KitRoot = dir }).Manifesto!;

            var texto = File.ReadAllText(ini);
            Assert.Equal(dgVoodoo, DxWrapperChain.LerRealDllPath(texto));
            Assert.Contains("HandleExceptions = 1", texto);
            // Era do usuário: entra como backup, não como arquivo nosso.
            Assert.True(manifesto.BackedUpFiles.ContainsKey(ini));
            Assert.DoesNotContain(ini, manifesto.AddedFiles);

            // Reverter devolve o ini dele, sem RealDllPath.
            engine.Revert(manifesto, removeRegistryOverride: false);
            Assert.Null(DxWrapperChain.LerRealDllPath(File.ReadAllText(ini)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FaxinaTiraODgVoodooEDesencadeiaOIni()
    {
        // Sem isso o DxWrapper ficaria apontando para um arquivo que sumiu — e o jogo
        // voltaria a não abrir por culpa nossa.
        var dir = PastaEncadeada(out var dgVoodoo);
        try
        {
            var sobras = new InstallerEngine(_ => { }).LimpezaTotal(dir);

            Assert.Empty(sobras);
            Assert.False(File.Exists(dgVoodoo));
            Assert.False(File.Exists(Path.Combine(dir, "D3D9.ini")));   // era nosso: sai inteiro
            Assert.True(File.Exists(Path.Combine(dir, "D3D9.dll")));          // o DxWrapper fica
            Assert.True(File.Exists(Path.Combine(dir, "dxwrapper.dll")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IniDoStubTemONomeDoStub()
    {
        // O DllMain do stub troca a extensão do PRÓPRIO caminho por ".ini": d3d9.dll lê
        // d3d9.ini. Gravar no dxwrapper.ini (erro da primeira versão) deixava a corrente
        // aberta — o jogo abria em D3D9 puro, sem dgVoodoo e sem ReShade.
        Assert.Equal("D3D9.ini", DxWrapperChain.IniPara("D3D9.dll"));
        Assert.Equal("D3D8.ini", DxWrapperChain.IniPara("D3D8.dll"));
        var dg = Path.Combine(Path.GetTempPath(), "jogo", "dgVoodoo_D3D9.dll");
        Assert.Equal(Path.Combine(Path.GetTempPath(), "jogo", "D3D9.ini"), DxWrapperChain.IniDoDgVoodoo(dg));
    }

    [Fact]
    public void FaxinaLimpaTambemODxwrapperIniLegado()
    {
        // A primeira versão gravou o RealDllPath no dxwrapper.ini; quem instalou com ela
        // tem esse arquivo (nosso, com a marca) na pasta. A faxina o reconhece e remove.
        var dir = PastaEncadeada(out var dgVoodoo);
        try
        {
            File.WriteAllText(Path.Combine(dir, DxWrapperChain.IniLegado), DxWrapperChain.GerarIni(null, dgVoodoo));

            var sobras = new InstallerEngine(_ => { }).LimpezaTotal(dir);

            Assert.Empty(sobras);
            Assert.False(File.Exists(Path.Combine(dir, DxWrapperChain.IniLegado)));
            Assert.False(File.Exists(Path.Combine(dir, "D3D9.ini")));
            Assert.True(File.Exists(Path.Combine(dir, "D3D9.dll")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FaxinaNumIniDoUsuarioSoLimpaALinha()
    {
        var dir = PastaEncadeada(out var dgVoodoo);
        try
        {
            var ini = Path.Combine(dir, "D3D9.ini");
            File.WriteAllText(ini, DxWrapperChain.GerarIni("[General]\r\nHandleExceptions = 1\r\n", dgVoodoo));

            new InstallerEngine(_ => { }).LimpezaTotal(dir);

            Assert.True(File.Exists(ini));
            var texto = File.ReadAllText(ini);
            Assert.Null(DxWrapperChain.LerRealDllPath(texto));
            Assert.Contains("HandleExceptions = 1", texto);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IsolamentoDesligaSoODgVoodooEDeixaODxWrapper()
    {
        // Desligar o D3D9.dll aqui tiraria o conserto do jogo e o teste concluiria errado.
        var dir = PastaEncadeada(out var dgVoodoo);
        try
        {
            var iso = new Isolamento(_ => { });
            iso.Aplicar(EstadoIsolamento.SemDgVoodoo, dir, dir);

            Assert.True(File.Exists(Path.Combine(dir, "D3D9.dll")));
            Assert.False(File.Exists(dgVoodoo));
            Assert.True(File.Exists(dgVoodoo + Isolamento.Sufixo));
            Assert.Null(DxWrapperChain.LerRealDllPath(File.ReadAllText(Path.Combine(dir, "D3D9.ini"))));

            iso.Aplicar(EstadoIsolamento.Tudo, dir, dir);

            Assert.True(File.Exists(dgVoodoo));
            Assert.Equal(dgVoodoo, DxWrapperChain.LerRealDllPath(File.ReadAllText(Path.Combine(dir, "D3D9.ini"))));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Checkpoint5ConfereACorrente()
    {
        var dir = PastaEncadeada(out var dgVoodoo);
        try
        {
            var perfil = new GameProfile
            {
                GameFolder = dir, RealExePath = Path.Combine(dir, "deadspace2.exe"),
                Architecture = PeArchitecture.X86, Api = GraphicsApi.D3D9, RendererFolder = dir,
            };

            var c5 = CheckpointVerifier.Verify(perfil, null).Where(c => c.Number == 5).ToList();
            var dg = c5.First(c => c.Title.StartsWith("dgVoodoo2", StringComparison.Ordinal));
            var corrente = c5.First(c => c.Title.Contains("RealDllPath", StringComparison.Ordinal));
            Assert.Equal(CheckStatus.Pass, dg.State);
            Assert.Contains("encadeado", dg.Detail);
            Assert.Equal(CheckStatus.Pass, corrente.State);

            // Corrente aberta: o dgVoodoo está lá, mas o DxWrapper não aponta para ele.
            var ini = Path.Combine(dir, "D3D9.ini");
            File.WriteAllText(ini, DxWrapperChain.Desencadear(File.ReadAllText(ini)));
            var aberta = CheckpointVerifier.Verify(perfil, null)
                .First(c => c.Number == 5 && c.Title.Contains("RealDllPath", StringComparison.Ordinal));
            Assert.Equal(CheckStatus.Fail, aberta.State);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class OverlaysTests
{
    [Fact]
    public void DetectaPeloNomeDoProcessoIgnorandoMaiusculas()
    {
        var achados = Overlays.Detectar(new[] { "EADesktop", "chrome", "RTSS", "explorer" });

        Assert.Contains(achados, o => o.Nome == "EA App");
        Assert.Contains(achados, o => o.Nome.Contains("RivaTuner"));
        Assert.Equal(2, achados.Count);

        Assert.Contains(Overlays.Detectar(new[] { "eadesktop" }), o => o.Nome == "EA App");
    }

    [Fact]
    public void MaquinaLimpaNaoAcusaNada()
    {
        Assert.Empty(Overlays.Detectar(new[] { "explorer", "svchost", "notepad" }));
    }

    [Fact]
    public void OQuePrecisaFicarAbertoNaoPedeParaFechar()
    {
        // "Feche o EA App" é conselho impossível: sem ele o Titanfall não abre. O que sai
        // é a sobreposição.
        var ea = Overlays.Conhecidos.Single(o => o.Nome == "EA App");
        Assert.True(ea.PrecisaFicarAberto);
        Assert.Contains("PRECISA continuar aberto", ea.ComoDesligar);

        var rtss = Overlays.Conhecidos.Single(o => o.Nome.Contains("RivaTuner"));
        Assert.False(rtss.PrecisaFicarAberto);
    }

    [Fact]
    public void TodaSobreposicaoDizOndeFicaAOpcao()
    {
        Assert.All(Overlays.Conhecidos, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Processo));
            Assert.False(string.IsNullOrWhiteSpace(o.ComoDesligar));
        });
    }
}

public class RenodxLogTests
{
    // Trechos fiéis ao ReShade.log do Onimusha, onde o caminho direto do RenoDX
    // (D3D12 + DLSS nativo) funcionou de ponta a ponta.
    private const string LogOnimusha = """
        INFO | [DLSS 5 Neural Rendering] DLSS5 Generic: RenoDX DLSS5 Generic v4.1.5 loaded
        INFO | [DLSS 5 Neural Rendering] DLSS5 Generic: D3D12 NGX hooks installed across 3 module copy(ies)
        INFO | [DLSS 5 Neural Rendering] DLSS5 Generic: signed DLSSNR 310.8.0 D3D12 runtime initialized
        INFO | [DLSS 5 Neural Rendering] DLSS5 Generic: NGX feature create intercepted: feature=1 (DLSS/DLAA), slot=0
        INFO | [DLSS 5 Neural Rendering] DLSS5 Generic: inline feature 18 evaluation succeeded (count=1, NR input 2560x1440)
        INFO | [DLSS 5 Neural Rendering] DLSS5 Generic: inline feature 18 evaluation succeeded (count=60, NR input 2560x1440)
        """;

    [Fact]
    public void ReconheceQueFuncionouEQuantosFramesRodaram()
    {
        var s = RenodxLog.Ler(LogOnimusha);

        Assert.NotNull(s);
        Assert.True(s!.Ativo);
        Assert.Equal(60, s.Avaliacoes);   // fica com a maior contagem, não a primeira
        Assert.False(s.HooksSemUso);
        Assert.False(s.AssinaturaRecusada);
        Assert.Contains("ATIVO", s.Resumo);
    }

    [Fact]
    public void HooksArmadosSemChamadaNaoContaComoFuncionando()
    {
        // Caso do God of War: D3D11 com DLSS nativo, o RenoDX só enxerga NGX em D3D12.
        var s = RenodxLog.Ler(
            "[DLSS 5 Neural Rendering] DLSS5 Generic: HOOKS ARMED — NO DLSS CREATE SEEN (creates 0)");

        Assert.NotNull(s);
        Assert.False(s!.Ativo);
        Assert.True(s.HooksSemUso);
    }

    [Fact]
    public void AssinaturaRecusadaEhReportadaSeparadamente()
    {
        var s = RenodxLog.Ler("[DLSS 5 Neural Rendering] DLSS5 Generic: NGX result 0xBAD00007");

        Assert.NotNull(s);
        Assert.True(s!.AssinaturaRecusada);
        Assert.Contains("reiniciar", s.Resumo);
    }

    [Fact]
    public void LogSemOAddonNaoRendeStatus()
    {
        Assert.Null(RenodxLog.Ler("INFO | Initializing crosire's ReShade version '6.8.0'"));
        Assert.Null(RenodxLog.Ler(""));
        Assert.Null(RenodxLog.Ler(null));
    }
}

public class CaminhoDiretoNaoAcusaFalsoAlarmeTests
{
    private static GameProfile PerfilD3D12ComDlssNativo(string pasta) => new()
    {
        GameFolder = pasta,
        RealExePath = Path.Combine(pasta, "jogo.exe"),
        Architecture = PeArchitecture.X64,
        Api = GraphicsApi.D3D12,
        HasNativeDlss = true,
    };

    [Fact]
    public void PresetVazioNoCaminhoDiretoNaoEhFalha()
    {
        // O preset sai sem efeitos de propósito: quem trabalha é o addon do RenoDX.
        // Cobrar a linha Techniques ali pintava de vermelho uma instalação correta.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5direto_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var perfil = PerfilD3D12ComDlssNativo(dir);
            Assert.False(perfil.NeedsFeeder);   // pré-condição do caso

            File.WriteAllText(Path.Combine(dir, "ReShadePreset.ini"), "[jogo.exe]\nTechniques=\n");

            var checagens = CheckpointVerifier.Verify(perfil, null);
            var c13 = checagens.First(c => c.Number == 13);
            var c15 = checagens.First(c => c.Number == 15);

            Assert.Equal(CheckStatus.NotApplicable, c13.State);
            Assert.Equal(CheckStatus.NotApplicable, c15.State);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ComFeederOPresetContinuaSendoCobrado()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5feeder_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var perfil = PerfilD3D12ComDlssNativo(dir);
            perfil.HasNativeDlss = false;       // agora o Feeder entra
            Assert.True(perfil.NeedsFeeder);

            File.WriteAllText(Path.Combine(dir, "ReShadePreset.ini"), "[jogo.exe]\nTechniques=\n");

            var c13 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 13);
            Assert.Equal(CheckStatus.Fail, c13.State);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class ReinicioPendenteTests
{
    [Fact]
    public void AtivoNoLogNaoValeEnquantoOReinicioEstaPendente()
    {
        // O padrão que enganou a sessão inteira: 30 jogos funcionando, uma desinstalação
        // removeu o override, o reboot seguinte o desativou no driver, e as instalações
        // seguintes regravaram a chave que o driver nunca mais leu. Resultado: log
        // dizendo "ativo" e imagem inalterada, em todos os jogos ao mesmo tempo. Um OK
        // do checkpoint 14 nesse estado é mentira.
        var dir = Path.Combine(Path.GetTempPath(), "dlss5reboot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "ReShade.log"), new string('x', 2000) +
                "\n[DLSS 5 Neural Rendering] DLSS5 Generic: inline feature 18 evaluation succeeded (count=99)\n");

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "jogo.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
            };

            // Override gravado agora, e o último boot é anterior a "agora": pendente.
            var manifesto = new InstallManifest
            {
                GameFolder = dir,
                ExeFolder = dir,
                RegistryOverrideApplied = true,
                RegistryOverrideAppliedUtc = DateTime.UtcNow,
            };

            var c14 = CheckpointVerifier.Verify(perfil, manifesto).First(c => c.Number == 14);

            Assert.Equal(CheckStatus.Warning, c14.State);
            Assert.Contains("não foi reiniciado", c14.Detail);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DepoisDoReinicioOAtivoVolta_ASerOk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5reboot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "ReShade.log"), new string('x', 2000) +
                "\n[DLSS 5 Neural Rendering] DLSS5 Generic: inline feature 18 evaluation succeeded (count=99)\n");

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "jogo.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
            };

            // Override gravado bem antes do último boot: reinício já aconteceu.
            var manifesto = new InstallManifest
            {
                GameFolder = dir,
                ExeFolder = dir,
                RegistryOverrideApplied = true,
                RegistryOverrideAppliedUtc = DateTime.UtcNow - TimeSpan.FromDays(365),
            };

            var c14 = CheckpointVerifier.Verify(perfil, manifesto).First(c => c.Number == 14);
            Assert.Equal(CheckStatus.Pass, c14.State);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class RenodxLogRe9Tests
{
    // Trechos fiéis ao terceiro ReShade.log do RE9 (build sem Generic Depth): o runtime
    // sobe para a resolução final, 2,2 s de silêncio, e o jogo registra a janela da
    // própria tela de erro — sem nenhum "feature create" antes.
    private const string LogRe9 = """
        00:33:51:039 [ 4524] | INFO  | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) loaded from 'G:\RE9\dxgi.dll' into 'G:\RE9\re9.exe' (0x60F442AB) ...
        00:33:52:679 [ 4524] | INFO  | Installing delayed hooks for 'C:\WINDOWS\system32\d3d12.dll' (Just loaded via LoadLibrary('G:\RE9\/sl.common.dll')) ...
        00:33:53:713 [ 4524] | INFO  | Registered add-on "DLSS 5 Neural Rendering" v0.2026.828.517 using ReShade API version 18.
        00:33:53:713 [ 4524] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: RenoDX DLSS5 Generic v4.1.5 (build Aug 30 2026 22:25:38) loaded (hotkeys: NR toggle F6, screenshot F5) | EnableHooks=2: NGX hooks only, Streamline modules left unpatched
        00:33:53:718 [ 4524] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: D3D12 NGX hooks installed across 3 module copy(ies); inline DLSS contract capture armed
        00:33:56:151 [35364] | INFO  | Redirecting IDXGIFactory2::CreateSwapChainForHwnd(this = 0000000058955B30, pDevice = 00000000773FAD20, hWnd = 00000000000B0DBE, ppSwapChain = ) ...
        00:33:56:228 [35364] | INFO  | Recreated runtime environment on runtime 00000001207EDFE0 ('G:\RE9\ReShade.ini').
        00:33:59:035 [35364] | INFO  | Recreated runtime environment on runtime 00000001207EDFE0 ('G:\RE9\ReShade.ini').
        00:34:01:257 [35364] | INFO  | Redirecting RegisterClassExW(lpWndClassEx = 000000018F6B8AE0 { "DirectUIHWND", style = 0x4000 }) ...
        00:34:01:259 [35364] | INFO  | Redirecting RegisterClassExW(lpWndClassEx = 000000018F6B87D0 { "CtrlNotifySink", style = 0x4000 }) ...
        00:34:29:753 [35364] | INFO  | Unregistered add-on "DLSS 5 Neural Rendering".
        00:34:30:141 [35364] | INFO  | Exiting ...
        """;

    // MGS V Ground Zeroes, ReShade.log real: o ReShade entra como d3d11.dll, o jogo cria o
    // device, os DOIS addons registram, o renderizador cria 27 contextos adiados — e o
    // processo sai limpo. Nenhuma swapchain, nenhum diálogo, nenhum log truncado. Sem os
    // arquivos o mesmo jogo abre. Isso não é travamento: é o jogo se fechando.
    private const string LogMgsvGz = """
        11:10:30:025 [23288] | INFO  | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) loaded from 'E:\MGS GZ\d3d11.dll' into 'E:\MGS GZ\MgsGroundZeroes.exe' (0x87BB9CE3) ...
        11:10:30:184 [23288] | INFO  | Initialized.
        11:10:31:175 [23288] | INFO  | Redirecting D3D11CreateDevice(pAdapter = 000002527E9982A0, DriverType = 0, Software = 0000000000000000, Flags = 0) ...
        11:10:31:177 [23288] | INFO  | Redirecting D3D11CreateDeviceAndSwapChain(pAdapter = 000002527E9982A0, pSwapChainDesc = 0000000000000000, ppSwapChain = 0000000000000000) ...
        11:10:31:182 [23288] | INFO  | Registered add-on "DLSS 5 Feed 0.5.0" v0.5.0.0 using ReShade API version 20.
        11:10:31:189 [23288] | INFO  | Registered add-on "DLSS 5 Neural Rendering" v0.2026.828.517 using ReShade API version 18.
        11:10:31:190 [23288] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: RenoDX DLSS5 Generic v4.1.5 (build Aug 30 2026 22:25:38) loaded (hotkeys: NR toggle F6, screenshot F5) | EnableHooks=2: NGX hooks only, Streamline modules left unpatched
        11:10:31:641 [23288] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: signed NR runtime (nvngx_dlssnr.dll) pre-loaded at device init
        11:10:31:664 [23288] | INFO  | Redirecting ID3D11Device::CreateDeferredContext(this = 000002521AD772A0, ContextFlags = 0) ...
        11:10:31:815 [23288] | INFO  | Unregistered add-on "DLSS 5 Neural Rendering".
        11:10:31:815 [23288] | INFO  | Unregistered add-on "DLSS 5 Feed 0.5.0".
        11:10:31:823 [23288] | INFO  | Exiting ...
        11:10:32:891 [23288] | INFO  | Finished exiting.
        """;

    [Fact]
    public void MgsvGz_FechouSozinhoSemChegarNaSwapchain()
    {
        var s = RenodxLog.Ler(LogMgsvGz);

        Assert.NotNull(s);
        Assert.False(s!.Ativo);
        Assert.True(s.CriouDevice);
        // "D3D11CreateDeviceAndSwapChain" tem a palavra, mas com ppSwapChain nulo: não
        // pode contar como swapchain criada, senão o veredito inteiro inverte.
        Assert.False(s.CriouSwapchain);
        Assert.True(s.Encerrou);
        Assert.Null(s.SegundosAteDialogo);     // não houve tela de erro: o jogo só saiu
        Assert.False(s.CaiuAntesDoDlss);       // aquele veredito exige diálogo
        Assert.True(s.FechouSemJanela);
        Assert.Contains("SAIU sozinho", s.Resumo);
    }

    [Fact]
    public void JogoQueChegouNaSwapchainNaoContaComoFechouSozinho()
    {
        // O contraveredito: com swapchain criada, sair do jogo é sair do jogo.
        var s = RenodxLog.Ler(
            "INFO | Registered add-on \"DLSS 5 Neural Rendering\" v0.2026.828.517\n" +
            "INFO | Redirecting D3D11CreateDevice(...) ...\n" +
            "INFO | Redirecting IDXGIFactory2::CreateSwapChainForHwnd(...) ...\n" +
            "INFO | Exiting ...\n");

        Assert.NotNull(s);
        Assert.True(s!.CriouSwapchain);
        Assert.False(s.FechouSemJanela);
    }

    [Fact]
    public void FoxEngineEhReconhecidoPeloExe()
    {
        Assert.True(MotorFox.EhFoxEngine(Path.Combine("E:", "jogo", "MgsGroundZeroes.exe")));
        Assert.True(MotorFox.EhFoxEngine(Path.Combine("E:", "jogo", "mgsvtpp.exe")));
        Assert.True(MotorFox.EhFoxEngine(Path.Combine("E:", "jogo", "mgsvgz.exe")));
        Assert.False(MotorFox.EhFoxEngine(Path.Combine("E:", "jogo", "re9.exe")));
        Assert.False(MotorFox.EhFoxEngine(null));
    }

    [Fact]
    public void Re9_CaiuAntesDeCriarODlss()
    {
        var s = RenodxLog.Ler(LogRe9);

        Assert.NotNull(s);
        Assert.False(s!.Ativo);
        Assert.False(s.HooksSemUso);           // o addon nunca chegou a escrever HOOKS ARMED
        Assert.True(s.HooksInstalados);
        Assert.False(s.CriouFeature);
        Assert.Equal(2, s.EnableHooks);
        Assert.True(s.Streamline);
        Assert.True(s.Encerrou);
        Assert.Equal(2.2, s.SegundosAteDialogo);
        Assert.True(s.CaiuAntesDoDlss);
        Assert.Contains("ANTES", s.Resumo);
        Assert.Contains("2.2 s", s.Resumo);
    }

    [Fact]
    public void OnimushaCriouAFeatureENaoContaComoQueda()
    {
        var s = RenodxLog.Ler(
            "INFO | [DLSS 5 Neural Rendering] DLSS5 Generic: D3D12 NGX hooks installed across 3 module copy(ies)\n" +
            "INFO | [DLSS 5 Neural Rendering] DLSS5 Generic: NGX feature create intercepted: feature=1 (DLSS/DLAA), slot=0\n");

        Assert.NotNull(s);
        Assert.True(s!.CriouFeature);
        Assert.Null(s.SegundosAteDialogo);
        Assert.False(s.CaiuAntesDoDlss);
        Assert.Contains("criou o DLSS", s.Resumo);
    }

    [Fact]
    public void DialogoDepoisDaFeatureNaoEhQuedaAntesDoDlss()
    {
        // Caiu, mas depois de o DLSS existir: aí o gancho volta à lista de suspeitos.
        var s = RenodxLog.Ler(
            "10:00:00:000 [1] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: D3D12 NGX hooks installed across 1 module copy(ies)\n" +
            "10:00:01:000 [1] | INFO  | Recreated runtime environment on runtime 1 ('ReShade.ini').\n" +
            "10:00:05:000 [1] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: NGX feature create intercepted: feature=1 (DLSS/DLAA), slot=0\n" +
            "10:00:09:500 [1] | INFO  | Redirecting RegisterClassExW(lpWndClassEx = 1 { \"DirectUIHWND\", style = 0x4000 }) ...\n");

        Assert.NotNull(s);
        Assert.Equal(8.5, s!.SegundosAteDialogo);
        Assert.False(s.CaiuAntesDoDlss);
    }

    [Fact]
    public void ViradaDeDiaEntreORuntimeEODialogo()
    {
        var s = RenodxLog.Ler(
            "23:59:59:500 [1] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: D3D12 NGX hooks installed across 1 module copy(ies)\n" +
            "23:59:59:500 [1] | INFO  | Recreated runtime environment on runtime 1 ('ReShade.ini').\n" +
            "00:00:01:000 [1] | INFO  | Redirecting RegisterClassExW(lpWndClassEx = 1 { \"DirectUIHWND\", style = 0 }) ...\n");

        Assert.Equal(1.5, s!.SegundosAteDialogo);
    }

    [Fact]
    public void SemRuntimeContaDoSwapchainOuDoRegistroDoAddon()
    {
        var s = RenodxLog.Ler(
            "10:00:00:000 [1] | INFO  | Registered add-on \"DLSS 5 Neural Rendering\" v1 using ReShade API version 18.\n" +
            "10:00:03:000 [1] | INFO  | Redirecting RegisterClassExW(lpWndClassEx = 1 { \"DirectUIHWND\", style = 0 }) ...\n");

        Assert.Equal(3.0, s!.SegundosAteDialogo);
        Assert.True(s.CaiuAntesDoDlss);
    }

    [Fact]
    public void PedidoDeStreamlineHooksDoProprioAddon()
    {
        var s = RenodxLog.Ler(
            "[DLSS 5 Neural Rendering] DLSS5 Generic: NR skipped: the game's NGX contract has no guide (input) dimensions. " +
            "If the game uses NVIDIA Streamline, set EnableHooks=1 in the [RenoDX.DLSS5] section of ReShade.ini and restart");

        Assert.NotNull(s);
        Assert.True(s!.PedeStreamlineHooks);
        Assert.False(s.CaiuAntesDoDlss);
        Assert.Contains("EnableHooks=1", s.Resumo);
    }

    [Fact]
    public void ModoSeguroLeEnableHooksZero()
    {
        var s = RenodxLog.Ler("[DLSS 5 Neural Rendering] DLSS5 Generic: loaded | SAFE MODE: EnableHooks=0, all hooks off (no NR)");
        Assert.Equal(0, s!.EnableHooks);
        Assert.False(s.HooksInstalados);
    }
}

public class RenodxIniTests
{
    [Fact]
    public void SemSecaoOuSemChaveNaoHaValor()
    {
        Assert.Null(RenodxIni.Ler(null));
        Assert.Null(RenodxIni.Ler(""));
        Assert.Null(RenodxIni.Ler("[GENERAL]\r\nEnableHooks=1\r\n"));   // chave fora da seção não vale
        Assert.Null(RenodxIni.Ler("[RenoDX.DLSS5]\r\nIntensity=1\r\n"));
    }

    [Fact]
    public void LeSemLigarParaCaixaNemEspacos()
    {
        Assert.Equal(1, RenodxIni.Ler("[renodx.dlss5]\n enablehooks = 1 \n"));
    }

    [Fact]
    public void GravarCriaASecaoNoFimQuandoNaoExiste()
    {
        var ini = "[GENERAL]\r\nPresetPath=.\\ReShadePreset.ini\r\n";
        var novo = RenodxIni.Gravar(ini, 1);

        Assert.StartsWith("[GENERAL]\r\nPresetPath=.\\ReShadePreset.ini\r\n", novo);
        Assert.Contains("\r\n[RenoDX.DLSS5]\r\nEnableHooks=1\r\n", novo);
        Assert.Equal(1, RenodxIni.Ler(novo));
        Assert.EndsWith("\r\n", novo);
    }

    [Fact]
    public void GravarTrocaSoALinhaDaChaveEPreservaOResto()
    {
        var ini = "[GENERAL]\r\nPresetPath=.\\ReShadePreset.ini\r\n\r\n" +
                  "[RenoDX.DLSS5]\r\nIntensity=1.0\r\nEnableHooks=2\r\nStyle=0\r\n\r\n" +
                  "[ADDON]\r\nAddonPath=.\\\r\n";
        var novo = RenodxIni.Gravar(ini, 0);

        Assert.Equal(0, RenodxIni.Ler(novo));
        Assert.Equal(ini.Replace("EnableHooks=2", "EnableHooks=0"), novo);
        Assert.Equal(1, novo.Split("EnableHooks=").Length - 1);
    }

    [Fact]
    public void GravarAcrescentaAChaveDentroDaSecaoQueJaExiste()
    {
        var ini = "[RenoDX.DLSS5]\r\nIntensity=1.0\r\n\r\n[ADDON]\r\nAddonPath=.\\\r\n";
        var novo = RenodxIni.Gravar(ini, 1);

        Assert.Equal("[RenoDX.DLSS5]\r\nIntensity=1.0\r\nEnableHooks=1\r\n\r\n[ADDON]\r\nAddonPath=.\\\r\n", novo);
    }

    [Fact]
    public void GravarNoIniGeradoTrocaOValorSemMexerNoResto()
    {
        var gerado = ReShadeConfigWriter.BuildReShadeIni(feederUsed: false);
        var novo = RenodxIni.Gravar(gerado, 1);

        Assert.Equal(1, RenodxIni.Ler(novo));
        Assert.Equal(gerado.Replace("EnableHooks=2", "EnableHooks=1"), novo);
        Assert.Contains("DisabledAddons=Generic Depth", novo);
    }

    [Fact]
    public void ListaComecaPeloPadraoEDescreveOsTres()
    {
        Assert.Equal(RenodxIni.Padrao, RenodxIni.Valores[0]);
        foreach (var v in RenodxIni.Valores)
        {
            Assert.StartsWith(v.ToString(), RenodxIni.Descricao(v));
            Assert.Contains($"EnableHooks={v}", RenodxIni.Leitura(v));
        }
    }
}

public class IsolamentoRotaATests
{
    [Fact]
    public void SemDgVoodooOTesteDoReShadeRespondeSozinho()
    {
        Assert.Contains("ReShade é quem derruba", Isolamento.Veredito(null, true, temDgVoodoo: false));
        // A conclusão nomeia a proteção anti-adulteração e o caminho que funciona,
        // em vez de mandar só "trocar a versão do ReShade".
        var v = Isolamento.Veredito(null, true, temDgVoodoo: false);
        Assert.Contains("anti-adulteração", v);
        Assert.Contains("REFramework", v);
        Assert.Contains("NÃO é a instalação", Isolamento.Veredito(null, false, temDgVoodoo: false));
        Assert.Contains("Faltou responder", Isolamento.Veredito(null, null, temDgVoodoo: false));
    }

    [Fact]
    public void ComDgVoodooOVereditoContinuaPrecisandoDosDoisTestes()
    {
        Assert.Contains("Faltou responder", Isolamento.Veredito(null, true));
        Assert.Contains("sozinhos os dois funcionam", Isolamento.Veredito(true, true));
    }

    [Fact]
    public void LeituraDoTesteSemReShadeNaoFalaDeDgVoodooNaRotaA()
    {
        var rotaA = Isolamento.Leitura(EstadoIsolamento.SemReShade, temDgVoodoo: false);
        Assert.DoesNotContain("dgVoodoo", rotaA);
        Assert.Contains("dxgi.dll", rotaA);

        var rotaC = Isolamento.Leitura(EstadoIsolamento.SemReShade);
        Assert.Contains("dgVoodoo", rotaC);
    }
}

public class Re9AtivoMasSemDiferencaTests
{
    private static string Pasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-re9-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // RE9 funcionando: 7633 quadros com NR, geração de quadros ligada, e o usuário sem
    // ver diferença. O log confirma as duas coisas ao mesmo tempo.
    private const string LogRe9Ativo =
        "11:59:19:402 [45832] | INFO  | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) loaded from 'dxgi.dll' into 're9.exe' ...\n" +
        "11:59:21:883 [45832] | INFO  | Installing delayed hooks for 'd3d12.dll' (Just loaded via LoadLibrary('...\\_storage_\\/sl.common.dll')) ...\n" +
        "11:59:25:774 [45832] | INFO  | Registered add-on \"DLSS 5 Neural Rendering\" v0.2026.828.517 using ReShade API version 18.\n" +
        "11:59:25:774 [45832] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: RenoDX DLSS5 Generic v4.1.5 loaded (hotkeys: NR toggle F6, screenshot F5) | EnableHooks=2: NGX hooks only, Streamline modules left unpatched\n" +
        "11:59:25:779 [45832] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: D3D12 NGX hooks installed across 3 module copy(ies)\n" +
        "11:59:28:706 [55796] | INFO  | Redirecting IDXGIFactory2::CreateSwapChainForHwnd(...) ...\n" +
        "11:59:33:424 [53588] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: NGX feature create intercepted: feature=13 (DLSSD/RR), slot=0\n" +
        "11:59:35:003 [37564] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: inline feature 18 evaluation succeeded (count=60, NR input 2560x1440, output 2560x1440 [native])\n" +
        "12:00:44:716 [ 3560] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: NGX feature create intercepted: feature=11 (DLSSG/FrameGeneration), slot=0\n" +
        "12:00:45:110 [29968] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: skipping NR on NGX evaluate: feature 11 (DLSSG/FrameGeneration) is not DLSS/DLSSD\n";

    [Fact]
    public void NrAtivoNumJogoDeStreamlineContinuaOkEApontaParaORuntime()
    {
        // O caso que fez o usuário perder rodadas: o addon anuncia "ACTIVE — NR INJECTED",
        // conta 7633 quadros, e a imagem não muda um fio. O painel do addon explicava o
        // porquê e ninguém lia: "Streamline: DLSS/DLSSD evaluations 0". Com EnableHooks=2
        // os módulos do Streamline ficam intocados, o addon escreve no buffer do NGX e o
        // Streamline monta o quadro com o dele. Dizer OK aqui é mentir com log na mão.
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");
            File.WriteAllText(Path.Combine(dir, "ReShade.log"), LogRe9Ativo + new string(' ', 2000));

            var s = RenodxLog.Ler(LogRe9Ativo);
            Assert.True(s!.Ativo);
            Assert.True(s.Streamline);
            Assert.Equal(2, s.EnableHooks);

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "re9.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                HasNativeDlss = true,
            };

            // Com o Streamline em jogo o veredito continua OK: o RHI não mexe no EnableHooks
            // e funciona; o RE9 com hooks=1 provou que o modo não muda o resultado. O que
            // muda é o runtime (item 18) — e o OK aponta para lá.
            var c14 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 14);
            Assert.Equal(CheckStatus.Pass, c14.State);
            Assert.Contains("item 18", c14.FixHint!);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void JogoDeStreamlineInstalaComOPadraoDoAddon()
    {
        var comSl = new GameProfile
        {
            GameFolder = "x",
            NativeDlss = new NativeDlssDetection
            {
                Present = true,
                Clues = new[] { new NativeDlssClue("sl.dlss.dll", 60) },
            },
        };
        Assert.True(comSl.UsaStreamline);
        // O padrão é o do addon, como no RHI: o modo 1 fica para a barra da verificação.
        Assert.Equal(RenodxIni.Padrao, comSl.HooksDoRenodx);

        var semSl = new GameProfile
        {
            GameFolder = "x",
            NativeDlss = new NativeDlssDetection
            {
                Present = true,
                Clues = new[] { new NativeDlssClue("nvngx_dlss.dll (veio com o jogo)", 50) },
            },
        };
        Assert.False(semSl.UsaStreamline);
        Assert.Equal(RenodxIni.Padrao, semSl.HooksDoRenodx);

        // E o valor chega no ini que a instalação grava.
        var ini = ReShadeConfigWriter.BuildReShadeIni(renodxHooks: comSl.HooksDoRenodx);
        Assert.Equal(RenodxIni.Padrao, RenodxIni.Ler(ini));
    }

    [Fact]
    public void ComNrAtivoEGeracaoDeQuadrosADicaEnsinaAComparar()
    {
        var dir = Pasta();
        try
        {
            // Jogo que NÃO usa Streamline: aí "ativo" é ativo mesmo, e o que falta é o
            // usuário saber comparar.
            var semStreamline = LogRe9Ativo.Replace(
                "11:59:21:883 [45832] | INFO  | Installing delayed hooks for 'd3d12.dll' (Just loaded via LoadLibrary('...\\_storage_\\/sl.common.dll')) ...\n", "");
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");
            File.WriteAllText(Path.Combine(dir, "ReShade.log"), semStreamline + new string(' ', 2000));

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "jogo.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                HasNativeDlss = true,
            };

            var s = RenodxLog.Ler(semStreamline);
            Assert.True(s!.Ativo);
            Assert.True(s.FrameGeneration);

            var c14 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 14);
            Assert.Equal(CheckStatus.Pass, c14.State);
            Assert.Contains("F6", c14.FixHint!);
            Assert.Contains("GERAÇÃO DE QUADROS ESTÁ LIGADA", c14.FixHint!);
            // Os três estilos pelo nome: o RE9 estava no Natural, o mais discreto deles.
            Assert.Contains("Cinematic", c14.FixHint!);
            Assert.Contains("Natural", c14.FixHint!);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SemGeracaoDeQuadrosADicaNaoInventaQueElaEstaLigada()
    {
        var semFg = LogRe9Ativo.Replace(
            "12:00:44:716 [ 3560] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: NGX feature create intercepted: feature=11 (DLSSG/FrameGeneration), slot=0\n" +
            "12:00:45:110 [29968] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: skipping NR on NGX evaluate: feature 11 (DLSSG/FrameGeneration) is not DLSS/DLSSD\n", "");

        var s = RenodxLog.Ler(semFg);
        Assert.True(s!.Ativo);
        Assert.False(s.FrameGeneration);
    }

    [Fact]
    public void ComReFrameworkODxgiDoReShadeNaoEhAcusadoDeSobra()
    {
        // O veredito antigo dizia, na mesma tela, que o dxgi.dll está certo (item 6
        // "REFramework ao lado do ReShade") e que ele é sobra a ser apagada. Quem
        // seguisse a dica apagava a instalação que estava funcionando.
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "dinput8.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "x");

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "re9.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                HasNativeDlss = true,
                UsarReFramework = true,
            };

            var seis = CheckpointVerifier.Verify(perfil, null).Where(c => c.Number == 6).ToList();

            Assert.DoesNotContain(seis, c => c.Title.Contains("Sobrou", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(seis, c => c.Title.Contains("REFramework ao lado", StringComparison.Ordinal)
                                       && c.State == CheckStatus.Pass);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class RuntimeNrTests
{
    private static string Pasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-nr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void OsQuatroBuildsSaoReconhecidosPeloHash()
    {
        // Hashes dos zips publicados em RankFTW/rhi-repo, mais o remendo que estava no kit.
        Assert.Equal("310.8.SF-v2", RuntimeNr.PorHash("6eb209e764f39872625debd6abaf45e2bb6322f6f270f781f70c059ae30b3927")!.Nome);
        Assert.Equal("310.8.SF", RuntimeNr.PorHash("4C5BD1171C7336B4B04FB394DE51DA285AB6EAD6F922D7AFDEC163F71C319D74")!.Nome);
        Assert.True(RuntimeNr.PorHash("e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e")!.SoRtx50);
        var remendo = RuntimeNr.PorHash("368911e6865534edb9b82d803c1e5d3fa3292d9c832ee0a9ee3444ac58c96b82")!;
        Assert.True(remendo.Remendo);
        Assert.Null(RuntimeNr.PorHash("0000"));
        Assert.Null(RuntimeNr.PorHash(null));
    }

    [Fact]
    public void ORemendoDoKitAntigoEhFalhaEmQualquerPlaca()
    {
        var remendo = RuntimeNr.PorHash("368911e6865534edb9b82d803c1e5d3fa3292d9c832ee0a9ee3444ac58c96b82");
        Assert.True(RuntimeNr.Avaliar(remendo, 40).Falha);
        Assert.True(RuntimeNr.Avaliar(remendo, 50).Falha);
        Assert.True(RuntimeNr.Avaliar(remendo, null).Falha);
        Assert.Contains("untested build", RuntimeNr.Avaliar(remendo, 40).Texto);
    }

    [Fact]
    public void OOriginalSoPassaEmRtx50EOShortFusePassaEmTodas()
    {
        var original = RuntimeNr.PorHash("e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e");
        Assert.False(RuntimeNr.Avaliar(original, 50).Falha);
        Assert.True(RuntimeNr.Avaliar(original, 40).Falha);
        Assert.True(RuntimeNr.Avaliar(original, null).Falha);

        var sf = RuntimeNr.PorHash("6eb209e764f39872625debd6abaf45e2bb6322f6f270f781f70c059ae30b3927");
        Assert.False(RuntimeNr.Avaliar(sf, 40).Falha);
        Assert.False(RuntimeNr.Avaliar(sf, 20).Falha);
        Assert.False(RuntimeNr.Avaliar(sf, null).Falha);

        Assert.True(RuntimeNr.Avaliar(null, 40).Falha);   // desconhecido não merece confiança
    }

    [Fact]
    public void APlacaSaiDoReShadeLog()
    {
        Assert.Equal(40, RuntimeNr.SerieRtxNoLog("11:59:28:727 [55796] | INFO  | Running on NVIDIA GeForce RTX 4070 Ti Driver 616.56."));
        Assert.Equal(50, RuntimeNr.SerieRtxNoLog("INFO | Running on NVIDIA GeForce RTX 5090 Driver 616.56."));
        Assert.Equal(30, RuntimeNr.SerieRtxNoLog("INFO | Running on NVIDIA GeForce RTX 3060 Driver 1."));
        Assert.Null(RuntimeNr.SerieRtxNoLog("INFO | Running on AMD Radeon RX 7900 XTX."));
        Assert.Null(RuntimeNr.SerieRtxNoLog(null));
        Assert.True(RuntimeNr.AddonMarcouComoDesconhecido(
            "WARN | [DLSS 5 Neural Rendering] DLSS5 Generic: signed runtime sha256 3689 (custom runtime accepted; untested build, NR failures may be specific to it)"));
        Assert.False(RuntimeNr.AddonMarcouComoDesconhecido("INFO | signed DLSSNR 310.8.SF.0 D3D12 runtime initialized"));
    }

    [Fact]
    public void Checkpoint18AcusaRuntimeDesconhecidoNaPastaDoJogo()
    {
        // Um nvngx_dlssnr.dll que não é nenhum build conhecido, com o log dizendo RTX 40.
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "nvngx_dlssnr.dll"), "não é nenhum dos builds");
            File.WriteAllText(Path.Combine(dir, "ReShade.log"),
                "INFO | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) loaded from 'dxgi.dll' into 're9.exe' ...\n" +
                "INFO | Running on NVIDIA GeForce RTX 4070 Ti Driver 616.56.\n" + new string(' ', 2000));

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "re9.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                HasNativeDlss = true,
            };

            var c18 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 18);
            Assert.Equal(CheckStatus.Fail, c18.State);
            Assert.Contains("rhi-repo", c18.FixHint!);
            Assert.Contains("310.8.SF-v2", c18.FixHint!);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Checkpoint18NaoApareceSemOArquivo()
    {
        var dir = Pasta();
        try
        {
            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "re9.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                HasNativeDlss = true,
            };
            Assert.DoesNotContain(CheckpointVerifier.Verify(perfil, null), c => c.Number == 18);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OPlanoAvisaQuandoORuntimeDoKitNaoEhConhecido()
    {
        var kitDir = Path.Combine(Path.GetTempPath(), "dlss5-kitnr-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-jogonr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(kitDir); Directory.CreateDirectory(dir);
        try
        {
            var kit = new KitInventory { KitRoot = kitDir, NvngxDlssnr = Path.Combine(kitDir, "nvngx_dlssnr.dll") };
            File.WriteAllText(kit.NvngxDlssnr!, "runtime desconhecido");

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "jogo.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                RendererFolder = dir,
                HasNativeDlss = true,
            };
            var plan = InstallPlanBuilder.Build(perfil, kit, new InstallOptions());

            Assert.Contains(plan.Warnings, w => w.Contains("nvngx_dlssnr.dll do kit", StringComparison.Ordinal)
                                                && w.Contains("rhi-repo", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); Directory.Delete(kitDir, true); }
    }
}

public class Titanfall2RenderizadorTests
{
    [Fact]
    public void DeteccaoAchaORenderizadorEmX64Retail()
    {
        // Titanfall 2 instalado com dxgi.dll na raiz: o jogo abriu, o Home não fez nada e o
        // ReShade.log nem nasceu. A DLL tem que ir para bin\x64_retail.
        var dir = PastaComExe("Titanfall2.exe");
        try
        {
            var retail = Path.Combine(dir, "bin", "x64_retail");
            Directory.CreateDirectory(retail);
            File.WriteAllText(Path.Combine(retail, "materialsystem_dx11.dll"), "x");

            var r = GameDetector.Detect(dir);

            Assert.Equal(retail, r.Profile.RendererFolder);
            Assert.Equal(retail, r.Profile.PastaDoReShade);
            Assert.Equal(dir, r.Profile.ExeFolder);
            Assert.False(r.Profile.IsSourceEngine);
            Assert.Contains(r.Notes, n => n.Contains("x64_retail", StringComparison.Ordinal));
            // O ini continua na raiz: é por ele que o ReShade escolhe a base.
            Assert.Equal(Path.Combine(dir, "ReShade.ini"), r.Profile.ReShadeIniPath);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OManifestoDaInstalacaoNaRaizNaoDesfazORenderizadorDetectado()
    {
        // Instalação anterior errada (dxgi.dll na raiz, manifesto dizendo raiz). Ao inspecionar
        // de novo, a detecção acha bin\x64_retail — e é ela que vale, senão "Atualizar" repete o erro.
        var dir = PastaComExe("Titanfall2.exe");
        try
        {
            var retail = Path.Combine(dir, "bin", "x64_retail");
            Directory.CreateDirectory(retail);
            File.WriteAllText(Path.Combine(retail, "materialsystem_dx11.dll"), "x");

            var manifesto = new InstallManifest
            {
                GameFolder = dir,
                ExeFolder = dir,
                RealExePath = Path.Combine(dir, "Titanfall2.exe"),
                RendererFolder = dir,
                Api = nameof(GraphicsApi.D3D11),
            };
            manifesto.Save(dir);

            var estado = EstadoDoMod.Inspecionar(dir, null, null);

            Assert.Equal(retail, estado.Deteccao!.Profile.RendererFolder);
            Assert.Equal(retail, estado.Deteccao.Profile.PastaDoReShade);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SemAPastaDoRenderizadorADllFicaNaRaiz()
    {
        var dir = PastaComExe("jogo.exe");
        try
        {
            var r = GameDetector.Detect(dir);
            Assert.Equal(dir, r.Profile.PastaDoReShade);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OPlanoPoeADllNoRenderizadorETiraAInerteDaRaiz()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-tf2-" + Guid.NewGuid().ToString("N"));
        var retail = Path.Combine(dir, "bin", "x64_retail");
        Directory.CreateDirectory(retail);
        try
        {
            // A sobra da instalação errada: um ReShade de verdade na raiz.
            File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "isto é ReShade 6.8");
            var profile = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "Titanfall2.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                RendererFolder = retail,
            };

            var plan = InstallPlanBuilder.Build(profile, PlanBuilderTests.KitCompleto(), new InstallOptions());

            Assert.Contains(plan.Actions, a => a.Kind == PlanActionKind.CopyFile
                && string.Equals(a.TargetPath, Path.Combine(retail, "dxgi.dll"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.Actions, a => a.Kind == PlanActionKind.DeleteForbiddenFile
                && string.Equals(a.TargetPath, Path.Combine(dir, "dxgi.dll"), StringComparison.OrdinalIgnoreCase));
            // O ini e os addons seguem na raiz.
            Assert.Contains(plan.Actions, a => a.Kind == PlanActionKind.WriteGeneratedFile
                && string.Equals(a.TargetPath, Path.Combine(dir, "ReShade.ini"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.Actions, a => a.Kind == PlanActionKind.CopyFile
                && a.TargetPath!.EndsWith(Path.Combine(dir, "renodx-dlss5.addon64"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.Warnings, w => w.Contains("renderizador", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ComADllForaDaRaizOIniGanhaBasePathENosOutrosJogosNao()
    {
        // O ReShade, como dxgi.dll, usa a pasta da própria DLL como base (get_base_path em
        // dll_main.cpp). O Titanfall 2 provou: DLL em bin\x64_retail, Home abrindo, e o painel
        // dizendo "nenhum arquivo de efeito encontrado em ...\bin\x64_retail". O desvio oficial
        // é [INSTALL] BasePath no ReShade.ini ao lado do exe — e SÓ nesse caso.
        var com = ReShadeConfigWriter.BuildReShadeIni(basePath: @"G:\Jogos\Titanfall2");
        Assert.StartsWith("[INSTALL]", com);
        Assert.Equal(@"G:\Jogos\Titanfall2", ReShadeConfigWriter.LerBasePath(com));
        Assert.Contains("[GENERAL]", com);

        var sem = ReShadeConfigWriter.BuildReShadeIni();
        Assert.DoesNotContain("[INSTALL]", sem);
        Assert.Null(ReShadeConfigWriter.LerBasePath(sem));
        Assert.Null(ReShadeConfigWriter.LerBasePath(null));
    }

    [Fact]
    public void OPlanoTiraOIniQueOReShadeCriouAoLadoDaDll()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-tf2ini-" + Guid.NewGuid().ToString("N"));
        var retail = Path.Combine(dir, "bin", "x64_retail");
        Directory.CreateDirectory(retail);
        try
        {
            File.WriteAllText(Path.Combine(retail, "ReShade.ini"), "[GENERAL]");
            File.WriteAllText(Path.Combine(retail, "ReShade.log"), "log");
            var profile = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "Titanfall2.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                RendererFolder = retail,
            };
            Assert.True(profile.ReShadeForaDaRaiz);

            var plan = InstallPlanBuilder.Build(profile, PlanBuilderTests.KitCompleto(), new InstallOptions());

            Assert.Contains(plan.Actions, a => a.Kind == PlanActionKind.DeleteForbiddenFile
                && string.Equals(a.TargetPath, Path.Combine(retail, "ReShade.ini"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.Actions, a => a.Kind == PlanActionKind.DeleteForbiddenFile
                && string.Equals(a.TargetPath, Path.Combine(retail, "ReShade.log"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.Warnings, w => w.Contains("BasePath", StringComparison.Ordinal));

            // Jogo comum: nada disso.
            var comum = new GameProfile
            {
                GameFolder = dir, RealExePath = Path.Combine(dir, "Titanfall2.exe"),
                Architecture = PeArchitecture.X64, Api = GraphicsApi.D3D11, RendererFolder = dir,
            };
            Assert.False(comum.ReShadeForaDaRaiz);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Checkpoint20CobraOBasePathEO7ExplicaOLogAoLadoDaDll()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-tf2cp-" + Guid.NewGuid().ToString("N"));
        var retail = Path.Combine(dir, "bin", "x64_retail");
        Directory.CreateDirectory(retail);
        try
        {
            var profile = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "Titanfall2.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                RendererFolder = retail,
            };

            // A rodada que o usuário viu: ini da raiz SEM BasePath, log nascido ao lado da DLL.
            File.WriteAllText(Path.Combine(dir, "ReShade.ini"), ReShadeConfigWriter.BuildReShadeIni());
            File.WriteAllText(Path.Combine(retail, "ReShade.log"), "INFO | Initializing crosire's ReShade" + new string(' ', 2000));

            var checagens = CheckpointVerifier.Verify(profile, null);
            var c20 = checagens.First(c => c.Number == 20);
            Assert.Equal(CheckStatus.Fail, c20.State);
            Assert.Contains("Nenhum arquivo de efeito", c20.Detail);
            var c7 = checagens.First(c => c.Number == 7);
            Assert.Equal(CheckStatus.Fail, c7.State);
            Assert.Contains("ao lado da DLL", c7.Detail);
            Assert.Contains("BasePath", c7.FixHint!);
            Assert.DoesNotContain("Outro...", c7.FixHint!);

            // Com o BasePath certo, o 20 passa.
            File.WriteAllText(Path.Combine(dir, "ReShade.ini"), ReShadeConfigWriter.BuildReShadeIni(basePath: dir));
            Assert.Equal(CheckStatus.Pass, CheckpointVerifier.Verify(profile, null).First(c => c.Number == 20).State);

            // Jogo comum: o item 20 nem aparece.
            var comum = new GameProfile
            {
                GameFolder = dir, RealExePath = Path.Combine(dir, "Titanfall2.exe"),
                Architecture = PeArchitecture.X64, Api = GraphicsApi.D3D11, RendererFolder = dir,
            };
            Assert.DoesNotContain(CheckpointVerifier.Verify(comum, null), c => c.Number == 20);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IsolarACausaDesligaOReShadeTambemNoRenderizador()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-tf2iso-" + Guid.NewGuid().ToString("N"));
        var retail = Path.Combine(dir, "bin", "x64_retail");
        Directory.CreateDirectory(retail);
        try
        {
            var dll = Path.Combine(retail, "dxgi.dll");
            File.WriteAllText(dll, "ReShade");
            new Isolamento(_ => { }).Aplicar(EstadoIsolamento.SemReShade, dir, retail);
            Assert.False(File.Exists(dll));
            Assert.True(File.Exists(dll + Isolamento.Sufixo));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string PastaComExe(string nomeExe)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-tf2det-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, nomeExe), DeteccaoEscolheONomeDoReShadeTests.ExeX64("D3D11CreateDevice"));
        return dir;
    }
}

public class FeedLogTests
{
    // dlss5-feed.log real do NFS (2015): funcionou a 1280x720 e caiu ao reconstruir em 4K.
    private const string LogNfs =
        "15:38:28.311  dlss5-feed 0.5.0 (built Aug 30 2026 12:38:05) attached.\n" +
        "15:38:34.443  [feed] building: 1280x720 backbuffer R8G8B8A8_UNORM (mv R16G16_FLOAT, depth R32_FLOAT, depth reversed=1)\n" +
        "15:38:34.523  [feed] feature ready: 1280x720 DLAA, flags=74 (SDR MVLowRes DepthInverted AutoExposure), color R8G8B8A8_UNORM -> output R8G8B8A8_UNORM\n" +
        "15:38:34.532  [feed] frame 1 delivered (1280x720, reset=1)\n" +
        "15:38:35.083  [feed] frame 2 delivered (1280x720, reset=0)\n" +
        "15:38:39.644  [feed] building: 3840x2160 backbuffer R8G8B8A8_UNORM (mv R16G16_FLOAT, depth R32_FLOAT, depth reversed=1)\n" +
        "15:38:39.703  [feed] CreateFeature raised exception 0xC0000005 (caught; nothing was submitted)\n" +
        "15:38:39.704  stopped: creating the DLSS feature crashed (the DLSS 5 add-on may be incompatible). The game renders normally. See dlss5-feed.log for the detail.\n" +
        "15:38:39.704  [feed] failure: resource build\n" +
        "15:38:39.705  ### CRASH RECORDED ###  exception 0xE06D7363 at 00007FFA6DBB3CFA in C:\\WINDOWS\\System32\\KERNELBASE.dll; this add-on was last doing: creating the DLSS feature\n";

    [Fact]
    public void LeOFimDoLogENaoSoOComeco()
    {
        var s = FeedLog.Ler(LogNfs)!;
        Assert.True(s.FeaturePronta);
        Assert.Equal(2, s.FramesEntregues);
        Assert.True(s.Travou);
        Assert.Equal("1280x720", s.ResolucaoQueFuncionou);
        Assert.Equal("3840x2160", s.ResolucaoQueFalhou);
        Assert.True(s.CaiuNaTrocaDeResolucao);
        Assert.Equal("creating the DLSS feature", s.UltimaAcao);
        Assert.StartsWith("stopped:", s.Motivo);
    }

    [Fact]
    public void LogSaudavelNaoTravou()
    {
        var s = FeedLog.Ler("[feed] building: 2560x1440 backbuffer\n[feed] feature ready: 2560x1440 DLAA\n[feed] frame 1 delivered\n[feed] frame 300 delivered\n")!;
        Assert.False(s.Travou);
        Assert.False(s.CaiuNaTrocaDeResolucao);
        Assert.Equal(300, s.FramesEntregues);
        Assert.Null(FeedLog.Ler(""));
    }

    [Fact]
    public void Checkpoint15AcusaAQuedaEO14NaoVendeAJanelaInicialComoOk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-feedcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "dlss5-feed.log"), LogNfs);
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");
            File.WriteAllText(Path.Combine(dir, "ReShade.log"),
                "INFO | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) loaded from 'dxgi.dll' into 'NFS16.exe' ...\n" +
                "INFO | Registered add-on \"DLSS 5 Neural Rendering\" v0.2026.828.517\n" +
                "INFO | Redirecting IDXGIFactory::CreateSwapChain(...) ...\n" +
                "INFO | [DLSS 5 Neural Rendering] DLSS5 Generic: inline feature 18 evaluation succeeded (count=1, NR input 1280x720 (guides 1280x720), output 1280x720 [native])\n" +
                new string(' ', 2000));

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "NFS16.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
            };
            var checagens = CheckpointVerifier.Verify(perfil, null);

            // O log é do 0.5.0: o remédio que vale é o Feeder novo do kit, e a queda
            // (janela inicial → 4K) continua descrita. A escada do work_resolution é
            // para o 0.12.0 — o 0.5.0 nem lê a chave.
            var c15 = checagens.First(c => c.Number == 15);
            Assert.Equal(CheckStatus.Fail, c15.State);
            Assert.Contains("Feeder 0.5.0", c15.Detail);
            Assert.Contains("1280x720", c15.Detail);
            Assert.Contains("3840x2160", c15.Detail);
            Assert.Contains("Instalar de novo", c15.FixHint!);
            Assert.DoesNotContain("Resolução de trabalho do Feeder", c15.FixHint!);

            var c14 = checagens.First(c => c.Number == 14);
            Assert.Equal(CheckStatus.Warning, c14.State);
            Assert.Contains("janela inicial", c14.Detail);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FeedCfgTrocaSoAChavePedidaEPreservaOResto()
    {
        var cfg = "# config\r\nenabled=1\r\nmode=2\r\nwork_resolution=100\r\nhdr=-1\r\n";
        Assert.Equal(100, FeedCfg.Ler(cfg));
        var novo = FeedCfg.Gravar(cfg, 67);
        Assert.Equal(67, FeedCfg.Ler(novo));
        Assert.Contains("enabled=1\r\n", novo);
        Assert.Contains("hdr=-1\r\n", novo);
        Assert.Contains("# config\r\n", novo);

        // Sem a chave: acrescenta. Vazio: cria.
        Assert.Equal(75, FeedCfg.Ler(FeedCfg.Gravar("enabled=1\n", 75)));
        Assert.Equal(50, FeedCfg.Ler(FeedCfg.Gravar(null, 50)));
        Assert.Null(FeedCfg.Ler("enabled=1"));
        Assert.Equal(ResolucaoPadraoEsperado(), FeedCfg.ResolucaoPadrao);
    }

    private static int ResolucaoPadraoEsperado() => 100;
}

public class SemLogNaFrostbiteTests
{
    [Fact]
    public void SemReShadeLogADicaMandaDesligarOEaAppETrocarONomeDaDll()
    {
        // NFS: "abre, sem Home, sem log". A escada tem que estar na dica, na ordem certa, com
        // o OUTRO nome da DLL para a API do jogo — e não terminar em "troque de exe".
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-nfs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "NeedForSpeedHeat.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
            };
            var c7 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 7);
            Assert.Equal(CheckStatus.Manual, c7.State);
            Assert.Contains("EA App", c7.FixHint!);
            Assert.Contains("d3d11.dll", c7.FixHint!);

            perfil.Api = GraphicsApi.D3D12;
            var c7d12 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 7);
            Assert.Contains("d3d12.dll", c7d12.FixHint!);

            perfil.NomeDoReShadeEscolhido = "d3d11.dll"; perfil.Api = GraphicsApi.D3D11;
            var c7volta = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 7);
            Assert.Contains("para dxgi.dll", c7volta.FixHint!);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void JogoDoEaAppGanhaANotaDaSobreposicaoNaDeteccao()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-eaapp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "__Installer"));
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "NeedForSpeedHeat.exe"), DeteccaoEscolheONomeDoReShadeTests.ExeX64("D3D11CreateDevice"));
            File.WriteAllText(Path.Combine(dir, "__Installer", "installerdata.xml"), "<x/>");
            var r = GameDetector.Detect(dir);
            Assert.Contains(r.Notes, n => n.Contains("EA App", StringComparison.Ordinal) && n.Contains("d3d11.dll", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class EaJavelinTests
{
    private static string Pasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-ea-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ReconhecePeloLauncherDoAnticheatEPeloLiveEditor()
    {
        var dir = Pasta();
        try
        {
            Assert.False(EaJavelin.EhJavelin(dir));
            File.WriteAllText(Path.Combine(dir, EaJavelin.Launcher), "x");
            Assert.True(EaJavelin.EhJavelin(dir));
            Assert.False(EaJavelin.EhJavelin(null));

            var le = Path.Combine(dir, "LE");
            Directory.CreateDirectory(le);
            var launcher = Path.Combine(le, EaJavelin.LauncherDoLiveEditor);
            File.WriteAllText(launcher, "x");
            Assert.False(EaJavelin.PareceLiveEditor(launcher));      // sem a DLL ao lado
            File.WriteAllText(Path.Combine(le, EaJavelin.DllDoLiveEditor), "x");
            Assert.True(EaJavelin.PareceLiveEditor(launcher));
            Assert.False(EaJavelin.PareceLiveEditor(null));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SemReShadeLogNumJogoJavelinADicaEhOLiveEditorENaoOsOverlays()
    {
        // O FC 26 pela Steam: os arquivos estão certos, o log nunca nasce. Mandar desligar
        // overlays aqui era a rodada perdida de sempre.
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, EaJavelin.Launcher), "x");
            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "FC26.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                HasNativeDlss = true,
            };

            var c7 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 7);
            Assert.Equal(CheckStatus.Fail, c7.State);
            Assert.Contains("Javelin", c7.Detail);
            Assert.Contains("Live Editor", c7.FixHint!);
            Assert.Contains("OFFLINE", c7.FixHint!);

            var plan = InstallPlanBuilder.Build(perfil, PlanBuilderTests.KitCompleto(), new InstallOptions());
            Assert.True(plan.CanRun);   // os arquivos são os mesmos: aviso, não bloqueio
            Assert.Contains(plan.Warnings, w => w.Contains("Javelin", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class CheckpointFoxEngineTests
{
    private static string Pasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-fox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private const string LogGz =
        "11:10:30:025 [23288] | INFO  | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) loaded from 'd3d11.dll' into 'MgsGroundZeroes.exe' ...\n" +
        "11:10:31:175 [23288] | INFO  | Redirecting D3D11CreateDevice(pAdapter = 1) ...\n" +
        "11:10:31:189 [23288] | INFO  | Registered add-on \"DLSS 5 Neural Rendering\" v0.2026.828.517 using ReShade API version 18.\n" +
        "11:10:31:190 [23288] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: RenoDX DLSS5 Generic v4.1.5 loaded | EnableHooks=2: NGX hooks only\n" +
        "11:10:31:823 [23288] | INFO  | Exiting ...\n";

    [Fact]
    public void CheckpointDizQueOJogoSeFechouENomeiaAProtecaoDaFoxEngine()
    {
        // Antes, este log caía no ramo "Warning / abra o jogo, jogue alguns segundos e
        // verifique de novo" — conselho impossível de seguir num jogo que fecha sozinho.
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");
            File.WriteAllText(Path.Combine(dir, "ReShade.log"), LogGz + new string(' ', 2000));

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "MgsGroundZeroes.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                NomeDoReShadeEscolhido = "d3d11.dll",
            };

            var checagens = CheckpointVerifier.Verify(perfil, null);
            var c14 = checagens.First(c => c.Number == 14);

            Assert.Equal(CheckStatus.Fail, c14.State);
            Assert.Contains("SAIU sozinho", c14.Detail);
            Assert.Contains("CheckModuleHook", c14.FixHint!);
            // Ground Zeroes: sem patch, a dica diz isso e não manda reinstalar.
            Assert.Contains("não há patch publicado", c14.FixHint!);

            var c19 = checagens.First(c => c.Number == 19);
            Assert.Equal(CheckStatus.Fail, c19.State);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ComOBackupDoPatchOItem19PassaEADicaDo14MudaDeAssunto()
    {
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");
            File.WriteAllText(Path.Combine(dir, "ReShade.log"), LogGz + new string(' ', 2000));
            // Exe remendado por outra versão do patch (hash desconhecido) com o backup do
            // patcher ao lado: conta como aplicado.
            File.WriteAllBytes(Path.Combine(dir, "mgsvtpp.exe"), new byte[777]);
            File.WriteAllText(Path.Combine(dir, "mgsvtpp.exe" + MotorFox.SufixoDoBackup), "exe original");

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "mgsvtpp.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
            };

            var checagens = CheckpointVerifier.Verify(perfil, null);
            Assert.Equal(CheckStatus.Pass, checagens.First(c => c.Number == 19).State);
            // O jogo fechou MESMO com o backup na pasta: a suspeita vira a Steam ter
            // restaurado o exe original, não "aplique o patch".
            var c14 = checagens.First(c => c.Number == 14);
            Assert.Contains("integridade", c14.FixHint!);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ForaDaFoxEngineADicaNaoInventaOMotor()
    {
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");
            File.WriteAllText(Path.Combine(dir, "ReShade.log"), LogGz + new string(' ', 2000));

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "outrojogo.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
            };

            var c14 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 14);

            Assert.Equal(CheckStatus.Fail, c14.State);
            Assert.DoesNotContain("Fox Engine", c14.FixHint!);
            Assert.Contains("Testar só o ReShade", c14.FixHint!);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class VerificacaoSemRenodxNoLogTests
{
    private static string Pasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-cp14-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static GameProfile Perfil(string dir) => new()
    {
        GameFolder = dir,
        RealExePath = Path.Combine(dir, "re9.exe"),
        Architecture = PeArchitecture.X64,
        Api = GraphicsApi.D3D12,
        HasNativeDlss = true,
    };

    private const string LogSemAddonComQueda =
        "00:33:51:039 [ 4524] | INFO  | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) loaded from 'dxgi.dll' into 're9.exe' ...\n" +
        "00:33:56:151 [35364] | INFO  | Redirecting IDXGIFactory2::CreateSwapChainForHwnd(this = 1, ppSwapChain = ) ...\n" +
        "00:33:59:035 [35364] | INFO  | Recreated runtime environment on runtime 1 ('ReShade.ini').\n" +
        "00:34:01:257 [35364] | INFO  | Redirecting RegisterClassExW(lpWndClassEx = 1 { \"DirectUIHWND\", style = 0x4000 }) ...\n" +
        "00:34:30:141 [35364] | INFO  | Exiting ...\n";

    [Fact]
    public void AddonLigadoNoLogSemLinhaDeleNaoViraTesteDeIsolamento()
    {
        // Com o addon LIGADO na pasta, dizer que o log é de um "teste de isolamento" é
        // chute vendido como fato. O que se pode afirmar é o que o log mostra.
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");
            File.WriteAllText(Path.Combine(dir, "ReShade.log"), LogSemAddonComQueda + new string(' ', 2000));

            var c14 = CheckpointVerifier.Verify(Perfil(dir), null).First(c => c.Number == 14);

            Assert.Equal(CheckStatus.Fail, c14.State);
            Assert.DoesNotContain("teste de isolamento", c14.Detail);
            Assert.Contains("viu o swapchain", c14.Detail);
            Assert.Contains("2.2 s", c14.Detail);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SemSwapchainOAddonNemTeveChanceDeRodar()
    {
        // O caso do MGS V: o ReShade carrega, nunca chega ao swapchain, e por isso NENHUM
        // addon é carregado. Culpar o RenoDX aqui manda caçar a pista errada.
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");
            File.WriteAllText(Path.Combine(dir, "ReShade.log"),
                "00:33:51:039 [1] | INFO  | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) ...\n" +
                "00:33:51:066 [1] | INFO  | Registering hooks for 'd3d11.dll' ...\n" + new string(' ', 2000));

            var c14 = CheckpointVerifier.Verify(Perfil(dir), null).First(c => c.Number == 14);

            Assert.Equal(CheckStatus.Fail, c14.State);
            Assert.Contains("nunca chegou ao swapchain", c14.Detail);
            Assert.Contains("nem teve chance", c14.Detail);
            Assert.Contains("item 8", c14.FixHint);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ComOAddonRenomeadoAiSimEhOTesteDeIsolamento()
    {
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64" + Isolamento.Sufixo), "x");
            File.WriteAllText(Path.Combine(dir, "ReShade.log"),
                "00:33:51:039 [1] | INFO  | Initializing crosire's ReShade version '6.8.0' ...\n" +
                "00:33:59:035 [1] | INFO  | Recreated runtime environment on runtime 1 ('ReShade.ini').\n" + new string(' ', 2000));

            var c14 = CheckpointVerifier.Verify(Perfil(dir), null).First(c => c.Number == 14);

            Assert.Equal(CheckStatus.Manual, c14.State);
            Assert.Contains("teste de isolamento", c14.Detail);
            Assert.Contains("Religue o RenoDX", c14.FixHint);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ADicaDoSwapchainNaoMandaReporODxgiQuandoONomeEhOutro()
    {
        // No MGS V o dxgi.dll é o arquivo que IMPEDE o jogo de abrir: mandar repô-lo era
        // o pior conselho possível.
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "ReShade.log"),
                "00:33:51:039 [1] | INFO  | Initializing crosire's ReShade version '6.8.0' ...\n" + new string(' ', 2000));
            var perfil = Perfil(dir);
            perfil.Api = GraphicsApi.D3D11;
            perfil.NomeDoReShadeEscolhido = "d3d11.dll";

            var c8 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 8);

            Assert.Equal(CheckStatus.Fail, c8.State);
            Assert.Contains("d3d11.dll", c8.FixHint);
            Assert.DoesNotContain("dxgi.dll", c8.FixHint);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LogSemOAddonESemQuedaSoPedeParaReligar()
    {
        var dir = Pasta();
        try
        {
            // Addon desligado pelo isolamento também conta como "está na pasta".
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64" + Isolamento.Sufixo), "x");
            File.WriteAllText(Path.Combine(dir, "ReShade.log"),
                "00:33:51:039 [1] | INFO  | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) ...\n" +
                "00:33:59:035 [1] | INFO  | Recreated runtime environment on runtime 1 ('ReShade.ini').\n" + new string(' ', 2000));

            var c14 = CheckpointVerifier.Verify(Perfil(dir), null).First(c => c.Number == 14);

            Assert.Equal(CheckStatus.Manual, c14.State);
            Assert.Contains("Religue o RenoDX", c14.FixHint);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SemOAddonNaPastaNaoHaCheckpoint14()
    {
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "ReShade.log"), LogSemAddonComQueda + new string(' ', 2000));
            Assert.DoesNotContain(CheckpointVerifier.Verify(Perfil(dir), null), c => c.Number == 14);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OsShadersSaoConferidosTambemNoCaminhoDireto()
    {
        // A pasta de shaders voltou a ser instalada nos dois caminhos: sem ela o ReShade
        // abre reclamando "nenhum arquivo de efeito encontrado", o que parece defeito.
        var dir = Pasta();
        try
        {
            var c11 = CheckpointVerifier.Verify(Perfil(dir), null).First(c => c.Number == 11);
            Assert.Equal(CheckStatus.Fail, c11.State);

            Directory.CreateDirectory(Path.Combine(dir, "reshade-shaders", "Shaders"));
            File.WriteAllText(Path.Combine(dir, "reshade-shaders", "Shaders", "DLSS5_Feed.fx"), "x");
            var ok = CheckpointVerifier.Verify(Perfil(dir), null).First(c => c.Number == 11);
            Assert.Equal(CheckStatus.Pass, ok.State);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class ReFrameworkTests
{
    private static string NovaPasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-ref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ReEngineEhReconhecidaPelosPaks()
    {
        var dir = NovaPasta();
        try
        {
            Assert.False(ReFramework.EhReEngine(dir));
            File.WriteAllText(Path.Combine(dir, "re_chunk_000.pak"), "x");
            Assert.True(ReFramework.EhReEngine(dir));
        }
        finally { Directory.Delete(dir, true); }

        Assert.False(ReFramework.EhReEngine(null));
        Assert.False(ReFramework.EhReEngine(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid())));
    }

    [Fact]
    public void OIniDoModoHospedadoUsaCaminhoAbsoluto()
    {
        // Hospedado em reframework\plugins, o ReShade resolve caminho relativo a partir
        // da pasta dele: com ".\" ele procuraria o addon dentro de plugins e o painel
        // abriria sem o DLSS 5. Foi o que aconteceu no primeiro teste do RE9.
        var jogo = Path.Combine(Path.GetTempPath(), "JogoRE");
        var ini = ReShadeConfigWriter.BuildReShadeIni(feederUsed: false, baseDir: jogo);

        Assert.Contains($"AddonPath={jogo}", ini);
        Assert.Contains(Path.Combine(jogo, "ReShadePreset.ini"), ini);
        Assert.Contains(Path.Combine(jogo, @"reshade-shaders\Shaders\**"), ini);
        Assert.DoesNotContain(@"AddonPath=.\", ini);

        // Sem baseDir nada muda: continua relativo, como sempre foi.
        var normal = ReShadeConfigWriter.BuildReShadeIni(feederUsed: false);
        Assert.Contains(@"AddonPath=.\", normal);
    }

    [Fact]
    public void PlanoPoeOReFrameworkAoLadoDoReShade()
    {
        var dir = NovaPasta();
        try
        {
            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "re9.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                HasNativeDlss = true,
                UsarReFramework = true,
            };

            var kitDir = NovaPasta();
            try
            {
                var kit = new KitInventory { KitRoot = kitDir }; 
                kit.DxgiX64 = Path.Combine(kitDir, "dxgi.dll");
                kit.RenodxAddon64 = Path.Combine(kitDir, "renodx-dlss5.addon64");
                kit.NvngxDlssnr = Path.Combine(kitDir, "nvngx_dlssnr.dll");
                kit.NvngxDlss = Path.Combine(kitDir, "nvngx_dlss.dll");
                kit.ShadersDir = kitDir;
                kit.HasDrme = true;
                kit.ReFrameworkDinput8 = Path.Combine(kitDir, "dinput8.dll");
                kit.ReFrameworkRevision = Path.Combine(kitDir, ReFramework.RevisionFile);

                var plan = InstallPlanBuilder.Build(perfil, kit, new InstallOptions());

                bool Alvo(string trecho) => plan.Actions.Any(a =>
                    a.TargetPath?.Contains(trecho, StringComparison.OrdinalIgnoreCase) == true);

                // O REFramework entra AO LADO do ReShade, não no lugar dele: o binário
                // dele traz o desarme da checagem de integridade (com patch nomeado para
                // o RE9), e é isso que deixa a DLL do ReShade conviver com o jogo.
                Assert.True(Alvo(ReFramework.Dinput8));
                Assert.True(Alvo(Path.Combine(dir, "dxgi.dll")));
                // Nada é hospedado dentro de reframework\plugins.
                Assert.DoesNotContain(plan.Actions, a =>
                    a.TargetPath?.Contains(Path.Combine("reframework", "plugins"), StringComparison.OrdinalIgnoreCase) == true);
                // E o ini é o de sempre, ao lado do executável.
                Assert.Equal(Path.Combine(dir, "ReShade.ini"), perfil.ReShadeIniPath);
            }
            finally { Directory.Delete(kitDir, true); }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SemOREFrameworkNoKitOPlanoBloqueia()
    {
        var dir = NovaPasta();
        try
        {
            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "re9.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                HasNativeDlss = true,
                UsarReFramework = true,
            };
            var kit = new KitInventory { KitRoot = dir };
            var plan = InstallPlanBuilder.Build(perfil, kit, new InstallOptions());
            Assert.Contains(plan.Blockers, b => b.Contains("REFramework", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FaxinaSoRemoveODinput8QueEhDoKit()
    {
        var jogo = NovaPasta();
        var kit = NovaPasta();
        try
        {
            File.WriteAllText(Path.Combine(kit, ReFramework.Dinput8), "bytes do REFramework");
            File.WriteAllText(Path.Combine(jogo, ReFramework.Dinput8), "bytes do REFramework");
            File.WriteAllText(Path.Combine(jogo, ReFramework.RevisionFile), "abc");
            File.WriteAllText(Path.Combine(jogo, "renodx-dlss5.addon64"), "x");
            Directory.CreateDirectory(ReFramework.PastaPlugins(jogo));
            File.WriteAllText(ReFramework.CaminhoPlugin(jogo), "reshade");

            var engine = new InstallerEngine(_ => { })
            {
                ReFrameworkDoKit = Path.Combine(kit, ReFramework.Dinput8),
            };
            engine.LimpezaTotal(jogo);

            Assert.False(File.Exists(Path.Combine(jogo, ReFramework.Dinput8)));
            Assert.False(File.Exists(ReFramework.CaminhoPlugin(jogo)));
            Assert.False(File.Exists(Path.Combine(jogo, ReFramework.RevisionFile)));
        }
        finally { Directory.Delete(jogo, true); Directory.Delete(kit, true); }
    }

    [Fact]
    public void UmDinput8DiferenteDoKitNaoEhTocado()
    {
        // Pode ser um REFramework que o usuário instalou por conta, com mods dele em volta.
        var jogo = NovaPasta();
        var kit = NovaPasta();
        try
        {
            File.WriteAllText(Path.Combine(kit, ReFramework.Dinput8), "bytes do kit");
            File.WriteAllText(Path.Combine(jogo, ReFramework.Dinput8), "OUTRA versao");
            File.WriteAllText(Path.Combine(jogo, "renodx-dlss5.addon64"), "x");

            new InstallerEngine(_ => { })
            {
                ReFrameworkDoKit = Path.Combine(kit, ReFramework.Dinput8),
            }.LimpezaTotal(jogo);

            Assert.True(File.Exists(Path.Combine(jogo, ReFramework.Dinput8)));

            // E sem gabarito nenhum, idem.
            File.WriteAllText(Path.Combine(jogo, "renodx-dlss5.addon64"), "x");
            new InstallerEngine(_ => { }).LimpezaTotal(jogo);
            Assert.True(File.Exists(Path.Combine(jogo, ReFramework.Dinput8)));
        }
        finally { Directory.Delete(jogo, true); Directory.Delete(kit, true); }
    }
}

public class ReFrameworkForaDaReEngineTests
{
    private static string Pasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-refw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static GameProfile Perfil(string dir, bool reEngine) 
    {
        File.WriteAllText(Path.Combine(dir, "jogo.exe"), "x");
        if (reEngine) File.WriteAllText(Path.Combine(dir, "re_chunk_000.pak"), "x");
        return new GameProfile
        {
            GameFolder = dir,
            RealExePath = Path.Combine(dir, "jogo.exe"),
            Architecture = PeArchitecture.X64,
            Api = GraphicsApi.D3D12,
            HasNativeDlss = true,
            UsarReFramework = true,
        };
    }

    private static KitInventory KitComReFramework(string dir)
    {
        var dinput8 = Path.Combine(dir, "kit-dinput8.dll");
        File.WriteAllText(dinput8, "reframework");
        return new KitInventory
        {
            KitRoot = dir,
            DxgiX64 = dinput8,
            RenodxAddon64 = dinput8,
            NvngxDlssnr = dinput8,
            ReFrameworkDinput8 = dinput8,
        };
    }

    [Fact]
    public void PastaSemReChunkAvisaMasNaoBloqueia()
    {
        var dir = Pasta();
        try
        {
            var plan = InstallPlanBuilder.Build(Perfil(dir, reEngine: false), KitComReFramework(dir), new InstallOptions());

            Assert.Contains(plan.Warnings, w => w.Contains("não parece um jogo da RE Engine", StringComparison.Ordinal));
            // Aviso não pode virar desvio: o REFramework continua sendo instalado.
            Assert.DoesNotContain(plan.Blockers, b => b.Contains("REFramework", StringComparison.Ordinal));
            Assert.Contains(plan.Actions, a =>
                a.TargetPath?.EndsWith(ReFramework.Dinput8, StringComparison.OrdinalIgnoreCase) == true);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PastaComReChunkNaoAvisaNada()
    {
        var dir = Pasta();
        try
        {
            var plan = InstallPlanBuilder.Build(Perfil(dir, reEngine: true), KitComReFramework(dir), new InstallOptions());

            Assert.DoesNotContain(plan.Warnings, w => w.Contains("não parece um jogo da RE Engine", StringComparison.Ordinal));
            Assert.Contains(plan.Actions, a =>
                a.TargetPath?.EndsWith(ReFramework.Dinput8, StringComparison.OrdinalIgnoreCase) == true);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SemOReFrameworkNoKitContinuaSendoBloqueio()
    {
        var dir = Pasta();
        try
        {
            var kit = KitComReFramework(dir);
            kit.ReFrameworkDinput8 = null;
            var plan = InstallPlanBuilder.Build(Perfil(dir, reEngine: true), kit, new InstallOptions());

            Assert.Contains(plan.Blockers, b => b.Contains("REFramework", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class ReFrameworkLogTests
{
    [Fact]
    public void PastaEmUsoEhADoJogoQuandoOLogEstaLa()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-refwlog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var exe = Path.Combine(dir, "re9.exe");
            Assert.Null(ReFramework.PastaEmUso(dir, exe));

            File.WriteAllText(Path.Combine(dir, ReFramework.LogDoFramework), "REFramework entry");
            Assert.Equal(dir, ReFramework.PastaEmUso(dir, exe));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PastaAppDataUsaONomeDoExecutavel()
    {
        var caminho = ReFramework.PastaAppData(Path.Combine("G:", "jogo", "re9.exe"));
        Assert.EndsWith(Path.Combine("REFramework", "re9"), caminho);
    }

    [Fact]
    public void OLogDizSeOPluginEntrou()
    {
        // Linha real do PluginLoader do REFramework.
        Assert.True(ReFramework.CarregouOPlugin(
            @"[PluginLoader] Loaded G:\jogo
eframework\plugins\ReShade64.dll"));
        // Plugin sem o export do REFramework é PULADO, não descarregado — e a linha
        // "Loaded" vem antes, então continua contando como carregado.
        Assert.True(ReFramework.CarregouOPlugin(
            "[PluginLoader] Loaded ReShade64.dll\n" +
            "[PluginLoader] ReShade64 has no reframework_plugin_required_version function, skipping..."));

        Assert.False(ReFramework.CarregouOPlugin("[PluginLoader] No plugins loaded."));
        Assert.False(ReFramework.CarregouOPlugin(null));
    }
}

public class NomeDoReShadeTests
{
    private static GameProfile Perfil(string exe, GraphicsApi api) => new()
    {
        GameFolder = Path.GetTempPath(),
        RealExePath = Path.Combine(Path.GetTempPath(), exe),
        Architecture = PeArchitecture.X64,
        Api = api,
    };

    [Fact]
    public void OPadraoContinuaSendoODxgi()
    {
        Assert.Equal("dxgi.dll", Perfil("jogo.exe", GraphicsApi.D3D11).ReShadeHookName);
        Assert.Equal("dxgi.dll", Perfil("jogo.exe", GraphicsApi.D3D12).ReShadeHookName);
        Assert.Equal("opengl32.dll", Perfil("jogo.exe", GraphicsApi.OpenGL).ReShadeHookName);
    }

    [Fact]
    public void EscolherOutroNomeTrocaOArquivoInstalado()
    {
        var p = Perfil("jogo.exe", GraphicsApi.D3D11);
        p.NomeDoReShadeEscolhido = "d3d11.dll";
        Assert.Equal("d3d11.dll", p.ReShadeHookName);

        // Em OpenGL o nome é ditado pela API: escolha nenhuma muda isso.
        var gl = Perfil("jogo.exe", GraphicsApi.OpenGL);
        gl.NomeDoReShadeEscolhido = "d3d11.dll";
        Assert.Equal("opengl32.dll", gl.ReShadeHookName);
    }

    [Fact]
    public void FoxEngineNaoPedeMaisD3d11()
    {
        // A preferência por d3d11.dll na Fox Engine era lenda de fórum: a checagem do jogo
        // (CheckModuleHook) olha o gancho no D3D11, não o nome do arquivo — o teste "só o
        // ReShade" com d3d11.dll fechou o jogo igual. Com o patch anti-hook o ReShade entra
        // como dxgi.dll, como as instruções do patcher mandam.
        static string Exe(string nome) => Path.Combine("jogo", nome);
        Assert.Null(GameProfile.NomeDeReShadePreferido(Exe("mgsvtpp.exe"), GraphicsApi.D3D11));
        Assert.Null(GameProfile.NomeDeReShadePreferido(Exe("MgsGroundZeroes.exe"), GraphicsApi.D3D11));
        Assert.Null(GameProfile.NomeDeReShadePreferido(Exe("re9.exe"), GraphicsApi.D3D12));
        Assert.Null(GameProfile.NomeDeReShadePreferido(null, GraphicsApi.D3D11));
    }

    [Fact]
    public void APastaSeLimpaDoNomeAlternativo()
    {
        // Sem isto a desinstalação deixaria para trás justamente o arquivo que impede o
        // jogo de abrir, e o usuário não teria como saber de onde ele veio.
        Assert.Contains(Propriedade.PrecisamDeProva, p => p.Nome == "d3d11.dll" && p.Prova == "ReShade");
        Assert.Contains(Propriedade.PrecisamDeProva, p => p.Nome == "d3d12.dll" && p.Prova == "ReShade");
    }

    [Fact]
    public void OPlanoInstalaComONomeEscolhido()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-nome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var arq = Path.Combine(dir, "x");
            File.WriteAllText(arq, "x");
            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "mgsvtpp.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                NomeDoReShadeEscolhido = "d3d11.dll",
            };
            var kit = new KitInventory { KitRoot = dir, DxgiX64 = arq, RenodxAddon64 = arq, NvngxDlssnr = arq };

            var plan = InstallPlanBuilder.Build(perfil, kit, new InstallOptions());

            Assert.Contains(plan.Actions, a =>
                a.TargetPath?.EndsWith("d3d11.dll", StringComparison.OrdinalIgnoreCase) == true);
            Assert.DoesNotContain(plan.Actions, a =>
                a.TargetPath?.EndsWith("dxgi.dll", StringComparison.OrdinalIgnoreCase) == true);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class IsolamentoDoFeederTests
{
    [Fact]
    public void DesligaSoOFeederEDeixaReShadeERenodx()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-feedoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "host64"));
        try
        {
            foreach (var n in new[] { "dxgi.dll", "renodx-dlss5.addon64", "dlss5-feed.addon64" })
                File.WriteAllText(Path.Combine(dir, n), "x");
            File.WriteAllText(Path.Combine(dir, "host64", "dlss5-feed-host64.exe"), "x");

            var iso = new Isolamento(_ => { });
            var desligados = iso.Aplicar(EstadoIsolamento.SemFeeder, dir, dir);

            Assert.Contains(desligados, d => d.EndsWith("dlss5-feed.addon64", StringComparison.Ordinal));
            Assert.False(File.Exists(Path.Combine(dir, "dlss5-feed.addon64")));
            Assert.False(File.Exists(Path.Combine(dir, "host64", "dlss5-feed-host64.exe")));
            // O que NÃO é o Feeder fica de pé: é isso que torna o teste conclusivo.
            Assert.True(File.Exists(Path.Combine(dir, "dxgi.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "renodx-dlss5.addon64")));

            iso.Aplicar(EstadoIsolamento.Tudo, dir, dir);
            Assert.True(File.Exists(Path.Combine(dir, "dlss5-feed.addon64")));
            Assert.True(File.Exists(Path.Combine(dir, "host64", "dlss5-feed-host64.exe")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ALeituraDizOQueCadaResultadoSignifica()
    {
        var texto = Isolamento.Leitura(EstadoIsolamento.SemFeeder);
        Assert.Contains("FEEDER DESLIGADO", texto);
        Assert.Contains("Se ele ABRIR agora", texto);
        Assert.Contains("Isolar a causa", texto);
    }
}

public class CarimboDoBuildTests
{
    [Fact]
    public void SemCarimboDoCiOBuildEhLocal()
    {
        // Os testes rodam sem SourceRevisionId, então é este o caminho exercitado aqui:
        // o importante é nunca devolver vazio nem explodir.
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Build));
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Versao));
    }

    [Fact]
    public void VersaoComBuildJuntaOsDois()
    {
        var texto = AppInfo.VersaoComBuild;
        Assert.Contains(AppInfo.Versao, texto);
        Assert.Contains(AppInfo.Build, texto);
        Assert.Contains("build", texto);
    }

    [Fact]
    public void OBuildNuncaCarregaOSha40Inteiro()
    {
        // 7 caracteres é o que se lê num print sem esforço; o sha inteiro polui a tela.
        Assert.True(AppInfo.Build.Length <= 7, $"Build longo demais: {AppInfo.Build}");
    }
}

public class DeteccaoEscolheONomeDoReShadeTests
{
    /// <summary>
    /// PE x64 mínimo que o detector aceita: cabeçalho MZ, ponteiro em 0x3C, assinatura
    /// "PE\0\0" e a máquina. O resto é preenchimento com o marcador da API enterrado
    /// dentro, que é como o ApiDetector reconhece D3D11.
    /// </summary>
    internal static byte[] ExeX64(string marcador)
    {
        var buf = new byte[8 * 1024];
        buf[0] = (byte)'M'; buf[1] = (byte)'Z';
        const int peOff = 0x100;
        BitConverter.GetBytes(peOff).CopyTo(buf, 0x3C);
        buf[peOff] = (byte)'P'; buf[peOff + 1] = (byte)'E';
        BitConverter.GetBytes((ushort)0x8664).CopyTo(buf, peOff + 4);   // AMD64
        System.Text.Encoding.ASCII.GetBytes(marcador).CopyTo(buf, 0x800);
        return buf;
    }

    private static string PastaComExe(string nomeExe)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-det-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, nomeExe), ExeX64("D3D11CreateDevice"));
        return dir;
    }

    [Fact]
    public void OGroundZeroesSaiDaDeteccaoComOMecanismoEOAvisoDoPatch()
    {
        var dir = PastaComExe("MgsGroundZeroes.exe");
        try
        {
            var r = GameDetector.Detect(dir);

            Assert.Equal(PeArchitecture.X64, r.Profile.Architecture);
            Assert.Null(r.Profile.NomeDoReShadeEscolhido);
            Assert.Equal("dxgi.dll", r.Profile.ReShadeHookName);
            Assert.Contains(r.Notes, n => n.Contains("CheckModuleHook", StringComparison.Ordinal));
            // Ground Zeroes: sem patcher conhecido, e a nota diz isso em vez de mandar instalar.
            Assert.Contains(r.Notes, n => n.Contains("não há patch publicado", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NaPastaDoPhantomPainOJogoGanhaDoMetalGearOnline()
    {
        // A pasta do MGS V tem mgsvtpp.exe (o jogo) e mgsvmgo.exe (Metal Gear Online),
        // e o segundo é maior. O tamanho escolhia o MGO; a instalação ia para o jogo
        // errado e o plano nem pedia o patch da Fox Engine.
        var dir = PastaComExe("mgsvtpp.exe");
        try
        {
            var mgo = ExeX64("D3D11CreateDevice").Concat(new byte[512 * 1024]).ToArray();
            File.WriteAllBytes(Path.Combine(dir, "mgsvmgo.exe"), mgo);

            var r = GameDetector.Detect(dir);

            Assert.Equal("mgsvtpp.exe", Path.GetFileName(r.Profile.RealExePath));
            Assert.True(MotorFox.EhFoxEngine(r.Profile.RealExePath));
            Assert.Contains(r.Notes, n => n.Contains("não é o 1.0.15.4", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OPhantomPainComOBackupDoPatchSaiLiberado()
    {
        var dir = PastaComExe("mgsvtpp.exe");
        try
        {
            File.WriteAllText(Path.Combine(dir, "mgsvtpp.exe" + MotorFox.SufixoDoBackup), "exe original");
            var r = GameDetector.Detect(dir);
            Assert.True(MotorFox.PatchAplicado(r.Profile.RealExePath));
            Assert.Contains(r.Notes, n => n.Contains("já aplicado", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void JogoComumSegueNoDxgiESemNotaNenhuma()
    {
        var dir = PastaComExe("jogo.exe");
        try
        {
            var r = GameDetector.Detect(dir);

            Assert.Null(r.Profile.NomeDoReShadeEscolhido);
            Assert.Equal("dxgi.dll", r.Profile.ReShadeHookName);
            Assert.DoesNotContain(r.Notes, n => n.Contains("recusa o dxgi.dll", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class IsolamentoSoOReShadeTests
{
    [Fact]
    public void DesligaOsDoisAddonsEDeixaOReShade()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-soreshade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "host64"));
        try
        {
            foreach (var n in new[] { "d3d11.dll", "renodx-dlss5.addon64", "dlss5-feed.addon64" })
                File.WriteAllText(Path.Combine(dir, n), "x");
            File.WriteAllText(Path.Combine(dir, "host64", "dlss5-feed-host64.exe"), "x");

            var iso = new Isolamento(_ => { });
            iso.Aplicar(EstadoIsolamento.SoOReShade, dir, dir);

            // Os dois addons saem juntos: é isso que os testes um a um não conseguem.
            Assert.False(File.Exists(Path.Combine(dir, "renodx-dlss5.addon64")));
            Assert.False(File.Exists(Path.Combine(dir, "dlss5-feed.addon64")));
            Assert.False(File.Exists(Path.Combine(dir, "host64", "dlss5-feed-host64.exe")));
            // E o ReShade fica — inclusive com nome alternativo.
            Assert.True(File.Exists(Path.Combine(dir, "d3d11.dll")));

            iso.Aplicar(EstadoIsolamento.Tudo, dir, dir);
            Assert.True(File.Exists(Path.Combine(dir, "renodx-dlss5.addon64")));
            Assert.True(File.Exists(Path.Combine(dir, "dlss5-feed.addon64")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ALeituraDizOQueCadaResultadoConclui()
    {
        var texto = Isolamento.Leitura(EstadoIsolamento.SoOReShade);
        Assert.Contains("DOIS ADDONS DESLIGADOS", texto);
        Assert.Contains("Se ABRIR agora", texto);
        Assert.Contains("Isolar a causa", texto);
    }
}

public class IsolarOReShadeComNomeAlternativoTests
{
    private static string Pasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-isoname-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void DesligaOReShadeInstaladoComoD3d11()
    {
        // A regressão que isto tranca: o teste listava só dxgi.dll e opengl32.dll, então
        // num jogo instalado como d3d11.dll ele renomeava NADA e ainda assim se
        // apresentava como "ReShade desligado" — e o usuário concluía o oposto do certo.
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "d3d11.dll"), "ReShade");

            var desligados = new Isolamento(_ => { }).Aplicar(EstadoIsolamento.SemReShade, dir, dir);

            Assert.Contains(desligados, d => d.EndsWith("d3d11.dll", StringComparison.Ordinal));
            Assert.False(File.Exists(Path.Combine(dir, "d3d11.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "d3d11.dll" + Isolamento.Sufixo)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UmDxgiDeInstalacaoAnteriorTambemSaiDoTeste()
    {
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "d3d11.dll"), "ReShade");
            File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "ReShade");

            new Isolamento(_ => { }).Aplicar(EstadoIsolamento.SemReShade, dir, dir);

            // Com um deles de pé o teste concluiria errado: os dois carregam ReShade.
            Assert.False(File.Exists(Path.Combine(dir, "d3d11.dll")));
            Assert.False(File.Exists(Path.Combine(dir, "dxgi.dll")));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class PlanoRemoveReShadeAntigoTests
{
    [Fact]
    public void InstalarComNomeNovoRemoveOReShadeComONomeVelho()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-velho-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var arq = Path.Combine(dir, "kit");
            File.WriteAllText(arq, "x");
            // Sobra da instalação anterior, com o nome padrão e conteúdo de ReShade.
            File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "conteudo com ReShade dentro");

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "mgsvtpp.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                NomeDoReShadeEscolhido = "d3d11.dll",
            };
            var kit = new KitInventory { KitRoot = dir, DxgiX64 = arq, RenodxAddon64 = arq, NvngxDlssnr = arq };

            var plan = InstallPlanBuilder.Build(perfil, kit, new InstallOptions());

            Assert.Contains(plan.Actions, a =>
                a.Kind == PlanActionKind.DeleteForbiddenFile &&
                a.TargetPath?.EndsWith("dxgi.dll", StringComparison.OrdinalIgnoreCase) == true);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ArquivoComOMesmoNomeQueNaoEhReShadeNaoEhTocado()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-alheio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var arq = Path.Combine(dir, "kit");
            File.WriteAllText(arq, "x");
            // Wrapper de terceiro com o mesmo nome: sem a prova, não se encosta nele.
            File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "wrapper de outra pessoa");

            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "mgsvtpp.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                NomeDoReShadeEscolhido = "d3d11.dll",
            };
            var kit = new KitInventory { KitRoot = dir, DxgiX64 = arq, RenodxAddon64 = arq, NvngxDlssnr = arq };

            var plan = InstallPlanBuilder.Build(perfil, kit, new InstallOptions());

            Assert.DoesNotContain(plan.Actions, a =>
                a.Kind == PlanActionKind.DeleteForbiddenFile &&
                a.TargetPath?.EndsWith("dxgi.dll", StringComparison.OrdinalIgnoreCase) == true);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class FeederKitTests
{
    [Fact]
    public void LeAVersaoDoBannerDoLog()
    {
        Assert.Equal("0.5.0", FeederKit.VersaoNoLog("15:38:28.311  dlss5-feed 0.5.0 (built Aug 30 2026 12:38:05) attached.\n"));
        Assert.Equal("0.12.0", FeederKit.VersaoNoLog("dlss5-feed 0.12.0 (built Sep  1 2026 10:00:00) attached."));
        Assert.Equal("0.10.0-beta.3", FeederKit.VersaoNoLog("dlss5-feed 0.10.0-beta.3 (built Sep  1 2026 10:00:00) attached."));
        Assert.Null(FeederKit.VersaoNoLog("[feed] building: 1280x720"));
        Assert.Null(FeederKit.VersaoNoLog(null));
    }

    [Fact]
    public void AbaixoDe0120EhAntigo()
    {
        Assert.True(FeederKit.Antiga("0.5.0"));
        Assert.True(FeederKit.Antiga("0.10.0-beta.3"));
        Assert.True(FeederKit.Antiga("0.11.0"));
        Assert.True(FeederKit.Antiga(null));
        Assert.True(FeederKit.Antiga("lixo"));
        Assert.False(FeederKit.Antiga("0.12.0"));
        Assert.False(FeederKit.Antiga("0.12.0.0"));
        Assert.False(FeederKit.Antiga("v0.13.1"));
        Assert.False(FeederKit.Antiga("1.0.0"));
        Assert.Equal("0.12.0", FeederKit.VersaoDoKit);
        Assert.False(FeederKit.Antiga(FeederKit.VersaoDoKit));
    }

    [Fact]
    public void ArquivoSemVersaoEhAntigo()
    {
        // O 0.5.0 não gravava VERSIONINFO nenhum: sem versão = antigo.
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, new byte[] { 0x4D, 0x5A, 0, 0 });
            Assert.True(FeederKit.Antiga(FeederKit.VersaoDoArquivo(tmp)));
        }
        finally { File.Delete(tmp); }
        Assert.Null(FeederKit.VersaoDoArquivo(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void PlanoAvisaKitComFeederAntigo()
    {
        // O kit de teste tem arquivos sem versão gravada: o plano tem que avisar, e só
        // quando o Feeder vai ser usado.
        var plan = InstallPlanBuilder.Build(
            new GameProfile { GameFolder = @"C:\game", RealExePath = @"C:\game\jogo.exe", Architecture = PeArchitecture.X64, Api = GraphicsApi.D3D11 },
            PlanBuilderTests.KitCompleto(), new InstallOptions());
        Assert.Contains(plan.Warnings, w => w.Contains("dlss5-feed.addon64 do kit", StringComparison.Ordinal)
                                          && w.Contains(FeederKit.VersaoDoKit, StringComparison.Ordinal));

        var direto = InstallPlanBuilder.Build(
            new GameProfile { GameFolder = @"C:\game", RealExePath = @"C:\game\jogo.exe", Architecture = PeArchitecture.X64, Api = GraphicsApi.D3D12, HasNativeDlss = true },
            PlanBuilderTests.KitCompleto(), new InstallOptions());
        Assert.DoesNotContain(direto.Warnings, w => w.Contains("dlss5-feed.addon64 do kit", StringComparison.Ordinal));
    }
}

public class PresetComProvedorTests
{
    [Fact]
    public void PresetGravaADefinicaoPorEfeito()
    {
        // O DLSS5_Feed.fx 0.12.0 escolhe o provedor por DLSS5_MV_PROVIDER, e o ReShade guarda
        // a definição POR EFEITO, na seção [DLSS5_Feed.fx] do preset — não no [GENERAL].
        var launchpad = ReShadeConfigWriter.BuildPresetIni(MvProvider.Launchpad);
        Assert.Contains("[DLSS5_Feed.fx]\r\nPreprocessorDefinitions=DLSS5_MV_PROVIDER=1", launchpad.Replace("\n", "\r\n").Replace("\r\r", "\r"));
        Assert.Equal(1, ReShadeConfigWriter.LerProvedorDoPreset(launchpad));

        var drme = ReShadeConfigWriter.BuildPresetIni(MvProvider.Drme);
        Assert.Equal(0, ReShadeConfigWriter.LerProvedorDoPreset(drme));

        // Chaves globais ANTES da primeira seção, senão o ReShade as lê como parte dela.
        int tech = launchpad.IndexOf("Techniques=", StringComparison.Ordinal);
        int secao = launchpad.IndexOf("[DLSS5_Feed.fx]", StringComparison.Ordinal);
        Assert.True(tech >= 0 && secao > tech);

        // Caminho direto: preset vazio, sem seção.
        var direto = ReShadeConfigWriter.BuildPresetIni(MvProvider.Launchpad, feederUsed: false);
        Assert.DoesNotContain("DLSS5_MV_PROVIDER", direto);
        Assert.Null(ReShadeConfigWriter.LerProvedorDoPreset(direto));
    }

    [Fact]
    public void LeADefinicaoSoDaSecaoDoFeed()
    {
        Assert.Null(ReShadeConfigWriter.LerProvedorDoPreset("Techniques=DLSS5_Feed@DLSS5_Feed.fx\n"));
        Assert.Null(ReShadeConfigWriter.LerProvedorDoPreset("[Outro.fx]\nPreprocessorDefinitions=DLSS5_MV_PROVIDER=3\n"));
        Assert.Equal(3, ReShadeConfigWriter.LerProvedorDoPreset("Techniques=\n\n[DLSS5_Feed.fx]\nPreprocessorDefinitions=OUTRA=1,DLSS5_MV_PROVIDER=3\n"));
        Assert.Null(ReShadeConfigWriter.LerProvedorDoPreset(null));
    }

    [Fact]
    public void Checkpoint13ExigeADefinicaoNoPreset()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-cp13-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "jogo.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
                MvProvider = MvProvider.Launchpad,
            };

            // Preset da versão anterior do programa: ordem certa, sem a definição.
            File.WriteAllText(Path.Combine(dir, "ReShadePreset.ini"),
                "Techniques=MartysMods_Launchpad@MartysMods_LAUNCHPAD.fx,DLSS5_Feed@DLSS5_Feed.fx\n" +
                "TechniqueSorting=MartysMods_Launchpad@MartysMods_LAUNCHPAD.fx,DLSS5_Feed@DLSS5_Feed.fx\n");
            var c13 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 13);
            Assert.Equal(CheckStatus.Fail, c13.State);
            Assert.Contains("DLSS5_MV_PROVIDER", c13.Detail);
            Assert.Contains("Instalar de novo", c13.FixHint!);

            // O preset que esta versão grava passa.
            File.WriteAllText(Path.Combine(dir, "ReShadePreset.ini"), ReShadeConfigWriter.BuildPresetIni(MvProvider.Launchpad));
            c13 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 13);
            Assert.Equal(CheckStatus.Pass, c13.State);

            // Provedor trocado na detecção sem reinstalar: a definição não bate.
            perfil.MvProvider = MvProvider.Drme;
            c13 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 13);
            Assert.Equal(CheckStatus.Fail, c13.State);
            Assert.Contains("pede 0", c13.Detail);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class FeederAntigoNaVerificacaoTests
{
    private const string Log0120 =
        "10:00:00.000  dlss5-feed 0.12.0 (built Sep  1 2026 10:00:00) attached.\n" +
        "10:00:03.000  [feed] DLSS5_MV_PROVIDER=1 (Launchpad) -> MartysMods_Launchpad (enabled)\n" +
        "10:00:04.000  [feed] building: 2560x1440 backbuffer R8G8B8A8_UNORM (mv R16G16_FLOAT, depth R32_FLOAT, depth reversed=1)\n" +
        "10:00:04.100  [feed] feature ready: 2560x1440 DLAA, flags=74 (SDR)\n" +
        "10:00:04.200  [feed] frame 1 delivered (2560x1440, reset=1)\n" +
        "10:00:30.000  [feed] frame 1500 delivered (2560x1440, reset=0)\n" +
        "10:00:31.000  [feed] effect runtime 000000003557D1B0 destroyed\n" +
        "10:00:32.000  [feed] feature re-create crashed (caught); keeping the previous feature\n" +
        "10:00:40.000  [feed] frame 2000 delivered (2560x1440, reset=0)\n";

    [Fact]
    public void LogNovoLeVersaoProvedorERecriacaoSobrevivida()
    {
        var s = FeedLog.Ler(Log0120)!;
        Assert.Equal("0.12.0", s.Versao);
        Assert.Equal("Launchpad -> MartysMods_Launchpad", s.Provedor);
        Assert.Equal("enabled", s.EstadoDoProvedor);
        Assert.True(s.ProvedorOk);
        Assert.True(s.RuntimeRecriado);
        Assert.True(s.ManteveFeatureAntiga);
        Assert.False(s.Travou);
        Assert.False(s.CaiuNaRecriacao);
        Assert.Equal(2000, s.FramesEntregues);

        var desligado = FeedLog.Ler(Log0120.Replace("(enabled)", "(DISABLED)"))!;
        Assert.False(desligado.ProvedorOk);
        Assert.Equal("DISABLED", desligado.EstadoDoProvedor);

        // Log antigo não registra provedor: não se pode acusar nada.
        Assert.True(FeedLog.Ler("dlss5-feed 0.5.0 (built Aug 30 2026 12:38:05) attached.\n[feed] frame 1 delivered\n")!.ProvedorOk);
    }

    [Fact]
    public void Checkpoint15ApontaOFeederAntigoEmVezDaEscadaDeResolucao()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-feedold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // O log real do NFS com o 0.5.0: o remédio é o Feeder novo, não o work_resolution
            // (que o 0.5.0 nem lê).
            File.WriteAllText(Path.Combine(dir, "dlss5-feed.log"),
                "15:38:28.311  dlss5-feed 0.5.0 (built Aug 30 2026 12:38:05) attached.\n" +
                "15:38:34.523  [feed] feature ready: 1280x720 DLAA, flags=74 (SDR)\n" +
                "15:38:35.083  [feed] frame 2 delivered (1280x720, reset=0)\n" +
                "15:38:39.000  [feed] the game recreated its device; rebuilding the session\n" +
                "15:38:39.644  [feed] building: 3840x2160 backbuffer R8G8B8A8_UNORM (mv R16G16_FLOAT, depth R32_FLOAT, depth reversed=1)\n" +
                "15:38:39.703  [feed] CreateFeature raised exception 0xC0000005 (caught; nothing was submitted)\n" +
                "15:38:39.705  ### CRASH RECORDED ###  exception 0xE06D7363 at 00007FFA6DBB3CFA in KERNELBASE.dll; this add-on was last doing: creating the DLSS feature\n");
            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "Mafia.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D11,
            };
            var c15 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 15);
            Assert.Equal(CheckStatus.Fail, c15.State);
            Assert.Contains("Feeder 0.5.0", c15.Detail);
            Assert.Contains(FeederKit.VersaoDoKit, c15.Detail);
            Assert.Contains("recriou device/runtime", c15.Detail);
            Assert.Contains("Instalar de novo", c15.FixHint!);
            Assert.DoesNotContain("Resolução de trabalho", c15.FixHint!);

            // Com o 0.12.0 rodando e o provedor desmarcado: aviso, com a instrução de marcar.
            File.WriteAllText(Path.Combine(dir, "dlss5-feed.log"), Log0120.Replace("(enabled)", "(DISABLED)"));
            c15 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 15);
            Assert.Equal(CheckStatus.Warning, c15.State);
            Assert.Contains("DISABLED", c15.Detail);
            Assert.Contains("ACIMA do DLSS 5 Feed", c15.FixHint!);

            // Saudável.
            File.WriteAllText(Path.Combine(dir, "dlss5-feed.log"), Log0120);
            c15 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 15);
            Assert.Equal(CheckStatus.Pass, c15.State);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class RayReconstructionTests
{
    // Trecho real do ReShade.log do Alan Wake 2: NR "ativo" em todo quadro, imagem igual com F6.
    private const string LogAw2 =
        "17:18:53:317 [34476] | INFO  | Registered add-on \"DLSS 5 Neural Rendering\" v0.2026.828.517 using ReShade API version 18.\n" +
        "17:18:53:318 [34476] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: RenoDX DLSS5 Generic v4.1.5 (build Aug 30 2026 22:25:38) loaded (hotkeys: NR toggle F6, screenshot F5) | EnableHooks=2: NGX hooks only, Streamline modules left unpatched\n" +
        "17:18:53:321 [34476] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: D3D12 NGX hooks installed across 3 module copy(ies); inline DLSS contract capture armed\n" +
        "17:18:56:938 [37352] | INFO  | Redirecting IDXGIFactory::CreateSwapChain(...) ...\n" +
        "17:20:08:575 [37352] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: NGX feature create intercepted: feature=13 (DLSSD/RR), slot=0\n" +
        "17:20:08:660 [37352] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: NGX feature create intercepted: feature=11 (DLSSG/FrameGeneration), slot=0\n" +
        "17:20:08:820 [38888] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: skipping NR on NGX evaluate: feature 11 (DLSSG/FrameGeneration) is not DLSS/DLSSD; frame generation and other NGX features are untouched\n" +
        "17:20:09:303 [37352] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: feature 18 created via the signed snippet after DLSSD/RR for NR input 2560x1440 -> output 2560x1440 with guides 2560x1440\n" +
        "17:20:12:797 [37352] | INFO  | [DLSS 5 Neural Rendering] DLSS5 Generic: inline feature 18 evaluation succeeded (count=60, NR input 2560x1440 (guides 1707x960), output 2560x1440 [native])\n";

    [Fact]
    public void LogDoAlanWake2LeRayReconstructionEFrameGeneration()
    {
        var s = RenodxLog.Ler(LogAw2)!;
        Assert.True(s.Ativo);
        Assert.True(s.RayReconstruction);
        Assert.True(s.FrameGeneration);

        var comum = RenodxLog.Ler(LogAw2.Replace("after DLSSD/RR", "after DLSS").Replace("feature=13 (DLSSD/RR)", "feature=1 (DLSS)"))!;
        Assert.False(comum.RayReconstruction);
    }

    [Fact]
    public void Checkpoint14MandaDesligarORayReconstructionParaComparar()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5-aw2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "ReShade.log"), LogAw2 + new string(' ', 2000));
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");
            var perfil = new GameProfile
            {
                GameFolder = dir,
                RealExePath = Path.Combine(dir, "AlanWake2.exe"),
                Architecture = PeArchitecture.X64,
                Api = GraphicsApi.D3D12,
                HasNativeDlss = true,
            };
            var c14 = CheckpointVerifier.Verify(perfil, null).First(c => c.Number == 14);
            Assert.Equal(CheckStatus.Pass, c14.State);
            Assert.Contains("RAY RECONSTRUCTION", c14.FixHint!);
            Assert.Contains("DLSSD", c14.FixHint!);
            Assert.Contains("GERAÇÃO DE QUADROS", c14.FixHint!);
        }
        finally { Directory.Delete(dir, true); }
    }
}
