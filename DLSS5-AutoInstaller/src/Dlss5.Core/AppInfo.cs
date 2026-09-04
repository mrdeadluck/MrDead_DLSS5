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

    /// <summary>
    /// Identificação do BUILD, não da versão. A versão do csproj é a mesma em toda
    /// compilação, então ela não distingue o executável de meia hora atrás do de agora —
    /// e essa dúvida já custou rodadas inteiras de teste: o usuário testava uma build
    /// antiga procurando um recurso que só existia na nova. O CI carimba o commit em
    /// SourceRevisionId, que o .NET anexa ao InformationalVersion depois de um '+'.
    /// Fora do CI (compilação local) não há carimbo, e aqui vira "local".
    /// </summary>
    public static string Build { get; } = LerBuild();

    private static string LerBuild()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info)) return "local";

            int plus = info.IndexOf('+');
            if (plus < 0 || plus + 1 >= info.Length) return "local";

            var sha = info[(plus + 1)..].Trim();
            return sha.Length >= 7 ? sha[..7] : sha;
        }
        catch
        {
            return "local";
        }
    }

    /// <summary>"1.1.0 (build a1b2c3d)" — o que identifica um executável sem ambiguidade.</summary>
    public static string VersaoComBuild => $"{Versao} (build {Build})";

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
