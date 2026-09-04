using System.Diagnostics;

namespace Dlss5.Core;

/// <summary>Um bloqueio encontrado antes de tocar em qualquer arquivo.</summary>
public sealed record Bloqueio(string Titulo, string Detalhe, string OQueFazer);

/// <summary>
/// Checagens que precisam passar ANTES de qualquer modificação: se vão falhar, que
/// falhem enquanto ainda não há nada para desfazer.
/// </summary>
public static class Preflight
{
    /// <summary>Dá para criar e apagar um arquivo nesta pasta?</summary>
    public static bool PastaGravavel(string pasta, out string? motivo)
    {
        motivo = null;
        try
        {
            if (!Directory.Exists(pasta))
            {
                motivo = "a pasta não existe";
                return false;
            }
            var sonda = Path.Combine(pasta, $".dlss5-sonda-{Guid.NewGuid():N}.tmp");
            using (var fs = new FileStream(sonda, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                fs.WriteByte(0);
            }
            return true;
        }
        catch (UnauthorizedAccessException ex) { motivo = "sem permissão de escrita: " + ex.Message; }
        catch (IOException ex) { motivo = ex.Message; }
        catch (Exception ex) { motivo = ex.Message; }
        return false;
    }

    /// <summary>Espaço livre na unidade da pasta, ou null se não der para saber.</summary>
    public static long? EspacoLivre(string pasta)
    {
        try
        {
            var raiz = Path.GetPathRoot(Path.GetFullPath(pasta));
            if (string.IsNullOrEmpty(raiz)) return null;
            return new DriveInfo(raiz).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Quais destes arquivos estão abertos por outro processo (não dá para substituir
    /// nem apagar). Testa abrindo com acesso exclusivo — o mesmo que a operação vai precisar.
    /// </summary>
    public static IReadOnlyList<string> ArquivosEmUso(IEnumerable<string> caminhos)
    {
        var emUso = new List<string>();
        foreach (var caminho in caminhos.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(caminho)) continue;
            try
            {
                using var fs = new FileStream(caminho, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) { emUso.Add(caminho); }
            catch (UnauthorizedAccessException)
            {
                // Somente leitura ou ACL: também não vai dar para substituir.
                emUso.Add(caminho);
            }
        }
        return emUso;
    }

    /// <summary>Nome do processo se o executável do jogo estiver rodando; senão null.</summary>
    public static string? JogoRodando(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        var nome = Path.GetFileNameWithoutExtension(exePath);
        try
        {
            var processos = Process.GetProcessesByName(nome);
            try { return processos.Length > 0 ? nome : null; }
            finally { foreach (var p in processos) p.Dispose(); }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Tamanho total dos arquivos de origem de um plano (o que será copiado).</summary>
    public static long BytesNecessarios(IEnumerable<PlanAction> acoes)
    {
        long total = 0;
        foreach (var a in acoes)
        {
            if (a.Kind != PlanActionKind.CopyFile || a.SourcePath is null) continue;
            try
            {
                if (Directory.Exists(a.SourcePath))
                    total += new DirectoryInfo(a.SourcePath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                else if (File.Exists(a.SourcePath))
                    total += new FileInfo(a.SourcePath).Length;
            }
            catch { /* origem ilegível aparece depois como erro de cópia */ }
        }
        return total;
    }

    /// <summary>
    /// Todas as checagens de uma vez, para a interface listar de uma só vez o que impede.
    /// </summary>
    public static IReadOnlyList<Bloqueio> Checar(
        string exeFolder, string? rendererFolder, string? exePath,
        IEnumerable<string> alvosQueSeraoSubstituidos, long bytesNecessarios)
    {
        var bloqueios = new List<Bloqueio>();

        var rodando = JogoRodando(exePath);
        if (rodando is not null)
            bloqueios.Add(new Bloqueio("O jogo está aberto",
                $"O processo {rodando}.exe está em execução. Arquivos em uso não podem ser substituídos nem removidos.",
                "Feche o jogo (e o cliente da loja, se ele mantiver o jogo aberto) e tente de novo."));

        foreach (var pasta in new[] { exeFolder, rendererFolder }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!PastaGravavel(pasta!, out var motivo))
                bloqueios.Add(new Bloqueio("Sem permissão para gravar na pasta do jogo",
                    $"{pasta}: {motivo}.",
                    "Execute o programa como administrador ou ajuste as permissões da pasta. Se a pasta estiver em uma unidade somente leitura, mova o jogo."));
        }

        var livre = EspacoLivre(exeFolder);
        // Folga de 64 MB para temporários, backups e o manifesto.
        long necessario = bytesNecessarios + 64L * 1024 * 1024;
        if (livre is not null && livre < necessario)
            bloqueios.Add(new Bloqueio("Pouco espaço em disco",
                $"Livre: {livre / (1024 * 1024)} MB. Necessário: cerca de {necessario / (1024 * 1024)} MB (arquivos + backups).",
                "Libere espaço na unidade do jogo e tente de novo."));

        var emUso = ArquivosEmUso(alvosQueSeraoSubstituidos);
        if (emUso.Count > 0)
            bloqueios.Add(new Bloqueio("Arquivo em uso ou protegido",
                string.Join("\r\n", emUso.Take(8)) + (emUso.Count > 8 ? $"\r\n… e mais {emUso.Count - 8}" : ""),
                "Algum programa mantém o arquivo aberto (jogo, cliente da loja, antivírus, overlay). Feche-os e tente de novo. Se persistir, reinicie o PC."));

        return bloqueios;
    }
}
