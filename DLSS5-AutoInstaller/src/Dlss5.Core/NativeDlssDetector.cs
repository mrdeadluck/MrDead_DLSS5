namespace Dlss5.Core;

/// <summary>Uma pista de que o jogo tem (ou não tem) DLSS próprio, com o peso que ela vale.</summary>
public sealed record NativeDlssClue(string Texto, int Peso);

/// <summary>Resultado da detecção de DLSS nativo, com as pistas que levaram até ele.</summary>
public sealed class NativeDlssDetection
{
    public bool Present { get; init; }
    public int Score { get; init; }
    public IReadOnlyList<NativeDlssClue> Clues { get; init; } = Array.Empty<NativeDlssClue>();

    /// <summary>Frase curta para a tela: o veredito e a razão dele.</summary>
    public string Resumo => Present
        ? "SIM — " + Motivos()
        : Clues.Count > 0
            ? "NÃO — só achei " + Motivos() + ", que não basta"
            : "NÃO — nenhum sinal de DLSS próprio no jogo";

    private string Motivos(int max = 3) => string.Join(", ", Clues
        .OrderByDescending(c => c.Peso)
        .Take(max)
        .Select(c => c.Texto));
}

/// <summary>
/// Descobre sozinho se o jogo já tem DLSS próprio.
///
/// O detector antigo perguntava só "existe nvngx_dlss.dll na pasta?" — e essa pergunta
/// passa a mentir no instante em que o programa instala: o kit TEM um nvngx_dlss.dll e a
/// instalação copia ele para a pasta do jogo. Bastava instalar uma vez e detectar de novo
/// para o mesmo jogo virar "tem DLSS nativo", mudando o plano da segunda instalação.
/// Era daí que vinha a impressão de que a resposta muda de jogo para jogo sem critério.
///
/// Aqui a evidência é só a que o instalador não consegue forjar:
/// as DLLs do Streamline e a frame generation (que não existem no kit) e o texto do
/// próprio executável do jogo, que o programa nunca toca.
/// </summary>
public static class NativeDlssDetector
{
    /// <summary>Arquivos que só podem ter vindo com o jogo — nenhum deles existe no kit.</summary>
    private static readonly (string Nome, int Peso)[] ArquivosDoJogo =
    {
        ("sl.dlss.dll", 60),
        ("sl.dlss_g.dll", 60),
        ("sl.dlss_nr.dll", 60),
        ("nvngx_dlssg.dll", 60),
        ("nvngx_dlssd.dll", 55),
        // Streamline pode estar ali só por causa do Reflex: sozinho não decide.
        ("sl.interposer.dll", 25),
        ("sl.common.dll", 15),
    };

    /// <summary>
    /// Texto dentro do executável do jogo. Só o exe: varrer as DLLs vizinhas traria de
    /// volta o problema que este detector existe para resolver, porque o nvngx_dlss.dll
    /// que a instalação copia é cheio de "NVSDK_NGX".
    /// </summary>
    private static readonly (string Texto, int Peso)[] MarcadoresNoExe =
    {
        ("NVSDK_NGX_D3D12_Init", 55),
        ("NVSDK_NGX_D3D11_Init", 55),
        ("NVSDK_NGX_VULKAN_Init", 55),
        ("NVSDK_NGX", 50),
        ("sl.interposer.dll", 40),
        ("nvngx.dll", 30),
    };

    /// <summary>Ambíguo: o kit também tem esse arquivo, então o contexto é que decide.</summary>
    private const string Ambiguo = "nvngx_dlss.dll";
    private const int PesoAmbiguo = 30;
    private const int PesoDecisivo = 50;

    /// <summary>Arquivos que provam que ESTE programa instalou nesta pasta.</summary>
    private static readonly string[] SinaisDaNossaInstalacao =
    {
        "renodx-dlss5.addon64", "dlss5-feed.addon64", "dlss5-feed.addon32",
        "nvngx_dlssnr.dll", "ReShade.ini", InstallManifest.FileName,
    };

    private const int Limiar = 45;
    private const long OrcamentoExe = 192L * 1024 * 1024;

