using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Dlss5.Core;

/// <summary>Loja ou launcher que registrou o jogo no computador.</summary>
public enum LojaDeJogos
{
    Steam,
    Epic,
    Gog,
    Ea,
    Ubisoft,
    Xbox,
    BattleNet,
    Rockstar,
    Amazon,
}

/// <summary>
/// Um jogo instalado, do jeito que a loja o registrou: nome de exibição, pasta raiz (o que
/// a tela inicial precisa) e, quando a loja diz, o executável de lançamento. O executável
/// REAL continua sendo decidido pelo <see cref="GameDetector"/> ao inspecionar a pasta —
/// aqui ele é só uma pista para o usuário reconhecer o jogo na lista.
/// </summary>
public sealed record JogoInstalado(
    string Nome,
    string Pasta,
    LojaDeJogos Loja,
    string? ExecutavelIndicado = null,
    string? Id = null)
{
    public string NomeDaLoja => JogosInstalados.NomeDaLoja(Loja);
}

/// <summary>Leitura do registro do Windows atrás de uma interface, para os testes rodarem sem registro.</summary>
public interface ILeitorDeRegistro
{
    /// <summary>Nomes das subchaves de "HKLM\..." ou "HKCU\..."; vazio se a chave não existe.</summary>
    IReadOnlyList<string> SubChaves(string chave);

    /// <summary>Valor de uma chave, como texto; nulo se não existe.</summary>
    string? Valor(string chave, string nome);
}

/// <summary>Registro vazio: fora do Windows (testes) nada é lido.</summary>
public sealed class RegistroVazio : ILeitorDeRegistro
{
    public IReadOnlyList<string> SubChaves(string chave) => Array.Empty<string>();
    public string? Valor(string chave, string nome) => null;
}

/// <summary>O registro de verdade, sempre pela visão de 64 bits (as chaves WOW6432Node são escritas por extenso).</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistroDoWindows : ILeitorDeRegistro
{
    public IReadOnlyList<string> SubChaves(string chave)
    {
        try
        {
            using var k = Abrir(chave);
            return k?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    public string? Valor(string chave, string nome)
    {
        try
        {
            using var k = Abrir(chave);
            return k?.GetValue(nome)?.ToString();
        }
        catch { return null; }
    }

    private static Microsoft.Win32.RegistryKey? Abrir(string chave)
    {
        int corte = chave.IndexOf('\\');
        if (corte <= 0) return null;
        var raiz = chave[..corte].ToUpperInvariant() switch
        {
            "HKLM" => Microsoft.Win32.RegistryHive.LocalMachine,
            "HKCU" => Microsoft.Win32.RegistryHive.CurrentUser,
            _ => (Microsoft.Win32.RegistryHive?)null,
        };
        if (raiz is null) return null;
        using var basica = Microsoft.Win32.RegistryKey.OpenBaseKey(raiz.Value, Microsoft.Win32.RegistryView.Registry64);
        return basica.OpenSubKey(chave[(corte + 1)..]);
    }
}

/// <summary>
/// Onde procurar. <see cref="DoSistema"/> lê a máquina; os testes apontam pastas
/// temporárias e um registro falso.
/// </summary>
public sealed class AmbienteDeLojas
{
    /// <summary>Pasta do cliente da Steam (onde ficam steamapps\ e appcache\).</summary>
    public string? RaizDaSteam { get; init; }

    /// <summary>%ProgramData% — manifestos da Epic e do Origin.</summary>
    public string? ProgramData { get; init; }

    /// <summary>Raízes das unidades fixas — a pasta XboxGames mora na raiz de uma delas.</summary>
    public IReadOnlyList<string> RaizesDeUnidades { get; init; } = Array.Empty<string>();

    public ILeitorDeRegistro Registro { get; init; } = new RegistroVazio();

    public static AmbienteDeLojas DoSistema()
    {
        var raizes = new List<string>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.DriveType == DriveType.Fixed && d.IsReady) raizes.Add(d.RootDirectory.FullName); }
                catch { /* unidade que some no meio da leitura */ }
            }
        }
        catch { }

        string? programData = null;
        try { programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); }
        catch { }

        return new AmbienteDeLojas
        {
            RaizDaSteam = SteamGame.RaizDaSteam(),
            ProgramData = string.IsNullOrWhiteSpace(programData) ? null : programData,
            RaizesDeUnidades = raizes,
            Registro = OperatingSystem.IsWindows() ? new RegistroDoWindows() : new RegistroVazio(),
        };
    }
}

