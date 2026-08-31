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

        [DirectXExt]
        dgVoodooWatermark                   = false
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
    // Regra derivada da spec: 32-bit em Vulkan não tem caminho.
    [InlineData(PeArchitecture.X86, GraphicsApi.Vulkan, InstallRoute.Unsupported)]
    [InlineData(PeArchitecture.X64, GraphicsApi.D3D10, InstallRoute.Unsupported)]
    [InlineData(PeArchitecture.X64, GraphicsApi.OpenGL, InstallRoute.Unsupported)]
    [InlineData(PeArchitecture.X86, GraphicsApi.D3D10, InstallRoute.Unsupported)]
    [InlineData(PeArchitecture.Unknown, GraphicsApi.D3D11, InstallRoute.Unsupported)]
    public void Route_FollowsDecisionTree(PeArchitecture arch, GraphicsApi api, InstallRoute expected)
    {
        Assert.Equal(expected, Profile(arch, api).Route);
    }

    [Fact]
    public void NeedsDgVoodoo_OnlyOnRouteC()
    {
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
        profile.RendererFolder = @"C:\game\bin";   // variante Source
        var plan = InstallPlanBuilder.Build(profile, FullKit(), new InstallOptions());

        Assert.True(Targets(plan, @"bin\D3D9.dll"));
        Assert.Contains(plan.Actions, a => a.Kind == PlanActionKind.PatchDgVoodooConf
                                        && a.TargetPath!.EndsWith(@"bin\dgVoodoo.conf", StringComparison.OrdinalIgnoreCase));
        // ReShade continua na pasta do EXE, nunca em bin\.
        Assert.True(Targets(plan, @"game\dxgi.dll"));
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

public class ForbiddenFilesTests
{
    [Fact]
    public void FindPresent_DetectsStreamlineInterposer()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5game_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "sl.interposer.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "nvngx_dlssg.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "jogo.exe"), "x");

            var found = ForbiddenFiles.FindPresent(dir).Select(Path.GetFileName).ToList();

            Assert.Contains("sl.interposer.dll", found);
            Assert.Contains("nvngx_dlssg.dll", found);
            Assert.DoesNotContain("jogo.exe", found);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
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
