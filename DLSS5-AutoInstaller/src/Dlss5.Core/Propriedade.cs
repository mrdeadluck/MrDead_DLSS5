namespace Dlss5.Core;

/// <summary>De quem é um arquivo encontrado na pasta do jogo.</summary>
public enum OrigemDoArquivo
{
    /// <summary>Gravado por este programa nesta instalação (manifesto + hash conferem).</summary>
    Nosso,
    /// <summary>Tem cara de nosso (nome exclusivo do kit ou texto do ReShade/dgVoodoo dentro), mas sem manifesto que confirme.</summary>
    NossoLegado,
    /// <summary>Estava lá antes de nós ou foi trocado depois: do jogo, de outro mod ou do usuário.</summary>
    DoJogoOuTerceiro,
}

/// <summary>
/// Regras de propriedade de arquivo, num lugar só. Instalação, reversão, faxina e
/// inspetor de estado precisam responder a mesma pergunta — "posso apagar isto?" — e
/// precisam responder igual.
///
/// O critério é conservador de propósito: só sai o que não tem como ser do jogo.
/// </summary>
public static class Propriedade
{
    public const string BackupSuffix = ".dlss5bak";
    public const string TempSuffix = ".dlss5tmp";
    public const string PrevSuffix = ".dlss5prev";

    /// <summary>Nomes que só este programa põe numa pasta de jogo. Nenhum jogo traz nada disso.</summary>
    public static readonly string[] SoNossos =
    {
        "renodx-dlss5.addon64", "dlss5-feed.addon64", "dlss5-feed.addon32",
        "dlss5-feed-host64.exe", "dlss5-feed.cfg", "dlss5-feed.log", "dlss5-feed-host.log",
        "nvngx_dlssnr.dll",
        // O dgVoodoo encadeado atrás do DxWrapper: nenhum jogo traz um arquivo com esse nome.
        "dgVoodoo_D3D9.dll", "dgVoodoo_D3D8.dll",
        "ReShade.ini", "ReShade.log", "ReShadePreset.ini",
        "ReShade64.json", "ReShade32.json", "ReShade64_XR.json", "ReShade32_XR.json",
        InstallManifest.FileName,
    };

    /// <summary>Nomes que o ReShade e o Feeder criam ao rodar, e que o manifesto não conhece.</summary>
    public static readonly string[] RestosDeExecucao =
    {
        "ReShade.log", "ReShade64.json", "ReShade32.json",
        "ReShade64_XR.json", "ReShade32_XR.json",
        "dlss5-feed.log", "dlss5-feed.cfg", "dlss5-feed-host.log",
    };

    /// <summary>
    /// dgVoodoo é dele mesmo: existe jogo antigo que já vem com o dgVoodoo instalado de
    /// fábrica. Estes só saem se houver instalação nossa na mesma pasta ou na de cima.
    /// </summary>
    public static readonly string[] DgVoodooComEscolta = { "dgVoodoo.conf", "dgVoodooCpl.exe" };

    /// <summary>
    /// Nomes genéricos demais para sair só pelo nome: só saem se o texto dentro do
    /// arquivo provar de onde vieram.
    /// </summary>
    public static readonly (string Nome, string Prova)[] PrecisamDeProva =
    {
        ("dxgi.dll", "ReShade"),
        ("opengl32.dll", "ReShade"),
        // Nomes alternativos do ReShade, usados em jogo que recusa o dxgi.dll (MGS V).
        // Sem eles aqui, a desinstalação deixaria para trás justamente o arquivo que
        // impede o jogo de abrir.
        ("d3d11.dll", "ReShade"),
        ("d3d12.dll", "ReShade"),
    };

    public static readonly (string Nome, string Prova)[] PrecisamDeProvaEEscolta =
    {
        ("D3D9.dll", "dgVoodoo"),
        ("D3D8.dll", "dgVoodoo"),
    };

    /// <summary>Arquivos que provam que ESTE programa instalou nesta pasta.</summary>
    public static readonly string[] ProvasDoKit =
    {
        "nvngx_dlssnr.dll", "renodx-dlss5.addon64", "dlss5-feed.addon64",
        "dlss5-feed.addon32", "dlss5-feed-host64.exe",
    };

