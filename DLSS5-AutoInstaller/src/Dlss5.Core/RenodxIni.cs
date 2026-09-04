using System.Text;

namespace Dlss5.Core;

/// <summary>
/// A chave EnableHooks do addon do RenoDX, na seção [RenoDX.DLSS5] do ReShade.ini.
///
/// É o addon que a lê: 2 pendura os ganchos só no NGX (padrão, o que o GTA 5 e o Onimusha
/// usam), 1 pendura também nos módulos do Streamline (o próprio addon pede isso em jogo
/// que entrega depth e motion vectors por lá, e avisa que pode cair na abertura), 0 deixa
/// o addon carregado sem gancho nenhum — o teste que separa "o gancho derruba o jogo" de
/// "o addon em si derruba o jogo". Trocar aqui poupa desinstalar e instalar a cada tentativa.
/// </summary>
public static class RenodxIni
{
    public const string Secao = "[RenoDX.DLSS5]";
    public const string Chave = "EnableHooks";
    public const int Padrao = 2;

    /// <summary>Na ordem em que a tela oferece: o padrão primeiro.</summary>
    public static IReadOnlyList<int> Valores { get; } = new[] { 2, 1, 0 };

    public static string Descricao(int valor) => valor switch
    {
        0 => "0 — desligados (modo seguro: o addon carrega e não intercepta nada)",
        1 => "1 — NGX + Streamline (jogo com sl.*.dll; pode cair na abertura)",
        _ => "2 — só NGX (padrão)",
    };

    /// <summary>O que esperar do jogo com cada valor, para não sobrar interpretação.</summary>
    public static string Leitura(int valor) => valor switch
    {
        0 => "EnableHooks=0 gravado: o addon carrega e NÃO intercepta nada. Abra o jogo.\r\n\r\n" +
             "• Se o jogo agora RODA: era o gancho do RenoDX (no NGX ou no Streamline) que " +
             "derrubava o jogo. Não há Neural Rendering neste modo — ele serve só para o teste.\r\n" +
             "• Se AINDA cai: o gancho está inocente. Teste em seguida sem o addon inteiro " +
             "(\"Testar sem o RenoDX\") e depois sem o ReShade (\"Isolar a causa\").",
        1 => "EnableHooks=1 gravado: ganchos no NGX e nos módulos do Streamline. Abra o jogo.\r\n\r\n" +
             "É o que o addon pede em jogo que entrega depth e motion vectors pelo Streamline " +
             "(sl.*.dll na pasta), onde o modo 2 vê a chamada de DLSS mas não os guias.\r\n" +
             "• Se o jogo CAIR na abertura, o próprio addon avisa que este ponto é disputado: " +
             "volte para 2.\r\n" +
             "• Se abrir, aperte Home e olhe o status na aba Complementos: \"ACTIVE - NR INJECTED\".",
        _ => "EnableHooks=2 gravado (padrão): ganchos só no NGX, Streamline intocado. É o modo " +
             "em que o GTA 5 e o Onimusha rodaram.",
    };

    /// <summary>
    /// NeuralUplift: o liga/desliga do Neural Rendering do próprio addon (1 ligado, 0 desligado).
    /// O F6 grava aqui. No Max Payne 3 (32-bit) o host64 subiu com NeuralUplift=0 depois de uma
    /// troca de configuração, e o feed entregava 30 mil quadros para um NR desligado.
    /// </summary>
    public const string ChaveNeuralUplift = "NeuralUplift";

    /// <summary>Valor gravado, ou nulo quando a seção ou a chave não existem.</summary>
    public static int? Ler(string? ini) => Ler(ini, Chave);

    /// <summary>Uma chave qualquer da seção [RenoDX.DLSS5].</summary>
    public static int? Ler(string? ini, string chave)
    {
        if (string.IsNullOrWhiteSpace(ini)) return null;
        bool dentro = false;
        foreach (var bruta in ini.Replace("\r\n", "\n").Split('\n'))
        {
            var linha = bruta.Trim();
            if (linha.StartsWith('['))
            {
                dentro = linha.Equals(Secao, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!dentro) continue;
            int eq = linha.IndexOf('=');
            if (eq < 0) continue;
            if (!linha[..eq].Trim().Equals(chave, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(linha[(eq + 1)..].Trim(), out var v) ? v : null;
        }
        return null;
    }

    /// <summary>
    /// Devolve o ini com EnableHooks=valor na seção [RenoDX.DLSS5]: troca a linha se ela
    /// existe, acrescenta a chave se a seção existe sem ela, cria a seção no fim se não
    /// existe. Todo o resto do arquivo (as outras seções e as outras chaves do addon)
    /// fica exatamente como estava.
    /// </summary>
    public static string Gravar(string? ini, int valor)
    {
        var texto = ini ?? "";
        var quebra = texto.Contains("\r\n") || texto.Length == 0 ? "\r\n" : "\n";
        var linhas = texto.Replace("\r\n", "\n").Split('\n').ToList();
        if (linhas.Count > 0 && linhas[^1].Length == 0) linhas.RemoveAt(linhas.Count - 1);

        var nova = $"{Chave}={valor}";
        int cabecalho = -1, fimDaSecao = -1;
        bool dentro = false;
        for (int i = 0; i < linhas.Count; i++)
        {
            var linha = linhas[i].Trim();
            if (linha.StartsWith('['))
            {
                if (dentro) { fimDaSecao = i; break; }
                dentro = linha.Equals(Secao, StringComparison.OrdinalIgnoreCase);
                if (dentro) cabecalho = i;
                continue;
            }
            if (!dentro) continue;
            int eq = linha.IndexOf('=');
            if (eq >= 0 && linha[..eq].Trim().Equals(Chave, StringComparison.OrdinalIgnoreCase))
            {
                linhas[i] = nova;
                return Juntar(linhas, quebra);
            }
        }

        if (cabecalho >= 0)
        {
            // Seção existe sem a chave: entra logo abaixo do último item dela.
            int pos = fimDaSecao < 0 ? linhas.Count : fimDaSecao;
            while (pos - 1 > cabecalho && linhas[pos - 1].Trim().Length == 0) pos--;
            linhas.Insert(pos, nova);
            return Juntar(linhas, quebra);
        }

        if (linhas.Count > 0 && linhas[^1].Trim().Length != 0) linhas.Add("");
        linhas.Add(Secao);
        linhas.Add(nova);
        return Juntar(linhas, quebra);
    }

    private static string Juntar(List<string> linhas, string quebra)
    {
        var sb = new StringBuilder();
        foreach (var l in linhas) sb.Append(l).Append(quebra);
        return sb.ToString();
    }
}
