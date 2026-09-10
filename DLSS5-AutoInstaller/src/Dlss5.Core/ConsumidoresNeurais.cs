using System.Text;

namespace Dlss5.Core;

/// <summary>Os motores de Neural Rendering que o instalador sabe montar, e as regras de cada um.</summary>
public static class Motores
{
    public const int PassesMin = 1;

    public static string Rotulo(NeuralEngine e) => e switch
    {
        NeuralEngine.RenodxDlssShortFuse => "RenoDX DLSS (ShortFuse) — 1 a 10 passadas (64-bit direto; 32-bit dentro do host64 — o x2+ que funcionou)",
        NeuralEngine.OptiScalerNr => "OptiScaler DLSS-NR no host64 — 32-bit, 1 a 5 passadas (no SH2 EE as passadas não fizeram diferença visível)",
        NeuralEngine.DeepFriedChicken => "Deep Fried Chicken no host64 — 32-bit, 1 a 30 passadas (arquivos do Discord)",
        _ => "RenoDX DLSS5 (Krish) + Feeder — uma passada (padrão até aqui)",
    };

    public static int PassesMax(NeuralEngine e) => e switch
    {
        NeuralEngine.RenodxDlssShortFuse => ShortFuseDlss.PassesMax,
        NeuralEngine.OptiScalerNr => OptiScalerNr.PassesMax,
        NeuralEngine.DeepFriedChicken => DeepFriedChicken.PassesMax,
        _ => 1,
    };

    public static int Limitar(NeuralEngine e, int passes) => Math.Clamp(passes, PassesMin, Math.Max(PassesMin, PassesMax(e)));

    /// <summary>Os motores que fazem sentido para a arquitetura, na ordem da tela.</summary>
    public static IReadOnlyList<NeuralEngine> Disponiveis(PeArchitecture arch) => arch == PeArchitecture.X86
        ? new[] { NeuralEngine.RenodxDlss5Feeder, NeuralEngine.RenodxDlssShortFuse, NeuralEngine.OptiScalerNr, NeuralEngine.DeepFriedChicken }
        : new[] { NeuralEngine.RenodxDlss5Feeder, NeuralEngine.RenodxDlssShortFuse };

    public static bool Aplicavel(NeuralEngine e, PeArchitecture arch) => Disponiveis(arch).Contains(e);
}

