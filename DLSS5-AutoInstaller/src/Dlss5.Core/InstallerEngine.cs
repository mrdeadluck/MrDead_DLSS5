using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dlss5.Core;

/// <summary>Registro do que a instalação criou/alterou, para poder reverter (spec 13).</summary>
public sealed class InstallManifest
{
    public string Version { get; set; } = "1";
    public DateTime InstalledUtc { get; set; } = DateTime.UtcNow;
    public string GameFolder { get; set; } = "";
    public string ExeFolder { get; set; } = "";
    public string? RealExePath { get; set; }
    public string Route { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string Api { get; set; } = "";
    public string MvProvider { get; set; } = "";
    public bool RegistryOverrideApplied { get; set; }
    public DateTime? RegistryOverrideAppliedUtc { get; set; }

    /// <summary>Arquivos criados por nós (apagar ao reverter).</summary>
    public List<string> AddedFiles { get; set; } = new();

    /// <summary>Pastas criadas por nós (apagar se vazias/nossas ao reverter).</summary>
    public List<string> AddedDirectories { get; set; } = new();

    /// <summary>Arquivos originais que sobrescrevemos: caminho → backup (.dlss5bak).</summary>
    public Dictionary<string, string> BackedUpFiles { get; set; } = new();

    /// <summary>Arquivos proibidos removidos: caminho original → backup.</summary>
    public Dictionary<string, string> RemovedFiles { get; set; } = new();

    public const string FileName = "dlss5-autoinstaller-manifest.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, JsonOpts));
    }

    /// <summary>
    /// Procura o manifesto na pasta do exe e, se não achar, em qualquer subpasta do jogo.
    /// A instalação pode ter sido feita apontando outro executável (jogo Unreal, launcher),
    /// e aí o manifesto não está onde a detecção de agora aponta.
    /// </summary>
    public static InstallManifest? Find(string? gameFolder, string? exeFolder)
    {
        if (!string.IsNullOrWhiteSpace(exeFolder))
        {
            var direto = Load(exeFolder);
            if (direto is not null) return direto;
        }
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder)) return null;
        try
        {
            foreach (var achado in Directory.EnumerateFiles(gameFolder, FileName, SearchOption.AllDirectories))
            {
                var m = Load(Path.GetDirectoryName(achado)!);
                if (m is not null) return m;
            }
        }
        catch
        {
            // pasta sem permissão de leitura: segue sem manifesto
        }
        return null;
    }

    public static InstallManifest? Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Executa um plano e sabe desfazê-lo.</summary>
public sealed partial class InstallerEngine
{
    private readonly Action<string> _log;

    public InstallerEngine(Action<string> log) => _log = log;

    /// <summary>Sufixo dos backups que criamos.</summary>
    public const string BackupSuffix = ".dlss5bak";

    public InstallManifest Execute(InstallPlan plan, KitInventory kit)
    {
        var profile = plan.Profile;
        var manifest = new InstallManifest
        {
            GameFolder = profile.GameFolder,
            ExeFolder = profile.ExeFolder,
            RealExePath = profile.RealExePath,
            Route = profile.Route.ToString(),
            Architecture = profile.Architecture.ToString(),
            Api = profile.Api.ToString(),
            MvProvider = plan.Options.MvProvider.ToString(),
        };

        foreach (var action in plan.Actions)
        {
            switch (action.Kind)
            {
                case PlanActionKind.DeleteForbiddenFile:
                    RemoveForbidden(action.TargetPath!, manifest);
                    break;

                case PlanActionKind.CopyFile:
                    if (Directory.Exists(action.SourcePath))
                        CopyDirectory(action.SourcePath!, action.TargetPath!, manifest);
                    else
                        CopyFile(action.SourcePath!, action.TargetPath!, manifest);
                    break;

                case PlanActionKind.ExtractReShadeDll:
                    EnsureDirFor(action.TargetPath!, manifest);
                    BackupIfExists(action.TargetPath!, manifest);
                    ReShadeExtractor.ExtractTo(action.SourcePath!, profile.Architecture, action.TargetPath!);
                    Track(manifest, action.TargetPath!);
                    _log($"Extraído: {action.TargetPath}");
                    break;

                case PlanActionKind.WriteGeneratedFile:
                    WriteGenerated(action.TargetPath!, plan, manifest, action.SourcePath);
                    break;

                case PlanActionKind.PatchDgVoodooConf:
                    PatchConf(action.SourcePath!, action.TargetPath!, manifest, profile.Api);
                    break;

                case PlanActionKind.RegistryOverride:
                    SignatureOverride.Enable();
                    manifest.RegistryOverrideApplied = true;
                    manifest.RegistryOverrideAppliedUtc = DateTime.UtcNow;
                    _log("Override de assinatura NGX aplicado no registro (exige REINICIAR o PC).");
                    break;
            }
        }

        manifest.Save(profile.ExeFolder);
        _log($"Manifesto salvo em {Path.Combine(profile.ExeFolder, InstallManifest.FileName)}");
        return manifest;
    }

