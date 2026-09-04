namespace Dlss5.Core;

/// <summary>Inventário dos arquivos do kit DLSS 5 resolvidos a partir de uma pasta raiz.</summary>
public sealed class KitInventory
{
    public required string KitRoot { get; init; }

    // Peças principais (spec 3.1–3.3)
    public string? NvngxDlssnr { get; set; }
    public string? NvngxDlss { get; set; }
    public string? RenodxAddon64 { get; set; }
    /// <summary>renodx-dlss.addon64 do ShortFuse (motor alternativo, passadas múltiplas).</summary>
    public string? RenodxDlssShortFuse { get; set; }
    public string? FeedAddon64 { get; set; }
    public string? FeedAddon32 { get; set; }
    public string? FeedHost64Exe { get; set; }

    /// <summary>dxgi.dll do ReShade já extraído, por arquitetura.</summary>
    public string? DxgiX64 { get; set; }
    public string? DxgiX86 { get; set; }

    /// <summary>Instalador (ou zip) do ReShade para extrair ReShade32/64.dll.</summary>
    public string? ReShadeSetup { get; set; }

    /// <summary>dinput8.dll do REFramework (x64) e o marcador de revisão da nightly.</summary>
    public string? ReFrameworkDinput8 { get; set; }
    public string? ReFrameworkRevision { get; set; }


    /// <summary>Pasta reshade-shaders completa (Shaders + Textures).</summary>
    public string? ShadersDir { get; set; }

    // dgVoodoo2 (spec 3.5)
    public string? DgVoodooD3D9X86 { get; set; }
    public string? DgVoodooD3D8X86 { get; set; }
    public string? DgVoodooConf { get; set; }
    public string? DgVoodooCpl { get; set; }

    public bool HasDrme { get; set; }
    public bool HasLaunchpad { get; set; }
    public bool HasVort { get; set; }
    public bool HasLumenite { get; set; }

    /// <summary>Algum provedor de motion vectors utilizável (o DRME não conta: não compila no 6.8).</summary>
    public bool HasAnyMvProvider => HasVort || HasLaunchpad || HasLumenite;
    public bool HasDisplayDepth { get; set; }

    public List<string> Problems { get; } = new();

    /// <summary>
    /// Impressão digital do kit: tamanho das peças principais. Serve para saber se o mod
    /// que está no jogo é o mesmo que está na pasta do kit (atualização pendente).
    /// Tamanho, e não hash: o nvngx_dlssnr.dll tem 158 MB e isto roda a cada abertura.
    /// </summary>
    public string Fingerprint()
    {
        static string T(string? p)
        {
            try { return p is not null && File.Exists(p) ? new FileInfo(p).Length.ToString() : "-"; }
            catch { return "?"; }
        }
        return $"dlssnr={T(NvngxDlssnr)};dlss={T(NvngxDlss)};renodx={T(RenodxAddon64)};sf={T(RenodxDlssShortFuse)};feed64={T(FeedAddon64)};feed32={T(FeedAddon32)};host={T(FeedHost64Exe)};dxgi64={T(DxgiX64)};dxgi32={T(DxgiX86)}";
    }

    /// <summary>Valida o inventário para uma rota específica; devolve o que falta.</summary>
    public IReadOnlyList<string> MissingFor(
        InstallRoute route, bool nativeDlss, GraphicsApi api = GraphicsApi.Unknown, bool shortFuse = false)
    {
        var missing = new List<string>();
        void Need(string? path, string what)
        {
            if (path is null) missing.Add(what);
        }

        if (shortFuse)
        {
            // Motor ShortFuse (64-bit): um addon só, sem Feeder e sem shader de vetores.
            Need(NvngxDlssnr, "nvngx_dlssnr.dll (x64, ~158 MB)");
            Need(NvngxDlss, "nvngx_dlss.dll (x64, ~56 MB)");
            Need(RenodxDlssShortFuse, ShortFuseDlss.Addon + " (ShortFuse) — no kit fica em \"renodx-dlss-SF-... (alternativa ShortFuse)\"");
            if (DxgiX64 is null && ReShadeSetup is null)
                missing.Add("dxgi.dll x64 do ReShade (ou o instalador ReShade_Setup para extrair)");
            return missing;
        }

        Need(NvngxDlssnr, "nvngx_dlssnr.dll (x64, ~158 MB)");
        Need(NvngxDlss, "nvngx_dlss.dll (x64, ~56 MB)");
        Need(RenodxAddon64, "renodx-dlss5.addon64");
        Need(ShadersDir, "pasta reshade-shaders com DLSS5_Feed.fx");
        if (!HasAnyMvProvider)
            missing.Add("um provedor de motion vectors (vort_Motion.fx, MartysMods_LAUNCHPAD.fx ou lumenite_Kernel.fx)");

        if (route == InstallRoute.A)
        {
            if (!nativeDlss) Need(FeedAddon64, "dlss5-feed.addon64");
            if (DxgiX64 is null && ReShadeSetup is null)
                missing.Add("dxgi.dll x64 do ReShade (ou o instalador ReShade_Setup para extrair)");
        }
        else if (route is InstallRoute.B or InstallRoute.C)
        {
            Need(FeedAddon32, "dlss5-feed.addon32");
            Need(FeedHost64Exe, "host64\\dlss5-feed-host64.exe");
            if (DxgiX86 is null && ReShadeSetup is null)
                missing.Add("dxgi.dll x86 do ReShade (ou o instalador ReShade_Setup para extrair)");
            if (DxgiX64 is null && ReShadeSetup is null)
                missing.Add("dxgi.dll x64 do ReShade para host64\\ (ou o instalador ReShade_Setup)");
        }

        if (route == InstallRoute.C)
        {
            // Cada API tem o seu wrapper: o jogo D3D8 nunca carrega um D3D9.dll.
            if (api == GraphicsApi.D3D8)
                Need(DgVoodooD3D8X86, "dgVoodoo2: MS\\x86\\D3D8.dll");
            else
                Need(DgVoodooD3D9X86, "dgVoodoo2: MS\\x86\\D3D9.dll");
            Need(DgVoodooConf, "dgVoodoo2: dgVoodoo.conf");
        }

        return missing;
    }
}