    public static readonly string[] PastasNossas = { "host64", "reshade-shaders" };

    /// <summary>
    /// Arquivos que outros mods/injetores usam e que podem disputar o mesmo gancho.
    /// Só viram aviso — nunca são tocados.
    /// </summary>
    public static readonly (string Nome, string Descricao)[] OutrosInjetores =
    {
        ("d3d11.dll", "wrapper de D3D11 (outro mod ou injector)"),
        ("d3d12.dll", "wrapper de D3D12 (outro mod ou injector)"),
        ("dinput8.dll", "proxy dinput8 (ASI loader, Script Hook, outros mods)"),
        ("version.dll", "proxy version.dll (mods/loaders)"),
        ("winmm.dll", "proxy winmm.dll (mods/loaders)"),
        ("xinput1_3.dll", "proxy xinput (mods/loaders)"),
        ("nvngx.dll", "nvngx.dll falso (OptiScaler/FSR-em-DLSS)"),
        ("OptiScaler.ini", "OptiScaler"),
        ("SpecialK64.dll", "Special K"),
        ("SpecialK32.dll", "Special K"),
        ("dlssg_to_fsr3_amd_is_better.dll", "DLSS-G para FSR3"),
    };

    private const long OrcamentoProva = 96L * 1024 * 1024;

    /// <summary>Texto dentro do arquivo denuncia de onde ele veio?</summary>
    public static bool ContemTexto(string caminho, string texto)
    {
        try { return ApiDetector.ScanForMarkers(caminho, new[] { texto }, OrcamentoProva).Contains(texto); }
        catch { return false; }
    }

    /// <summary>Há sinal de instalação nossa nesta pasta?</summary>
    public static bool TemSinalNosso(string pasta)
    {
        try
        {
            return ProvasDoKit.Any(p => File.Exists(Path.Combine(pasta, p)))
                   || File.Exists(Path.Combine(pasta, "ReShade.ini"))
                   || File.Exists(Path.Combine(pasta, InstallManifest.FileName));
        }
        catch { return false; }
    }

    /// <summary>Há sinal de instalação nossa nesta pasta ou na de cima?</summary>
    public static bool TemInstalacaoNossaPorPerto(string pasta)
    {
        if (TemSinalNosso(pasta)) return true;
        try
        {
            var pai = Directory.GetParent(pasta)?.FullName;
            return pai is not null && TemSinalNosso(pai);
        }
        catch { return false; }
    }

    /// <summary>host64\ e reshade-shaders\ só são nossas se houver instalação nossa junto.</summary>
    public static bool PastaEhNossa(string pastaAlvo, string pastaPai) =>
        ProvasDoKit.Any(p => File.Exists(Path.Combine(pastaAlvo, p))) || TemSinalNosso(pastaPai);

    /// <summary>Nomes exclusivos do kit DLSS 5 (nunca de um ReShade instalado à parte pelo usuário).</summary>
    private static readonly string[] ExclusivosDoKit =
    {
        "renodx-dlss5.addon64", "dlss5-feed.addon64", "dlss5-feed.addon32",
        "dlss5-feed-host64.exe", "dlss5-feed.cfg", "dlss5-feed.log", "dlss5-feed-host.log",
        "nvngx_dlssnr.dll", "dgVoodoo_D3D9.dll", "dgVoodoo_D3D8.dll", InstallManifest.FileName,
    };

    /// <summary>Há peça do kit (não só ReShade) nesta pasta ou na de cima?</summary>
    public static bool TemProvaDoKitPorPerto(string pasta)
    {
        static bool Em(string p)
        {
            try { return ProvasDoKit.Any(n => File.Exists(Path.Combine(p, n))) || File.Exists(Path.Combine(p, InstallManifest.FileName)); }
            catch { return false; }
        }
        if (Em(pasta)) return true;
        try
        {
            var pai = Directory.GetParent(pasta)?.FullName;
            return pai is not null && Em(pai);
        }
        catch { return false; }
    }

