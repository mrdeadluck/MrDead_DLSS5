namespace Dlss5.Core;

/// <summary>
/// Faxina: desfazer a instalação sem depender de manifesto, de detecção ou de nada
/// que a instalação tenha deixado gravado.
///
/// A reversão normal parte do manifesto e é a mais precisa. Só que ela falha exatamente
/// quando mais faz falta: manifesto apagado, instalação feita apontando outro executável,
/// pasta trocada, reversão interrompida. Foi o que aconteceu num teste real — sobrou o
/// dxgi.dll na pasta, o overlay do ReShade continuou aparecendo, o DLSS do jogo travava,
/// e não havia botão nenhum que resolvesse: teve que ser na mão, arquivo por arquivo.
///
/// Aqui a busca é pelo nome do arquivo, na pasta do jogo inteira, e o critério para
/// apagar é conservador: só sai o que não tem como ser do jogo.
/// </summary>
public sealed partial class InstallerEngine
{
    /// <summary>
    /// Nomes que só este programa põe numa pasta de jogo. Nenhum jogo traz nada disso.
    /// </summary>
    private static readonly string[] SoNossos =
    {
        "renodx-dlss5.addon64", "dlss5-feed.addon64", "dlss5-feed.addon32",
        "dlss5-feed-host64.exe", "dlss5-feed.cfg", "dlss5-feed.log", "dlss5-feed-host.log",
        "nvngx_dlssnr.dll",
        "ReShade.ini", "ReShade.log", "ReShadePreset.ini",
        "ReShade64.json", "ReShade32.json", "ReShade64_XR.json", "ReShade32_XR.json",
        InstallManifest.FileName,
    };

    /// <summary>
    /// dgVoodoo é dele mesmo: existe jogo antigo que já vem com o dgVoodoo instalado de
    /// fábrica, e apagar a configuração dele quebraria o jogo. Estes só saem se houver
    /// instalação nossa na mesma pasta ou na pasta acima (na engine Source o dgVoodoo vai
    /// em bin\ e o ReShade fica um nível acima).
    /// </summary>
    private static readonly string[] DgVoodooComEscolta =
    {
        "dgVoodoo.conf", "dgVoodooCpl.exe",
    };

    /// <summary>
    /// Nomes genéricos demais para sair só pelo nome: dxgi.dll e D3D9.dll também são
    /// nomes de DLL da Microsoft, e jogo antigo às vezes traz o próprio wrapper de D3D9.
    /// Só sai se o texto dentro do arquivo provar de onde ele veio.
    /// </summary>
    private static readonly (string Nome, string Prova)[] PrecisamDeProva =
    {
        // O dxgi.dll sai só com a prova, sem escolta: o caso clássico é justamente uma
        // reversão que falhou e deixou SÓ ele para trás — o overlay continua aparecendo
        // no jogo e não há mais nada na pasta que sirva de escolta.
        ("dxgi.dll", "ReShade"),
    };

    private static readonly (string Nome, string Prova)[] PrecisamDeProvaEEscolta =
    {
        ("D3D9.dll", "dgVoodoo"),
    };

    /// <summary>
    /// O kit tem um nvngx_dlss.dll e muitos jogos também. Este só sai quando há, na mesma
    /// pasta, um arquivo que é inconfundivelmente nosso — senão a "limpeza" apagaria o
    /// DLSS do próprio jogo, que é o erro que já custou caro uma vez.
    /// </summary>
    private const string AmbiguoDoJogo = "nvngx_dlss.dll";

    private static readonly string[] ProvasDoKit =
    {
        "nvngx_dlssnr.dll", "renodx-dlss5.addon64", "dlss5-feed.addon64",
        "dlss5-feed.addon32", "dlss5-feed-host64.exe",
    };

    private static readonly string[] PastasNossas = { "host64", "reshade-shaders" };

    private const long OrcamentoProva = 96L * 1024 * 1024;