/// <summary>Leitura e escrita de chave em texto no formato ini, sem tocar no resto.</summary>
public static class IniTexto
{
    public static string? Ler(string? texto, string secao, string chave)
    {
        if (string.IsNullOrEmpty(texto)) return null;
        bool dentro = false;
        foreach (var bruta in texto.Replace("\r\n", "\n").Split('\n'))
        {
            var linha = bruta.Trim();
            if (linha.StartsWith('['))
            {
                dentro = linha.Equals("[" + secao + "]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!dentro || linha.StartsWith(';') || linha.StartsWith('#')) continue;
            int eq = linha.IndexOf('=');
            if (eq < 0) continue;
            if (linha[..eq].Trim().Equals(chave, StringComparison.OrdinalIgnoreCase)) return linha[(eq + 1)..].Trim();
        }
        return null;
    }

    /// <summary>Troca a linha se existe na seção, acrescenta na seção se ela existe, ou cria a seção no fim.</summary>
    public static string Definir(string? texto, string secao, string chave, string valor)
    {
        texto ??= "";
        var quebra = texto.Contains("\r\n") || texto.Length == 0 ? "\r\n" : "\n";
        var linhas = texto.Replace("\r\n", "\n").Split('\n').ToList();
        if (linhas.Count > 0 && linhas[^1].Length == 0) linhas.RemoveAt(linhas.Count - 1);
        var nova = $"{chave}={valor}";
        int cabecalho = -1, fim = -1;
        bool dentro = false;
        for (int i = 0; i < linhas.Count; i++)
        {
            var linha = linhas[i].Trim();
            if (linha.StartsWith('['))
            {
                if (dentro) { fim = i; break; }
                dentro = linha.Equals("[" + secao + "]", StringComparison.OrdinalIgnoreCase);
                if (dentro) cabecalho = i;
                continue;
            }
            if (!dentro || linha.StartsWith(';') || linha.StartsWith('#')) continue;
            int eq = linha.IndexOf('=');
            if (eq >= 0 && linha[..eq].Trim().Equals(chave, StringComparison.OrdinalIgnoreCase))
            {
                linhas[i] = nova;
                return string.Join(quebra, linhas) + quebra;
            }
        }
        if (cabecalho >= 0)
        {
            int pos = fim < 0 ? linhas.Count : fim;
            while (pos - 1 > cabecalho && linhas[pos - 1].Trim().Length == 0) pos--;
            linhas.Insert(pos, nova);
            return string.Join(quebra, linhas) + quebra;
        }
        if (linhas.Count > 0 && linhas[^1].Trim().Length != 0) linhas.Add("");
        linhas.Add("[" + secao + "]");
        linhas.Add(nova);
        return string.Join(quebra, linhas) + quebra;
    }
}

/// <summary>
/// OptiScaler DLSS-NR (a linha Dagherbou/OptiScaler_DLSSNR; o kit traz o OptiScaler v10.0.0-pre1 de
/// 04/09/2026, do 7z do Discord, porque é o build que tem a chave Passes — o fork v0.2.0-patch1 do
/// GitHub faz UMA passada só): o Feeder 0.15 o aceita como terceiro consumidor neural. Entra como winmm.dll (o host64 importa winmm.dll e version.dll no início),
/// com o nvngx.dll_dlssnr.dll (o modelo recusa chamador cujo caminho não contenha "nvngx.dll")
/// e o OptiScaler.ini com a seção [DlssNr] ligada. "Passes" é quantas vezes o modelo roda sobre o
/// quadro, cada passada vendo a anterior — o "x2" em jogo 32-bit. O menu dele abre com Insert,
/// na janela do host (painel projetado ou host_window=1).
/// </summary>
public static class OptiScalerNr
{
    public const string Dll = "OptiScaler.dll";
    /// <summary>Nome com que a DLL entra no host64: o auxiliar importa winmm.dll ao iniciar.</summary>
    public const string Proxy = "winmm.dll";
    public const string Shim = "nvngx.dll_dlssnr.dll";
    public const string Ini = "OptiScaler.ini";
    public const string Log = "OptiScaler.log";
    public const string AgilityDll = "D3D12Core.dll";
    /// <summary>Subpasta onde o OptiScaler procura o Agility SDK.</summary>
    public const string AgilityRel = "OptiScaler\\D3D12_OptiScaler";
    public const int PassesMax = 5;
    /// <summary>Marca que prova que uma DLL é o OptiScaler (o instalador oficial usa a mesma).</summary>
    public const string Marca = "OptiScaler.ini";

    /// <summary>As chaves que o instalador oficial do Feeder grava para o fork servir de consumidor.</summary>
    public static readonly (string Secao, string Chave, string Valor)[] ChavesDoFeed =
    {
        ("DlssNr", "Enabled", "true"), ("DlssNr", "ScanExposure", "false"),
        ("Upscalers", "Dx12Upscaler", "dlss"),
        ("Log", "LogToFile", "true"), ("Log", "LogLevel", "2"),
        ("Spoofing", "Dxgi", "false"), ("Spoofing", "StreamlineSpoofing", "false"),
        ("Inputs", "EnableXeSSInputs", "false"), ("Inputs", "EnableFsr2Inputs", "false"),
        ("Inputs", "EnableFsr3Inputs", "false"), ("Inputs", "EnableFfxInputs", "false"),
        ("Hotfix", "CheckForUpdate", "false"),
        // O OptiScaler (winmm.dll) carrega o dxgi.dll do System32 antes do host pedir o seu, e o
        // ReShade x64 que está em host64\dxgi.dll nunca entra ("dxgi.dll here is Windows' own ...
        // there is no overlay" no dlss5-feed-host.log): a tecla Home na janela do host não abre nada.
        // O próprio OptiScaler resolve: com LoadReshade=true ele carrega um ReShade64.dll da pasta.
        ("Plugins", "LoadReshade", "true"),
    };

    /// <summary>ReShade x64 com o nome que o OptiScaler carrega por LoadReshade=true (ao lado dele, em host64\).</summary>
    public const string ReShade64 = "ReShade64.dll";

    /// <summary>O OptiScaler.ini do kit com o que o feed precisa e as passadas pedidas.</summary>
    public static string GerarIni(string? original, int passes)
    {
        var texto = original ?? "";
        foreach (var (secao, chave, valor) in ChavesDoFeed)
            texto = IniTexto.Definir(texto, secao, chave, valor);
        texto = IniTexto.Definir(texto, "DlssNr", "Passes", Math.Clamp(passes, Motores.PassesMin, PassesMax).ToString());
        return texto;
    }

    public static int? LerPassadas(string? ini) =>
        int.TryParse(IniTexto.Ler(ini, "DlssNr", "Passes"), out var v) ? v : null;

    /// <summary>
    /// O build do OptiScaler entende passadas múltiplas? Só se o OptiScaler.ini dele traz a chave
    /// Passes em [DlssNr]. O fork v0.2.0-patch1 não traz (e ignora a chave gravada: o Feeder mostra
    /// "Passes=2" lendo o nosso ini, mas o modelo roda uma vez); o v10.0.0-pre1 traz ("1 to 5").
    /// </summary>
    public static bool SuportaPassadas(string? iniDoKit) => IniTexto.Ler(iniDoKit, "DlssNr", "Passes") is not null;

    /// <summary>Enabled=true na [DlssNr]. "auto" é falso: o fork nunca sai ligado.</summary>
    public static bool Ligado(string? ini) =>
        string.Equals(IniTexto.Ler(ini, "DlssNr", "Enabled"), "true", StringComparison.OrdinalIgnoreCase);

    public static string PassoManual(int passes) =>
        $"O OptiScaler mora dentro do host64 (é 64-bit). O painel do Feeder (Home → Complementos → DLSS 5 Feed) só MOSTRA " +
        "as chaves dele, em leitura: as passadas não se mudam ali. Mudam em três lugares: (1) neste programa, campo " +
        $"\"Passadas\" + Instalar de novo (regrava o host64\\OptiScaler.ini); (2) no menu do próprio OptiScaler — marque " +
        "\"Show the DLSS 5 host window\" no painel do Feeder e aperte Insert NA JANELA DO HOST, seção DLSS Neural " +
        $"Rendering, controle Passes; (3) editando [DlssNr] Passes={passes} no host64\\OptiScaler.ini e clicando " +
        "\"Restart the DLSS 5 host\" no painel. A prova de que rodou é o host64\\OptiScaler.log: a linha \"DLSS-NR " +
        $"composition: ... model WxH x{passes} pass(es)\" (a verificação, item 25, lê isso); \"running one pass\" ali " +
        "quer dizer que a passada extra não coube (VRAM) ou não construiu. Cada passada custa o mesmo que a primeira.";

    /// <summary>Marcador que só o build com passadas múltiplas (v10.0.0-pre1) carrega; o fork v0.2.0-patch1 não.</summary>
    public const string MarcaPassadas = "pass(es)";

    /// <summary>
    /// Quantas passadas o OptiScaler DE FATO rodou, pelo host64\OptiScaler.log: a linha
    /// "DLSS-NR composition: ... model 2560x1440 x2 pass(es)". Última ocorrência; null se nunca avaliou.
    /// </summary>
    public static int? PassadasNoLog(string? log)
    {
        if (string.IsNullOrEmpty(log)) return null;
        // A linha "composition" sai UMA vez, no primeiro quadro, quando só a passada 1 existe;
        // as extras chegam depois ("pass 2 built at ...", "pass 3 built ...") e o custo de GPU
        // do host sobe junto. Então: o maior número entre a composition e as passadas construídas.
        int? ultimo = null;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     log, @"model \d+x\d+ x(\d+) pass\(es\)"))
            if (int.TryParse(m.Groups[1].Value, out var n)) ultimo = n;
        int maiorConstruida = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     log, @"DLSS-NR: pass (\d+) built at"))
            if (int.TryParse(m.Groups[1].Value, out var n) && n > maiorConstruida) maiorConstruida = n;
        if (maiorConstruida > 0 && (ultimo is null || maiorConstruida > ultimo.Value)) ultimo = maiorConstruida;
        return ultimo;
    }

    /// <summary>A linha do log que explica por que uma passada pedida não rodou (ou null).</summary>
    public static string? MotivoDePassadaPerdida(string? log)
    {
        if (string.IsNullOrEmpty(log)) return null;
        string? ultima = null;
        foreach (var raw in log.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.Contains("running one pass", StringComparison.OrdinalIgnoreCase)
                || l.Contains("would not build", StringComparison.OrdinalIgnoreCase)
                || l.Contains("waiting on video memory", StringComparison.OrdinalIgnoreCase)
                || (l.Contains("DLSS-NR: pass", StringComparison.OrdinalIgnoreCase) && l.Contains("returned 0x", StringComparison.OrdinalIgnoreCase))
                || l.Contains("no longer findable", StringComparison.OrdinalIgnoreCase))
                ultima = l.Trim();
        }
        return ultima;
    }
}