    /// <param name="realDllPath">
    /// Só para o ini do stub do DxWrapper (d3d9.ini): o dgVoodoo encadeado que o RealDllPath deve apontar. Um
    /// ini que já era do usuário é preservado — só essa linha muda (o backup fica no
    /// manifesto, e a reversão devolve o arquivo dele).
    /// </param>
    private void WriteGenerated(string target, InstallPlan plan, InstallManifest manifest, string? realDllPath = null)
    {
        EnsureDirFor(target, manifest);
        BackupIfExists(target, manifest);
        var name = Path.GetFileName(target);
        string content;
        if (realDllPath is not null)
        {
            var existente = File.Exists(target) ? File.ReadAllText(target) : null;
            content = DxWrapperChain.GerarIni(existente, realDllPath!);
        }
        else if (name.Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase)
                 || name.Equals(ReFramework.ReShadeIni, StringComparison.OrdinalIgnoreCase))
        {
            content = ReShadeConfigWriter.BuildReShadeIni(
                plan.Options.OverlayKey,
                plan.Options.OverlayCtrl,
                plan.Options.OverlayShift,
                plan.Options.OverlayAlt,
                feederUsed: plan.Profile.NeedsFeeder,
                // Hospedado no REFramework o ini fica longe da pasta do jogo: sem caminho
                // absoluto o ReShade procuraria shaders e addons dentro de plugins\.
                baseDir: plan.Profile.UsarReFramework ? plan.Profile.ExeFolder : null);
        }
        else
        {
            content = ReShadeConfigWriter.BuildPresetIni(
                plan.Options.MvProvider,
                feederUsed: plan.Profile.NeedsFeeder);
        }
        File.WriteAllText(target, content);
        Track(manifest, target);
        _log($"Gerado: {target}");
    }

    private void PatchConf(string source, string target, InstallManifest manifest, GraphicsApi api)
    {
        EnsureDirFor(target, manifest);
        BackupIfExists(target, manifest);
        var perfil = DgVoodooConfigurator.ProfileFor(api);
        var text = File.ReadAllText(source);
        var patched = DgVoodooConfigurator.Patch(text, perfil);
        File.WriteAllText(target, patched);
        Track(manifest, target);

        var missing = DgVoodooConfigurator.MissingKeys(patched, perfil);
        if (missing.Count > 0)
            _log($"Aviso: chaves não encontradas no dgVoodoo.conf: {string.Join(", ", missing)}");
        _log($"dgVoodoo.conf ajustado ({perfil}): {target}");
        foreach (var (secao, chave, valor) in DgVoodooConfigurator.TargetsFor(perfil))
            _log($"   [{secao}] {chave} = {valor}");
    }

    /// <summary>Tudo que a instalação pode ter deixado na pasta do exe.</summary>
    private static readonly string[] NossosArquivos =
    {
        "dxgi.dll", "opengl32.dll", "ReShade.ini", "ReShade.log", "ReShadePreset.ini",
        "ReShade64.json", "ReShade32.json", "ReShade64_XR.json", "ReShade32_XR.json",
        "renodx-dlss5.addon64", "nvngx_dlssnr.dll",
        "dlss5-feed.addon64", "dlss5-feed.addon32", "dlss5-feed.cfg", "dlss5-feed.log",
        "D3D9.dll", "D3D8.dll", "dgVoodoo.conf", "dgVoodooCpl.exe",
        "dgVoodoo_D3D9.dll", "dgVoodoo_D3D8.dll",
    };

    /// <summary>
    /// Confere o que ainda está na pasta depois de reverter. Arquivo em uso não é
    /// apagado — se o jogo ou a Steam estiverem abertos, a remoção falha em silêncio.
    /// </summary>
    public IReadOnlyList<string> ConferirSobras(string? exeFolder)
    {
        var sobras = new List<string>();
        if (string.IsNullOrWhiteSpace(exeFolder) || !Directory.Exists(exeFolder)) return sobras;

        foreach (var nome in NossosArquivos)
        {
            var caminho = Path.Combine(exeFolder, nome);
            if (File.Exists(caminho)) sobras.Add(caminho);
        }

        // O transplante que resistiu (arquivo em uso) é sobra como qualquer outra:
        // listado aqui, ele entra no aviso e na oferta de faxina completa.
        var transplante = Path.Combine(exeFolder, "nvngx_dlss.dll");
        if (TransplanteDlss.EhDoKit(transplante, NvngxDlssDoKit)) sobras.Add(transplante);

        // Modo REFramework: o hospedeiro (só se for o do kit) e o ReShade dentro dele.
        var dinput = ReFramework.CaminhoDinput8(exeFolder);
        if (ReFramework.EhDoKit(dinput, ReFrameworkDoKit)) sobras.Add(dinput);
        foreach (var caminho in new[] { ReFramework.CaminhoPlugin(exeFolder), ReFramework.CaminhoIni(exeFolder) })
            if (File.Exists(caminho)) sobras.Add(caminho);

        // Um ini do DxWrapper ainda apontando para o dgVoodoo é sobra perigosa: com o
        // dgVoodoo fora, o DxWrapper tentaria carregar um arquivo que não existe.
        sobras.AddRange(InisEncadeadosEm(exeFolder));
        foreach (var pasta in new[] { "reshade-shaders", "host64" })
        {
            var caminho = Path.Combine(exeFolder, pasta);
            if (Directory.Exists(caminho)) sobras.Add(caminho + Path.DirectorySeparatorChar);
        }
        try
        {
            sobras.AddRange(Directory.EnumerateFiles(exeFolder, "*" + BackupSuffix, SearchOption.AllDirectories));
        }
        catch
        {
            // sem permissão de leitura: o que já foi listado basta
        }
        return sobras;
    }

    /// <summary>Nomes que o ReShade e o Feeder criam ao rodar, e que o manifesto não conhece.</summary>
    private static readonly string[] RestosDeExecucao =
    {
        "ReShade.log", "ReShade64.json", "ReShade32.json",
        "ReShade64_XR.json", "ReShade32_XR.json",
        "dlss5-feed.log", "dlss5-feed.cfg", "dlss5-feed-host.log",
    };

    /// <summary>
    /// Devolve ao lugar todo arquivo *.dlss5bak encontrado na pasta e diz quais voltaram.
    /// Saber quais voltaram importa: um arquivo restaurado é do JOGO outra vez, e a
    /// limpeza não pode apagá-lo depois só porque o nome dele também está na nossa lista.
    /// </summary>
    public IReadOnlyList<string> RestaurarBackupsOrfaos(string? pasta)
    {
        var restaurados = new List<string>();
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) return restaurados;

        List<string> backups;
        try
        {
            backups = Directory.EnumerateFiles(pasta, "*" + BackupSuffix, SearchOption.AllDirectories).ToList();
        }
        catch
        {
            return restaurados;
        }

        foreach (var backup in backups)
        {
            var original = backup[..^BackupSuffix.Length];
            try
            {
                if (!File.Exists(backup)) continue;
                File.Move(backup, original, overwrite: true);
                restaurados.Add(original);
                _log($"Devolvido ao lugar: {original}");
            }
            catch (Exception ex)
            {
                _log($"Aviso: não consegui devolver {original}: {ex.Message}");
            }
        }
        return restaurados;
    }

    /// <summary>Apaga os arquivos que só aparecem depois que o jogo roda uma vez.</summary>
    public void LimparRestosDeExecucao(string? exeFolder)
    {
        if (string.IsNullOrWhiteSpace(exeFolder) || !Directory.Exists(exeFolder)) return;

        foreach (var nome in RestosDeExecucao)
        {
            foreach (var alvo in new[]
                     {
                         Path.Combine(exeFolder, nome),
                         Path.Combine(exeFolder, "host64", nome),
                     })
            {
                try
                {
                    if (!File.Exists(alvo)) continue;
                    File.Delete(alvo);
                    _log($"Apagado (gerado ao rodar): {alvo}");
                }
                catch (Exception ex)
                {
                    _log($"Aviso: {alvo}: {ex.Message}");
                }
            }
        }

        foreach (var pasta in new[] { "host64", "reshade-shaders" })
        {
            try
            {
                var alvo = Path.Combine(exeFolder, pasta);
                if (Directory.Exists(alvo) && !Directory.EnumerateFileSystemEntries(alvo).Any())
                    Directory.Delete(alvo);
            }
            catch
            {
                // pasta com conteúdo do usuário: fica
            }
        }
    }

    private void RemoveForbidden(string path, InstallManifest manifest)
    {
        if (!File.Exists(path)) return;
        var backup = path + BackupSuffix;
        try
        {
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(path, backup);
            manifest.RemovedFiles[path] = backup;
            _log($"Removido (backup guardado): {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            _log($"Aviso: não consegui remover {path}: {ex.Message}");
        }
    }

    private void CopyFile(string source, string target, InstallManifest manifest)
    {
        EnsureDirFor(target, manifest);
        BackupIfExists(target, manifest);
        File.Copy(source, target, overwrite: true);
        Track(manifest, target);
        _log($"Copiado: {Path.GetFileName(target)} → {Path.GetDirectoryName(target)}");
    }

    private void CopyDirectory(string sourceDir, string targetDir, InstallManifest manifest)
    {
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
            manifest.AddedDirectories.Add(targetDir);
        }
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var dst = Path.Combine(targetDir, rel);
            EnsureDirFor(dst, manifest);
            BackupIfExists(dst, manifest);
            File.Copy(file, dst, overwrite: true);
            Track(manifest, dst);
        }
        _log($"Copiada pasta: {Path.GetFileName(sourceDir)} → {targetDir}");
    }

    private static void EnsureDirFor(string filePath, InstallManifest manifest)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir) || Directory.Exists(dir)) return;
        Directory.CreateDirectory(dir);
        manifest.AddedDirectories.Add(dir);
    }

    private static void BackupIfExists(string target, InstallManifest manifest)
    {
        if (!File.Exists(target)) return;
        if (manifest.BackedUpFiles.ContainsKey(target)) return;
        var backup = target + BackupSuffix;
        try
        {
            if (File.Exists(backup)) File.Delete(backup);
            File.Copy(target, backup);
            manifest.BackedUpFiles[target] = backup;
        }
        catch
        {
            // sem backup: seguimos, mas não registramos
        }
    }

    private static void Track(InstallManifest manifest, string path)
    {
        if (!manifest.AddedFiles.Contains(path, StringComparer.OrdinalIgnoreCase)
            && !manifest.BackedUpFiles.ContainsKey(path))
            manifest.AddedFiles.Add(path);
    }

    /// <summary>Desfaz a instalação a partir do manifesto (spec 13).</summary>
    /// <summary>
    /// Desfaz a instalação e devolve a lista do que NÃO saiu. Devolver essa lista é o
    /// ponto: antes cada falha virava só uma linha de aviso no log, e o usuário só
    /// descobria que sobrou arquivo quando o overlay do ReShade aparecia no jogo.
    /// </summary>
    public IReadOnlyList<string> Revert(InstallManifest manifest, bool removeRegistryOverride)
    {
        foreach (var file in manifest.AddedFiles)
        {
            try
            {
                if (File.Exists(file)) { File.Delete(file); _log($"Apagado: {file}"); }
            }
            catch (Exception ex) { _log($"Aviso: {file}: {ex.Message}"); }
        }

        foreach (var (original, backup) in manifest.BackedUpFiles)
        {
            try
            {
                if (File.Exists(backup))
                {
                    File.Copy(backup, original, overwrite: true);
                    File.Delete(backup);
                    _log($"Restaurado: {original}");
                }
            }
            catch (Exception ex) { _log($"Aviso: {original}: {ex.Message}"); }
        }

        foreach (var (original, backup) in manifest.RemovedFiles)
        {
            try
            {
                if (File.Exists(backup))
                {
                    File.Move(backup, original, overwrite: true);
                    _log($"Devolvido: {original}");
                }
            }
            catch (Exception ex) { _log($"Aviso: {original}: {ex.Message}"); }
        }

        foreach (var dir in manifest.AddedDirectories
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    _log($"Pasta removida: {dir}");
                }
            }
            catch { /* pasta com conteúdo do usuário: deixa quieto */ }
        }

        // Rede de segurança: qualquer .dlss5bak que sobrou volta ao lugar, mesmo sem
        // constar no manifesto. É o que salva uma instalação antiga ou interrompida.
        RestaurarBackupsOrfaos(manifest.ExeFolder);
        RestaurarBackupsOrfaos(manifest.GameFolder);

        // O transplante de instalação antiga não consta em manifesto nenhum: se o
        // nvngx_dlss.dll que ficou é byte a byte o do kit, ele NÃO é do jogo — sai
        // agora, para a verificação de integridade da Steam poder repor o original.
        // Depois dos backups: um original recém-devolvido tem bytes diferentes e fica.
        RemoverTransplante(manifest.ExeFolder);

        // O ReShade e o addon criam estes depois de instalar, então não estão no
        // manifesto — e sem apagá-los a "desinstalação" deixa sujeira para trás.
        LimparRestosDeExecucao(manifest.ExeFolder);

        if (removeRegistryOverride && manifest.RegistryOverrideApplied)
        {
            SignatureOverride.Disable();
            _log("Override de assinatura removido do registro (exige REINICIAR o PC).");
        }

        var manifestPath = Path.Combine(manifest.ExeFolder, InstallManifest.FileName);
        try { if (File.Exists(manifestPath)) File.Delete(manifestPath); } catch { }
        _log("Reversão concluída.");

        var sobras = ConferirSobras(manifest.ExeFolder);
        if (sobras.Count > 0)
        {
            _log("");
            _log("ATENÇÃO: estes arquivos NÃO foram removidos:");
            foreach (var f in sobras) _log("   " + f);
            _log("Feche o jogo e a Steam e tente de novo, ou apague à mão.");
        }
        else
        {
            _log("Conferido: nenhum arquivo da instalação sobrou na pasta.");
        }
        return sobras;
    }
}