    /// <summary>
    /// Sem manifesto: pelo nome e pelo conteúdo, este arquivo tem como ser de outra
    /// coisa que não este programa? Devolve true só quando a resposta é "não tem".
    /// </summary>
    /// <param name="exigirProvaDoKit">
    /// Antes de INSTALAR, um ReShade.ini ou dxgi.dll do ReShade sem nenhuma peça do kit
    /// por perto é um ReShade que o usuário instalou por conta própria — e aí é original
    /// a preservar, não vestígio nosso. Na remoção conservadora (que o usuário confirma
    /// vendo a lista) a exigência não se aplica.
    /// </param>
    public static bool EhNossoPorHeuristica(string caminho, bool exigirProvaDoKit = false)
    {
        var nome = Path.GetFileName(caminho);
        var pasta = Path.GetDirectoryName(caminho) ?? "";

        if (ExclusivosDoKit.Any(n => n.Equals(nome, StringComparison.OrdinalIgnoreCase))) return true;
        if (exigirProvaDoKit && !TemProvaDoKitPorPerto(pasta)) return false;

        if (SoNossos.Any(n => n.Equals(nome, StringComparison.OrdinalIgnoreCase))) return true;

        foreach (var (n, prova) in PrecisamDeProva)
            if (n.Equals(nome, StringComparison.OrdinalIgnoreCase))
                return File.Exists(caminho) && ContemTexto(caminho, prova);

        bool comEscolta = TemInstalacaoNossaPorPerto(pasta);
        foreach (var (n, prova) in PrecisamDeProvaEEscolta)
            if (n.Equals(nome, StringComparison.OrdinalIgnoreCase))
                return comEscolta && File.Exists(caminho) && ContemTexto(caminho, prova);

        if (DgVoodooComEscolta.Any(n => n.Equals(nome, StringComparison.OrdinalIgnoreCase)))
            return comEscolta;

        // Dentro das nossas pastas (shaders/host64) tudo que veio do kit é nosso; um
        // arquivo que o usuário pôs lá não tem como ser reconhecido — e fica.
        return false;
    }

    /// <summary>O arquivo está dentro de uma das pastas que criamos (host64\, reshade-shaders\)?</summary>
    public static bool EstaEmPastaNossa(string caminho)
    {
        var sep = Path.DirectorySeparatorChar;
        foreach (var p in PastasNossas)
            if (caminho.Contains($"{sep}{p}{sep}", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Classifica um arquivo existente. O manifesto decide quando existe e confere;
    /// sem ele, a heurística; e o resto é do jogo ou de terceiros.
    /// </summary>
    public static OrigemDoArquivo Classificar(string caminho, InstallManifest? manifesto, bool paraInstalar = false)
    {
        if (manifesto is not null)
        {
            if (manifesto.Files.ContainsKey(caminho))
            {
                var conf = manifesto.ConferirGravado(caminho);
                if (conf == ConferenciaDeArquivo.Igual) return OrigemDoArquivo.Nosso;
                // Mudou depois que gravamos: se o nome é exclusivo do kit continua sendo
                // nosso (o Feeder regrava o cfg, por exemplo); senão, alguém trocou.
                return EhNossoPorHeuristica(caminho) ? OrigemDoArquivo.NossoLegado : OrigemDoArquivo.DoJogoOuTerceiro;
            }
            if (manifesto.AddedFiles.Contains(caminho, StringComparer.OrdinalIgnoreCase))
            {
                // Manifesto antigo, sem hash: o nome decide.
                return EhNossoPorHeuristica(caminho) || EstaEmPastaNossa(caminho)
                    ? OrigemDoArquivo.NossoLegado
                    : OrigemDoArquivo.DoJogoOuTerceiro;
            }
        }
        return EhNossoPorHeuristica(caminho, exigirProvaDoKit: paraInstalar)
            ? OrigemDoArquivo.NossoLegado
            : OrigemDoArquivo.DoJogoOuTerceiro;
    }
}