/// <summary>
/// O renodx-dlss do ShortFuse DENTRO do host64 (jogo 32-bit) — VALIDADO pelo usuário no Silent Hill 2
/// Enhanced Edition em 09/09/2026 (foi o único motor em que o x2+ apareceu na tela; o OptiScaler
/// construía as passadas no log sem diferença visível). O addon é 64-bit e
/// se pendura no NVSDK_NGX_D3D12_EvaluateFeature do processo em que vive; o host64 faz exatamente
/// essa chamada (DLAA sintético) para cada quadro do jogo. A ideia: o ReShade x64 do host64 carrega
/// o addon (LoadFromDllMain), ele intercepta o evaluate do host e roda as N passadas no lugar do
/// Krish. O Feeder não o reconhece como consumidor ("renodx-dlss5*.addon64 not found next to the
/// host" e o host segue servindo DLAA), e o autor do Feeder diz que em 64-bit o addon SUBSTITUI o
/// projeto em vez de trabalhar com ele; dentro do host ninguém mediu. Fica como opção para quem
/// quer x6–x10 em 32-bit e aceita testar. Se a imagem não mudar ou o host cair, volte ao OptiScaler.
/// </summary>
public static class ShortFuseNoHost64
{
    public const string Ini = "ReShade.ini";

    /// <summary>host64\ReShade.ini com o addon carregado cedo e as passadas pedidas; o resto do ini (o host grava chaves nele) fica.</summary>
    public static string GerarIni(string? existente, int passes)
    {
        var texto = string.IsNullOrWhiteSpace(existente) ? "" : existente!;
        texto = IniTexto.Definir(texto, "ADDON", "LoadFromDllMain", ShortFuseDlss.Addon);
        texto = IniTexto.Definir(texto, ShortFuseDlss.Secao, ShortFuseDlss.ChavePassadas, ShortFuseDlss.Limitar(passes).ToString());
        return texto;
    }