/// <summary>
/// Descobre os jogos instalados no computador lendo o que as LOJAS registraram — nunca
/// varrendo o disco atrás de .exe. É isso que garante que só jogo entra na lista: um
/// executável solto em Program Files pode ser qualquer coisa; um item da biblioteca da
/// Steam, da Epic ou da GOG é um jogo (ou algo que a própria loja classifica como DLC,
/// ferramenta, trilha sonora... e que aqui é descartado).
///
/// Fontes, e como cada uma separa jogo do resto:
///   Steam       steamapps\libraryfolders.vdf + appmanifest_*.acf de cada biblioteca; o tipo
///               vem do appcache\appinfo.vdf ("Game"/"Demo" entram; "DLC", "Tool", "Music",
///               "Application" não). Sem o appinfo, vale uma lista de nomes conhecidos.
///   Epic        %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item: só quem tem
///               "games" em AppCategories e é o app principal (MainGameAppName == AppName).
///   GOG         HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\*: DLC tem dependsOn preenchido.
///   EA/Origin   Chaves "Install Dir" sob Electronic Arts/EA Games que tenham
///               __Installer\installerdata.xml na pasta, e os .mfst do Origin.
///   Ubisoft     HKLM\...\Ubisoft\Launcher\Installs\<id>\InstallDir; o nome vem do Uninstall.
///   Rockstar    HKLM\...\Rockstar Games\<Jogo>\InstallFolder, menos o Launcher e o Social Club.
///   Xbox        <unidade>\XboxGames\<Jogo>\Content\MicrosoftGame.config (ou a pasta que o
///               .GamingRoot da unidade indicar).
///   Battle.net  Entradas de desinstalação do "Blizzard Uninstaller", menos o próprio cliente.
///   Amazon      Entradas de desinstalação "AmazonGames/<Jogo>" do usuário.
/// Por fim, toda pasta precisa ter algum .exe até dois níveis abaixo — é o que tira da
/// lista uma trilha sonora ou um pacote de arte que a loja instalou em steamapps\common.
/// </summary>
public static class JogosInstalados
{
    /// <summary>Frase para a interface: quais lojas são lidas.</summary>
    public const string LojasCobertas =
        "Steam, Epic Games, GOG, EA app/Origin, Ubisoft Connect, Xbox, Battle.net, Rockstar e Amazon Games";

    public static string NomeDaLoja(LojaDeJogos loja) => loja switch
    {
        LojaDeJogos.Steam => "Steam",
        LojaDeJogos.Epic => "Epic Games",
        LojaDeJogos.Gog => "GOG",
        LojaDeJogos.Ea => "EA app / Origin",
        LojaDeJogos.Ubisoft => "Ubisoft Connect",
        LojaDeJogos.Xbox => "Xbox / Game Pass",
        LojaDeJogos.BattleNet => "Battle.net",
        LojaDeJogos.Rockstar => "Rockstar Games",
        LojaDeJogos.Amazon => "Amazon Games",
        _ => loja.ToString(),
    };

    /// <summary>Lê as lojas deste computador. Nunca lança: uma loja ilegível é pulada e anotada no diário.</summary>
    public static IReadOnlyList<JogoInstalado> Procurar(Diario? diario = null) =>
        Procurar(AmbienteDeLojas.DoSistema(), diario);

    public static IReadOnlyList<JogoInstalado> Procurar(AmbienteDeLojas ambiente, Diario? diario = null)
    {
        var achados = new List<JogoInstalado>();
        var descartes = new List<string>();

        void Fonte(string nome, Func<List<JogoInstalado>> ler)
        {
            int antes = achados.Count;
            try { achados.AddRange(ler()); }
            catch (Exception ex)
            {
                diario?.Aviso($"Jogos instalados: a leitura da {nome} falhou e foi ignorada — {ex.Message}");
            }
            diario?.Tecnico($"Jogos instalados: {nome} — {achados.Count - antes} registro(s)");
        }

        // A ordem é a prioridade quando a mesma pasta aparece em duas fontes.
        Fonte("Steam", () => Steam(ambiente, descartes));
        Fonte("Epic Games", () => Epic(ambiente, descartes));
        Fonte("GOG", () => Gog(ambiente, descartes));
        Fonte("Ubisoft Connect", () => Ubisoft(ambiente, descartes));
        Fonte("Rockstar Games", () => Rockstar(ambiente, descartes));
        Fonte("EA app / Origin", () => Ea(ambiente, descartes));
        Fonte("Xbox", () => Xbox(ambiente, descartes));
        Fonte("Battle.net", () => BattleNet(ambiente, descartes));
        Fonte("Amazon Games", () => Amazon(ambiente, descartes));

        // Pasta sem executável algum não é jogo — é trilha sonora, pacote de arte, SDK.
        var comExe = new List<JogoInstalado>(achados.Count);
        foreach (var j in achados)
        {
            if (ExecutavelIndicadoExiste(j) || TemExecutavel(j.Pasta)) comExe.Add(j);
            else descartes.Add($"{j.NomeDaLoja}: {j.Nome} — nenhum .exe na pasta ({j.Pasta})");
        }

        foreach (var d in descartes) diario?.Tecnico("Jogos instalados: fora da lista — " + d);
        var lista = Consolidar(comExe);
        diario?.Info($"Jogos instalados encontrados: {lista.Count} (de {achados.Count} registro(s) das lojas).");
        return lista;
    }

