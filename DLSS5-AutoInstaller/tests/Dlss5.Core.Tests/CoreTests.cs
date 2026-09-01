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
        var p = new GameProfile
        {
            GameFolder = @"C:\game",
            RealExePath = @"C:\game\Binaries\Win32\g.exe",
        };
        Assert.Equal(@"C:\game\Binaries\Win32", p.ExeFolder);
    }
}

public class PlanBuilderTests
{
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

    private static GameProfile Profile(PeArchitecture arch, GraphicsApi api) => new()
    {
        GameFolder = @"C:\game",
        RealExePath = @"C:\game\g.exe",
        Architecture = arch,
        Api = api,
        RendererFolder = @"C:\game",
    };

    private static bool Targets(InstallPlan plan, string relative) =>
        plan.Actions.Any(a => a.TargetPath?.EndsWith(relative, StringComparison.OrdinalIgnoreCase) == true);

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
        // O aviso deixou de mandar desligar o DLSS (ele pode ficar ligado) e passou a
        // dizer que o kit não sobrescreve o DLSS do jogo.
        Assert.Contains(plan.Warnings, w => w.Contains("NÃO sobrescreve", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeDlss_InD3D12_SkipsTheFeeder()
    {
        // O caminho direto agora é opt-in: em 30+ jogos o Feeder funcionou de forma
        // visível, e o direto disse "ok" no log com a tela inalterada (Onimusha, GTA 5).

        var profile = Profile(PeArchitecture.X64, GraphicsApi.D3D12);
        profile.HasNativeDlss = true;
        profile.PreferirCaminhoDireto = true;

        Assert.False(profile.NeedsFeeder);
        Assert.True(profile.UsesRenodxDirectPath);

        var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());
        Assert.False(Targets(plan, "dlss5-feed.addon64"));
        Assert.True(Targets(plan, "renodx-dlss5.addon64"));
    }

    [Fact]
    public void D3D12ComDlssNativo_PorPadraoAindaUsaOFeeder()
    {
        // A decisão é empírica: 30+ jogos funcionando pelo Feeder contra zero sucesso
        // visível do caminho direto. Sem pedido explícito, o Feeder entra sempre.
        var profile = Profile(PeArchitecture.X64, GraphicsApi.D3D12);
        profile.HasNativeDlss = true;

        Assert.False(profile.UsesRenodxDirectPath);
        Assert.True(profile.NeedsFeeder);

        var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());
        Assert.True(Targets(plan, @"\dlss5-feed.addon64"));
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
        profile.PreferirCaminhoDireto = true;
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
        profile.RendererFolder = @"C:\game\bin";   // variante Source
        var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());

        Assert.True(Targets(plan, @"bin\D3D9.dll"));
        Assert.Contains(plan.Actions, a => a.Kind == PlanActionKind.PatchDgVoodooConf
                                        && a.TargetPath!.EndsWith(@"bin\dgVoodoo.conf", StringComparison.OrdinalIgnoreCase));
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
            File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "renodx-dlss5.addon64"), "x");
            File.WriteAllText(Path.Combine(dir, "forzahorizon6.exe"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "reshade-shaders"));

            var sobras = new InstallerEngine(_ => { }).ConferirSobras(dir);

            Assert.Contains(sobras, f => f.EndsWith("dxgi.dll", StringComparison.Ordinal));
            Assert.Contains(sobras, f => f.EndsWith("renodx-dlss5.addon64", StringComparison.Ordinal));
            Assert.Contains(sobras, f => f.Contains("reshade-shaders", StringComparison.Ordinal));

            // O executável do jogo não é nosso e não pode entrar na lista.
            Assert.DoesNotContain(sobras, f => f.EndsWith("forzahorizon6.exe", StringComparison.Ordinal));
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
            var sobras = engine.Revert(manifesto, removeRegistryOverride: false);

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

            Assert.Equal(CheckStatus.Manual, c3.State);
            Assert.Contains("como você preferir", c3.Title);
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
        PreferirCaminhoDireto = true,   // o caminho direto virou opt-in
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
            perfil.PreferirCaminhoDireto = false;
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