    public static int? LerPassadas(string? ini) =>
        int.TryParse(IniTexto.Ler(ini, ShortFuseDlss.Secao, ShortFuseDlss.ChavePassadas), out var v) ? v : null;

    public static bool CarregaCedo(string? ini) =>
        (IniTexto.Ler(ini, "ADDON", "LoadFromDllMain") ?? "").Contains(ShortFuseDlss.Addon, StringComparison.OrdinalIgnoreCase);

    public static string PassoManual(int passes) =>
        $"O renodx-dlss.addon64 do ShortFuse mora dentro do host64 e intercepta a chamada de DLSS que o " +
        $"host faz para cada quadro; o host64\\ReShade.ini pede {passes} passada(s) ([RENODX-DLSS] DirectNeuralRenderingPassCount). " +
        "Prova: host64\\ReShade.log com \"Registered add-on \"RenoDX DLSS\"\", \"RenoDX DLSS attached\" e \"DLSS-NR source " +
        "evaluation completed\" (a verificação, item 25, lê). O painel dele abre com Home NA JANELA DO HOST (marque \"Show the " +
        "DLSS 5 host window\" no painel do Feeder): aba RenoDX DLSS → Advanced → Pass Count. O Feeder não o conhece como " +
        "consumidor (o host diz \"renodx-dlss5*.addon64 not found\" e segue servindo DLAA), mas foi o motor em que o x2+ " +
        "apareceu de fato (Silent Hill 2 EE, 09/09/2026). DUAS ARMADILHAS no painel do Feeder: (1) a seção \"DLSS 5 " +
        "neural-rendering settings (on the host)\" (NR Preset, Style, Intensity...) é do addon do KRISH — o ShortFuse ignora " +
        "tudo ali; as opções dele ficam na aba RenoDX DLSS da janela do host; (2) NÃO use \"Apply to the DLSS 5 host\" nem " +
        "\"Restart the DLSS 5 host\": o host novo morre no primeiro quadro (\"D3D12 device was removed 0x887A0001\", Silent " +
        "Hill Homecoming, 10/09/2026) e cada tentativa repete. Para mudar qualquer coisa, feche e abra o jogo. Se o host cair " +
        "logo na primeira abertura, teste menos passadas antes de trocar de motor.";
}