    /// <summary>
    /// Tudo que este programa possa ter deixado em qualquer subpasta do jogo — arquivos,
    /// pastas nossas e backups .dlss5bak ainda por devolver. Só olha, não mexe.
    /// </summary>
    public IReadOnlyList<string> EncontrarInstalacao(string? gameFolder)
    {
        var achados = new List<string>();
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder)) return achados;

        foreach (var pasta in PastasParaVarrer(gameFolder))
        {
            achados.AddRange(NossosArquivosEm(pasta));

            foreach (var nome in PastasNossas)
            {
                var alvo = Path.Combine(pasta, nome);
                if (Directory.Exists(alvo) && PastaEhNossa(alvo, pasta))
                    achados.Add(alvo + Path.DirectorySeparatorChar);
            }
        }

        try
        {
            achados.AddRange(Directory.EnumerateFiles(gameFolder, "*" + BackupSuffix, SearchOption.AllDirectories));
        }
        catch
        {
            // sem permissão de leitura em alguma subpasta: o que já foi listado basta
        }

        return achados.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
    }

    /// <summary>
    /// Desfaz a instalação sem manifesto: devolve todo backup ao lugar e apaga o que é
    /// nosso. Devolve a lista do que resistiu (quase sempre arquivo em uso).
    /// </summary>
    public IReadOnlyList<string> LimpezaTotal(string? gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
        {
            _log("Pasta do jogo não encontrada.");
            return Array.Empty<string>();
        }

        _log($"Faxina completa em {gameFolder}");

        // 1. Backups primeiro. Um arquivo devolvido volta a ser do JOGO — e por isso
        //    entra na lista de intocáveis do passo 3, mesmo que o nome dele seja um dos
        //    nossos (é o caso do nvngx_dlss.dll que o jogo já tinha antes).
        var restaurados = new HashSet<string>(
            RestaurarBackupsOrfaos(gameFolder), StringComparer.OrdinalIgnoreCase);

        // 2. Levantar tudo ANTES de apagar qualquer coisa. A prova de que host64\ e
        //    reshade-shaders\ são nossas está justamente nos arquivos que o passo 3
        //    remove; decidir depois deixaria as duas pastas para trás.
        var arquivos = new List<string>();
        var pastas = new List<string>();
        foreach (var pasta in PastasParaVarrer(gameFolder).ToList())
        {
            arquivos.AddRange(NossosArquivosEm(pasta));
            foreach (var nome in PastasNossas)
            {
                var alvo = Path.Combine(pasta, nome);
                if (Directory.Exists(alvo) && PastaEhNossa(alvo, pasta)) pastas.Add(alvo);
            }
        }

        // 3. Arquivos.
        int apagados = 0;
        foreach (var arquivo in arquivos)
        {
            if (restaurados.Contains(arquivo))
            {
                _log($"Mantido (é do jogo, acabou de ser restaurado): {arquivo}");
                continue;
            }
            if (Apagar(arquivo)) apagados++;
        }

        // 4. Pastas nossas por inteiro, das mais fundas para as mais rasas.
        foreach (var alvo in pastas.OrderByDescending(d => d.Length))
        {
            if (!Directory.Exists(alvo)) continue;
            try
            {
                Directory.Delete(alvo, recursive: true);
                apagados++;
                _log($"Pasta removida: {alvo}");
            }
            catch (Exception ex)
            {
                _log($"Aviso: {alvo}: {ex.Message}");
            }
        }

        _log($"Faxina: {apagados} item(ns) removido(s).");

        var sobras = EncontrarInstalacao(gameFolder)
            .Concat(pastas.Where(Directory.Exists).Select(d => d + Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        if (sobras.Count > 0)
        {
            _log("");
            _log("ATENÇÃO: estes não saíram:");
            foreach (var s in sobras) _log("   " + s);
            _log("Quase sempre é arquivo em uso: feche o jogo E a Steam e repita.");
        }
        else
        {
            _log("Conferido: não sobrou nada deste programa na pasta do jogo.");
        }
        return sobras;
    }

    private bool Apagar(string arquivo)
    {
        try
        {
            if (!File.Exists(arquivo)) return false;
            File.Delete(arquivo);
            _log($"Apagado: {arquivo}");
            return true;
        }
        catch (Exception ex)
        {
            _log($"Aviso: {arquivo}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Arquivos desta pasta (só nela) que dá para afirmar que são nossos.</summary>
    private static List<string> NossosArquivosEm(string pasta)
    {
        var nossos = new List<string>();

        foreach (var nome in SoNossos)
        {
            var caminho = Path.Combine(pasta, nome);
            if (File.Exists(caminho)) nossos.Add(caminho);
        }

        foreach (var (nome, prova) in PrecisamDeProva)
        {
            var caminho = Path.Combine(pasta, nome);
            if (File.Exists(caminho) && ContemTexto(caminho, prova)) nossos.Add(caminho);
        }

        bool comEscolta = TemInstalacaoNossaPorPerto(pasta);

        foreach (var (nome, prova) in PrecisamDeProvaEEscolta)
        {
            var caminho = Path.Combine(pasta, nome);
            if (comEscolta && File.Exists(caminho) && ContemTexto(caminho, prova)) nossos.Add(caminho);
        }

        if (comEscolta)
            foreach (var nome in DgVoodooComEscolta)
            {
                var caminho = Path.Combine(pasta, nome);
                if (File.Exists(caminho)) nossos.Add(caminho);
            }

        var ambiguo = Path.Combine(pasta, AmbiguoDoJogo);
        if (File.Exists(ambiguo) &&
            ProvasDoKit.Any(p => File.Exists(Path.Combine(pasta, p))))
            nossos.Add(ambiguo);

        return nossos;
    }

    /// <summary>Há sinal de instalação nossa nesta pasta ou na de cima?</summary>
    private static bool TemInstalacaoNossaPorPerto(string pasta)
    {
        if (TemSinalNosso(pasta)) return true;
        try
        {
            var pai = Directory.GetParent(pasta)?.FullName;
            return pai is not null && TemSinalNosso(pai);
        }
        catch
        {
            return false;
        }
    }

    private static bool TemSinalNosso(string pasta) =>
        ProvasDoKit.Any(p => File.Exists(Path.Combine(pasta, p)))
        || File.Exists(Path.Combine(pasta, "ReShade.ini"))
        || File.Exists(Path.Combine(pasta, InstallManifest.FileName));

    /// <summary>host64\ e reshade-shaders\ só são nossas se houver instalação nossa junto.</summary>
    private static bool PastaEhNossa(string pastaAlvo, string pastaPai) =>
        ProvasDoKit.Any(p => File.Exists(Path.Combine(pastaAlvo, p))) || TemSinalNosso(pastaPai);

    private static bool ContemTexto(string caminho, string texto) =>
        ApiDetector.ScanForMarkers(caminho, new[] { texto }, OrcamentoProva).Contains(texto);

    /// <summary>A pasta do jogo e todas as subpastas dela.</summary>
    private static IEnumerable<string> PastasParaVarrer(string gameFolder)
    {
        yield return gameFolder;

        IEnumerable<string> subpastas;
        try { subpastas = Directory.EnumerateDirectories(gameFolder, "*", SearchOption.AllDirectories); }
        catch { yield break; }

        using var e = subpastas.GetEnumerator();
        while (true)
        {
            // Uma subpasta ilegível não pode derrubar a varredura inteira.
            try { if (!e.MoveNext()) break; }
            catch { break; }
            yield return e.Current;
        }
    }
}
