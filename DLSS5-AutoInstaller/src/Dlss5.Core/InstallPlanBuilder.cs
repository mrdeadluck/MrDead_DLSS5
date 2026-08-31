namespace Dlss5.Core;

public sealed class InstallPlan
{
    public required GameProfile Profile { get; init; }
    public required InstallOptions Options { get; init; }
    public List<PlanAction> Actions { get; } = new();
    public List<string> Blockers { get; } = new();

    /// <summary>Coisas que não impedem instalar, mas que o usuário precisa saber.</summary>
    public List<string> Warnings { get; } = new();

    public bool CanRun => Blockers.Count == 0 && Actions.Count > 0;
}

/// <summary>
/// Deriva a lista de cópias e arquivos gerados a partir do perfil (spec 6 e 11.4).
/// Não toca em disco — só descreve o que será feito.
/// </summary>
public static class InstallPlanBuilder
{
    public static InstallPlan Build(GameProfile profile, KitInventory kit, InstallOptions options)
    {
        var plan = new InstallPlan { Profile = profile, Options = options };
        var route = profile.Route;

        if (route == InstallRoute.Unsupported)
        {
            plan.Blockers.Add(DescribeUnsupported(profile));
            return plan;
        }

        var missing = kit.MissingFor(route, profile.UsesRenodxDirectPath);
        if (missing.Count > 0)
        {
            foreach (var m in missing)
                plan.Blockers.Add("Falta no kit: " + m);
        }
        foreach (var p in kit.Problems)
            plan.Blockers.Add(p);

        var exe = profile.ExeFolder;
        var host64 = Path.Combine(exe, "host64");
        var shadersTarget = Path.Combine(exe, "reshade-shaders");

        void Copy(string? src, string dstFolder, string dstName)
        {
            if (src is null) return;
            var dst = Path.Combine(dstFolder, dstName);
            plan.Actions.Add(new PlanAction(PlanActionKind.CopyFile,
                $"Copiar {dstName} → {Rel(profile, dst)}", src, dst));
        }

        // Limpeza dos proibidos primeiro (spec 3.7 / prólogo).
        if (options.CleanForbidden)
        {
            foreach (var f in ForbiddenFiles.FindPresent(exe))
                plan.Actions.Add(new PlanAction(PlanActionKind.DeleteForbiddenFile,
                    $"Remover arquivo proibido {Path.GetFileName(f)}", null, f));
        }

        // ReShade (dxgi.dll) na pasta do exe, arquitetura = exe.
        var dxgiArch = profile.Architecture;
        var dxgiSrc = dxgiArch == PeArchitecture.X64 ? kit.DxgiX64 : kit.DxgiX86;
        if (dxgiSrc is not null)
        {
            Copy(dxgiSrc, exe, "dxgi.dll");
        }
        else if (kit.ReShadeSetup is not null)
        {
            plan.Actions.Add(new PlanAction(PlanActionKind.ExtractReShadeDll,
                $"Extrair ReShade ({dxgiArch}) do instalador → {Rel(profile, Path.Combine(exe, "dxgi.dll"))}",
                kit.ReShadeSetup, Path.Combine(exe, "dxgi.dll")));
        }

        // ReShade.ini + preset gerados.
        plan.Actions.Add(new PlanAction(PlanActionKind.WriteGeneratedFile,
            "Gerar ReShade.ini", null, Path.Combine(exe, "ReShade.ini")));
        plan.Actions.Add(new PlanAction(PlanActionKind.WriteGeneratedFile,
            profile.NeedsFeeder
                ? $"Gerar ReShadePreset.ini (MV = {options.MvProvider}, acima do DLSS 5 Feed)"
                : "Gerar ReShadePreset.ini (sem efeitos: com DLSS nativo em D3D12 o RenoDX se pendura na chamada do próprio jogo)",
            null, Path.Combine(exe, "ReShadePreset.ini")));

        // Pasta de shaders.
        if (kit.ShadersDir is not null)
            plan.Actions.Add(new PlanAction(PlanActionKind.CopyFile,
                $"Copiar pasta reshade-shaders → {Rel(profile, shadersTarget)}",
                kit.ShadersDir, shadersTarget));

        if (route == InstallRoute.A)
        {
            // 64-bit: tudo na pasta do exe.
            if (profile.NeedsFeeder)
                Copy(kit.FeedAddon64, exe, "dlss5-feed.addon64");
            Copy(kit.RenodxAddon64, exe, "renodx-dlss5.addon64");
            Copy(kit.NvngxDlssnr, exe, "nvngx_dlssnr.dll");
            Copy(kit.NvngxDlss, exe, "nvngx_dlss.dll");
        }
        else
        {
            // 32-bit (B/C): addon32 na raiz; o resto do Feeder dentro de host64\.
            Copy(kit.FeedAddon32, exe, "dlss5-feed.addon32");
            Copy(kit.FeedHost64Exe, host64, "dlss5-feed-host64.exe");
            Copy(kit.DxgiX64, host64, "dxgi.dll");
            Copy(kit.RenodxAddon64, host64, "renodx-dlss5.addon64");
            Copy(kit.NvngxDlssnr, host64, "nvngx_dlssnr.dll");
            Copy(kit.NvngxDlss, host64, "nvngx_dlss.dll");
        }

        // Rota C: dgVoodoo na pasta do renderizador (exe ou bin\ no Source).
        if (route == InstallRoute.C)
        {
            var renderer = profile.RendererFolder ?? exe;
            Copy(kit.DgVoodooD3D9X86, renderer, "D3D9.dll");
            Copy(kit.DgVoodooCpl, renderer, "dgVoodooCpl.exe");
            if (kit.DgVoodooConf is not null)
                plan.Actions.Add(new PlanAction(PlanActionKind.PatchDgVoodooConf,
                    $"Copiar e ajustar dgVoodoo.conf → {Rel(profile, Path.Combine(renderer, "dgVoodoo.conf"))}",
                    kit.DgVoodooConf, Path.Combine(renderer, "dgVoodoo.conf")));
        }

        // As sl.*.dll e a nvngx_dlssg.dll são do jogo, não do kit. O programa não encosta
        // nelas — só avisa, porque elas explicam de onde vem o DLSS nativo.
        var doJogo = ForbiddenFiles.FindGameOwned(exe);
        if (doJogo.Count > 0)
        {
            plan.Warnings.Add(
                "A pasta tem arquivos de DLSS do próprio jogo (" + string.Join(", ", doJogo) + "). " +
                "Eles são mantidos: é por eles que o jogo chega ao DLSS, e removê-los faria as opções " +
                "de DLSS sumirem do menu.");
        }

        if (profile.HasNativeDlss && !profile.UsesRenodxDirectPath)
        {
            plan.Warnings.Add(
                $"O jogo tem DLSS nativo, mas roda em {profile.Api} — e o RenoDX só enxerga NGX em D3D12. " +
                "Sozinho ele ficaria em \"HOOKS ARMED / NO DLSS CREATE SEEN\", sem nunca aplicar nada. " +
                "Por isso o Feeder (dlss5-feed.addon64) entra mesmo assim: ele roda o NGX num device D3D12 " +
                "próprio. Nas opções do jogo, DESLIGUE o DLSS/upscaling — o Feeder faz DLAA (resolução de " +
                "render = saída), então os dois juntos só atrapalham.");
        }

        if (profile.Api == GraphicsApi.Vulkan && profile.Architecture == PeArchitecture.X64)
        {
            plan.Warnings.Add(
                "Vulkan: o ReShade não entra como dxgi.dll — ele é um layer global, instalado pelo " +
                "instalador oficial do ReShade (é preciso marcar o jogo lá). O dxgi.dll copiado aqui " +
                "fica sem uso; os addons, os shaders e o ReShade.ini com AddonPath continuam corretos.");
        }

        // Override de assinatura no registro.
        if (options.ApplyRegistryOverride)
            plan.Actions.Add(new PlanAction(PlanActionKind.RegistryOverride,
                "Aplicar override de assinatura NGX no registro (HKLM, 3 chaves)", null, null));

        return plan;
    }

    private static string DescribeUnsupported(GameProfile p)
    {
        if (p.Architecture == PeArchitecture.X86 && p.Api == GraphicsApi.Vulkan)
            return "Jogo 32-bit em Vulkan não é suportado (o addon32 exige Direct3D 11). " +
                   "Se o jogo também oferecer D3D9, troque a API para D3D9 (rota C).";
        if (p.Api == GraphicsApi.D3D10)
            return "Direct3D 10 não é suportado por este fluxo.";
        if (p.Api == GraphicsApi.OpenGL)
            return "OpenGL não é suportado: o Feeder precisa de D3D11/D3D12 (ou Vulkan em 64-bit).";
        if (p.Architecture == PeArchitecture.Unknown)
            return "Arquitetura do executável não identificada. Selecione o exe real do jogo.";
        return "Combinação de arquitetura/API sem caminho suportado.";
    }

    private static string Rel(GameProfile p, string full)
    {
        try { return Path.GetRelativePath(p.GameFolder, full); }
        catch { return full; }
    }
}
