using System.IO.Compression;

namespace Dlss5.Core;

/// <summary>
/// Extrai ReShade32.dll / ReShade64.dll do instalador do ReShade.
/// O instalador carrega um ZIP embutido (spec 11.2 / 12.4), então o próprio
/// .exe abre com ZipFile; um ReShade_Setup_*.zip também serve.
/// </summary>
public static class ReShadeExtractor
{
    /// <summary>Nome da entrada no zip para a arquitetura pedida.</summary>
    public static string EntryNameFor(PeArchitecture arch) =>
        arch == PeArchitecture.X64 ? "ReShade64.dll" : "ReShade32.dll";

    /// <summary>
    /// Extrai a DLL do ReShade da arquitetura pedida para <paramref name="targetPath"/>
    /// (tipicamente "dxgi.dll" na pasta do jogo). Lança InvalidOperationException com
    /// mensagem clara se não conseguir.
    /// </summary>
    public static void ExtractTo(string setupPath, PeArchitecture arch, string targetPath)
    {
        string wanted = EntryNameFor(arch);

        ZipArchive OpenSetup()
        {
            ZipArchive? direct = null;
            try
            {
                direct = ZipFile.OpenRead(setupPath);

                // O ZipArchive lê o diretório central sob demanda. Num instalador com o zip
                // ANEXADO depois do PE, abrir passa e só o acesso às entradas estoura
                // ("number of entries ... does not correspond"), porque o offset gravado no
                // EOCD é relativo ao início do zip, não ao início do arquivo. Forçar a
                // leitura aqui é o que faz o erro cair neste catch, e não lá na frente.
                _ = direct.Entries.Count;
                return direct;
            }
            catch (InvalidDataException)
            {
                direct?.Dispose();

                // Procura o zip anexado e reabre a partir do início real dele.
                var ms = ExtractTrailingZip(setupPath)
                    ?? throw new InvalidOperationException(
                        $"Não encontrei um ZIP legível em {Path.GetFileName(setupPath)}. " +
                        "Aponte o kit para uma pasta que tenha o dxgi.dll do ReShade já extraído.");

                var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
                try
                {
                    _ = zip.Entries.Count;
                }
                catch (InvalidDataException ex)
                {
                    zip.Dispose();
                    throw new InvalidOperationException(
                        $"O ZIP embutido em {Path.GetFileName(setupPath)} não pôde ser lido: {ex.Message}");
                }
                return zip;
            }
        }

        using var zip = OpenSetup();
        var entry = zip.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"O instalador {Path.GetFileName(setupPath)} não contém {wanted}.");

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        entry.ExtractToFile(targetPath, overwrite: true);

        var got = PeFile.GetArchitecture(targetPath);
        var expected = arch;
        if (got != expected)
            throw new InvalidOperationException(
                $"{wanted} extraído tem arquitetura {got}, esperava {expected}.");
    }

    /// <summary>
    /// Procura um ZIP anexado ao fim de um arquivo (padrão de instaladores):
    /// acha a assinatura EOCD (PK\x05\x06) no fim, deriva o início do zip e
    /// devolve um MemoryStream só com o trecho do zip.
    /// </summary>
    public static MemoryStream? ExtractTrailingZip(string path)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }

        // EOCD: PK\x05\x06 — varre os últimos 66 KB (comentário máx. 64 KB).
        int searchStart = Math.Max(0, data.Length - 66 * 1024);
        int eocd = -1;
        for (int i = data.Length - 22; i >= searchStart; i--)
        {
            if (data[i] == 0x50 && data[i + 1] == 0x4B && data[i + 2] == 0x05 && data[i + 3] == 0x06)
            {
                eocd = i;
                break;
            }
        }
        if (eocd < 0) return null;

        uint cdSize = BitConverter.ToUInt32(data, eocd + 12);
        uint cdOffsetStored = BitConverter.ToUInt32(data, eocd + 16);
        long cdActual = eocd - cdSize;
        long zipStart = cdActual - cdOffsetStored;
        if (zipStart < 0 || zipStart >= data.Length) return null;

        // O zip "real" começa em zipStart; ZipArchive lê offsets relativos a ele.
        return new MemoryStream(data, (int)zipStart, data.Length - (int)zipStart, writable: false);
    }
}
