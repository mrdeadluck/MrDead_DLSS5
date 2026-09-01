using System.Text;

namespace Dlss5.Core;

/// <summary>
/// Convivência do dgVoodoo com o DxWrapper na mesma pasta.
///
/// Os dois precisam do MESMO nome de arquivo (d3d9.dll / d3d8.dll): é pelo nome que o
/// jogo carrega o wrapper. No Dead Space 2 o d3d9.dll da pasta era uma build especial do
/// DxWrapper (a única coisa que faz o jogo abrir em CPU com mais de 10 núcleos), e a
/// instalação copiava o dgVoodoo por cima — o conserto sumia e o jogo voltava a não abrir.
///
/// A saída é encadear: o DxWrapper continua sendo o d3d9.dll, e o dxwrapper.ini dele
/// ganha um RealDllPath apontando para o dgVoodoo, gravado ao lado com outro nome. O
/// stub do DxWrapper lê essa chave e carrega o "d3d9 real" de lá em vez do System32; o
/// dgVoodoo acha o dgVoodoo.conf pela pasta, não pelo nome, então funciona renomeado.
/// </summary>
public static class DxWrapperChain
{
    public const string DxWrapperDll = "dxwrapper.dll";
    public const string IniName = "dxwrapper.ini";
    public const string Prefixo = "dgVoodoo_";
    public const string Marca = "; Gerado pelo DLSS 5 AutoInstaller";

    /// <summary>Nome com que o dgVoodoo é gravado quando o nome original está ocupado.</summary>
    public static string NomeEncadeado(string wrapper) => Prefixo + wrapper;

    /// <summary>Há um DxWrapper nesta pasta?</summary>
    public static bool DxWrapperPresente(string pasta) =>
        File.Exists(Path.Combine(pasta, DxWrapperDll));

    /// <summary>A instalação desta pasta está no arranjo encadeado?</summary>
    public static bool Encadeado(string pasta, string wrapper) =>
        DxWrapperPresente(pasta) && File.Exists(Path.Combine(pasta, NomeEncadeado(wrapper)));

    /// <summary>
    /// Texto do dxwrapper.ini com o RealDllPath apontando para o dgVoodoo. Se já existe
    /// um ini do usuário, só a linha do RealDllPath muda (ou é acrescentada) — o resto é
    /// dele e fica como está.
    /// </summary>
    public static string GerarIni(string? existente, string realDllPath)
    {
        if (string.IsNullOrWhiteSpace(existente))
        {
            var sb = new StringBuilder();
            sb.Append(Marca).Append("\r\n");
            sb.Append("; Encadeia o DxWrapper (que continua sendo o d3d9.dll) ao dgVoodoo. Sem esta linha o\r\n");
            sb.Append("; DxWrapper carregaria o d3d9 do Windows e o dgVoodoo ficaria fora do caminho.\r\n");
            sb.Append("[General]\r\n");
            sb.Append("RealDllPath = ").Append(realDllPath).Append("\r\n");
            return sb.ToString();
        }

        var linhas = existente.Replace("\r\n", "\n").Split('\n').ToList();
        int i = IndiceDaChave(linhas);
        if (i >= 0)
        {
            var raw = linhas[i];
            int eq = raw.IndexOf('=');
            linhas[i] = raw[..(eq + 1)] + " " + realDllPath;
        }
        else
        {
            int geral = linhas.FindIndex(l => l.Trim().Equals("[General]", StringComparison.OrdinalIgnoreCase));
            var nova = "RealDllPath = " + realDllPath;
            if (geral >= 0) linhas.Insert(geral + 1, nova);
            else linhas.Add(nova);
        }
        return string.Join("\r\n", linhas);
    }

    /// <summary>Valor atual do RealDllPath, ou null se não houver.</summary>
    public static string? LerRealDllPath(string ini)
    {
        var linhas = ini.Replace("\r\n", "\n").Split('\n');
        foreach (var raw in linhas)
        {
            int eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            if (!raw[..eq].Trim().Equals("RealDllPath", StringComparison.OrdinalIgnoreCase)) continue;
            var valor = raw[(eq + 1)..].Trim();
            return valor.Length == 0 ? null : valor;
        }
        return null;
    }

    /// <summary>O RealDllPath aponta para o dgVoodoo que nós gravamos?</summary>
    public static bool ApontaParaODgVoodoo(string ini) =>
        LerRealDllPath(ini) is { } valor
        && Path.GetFileName(valor).StartsWith(Prefixo, StringComparison.OrdinalIgnoreCase);

    /// <summary>O ini inteiro é obra nossa (e pode ser apagado sem perda)?</summary>
    public static bool IniEhNosso(string ini) =>
        ini.TrimStart().StartsWith(Marca, StringComparison.Ordinal);

    /// <summary>
    /// Devolve o ini com o RealDllPath em branco — o DxWrapper volta a carregar o d3d9 do
    /// Windows. É o que a faxina faz num ini que é do usuário, e o que o isolamento faz
    /// para tirar o dgVoodoo do caminho sem mexer no DxWrapper.
    /// </summary>
    public static string Desencadear(string ini)
    {
        var linhas = ini.Replace("\r\n", "\n").Split('\n').ToList();
        int i = IndiceDaChave(linhas);
        if (i >= 0)
        {
            int eq = linhas[i].IndexOf('=');
            linhas[i] = linhas[i][..(eq + 1)] + " ";
        }
        return string.Join("\r\n", linhas);
    }

    private static int IndiceDaChave(List<string> linhas) =>
        linhas.FindIndex(raw =>
        {
            int eq = raw.IndexOf('=');
            return eq > 0 && raw[..eq].Trim().Equals("RealDllPath", StringComparison.OrdinalIgnoreCase);
        });
}
