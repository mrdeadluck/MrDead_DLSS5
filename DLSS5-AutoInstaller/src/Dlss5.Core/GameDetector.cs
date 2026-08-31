namespace Dlss5.Core;

public sealed record ExeCandidate(string Path, PeArchitecture Arch, long Size, int Score);

public sealed class DetectionResult
{
    public required GameProfile Profile { get; init; }
    public required IReadOnlyList<ExeCandidate> Candidates { get; init; }
    public List<string> Notes { get; } = new();
}

/// <summary>
/// Detecção automática do perfil do jogo (spec 5 e 8.1/8.2):
/// executável real, arquitetura pelo cabeçalho PE, API por imports/DLLs presentes,
/// engine Source, DLSS nativo e pasta do renderizador.
/// </summary>
public static class GameDetector
{
    private static readonly string[] LauncherNameHints =
    {
        "launcher", "play", "setup", "unins", "install", "crash", "report",
        "redist", "dxsetup", "vcredist", "eac", "activation", "config", "settings",
    };

    private static readonly string[] JunkFolderHints =
    {
        "_commonredist", "redist", "directx", "vcredist", "support", "dotnet",
        "easyanticheat", "battleye", "engine\\extras",
    };

    public static DetectionResult Detect(string gameFolder)
    {
        var profile = new GameProfile { GameFolder = gameFolder };
        var result = new DetectionResult
        {
            Profile = profile,
            Candidates = FindExeCandidates(gameFolder),
        };

        var best = result.Candidates.OrderByDescending(c => c.Score).FirstOrDefault();
        if (best is null)
        {
            result.Notes.Add("Nenhum executável encontrado na pasta.");
            return result;
        }

        profile.RealExePath = best.Path;
        profile.Architecture = best.Arch;

        // Launcher informativo: um candidato com cara de launcher diferente do escolhido.
        profile.LauncherExePath = result.Candidates
            .Where(c => !string.Equals(c.Path, best.Path, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(c => LauncherNameHints.Any(h =>
                Path.GetFileNameWithoutExtension(c.Path).Contains(h, StringComparison.OrdinalIgnoreCase)))
            ?.Path;

        DetectApiAndRenderer(profile, result.Notes);
        DetectNativeDlss(profile, result.Notes);
        return result;
    }

    private static List<ExeCandidate> FindExeCandidates(string gameFolder)
    {
        var list = new List<ExeCandidate>();
        IEnumerable<string> exes;
        try
        {
            exes = Directory.EnumerateFiles(gameFolder, "*.exe", SearchOption.AllDirectories);
        }
        catch
        {
            return list;
        }

        foreach (var exe in exes)
        {
            var arch = PeFile.GetArchitecture(exe);
            if (arch == PeArchitecture.Unknown) continue;

            long size;
            try { size = new FileInfo(exe).Length; } catch { continue; }

            var rel = Path.GetRelativePath(gameFolder, exe);
            var name = Path.GetFileNameWithoutExtension(exe);
            int depth = rel.Count(ch => ch is '\\' or '/');

            int score = 100;
            score -= depth * 10;
            // Tamanho como proxy de "exe principal" (bônus logarítmico).
            score += (int)Math.Min(40, Math.Log2(Math.Max(1, size / 1024)));

            if (LauncherNameHints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase)))
                score -= 80;
            if (JunkFolderHints.Any(h => rel.Replace('/', '\\')
                    .Contains(h, StringComparison.OrdinalIgnoreCase)))
                score -= 120;

            // Stub da Source: exe pequeno na raiz + bin\shaderapi*.dll — é o alvo certo.
            var exeDir = Path.GetDirectoryName(exe)!;
            if (size < 300 * 1024 && HasSourceRendererDll(Path.Combine(exeDir, "bin")))
                score += 70;

            list.Add(new ExeCandidate(exe, arch, size, score));
        }
        return list;
    }

    private static bool HasSourceRendererDll(string binDir)
    {
        try
        {
            return Directory.Exists(binDir) &&
                   Directory.EnumerateFiles(binDir, "shaderapi*.dll").Any();
        }
        catch
        {
            return false;
        }
    }

    private static void DetectApiAndRenderer(GameProfile profile, List<string> notes)
    {
        var exeDir = profile.ExeFolder;
        var binDir = Path.Combine(exeDir, "bin");

        // Engine Source: renderizador em bin\ (spec 8.9).
        if (HasSourceRendererDll(binDir))
        {
            profile.IsSourceEngine = true;
            profile.RendererFolder = binDir;
            profile.Api = GraphicsApi.D3D9;
            notes.Add("Engine Source detectada (bin\\shaderapi*.dll): D3D9 forçado com -dxlevel 95; dgVoodoo vai em bin\\.");
            return;
        }

        profile.RendererFolder = exeDir;

        var imports = profile.RealExePath is null
            ? Array.Empty<string>().AsEnumerable().ToList()
            : PeFile.GetImportedDlls(profile.RealExePath).ToList();

        bool ImportsAny(params string[] names) =>
            imports.Any(i => names.Any(n => i.StartsWith(n, StringComparison.OrdinalIgnoreCase)));

        if (ImportsAny("d3d12")) profile.Api = GraphicsApi.D3D12;
        else if (ImportsAny("d3d11")) profile.Api = GraphicsApi.D3D11;
        else if (ImportsAny("d3d10")) profile.Api = GraphicsApi.D3D10;
        else if (ImportsAny("d3d9")) profile.Api = GraphicsApi.D3D9;
        else if (ImportsAny("vulkan-1")) profile.Api = GraphicsApi.Vulkan;
        else if (ImportsAny("dxgi")) profile.Api = GraphicsApi.D3D11; // dxgi sem d3dNN: D3D11+ é o palpite
        else if (ImportsAny("opengl32")) profile.Api = GraphicsApi.OpenGL;

        if (profile.Api == GraphicsApi.Unknown)
        {
            notes.Add("API gráfica não detectada pelos imports (muitos jogos carregam Direct3D dinamicamente). " +
                      "Confirme a API manualmente.");
            // Palpite razoável por arquitetura, que o usuário pode corrigir.
            profile.Api = profile.Architecture == PeArchitecture.X64 ? GraphicsApi.D3D11 : GraphicsApi.D3D9;
        }
        else
        {
            notes.Add($"API sugerida pelos imports do exe: {profile.Api}. Confirme se o jogo pode usar outra.");
        }
    }

    private static void DetectNativeDlss(GameProfile profile, List<string> notes)
    {
        var exeDir = profile.ExeFolder;
        bool Has(string pattern)
        {
            try { return Directory.EnumerateFiles(exeDir, pattern, SearchOption.TopDirectoryOnly).Any(); }
            catch { return false; }
        }

        if (Has("sl.dlss*.dll") || Has("sl.interposer.dll") || Has("nvngx_dlssg.dll") || Has("nvngx_dlss.dll"))
        {
            profile.HasNativeDlss = true;
            notes.Add("DLSS nativo detectado (DLLs nvngx/Streamline na pasta do jogo): o Feeder não é necessário.");
        }
    }
}
