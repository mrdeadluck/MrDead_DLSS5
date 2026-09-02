using System.Reflection;

namespace Dlss5.Core;

/// <summary>Identidade do programa, para carimbar manifestos e logs.</summary>
public static class AppInfo
{
    public const string Nome = "DLSS 5 AutoInstaller";

    /// <summary>Versão do executável (a do csproj). "0.0.0" só quando a reflexão falha.</summary>
    public static string Versao { get; } = Ler();

    private static string Ler()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                // "1.2.0+abc123" → "1.2.0"
                int plus = info.IndexOf('+');
                return plus > 0 ? info[..plus] : info;
            }
            return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

    /// <summary>Descrição curta do sistema, para o cabeçalho do log (sem dados pessoais).</summary>
    public static string SistemaOperacional
    {
        get
        {
            try { return $"{Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})"; }
            catch { return "desconhecido"; }
        }
    }
}
