namespace Dlss5.Core;

/// <summary>Uma pista encontrada sobre a API gráfica, com o peso que ela vale.</summary>
public sealed record ApiEvidence(GraphicsApi Api, int Weight, string Source);

/// <summary>Resultado da detecção de API, junto com as pistas que levaram até ele.</summary>
public sealed class ApiDetection
{
    public GraphicsApi Api { get; init; } = GraphicsApi.Unknown;
    public int Score { get; init; }
    public GraphicsApi Runner { get; init; } = GraphicsApi.Unknown;
    public int RunnerScore { get; init; }
    public IReadOnlyList<ApiEvidence> Evidence { get; init; } = Array.Empty<ApiEvidence>();

    /// <summary>
    /// Confiante quando há pista forte E folga sobre a segunda colocada. Um jogo que
    /// menciona duas APIs (comum: D3D11 e D3D12 no mesmo binário) cai fora daqui de
    /// propósito — nesse caso o usuário confirma.
    /// </summary>
    public bool Confident => Api != GraphicsApi.Unknown && Score >= 60 && Score - RunnerScore >= 30;

    /// <summary>As pistas mais fortes a favor da API escolhida, para mostrar na tela.</summary>
    public string TopSources(int max = 3) => string.Join("; ", Evidence
        .Where(e => e.Api == Api)
        .OrderByDescending(e => e.Weight)
        .Take(max)
        .Select(e => e.Source));
}

/// <summary>
/// Descobre a API gráfica do jogo sem precisar abrir o jogo (spec 8.1/11.2).
///
/// A tabela de imports do PE só mostra o que o exe linka estaticamente, e muitos jogos
/// carregam o Direct3D com LoadLibrary — por isso aqui se olha também o TEXTO dentro do
/// binário: o nome da DLL e o da função ficam gravados como string mesmo quando a carga
/// é dinâmica. Cada pista soma peso e a API com mais peso vence.
/// </summary>
public static class ApiDetector
{
    private sealed record Marker(GraphicsApi Api, string Text, int Weight);

    // Nome de função pesa mais que nome de DLL: é mais difícil aparecer por acaso.
    private static readonly Marker[] Markers =
    {
        new(GraphicsApi.D3D12, "D3D12CreateDevice", 34),
        new(GraphicsApi.D3D12, "D3D12SerializeVersionedRootSignature", 30),
        new(GraphicsApi.D3D12, "d3d12.dll", 24),
        new(GraphicsApi.D3D12, "D3D12Core.dll", 22),

        new(GraphicsApi.D3D11, "D3D11CreateDeviceAndSwapChain", 34),
        new(GraphicsApi.D3D11, "D3D11CreateDevice", 28),
        new(GraphicsApi.D3D11, "d3d11.dll", 24),

        new(GraphicsApi.D3D10, "D3D10CreateDevice", 30),
        new(GraphicsApi.D3D10, "d3d10.dll", 20),

        new(GraphicsApi.D3D9, "Direct3DCreate9Ex", 34),
        new(GraphicsApi.D3D9, "Direct3DCreate9", 28),
        new(GraphicsApi.D3D9, "d3d9.dll", 24),

        new(GraphicsApi.D3D8, "Direct3DCreate8", 34),
        new(GraphicsApi.D3D8, "d3d8.dll", 24),

        new(GraphicsApi.Vulkan, "vkCreateSwapchainKHR", 34),
        new(GraphicsApi.Vulkan, "vkCreateInstance", 28),
        new(GraphicsApi.Vulkan, "vulkan-1.dll", 24),

        new(GraphicsApi.OpenGL, "wglCreateContext", 30),
        new(GraphicsApi.OpenGL, "wglMakeCurrent", 26),
        new(GraphicsApi.OpenGL, "opengl32.dll", 20),
    };

