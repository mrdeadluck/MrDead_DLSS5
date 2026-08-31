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
public sealed class InstallerEngine
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
                    WriteGenerated(action.TargetPath!, plan, manifest);
                    break;

                case PlanActionKind.PatchDgVoodooConf:
                    PatchConf(action.SourcePath!, action.TargetPath!, manifest);
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

    private void WriteGenerated(string target, InstallPlan plan, InstallManifest manifest)
    {
        EnsureDirFor(target, manifest);
        BackupIfExists(target, manifest);
        var name = Path.GetFileName(target);
        string content = name.Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase)
            ? ReShadeConfigWriter.BuildReShadeIni(
                plan.Options.OverlayKey,
                plan.Options.OverlayCtrl,
                plan.Options.OverlayShift,
                plan.Options.OverlayAlt)
            : ReShadeConfigWriter.BuildPresetIni(
                plan.Options.MvProvider,
                feederUsed: plan.Profile.NeedsFeeder);
        File.WriteAllText(target, content);
        Track(manifest, target);
        _log($"Gerado: {target}");
    }

    private void PatchConf(string source, string target, InstallManifest manifest)
    {
        EnsureDirFor(target, manifest);
        BackupIfExists(target, manifest);
        var text = File.ReadAllText(source);
        var patched = DgVoodooConfigurator.Patch(text);
        File.WriteAllText(target, patched);
        Track(manifest, target);

        var missing = DgVoodooConfigurator.MissingKeys(patched);
        if (missing.Count > 0)
            _log($"Aviso: chaves não encontradas no dgVoodoo.conf: {string.Join(", ", missing)}");
        _log($"dgVoodoo.conf ajustado: {target}");
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
    public void Revert(InstallManifest manifest, bool removeRegistryOverride)
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

        if (removeRegistryOverride && manifest.RegistryOverrideApplied)
        {
            SignatureOverride.Disable();
            _log("Override de assinatura removido do registro (exige REINICIAR o PC).");
        }

        var manifestPath = Path.Combine(manifest.ExeFolder, InstallManifest.FileName);
        try { if (File.Exists(manifestPath)) File.Delete(manifestPath); } catch { }
        _log("Reversão concluída.");
    }
}
