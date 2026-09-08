using System.Text;

namespace Dlss5.Core;

/// <summary>
/// Leitor do appcache\appinfo.vdf da Steam (formato binário), só para saber o TIPO de cada
/// app instalado: "Game", "Demo", "DLC", "Tool", "Music", "Application"... O appmanifest
/// não diz isso, e é a única forma confiável de separar jogo de trilha sonora, SDK,
/// servidor dedicado ou programa (Wallpaper Engine) sem chutar pelo nome.
///
/// Formato (versões 27, 28 e 29 — a 29, de 2024, move os nomes das chaves para uma
/// tabela de strings no fim do arquivo):
///   cabeçalho: magic u32, universe u32, [v29: offset i64 da tabela de strings]
///   entradas:  appid u32 (0 = fim), size u32 (bytes até o fim da entrada), infoState u32,
///              lastUpdated u32, picsToken u64, sha1[20], changeNumber u32,
///              [v28+: sha1 do blob, 20 bytes], KeyValues binário
///   KeyValues: tipo u8 (0 objeto, 1 string, 2 int32, 3 float, 4 ptr, 5 wstring, 6 cor,
///              7 uint64, 10 int64, 8 fim do objeto), chave (cstring, ou índice u32 na v29), valor
/// Só as entradas pedidas são decodificadas; as outras são puladas pelo "size". Qualquer
/// coisa fora do esperado devolve o que já foi lido: quem chama volta para as heurísticas.
/// </summary>
public static class SteamAppInfo
{
    public sealed record InfoDoApp(string? Tipo, string? Nome);

    public const uint MagicV27 = 0x07564427;
    public const uint MagicV28 = 0x07564428;
    public const uint MagicV29 = 0x07564429;

    public static IReadOnlyDictionary<uint, InfoDoApp> Ler(string caminho, IReadOnlySet<uint> appids)
    {
        var resultado = new Dictionary<uint, InfoDoApp>();
        if (appids.Count == 0 || !File.Exists(caminho)) return resultado;
        try
        {
            using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            Ler(fs, appids, resultado);
        }
        catch
        {
            // arquivo ilegível ou formato desconhecido: devolve o que deu
        }
        return resultado;
    }

    public static IReadOnlyDictionary<uint, InfoDoApp> Ler(Stream stream, IReadOnlySet<uint> appids)
    {
        var resultado = new Dictionary<uint, InfoDoApp>();
        try { Ler(stream, appids, resultado); } catch { }
        return resultado;
    }

    private static void Ler(Stream fs, IReadOnlySet<uint> appids, Dictionary<uint, InfoDoApp> resultado)
    {
        using var r = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
        uint magic = r.ReadUInt32();
        int versao = magic switch
        {
            MagicV27 => 27,
            MagicV28 => 28,
            MagicV29 => 29,
            _ => 0,
        };
        if (versao == 0) return;
        r.ReadUInt32(); // universe

        string[]? tabela = null;
        if (versao >= 29)
        {
            long offset = r.ReadInt64();
            long volta = fs.Position;
            fs.Seek(offset, SeekOrigin.Begin);
            uint quantidade = r.ReadUInt32();
            tabela = new string[quantidade];
            for (uint i = 0; i < quantidade; i++) tabela[i] = LerTexto(r);
            fs.Seek(volta, SeekOrigin.Begin);
        }

        int faltam = appids.Count;
        while (faltam > 0)
        {
            uint appid = r.ReadUInt32();
            if (appid == 0) break;
            uint tamanho = r.ReadUInt32();
            long fim = fs.Position + tamanho;

            if (appids.Contains(appid))
            {
                try
                {
                    r.ReadUInt32();   // infoState
                    r.ReadUInt32();   // lastUpdated
                    r.ReadUInt64();   // picsToken
                    r.ReadBytes(20);  // sha1
                    r.ReadUInt32();   // changeNumber
                    if (versao >= 28) r.ReadBytes(20);

                    var kv = LerObjeto(r, tabela);
                    var common = Secao(kv, "common");
                    resultado[appid] = new InfoDoApp(Texto(common, "type"), Texto(common, "name"));
                }
                catch
                {
                    // entrada com formato estranho: fica sem tipo e as heurísticas decidem
                }
                faltam--;
            }

            // O "size" manda: mesmo que o blob tenha sido lido até o fim, a próxima entrada começa aqui.
            if (fim > fs.Length) break;
            fs.Seek(fim, SeekOrigin.Begin);
        }
    }

    /// <summary>A seção pedida, esteja ela na raiz ou dentro de "appinfo".</summary>
    private static Dictionary<string, object?>? Secao(Dictionary<string, object?> raiz, string nome)
    {
        if (raiz.TryGetValue(nome, out var direto) && direto is Dictionary<string, object?> d1) return d1;
        if (raiz.TryGetValue("appinfo", out var appinfo) && appinfo is Dictionary<string, object?> a &&
            a.TryGetValue(nome, out var dentro) && dentro is Dictionary<string, object?> d2) return d2;
        return null;
    }

    private static string? Texto(Dictionary<string, object?>? secao, string chave) =>
        secao is not null && secao.TryGetValue(chave, out var v) && v is not null ? v.ToString() : null;

    private static Dictionary<string, object?> LerObjeto(BinaryReader r, string[]? tabela)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            byte tipo = r.ReadByte();
            if (tipo == 0x08 || tipo == 0x0B) return d;
            string chave = tabela is null ? LerTexto(r) : tabela[r.ReadUInt32()];
            object? valor = tipo switch
            {
                0x00 => LerObjeto(r, tabela),
                0x01 => LerTexto(r),
                0x02 => r.ReadInt32(),
                0x03 => r.ReadSingle(),
                0x04 => r.ReadInt32(),
                0x05 => LerTextoLargo(r),
                0x06 => r.ReadInt32(),
                0x07 => r.ReadUInt64(),
                0x0A => r.ReadInt64(),
                _ => throw new InvalidDataException($"Tipo de KeyValue desconhecido: 0x{tipo:X2}"),
            };
            d[chave] = valor;
        }
    }

    private static string LerTexto(BinaryReader r)
    {
        var bytes = new List<byte>(32);
        while (true)
        {
            byte b = r.ReadByte();
            if (b == 0) break;
            bytes.Add(b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static string LerTextoLargo(BinaryReader r)
    {
        var bytes = new List<byte>(32);
        while (true)
        {
            byte lo = r.ReadByte(), hi = r.ReadByte();
            if (lo == 0 && hi == 0) break;
            bytes.Add(lo);
            bytes.Add(hi);
        }
        return Encoding.Unicode.GetString(bytes.ToArray());
    }
}
