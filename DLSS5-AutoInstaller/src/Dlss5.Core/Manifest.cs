using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dlss5.Core;

/// <summary>Em que ponto do ciclo a instalação registrada está.</summary>
public static class StatusDoManifesto
{
    public const string Concluida = "Concluida";
    public const string InstalacaoEmAndamento = "InstalacaoEmAndamento";
    public const string InstalacaoIncompleta = "InstalacaoIncompleta";
    public const string ReversaoEmAndamento = "ReversaoEmAndamento";
    public const string ReversaoIncompleta = "ReversaoIncompleta";
}

/// <summary>
/// Registro do que a instalação criou/alterou, para poder reverter e reparar (spec 13).
///
/// Versão 2: além das listas de arquivos, guarda a versão do programa e do kit, as
/// opções escolhidas, a impressão digital (tamanho + SHA-256) de tudo que foi gravado e
/// dos originais guardados em backup, e o status da operação — gravado ANTES da
/// primeira modificação e atualizado a cada passo, para que um fechamento no meio deixe
/// rastro do que já foi feito. Manifestos da versão 1 continuam sendo lidos.
/// </summary>
public sealed class InstallManifest
{
    public const string VersaoAtual = "2";
    public const string FileName = "dlss5-autoinstaller-manifest.json";

    public string Version { get; set; } = VersaoAtual;
    public string? AppVersion { get; set; }
    public string? OperationId { get; set; }
    public string Status { get; set; } = StatusDoManifesto.Concluida;
    public DateTime InstalledUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }

    // Jogo
    public string GameFolder { get; set; } = "";
    public string ExeFolder { get; set; } = "";
    public string? RealExePath { get; set; }
    public string? RendererFolder { get; set; }
    public string Route { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string Api { get; set; } = "";
    public bool HasNativeDlss { get; set; }
    public bool IsSourceEngine { get; set; }
    public long? GameExeSize { get; set; }
    public DateTime? GameExeModifiedUtc { get; set; }

    // Kit
    public string? KitRoot { get; set; }
    /// <summary>Impressão digital das peças principais do kit (para saber se o mod mudou).</summary>
    public string? KitVersion { get; set; }

    // Opções aplicadas (para "Atualizar ou reconfigurar" e "Reparar" sem perguntar de novo)
    public string MvProvider { get; set; } = "";
    public int OverlayKey { get; set; } = ReShadeConfigWriter.KeyHome;
    public bool OverlayCtrl { get; set; }
    public bool OverlayShift { get; set; }
    public bool OverlayAlt { get; set; }
    public bool ApplyRegistryOverride { get; set; } = true;
    public bool DgVoodooWatermark { get; set; } = true;
    public bool PreferirCaminhoDireto { get; set; }

    // Registro
    public bool RegistryOverrideApplied { get; set; }
    public DateTime? RegistryOverrideAppliedUtc { get; set; }

    /// <summary>Arquivos criados por nós (apagar ao reverter). Ordem de criação.</summary>
    public List<string> AddedFiles { get; set; } = new();

    /// <summary>Pastas criadas por nós (apagar se vazias ao reverter).</summary>
    public List<string> AddedDirectories { get; set; } = new();

    /// <summary>Arquivos originais que sobrescrevemos: caminho → backup (.dlss5bak).</summary>
    public Dictionary<string, string> BackedUpFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Arquivos proibidos removidos: caminho original → backup.</summary>
    public Dictionary<string, string> RemovedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Impressão digital do que NÓS gravamos, por caminho (adicionados e sobrescritos).</summary>
    public Dictionary<string, FileRecord> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Impressão digital do conteúdo ORIGINAL guardado em backup, por caminho original.</summary>
    public Dictionary<string, FileRecord> Backups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string CaminhoEm(string folder) => Path.Combine(folder, FileName);

    [JsonIgnore]
    public string Caminho => CaminhoEm(ExeFolder);

    [JsonIgnore]
    public bool EhVersaoAntiga => !string.Equals(Version, VersaoAtual, StringComparison.Ordinal);

    /// <summary>Todos os caminhos que a instalação gravou (adicionados + sobrescritos).</summary>
    [JsonIgnore]
    public IEnumerable<string> ArquivosGravados =>
        AddedFiles.Concat(BackedUpFiles.Keys).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Grava de forma atômica: escreve num temporário e move por cima. Um fechamento no
    /// meio da gravação nunca deixa um manifesto truncado.
    /// </summary>
    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        UpdatedUtc = DateTime.UtcNow;
        var destino = CaminhoEm(folder);
        var temp = destino + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOpts));
        File.Move(temp, destino, overwrite: true);
    }

    public void Save() => Save(ExeFolder);

    /// <summary>
    /// Procura o manifesto na pasta do exe e, se não achar, em qualquer subpasta do jogo.
    /// A instalação pode ter sido feita apontando outro executável (jogo Unreal, launcher),
    /// e aí o manifesto não está onde a detecção de agora aponta.
    /// </summary>
    public static InstallManifest? Find(string? gameFolder, string? exeFolder) =>
        Find(gameFolder, exeFolder, out _);

    public static InstallManifest? Find(string? gameFolder, string? exeFolder, out string? corrompidoEm)
    {
        corrompidoEm = null;
        if (!string.IsNullOrWhiteSpace(exeFolder))
        {
            var direto = Load(exeFolder, out var corrompido);
            if (direto is not null) return direto;
            if (corrompido) corrompidoEm = CaminhoEm(exeFolder);
        }
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder)) return null;
        try
        {
            foreach (var achado in Directory.EnumerateFiles(gameFolder, FileName, SearchOption.AllDirectories))
            {
                var m = Load(Path.GetDirectoryName(achado)!, out var corrompido);
                if (m is not null) return m;
                if (corrompido) corrompidoEm ??= achado;
            }
        }
        catch
        {
            // pasta sem permissão de leitura: segue sem manifesto
        }
        return null;
    }

    public static InstallManifest? Load(string folder) => Load(folder, out _);

    /// <summary>Lê o manifesto; <paramref name="corrompido"/> distingue "não existe" de "existe mas não dá para ler".</summary>
    public static InstallManifest? Load(string folder, out bool corrompido)
    {
        corrompido = false;
        var path = CaminhoEm(folder);
        if (!File.Exists(path)) return null;
        try
        {
            var m = JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path));
            if (m is null) { corrompido = true; return null; }
            m.Normalizar(folder);
            return m;
        }
        catch
        {
            corrompido = true;
            return null;
        }
    }

    /// <summary>
    /// Dicionários voltam do JSON com comparador sensível a maiúsculas; caminhos do
    /// Windows não são. Também preenche o que um manifesto antigo não tinha.
    /// </summary>
    public void Normalizar(string? pastaOndeEstava = null)
    {
        BackedUpFiles = new Dictionary<string, string>(BackedUpFiles, StringComparer.OrdinalIgnoreCase);
        RemovedFiles = new Dictionary<string, string>(RemovedFiles, StringComparer.OrdinalIgnoreCase);
        Files = new Dictionary<string, FileRecord>(Files, StringComparer.OrdinalIgnoreCase);
        Backups = new Dictionary<string, FileRecord>(Backups, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(Status)) Status = StatusDoManifesto.Concluida;
        if (string.IsNullOrWhiteSpace(ExeFolder) && pastaOndeEstava is not null) ExeFolder = pastaOndeEstava;
        if (string.IsNullOrWhiteSpace(GameFolder)) GameFolder = ExeFolder;
    }

    /// <summary>Registra um arquivo gravado por nós (com impressão digital).</summary>
    public void RegistrarGravado(string path)
    {
        if (!BackedUpFiles.ContainsKey(path) &&
            !AddedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            AddedFiles.Add(path);
        try { Files[path] = FileRecord.Capturar(path); }
        catch { Files.Remove(path); }
    }

    /// <summary>O arquivo no disco ainda é exatamente o que este manifesto diz ter gravado?</summary>
    public ConferenciaDeArquivo ConferirGravado(string path)
    {
        if (Files.TryGetValue(path, out var rec)) return rec.Conferir(path);
        // Manifesto antigo (sem hash): só dá para saber se existe.
        return File.Exists(path) ? ConferenciaDeArquivo.Igual : ConferenciaDeArquivo.Ausente;
    }

    /// <summary>O backup guardado ainda é íntegro?</summary>
    public ConferenciaDeArquivo ConferirBackup(string original)
    {
        if (!BackedUpFiles.TryGetValue(original, out var backup)) return ConferenciaDeArquivo.Ausente;
        if (Backups.TryGetValue(original, out var rec)) return rec.Conferir(backup);
        if (!File.Exists(backup)) return ConferenciaDeArquivo.Ausente;
        try { return new FileInfo(backup).Length > 0 ? ConferenciaDeArquivo.Igual : ConferenciaDeArquivo.Diferente; }
        catch { return ConferenciaDeArquivo.Ilegivel; }
    }

    /// <summary>Opções de instalação como o usuário deixou, para reconfigurar/reparar sem perguntar de novo.</summary>
    public InstallOptions OpcoesGravadas()
    {
        var o = new InstallOptions
        {
            OverlayKey = OverlayKey,
            OverlayCtrl = OverlayCtrl,
            OverlayShift = OverlayShift,
            OverlayAlt = OverlayAlt,
            ApplyRegistryOverride = ApplyRegistryOverride,
            DgVoodooWatermark = DgVoodooWatermark,
        };
        if (Enum.TryParse<MvProvider>(MvProvider, out var mv)) o.MvProvider = mv;
        return o;
    }

    /// <summary>Perfil do jogo como estava quando a instalação foi feita.</summary>
    public GameProfile? PerfilGravado()
    {
        if (string.IsNullOrWhiteSpace(GameFolder)) return null;
        var p = new GameProfile
        {
            GameFolder = GameFolder,
            RealExePath = RealExePath,
            RendererFolder = RendererFolder,
            HasNativeDlss = HasNativeDlss,
            IsSourceEngine = IsSourceEngine,
            PreferirCaminhoDireto = PreferirCaminhoDireto,
        };
        if (Enum.TryParse<PeArchitecture>(Architecture, out var arch)) p.Architecture = arch;
        if (Enum.TryParse<GraphicsApi>(Api, out var api)) p.Api = api;
        if (Enum.TryParse<MvProvider>(MvProvider, out var mv)) p.MvProvider = mv;
        return p;
    }

    /// <summary>Cria o manifesto de uma nova operação a partir do plano.</summary>
    public static InstallManifest Para(InstallPlan plan, KitInventory kit)
    {
        var p = plan.Profile;
        var o = plan.Options;
        var m = new InstallManifest
        {
            AppVersion = AppInfo.Versao,
            OperationId = Guid.NewGuid().ToString("N")[..12],
            Status = StatusDoManifesto.InstalacaoEmAndamento,
            GameFolder = p.GameFolder,
            ExeFolder = p.ExeFolder,
            RealExePath = p.RealExePath,
            RendererFolder = p.RendererFolder,
            Route = p.Route.ToString(),
            Architecture = p.Architecture.ToString(),
            Api = p.Api.ToString(),
            HasNativeDlss = p.HasNativeDlss,
            IsSourceEngine = p.IsSourceEngine,
            PreferirCaminhoDireto = p.PreferirCaminhoDireto,
            KitRoot = kit.KitRoot,
            KitVersion = kit.Fingerprint(),
            MvProvider = o.MvProvider.ToString(),
            OverlayKey = o.OverlayKey,
            OverlayCtrl = o.OverlayCtrl,
            OverlayShift = o.OverlayShift,
            OverlayAlt = o.OverlayAlt,
            ApplyRegistryOverride = o.ApplyRegistryOverride,
            DgVoodooWatermark = o.DgVoodooWatermark,
        };
        try
        {
            if (p.RealExePath is not null && File.Exists(p.RealExePath))
            {
                var fi = new FileInfo(p.RealExePath);
                m.GameExeSize = fi.Length;
                m.GameExeModifiedUtc = fi.LastWriteTimeUtc;
            }
        }
        catch { /* informativo */ }
        return m;
    }

    /// <summary>
    /// Traz de um manifesto anterior o que continua valendo: backups dos originais (que
    /// NUNCA são refeitos por cima), arquivos proibidos removidos, pastas criadas e o
    /// override do registro. É o que impede uma reinstalação de destruir o original.
    /// </summary>
    public void HerdarDe(InstallManifest anterior)
    {
        foreach (var (original, backup) in anterior.BackedUpFiles)
        {
            if (!File.Exists(backup)) continue;
            BackedUpFiles[original] = backup;
            if (anterior.Backups.TryGetValue(original, out var rec)) Backups[original] = rec;
        }
        foreach (var (original, backup) in anterior.RemovedFiles)
            if (File.Exists(backup)) RemovedFiles[original] = backup;
        foreach (var dir in anterior.AddedDirectories)
            if (Directory.Exists(dir) && !AddedDirectories.Contains(dir, StringComparer.OrdinalIgnoreCase))
                AddedDirectories.Add(dir);
        if (anterior.RegistryOverrideApplied)
        {
            RegistryOverrideApplied = true;
            RegistryOverrideAppliedUtc = anterior.RegistryOverrideAppliedUtc;
        }
        if (InstalledUtc > anterior.InstalledUtc) InstalledUtc = anterior.InstalledUtc;
    }
}