/// <summary>
/// Varre a pasta do kit ("DLSS 5 Files" ou equivalente) e localiza cada peça
/// pelo nome + arquitetura, tolerando qualquer organização de subpastas.
/// </summary>
public static class KitResolver
{
    /// <summary>Um arquivo baixado como ponteiro do Git LFS em vez do conteúdo real.</summary>
    public static bool IsLfsPointer(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Length is 0 or > 1024) return false;
            using var sr = new StreamReader(path);
            var first = sr.ReadLine();
            return first is not null && first.StartsWith("version https://git-lfs", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static KitInventory Resolve(string kitRoot)
    {
        var inv = new KitInventory { KitRoot = kitRoot };
        if (!Directory.Exists(kitRoot))
        {
            inv.Problems.Add($"Pasta do kit não existe: {kitRoot}");
            return inv;
        }

        List<string> all;
        try
        {
            all = Directory.EnumerateFiles(kitRoot, "*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex)
        {
            inv.Problems.Add($"Falha ao varrer a pasta do kit: {ex.Message}");
            return inv;
        }

        var pointers = new List<string>();
        // Candidato válido = existe e não é ponteiro LFS.
        bool Ok(string p)
        {
            if (!IsLfsPointer(p)) return true;
            pointers.Add(p);
            return false;
        }

        IEnumerable<string> Named(string fileName) =>
            all.Where(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));

        // Evita variantes arm64 do dgVoodoo e afins.
        static bool LooksArm(string p) => p.Contains("arm64", StringComparison.OrdinalIgnoreCase);

        string? First(string fileName, Func<string, bool>? extra = null) =>
            Named(fileName).Where(Ok).Where(p => extra?.Invoke(p) ?? true).FirstOrDefault();

        inv.NvngxDlssnr = First("nvngx_dlssnr.dll");
        inv.NvngxDlss = First("nvngx_dlss.dll");
        inv.RenodxAddon64 = First("renodx-dlss5.addon64");
        inv.RenodxDlssShortFuse = First(ShortFuseDlss.Addon);
        inv.FeedAddon64 = First("dlss5-feed.addon64");
        inv.FeedAddon32 = First("dlss5-feed.addon32");
        inv.FeedHost64Exe = First("dlss5-feed-host64.exe");

        // REFramework: só o x64 serve, e ele nunca pode ser confundido com um dinput8.dll
        // que por acaso esteja em outra pasta do kit.
        inv.ReFrameworkDinput8 = Named(ReFramework.Dinput8)
            .Where(Ok)
            .FirstOrDefault(p => PeFile.GetArchitecture(p) == PeArchitecture.X64);
        inv.ReFrameworkRevision = First(ReFramework.RevisionFile);

        // dxgi.dll do ReShade: classifica por arquitetura lendo o cabeçalho PE.
        foreach (var dxgi in Named("dxgi.dll").Where(Ok))
        {
            switch (PeFile.GetArchitecture(dxgi))
            {
                case PeArchitecture.X64:
                    inv.DxgiX64 ??= dxgi;
                    break;
                case PeArchitecture.X86:
                    inv.DxgiX86 ??= dxgi;
                    break;
            }
        }

        // Instalador do ReShade (preferir o .exe, menor; o .zip serve de fallback).
        inv.ReShadeSetup =
            all.Where(p => Path.GetFileName(p).StartsWith("ReShade_Setup", StringComparison.OrdinalIgnoreCase)
                        && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
               .Where(Ok).FirstOrDefault()
            ?? all.Where(p => Path.GetFileName(p).StartsWith("ReShade_Setup", StringComparison.OrdinalIgnoreCase)
                           && p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                  .Where(Ok).FirstOrDefault();

        // Pasta reshade-shaders: precisa conter Shaders\DLSS5_Feed.fx.
        // Se houver mais de uma, prefere a mais completa (com os dois provedores de MV).
        var shaderRoots = Named("DLSS5_Feed.fx")
            .Where(Ok)
            .Select(p => Path.GetDirectoryName(Path.GetDirectoryName(p)))
            .Where(d => d is not null
                     && string.Equals(Path.GetFileName(d), "reshade-shaders", StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        static int ShaderScore(string root)
        {
            int score = 0;
            var shaders = Path.Combine(root, "Shaders");
            if (File.Exists(Path.Combine(shaders, "MotionEstimation.fx"))) score += 1;
            if (File.Exists(Path.Combine(shaders, "MartysMods_LAUNCHPAD.fx"))) score += 2;
            if (File.Exists(Path.Combine(shaders, "vort_Motion.fx"))) score += 2;
            if (File.Exists(Path.Combine(shaders, "lumenite_Kernel.fx"))) score += 2;
            if (File.Exists(Path.Combine(shaders, "ReShade.fxh"))) score += 1;
            if (Directory.Exists(Path.Combine(root, "Textures"))) score += 1;
            return score;
        }

        inv.ShadersDir = shaderRoots.OrderByDescending(ShaderScore).FirstOrDefault();
        if (inv.ShadersDir is not null)
        {
            var shaders = Path.Combine(inv.ShadersDir, "Shaders");
            inv.HasDrme = File.Exists(Path.Combine(shaders, "MotionEstimation.fx"));
            inv.HasLaunchpad = File.Exists(Path.Combine(shaders, "MartysMods_LAUNCHPAD.fx"));
            inv.HasVort = File.Exists(Path.Combine(shaders, "vort_Motion.fx"));
            inv.HasLumenite = File.Exists(Path.Combine(shaders, "lumenite_Kernel.fx"));
            inv.HasDisplayDepth = File.Exists(Path.Combine(shaders, "DisplayDepth.fx"));
        }

        // dgVoodoo2: o wrapper da API dentro de MS\x86 (nunca 3Dfx, nunca arm) e com
        // arch x86 real. O pacote traz um arquivo por API — D3D8.dll atende os jogos de
        // DirectX 8, que são maioria entre 2001 e 2003.
        string? WrapperDgVoodoo(string nome) => Named(nome)
            .Where(Ok)
            .Where(p => !LooksArm(p))
            .Where(p =>
            {
                var dir = Path.GetDirectoryName(p) ?? "";
                var parent = Path.GetDirectoryName(dir) ?? "";
                return string.Equals(Path.GetFileName(dir), "x86", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFileName(parent), "MS", StringComparison.OrdinalIgnoreCase);
            })
            .FirstOrDefault(p => PeFile.GetArchitecture(p) == PeArchitecture.X86);

        inv.DgVoodooD3D9X86 = WrapperDgVoodoo("D3D9.dll");
        inv.DgVoodooD3D8X86 = WrapperDgVoodoo("D3D8.dll");

        var wrapperParaRaiz = inv.DgVoodooD3D9X86 ?? inv.DgVoodooD3D8X86;
        if (wrapperParaRaiz is not null)
        {
            // conf e Cpl ficam na raiz do pacote dgVoodoo (dois níveis acima de MS\x86).
            var dgRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(wrapperParaRaiz)));
            if (dgRoot is not null)
            {
                var conf = Path.Combine(dgRoot, "dgVoodoo.conf");
                var cpl = Path.Combine(dgRoot, "dgVoodooCpl.exe");
                if (File.Exists(conf) && Ok(conf)) inv.DgVoodooConf = conf;
                if (File.Exists(cpl) && Ok(cpl)) inv.DgVoodooCpl = cpl;
            }
        }
        // Fallback: qualquer dgVoodoo.conf no kit.
        inv.DgVoodooConf ??= First("dgVoodoo.conf");
        inv.DgVoodooCpl ??= First("dgVoodooCpl.exe", p => !LooksArm(p));

        if (pointers.Count > 0)
        {
            inv.Problems.Add(
                $"{pointers.Count} arquivo(s) do kit são ponteiros do Git LFS, não o conteúdo real. " +
                "Baixe o repositório com o Git LFS instalado (GitHub Desktop faz isso sozinho) " +
                "ou aponte o programa para a pasta original no seu PC. Exemplo: " +
                Path.GetFileName(pointers[0]));
        }

        return inv;
    }
}
