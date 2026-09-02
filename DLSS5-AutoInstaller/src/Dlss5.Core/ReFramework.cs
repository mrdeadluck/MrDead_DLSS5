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
    public static string PastaAppData(string exePath)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var nome = Path.GetFileNameWithoutExtension(exePath);
        return Path.Combine(appData, "REFramework", nome);
    }

    /// <summary>
    /// A pasta que o REFramework está realmente usando: a do jogo quando o log dele está
    /// lá, a de %APPDATA% quando o log está lá. Nulo quando não há log em lugar nenhum —
    /// aí o REFramework não rodou.
    /// </summary>
    public static string? PastaEmUso(string exeFolder, string exePath)
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
    /// O dinput8.dll da pasta é o REFramework que este kit traz? Byte a byte, a mesma
    /// prova usada no transplante: sem ela a desinstalação apagaria um REFramework que o
    /// usuário instalou por conta própria, ou outro mod que use o mesmo nome de arquivo.
    /// </summary>
    public static bool EhDoKit(string? arquivoNoJogo, string? arquivoDoKit) =>
        TransplanteDlss.EhDoKit(arquivoNoJogo, arquivoDoKit);
}