/// <summary>Leitura do host64\\dlss5-feed-host.log: o device D3D12 do host morreu?</summary>
public static class HostLog
{
    /// <summary>O código DXGI da remoção ("0x887A0006") e a explicação, ou null se o log não a registrou.</summary>
    public static (string Codigo, string Explicacao)? DeviceRemovido(string? log)
    {
        if (string.IsNullOrEmpty(log)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(log, @"the D3D12 device was removed \((0x[0-9A-Fa-f]{8})");
        if (!m.Success) return null;
        var codigo = m.Groups[1].Value.ToUpperInvariant().Replace("0X", "0x");
        var explicacao = codigo switch
        {
            "0x887A0006" => "DEVICE_HUNG: a GPU travou numa avaliação (o projeto do Feeder acompanha em issues/57).",
            "0x887A0007" => "DEVICE_RESET: o driver reiniciou a GPU (TDR).",
            "0x887A0005" => "DEVICE_REMOVED: o driver derrubou o device.",
            "0x887A0001" => "INVALID_CALL: o driver recusou uma chamada no primeiro quadro do host — o padrão visto quando o host é " +
                            "REINICIADO pelo painel (\"Restart\"/\"Apply to the DLSS 5 host\") com o RenoDX DLSS (ShortFuse) dentro dele " +
                            "(Silent Hill Homecoming, 10/09/2026: o primeiro host rodou 5 min; cada host reiniciado morreu em 130 ms).",
            "0x887A0020" => "DRIVER_INTERNAL_ERROR: erro interno do driver.",
            _ => "código DXGI não catalogado.",
        };
        return (codigo, explicacao);
    }

    /// <summary>Quantas vezes o addon do jogo viu o host morrer ("host lost: frame message failed").</summary>
    public static int HostsPerdidos(string? feedLog)
    {
        if (string.IsNullOrEmpty(feedLog)) return 0;
        return System.Text.RegularExpressions.Regex.Matches(feedLog, @"host lost: frame message failed").Count;
    }
}

/// <summary>
/// Deep Fried Chicken (Alexander): o consumidor que o Feeder recomenda, até 30 passadas. Não tem
/// download público — os três arquivos vêm do Discord do autor e o usuário os põe no kit. Em jogo
/// 32-bit vão para host64\. O cfg é chave=valor sem seção; o Feeder lê arm, enabled e layers.
/// </summary>
public static class DeepFriedChicken
{
    public const string Addon = "deep-fried-chicken.addon64";
    public const string Nvngx = "deep-fried-chicken-nvngx.dll";
    public const string Cfg = "deep-fried-chicken.cfg";
    public const int PassesMax = 30;
    public const string Discord = "https://discord.gg/g2v2XGqvR";

    /// <summary>O cfg do kit com as passadas pedidas; enabled=1 e arm=1 garantidos.</summary>
    public static string GerarCfg(string? original, int passes)
    {
        var texto = original ?? "";
        texto = DefinirChave(texto, "layers", Math.Clamp(passes, Motores.PassesMin, PassesMax).ToString());
        if (LerChave(texto, "enabled") is null) texto = DefinirChave(texto, "enabled", "1");
        if (LerChave(texto, "arm") is null) texto = DefinirChave(texto, "arm", "1");
        return texto;
    }

    public static int? LerPassadas(string? cfg) => int.TryParse(LerChave(cfg, "layers"), out var v) ? v : null;
    public static bool Armado(string? cfg) => LerChave(cfg, "arm") is not "0";

    public static string? LerChave(string? cfg, string chave)
    {
        if (string.IsNullOrEmpty(cfg)) return null;
        foreach (var bruta in cfg.Replace("\r\n", "\n").Split('\n'))
        {
            var linha = bruta.Trim();
            if (linha.Length == 0 || linha.StartsWith(';') || linha.StartsWith('#') || linha.StartsWith('[')) continue;
            int eq = linha.IndexOf('=');
            if (eq < 0) continue;
            if (linha[..eq].Trim().Equals(chave, StringComparison.OrdinalIgnoreCase)) return linha[(eq + 1)..].Trim();
        }
        return null;
    }