    /// <summary>Uma pasta só entra uma vez (a primeira fonte vence) e a lista sai em ordem alfabética.</summary>
    public static IReadOnlyList<JogoInstalado> Consolidar(IEnumerable<JogoInstalado> jogos)
    {
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lista = new List<JogoInstalado>();
        foreach (var j in jogos)
        {
            var pasta = Normalizar(j.Pasta);
            if (pasta.Length == 0 || !vistas.Add(pasta)) continue;
            lista.Add(j with { Pasta = pasta, Nome = string.IsNullOrWhiteSpace(j.Nome) ? Path.GetFileName(pasta) : j.Nome.Trim() });
        }
        lista.Sort((a, b) => string.Compare(a.Nome, b.Nome, StringComparison.CurrentCultureIgnoreCase));
        return lista;
    }

    private static string Normalizar(string pasta)
    {
        try
        {
            var completa = Path.GetFullPath(pasta.Trim());
            var semBarra = completa.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // A raiz de uma unidade ("C:\") não pode perder a barra.
            return semBarra.Length == 0 || semBarra.EndsWith(':') ? completa : semBarra;
        }
        catch { return pasta.Trim(); }
    }

    /// <summary>
    /// Há algum .exe na pasta ou até <paramref name="profundidadeMaxima"/> níveis abaixo?
    /// Busca em largura com teto de pastas visitadas: a pergunta é "isto é um jogo?", não
    /// "onde está o exe?" — quem responde a segunda é o GameDetector, depois.
    /// </summary>
    public static bool TemExecutavel(string pasta, int profundidadeMaxima = 2, int limiteDePastas = 300)
    {
        var fila = new Queue<(string Dir, int Nivel)>();
        fila.Enqueue((pasta, 0));
        int visitadas = 0;
        while (fila.Count > 0 && visitadas < limiteDePastas)
        {
            var (dir, nivel) = fila.Dequeue();
            visitadas++;
            try
            {
                if (Directory.EnumerateFiles(dir, "*.exe").Any()) return true;
                if (nivel < profundidadeMaxima)
                    foreach (var sub in Directory.EnumerateDirectories(dir)) fila.Enqueue((sub, nivel + 1));
            }
            catch { /* sem permissão ou pasta que sumiu: segue para a próxima */ }
        }
        return false;
    }

