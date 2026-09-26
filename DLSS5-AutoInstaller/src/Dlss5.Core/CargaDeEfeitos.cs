using System.Text.RegularExpressions;

namespace Dlss5.Core;

/// <summary>
/// Quais efeitos o ReShade do jogo carrega.
///
/// Sem ordem em contrário, o ReShade compila e cria TODO .fx dos caminhos de busca — no kit,
/// a pasta reshade-shaders inteira (44 efeitos: SweetFX, MartysMods, REST...), mesmo que o
/// preset só marque dois. Num jogo 32-bit isso tudo mora nos 4 GB do processo, junto com o
/// jogo, o dgVoodoo, o feed e o driver: o Black Mesa (26/09/2026) compilou os 44 dentro do
/// bms.exe e caiu com "failed to lock vertex buffer in CMeshDX8::LockVertexBuffer" segundos
/// depois de o DLSS 5 começar a entregar quadros — com o dgVoodoo e o feed ativos, sem DXVK —
/// e, noutra abertura, ficou parado em "Compilando (40 efeitos restantes)" na tela de
/// carregamento. O próprio ReShade limita a 4 as threads de compilação em 32-bit "porque o
/// espaço de endereço é pouco e compilar consome muita memória".
///
/// A opção "Load only enabled effects" do ReShade pula o .fx que não tem nenhuma technique na
/// lista Techniques do preset: nem compila, nem cria textura. Com lista vazia ela não pula
/// nada (conferido no runtime.cpp do ReShade 6.8.0).
/// </summary>
public static class CargaDeEfeitos
{
    /// <summary>
    /// [GENERAL] do ReShade.ini: 1 = só os efeitos marcados no preset. É o nome que o ReShade 6
    /// lê; "EffectLoadSkipping", que este programa gravou até 26/09/2026, não existe e era ignorado.
    /// </summary>
    public const string Chave = "SkipLoadingDisabledEffects";

    /// <summary>
    /// [OVERLAY] do ReShade.ini: 0 = o preset só é gravado pelo botão de salvar do painel. Com os
    /// efeitos pulados, um preset gravado com o DLSS 5 desligado (F6, que o ReShade salva na hora)
    /// faria o DLSS5_Feed.fx nem ser carregado na abertura seguinte — e aí o F6 não tem o que ligar.
    /// </summary>
    public const string ChaveAutoSalvar = "AutoSavePreset";

    /// <summary>
    /// Quantos efeitos cabem numa abertura com a chave: o provedor de MV, o DLSS5_Feed, os
    /// .addonfx (o ReShade nunca os pula) e algum que o usuário tenha marcado. Acima disso o
    /// ReShade.log é de antes da chave, ou o "Force load all effects" do painel foi usado.
    /// </summary>
    public const int LimiteEsperado = 8;

    // "Successfully compiled '<caminho>'[ permutation] in ..." e "Failed to compile '<caminho>'[ permutation]!" ou ":".
    // O caminho pode ter apóstrofo ("Tom Clancy's ..."): o fim dele é o ".fx'" seguido do que o ReShade escreve depois.
    private static readonly Regex Carregado = new(
        @"(?:Successfully compiled|Failed to compile) '([^\r\n]+?\.(?:fx|addonfx))'(?= permutation| in |!|:)", RegexOptions.IgnoreCase);

    /// <summary>
    /// Os efeitos (.fx e .addonfx) distintos que o ReShade.log registra como compilados ou com
    /// falha — o ReShade escreve uma dessas linhas para cada efeito que tentou carregar, vindo do
    /// cache ou não, e nenhuma para o que pulou. Recarregar na mesma sessão não conta duas vezes.
    /// </summary>
    public static IReadOnlyList<string> NoLog(string? reshadeLog)
    {
        if (string.IsNullOrEmpty(reshadeLog)) return Array.Empty<string>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lista = new List<string>();
        foreach (Match m in Carregado.Matches(reshadeLog))
            if (vistos.Add(m.Groups[1].Value))
                lista.Add(m.Groups[1].Value);
        return lista;
    }

    /// <summary>O nome do arquivo de um caminho do log, que é do Windows (a barra não muda com o SO que lê).</summary>
    public static string NomeDoArquivo(string caminho) => caminho[(caminho.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
}
