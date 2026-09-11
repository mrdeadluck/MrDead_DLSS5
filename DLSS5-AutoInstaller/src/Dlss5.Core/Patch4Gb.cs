namespace Dlss5.Core;

/// <summary>
/// O "4GB Patch" (Large Address Aware) feito pelo próprio instalador: liga o bit
/// IMAGE_FILE_LARGE_ADDRESS_AWARE (0x0020) no Characteristics do cabeçalho COFF de um exe
/// 32-bit. Com ele, num Windows 64-bit, o processo enxerga 4 GB em vez de 2 GB. É o mesmo
/// que a ferramenta "4GB Patch" da NTCore faz — um bit, reversível, com backup ao lado.
///
/// Por que existe: em jogo 32-bit pesado atrás do dgVoodoo + ReShade + feed, os 2 GB acabam e
/// a engine Source responde com "failed to lock vertex buffer in CMeshDX8::LockVertexBuffer"
/// (Black Mesa, 11/09/2026). Numa engine tipo Source a flag vai no exe-stub da raiz.
///
/// A desinstalação NÃO desfaz de propósito (como o patch anti-hook do MGS V): o jogo abre
/// normalmente com a flag, e a Steam repõe o original se o usuário verificar a integridade.
/// O backup fica ao lado, com sufixo próprio, para quem quiser voltar à mão.
/// </summary>
public static class Patch4Gb
{
    public const string SufixoDoBackup = ".4gb-original";
    private const ushort FlagLaa = 0x0020;

    public static string CaminhoDoBackup(string exePath) => exePath + SufixoDoBackup;

    /// <summary>Se faz sentido oferecer: exe 32-bit legível, sem a flag.</summary>
    public static bool Cabe(string? exePath) =>
        exePath is not null && PeFile.GetArchitecture(exePath) == PeArchitecture.X86
        && PeFile.IsLargeAddressAware(exePath) == false;

    /// <summary>Liga a flag; guarda o original ao lado antes. Idempotente. Lança com explicação.</summary>
    public static void Aplicar(string exePath, Action<string>? log = null)
    {
        if (!File.Exists(exePath))
            throw new InvalidOperationException($"Não achei {exePath}.");
        if (PeFile.GetArchitecture(exePath) != PeArchitecture.X86)
            throw new InvalidOperationException($"{Path.GetFileName(exePath)} não é um exe 32-bit; a flag só faz sentido em 32-bit. Nada foi alterado.");
        var laa = PeFile.IsLargeAddressAware(exePath);
        if (laa is null)
            throw new InvalidOperationException($"Não consegui ler o cabeçalho de {Path.GetFileName(exePath)}. Nada foi alterado.");
        if (laa == true)
        {
            log?.Invoke($"{Path.GetFileName(exePath)} já tem a flag LAA; nada a fazer.");
            return;
        }

        var backup = CaminhoDoBackup(exePath);
        if (!File.Exists(backup))
        {
            File.Copy(exePath, backup, overwrite: false);
            log?.Invoke($"Backup do exe original guardado: {Path.GetFileName(backup)}");
        }

        using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var br = new BinaryReader(fs))
        using (var bw = new BinaryWriter(fs))
        {
            fs.Position = 0x3C;
            uint peOffset = br.ReadUInt32();
            fs.Position = peOffset + 22;
            ushort characteristics = br.ReadUInt16();
            fs.Position = peOffset + 22;
            bw.Write((ushort)(characteristics | FlagLaa));
            fs.Flush(true);
        }

        if (PeFile.IsLargeAddressAware(exePath) != true)
        {
            File.Copy(backup, exePath, overwrite: true);
            throw new InvalidOperationException("A flag não ficou gravada; o exe original foi devolvido do backup.");
        }
        log?.Invoke($"Flag LAA (4 GB) ligada em {Path.GetFileName(exePath)}.");
    }

    /// <summary>Devolve o exe original do backup, se existir.</summary>
    public static bool Reverter(string exePath, Action<string>? log = null)
    {
        var backup = CaminhoDoBackup(exePath);
        if (!File.Exists(backup)) return false;
        File.Copy(backup, exePath, overwrite: true);
        File.Delete(backup);
        log?.Invoke($"{Path.GetFileName(exePath)} devolvido do backup {SufixoDoBackup}.");
        return true;
    }
}