    public static NativeDlssDetection Detect(
        string gameFolder, string exeFolder, string? exePath, InstallManifest? instalacaoAnterior = null)
    {
        var clues = new List<NativeDlssClue>();
        int score = 0;

        void Add(int peso, string texto)
        {
            score += peso;
            clues.Add(new NativeDlssClue(texto, peso));
        }

        var presentes = ArquivosNvidiaNoJogo(gameFolder, exeFolder);

        foreach (var (nome, peso) in ArquivosDoJogo)
            if (presentes.ContainsKey(nome))
                Add(peso, nome);

        // O nvngx_dlss.dll é o caso ambíguo: existe no kit e em muitos jogos. A regra
        // decide pelo contexto, e o desempate pende para "é do jogo" DE PROPÓSITO — os
        // dois erros não custam o mesmo. Dizer "tem DLSS" num jogo que não tem deixa o
        // RenoDX esperando uma chamada que nunca vem: não aplica nada, não quebra nada.
        // Dizer "não tem" num jogo que tem instala o Feeder junto do DLSS do jogo, e
        // mexer no DLSS do menu TRAVA o jogo — aconteceu em Forza e em GTA 5.
        if (presentes.TryGetValue(Ambiguo, out var caminhoAmbiguo))
        {
            var pastaDele = Path.GetDirectoryName(caminhoAmbiguo) ?? exeFolder;
            bool nossaInstalacaoAqui = SinaisDaNossaInstalacao
                .Any(n => File.Exists(Path.Combine(pastaDele, n)));

            if (instalacaoAnterior is not null)
            {
                // O manifesto lista tudo que copiamos: ele decide sozinho.
                if (!NossoArquivo(caminhoAmbiguo, instalacaoAnterior))
                    Add(PesoDecisivo, Ambiguo + " (o manifesto da instalação não o lista: veio com o jogo)");
            }
            else if (!nossaInstalacaoAqui)
            {
                // Sem manifesto e sem NENHUM arquivo nosso na pasta, não fomos nós:
                // o instalador nunca deixa o nvngx_dlss.dll sozinho para trás.
                Add(PesoDecisivo, Ambiguo + " (sem instalação nossa na pasta: veio com o jogo)");
            }
            else
            {
                // Instalação nossa presente e manifesto sumido: não dá para afirmar.
                Add(PesoAmbiguo, Ambiguo + " (pode ser desta instalação; peso reduzido)");
            }
        }

        if (exePath is not null && File.Exists(exePath))
        {
            var achados = ApiDetector.ScanForMarkers(
                exePath, MarcadoresNoExe.Select(m => m.Texto).ToList(), OrcamentoExe);

            // Só o marcador mais forte encontrado conta: eles se repetem no mesmo binário
            // (quem tem NVSDK_NGX_D3D12_Init tem NVSDK_NGX), e somar todos inflaria o placar.
            var melhores = MarcadoresNoExe
                .Where(m => achados.Contains(m.Texto))
                .OrderByDescending(m => m.Peso)
                .ToList();
            if (melhores.Count > 0)
                Add(melhores[0].Peso, $"\"{melhores[0].Texto}\" dentro do exe do jogo");
        }

        return new NativeDlssDetection
        {
            Present = score >= Limiar,
            Score = score,
            Clues = clues,
        };
    }

    /// <summary>O arquivo consta como criado pela instalação anterior deste programa?</summary>
    private static bool NossoArquivo(string caminho, InstallManifest? manifesto)
    {
        if (manifesto is null) return false;
        return manifesto.AddedFiles.Any(f => string.Equals(f, caminho, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Nome → caminho de toda DLL nvngx_*/sl.* debaixo da pasta do jogo. Ignora host64\,
    /// que é pasta nossa, e olha a pasta do exe primeiro (é onde o jogo carrega de fato).
    /// </summary>
    private static Dictionary<string, string> ArquivosNvidiaNoJogo(string gameFolder, string exeFolder)
    {
        var achados = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Coletar(string pasta, SearchOption modo)
        {
            foreach (var padrao in new[] { "nvngx_*.dll", "sl.*.dll" })
            {
                IEnumerable<string> arquivos;
                try { arquivos = Directory.EnumerateFiles(pasta, padrao, modo); }
                catch { continue; }

                foreach (var arquivo in arquivos)
                {
                    if (arquivo.Contains($"{Path.DirectorySeparatorChar}host64{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    achados.TryAdd(Path.GetFileName(arquivo), arquivo);
                }
            }
        }

        if (Directory.Exists(exeFolder)) Coletar(exeFolder, SearchOption.TopDirectoryOnly);
        if (Directory.Exists(gameFolder)) Coletar(gameFolder, SearchOption.AllDirectories);
        return achados;
    }
}
