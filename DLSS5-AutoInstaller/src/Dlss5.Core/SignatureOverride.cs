using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Dlss5.Core;

/// <summary>
/// Override de assinatura do NGX no registro (spec 12.1). O nvngx_dlssnr.dll
/// patcheado para Ada tem Authenticode quebrada; sem este override o driver recusa.
/// Requer privilégio de administrador e REINÍCIO (o driver só lê na inicialização).
/// </summary>
[SupportedOSPlatform("windows")]
public static class SignatureOverride
{
    private const string ValueName = "{41FCC608-8496-4DEF-B43E-7D9BD675A6FF}";

    private static readonly string[] Paths =
    {
        @"SOFTWARE\NVIDIA Corporation\Global",
        @"SYSTEM\ControlSet001\Services\nvlddmkm",
        @"SYSTEM\CurrentControlSet\Services\nvlddmkm",
    };

    public sealed record Status(bool AllSet, IReadOnlyList<(string Path, bool Set)> Entries);

    public static Status Query()
    {
        var entries = new List<(string, bool)>();
        foreach (var p in Paths)
        {
            bool set = false;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(p);
                var v = key?.GetValue(ValueName);
                set = v is int i && i == 1;
            }
            catch
            {
                set = false;
            }
            entries.Add((p, set));
        }
        return new Status(entries.All(e => e.Item2), entries);
    }

    /// <summary>Aplica o DWORD=1 nas 3 chaves. Lança se faltar permissão (rode como admin).</summary>
    public static void Enable()
    {
        foreach (var p in Paths)
        {
            using var key = Registry.LocalMachine.CreateSubKey(p, writable: true)
                ?? throw new InvalidOperationException($"Não consegui abrir/criar HKLM\\{p}. Rode como administrador.");
            key.SetValue(ValueName, 1, RegistryValueKind.DWord);
        }
    }

    public static void Disable()
    {
        foreach (var p in Paths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(p, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            catch
            {
                // segue nas demais chaves
            }
        }
    }

    /// <summary>
    /// Verifica se o sistema foi reiniciado depois de a chave ter sido escrita.
    /// Compara o último boot com a hora de modificação da chave. Como o registro
    /// não expõe a hora do valor via API gerenciada, usamos um marcador próprio.
    /// </summary>
    public static bool RebootedSinceEnable(DateTime enabledUtc)
    {
        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var lastBoot = DateTime.UtcNow - uptime;
            return lastBoot > enabledUtc;
        }
        catch
        {
            return false;
        }
    }
}
