using System.Text.Json;

namespace Dlss5.Core;

/// <summary>Preferências persistidas em %APPDATA%\DLSS5-AutoInstaller\settings.json.</summary>
public sealed class AppSettings
{
    public string? KitFolder { get; set; }
    public string? LastGameFolder { get; set; }
    public string MvProvider { get; set; } = nameof(Core.MvProvider.Launchpad);
    public int OverlayKey { get; set; } = ReShadeConfigWriter.KeyHome;
    public bool OverlayCtrl { get; set; }
    public bool OverlayShift { get; set; }
    public bool OverlayAlt { get; set; }

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DLSS5-AutoInstaller");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // config corrompida: começa limpo
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // salvar preferências nunca deve derrubar o programa
        }
    }

    /// <summary>
    /// Tenta achar a pasta do kit sozinho: ao lado do .exe, na pasta acima,
    /// ou nos lugares óbvios (Downloads, Documentos\GitHub).
    /// </summary>
    public static string? GuessKitFolder()
    {
        var candidates = new List<string>();
        try
        {
            var baseDir = AppContext.BaseDirectory;
            candidates.Add(baseDir);
            var parent = Directory.GetParent(baseDir)?.FullName;
            if (parent is not null) candidates.Add(parent);
            var grand = parent is null ? null : Directory.GetParent(parent)?.FullName;
            if (grand is not null) candidates.Add(grand);
        }
        catch { }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(profile, "Downloads"));
        candidates.Add(Path.Combine(profile, "Documents", "GitHub", "MrDead_DLSS5"));
        candidates.Add(Path.Combine(profile, "Downloads", "MrDead_DLSS5"));

        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c) || !Directory.Exists(c)) continue;

            // A pasta do kit é a que contém "DLSS 5 Files" ou já é ela.
            var nested = Path.Combine(c, "DLSS 5 Files");
            if (Directory.Exists(nested) && LooksLikeKit(nested)) return nested;
            if (LooksLikeKit(c)) return c;
        }
        return null;
    }

    private static bool LooksLikeKit(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "nvngx_dlssnr.dll", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }
}
