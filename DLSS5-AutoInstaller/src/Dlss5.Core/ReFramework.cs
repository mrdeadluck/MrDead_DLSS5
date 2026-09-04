using System.IO.Compression;
namespace Dlss5.Core;

/// <summary>
/// O REFramework (praydog) como hospedeiro do ReShade em jogo da RE Engine com proteção
/// anti-adulteração.
///
/// O RE9 (Resident Evil Requiem) recusa a injeção direta do ReShade: com o dxgi.dll na
/// pasta, o jogo abre a própria tela de erro 1 a 3 segundos depois de o runtime do
/// ReShade subir, sempre antes de criar qualquer DLSS — e renomear para dinput8.dll não
/// muda nada, porque o que é barrado é a DLL entrar pela tabela de importação do
/// executável, não o nome dela.
///
/// O REFramework passa por esse ponto e, depois de o jogo já estar de pé, carrega tudo o
/// que estiver em reframework\plugins\ com LoadLibrary. As mensagens do próprio binário
/// mostram a ordem: primeiro "[PluginLoader] Loaded {}" (a DLL já está no processo), e só
/// depois "{} has no reframework_plugin_required_version function, skipping..." — quem
/// não tem o export é pulado no aperto de mão, mas NÃO é descarregado. É essa brecha que
/// serve ao ReShade: o DllMain dele roda ali, tarde, e instala os ganchos de DXGI/D3D12
/// fora da janela em que a proteção olha.
///
/// Por isso o ReShade entra como reframework\plugins\ReShade64.dll (o nome que o próprio
/// injetor do ReShade usa) em vez de dxgi.dll, e a configuração que vale passa a ser o
/// ReShade64.ini ao lado dele — o ReShade procura o ini pelo nome do próprio módulo.
/// </summary>
public static class ReFramework
{
    /// <summary>Nome com que o REFramework é carregado pelo jogo.</summary>
    public const string Dinput8 = "dinput8.dll";

    /// <summary>Marcador de versão que acompanha a nightly.</summary>
    public const string RevisionFile = "reframework_revision.txt";

    /// <summary>
    /// Nome do ReShade dentro de plugins\. É o nome que o injetor oficial do ReShade usa,
    /// então é o caminho mais rodado para o ReShade carregado por LoadLibrary.
    /// </summary>
    public const string ReShadePlugin = "ReShade64.dll";

    /// <summary>Configuração que esse ReShade lê: o próprio nome, com extensão .ini.</summary>
    public const string ReShadeIni = "ReShade64.ini";

    /// <summary>Log que esse ReShade grava.</summary>
    public const string ReShadeLog = "ReShade64.log";

    /// <summary>
    /// O log do próprio REFramework. É o arquivo que responde tudo quando "nada acontece":
    /// se ele não existe, o REFramework nem carregou (o jogo não puxou o dinput8.dll da
    /// pasta); se existe, ele diz em "[PluginLoader] Loaded ..." se o ReShade entrou.
    /// </summary>
    public const string LogDoFramework = "re2_framework_log.txt";

