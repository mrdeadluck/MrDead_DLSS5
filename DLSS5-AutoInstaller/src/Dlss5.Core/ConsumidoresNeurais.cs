using System.Text;

namespace Dlss5.Core;

/// <summary>Os motores de Neural Rendering que o instalador sabe montar, e as regras de cada um.</summary>
public static class Motores
{
    public const int PassesMin = 1;

    public static string Rotulo(NeuralEngine e) => e switch
    {
        NeuralEngine.RenodxDlssShortFuse => "RenoDX DLSS (ShortFuse) — 64-bit, 1 a 10 passadas",
        NeuralEngine.OptiScalerNr => "OptiScaler DLSS-NR no host64 — 32-bit, 1 a 5 passadas",
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
        ? new[] { NeuralEngine.RenodxDlss5Feeder, NeuralEngine.OptiScalerNr, NeuralEngine.DeepFriedChicken }
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
/// OptiScaler DLSS-NR (fork Dagherbou/OptiScaler_DLSSNR): o Feeder 0.15 o aceita como terceiro
/// consumidor neural. Entra como winmm.dll (o host64 importa winmm.dll e version.dll no início),
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
    };

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

    /// <summary>Enabled=true na [DlssNr]. "auto" é falso: o fork nunca sai ligado.</summary>
    public static bool Ligado(string? ini) =>
        string.Equals(IniTexto.Ler(ini, "DlssNr", "Enabled"), "true", StringComparison.OrdinalIgnoreCase);

    public static string PassoManual(int passes) =>
        $"O OptiScaler mora dentro do host64 (é 64-bit) e o menu dele abre com a tecla Insert NA JANELA DO HOST: no jogo, " +
        "Home → Complementos → DLSS 5 Feed → \"Show the DLSS 5 panel in-game\" (ou host_window=1 no dlss5-feed.cfg para o " +
        $"auxiliar ter janela própria). Em \"DLSS Neural Rendering\", \"Enable Neural Rendering\" tem que estar marcado e Passes em {passes} " +
        "— o instalador gravou os dois no host64\\OptiScaler.ini. O host64\\OptiScaler.log diz qual upscaler rodou; a linha do " +
        "Feeder \"min GPU architecture 0x0\" prova que a chamada passou pelo OptiScaler. Cada passada custa o mesmo que a primeira.";
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
