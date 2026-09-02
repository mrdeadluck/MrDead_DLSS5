namespace Dlss5.Core;

/// <summary>
/// Faxina: desfazer a instalação sem depender de manifesto, de detecção ou de nada
/// que a instalação tenha deixado gravado. É o modo de detecção legada: instalações de
/// versões antigas do programa (sem manifesto ou com manifesto perdido).
///
/// A busca é pelo nome do arquivo, na pasta do jogo inteira, e o critério para apagar
/// é conservador: só sai o que não tem como ser do jogo (ver <see cref="Propriedade"/>).
/// Nunca apaga arquivo desconhecido.
/// </summary>
public sealed partial class InstallerEngine
{
    /// <summary>
    /// Tudo que este programa possa ter deixado em qualquer subpasta do jogo — arquivos,
    /// pastas nossas e backups .dlss5bak ainda por devolver. Só olha, não mexe.
    /// </summary>
    /// <param name="estrito">
    /// Arquivos com nome de ReShade (ReShade.ini, dxgi.dll do ReShade…) só contam se houver
    /// peça do kit por perto — senão podem ser um ReShade que o usuário instalou sozinho.
    /// O inspetor de estado usa este modo; a remoção conservadora, que mostra a lista e
    /// pede confirmação, usa o modo largo.
    /// </param>
    public IReadOnlyList<string> EncontrarInstalacao(string? gameFolder, bool estrito = false)
    {
        var achados = new List<string>();
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder)) return achados;

        foreach (var pasta in PastasParaVarrer(gameFolder))
        {
            achados.AddRange(NossosArquivosEm(pasta, estrito));

            foreach (var nome in Propriedade.PastasNossas)
            {
                var alvo = Path.Combine(pasta, nome);
                if (Directory.Exists(alvo) && Propriedade.PastaEhNossa(alvo, pasta))
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
    public IReadOnlyList<string> LimpezaTotal(string? gameFolder) => LimpezaConservadora(gameFolder).Sobras;

    /// <summary>Versão com relatório completo, para a interface explicar o que aconteceu.</summary>
    public ResultadoDaReversao LimpezaConservadora(string? gameFolder, CancellationToken ct = default,
        IProgress<ProgressoDaOperacao>? progresso = null)
    {
        var r = new ResultadoDaReversao();
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
        {
            r.Erro = "Pasta do jogo não encontrada.";
            _log(r.Erro);
            return r;
        }

        _log($"Remoção conservadora (sem manifesto) em {gameFolder}");
        progresso?.Report(new ProgressoDaOperacao("Restaurando arquivos originais", 1, 5));

        // 1. Backups primeiro. Um arquivo devolvido volta a ser do JOGO — e por isso
        //    entra na lista de intocáveis do passo 3, mesmo que o nome dele seja um dos
        //    nossos (é o caso do nvngx_dlss.dll que o jogo já tinha antes).
        var restaurados = new HashSet<string>(RestaurarBackupsOrfaos(gameFolder), StringComparer.OrdinalIgnoreCase);
        r.Restaurados.AddRange(restaurados);

        // 2. Levantar tudo ANTES de apagar qualquer coisa. A prova de que host64\ e
        //    reshade-shaders\ são nossas está justamente nos arquivos que o passo 3
        //    remove; decidir depois deixaria as duas pastas para trás.
        progresso?.Report(new ProgressoDaOperacao("Procurando componentes do mod", 2, 5));
        var arquivos = new List<string>();
        var pastas = new List<string>();
        foreach (var pasta in PastasParaVarrer(gameFolder).ToList())
        {
            ct.ThrowIfCancellationRequested();
            arquivos.AddRange(NossosArquivosEm(pasta));
            foreach (var nome in Propriedade.PastasNossas)
            {
                var alvo = Path.Combine(pasta, nome);
                if (Directory.Exists(alvo) && Propriedade.PastaEhNossa(alvo, pasta)) pastas.Add(alvo);
            }
        }

        // 3. Arquivos.
        progresso?.Report(new ProgressoDaOperacao("Removendo componentes do mod", 3, 5));
        foreach (var arquivo in arquivos)
        {
            ct.ThrowIfCancellationRequested();
            if (restaurados.Contains(arquivo))
            {
                r.Preservados.Add($"{arquivo} — é do jogo (acabou de ser restaurado do backup)");
                _log($"Mantido (é do jogo, acabou de ser restaurado): {arquivo}");
                continue;
            }
            try
            {
                if (!File.Exists(arquivo)) continue;
                File.Delete(arquivo);
                r.Removidos.Add(arquivo);
                _log($"Apagado: {arquivo}");
            }
            catch (Exception ex)
            {
                r.Falhas.Add($"{arquivo} — {ex.Message}");
                Aviso($"{arquivo}: {ex.Message}");
            }
        }

        // 4. Pastas nossas por inteiro, das mais fundas para as mais rasas.
        progresso?.Report(new ProgressoDaOperacao("Removendo pastas do mod", 4, 5));
        foreach (var alvo in pastas.OrderByDescending(d => d.Length))
        {
            if (!Directory.Exists(alvo)) continue;
            try
            {
                Directory.Delete(alvo, recursive: true);
                r.Removidos.Add(alvo + Path.DirectorySeparatorChar);
                _log($"Pasta removida: {alvo}");
            }
            catch (Exception ex)
            {
                r.Falhas.Add($"{alvo} — {ex.Message}");
                Aviso($"{alvo}: {ex.Message}");
            }
        }
        LimparTemporarios(gameFolder);

        _log($"Remoção conservadora: {r.Removidos.Count} item(ns) removido(s).");

        progresso?.Report(new ProgressoDaOperacao("Conferindo o resultado", 5, 5));
        var sobras = EncontrarInstalacao(gameFolder)
            .Concat(pastas.Where(Directory.Exists).Select(d => d + Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        r.Sobras.AddRange(sobras);
        r.Sucesso = sobras.Count == 0 && r.Falhas.Count == 0;
        r.ManifestoRemovido = true;
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
        return r;
    }

    /// <summary>Arquivos desta pasta (só nela) que dá para afirmar que são nossos.</summary>
    internal static List<string> NossosArquivosEm(string pasta, bool estrito = false)
    {
        var nossos = new List<string>();
        bool provaDoKit = !estrito || Propriedade.TemProvaDoKitPorPerto(pasta);

        foreach (var nome in Propriedade.SoNossos)
        {
            var caminho = Path.Combine(pasta, nome);
            if (File.Exists(caminho) && (provaDoKit || Propriedade.EhNossoPorHeuristica(caminho, exigirProvaDoKit: true)))
                nossos.Add(caminho);
        }

        foreach (var (nome, prova) in Propriedade.PrecisamDeProva)
        {
            var caminho = Path.Combine(pasta, nome);
            if (provaDoKit && File.Exists(caminho) && Propriedade.ContemTexto(caminho, prova)) nossos.Add(caminho);
        }

        bool comEscolta = Propriedade.TemInstalacaoNossaPorPerto(pasta);

        foreach (var (nome, prova) in Propriedade.PrecisamDeProvaEEscolta)
        {
            var caminho = Path.Combine(pasta, nome);
            if (comEscolta && File.Exists(caminho) && Propriedade.ContemTexto(caminho, prova)) nossos.Add(caminho);
        }

        if (comEscolta)
            foreach (var nome in Propriedade.DgVoodooComEscolta)
            {
                var caminho = Path.Combine(pasta, nome);
                if (File.Exists(caminho)) nossos.Add(caminho);
            }

        // O nvngx_dlss.dll NUNCA sai pela faxina: desde que o kit deixou de sobrescrever
        // o DLSS do próprio jogo, o arquivo ao lado dos nossos addons num jogo com DLSS
        // nativo é o do JOGO — apagá-lo faz as opções de DLSS sumirem do menu.
        return nossos;
    }

    /// <summary>A pasta do jogo e todas as subpastas dela.</summary>
    internal static IEnumerable<string> PastasParaVarrer(string gameFolder)
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
