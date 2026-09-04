using System.Diagnostics;

namespace Dlss5.Core;

/// <summary>
/// O d3dcompiler_47.dll que o jogo carrega — e que o addon do RenoDX usa para compilar o
/// shader do Neural Rendering.
///
/// O que ensinou isto: Spider-Man Remastered. Abre, Home, hooks armados, o jogo cria o
/// DLSS, o addon intercepta, o runtime inicializa... e o ReShade.log repete, a cada
/// tentativa, "proxy encode compilation failed with HRESULT 0x8876086c: error X3506:
/// unrecognized compiler target 'cs_5_1'". O jogo traz na pasta um d3dcompiler_47.dll do
/// SDK do Windows 8.1 (6.3.9600), que não conhece Shader Model 5.1; como a DLL já está
/// carregada no processo com esse nome, é ela que o addon recebe ao pedir D3DCompile —
/// o do Windows (10.0.x, em System32) nunca entra. O NR nunca roda, e nada avisa na tela.
///
/// O conserto é trocar a cópia do jogo pela do Windows, com backup: a API é a mesma, a
/// versão nova compila tudo que a velha compilava, e o original volta na desinstalação.
/// </summary>
public static class CompiladorD3D
{
    public const string Arquivo = "d3dcompiler_47.dll";

    /// <summary>A partir do SDK do Windows 10 o compilador conhece cs_5_1.</summary>
    public static readonly Version Minima = new(10, 0);

    /// <summary>O que o addon grava no ReShade.log quando o compilador é o velho.</summary>
    public const string ErroNoLog = "unrecognized compiler target 'cs_5_1'";

    /// <summary>Lê a versão gravada no arquivo; trocável nos testes (o Linux não lê VERSIONINFO).</summary>
    public static Func<string, Version?> LerVersao { get; set; } = VersaoPorRecurso;

    /// <summary>Onde está a cópia do Windows; trocável nos testes.</summary>
    public static Func<string?> CaminhoNoSistema { get; set; } = () =>
    {
        var sys = Environment.SystemDirectory;
        return string.IsNullOrEmpty(sys) ? null : Path.Combine(sys, Arquivo);
    };

    public static Version? VersaoPorRecurso(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            if (info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0) return null;
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A versão do arquivo, em texto, para as telas.</summary>
    public static string Descrever(string path) => LerVersao(path)?.ToString() ?? "sem versão gravada";

    /// <summary>Existe na pasta e não conhece cs_5_1 (sem versão, ou anterior ao SDK do Windows 10).</summary>
    public static bool Antigo(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        var v = LerVersao(path);
        return v is null || v < Minima;
    }

    /// <summary>A cópia do Windows, quando existe e serve; senão nulo.</summary>
    public static string? DoSistema()
    {
        var caminho = CaminhoNoSistema();
        if (string.IsNullOrEmpty(caminho) || !File.Exists(caminho)) return null;
        var v = LerVersao(caminho);
        return v is not null && v >= Minima ? caminho : null;
    }

    public static string PorQueTrocar(string caminhoNoJogo) =>
        $"O {Arquivo} da pasta do jogo ({Descrever(caminhoNoJogo)}) não conhece cs_5_1, o alvo do shader do " +
        "Neural Rendering. Como o jogo o carrega primeiro, é ele que o addon recebe: o NR falha ao compilar em " +
        "silêncio (\"error X3506\" no ReShade.log) e a imagem não muda.";

    public const string ComoConsertar =
        "Instale de novo com esta versão: o plano troca o d3dcompiler_47.dll do jogo pela cópia do Windows " +
        "(o original vai para backup e volta na desinstalação).";
}
