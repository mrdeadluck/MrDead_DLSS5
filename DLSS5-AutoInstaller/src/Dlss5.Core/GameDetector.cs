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
    /// <summary>
    /// Exes conhecidos que dividem a pasta com o jogo e NÃO são o jogo: modos online,
    /// editores, ferramentas. Sem esta lista o tamanho decide, e ele mente.
    /// </summary>
    private static readonly string[] SecondaryExeNames =
    {
        "mgsvmgo",   // Metal Gear Online, ao lado do mgsvtpp.exe
    };

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

    /// <summary>Executáveis auxiliares que a Unreal instala junto e nunca renderizam.</summary>
    private static readonly string[] HelperExeNames =
    {
        "crashreportclient", "unrealcefsubprocess", "epicwebhelper", "unrealeditor",
        "unrealpak", "bootstrappackagedgame",
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

        var bestRel = Path.GetRelativePath(gameFolder, best.Path).Replace('/', '\\');
        if (bestRel.Contains("\\Binaries\\Win", StringComparison.OrdinalIgnoreCase))
            result.Notes.Add($"Unreal Engine detectada: o alvo é {bestRel}, não o exe da raiz " +
                             "(esse é só um atalho). Tudo vai para a pasta do binário real.");

        DetectApiAndRenderer(profile, result.Notes);
        DetectNativeDlss(profile, result.Notes);

        // Só o jogo que recusa o ReShade na abertura sai com o REFramework marcado. Nos
        // outros da RE Engine ele é a causa do crash, não o remédio (Dragon's Dogma 2).
        profile.UsarReFramework = ReFramework.PrecisaDoBypass(profile.RealExePath);
        if (profile.UsarReFramework)
            result.Notes.Add("Este jogo recusa a DLL do ReShade na abertura: o REFramework entra junto para " +
                             "desarmar a checagem (caixa marcada).");
        else if (profile.EhReEngine)
            result.Notes.Add("RE Engine: este jogo aceita o ReShade direto. " + ReFramework.QuandoMarcar);

        // Jogo que recusa o dxgi.dll já sai da detecção com o nome que ele aceita. Isto
        // ficava na tela e chegava tarde: cada atribuição de controle dispara o
        // sincronismo do formulário, que gravava o nome do combo no perfil ANTES — e a
        // preferência, aplicada depois com "??=", virava no-op. No MGS V o resultado era
        // o instalador continuar em dxgi.dll, justamente o nome que impede o jogo de abrir.
        profile.NomeDoReShadeEscolhido =
            GameProfile.NomeDeReShadePreferido(profile.RealExePath, profile.Api);
        if (profile.NomeDoReShadeEscolhido is { } nome)
            result.Notes.Add($"Este jogo recusa o dxgi.dll (proteção anti-adulteração): o ReShade entra como {nome}.");
        if (EaJavelin.EhJavelin(profile.ExeFolder))
            result.Notes.Add(EaJavelin.Aviso + " " + EaJavelin.ComoAbrir);
        else if (File.Exists(Path.Combine(profile.GameFolder, "__Installer", "installerdata.xml")))
            result.Notes.Add("Jogo do EA App (__Installer\\installerdata.xml): a sobreposição do EA App costuma pegar o " +
                             "DXGI antes do ReShade — jogo abre, Home não faz nada, ReShade.log nem nasce. Desligue-a " +
                             "no EA App (Configurações → Sobreposição no jogo) antes de abrir. Se mesmo assim não houver " +
                             "log, troque \"ReShade entra como\" para o outro nome (d3d11.dll) e instale de novo.");

        // Easy Anti-Cheat (Gears of War Reloaded): a DLL do ReShade não carrega sob ele e
        // o jogo fecha com "does not support Direct3D 12". Dito aqui, antes de instalar.
        if (EasyAntiCheat.Encontrar(profile.GameFolder, profile.ExeFolder) is { } eac)
            result.Notes.Add(EasyAntiCheat.Nota(eac, profile.GameFolder));

        if (MotorFox.EhFoxEngine(profile.RealExePath))
        {
            result.Notes.Add(MotorFox.Aviso);
            var exeFox = profile.RealExePath!;
            result.Notes.Add(MotorFox.PatchAplicado(exeFox)
                ? "Patch anti-hook já aplicado no exe: a instalação está liberada."
                : !MotorFox.PatcherCobre(exeFox) ? MotorFox.SemPatcherParaGz
                : MotorFox.EstadoDoExe(exeFox) == EstadoDoExeFox.Original
                    ? "mgsvtpp.exe 1.0.15.4 Steam inglês reconhecido: a instalação aplica o patch anti-hook sozinha."
                    : MotorFox.ExeNaoCoberto(exeFox));
        }

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

            // Unreal Engine: o exe da raiz é só um atalho que relança o binário real em
            // <Jogo>\Binaries\Win64\<Nome>-Shipping.exe. Sem reconhecer isso, a instalação
            // para ao lado do atalho — numa pasta onde o processo que renderiza nunca procura,
            // e o ReShade.log sequer chega a existir.
            var relLower = "\\" + rel.Replace('/', '\\').ToLowerInvariant();
            if (name.EndsWith("-shipping", StringComparison.OrdinalIgnoreCase))
                score += 150;
            if (relLower.Contains("\\binaries\\win64\\") || relLower.Contains("\\binaries\\win32\\"))
                score += 60;
            // Engine\Binaries guarda os auxiliares da própria engine, nunca o jogo.
            if (relLower.Contains("\\engine\\binaries\\"))
                score -= 140;
            if (HelperExeNames.Any(h => name.Equals(h, StringComparison.OrdinalIgnoreCase)))
                score -= 200;
            // Executável secundário que mora na MESMA pasta do jogo e é maior ou igual a ele:
            // o mgsvmgo.exe (Metal Gear Online) ao lado do mgsvtpp.exe. Escolhido, ele levava
            // a instalação para o jogo errado sem ninguém perceber — o plano nem pedia o patch
            // da Fox Engine, porque o nome não era o do Phantom Pain.
            if (SecondaryExeNames.Any(h => name.Equals(h, StringComparison.OrdinalIgnoreCase)))
                score -= 150;

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

        // Engine da Respawn (Titanfall 2): exe na raiz, renderizador em bin\x64_retail.
        // A DLL do ReShade na raiz nunca carrega; em bin\x64_retail carrega.
        var respawn = Path.Combine(exeDir, "bin", "x64_retail");
        if (File.Exists(Path.Combine(respawn, "materialsystem_dx11.dll")))
        {
            profile.RendererFolder = respawn;
            notes.Add("Renderizador em bin\\x64_retail (materialsystem_dx11.dll — engine do Titanfall 2): o " +
                      "dxgi.dll do ReShade vai LÁ. Na raiz ele nunca carrega: o jogo abre, o Home não faz nada e " +
                      "o ReShade.log nem nasce. ReShade.ini, addons e shaders ficam na raiz, ao lado do exe.");
        }
        else
        {
            profile.RendererFolder = exeDir;
        }

        var detection = ApiDetector.Detect(profile.RealExePath, exeDir);
        profile.ApiDetection = detection;

        if (detection.ExeOpaco)
            notes.Add("O executável não mostra pista nenhuma de API (nem import, nem string, nem NGX): é a cara de " +
                      "exe cifrado ou empacotado (Arxan, Denuvo, stub da Steam). A detecção fica com o que há ao " +
                      "lado dele e com os logs da última execução — as DLLs da NVIDIA (nvngx_*.dll) e os proxies " +
                      "não contam, porque citam todas as APIs.");

        if (detection.Api != GraphicsApi.Unknown)
        {
            profile.Api = detection.Api;
            notes.Add(detection.Confident
                ? $"API detectada: {detection.Api} — {detection.TopSources()}."
                : $"API provável: {detection.Api} — {detection.TopSources()}. " +
                  "A evidência não é conclusiva; confirme se você sabe que o jogo usa outra.");
        }
        else
        {
            profile.Api = profile.Architecture == PeArchitecture.X64
                ? GraphicsApi.D3D11
                : GraphicsApi.D3D9;
            notes.Add($"Nenhuma pista de API no executável nem nas DLLs ao lado. Assumi {profile.Api} " +
                      "pela arquitetura — confirme antes de instalar.");
        }

        // Tira o peso da escolha quando ela não muda nada (rota A cobre as duas).
        if (profile.Architecture == PeArchitecture.X64 &&
            profile.Api is GraphicsApi.D3D11 or GraphicsApi.D3D12)
        {
            notes.Add("Em 64-bit, D3D11 e D3D12 caem na mesma rota A: se a escolha entre essas duas " +
                      "estiver trocada, os arquivos instalados são exatamente os mesmos.");
        }
    }

    private static void DetectNativeDlss(GameProfile profile, List<string> notes)
    {
        // O manifesto de uma instalação anterior entra aqui para o detector saber
        // distinguir o nvngx_dlss.dll do jogo do nvngx_dlss.dll que nós mesmos copiamos.
        var anterior = InstallManifest.Find(profile.GameFolder, profile.ExeFolder);
        var deteccao = NativeDlssDetector.Detect(
            profile.GameFolder, profile.ExeFolder, profile.RealExePath, anterior);

        profile.NativeDlss = deteccao;
        profile.HasNativeDlss = deteccao.Present;
        profile.NativeDlssOverridden = false;

        notes.Add("DLSS nativo do jogo: " + deteccao.Resumo + ".");
        if (deteccao.Present && profile.Api != GraphicsApi.D3D12)
            notes.Add("Como o jogo não é D3D12, o Feeder entra assim mesmo (o RenoDX só enxerga NGX em D3D12). " +
                      "Nas opções do jogo, deixe o DLSS/upscaling DESLIGADO.");
    }
}