    public static string DefinirChave(string? cfg, string chave, string valor)
    {
        cfg ??= "";
        var quebra = cfg.Contains("\r\n") || cfg.Length == 0 ? "\r\n" : "\n";
        var linhas = cfg.Replace("\r\n", "\n").Split('\n').ToList();
        if (linhas.Count > 0 && linhas[^1].Length == 0) linhas.RemoveAt(linhas.Count - 1);
        for (int i = 0; i < linhas.Count; i++)
        {
            var linha = linhas[i].Trim();
            if (linha.StartsWith(';') || linha.StartsWith('#') || linha.StartsWith('[')) continue;
            int eq = linha.IndexOf('=');
            if (eq >= 0 && linha[..eq].Trim().Equals(chave, StringComparison.OrdinalIgnoreCase))
            {
                linhas[i] = $"{chave}={valor}";
                return string.Join(quebra, linhas) + quebra;
            }
        }
        linhas.Add($"{chave}={valor}");
        return string.Join(quebra, linhas) + quebra;
    }

    public static string PassoManual(int passes) =>
        "O Windows Defender costuma apagar o deep-fried-chicken.addon64 segundos depois da cópia (ele usa Detours no NGX): " +
        "se o arquivo sumir do host64\\, restaure em Segurança do Windows → Histórico de proteção e adicione a pasta do jogo às " +
        $"exclusões. O painel do Chicken fica na janela do host (painel projetado, aba Deep Fried Chicken); o estado tem que ser ARMED. " +
        $"Passadas: {passes} (layers={passes} no host64\\deep-fried-chicken.cfg). Comece com 1 e suba uma de cada vez.";
}

/// <summary>O que o host64 --test do Feeder respondeu.</summary>
public sealed record HostTestResultado(int? Sucessos, int Total, bool FalhaNoDriver, bool NaoEhNvidia, string Trecho)
{
    public bool Passou => Sucessos is not null && Sucessos == Total && Total > 0;

    public string Titulo => Passou ? $"Passou: {Sucessos}/{Total} avaliações"
        : NaoEhNvidia ? "A GPU não é NVIDIA"
        : FalhaNoDriver ? "Falhou dentro do NGX do driver"
        : Sucessos is null ? "Sem veredito" : $"Falhou: {Sucessos}/{Total} avaliações";

    public string Texto => Passou
        ? "Driver, runtimes e consumidor neural trabalham juntos: o host montou um DLAA sintético sem jogo nenhum e o Neural Rendering " +
          "avaliou todos os quadros. Se um jogo 32-bit ainda não mostra efeito, o problema está no jogo (depth, vetores, feed), não no host."
        : FalhaNoDriver
            ? "O host reproduziu o defeito conhecido: \"evaluate raised 0xC0000005\" dentro do NGX do driver. É o addon do Krish 4.6/4.7 " +
              "com driver NVIDIA 616.64 ou mais novo (o projeto do Feeder mediu 0/300 nessa combinação). Três saídas, qualquer uma: " +
              "usar o consumidor OptiScaler DLSS-NR ou o Deep Fried Chicken no host64 (motor na tela de detecção), pôr o addon 4.55 " +
              "(\"classic engine\", na pasta versoes-anteriores do kit) no lugar do 4.70, ou voltar o driver para 616.56."
            : NaoEhNvidia
                ? "NGX é o runtime da NVIDIA: nenhuma combinação de arquivos faz o DLSS 5 rodar nesta placa."
                : "Leia as últimas linhas abaixo: o host diz em que módulo parou.";
}

public static class HostTest
{
    public static HostTestResultado Ler(string? saida)
    {
        saida ??= "";
        int? ok = null; int total = 300;
        var m = System.Text.RegularExpressions.Regex.Match(saida, @"--test finished:\s*(\d+)/(\d+)\s*evaluates succeeded");
        if (m.Success) { ok = int.Parse(m.Groups[1].Value); total = int.Parse(m.Groups[2].Value); }
        bool driver = saida.Contains("evaluate raised 0xC0000005", StringComparison.OrdinalIgnoreCase);
        bool naoNvidia = saida.Contains("this is not an NVIDIA GPU", StringComparison.OrdinalIgnoreCase);
        var linhas = saida.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        var trecho = string.Join("\r\n", linhas.Skip(Math.Max(0, linhas.Count - 12)));
        return new HostTestResultado(ok, total, driver, naoNvidia, trecho);
    }
}