    private static bool ExecutavelIndicadoExiste(JogoInstalado j)
    {
        if (string.IsNullOrWhiteSpace(j.ExecutavelIndicado)) return false;
        try
        {
            var caminho = Path.IsPathRooted(j.ExecutavelIndicado)
                ? j.ExecutavelIndicado
                : Path.Combine(j.Pasta, j.ExecutavelIndicado.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            return File.Exists(caminho);
        }
        catch { return false; }
    }

    /// <summary>Exe como caminho relativo à pasta do jogo, quando ele mora dentro dela.</summary>
    public static string? RelativoSePossivel(string? exe, string pasta)
    {
        if (string.IsNullOrWhiteSpace(exe)) return null;
        exe = exe.Trim().Trim('"');
        var raiz = pasta.TrimEnd('\\', '/');
        if (exe.StartsWith(raiz, StringComparison.OrdinalIgnoreCase) && exe.Length > raiz.Length)
            return exe[raiz.Length..].TrimStart('\\', '/');
        return exe;
    }

    // ------------------------------------------------------------------ Steam

    /// <summary>Tipos do appinfo.vdf que são jogo de verdade (jogável).</summary>
    public static bool EhTipoDeJogoDaSteam(string? tipo) =>
        tipo is not null && tipo.Trim().ToLowerInvariant() is "game" or "demo" or "beta";

    private static readonly string[] NomesDeNaoJogoDaSteam =
    {
        "steamworks common redistributables", "steamworks shared", "steam linux runtime", "steamvr",
    };

    /// <summary>Palavras inteiras (\b): "OST" derruba "Portal 2 OST", nunca "Ghost of Tsushima" nem "Ostriv".</summary>
    private static readonly Regex PalavrasDeNaoJogoDaSteam = new(
        @"\b(dedicated server|soundtrack|ost|redistributables?|sdk|mod tools|workshop tools|server tools?|authoring tools)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Sem o appinfo.vdf, o que dá para descartar pelo nome (e pelo appid dos redistribuíveis).</summary>
    public static bool PareceNaoJogoDaSteam(string? nome, uint appid)
    {
        if (appid == 228980) return true; // Steamworks Common Redistributables
        if (string.IsNullOrWhiteSpace(nome)) return false;
        var n = nome.Trim().ToLowerInvariant();
        if (n.StartsWith("proton", StringComparison.Ordinal)) return true;
        if (NomesDeNaoJogoDaSteam.Any(x => n.StartsWith(x, StringComparison.Ordinal))) return true;
        return PalavrasDeNaoJogoDaSteam.IsMatch(n);
    }

    /// <summary>
    /// As bibliotecas da Steam: a própria pasta do cliente e cada "path" do
    /// libraryfolders.vdf (formato novo, "path" dentro de cada bloco numerado; e o antigo,
    /// "1" "D:\\SteamLibrary" direto). Os caminhos vêm com a barra dobrada.
    /// </summary>
    public static List<string> BibliotecasDaSteam(string raiz)
    {
        var libs = new List<string>();
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Adicionar(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            string norm;
            try { norm = Path.GetFullPath(p.Trim()).TrimEnd('\\', '/'); } catch { return; }
            if (!Directory.Exists(norm)) return;
            if (vistas.Add(norm)) libs.Add(norm);
        }

        Adicionar(raiz);
        foreach (var arquivo in new[]
                 {
                     Path.Combine(raiz, "steamapps", "libraryfolders.vdf"),
                     Path.Combine(raiz, "config", "libraryfolders.vdf"),
                 })
        {
            if (!File.Exists(arquivo)) continue;
            string texto;
            try { texto = File.ReadAllText(arquivo); } catch { continue; }
            foreach (Match m in Regex.Matches(texto, "\"(?:path|\\d+)\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase))
            {
                var p = m.Groups[1].Value.Replace("\\\\", "\\");
                // "apps" { "228980" "1234" } também casa: valor numérico não é caminho.
                if (!p.Contains('\\') && !p.Contains('/')) continue;
                Adicionar(p);
            }
        }
        return libs;
    }

    private static List<JogoInstalado> Steam(AmbienteDeLojas amb, List<string> descartes)
    {
        var lista = new List<JogoInstalado>();
        var raiz = amb.RaizDaSteam;
        if (string.IsNullOrWhiteSpace(raiz) || !Directory.Exists(raiz)) return lista;

        var entradas = new List<(uint AppId, string? Nome, string InstallDir, string Pasta, int? Flags)>();
        foreach (var lib in BibliotecasDaSteam(raiz))
        {
            var steamapps = Path.Combine(lib, "steamapps");
            IEnumerable<string> manifestos;
            try { manifestos = Directory.EnumerateFiles(steamapps, "appmanifest_*.acf").ToList(); }
            catch { continue; }

            foreach (var manifesto in manifestos)
            {
                string texto;
                try { texto = File.ReadAllText(manifesto); } catch { continue; }

                var installDir = SteamGame.Valor(texto, "installdir");
                if (string.IsNullOrWhiteSpace(installDir)) continue;
                var appidTexto = SteamGame.Valor(texto, "appid");
                if (appidTexto is null)
                {
                    var m = Regex.Match(Path.GetFileName(manifesto), @"appmanifest_(\d+)\.acf", RegexOptions.IgnoreCase);
                    if (m.Success) appidTexto = m.Groups[1].Value;
                }
                if (!uint.TryParse(appidTexto, out var appid)) continue;
                int? flags = int.TryParse(SteamGame.Valor(texto, "StateFlags"), out var f) ? f : null;
                entradas.Add((appid, SteamGame.Valor(texto, "name"), installDir, Path.Combine(steamapps, "common", installDir), flags));
            }
        }
        if (entradas.Count == 0) return lista;

        // Uma leitura só do appinfo.vdf, pedindo apenas os apps instalados.
        var tipos = SteamAppInfo.Ler(Path.Combine(raiz, "appcache", "appinfo.vdf"),
            entradas.Select(e => e.AppId).ToHashSet());

        foreach (var e in entradas)
        {
            var nome = string.IsNullOrWhiteSpace(e.Nome) ? e.InstallDir : e.Nome.Trim();
            // StateFlags: bit 4 = totalmente instalado. Sem ele é download agendado ou pela metade.
            if (e.Flags is { } fl && fl != 0 && (fl & 4) == 0)
            {
                descartes.Add($"Steam: {nome} — não está instalado por completo (StateFlags {fl})");
                continue;
            }
            if (!Directory.Exists(e.Pasta))
            {
                descartes.Add($"Steam: {nome} — pasta ausente ({e.Pasta})");
                continue;
            }
            if (tipos.TryGetValue(e.AppId, out var info) && !string.IsNullOrWhiteSpace(info.Tipo))
            {
                if (!EhTipoDeJogoDaSteam(info.Tipo))
                {
                    descartes.Add($"Steam: {nome} — tipo \"{info.Tipo}\" no appinfo.vdf");
                    continue;
                }
            }
            else if (PareceNaoJogoDaSteam(nome, e.AppId))
            {
                descartes.Add($"Steam: {nome} — nome de ferramenta/redistribuível/trilha");
                continue;
            }
            lista.Add(new JogoInstalado(nome, e.Pasta, LojaDeJogos.Steam, null, e.AppId.ToString()));
        }
        return lista;
    }

    // ------------------------------------------------------------------- Epic

    private static List<JogoInstalado> Epic(AmbienteDeLojas amb, List<string> descartes)
    {
        var lista = new List<JogoInstalado>();
        if (string.IsNullOrWhiteSpace(amb.ProgramData)) return lista;
        var pasta = Path.Combine(amb.ProgramData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(pasta)) return lista;

        foreach (var arquivo in Directory.EnumerateFiles(pasta, "*.item"))
        {
            try
            {
                var jogo = LerManifestoDaEpic(File.ReadAllText(arquivo), descartes);
                if (jogo is not null) lista.Add(jogo);
            }
            catch (Exception ex)
            {
                descartes.Add($"Epic: {Path.GetFileName(arquivo)} ilegível — {ex.Message}");
            }
        }
        return lista;
    }

    /// <summary>
    /// Um .item da Epic. Entra só o app principal de um jogo: DLC tem MainGameAppName
    /// diferente do AppName; a Unreal Engine e os plugins não têm "games" em AppCategories.
    /// </summary>
    public static JogoInstalado? LerManifestoDaEpic(string json, List<string> descartes)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string? S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        bool B(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;

        var nome = S("DisplayName");
        var local = S("InstallLocation");
        if (string.IsNullOrWhiteSpace(local)) return null;
        var rotulo = nome ?? Path.GetFileName(local.TrimEnd('\\', '/'));

        if (B("bIsIncompleteInstall"))
        {
            descartes.Add($"Epic: {rotulo} — instalação incompleta");
            return null;
        }
        var appName = S("AppName");
        var principal = S("MainGameAppName");
        if (!string.IsNullOrEmpty(principal) && !string.IsNullOrEmpty(appName) &&
            !principal.Equals(appName, StringComparison.OrdinalIgnoreCase))
        {
            descartes.Add($"Epic: {rotulo} — DLC/complemento de {principal}");
            return null;
        }
        if (r.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array)
        {
            var categorias = cats.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.String)
                .Select(c => c.GetString() ?? "")
                .ToList();
            if (categorias.Count > 0 && !categorias.Any(c => c.Equals("games", StringComparison.OrdinalIgnoreCase)))
            {
                descartes.Add($"Epic: {rotulo} — categorias [{string.Join(", ", categorias)}] sem \"games\"");
                return null;
            }
        }
        else if (string.Equals(S("CatalogNamespace"), "ue", StringComparison.OrdinalIgnoreCase))
        {
            descartes.Add($"Epic: {rotulo} — Unreal Engine");
            return null;
        }
        if (!Directory.Exists(local))
        {
            descartes.Add($"Epic: {rotulo} — pasta ausente ({local})");
            return null;
        }
        var exe = S("LaunchExecutable");
        return new JogoInstalado(rotulo, local, LojaDeJogos.Epic, string.IsNullOrWhiteSpace(exe) ? null : exe, appName);
    }

    // -------------------------------------------------------------------- GOG

    private static readonly string[] ChavesDaGog =
    {
        @"HKLM\SOFTWARE\WOW6432Node\GOG.com\Games",
        @"HKLM\SOFTWARE\GOG.com\Games",
    };

    private static List<JogoInstalado> Gog(AmbienteDeLojas amb, List<string> descartes)
    {
        var lista = new List<JogoInstalado>();
        foreach (var chave in ChavesDaGog)
        {
            foreach (var id in amb.Registro.SubChaves(chave))
            {
                var k = chave + "\\" + id;
                var nome = amb.Registro.Valor(k, "gameName");
                var dependeDe = amb.Registro.Valor(k, "dependsOn");
                if (!string.IsNullOrWhiteSpace(dependeDe))
                {
                    descartes.Add($"GOG: {nome ?? id} — DLC (dependsOn {dependeDe})");
                    continue;
                }
                var pasta = amb.Registro.Valor(k, "path")?.Trim().TrimEnd('\\', '/');
                if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta))
                {
                    descartes.Add($"GOG: {nome ?? id} — pasta ausente ({pasta})");
                    continue;
                }
                lista.Add(new JogoInstalado(nome ?? Path.GetFileName(pasta), pasta, LojaDeJogos.Gog,
                    RelativoSePossivel(amb.Registro.Valor(k, "exe"), pasta), id));
            }
        }
        return lista;
    }

    // ---------------------------------------------------------------- Ubisoft

    private const string InstallsDaUbisoft = @"HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";

    private static readonly string[] ChavesDeUninstall =
    {
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    private static List<JogoInstalado> Ubisoft(AmbienteDeLojas amb, List<string> descartes)
    {
        var lista = new List<JogoInstalado>();
        foreach (var id in amb.Registro.SubChaves(InstallsDaUbisoft))
        {
            var dir = amb.Registro.Valor(InstallsDaUbisoft + "\\" + id, "InstallDir");
            if (string.IsNullOrWhiteSpace(dir)) continue;
            // A Ubisoft grava com barra normal e barra no fim: C:/Program Files (x86)/.../Jogo/
            var pasta = dir.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(pasta))
            {
                descartes.Add($"Ubisoft: instalação {id} — pasta ausente ({pasta})");
                continue;
            }
            string? nome = null;
            foreach (var un in ChavesDeUninstall)
            {
                nome = amb.Registro.Valor(un + "\\Uplay Install " + id, "DisplayName");
                if (!string.IsNullOrWhiteSpace(nome)) break;
            }
            lista.Add(new JogoInstalado(string.IsNullOrWhiteSpace(nome) ? Path.GetFileName(pasta) : nome, pasta,
                LojaDeJogos.Ubisoft, null, id));
        }
        return lista;
    }

    // --------------------------------------------------------------- Rockstar

    private static readonly string[] ChavesDaRockstar =
    {
        @"HKLM\SOFTWARE\WOW6432Node\Rockstar Games",
        @"HKLM\SOFTWARE\Rockstar Games",
    };

    private static List<JogoInstalado> Rockstar(AmbienteDeLojas amb, List<string> descartes)
    {
        var lista = new List<JogoInstalado>();
        foreach (var chave in ChavesDaRockstar)
        {
            foreach (var nome in amb.Registro.SubChaves(chave))
            {
                if (nome.Contains("Launcher", StringComparison.OrdinalIgnoreCase) ||
                    nome.Contains("Social Club", StringComparison.OrdinalIgnoreCase))
                {
                    descartes.Add($"Rockstar: {nome} — é o launcher/Social Club, não um jogo");
                    continue;
                }
                var pasta = amb.Registro.Valor(chave + "\\" + nome, "InstallFolder")?.Trim().TrimEnd('\\', '/');
                if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) continue;
                lista.Add(new JogoInstalado(nome, pasta, LojaDeJogos.Rockstar));
            }
        }
        return lista;
    }

