using System.Text;
using System.Text.Json;
using Dlss5.Core;
using Xunit;

namespace Dlss5.Core.Tests;

/// <summary>
/// Registro falso: chave → valores, com as chaves-pai criadas sozinhas. Uma chave pode
/// "explodir" para simular uma loja cujo registro está inacessível.
/// </summary>
internal sealed class RegistroFalso : ILeitorDeRegistro
{
    private readonly Dictionary<string, Dictionary<string, string>> _valores = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _chaves = new(StringComparer.OrdinalIgnoreCase);

    public string? ChaveQueExplode { get; set; }

    public RegistroFalso Chave(string caminho, params (string Nome, string Valor)[] valores)
    {
        var partes = caminho.Split('\\');
        for (int i = 1; i <= partes.Length; i++) _chaves.Add(string.Join('\\', partes.Take(i)));
        if (!_valores.TryGetValue(caminho, out var d)) _valores[caminho] = d = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, v) in valores) d[n] = v;
        return this;
    }

    public IReadOnlyList<string> SubChaves(string chave)
    {
        Explode(chave);
        var prefixo = chave.TrimEnd('\\') + "\\";
        return _chaves
            .Where(c => c.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase) && !c[prefixo.Length..].Contains('\\'))
            .Select(c => c[prefixo.Length..])
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? Valor(string chave, string nome)
    {
        Explode(chave);
        return _valores.TryGetValue(chave, out var d) && d.TryGetValue(nome, out var v) ? v : null;
    }

    private void Explode(string chave)
    {
        if (ChaveQueExplode is not null && chave.StartsWith(ChaveQueExplode, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("registro bloqueado: " + chave);
    }
}

public class JogosInstaladosTests
{
    private const string Un64 = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UnUsuario = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static string Pasta()
    {
        var p = Path.Combine(Path.GetTempPath(), "dlss5lojas_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>Pasta de um jogo com (ou sem) um exe dentro; devolve a pasta.</summary>
    private static string Jogo(string raiz, string nome, string? exeRelativo = "jogo.exe")
    {
        var dir = Path.Combine(raiz, nome);
        Directory.CreateDirectory(dir);
        if (exeRelativo is not null)
        {
            var exe = Path.Combine(dir, exeRelativo);
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "MZ");
        }
        return dir;
    }

    private static void Acf(string steamapps, uint appid, string nome, string installdir, int? flags = 4)
    {
        Directory.CreateDirectory(steamapps);
        var sb = new StringBuilder("\"AppState\"\n{\n\t\"appid\"\t\t\"" + appid + "\"\n\t\"Universe\"\t\t\"1\"\n");
        if (flags is { } f) sb.Append("\t\"StateFlags\"\t\t\"" + f + "\"\n");
        sb.Append("\t\"name\"\t\t\"" + nome + "\"\n\t\"installdir\"\t\t\"" + installdir + "\"\n}\n");
        File.WriteAllText(Path.Combine(steamapps, $"appmanifest_{appid}.acf"), sb.ToString());
    }

    /// <summary>
    /// Um appinfo.vdf sintético, fiel ao layout real das versões 27/28/29 (na 29 as chaves
    /// viram índices numa tabela de strings gravada no fim do arquivo).
    /// </summary>
    private static byte[] AppInfoFalso(int versao, params (uint AppId, string Tipo, string Nome)[] apps)
    {
        var tabela = new List<string>();
        int Indice(string s)
        {
            int i = tabela.IndexOf(s);
            if (i < 0) { tabela.Add(s); i = tabela.Count - 1; }
            return i;
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(versao switch { 27 => SteamAppInfo.MagicV27, 28 => SteamAppInfo.MagicV28, _ => SteamAppInfo.MagicV29 });
        w.Write(1u); // universe
        long posDoOffset = 0;
        if (versao >= 29) { posDoOffset = ms.Position; w.Write(0L); }

        foreach (var a in apps)
        {
            using var blob = new MemoryStream();
            using var b = new BinaryWriter(blob, Encoding.UTF8, leaveOpen: true);
            void Chave(string k)
            {
                if (versao >= 29) b.Write((uint)Indice(k));
                else { b.Write(Encoding.UTF8.GetBytes(k)); b.Write((byte)0); }
            }
            void Str(string k, string v) { b.Write((byte)0x01); Chave(k); b.Write(Encoding.UTF8.GetBytes(v)); b.Write((byte)0); }
            void Int(string k, int v) { b.Write((byte)0x02); Chave(k); b.Write(v); }
            void U64(string k, ulong v) { b.Write((byte)0x07); Chave(k); b.Write(v); }

            b.Write((byte)0x00); Chave("appinfo");
                Int("appid", (int)a.AppId);
                b.Write((byte)0x00); Chave("common");
                    Str("name", a.Nome);
                    Str("type", a.Tipo);
                    Str("oslist", "windows");
                    Int("metacritic_score", 90);
                    b.Write((byte)0x00); Chave("associations");
                        b.Write((byte)0x00); Chave("0");
                            Str("type", "developer");
                            Str("name", "Alguém");
                        b.Write((byte)0x08);
                    b.Write((byte)0x08);
                b.Write((byte)0x08);
                b.Write((byte)0x00); Chave("extended");
                    Str("developer", "Alguém");
                    U64("dlcforappid", 0);
                b.Write((byte)0x08);
            b.Write((byte)0x08);
            b.Write((byte)0x08); // fim do blob

            var bytes = blob.ToArray();
            int cabecalho = 4 + 4 + 8 + 20 + 4 + (versao >= 28 ? 20 : 0);
            w.Write(a.AppId);
            w.Write((uint)(cabecalho + bytes.Length));
            w.Write(2u);                 // infoState
            w.Write(1700000000u);        // lastUpdated
            w.Write(123456789UL);        // picsToken
            w.Write(new byte[20]);       // sha1
            w.Write(42u);                // changeNumber
            if (versao >= 28) w.Write(new byte[20]);
            w.Write(bytes);
        }
        w.Write(0u); // fim da lista de apps

        if (versao >= 29)
        {
            long posDaTabela = ms.Position;
            w.Write((uint)tabela.Count);
            foreach (var s in tabela) { w.Write(Encoding.UTF8.GetBytes(s)); w.Write((byte)0); }
            ms.Position = posDoOffset;
            w.Write(posDaTabela);
        }
        w.Flush();
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ Steam

    [Fact]
    public void SteamListaOsJogosDeTodasAsBibliotecasESoOsJogos()
    {
        var raiz = Pasta();
        try
        {
            var steam = Path.Combine(raiz, "Steam");
            var lib2 = Path.Combine(raiz, "SteamLibrary");
            var sa1 = Path.Combine(steam, "steamapps");
            var sa2 = Path.Combine(lib2, "steamapps");
            Directory.CreateDirectory(sa1);
            Directory.CreateDirectory(sa2);
            // Formato atual: bloco por biblioteca com "path" (barra dobrada) e a lista "apps".
            File.WriteAllText(Path.Combine(sa1, "libraryfolders.vdf"),
                "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam.Replace("\\", "\\\\") + "\"\n" +
                "\t\t\"label\"\t\t\"\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"12140\"\t\t\"1234567\"\n\t\t\t\"228980\"\t\t\"55\"\n\t\t}\n\t}\n" +
                "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + lib2.Replace("\\", "\\\\") + "\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"1091500\"\t\t\"70000000000\"\n\t\t}\n\t}\n}\n");

            var maxPayne = Jogo(Path.Combine(sa1, "common"), "Max Payne", "maxpayne.exe");
            Acf(sa1, 12140, "Max Payne", "Max Payne");
            var cyber = Jogo(Path.Combine(sa2, "common"), "Cyberpunk 2077", Path.Combine("bin", "x64", "Cyberpunk2077.exe"));
            Acf(sa2, 1091500, "Cyberpunk 2077", "Cyberpunk 2077");

            // Não são jogos, cada um por um motivo diferente:
            Jogo(Path.Combine(sa1, "common"), "Steamworks Shared", "steamservice.exe");
            Acf(sa1, 228980, "Steamworks Common Redistributables", "Steamworks Shared");   // appid conhecido
            var artbook = Jogo(Path.Combine(sa2, "common"), "Cyberpunk 2077 Artbook", null);
            File.WriteAllText(Path.Combine(artbook, "artbook.pdf"), "x");
            Acf(sa2, 1091510, "Cyberpunk 2077 Artbook", "Cyberpunk 2077 Artbook");        // sem exe nenhum
            Acf(sa1, 777, "Jogo Removido", "Nao Existe");                                 // pasta ausente
            Jogo(Path.Combine(sa1, "common"), "Agendado", "a.exe");
            Acf(sa1, 888, "Agendado", "Agendado", flags: 1026);                            // download agendado
            Jogo(Path.Combine(sa1, "common"), "HL2DM Server", "srcds.exe");
            Acf(sa1, 232370, "Half-Life 2: Deathmatch Dedicated Server", "HL2DM Server");  // nome de servidor

            var jogos = JogosInstalados.Procurar(new AmbienteDeLojas { RaizDaSteam = steam });

            Assert.Equal(new[] { "Cyberpunk 2077", "Max Payne" }, jogos.Select(j => j.Nome).ToArray());
            Assert.All(jogos, j => Assert.Equal(LojaDeJogos.Steam, j.Loja));
            Assert.Equal(Path.GetFullPath(cyber), jogos[0].Pasta);
            Assert.Equal("1091500", jogos[0].Id);
            Assert.Equal(Path.GetFullPath(maxPayne), jogos[1].Pasta);
            Assert.Equal("Steam", jogos[1].NomeDaLoja);
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void SteamAceitaOLibraryfoldersAntigoESemDuplicarABibliotecaPrincipal()
    {
        var raiz = Pasta();
        try
        {
            var steam = Path.Combine(raiz, "Steam");
            var lib2 = Path.Combine(raiz, "Jogos");
            Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
            Directory.CreateDirectory(Path.Combine(lib2, "steamapps"));
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
                "\"LibraryFolders\"\n{\n\t\"TimeNextStatsReport\"\t\t\"1600000000\"\n\t\"ContentStatsID\"\t\t\"-1\"\n" +
                "\t\"1\"\t\t\"" + lib2.Replace("\\", "\\\\") + "\"\n\t\"2\"\t\t\"" + steam.Replace("\\", "\\\\") + "\"\n}\n");

            var libs = JogosInstalados.BibliotecasDaSteam(steam);
            Assert.Equal(2, libs.Count);
            Assert.Equal(Path.GetFullPath(steam), libs[0]);
            Assert.Equal(Path.GetFullPath(lib2), libs[1]);
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void SteamUsaOTipoDoAppinfoQuandoEleExiste()
    {
        var raiz = Pasta();
        try
        {
            var steam = Path.Combine(raiz, "Steam");
            var sa = Path.Combine(steam, "steamapps");
            var common = Path.Combine(sa, "common");
            // Todos com exe: aqui só o TIPO separa.
            Jogo(common, "Portal 2", "portal2.exe");      Acf(sa, 620, "Portal 2", "Portal 2");
            Jogo(common, "Source SDK", "sdk.exe");        Acf(sa, 211, "Source SDK", "Source SDK");
            Jogo(common, "wallpaper_engine", "wp.exe");   Acf(sa, 431960, "Wallpaper Engine", "wallpaper_engine");
            Jogo(common, "Demo X", "demo.exe");           Acf(sa, 1000, "Demo X", "Demo X");
            Jogo(common, "Portal 2 DLC", "dlc.exe");      Acf(sa, 1001, "Portal 2 DLC", "Portal 2 DLC");
            Jogo(common, "Trilha", "player.exe");         Acf(sa, 1002, "Portal 2 Music", "Trilha");
            // Fora do appinfo.vdf (app novo): decide pelo nome, e "Coisa Nova" não tem cara de ferramenta.
            Jogo(common, "Coisa Nova", "nova.exe");       Acf(sa, 2000, "Coisa Nova", "Coisa Nova");
            Jogo(common, "SDK Nova", "sdk.exe");          Acf(sa, 2001, "Coisa Nova SDK", "SDK Nova");

            Directory.CreateDirectory(Path.Combine(steam, "appcache"));
            File.WriteAllBytes(Path.Combine(steam, "appcache", "appinfo.vdf"), AppInfoFalso(29,
                (620, "Game", "Portal 2"),
                (211, "Tool", "Source SDK"),
                (431960, "Application", "Wallpaper Engine"),
                (1000, "Demo", "Demo X"),
                (1001, "DLC", "Portal 2 DLC"),
                (1002, "Music", "Portal 2 Soundtrack"),
                (3000, "Game", "Nao Instalado")));

            var jogos = JogosInstalados.Procurar(new AmbienteDeLojas { RaizDaSteam = steam });
            Assert.Equal(new[] { "Coisa Nova", "Demo X", "Portal 2" }, jogos.Select(j => j.Nome).ToArray());
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Theory]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    public void AppinfoEhLidoNasTresVersoesDoFormato(int versao)
    {
        var bytes = AppInfoFalso(versao,
            (620, "Game", "Portal 2"),
            (211, "Tool", "Source SDK"),
            (431960, "Application", "Wallpaper Engine"),
            (999, "game", "Antigo em minúsculo"));

        var lido = SteamAppInfo.Ler(new MemoryStream(bytes), new HashSet<uint> { 620, 431960, 999, 123 });

        Assert.Equal(3, lido.Count);
        Assert.Equal(new SteamAppInfo.InfoDoApp("Game", "Portal 2"), lido[620]);
        Assert.Equal(new SteamAppInfo.InfoDoApp("Application", "Wallpaper Engine"), lido[431960]);
        Assert.Equal("game", lido[999].Tipo);
        Assert.False(lido.ContainsKey(211));   // não pedido: pulado pelo "size", sem decodificar
        Assert.False(lido.ContainsKey(123));   // pedido mas ausente
        Assert.True(JogosInstalados.EhTipoDeJogoDaSteam("game"));
        Assert.True(JogosInstalados.EhTipoDeJogoDaSteam("Demo"));
        Assert.False(JogosInstalados.EhTipoDeJogoDaSteam("DLC"));
        Assert.False(JogosInstalados.EhTipoDeJogoDaSteam("Tool"));
        Assert.False(JogosInstalados.EhTipoDeJogoDaSteam("Music"));
        Assert.False(JogosInstalados.EhTipoDeJogoDaSteam(null));
    }

    [Fact]
    public void AppinfoIlegivelDevolveVazioEABuscaCaiNasHeuristicas()
    {
        Assert.Empty(SteamAppInfo.Ler(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }), new HashSet<uint> { 620 }));
        Assert.Empty(SteamAppInfo.Ler(new MemoryStream(Array.Empty<byte>()), new HashSet<uint> { 620 }));
        Assert.Empty(SteamAppInfo.Ler(Path.Combine(Path.GetTempPath(), "nao_existe_" + Guid.NewGuid().ToString("N")), new HashSet<uint> { 620 }));
        // Cabeçalho certo, resto lixo: não lança.
        var lixo = BitConverter.GetBytes(SteamAppInfo.MagicV29).Concat(BitConverter.GetBytes(1u))
            .Concat(BitConverter.GetBytes(999999L)).Concat(new byte[] { 9, 9, 9 }).ToArray();
        Assert.Empty(SteamAppInfo.Ler(new MemoryStream(lixo), new HashSet<uint> { 620 }));

        var raiz = Pasta();
        try
        {
            var steam = Path.Combine(raiz, "Steam");
            var sa = Path.Combine(steam, "steamapps");
            Jogo(Path.Combine(sa, "common"), "Portal 2", "portal2.exe");
            Acf(sa, 620, "Portal 2", "Portal 2");
            Directory.CreateDirectory(Path.Combine(steam, "appcache"));
            File.WriteAllBytes(Path.Combine(steam, "appcache", "appinfo.vdf"), lixo);

            var jogos = JogosInstalados.Procurar(new AmbienteDeLojas { RaizDaSteam = steam });
            Assert.Single(jogos);
            Assert.Equal("Portal 2", jogos[0].Nome);
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Theory]
    [InlineData("Steamworks Common Redistributables", 228980u, true)]
    [InlineData("Proton 9.0 (Beta)", 2805730u, true)]
    [InlineData("Steam Linux Runtime 3.0 (sniper)", 1628350u, true)]
    [InlineData("Half-Life 2: Deathmatch Dedicated Server", 232370u, true)]
    [InlineData("Hades Soundtrack", 1145360u, true)]
    [InlineData("Cyberpunk 2077 REDmod", 2060310u, false)]
    [InlineData("Portal 2", 620u, false)]
    [InlineData("Ghost of Tsushima DIRECTOR'S CUT", 2215430u, false)]
    [InlineData("Ostriv", 773790u, false)]
    [InlineData("The Ghost", 1u, false)]
    [InlineData("Super Ostrich Run", 2u, false)]
    [InlineData("Portal 2 - OST", 3u, true)]
    [InlineData("Source SDK Base 2013 Multiplayer", 243750u, true)]
    [InlineData("Half-Life Alyx Workshop Tools", 4u, true)]
    public void PeloNomeSoCaiQuemTemCaraDeFerramenta(string nome, uint appid, bool foraDaLista) =>
        Assert.Equal(foraDaLista, JogosInstalados.PareceNaoJogoDaSteam(nome, appid));

    // ------------------------------------------------------------------- Epic

    private static void Item(string manifests, string arquivo, object conteudo) =>
        File.WriteAllText(Path.Combine(manifests, arquivo + ".item"), JsonSerializer.Serialize(conteudo));

    [Fact]
    public void EpicEntraSoOJogoPrincipalComCategoriaGames()
    {
        var raiz = Pasta();
        try
        {
            var programData = Path.Combine(raiz, "ProgramData");
            var manifests = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
            Directory.CreateDirectory(manifests);
            var jogos = Path.Combine(raiz, "Epic Games");

            var control = Jogo(jogos, "Control", "Control_DX12.exe");
            Item(manifests, "A1", new
            {
                FormatVersion = 0, bIsIncompleteInstall = false, LaunchExecutable = "Control_DX12.exe",
                bIsApplication = true, AppCategories = new[] { "public", "games", "applications" },
                DisplayName = "Control", InstallLocation = control, TechnicalType = "public,games,applications",
                CatalogNamespace = "calluna", AppName = "Calluna", MainGameAppName = "Calluna",
            });
            Item(manifests, "A2", new   // DLC do Control: MainGameAppName aponta para o jogo
            {
                bIsIncompleteInstall = false, AppCategories = new[] { "public", "addons", "applications" },
                DisplayName = "Control - AWE", InstallLocation = control, CatalogNamespace = "calluna",
                AppName = "CallunaAWE", MainGameAppName = "Calluna",
            });
            var ue = Jogo(jogos, "UE_5.3", Path.Combine("Engine", "Binaries", "Win64", "UnrealEditor.exe"));
            Item(manifests, "A3", new   // Unreal Engine: categoria "engines"
            {
                bIsIncompleteInstall = false, AppCategories = new[] { "public", "engines" },
                DisplayName = "UE_5.3", InstallLocation = ue, CatalogNamespace = "ue",
                AppName = "UE_5.3", MainGameAppName = "UE_5.3", LaunchExecutable = "Engine/Binaries/Win64/UnrealEditor.exe",
            });
            var pelaMetade = Jogo(jogos, "Alan Wake 2", "AlanWake2.exe");
            Item(manifests, "A4", new
            {
                bIsIncompleteInstall = true, AppCategories = new[] { "public", "games" },
                DisplayName = "Alan Wake 2", InstallLocation = pelaMetade, AppName = "AW2", MainGameAppName = "AW2",
            });
            Item(manifests, "A5", new   // pasta apagada à mão
            {
                bIsIncompleteInstall = false, AppCategories = new[] { "public", "games" },
                DisplayName = "Removido", InstallLocation = Path.Combine(jogos, "Removido"), AppName = "R", MainGameAppName = "R",
            });
            var antigo = Jogo(jogos, "GTAV", "PlayGTAV.exe");
            Item(manifests, "A6", new   // manifesto antigo, sem AppCategories: bIsApplication + namespace que não é "ue"
            {
                bIsIncompleteInstall = false, bIsApplication = true, DisplayName = "Grand Theft Auto V",
                InstallLocation = antigo, CatalogNamespace = "0584d2013f0149a791e7b9bad0eec102",
                AppName = "9d2d0eb64d5c44529cece33fe2a46482", MainGameAppName = "9d2d0eb64d5c44529cece33fe2a46482",
                LaunchExecutable = "PlayGTAV.exe",
            });
            var ueAntigo = Jogo(jogos, "UE_4.27", Path.Combine("Engine", "Binaries", "Win64", "UE4Editor.exe"));
            Item(manifests, "A7", new { bIsIncompleteInstall = false, bIsApplication = true, DisplayName = "UE_4.27", InstallLocation = ueAntigo, CatalogNamespace = "ue", AppName = "UE_4.27" });
            File.WriteAllText(Path.Combine(manifests, "quebrado.item"), "{ isto não é json");

            var achados = JogosInstalados.Procurar(new AmbienteDeLojas { ProgramData = programData });

            Assert.Equal(new[] { "Control", "Grand Theft Auto V" }, achados.Select(j => j.Nome).ToArray());
            Assert.All(achados, j => Assert.Equal(LojaDeJogos.Epic, j.Loja));
            Assert.Equal("Control_DX12.exe", achados[0].ExecutavelIndicado);
            Assert.Equal("Calluna", achados[0].Id);
            Assert.Equal(Path.GetFullPath(antigo), achados[1].Pasta);
        }
        finally { Directory.Delete(raiz, true); }
    }

    // -------------------------------------------------------------------- GOG

    [Fact]
    public void GogIgnoraDlcPeloDependsOnEDaOExeRelativo()
    {
        var raiz = Pasta();
        try
        {
            var cp = Jogo(raiz, "Cyberpunk 2077", Path.Combine("bin", "x64", "Cyberpunk2077.exe"));
            var reg = new RegistroFalso()
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\1423049311",
                    ("gameName", "Cyberpunk 2077"), ("path", cp), ("exe", Path.Combine(cp, "bin", "x64", "Cyberpunk2077.exe")),
                    ("dependsOn", ""), ("productID", "1423049311"))
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\1256996170",
                    ("gameName", "Cyberpunk 2077: Phantom Liberty"), ("path", cp), ("dependsOn", "1423049311"))
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\999",
                    ("gameName", "Desinstalado à mão"), ("path", Path.Combine(raiz, "sumiu")));

            var jogos = JogosInstalados.Procurar(new AmbienteDeLojas { Registro = reg });

            var unico = Assert.Single(jogos);
            Assert.Equal("Cyberpunk 2077", unico.Nome);
            Assert.Equal(LojaDeJogos.Gog, unico.Loja);
            Assert.Equal(Path.Combine("bin", "x64", "Cyberpunk2077.exe"), unico.ExecutavelIndicado);
            Assert.Equal("1423049311", unico.Id);
        }
        finally { Directory.Delete(raiz, true); }
    }

    // ---------------------------------------------------------------- Ubisoft

    [Fact]
    public void UbisoftPegaONomeDoUninstallEAceitaBarraNormalNoFim()
    {
        var raiz = Pasta();
        try
        {
            var acv = Jogo(raiz, "Assassin's Creed Valhalla", "ACValhalla.exe");
            var semNome = Jogo(raiz, "Far Cry 6", Path.Combine("bin", "FarCry6.exe"));
            var reg = new RegistroFalso()
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\5603",
                    ("InstallDir", acv.Replace('\\', '/') + "/"), ("Language", "en-US"))
                .Chave(Un64 + @"\Uplay Install 5603", ("DisplayName", "Assassin's Creed Valhalla"), ("InstallLocation", acv))
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\5266", ("InstallDir", semNome + "/"))
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\1", ("InstallDir", Path.Combine(raiz, "apagado") + "/"));

            var jogos = JogosInstalados.Procurar(new AmbienteDeLojas { Registro = reg });

            Assert.Equal(new[] { "Assassin's Creed Valhalla", "Far Cry 6" }, jogos.Select(j => j.Nome).ToArray());
            Assert.All(jogos, j => Assert.Equal(LojaDeJogos.Ubisoft, j.Loja));
            Assert.Equal(Path.GetFullPath(acv), jogos[0].Pasta);
            Assert.Equal("5603", jogos[0].Id);
        }
        finally { Directory.Delete(raiz, true); }
    }

    // --------------------------------------------------------------- Rockstar

    [Fact]
    public void RockstarIgnoraOLauncherEOSocialClub()
    {
        var raiz = Pasta();
        try
        {
            var gta = Jogo(raiz, "Grand Theft Auto V", "GTA5.exe");
            var launcher = Jogo(raiz, "Launcher", "Launcher.exe");
            var reg = new RegistroFalso()
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V", ("InstallFolder", gta))
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\Rockstar Games\Launcher", ("InstallFolder", launcher))
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\Rockstar Games\Rockstar Games Social Club", ("InstallFolder", launcher));

            var jogos = JogosInstalados.Procurar(new AmbienteDeLojas { Registro = reg });

            var unico = Assert.Single(jogos);
            Assert.Equal(("Grand Theft Auto V", LojaDeJogos.Rockstar, Path.GetFullPath(gta)), (unico.Nome, unico.Loja, unico.Pasta));
        }
        finally { Directory.Delete(raiz, true); }
    }

    // --------------------------------------------------------------------- EA

    private static void InstallerData(string pastaDoJogo, string titulo, string chaveDoRegistro, string exe)
    {
        var dir = Path.Combine(pastaDoJogo, "__Installer");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "installerdata.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<DiPManifest version=\"4.0\">\n" +
            "  <gameTitles>\n    <gameTitle locale=\"pt_BR\">" + titulo + " (BR)</gameTitle>\n" +
            "    <gameTitle locale=\"en_US\">" + titulo + "</gameTitle>\n  </gameTitles>\n" +
            "  <runtime>\n    <launcher uid=\"1\">\n      <name locale=\"en_US\">" + titulo + "</name>\n" +
            "      <filePath>[" + chaveDoRegistro + "\\Install Dir]" + exe + "</filePath>\n      <trial>0</trial>\n    </launcher>\n  </runtime>\n" +
            "</DiPManifest>\n");
    }

    [Fact]
    public void EaPeloRegistroExigeOInstallerDataEOOriginVemDosMfst()
    {
        var raiz = Pasta();
        try
        {
            var bf1 = Jogo(raiz, "Battlefield 1", "bf1.exe");
            InstallerData(bf1, "Battlefield™ 1", @"HKEY_LOCAL_MACHINE\SOFTWARE\EA Games\Battlefield 1", "bf1.exe");
            var tf2 = Jogo(raiz, "Titanfall2", "Titanfall2.exe");
            InstallerData(tf2, "Titanfall® 2", @"HKEY_LOCAL_MACHINE\SOFTWARE\Respawn\Titanfall2", "Titanfall2.exe");
            var eaApp = Jogo(raiz, "EA Desktop", "EADesktop.exe");   // programa da EA, não jogo: sem __Installer
            var dai = Jogo(raiz, "Dragon Age Inquisition", "DragonAgeInquisition.exe");   // só no Origin, sem xml

            var reg = new RegistroFalso()
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\EA Games\Battlefield 1", ("Install Dir", bf1 + "\\"), ("Locale", "en_US"))
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\Electronic Arts\EA Desktop", ("InstallLocation", eaApp), ("Install Dir", eaApp))
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\Electronic Arts\EA Games\Titanfall2", ("Install Dir", tf2));

            var programData = Path.Combine(raiz, "ProgramData");
            var local = Path.Combine(programData, "Origin", "LocalContent");
            Directory.CreateDirectory(Path.Combine(local, "Dragon Age Inquisition"));
            File.WriteAllText(Path.Combine(local, "Dragon Age Inquisition", "Origin.OFR.50.0000123.mfst"),
                "?currentstate=kReadyToStart&ddinitialdownload=0&ddinstallalreadycompleted=1&dipinstallpath=" +
                Uri.EscapeDataString(dai) + "&id=Origin.OFR.50.0000123&previousstate=kCompleted&repairstate=");
            File.WriteAllText(Path.Combine(local, "Dragon Age Inquisition", "Origin.OFR.50.0000999.mfst"),   // DLC: mesma pasta
                "?currentstate=kReadyToStart&dipinstallpath=" + Uri.EscapeDataString(dai) + "&id=Origin.OFR.50.0000999");
            Directory.CreateDirectory(Path.Combine(local, "Battlefield 1"));
            File.WriteAllText(Path.Combine(local, "Battlefield 1", "Origin.OFR.50.0000557.mfst"),   // já veio pelo registro
                "?currentstate=kReadyToStart&dipinstallpath=" + Uri.EscapeDataString(bf1) + "&id=Origin.OFR.50.0000557");

            var jogos = JogosInstalados.Procurar(new AmbienteDeLojas { Registro = reg, ProgramData = programData });

            Assert.Equal(new[] { "Battlefield™ 1", "Dragon Age Inquisition", "Titanfall® 2" }, jogos.Select(j => j.Nome).ToArray());
            Assert.All(jogos, j => Assert.Equal(LojaDeJogos.Ea, j.Loja));
            Assert.Equal(Path.GetFullPath(bf1), jogos[0].Pasta);
            Assert.Equal("bf1.exe", jogos[0].ExecutavelIndicado);
            Assert.Equal("Origin.OFR.50.0000123", jogos[1].Id);
            Assert.Equal("Titanfall2.exe", jogos[2].ExecutavelIndicado);
        }
        finally { Directory.Delete(raiz, true); }
    }

    // ------------------------------------------------------------------- Xbox

    private static void MicrosoftGameConfig(string content, string? displayName, string exe)
    {
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "MicrosoftGame.config"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Game configVersion=\"1\">\n" +
            "  <Identity Name=\"Microsoft.624F8B84B80\" Publisher=\"CN=Microsoft\" Version=\"1.0.0.0\"/>\n" +
            "  <ExecutableList>\n    <Executable Name=\"" + exe + "\" Id=\"Game\" TargetDeviceFamily=\"PC\"/>\n  </ExecutableList>\n" +
            (displayName is null ? "" : "  <ShellVisuals DefaultDisplayName=\"" + displayName + "\" PublisherDisplayName=\"Xbox Game Studios\"/>\n") +
            "</Game>\n");
        File.WriteAllText(Path.Combine(content, exe), "MZ");
    }

    [Fact]
    public void XboxLeOMicrosoftGameConfigERespeitaOGamingRoot()
    {
        var raiz = Pasta();
        try
        {
            // Unidade 1: pasta padrão XboxGames.
            var unidade1 = Path.Combine(raiz, "C");
            var forza = Path.Combine(unidade1, "XboxGames", "Forza Horizon 5", "Content");
            MicrosoftGameConfig(forza, "Forza Horizon 5", "ForzaHorizon5.exe");
            var semNome = Path.Combine(unidade1, "XboxGames", "Sea of Thieves", "Content");
            MicrosoftGameConfig(semNome, "ms-resource:AppDisplayName", "Athena.exe");
            Directory.CreateDirectory(Path.Combine(unidade1, "XboxGames", "GamingServices"));   // sem Content\MicrosoftGame.config

            // Unidade 2: o .GamingRoot manda instalar em "Meus Jogos".
            var unidade2 = Path.Combine(raiz, "D");
            Directory.CreateDirectory(unidade2);
            var nome = Encoding.Unicode.GetBytes("Meus Jogos\0");
            File.WriteAllBytes(Path.Combine(unidade2, ".GamingRoot"),
                new byte[] { (byte)'R', (byte)'G', (byte)'B', (byte)'X', 1, 0, 0, 0 }.Concat(nome).ToArray());
            var halo = Path.Combine(unidade2, "Meus Jogos", "Halo Infinite", "Content");
            MicrosoftGameConfig(halo, "Halo Infinite", "HaloInfinite.exe");

            var jogos = JogosInstalados.Procurar(new AmbienteDeLojas { RaizesDeUnidades = new[] { unidade1, unidade2 } });

            Assert.Equal(new[] { "Forza Horizon 5", "Halo Infinite", "Sea of Thieves" }, jogos.Select(j => j.Nome).ToArray());
            Assert.All(jogos, j => Assert.Equal(LojaDeJogos.Xbox, j.Loja));
            Assert.Equal(Path.GetFullPath(forza), jogos[0].Pasta);
            Assert.Equal("ForzaHorizon5.exe", jogos[0].ExecutavelIndicado);
            Assert.Equal(Path.GetFullPath(halo), jogos[1].Pasta);
            Assert.Equal("Meus Jogos", JogosInstalados.LerGamingRoot(File.ReadAllBytes(Path.Combine(unidade2, ".GamingRoot"))));
            Assert.Null(JogosInstalados.LerGamingRoot(new byte[] { 1, 2, 3 }));
        }
        finally { Directory.Delete(raiz, true); }
    }

    // --------------------------------------------------- Battle.net e Amazon

    [Fact]
    public void BattleNetEAmazonVemDasEntradasDeDesinstalacao()
    {
        var raiz = Pasta();
        try
        {
            var ow = Jogo(raiz, "Overwatch", Path.Combine("_retail_", "Overwatch.exe"));
            var bnet = Jogo(raiz, "Battle.net", "Battle.net.exe");
            var f76 = Jogo(raiz, "Fallout76", "Fallout76.exe");
            var reg = new RegistroFalso()
                .Chave(Un64 + @"\Overwatch", ("DisplayName", "Overwatch"), ("Publisher", "Blizzard Entertainment"),
                    ("UninstallString", "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enUS --uid=prometheus --displayname=\"Overwatch\""),
                    ("InstallLocation", ow))
                .Chave(Un64 + @"\Battle.net", ("DisplayName", "Battle.net"), ("Publisher", "Blizzard Entertainment"),
                    ("UninstallString", "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enUS --uid=battle.net --displayname=\"Battle.net\""),
                    ("InstallLocation", bnet))
                .Chave(Un64 + @"\Diablo IV", ("DisplayName", "Diablo IV"), ("Publisher", "Blizzard Entertainment"))   // sem InstallLocation
                .Chave(Un64 + @"\Steam App 12140", ("DisplayName", "Max Payne"), ("Publisher", "Remedy"), ("InstallLocation", ow))
                .Chave(UnUsuario + @"\AmazonGames/Fallout 76", ("DisplayName", "Fallout 76"), ("InstallLocation", f76),
                    ("UninstallString", "\"C:\\Users\\x\\AppData\\Local\\Amazon Games\\App\\Amazon Games Remover.exe\" -m Game -p abc"));

            var jogos = JogosInstalados.Procurar(new AmbienteDeLojas { Registro = reg });

            Assert.Equal(new[] { "Fallout 76", "Overwatch" }, jogos.Select(j => j.Nome).ToArray());
            Assert.Equal(LojaDeJogos.Amazon, jogos[0].Loja);
            Assert.Equal(Path.GetFullPath(f76), jogos[0].Pasta);
            Assert.Equal(LojaDeJogos.BattleNet, jogos[1].Loja);
            Assert.Equal(Path.GetFullPath(ow), jogos[1].Pasta);
        }
        finally { Directory.Delete(raiz, true); }
    }

    // ------------------------------------------------------------ em conjunto

    [Fact]
    public void AMesmaPastaEmDuasLojasEntraUmaVezEALojaQuebradaNaoEscondeAsOutras()
    {
        var raiz = Pasta();
        try
        {
            var steam = Path.Combine(raiz, "Steam");
            var sa = Path.Combine(steam, "steamapps");
            var portal = Jogo(Path.Combine(sa, "common"), "Portal 2", "portal2.exe");
            Acf(sa, 620, "Portal 2", "Portal 2");

            // O registro da GOG explode; a Rockstar aponta a MESMA pasta da Steam (com barra no fim).
            var reg = new RegistroFalso { ChaveQueExplode = @"HKLM\SOFTWARE\WOW6432Node\GOG.com" }
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\1", ("gameName", "Nunca lido"), ("path", portal))
                .Chave(@"HKLM\SOFTWARE\WOW6432Node\Rockstar Games\Portal 2 (?)", ("InstallFolder", portal + Path.DirectorySeparatorChar));

            var jogos = JogosInstalados.Procurar(new AmbienteDeLojas { RaizDaSteam = steam, Registro = reg });

            var unico = Assert.Single(jogos);
            Assert.Equal("Portal 2", unico.Nome);
            Assert.Equal(LojaDeJogos.Steam, unico.Loja);   // a primeira fonte vence
            Assert.Equal(Path.GetFullPath(portal), unico.Pasta);
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void ConsolidarTiraDuplicatasEOrdenaPeloNome()
    {
        var raiz = Pasta();
        try
        {
            var a = Jogo(raiz, "Zelda", "z.exe");
            var b = Jogo(raiz, "abzu", "a.exe");
            var lista = JogosInstalados.Consolidar(new[]
            {
                new JogoInstalado("Zelda", a, LojaDeJogos.Steam),
                new JogoInstalado("ABZÛ", b, LojaDeJogos.Epic),
                new JogoInstalado("Zelda (GOG)", a + Path.DirectorySeparatorChar, LojaDeJogos.Gog),
                new JogoInstalado("   ", b, LojaDeJogos.Gog),
            });
            Assert.Equal(new[] { "ABZÛ", "Zelda" }, lista.Select(j => j.Nome).ToArray());
            Assert.Equal(Path.GetFullPath(a), lista[1].Pasta);
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void TemExecutavelOlhaAteDoisNiveisPorPadrao()
    {
        var raiz = Pasta();
        try
        {
            var doisNiveis = Jogo(raiz, "Dois", Path.Combine("bin", "x64", "jogo.exe"));
            var tresNiveis = Jogo(raiz, "Tres", Path.Combine("Game", "Binaries", "Win64", "jogo.exe"));
            var nenhum = Jogo(raiz, "Nenhum", null);
            File.WriteAllText(Path.Combine(nenhum, "leia-me.txt"), "x");

            Assert.True(JogosInstalados.TemExecutavel(doisNiveis));
            Assert.False(JogosInstalados.TemExecutavel(tresNiveis));
            Assert.True(JogosInstalados.TemExecutavel(tresNiveis, profundidadeMaxima: 3));
            Assert.False(JogosInstalados.TemExecutavel(nenhum));
            Assert.False(JogosInstalados.TemExecutavel(Path.Combine(raiz, "inexistente")));
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void SemNadaConfiguradoDevolveListaVaziaSemLancar()
    {
        var jogos = JogosInstalados.Procurar(new AmbienteDeLojas());
        Assert.Empty(jogos);
        Assert.Equal("EA app / Origin", JogosInstalados.NomeDaLoja(LojaDeJogos.Ea));
        Assert.Null(JogosInstalados.RelativoSePossivel(null, @"C:\Jogos\X"));
        Assert.Equal(@"bin\x.exe", JogosInstalados.RelativoSePossivel(@"C:\Jogos\X\bin\x.exe", @"C:\Jogos\X\"));
        Assert.Equal(@"D:\outro\x.exe", JogosInstalados.RelativoSePossivel(@"D:\outro\x.exe", @"C:\Jogos\X"));
    }
}
