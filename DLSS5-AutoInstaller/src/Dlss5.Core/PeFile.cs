namespace Dlss5.Core;

/// <summary>
/// Leitura mínima de executáveis PE: arquitetura (Machine) e tabela de imports.
/// Baseado na regra da spec 8.1: detectar pelo cabeçalho PE, nunca pelo nome da pasta.
/// </summary>
public static class PeFile
{
    private const ushort MachineX86 = 0x014C;
    private const ushort MachineX64 = 0x8664;

    /// <summary>Lê a arquitetura de um PE. Nunca lança; Unknown em caso de erro.</summary>
    public static PeArchitecture GetArchitecture(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            if (fs.Length < 0x40) return PeArchitecture.Unknown;
            if (br.ReadUInt16() != 0x5A4D) return PeArchitecture.Unknown; // "MZ"
            fs.Position = 0x3C;
            uint peOffset = br.ReadUInt32();
            if (peOffset + 6 > fs.Length) return PeArchitecture.Unknown;
            fs.Position = peOffset;
            if (br.ReadUInt32() != 0x00004550) return PeArchitecture.Unknown; // "PE\0\0"
            ushort machine = br.ReadUInt16();
            return machine switch
            {
                MachineX86 => PeArchitecture.X86,
                MachineX64 => PeArchitecture.X64,
                _ => PeArchitecture.Unknown,
            };
        }
        catch
        {
            return PeArchitecture.Unknown;
        }
    }

    /// <summary>
    /// Lê os nomes das DLLs importadas estaticamente pelo PE (minúsculas).
    /// Serve como dica de API gráfica; muitos jogos carregam D3D dinamicamente,
    /// então lista vazia não prova nada. Nunca lança.
    /// </summary>
    public static IReadOnlyList<string> GetImportedDlls(string path)
    {
        var result = new List<string>();
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            var pe = LerCabecalhos(fs, br);
            if (pe is null) return result;

            // Data directories: Import = índice 1.
            fs.Position = pe.DataDirStart + 8;
            uint importRva = br.ReadUInt32();
            uint importSize = br.ReadUInt32();
            if (importRva == 0 || importSize == 0) return result;

            long importOffset = pe.RvaToOffset(importRva);
            if (importOffset < 0) return result;

            // Import descriptors: 20 bytes cada; termina num descriptor todo zero.
            for (int i = 0; i < 512; i++)
            {
                long d = importOffset + i * 20L;
                if (d + 20 > fs.Length) break;
                fs.Position = d;
                uint originalFirstThunk = br.ReadUInt32();
                fs.Position = d + 12;
                uint nameRva = br.ReadUInt32();
                uint firstThunk = br.ReadUInt32();
                if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0) break;
                string? name = ReadAsciiZ(fs, pe.RvaToOffset(nameRva));
                if (!string.IsNullOrEmpty(name))
                    result.Add(name.ToLowerInvariant());
            }
        }
        catch
        {
            // dica opcional: falha silenciosa
        }
        return result;
    }

    /// <summary>
    /// Lê os nomes exportados pelo PE. Um exe quase nunca exporta nada — a exceção que
    /// interessa é D3D12SDKVersion/D3D12SDKPath, que o Agility SDK do D3D12 exige do
    /// executável: quem exporta isso renderiza em D3D12. A tabela fica legível mesmo em
    /// exe cifrado (o loader precisa dela). Nunca lança.
    /// </summary>
    public static IReadOnlyList<string> GetExportedNames(string path)
    {
        var result = new List<string>();
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            var pe = LerCabecalhos(fs, br);
            if (pe is null) return result;

            // Export = índice 0.
            fs.Position = pe.DataDirStart;
            uint exportRva = br.ReadUInt32();
            uint exportSize = br.ReadUInt32();
            if (exportRva == 0 || exportSize == 0) return result;

            long exportOffset = pe.RvaToOffset(exportRva);
            if (exportOffset < 0 || exportOffset + 40 > fs.Length) return result;

            // IMAGE_EXPORT_DIRECTORY: NumberOfNames em +24, AddressOfNames em +32.
            fs.Position = exportOffset + 24;
            uint numberOfNames = br.ReadUInt32();
            fs.Position = exportOffset + 32;
            uint addressOfNames = br.ReadUInt32();
            long namesOffset = pe.RvaToOffset(addressOfNames);
            if (namesOffset < 0) return result;

            uint limite = Math.Min(numberOfNames, 4096);
            for (uint i = 0; i < limite; i++)
            {
                long entry = namesOffset + i * 4L;
                if (entry + 4 > fs.Length) break;
                fs.Position = entry;
                uint nameRva = br.ReadUInt32();
                string? name = ReadAsciiZ(fs, pe.RvaToOffset(nameRva));
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
        }
        catch
        {
            // dica opcional: falha silenciosa
        }
        return result;
    }

    private sealed class Cabecalhos
    {
        public long DataDirStart;
        public List<(uint va, uint rawSize, uint rawPtr)> Sections = new();

        public long RvaToOffset(uint rva)
        {
            foreach (var (va, rawSize, rawPtr) in Sections)
            {
                if (rva >= va && rva < va + rawSize)
                    return rawPtr + (rva - va);
            }
            return -1;
        }
    }

    /// <summary>Cabeçalhos DOS/COFF/opcional e a tabela de seções; null se não for PE.</summary>
    private static Cabecalhos? LerCabecalhos(FileStream fs, BinaryReader br)
    {
        fs.Position = 0;
        if (fs.Length < 0x40 || br.ReadUInt16() != 0x5A4D) return null;
        fs.Position = 0x3C;
        uint peOffset = br.ReadUInt32();
        if (peOffset + 24 > fs.Length) return null;
        fs.Position = peOffset;
        if (br.ReadUInt32() != 0x00004550) return null;

        // COFF header
        fs.Position = peOffset + 4 + 2; // pula Machine
        ushort numberOfSections = br.ReadUInt16();
        fs.Position = peOffset + 4 + 16;
        ushort sizeOfOptionalHeader = br.ReadUInt16();

        long optionalHeaderStart = peOffset + 4 + 20;
        fs.Position = optionalHeaderStart;
        ushort magic = br.ReadUInt16();
        bool isPe32Plus = magic == 0x20B;
        if (magic != 0x10B && magic != 0x20B) return null;

        // Data directories: PE32 em +96, PE32+ em +112.
        var pe = new Cabecalhos { DataDirStart = optionalHeaderStart + (isPe32Plus ? 112 : 96) };

        long sectionTableStart = optionalHeaderStart + sizeOfOptionalHeader;
        for (int i = 0; i < numberOfSections; i++)
        {
            long s = sectionTableStart + i * 40L;
            if (s + 40 > fs.Length) break;
            fs.Position = s + 12;
            uint virtualAddress = br.ReadUInt32();
            fs.Position = s + 16;
            uint sizeOfRawData = br.ReadUInt32();
            uint pointerToRawData = br.ReadUInt32();
            pe.Sections.Add((virtualAddress, sizeOfRawData, pointerToRawData));
        }
        return pe;
    }

    private static string? ReadAsciiZ(FileStream fs, long offset)
    {
        if (offset < 0 || offset >= fs.Length) return null;
        fs.Position = offset;
        var bytes = new List<byte>(32);
        int b;
        while ((b = fs.ReadByte()) > 0 && bytes.Count < 256)
            bytes.Add((byte)b);
        return bytes.Count == 0 ? null : System.Text.Encoding.ASCII.GetString(bytes.ToArray());
    }
}