    // --------------------------------------------------------------------- EA

    private static readonly string[] ChavesDaEa =
    {
        @"HKLM\SOFTWARE\WOW6432Node\Electronic Arts",
        @"HKLM\SOFTWARE\Electronic Arts",
        @"HKLM\SOFTWARE\WOW6432Node\EA Games",
        @"HKLM\SOFTWARE\EA Games",
    };

    private static readonly string[] NomesDeInstallDir = { "Install Dir", "InstallDir", "Install Path", "InstallPath" };

    private static List<JogoInstalado> Ea(AmbienteDeLojas amb, List<string> descartes)
    {
        var lista = new List<JogoInstalado>();

        // 1) Registro: cada jogo grava "Install Dir" numa chave própria (Electronic Arts\<Jogo>,
        //    EA Games\<Jogo>, Electronic Arts\EA Games\<Jogo>...). O que confirma que é um
        //    jogo da EA — e não o EA app, o Origin ou um serviço — é o __Installer\installerdata.xml.
        void Visitar(string chave, string nomeDaChave, int nivel)
        {
            var dir = NomesDeInstallDir.Select(n => amb.Registro.Valor(chave, n))
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (dir is not null)
            {
                var pasta = dir.Trim().TrimEnd('\\', '/');
                var xml = Path.Combine(pasta, "__Installer", "installerdata.xml");
                if (Directory.Exists(pasta) && File.Exists(xml))
                {
                    var (titulo, exe) = LerInstallerData(xml);
                    lista.Add(new JogoInstalado(titulo ?? amb.Registro.Valor(chave, "DisplayName") ?? nomeDaChave,
                        pasta, LojaDeJogos.Ea, exe));
                }
                else if (Directory.Exists(pasta))
                {
                    descartes.Add($"EA: {nomeDaChave} — sem __Installer\\installerdata.xml em {pasta}");
                }
            }
            if (nivel >= 1) return;
            foreach (var sub in amb.Registro.SubChaves(chave)) Visitar(chave + "\\" + sub, sub, nivel + 1);
        }
        foreach (var raiz in ChavesDaEa)
            foreach (var sub in amb.Registro.SubChaves(raiz))
                Visitar(raiz + "\\" + sub, sub, 0);

        // 2) Origin: %ProgramData%\Origin\LocalContent\<Jogo>\*.mfst, uma query string com o
        //    dipinstallpath. DLC do mesmo jogo tem mfst próprio, com a mesma pasta.
        if (!string.IsNullOrWhiteSpace(amb.ProgramData))
        {
            var local = Path.Combine(amb.ProgramData, "Origin", "LocalContent");
            if (Directory.Exists(local))
            {
                foreach (var pastaDoJogo in Directory.EnumerateDirectories(local))
                {
                    IEnumerable<string> mfsts;
                    try { mfsts = Directory.EnumerateFiles(pastaDoJogo, "*.mfst").ToList(); }
                    catch { continue; }
                    foreach (var mfst in mfsts)
                    {
                        string texto;
                        try { texto = File.ReadAllText(mfst); } catch { continue; }
                        var jogo = LerMfstDoOrigin(texto, Path.GetFileName(pastaDoJogo));
                        if (jogo is null) continue;
                        if (!Directory.Exists(jogo.Pasta))
                        {
                            descartes.Add($"Origin: {jogo.Nome} — pasta ausente ({jogo.Pasta})");
                            continue;
                        }
                        lista.Add(jogo);
                        break;
                    }
                }
            }
        }
        return lista;
    }

