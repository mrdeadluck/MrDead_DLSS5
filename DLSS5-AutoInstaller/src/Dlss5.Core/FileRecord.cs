using System.Security.Cryptography;

namespace Dlss5.Core;

/// <summary>Resultado de conferir um arquivo do disco contra o registro guardado.</summary>
public enum ConferenciaDeArquivo
{
    /// <summary>Existe e tem o mesmo tamanho e hash.</summary>
    Igual,
    /// <summary>Existe, mas tamanho ou hash mudaram.</summary>
    Diferente,
    /// <summary>Não está mais no disco.</summary>
    Ausente,
    /// <summary>Existe, mas não foi possível ler (permissão, em uso).</summary>
    Ilegivel,
}

/// <summary>
/// Impressão digital de um arquivo (tamanho + SHA-256 + data). É o que permite afirmar
/// depois se um arquivo ainda é o que este programa gravou — e, portanto, se pode ser
/// apagado ou restaurado com segurança.
/// </summary>
public sealed class FileRecord
{
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime ModifiedUtc { get; set; }

    public static FileRecord Capturar(string path)
    {
        var fi = new FileInfo(path);
        return new FileRecord
        {
            Size = fi.Length,
            Sha256 = HashDe(path),
            ModifiedUtc = fi.LastWriteTimeUtc,
        };
    }

    /// <summary>SHA-256 em hexadecimal minúsculo. Abre compartilhado para tolerar logs abertos.</summary>
    public static string HashDe(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    public ConferenciaDeArquivo Conferir(string path)
    {
        if (!File.Exists(path)) return ConferenciaDeArquivo.Ausente;
        try
        {
            var fi = new FileInfo(path);
            if (fi.Length != Size) return ConferenciaDeArquivo.Diferente;
            // Tamanho e data iguais: quase certamente o mesmo arquivo — mas a data pode
            // ser preservada por uma cópia, então o hash decide quando há dúvida.
            if (string.IsNullOrEmpty(Sha256)) return ConferenciaDeArquivo.Igual;
            return string.Equals(HashDe(path), Sha256, StringComparison.OrdinalIgnoreCase)
                ? ConferenciaDeArquivo.Igual
                : ConferenciaDeArquivo.Diferente;
        }
        catch (IOException) { return ConferenciaDeArquivo.Ilegivel; }
        catch (UnauthorizedAccessException) { return ConferenciaDeArquivo.Ilegivel; }
    }

    /// <summary>Dois arquivos no disco têm o mesmo conteúdo? (tamanho primeiro, hash depois)</summary>
    public static bool MesmoConteudo(string a, string b)
    {
        try
        {
            if (!File.Exists(a) || !File.Exists(b)) return false;
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            return string.Equals(HashDe(a), HashDe(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