    /// <summary>Jogos validados na especificação, onde o nome do exe já entrega a API.</summary>
    private static readonly Dictionary<string, GraphicsApi> KnownGames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["re2"] = GraphicsApi.D3D12,
            ["re3"] = GraphicsApi.D3D12,
            ["re4"] = GraphicsApi.D3D12,
            ["re7"] = GraphicsApi.D3D12,
            ["re8"] = GraphicsApi.D3D12,
            ["tombraider"] = GraphicsApi.D3D11,
            ["hl2"] = GraphicsApi.D3D9,
            ["gtaiv"] = GraphicsApi.D3D9,
            // Max Payne 1 é DirectX 8 puro; o 2 já é D3D9.
            ["maxpayne"] = GraphicsApi.D3D8,
            ["maxpayne2"] = GraphicsApi.D3D9,
        };

    private static readonly (string Fragment, GraphicsApi Api)[] FolderHints =
    {
        ("dx12", GraphicsApi.D3D12),
        ("d3d12", GraphicsApi.D3D12),
        ("dx11", GraphicsApi.D3D11),
        ("d3d11", GraphicsApi.D3D11),
        ("vulkan", GraphicsApi.Vulkan),
        ("dx8", GraphicsApi.D3D8),
        ("d3d8", GraphicsApi.D3D8),
    };

    private const int ChunkSize = 4 * 1024 * 1024;
    private const int Overlap = 128;
    private const long ExeScanBudget = 192L * 1024 * 1024;
    private const long DllScanBudget = 64L * 1024 * 1024;

    public static ApiDetection Detect(string? exePath, string exeFolder)
    {
        var scores = new Dictionary<GraphicsApi, int>();
        var evidence = new List<ApiEvidence>();

        void Add(GraphicsApi api, int weight, string source)
        {
            if (api == GraphicsApi.Unknown || weight <= 0) return;
            scores[api] = scores.TryGetValue(api, out var v) ? v + weight : weight;
            evidence.Add(new ApiEvidence(api, weight, source));
        }

        // 1. Imports do PE: estrutural, a pista mais forte quando existe.
        if (exePath is not null)
        {
            foreach (var dll in PeFile.GetImportedDlls(exePath))
            {
                var api = ApiFromDllName(dll);
                if (api != GraphicsApi.Unknown)
                    Add(api, 45, $"import de {dll.ToLowerInvariant()}");
            }
        }

        // 2. Texto dentro do exe: pega o que é carregado dinamicamente.
        if (exePath is not null)
        {
            foreach (var m in MarkersFoundIn(exePath, ExeScanBudget))
                Add(m.Api, m.Weight, $"\"{m.Text}\" no exe");
        }

        // 3. Ainda empatado? O renderizador pode estar numa DLL ao lado do exe.
        if (!IsDecisive(scores))
        {
            foreach (var dll in RendererDllCandidates(exeFolder))
            {
                foreach (var m in MarkersFoundIn(dll, DllScanBudget))
                    Add(m.Api, Math.Max(8, m.Weight / 2), $"\"{m.Text}\" em {Path.GetFileName(dll)}");
                if (IsDecisive(scores)) break;
            }
        }

        // 4. Desempates fracos.
        if (exePath is not null &&
            KnownGames.TryGetValue(Path.GetFileNameWithoutExtension(exePath), out var known))
            Add(known, 35, "jogo conhecido da especificação");

        foreach (var (fragment, api) in FolderHints)
            if (FolderMentions(exeFolder, fragment))
                Add(api, 12, $"arquivo/pasta com \"{fragment}\" no nome");

        var ranked = scores.OrderByDescending(kv => kv.Value).ToList();
        var best = ranked.Count > 0 ? ranked[0] : default;
        var second = ranked.Count > 1 ? ranked[1] : default;

        return new ApiDetection
        {
            Api = ranked.Count > 0 ? best.Key : GraphicsApi.Unknown,
            Score = ranked.Count > 0 ? best.Value : 0,
            Runner = ranked.Count > 1 ? second.Key : GraphicsApi.Unknown,
            RunnerScore = ranked.Count > 1 ? second.Value : 0,
            Evidence = evidence,
        };
    }

    private static bool IsDecisive(Dictionary<GraphicsApi, int> scores)
    {
        if (scores.Count == 0) return false;
        var ranked = scores.OrderByDescending(kv => kv.Value).ToList();
        int top = ranked[0].Value;
        int next = ranked.Count > 1 ? ranked[1].Value : 0;
        return top >= 60 && top - next >= 30;
    }

    private static GraphicsApi ApiFromDllName(string dll)
    {
        if (dll.StartsWith("d3d12", StringComparison.OrdinalIgnoreCase)) return GraphicsApi.D3D12;
        if (dll.StartsWith("d3d11", StringComparison.OrdinalIgnoreCase)) return GraphicsApi.D3D11;
        if (dll.StartsWith("d3d10", StringComparison.OrdinalIgnoreCase)) return GraphicsApi.D3D10;
        if (dll.StartsWith("d3d9", StringComparison.OrdinalIgnoreCase)) return GraphicsApi.D3D9;
        if (dll.StartsWith("d3d8", StringComparison.OrdinalIgnoreCase)) return GraphicsApi.D3D8;
        if (dll.StartsWith("vulkan-1", StringComparison.OrdinalIgnoreCase)) return GraphicsApi.Vulkan;
        if (dll.StartsWith("opengl32", StringComparison.OrdinalIgnoreCase)) return GraphicsApi.OpenGL;
        return GraphicsApi.Unknown;
    }

    private static IEnumerable<Marker> MarkersFoundIn(string path, long budget)
    {
        var hits = ScanForMarkers(path, Markers.Select(m => m.Text).Distinct(StringComparer.Ordinal).ToList(), budget);
        return Markers.Where(m => hits.Contains(m.Text));
    }

    /// <summary>
    /// Procura cada marcador no arquivo, em ASCII e em UTF-16LE, lendo em blocos com
    /// sobreposição para não perder um marcador partido entre dois blocos.
    /// </summary>
    internal static HashSet<string> ScanForMarkers(string path, IReadOnlyCollection<string> needles, long budget)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Dictionary<string, byte[][]>(StringComparer.Ordinal);
        foreach (var n in needles)
            if (!pending.ContainsKey(n)) pending[n] = Variants(n);
        if (pending.Count == 0) return found;

        try
        {
            using var fs = File.OpenRead(path);
            var buffer = new byte[ChunkSize + Overlap];
            int carry = 0;
            long consumed = 0;

            while (consumed < budget && pending.Count > 0)
            {
                int read = fs.Read(buffer, carry, ChunkSize);
                if (read <= 0) break;
                consumed += read;

                int total = carry + read;
                var span = new ReadOnlySpan<byte>(buffer, 0, total);

                foreach (var pair in pending.ToList())
                {
                    foreach (var variant in pair.Value)
                    {
                        if (span.IndexOf(variant) < 0) continue;
                        found.Add(pair.Key);
                        pending.Remove(pair.Key);
                        break;
                    }
                }

                int keep = Math.Min(Overlap, total);
                Buffer.BlockCopy(buffer, total - keep, buffer, 0, keep);
                carry = keep;
            }
        }
        catch
        {
            // Arquivo bloqueado ou ilegível: simplesmente não rende pistas.
        }
        return found;
    }

    /// <summary>Bytes do marcador em ASCII e UTF-16LE, no caso original e em minúsculas.</summary>
    private static byte[][] Variants(string needle)
    {
        var forms = new List<string> { needle };
        var lower = needle.ToLowerInvariant();
        if (!string.Equals(lower, needle, StringComparison.Ordinal)) forms.Add(lower);

        var list = new List<byte[]>(forms.Count * 2);
        foreach (var form in forms)
        {
            var ascii = new byte[form.Length];
            var wide = new byte[form.Length * 2];
            for (int i = 0; i < form.Length; i++)
            {
                ascii[i] = (byte)form[i];
                wide[i * 2] = (byte)form[i];
            }
            list.Add(ascii);
            list.Add(wide);
        }
        return list.ToArray();
    }

    /// <summary>DLLs ao lado do exe, das maiores para as menores (o renderizador costuma ser grande).</summary>
    private static List<string> RendererDllCandidates(string exeFolder)
    {
        try
        {
            return new DirectoryInfo(exeFolder)
                .EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly)
                .Where(f => f.Length > 4096 && f.Length < 128L * 1024 * 1024)
                .OrderByDescending(f => f.Length)
                .Take(16)
                .Select(f => f.FullName)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static bool FolderMentions(string folder, string fragment)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(folder)
                .Any(e => Path.GetFileName(e).Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