    /// <summary>O .mfst do Origin é uma query string: ?currentstate=...&amp;dipinstallpath=C%3A%5C...&amp;id=Origin.OFR...</summary>
    public static JogoInstalado? LerMfstDoOrigin(string texto, string nomeDaPasta)
    {
        var m = Regex.Match(texto, @"(?:^|[?&])dipinstallpath=([^&\s]*)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string pasta;
        try { pasta = Uri.UnescapeDataString(m.Groups[1].Value.Replace('+', ' ')).Trim().TrimEnd('\\', '/'); }
        catch { return null; }
        if (pasta.Length == 0) return null;

        var id = Regex.Match(texto, @"(?:^|[?&])id=([^&\s]*)", RegexOptions.IgnoreCase);
        string? titulo = null, exe = null;
        var xml = Path.Combine(pasta, "__Installer", "installerdata.xml");
        if (File.Exists(xml)) (titulo, exe) = LerInstallerData(xml);
        return new JogoInstalado(titulo ?? nomeDaPasta, pasta, LojaDeJogos.Ea, exe,
            id.Success ? Uri.UnescapeDataString(id.Groups[1].Value) : null);
    }

    /// <summary>
    /// Título e exe do __Installer\installerdata.xml da EA: &lt;gameTitle locale="en_US"&gt; e o
    /// &lt;filePath&gt; do launcher, que vem como [HKEY_LOCAL_MACHINE\...\Install Dir]jogo.exe.
    /// </summary>
    public static (string? Titulo, string? Exe) LerInstallerData(string caminho)
    {
        try
        {
            var doc = XDocument.Load(caminho);
            var titulos = doc.Descendants().Where(e => e.Name.LocalName == "gameTitle").ToList();
            var titulo = titulos.FirstOrDefault(t => string.Equals((string?)t.Attribute("locale"), "en_US", StringComparison.OrdinalIgnoreCase))?.Value.Trim()
                         ?? titulos.FirstOrDefault()?.Value.Trim();

            string? exe = null;
            foreach (var fp in doc.Descendants().Where(e => e.Name.LocalName == "filePath"))
            {
                var v = fp.Value.Trim();
                int fecha = v.LastIndexOf(']');
                if (fecha >= 0) v = v[(fecha + 1)..];
                v = v.TrimStart('\\', '/');
                if (v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { exe = v; break; }
            }
            return (string.IsNullOrWhiteSpace(titulo) ? null : titulo, exe);
        }
        catch { return (null, null); }
    }

    // ------------------------------------------------------------------- Xbox

    private static List<JogoInstalado> Xbox(AmbienteDeLojas amb, List<string> descartes)
    {
        var lista = new List<JogoInstalado>();
        foreach (var unidade in amb.RaizesDeUnidades)
        {
            string nomeDaPasta = "XboxGames";
            var gamingRoot = Path.Combine(unidade, ".GamingRoot");
            if (File.Exists(gamingRoot))
            {
                try { nomeDaPasta = LerGamingRoot(File.ReadAllBytes(gamingRoot)) ?? nomeDaPasta; }
                catch { }
            }
            var raiz = Path.Combine(unidade, nomeDaPasta);
            if (!Directory.Exists(raiz)) continue;

            IEnumerable<string> pastas;
            try { pastas = Directory.EnumerateDirectories(raiz).ToList(); } catch { continue; }
            foreach (var pastaDoJogo in pastas)
            {
                var content = Path.Combine(pastaDoJogo, "Content");
                var config = Path.Combine(content, "MicrosoftGame.config");
                if (!File.Exists(config))
                {
                    descartes.Add($"Xbox: {Path.GetFileName(pastaDoJogo)} — sem Content\\MicrosoftGame.config");
                    continue;
                }
                var (nome, exe) = LerMicrosoftGameConfig(config);
                lista.Add(new JogoInstalado(nome ?? Path.GetFileName(pastaDoJogo), content, LojaDeJogos.Xbox, exe));
            }
        }
        return lista;
    }

    /// <summary>
    /// O .GamingRoot na raiz da unidade diz em que pasta o app Xbox instala ali: "RGBX",
    /// 4 bytes de versão e o nome da pasta em UTF-16 terminado em zero.
    /// </summary>
    public static string? LerGamingRoot(byte[] bytes)
    {
        if (bytes.Length <= 8 || bytes[0] != (byte)'R' || bytes[1] != (byte)'G' || bytes[2] != (byte)'B' || bytes[3] != (byte)'X')
            return null;
        var texto = Encoding.Unicode.GetString(bytes, 8, bytes.Length - 8);
        int zero = texto.IndexOf('\0');
        if (zero >= 0) texto = texto[..zero];
        texto = texto.Trim().Trim('\\', '/');
        return texto.Length == 0 ? null : texto;
    }

    /// <summary>Nome de exibição e exe do MicrosoftGame.config (ShellVisuals e ExecutableList).</summary>
    public static (string? Nome, string? Exe) LerMicrosoftGameConfig(string caminho)
    {
        try
        {
            var doc = XDocument.Load(caminho);
            var visuais = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ShellVisuals");
            var nome = (string?)visuais?.Attribute("DefaultDisplayName");
            // "ms-resource:..." é referência a um recurso empacotado: não serve como nome.
            if (string.IsNullOrWhiteSpace(nome) || nome.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase)) nome = null;

            var exes = doc.Descendants().Where(e => e.Name.LocalName == "Executable").ToList();
            var exe = (string?)(exes.FirstOrDefault(e =>
                              string.Equals((string?)e.Attribute("TargetDeviceFamily"), "PC", StringComparison.OrdinalIgnoreCase))
                          ?? exes.FirstOrDefault())?.Attribute("Name");
            return (nome, string.IsNullOrWhiteSpace(exe) ? null : exe);
        }
        catch { return (null, null); }
    }

    // ------------------------------------------------------------- Battle.net

    private static List<JogoInstalado> BattleNet(AmbienteDeLojas amb, List<string> descartes)
    {
        var lista = new List<JogoInstalado>();
        foreach (var chave in ChavesDeUninstall)
        {
            foreach (var sub in amb.Registro.SubChaves(chave))
            {
                var k = chave + "\\" + sub;
                var uninstall = amb.Registro.Valor(k, "UninstallString") ?? "";
                var publisher = amb.Registro.Valor(k, "Publisher") ?? "";
                if (!uninstall.Contains("Blizzard Uninstaller", StringComparison.OrdinalIgnoreCase) &&
                    !publisher.Contains("Blizzard", StringComparison.OrdinalIgnoreCase))
                    continue;
                var nome = amb.Registro.Valor(k, "DisplayName") ?? sub;
                if (nome.Contains("Battle.net", StringComparison.OrdinalIgnoreCase))
                {
                    descartes.Add($"Battle.net: {nome} — é o cliente, não um jogo");
                    continue;
                }
                var pasta = amb.Registro.Valor(k, "InstallLocation")?.Trim().Trim('"').TrimEnd('\\', '/');
                if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta))
                {
                    descartes.Add($"Battle.net: {nome} — sem InstallLocation válido");
                    continue;
                }
                lista.Add(new JogoInstalado(nome, pasta, LojaDeJogos.BattleNet));
            }
        }
        return lista;
    }

    // ----------------------------------------------------------------- Amazon

    private static List<JogoInstalado> Amazon(AmbienteDeLojas amb, List<string> descartes)
    {
        var lista = new List<JogoInstalado>();
        foreach (var chave in ChavesDeUninstall)
        {
            foreach (var sub in amb.Registro.SubChaves(chave))
            {
                if (!sub.StartsWith("AmazonGames/", StringComparison.OrdinalIgnoreCase)) continue;
                var k = chave + "\\" + sub;
                var nome = amb.Registro.Valor(k, "DisplayName") ?? sub["AmazonGames/".Length..];
                var pasta = amb.Registro.Valor(k, "InstallLocation")?.Trim().Trim('"').TrimEnd('\\', '/');
                if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta))
                {
                    descartes.Add($"Amazon: {nome} — sem InstallLocation válido");
                    continue;
                }
                lista.Add(new JogoInstalado(nome, pasta, LojaDeJogos.Amazon, null, sub));
            }
        }
        return lista;
    }
}
