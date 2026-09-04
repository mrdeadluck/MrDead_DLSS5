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
    /// O exe não rendeu pista NENHUMA: nem import, nem string de API, nem família de NGX,
    /// nem export do Agility. É a cara de executável cifrado ou empacotado (Arxan, Denuvo,
    /// stub da Steam) — o Gears of War Reloaded tem 25 MB e não mostra nada. Nesse caso a
    /// detecção só tem o que há ao lado do exe, e isso precisa ser dito.
    /// </summary>
    public bool ExeOpaco { get; init; }

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

    /// <summary>
    /// Jogos de uma API só cujo exe não mostra string nenhuma (cifrado), reconhecidos pelo
    /// prefixo do nome. Peso de pista forte: aqui o nome é a única prova que existe.
    /// Gears of War Reloaded/Ultimate (GOWDE-Steam.exe, GOWDE-*.exe) é D3D12 e nada mais —
    /// ele mesmo diz "does not support Direct3D 12" quando não consegue criar o device.
    /// </summary>
    private static readonly (string Prefixo, GraphicsApi Api, string Motivo)[] KnownPrefixes =
    {
        ("GOWDE", GraphicsApi.D3D12, "Gears of War Reloaded/Ultimate (GOWDE-*.exe) é só D3D12 — o exe é cifrado e não mostra as strings"),
    };
    private const int KnownPrefixWeight = 70;

    /// <summary>
    /// DLLs ao lado do exe que NÃO são o renderizador do jogo e citam todas as APIs: as da
    /// NVIDIA (o nvngx_dlss.dll fala em vulkan-1.dll porque o DLSS também roda em Vulkan —
    /// foi o que fez o Gears Reloaded sair como "Vulkan"), Streamline, XeSS, FidelityFX,
    /// o compilador de shaders, e os proxies (o dxgi.dll do próprio ReShade, o dinput8.dll
    /// do REFramework) que carregam ganchos para tudo. Olhar dentro delas só confunde.
    /// </summary>
    private static readonly string[] DllsQueNaoRenderizam =
    {
        "nvngx", "nvapi", "nvlowlatency", "nvpmodel", "sl.", "gfsdk_", "libxess", "xess",
        "amd_fidelityfx", "ffx_", "amd_ags", "d3dcompiler", "dxcompiler", "dxil",
        "steam_api", "eossdk", "easyanticheat", "galaxy", "reshade",
    };
    private static readonly string[] ProxiesConhecidos =
    {
        "dxgi.dll", "d3d12.dll", "d3d11.dll", "d3d10.dll", "d3d9.dll", "d3d8.dll", "opengl32.dll",
        "vulkan-1.dll", "dinput8.dll", "version.dll", "winmm.dll", "xinput1_3.dll",
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

    /// <summary>
    /// O DLSS do próprio jogo entrega a API: quem integra NGX em D3D11 renderiza em D3D11.
    /// O Crysis Remastered ensinou — o exe carrega o renderizador Vulkan da CryEngine inteiro
    /// (import de vulkan-1.dll, vkCreateSwapchainKHR, vkCreateInstance) e a detecção dizia
    /// Vulkan, enquanto o jogo roda em D3D11 e o DLSS dele é NVSDK_NGX_D3D11. Só conta quando
    /// há UMA família de NGX no exe: com duas (RDR2: D3D12 e Vulkan) a pista não decide.
    /// </summary>
    private static readonly (string Text, GraphicsApi Api)[] NgxFamilies =
    {
        ("NVSDK_NGX_D3D11_Init", GraphicsApi.D3D11),
        ("NVSDK_NGX_D3D12_Init", GraphicsApi.D3D12),
        ("NVSDK_NGX_VULKAN_Init", GraphicsApi.Vulkan),
    };
    private const int NgxFamilyWeight = 80;

    /// <summary>
    /// O que a última execução deixou nos logs vale mais do que qualquer string no exe: o
    /// jogo rodou com AQUELA API nesta máquina. Do ReShade.log ficam de fora o D3D12 (o
    /// Feeder cria um device D3D12 privado e o ReShade sonda D3D12CreateDevice ao subir,
    /// então "Redirecting D3D12CreateDevice" aparece em jogo D3D11 também) e o
    /// D3D11CreateDevice sem swapchain (muito jogo sonda adaptadores assim). O
    /// dlss5-feed.log diz o transporte que o Feeder abriu, e esse é inequívoco.
    /// </summary>
    private static readonly (string Text, GraphicsApi Api, int Weight, string Source)[] LogMarkers =
    {
        ("Redirecting vkCreateSwapchainKHR", GraphicsApi.Vulkan, 70, "o ReShade.log da última execução viu a swapchain Vulkan"),
        ("Redirecting D3D11CreateDeviceAndSwapChain", GraphicsApi.D3D11, 70, "o ReShade.log da última execução viu D3D11CreateDeviceAndSwapChain"),
        ("Redirecting D3D11CreateDevice(", GraphicsApi.D3D11, 30, "o ReShade.log da última execução viu D3D11CreateDevice (pode ser só sondagem)"),
        ("Redirecting Direct3DCreate9", GraphicsApi.D3D9, 70, "o ReShade.log da última execução viu Direct3DCreate9"),
        ("Redirecting Direct3DCreate8", GraphicsApi.D3D8, 70, "o ReShade.log da última execução viu Direct3DCreate8"),
        ("Redirecting D3D10CreateDevice", GraphicsApi.D3D10, 70, "o ReShade.log da última execução viu D3D10CreateDevice"),
        ("Redirecting wglCreateContext", GraphicsApi.OpenGL, 70, "o ReShade.log da última execução viu wglCreateContext"),
    };

    /// <summary>
    /// D3D12CreateDevice no ReShade.log só conta quando o Feeder não está na pasta (nem o
    /// addon nem o log dele): aí não há device D3D12 privado e a chamada é do jogo. É o
    /// caminho direto, o único em que o Feeder não deixa o próprio rastro.
    /// </summary>
    private static readonly (string Text, GraphicsApi Api, int Weight, string Source) MarcadorD3D12SemFeeder =
        ("Redirecting D3D12CreateDevice", GraphicsApi.D3D12, 60,
            "o ReShade.log da última execução viu D3D12CreateDevice (sem Feeder na pasta, a chamada é do jogo)");
    private static readonly (string Text, GraphicsApi Api, int Weight, string Source)[] FeedLogMarkers =
    {
        ("opening same-device D3D12 session", GraphicsApi.D3D12, 70, "o dlss5-feed.log da última execução abriu sessão D3D12 no device do jogo"),
        ("Vulkan transport", GraphicsApi.Vulkan, 70, "o dlss5-feed.log da última execução usou o transporte Vulkan"),
        ("fence11=", GraphicsApi.D3D11, 70, "o dlss5-feed.log da última execução abriu a ponte D3D11→D3D12"),
    };

    private const int ChunkSize = 4 * 1024 * 1024;
    private const int Overlap = 128;
    private const long ExeScanBudget = 192L * 1024 * 1024;
    private const long DllScanBudget = 64L * 1024 * 1024;

    public static ApiDetection Detect(string? exePath, string exeFolder)
    {
        var scores = new Dictionary<GraphicsApi, int>();
        var evidence = new List<ApiEvidence>();
        bool exeDeuPista = false;

        void Add(GraphicsApi api, int weight, string source)
        {
            if (api == GraphicsApi.Unknown || weight <= 0) return;
            scores[api] = scores.TryGetValue(api, out var v) ? v + weight : weight;
            evidence.Add(new ApiEvidence(api, weight, source));
        }

        // 0. Logs da última execução nesta pasta: o jogo rodou com aquela API de fato.
        foreach (var (text, api, weight, source) in LogEvidence(exeFolder))
            Add(api, weight, source);

        // 1. Imports do PE: estrutural, a pista mais forte quando existe.
        if (exePath is not null)
        {
            foreach (var dll in PeFile.GetImportedDlls(exePath))
            {
                var api = ApiFromDllName(dll);
                if (api != GraphicsApi.Unknown)
                {
                    Add(api, 45, $"import de {dll.ToLowerInvariant()}");
                    exeDeuPista = true;
                }
            }

            // Exe que exporta D3D12SDKVersion carrega o Agility SDK: só jogo D3D12 faz isso.
            // A tabela de exports fica legível mesmo em exe cifrado (o loader precisa dela).
            if (PeFile.GetExportedNames(exePath).Any(n => n.Equals("D3D12SDKVersion", StringComparison.Ordinal)))
            {
                Add(GraphicsApi.D3D12, 70, "o exe exporta D3D12SDKVersion (Agility SDK): só jogo D3D12 faz isso");
                exeDeuPista = true;
            }
        }

        // 2. Texto dentro do exe: pega o que é carregado dinamicamente.
        if (exePath is not null)
        {
            foreach (var m in MarkersFoundIn(exePath, ExeScanBudget))
            {
                Add(m.Api, m.Weight, $"\"{m.Text}\" no exe");
                exeDeuPista = true;
            }
        }

        // 2b. A família de NGX do DLSS nativo, quando há uma só.
        if (exePath is not null)
        {
            var ngx = ScanForMarkers(exePath, NgxFamilies.Select(f => f.Text).ToList(), ExeScanBudget);
            var familias = NgxFamilies.Where(f => ngx.Contains(f.Text)).ToList();
            if (familias.Count == 1)
                Add(familias[0].Api, NgxFamilyWeight,
                    $"o DLSS do próprio jogo é {familias[0].Text.Replace("_Init", "")}: quem integra NGX nessa API renderiza nela");
            if (familias.Count > 0) exeDeuPista = true;
        }

        // 2c. Agility SDK ao lado do exe (D3D12\D3D12Core.dll): só jogo D3D12 carrega isso.
        if (AgilityAoLado(exeFolder) is { } agility)
            Add(GraphicsApi.D3D12, 40, $"{agility} (Agility SDK do D3D12) ao lado do exe");

        // 2d. Jogo de uma API só cujo exe não mostra nada (Gears Reloaded).
        if (exePath is not null)
        {
            var nome = Path.GetFileNameWithoutExtension(exePath);
            foreach (var (prefixo, api, motivo) in KnownPrefixes)
                if (nome.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
                    Add(api, KnownPrefixWeight, motivo);
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
            ExeOpaco = exePath is not null && File.Exists(exePath) && !exeDeuPista,
        };
    }

    /// <summary>O D3D12Core.dll do Agility SDK, na pasta D3D12\ ou solto ao lado do exe.</summary>
    private static string? AgilityAoLado(string exeFolder)
    {
        try
        {
            if (File.Exists(Path.Combine(exeFolder, "D3D12", "D3D12Core.dll"))) return "D3D12\\D3D12Core.dll";
            if (File.Exists(Path.Combine(exeFolder, "D3D12Core.dll"))) return "D3D12Core.dll";
        }
        catch { }
        return null;
    }

    /// <summary>Pistas dos logs que a última execução deixou ao lado do exe.</summary>
    public static IEnumerable<(string Text, GraphicsApi Api, int Weight, string Source)> LogEvidence(string exeFolder)
    {
        var reshade = LerLog(Path.Combine(exeFolder, "ReShade.log"));
        var feed = LerLog(Path.Combine(exeFolder, "dlss5-feed.log"));
        bool feederNaPasta = feed is not null || File.Exists(Path.Combine(exeFolder, FeederKit.Addon64));
        if (reshade is not null)
        {
            bool comSwapchain = reshade.Contains("D3D11CreateDeviceAndSwapChain", StringComparison.OrdinalIgnoreCase);
            foreach (var m in LogMarkers)
            {
                // O device D3D11 sem swapchain só conta quando a versão com swapchain não contou.
                if (m.Text.EndsWith("D3D11CreateDevice(", StringComparison.Ordinal) && comSwapchain) continue;
                if (reshade.Contains(m.Text, StringComparison.OrdinalIgnoreCase)) yield return m;
            }
            if (!feederNaPasta && reshade.Contains(MarcadorD3D12SemFeeder.Text, StringComparison.OrdinalIgnoreCase))
                yield return MarcadorD3D12SemFeeder;
        }
        if (feed is not null)
        {
            foreach (var m in FeedLogMarkers)
                if (feed.Contains(m.Text, StringComparison.OrdinalIgnoreCase)) yield return m;
        }
    }

    private static string? LerLog(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch
        {
            return null;
        }
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
                .Where(f => !EhDllQueNaoRenderiza(f.Name))
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

    /// <summary>DLL de fornecedor ou proxy: cita todas as APIs sem renderizar nenhuma.</summary>
    public static bool EhDllQueNaoRenderiza(string nome)
    {
        if (ProxiesConhecidos.Any(p => p.Equals(nome, StringComparison.OrdinalIgnoreCase))) return true;
        return DllsQueNaoRenderizam.Any(p => nome.StartsWith(p, StringComparison.OrdinalIgnoreCase));
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