    /// <summary>
    /// Onde o REFramework guarda tudo. Ele testa se dá para escrever na pasta do
    /// executável; conseguindo, é ali. Se NÃO conseguir, ele cai para
    /// %APPDATA%\REFramework\&lt;nome do exe&gt; — e aí a pasta reframework\plugins que vale
    /// é a de lá, não a da pasta do jogo. Instalar na pasta errada é instalar no vácuo.
    /// </summary>
    public static string PastaAppData(string? exePath)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var nome = Path.GetFileNameWithoutExtension(exePath) ?? "";
        return Path.Combine(appData, "REFramework", nome);
    }

    /// <summary>
    /// A pasta que o REFramework está realmente usando: a do jogo quando o log dele está
    /// lá, a de %APPDATA% quando o log está lá. Nulo quando não há log em lugar nenhum —
    /// aí o REFramework não rodou.
    /// </summary>
    public static string? PastaEmUso(string exeFolder, string? exePath)
    {
        if (File.Exists(Path.Combine(exeFolder, LogDoFramework))) return exeFolder;
        var appData = PastaAppData(exePath);
        return File.Exists(Path.Combine(appData, LogDoFramework)) ? appData : null;
    }

    /// <summary>O log diz que este plugin foi carregado?</summary>
    public static bool CarregouOPlugin(string? textoDoLog) =>
        textoDoLog is not null
        && textoDoLog.Contains("Loaded", StringComparison.OrdinalIgnoreCase)
        && textoDoLog.Contains(ReShadePlugin, StringComparison.OrdinalIgnoreCase);

    /// <summary>Pasta que o REFramework varre atrás de plugins.</summary>
    public static string PastaPlugins(string exeFolder) =>
        Path.Combine(exeFolder, "reframework", "plugins");

    public static string CaminhoDinput8(string exeFolder) => Path.Combine(exeFolder, Dinput8);

    public static string CaminhoRevisao(string exeFolder) => Path.Combine(exeFolder, RevisionFile);

    public static string CaminhoPlugin(string exeFolder) =>
        Path.Combine(PastaPlugins(exeFolder), ReShadePlugin);

    public static string CaminhoIni(string exeFolder) =>
        Path.Combine(PastaPlugins(exeFolder), ReShadeIni);

    public static string CaminhoLog(string exeFolder) =>
        Path.Combine(PastaPlugins(exeFolder), ReShadeLog);

    /// <summary>
    /// A RE Engine empacota os dados em re_chunk_*.pak ao lado do executável. Serve para
    /// não oferecer o REFramework a jogo que não é dela: fora da RE Engine ele não carrega
    /// e seria só uma DLL a mais na pasta.
    /// </summary>
    public static bool EhReEngine(string? gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder)) return false;
        try
        {
            return Directory
                .EnumerateFiles(gameFolder, "re_chunk_*.pak", SearchOption.TopDirectoryOnly)
                .Any();
        }
        catch { return false; }
    }

    /// <summary>
    /// Jogos que RECUSAM a DLL do ReShade na abertura (tela de erro do jogo 1 a 3 s depois
    /// de o runtime subir) e só abrem com o REFramework desarmando a checagem. Fora desta
    /// lista, a RE Engine aceita o ReShade direto — e o REFramework, que mexe na memória do
    /// jogo, é quem dispara a checagem quando não consegue desarmá-la. O Dragon's Dogma 2
    /// ensinou: com a caixa marcada ele entra, não acha os padrões desta versão
    /// ("Could not find conditional_jmp for DD2", "stack destroyer") e o jogo cai na tela
    /// inicial, com o crash registrado pelo próprio REFramework.
    /// </summary>
    /// <remarks>
    /// O Dragon's Dogma 2 entrou na lista em 04/09/2026: a atualização que o levou ao TDB 83
    /// (a engine do RE9) trouxe a mesma recusa — sem o REFramework o jogo abre a própria tela
    /// de erro 3 s depois de o runtime subir, antes de criar qualquer DLSS. E a nightly de
    /// 28/08 que o kit trazia caía DENTRO do REFramework nessa versão do jogo; a de 02/09
    /// ("Initial fix for DD2" e os fixes seguintes) é a que serve.
    /// </remarks>
    private static readonly string[] ExesQuePrecisam = { "re9.exe", "DD2.exe" };

    /// <summary>A nightly universal (um dinput8.dll para todos os jogos) do praydog.</summary>
    public const string UrlNightly = "https://github.com/praydog/REFramework-nightly/releases/latest/download/REFramework.zip";

    /// <summary>A revisão (hash do commit) que o kit traz; null sem o arquivo.</summary>
    public static string? RevisaoDoKit(string? pastaReFramework)
    {
        try
        {
            if (string.IsNullOrEmpty(pastaReFramework)) return null;
            var f = Path.Combine(pastaReFramework, RevisionFile);
            return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Baixa a nightly mais nova e troca o dinput8.dll (e o reframework_revision.txt) da pasta
    /// do kit, guardando o anterior como .dlss5prev. Devolve a revisão baixada. Confere que
    /// o que veio é um PE x64 com a assinatura do REFramework antes de trocar.
    /// </summary>
    public static async Task<string> BaixarParaOKit(
        string pastaReFramework, IProgress<string>? progresso = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(pastaReFramework);
        var dllNoKit = Path.Combine(pastaReFramework, Dinput8);
        var revNoKit = Path.Combine(pastaReFramework, RevisionFile);
        var zipTemp = dllNoKit + ".zip" + Propriedade.TempSuffix;
        var dllTemp = dllNoKit + Propriedade.TempSuffix;
        var revTemp = revNoKit + Propriedade.TempSuffix;
        try
        {
            progresso?.Report("Baixando a nightly do REFramework (~10 MB)...");
            using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var resposta = await http.GetAsync(UrlNightly, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resposta.EnsureSuccessStatusCode();
                await using var origem = await resposta.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var destino = new FileStream(zipTemp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
                await origem.CopyToAsync(destino, ct).ConfigureAwait(false);
            }

            progresso?.Report("Extraindo o dinput8.dll do zip...");
            string revisao = "";
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipTemp))
            {
                var dll = zip.Entries.FirstOrDefault(e => e.Name.Equals(Dinput8, StringComparison.OrdinalIgnoreCase))
                          ?? throw new InvalidOperationException($"O zip da nightly não traz {Dinput8}.");
                dll.ExtractToFile(dllTemp, overwrite: true);
                var rev = zip.Entries.FirstOrDefault(e => e.Name.Equals(RevisionFile, StringComparison.OrdinalIgnoreCase));
                if (rev is not null)
                {
                    rev.ExtractToFile(revTemp, overwrite: true);
                    revisao = File.ReadAllText(revTemp).Trim();
                }
            }

            if (PeFile.GetArchitecture(dllTemp) != PeArchitecture.X64)
                throw new InvalidOperationException("O dinput8.dll baixado não é um executável x64. Nada foi trocado.");
            if (!ContemTexto(dllTemp, "REFramework"))
                throw new InvalidOperationException("O dinput8.dll baixado não parece ser o REFramework. Nada foi trocado.");

            Trocar(dllNoKit, dllTemp);
            if (File.Exists(revTemp)) Trocar(revNoKit, revTemp);
            progresso?.Report($"REFramework do kit agora é a nightly {(revisao.Length >= 8 ? revisao[..8] : revisao)}.");
            return revisao;
        }
        finally
        {
            foreach (var t in new[] { zipTemp, dllTemp, revTemp })
                try { if (File.Exists(t)) File.Delete(t); } catch { }
        }

        static void Trocar(string alvo, string novo)
        {
            if (File.Exists(alvo))
            {
                var anterior = alvo + Propriedade.PrevSuffix;
                if (File.Exists(anterior)) File.Delete(anterior);
                File.Move(alvo, anterior);
            }
            File.Move(novo, alvo);
        }

        static bool ContemTexto(string path, string texto) =>
            ApiDetector.ScanForMarkers(path, new[] { texto }, 64L * 1024 * 1024).Contains(texto);
    }

    /// <summary>O jogo precisa do REFramework para o ReShade entrar.</summary>
    public static bool PrecisaDoBypass(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        // O nome do arquivo, aceitando os dois separadores: os testes rodam fora do Windows.
        var nome = exePath.Split('\\', '/')[^1];
        return ExesQuePrecisam.Contains(nome, StringComparer.OrdinalIgnoreCase);
    }

    public const string QuandoMarcar =
        "Marque só em jogo da RE Engine que abre a própria tela de erro logo depois do ReShade subir " +
        "(Resident Evil Requiem e, desde a atualização de 2026, Dragon's Dogma 2). RE4, RE Village e " +
        "Monster Hunter Wilds aceitam o ReShade direto — neles o REFramework, quando não consegue desarmar " +
        "a checagem desta versão do jogo, é quem derruba o jogo. Se o jogo cair COM o REFramework, o " +
        "remédio é a nightly mais nova dele (botão \"Baixar REFramework nightly\" na verificação).";

    /// <summary>
    /// O dinput8.dll da pasta é o REFramework que este kit traz? Byte a byte, a mesma
    /// prova usada no transplante: sem ela a desinstalação apagaria um REFramework que o
    /// usuário instalou por conta própria, ou outro mod que use o mesmo nome de arquivo.
    /// </summary>
    public static bool EhDoKit(string? arquivoNoJogo, string? arquivoDoKit) =>
        TransplanteDlss.EhDoKit(arquivoNoJogo, arquivoDoKit);
}
