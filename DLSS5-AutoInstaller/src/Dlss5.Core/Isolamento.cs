namespace Dlss5.Core;

/// <summary>Etapa da bisseção: o que está desligado agora.</summary>
public enum EstadoIsolamento
{
    /// <summary>Instalação inteira ativa.</summary>
    Tudo,
    /// <summary>dgVoodoo desligado; o jogo volta a falar com o Direct3D do Windows.</summary>
    SemDgVoodoo,
    /// <summary>dgVoodoo ativo, ReShade desligado.</summary>
    SemReShade,
}

/// <summary>
/// Isola quem está barrando o jogo, renomeando uma peça de cada vez.
///
/// Quando o jogo nem abre, a lista de suspeitos tem três nomes: o dgVoodoo, o ReShade e
/// o próprio jogo. Trocar configuração no escuro pode custar horas e não conclui nada —
/// desligar uma peça e ver o que muda responde na primeira tentativa. Renomear é
/// reversível e não apaga nada: a extensão volta com um clique.
/// </summary>
public sealed class Isolamento
{
    public const string Sufixo = ".dlss5off";

    private readonly Action<string> _log;

    public Isolamento(Action<string> log) => _log = log;

    /// <summary>Arquivos de cada suspeito, na pasta onde eles foram instalados.</summary>
    private static IEnumerable<string> Alvos(EstadoIsolamento estado, string exeFolder, string rendererFolder) =>
        estado switch
        {
            EstadoIsolamento.SemDgVoodoo => new[]
            {
                Path.Combine(rendererFolder, "D3D8.dll"),
                Path.Combine(rendererFolder, "D3D9.dll"),
            },
            EstadoIsolamento.SemReShade => new[]
            {
                Path.Combine(exeFolder, "dxgi.dll"),
                Path.Combine(exeFolder, "opengl32.dll"),
            },
            _ => Array.Empty<string>(),
        };

    /// <summary>
    /// Deixa a pasta no estado pedido: religa tudo e depois desliga só o suspeito da vez.
    /// Devolve os arquivos que ficaram desligados.
    /// </summary>
    public IReadOnlyList<string> Aplicar(EstadoIsolamento estado, string exeFolder, string rendererFolder)
    {
        ReligarTudo(exeFolder);
        if (!string.Equals(exeFolder, rendererFolder, StringComparison.OrdinalIgnoreCase))
            ReligarTudo(rendererFolder);

        var desligados = new List<string>();
        foreach (var alvo in Alvos(estado, exeFolder, rendererFolder))
        {
            if (!File.Exists(alvo)) continue;
            try
            {
                var off = alvo + Sufixo;
                if (File.Exists(off)) File.Delete(off);
                File.Move(alvo, off);
                desligados.Add(alvo);
                _log($"Desligado: {alvo} → {Path.GetFileName(off)}");
            }
            catch (Exception ex)
            {
                _log($"Aviso: não consegui desligar {alvo}: {ex.Message}");
            }
        }

        if (estado == EstadoIsolamento.Tudo) _log("Instalação inteira religada.");
        return desligados;
    }

    /// <summary>Devolve a extensão original de todo arquivo desligado na pasta.</summary>
    public IReadOnlyList<string> ReligarTudo(string? pasta)
    {
        var religados = new List<string>();
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) return religados;

        List<string> desligados;
        try { desligados = Directory.EnumerateFiles(pasta, "*" + Sufixo, SearchOption.AllDirectories).ToList(); }
        catch { return religados; }

        foreach (var off in desligados)
        {
            var original = off[..^Sufixo.Length];
            try
            {
                File.Move(off, original, overwrite: true);
                religados.Add(original);
                _log($"Religado: {original}");
            }
            catch (Exception ex)
            {
                _log($"Aviso: não consegui religar {original}: {ex.Message}");
            }
        }
        return religados;
    }

    /// <summary>O que o resultado de cada teste significa, para não sobrar interpretação.</summary>
    public static string Leitura(EstadoIsolamento estado) => estado switch
    {
        EstadoIsolamento.SemDgVoodoo =>
            "dgVoodoo DESLIGADO. Abra o jogo (pela Steam, se for da Steam).\r\n\r\n" +
            "• Se o jogo ABRIR: o dgVoodoo é quem está barrando. O overlay do ReShade NÃO vai " +
            "abrir neste teste, e isso é esperado — sem o dgVoodoo não há D3D11, e sem D3D11 o " +
            "ReShade não tem onde se pendurar. Não é sintoma novo.\r\n" +
            "   APROVEITE QUE O JOGO ABRIU: se ele tem tela de configuração de vídeo, é agora " +
            "que dá para mexer nela. Coloque uma resolução clássica (1024x768) e, se houver " +
            "opção de aceleração, tire a exigência de \"Hardware T&L\" — jogo que grava " +
            "\"T&L\" e o nome da placa de verdade recusa o adaptador do dgVoodoo na volta, " +
            "porque nenhum dos dois bate. Depois religue o dgVoodoo e abra de novo.\r\n" +
            "• Se o jogo AINDA recusar: a instalação não tem nada a ver com isso — o jogo já " +
            "falharia sozinho nesta máquina, e o problema é outro (monitor, resolução do " +
            "desktop, configuração salva do próprio jogo).",

        EstadoIsolamento.SemReShade =>
            "ReShade DESLIGADO, dgVoodoo ativo. Abra o jogo.\r\n\r\n" +
            "• Se o jogo ABRIR agora: o ReShade é que atrapalha a criação do device pelo " +
            "dgVoodoo. Sem ele não há DLSS 5, mas ao menos o culpado está identificado.\r\n" +
            "• Se AINDA recusar: junto com o teste anterior, isso aponta para o dgVoodoo.",

        _ => "Instalação religada por inteiro. Nenhum arquivo foi apagado em nenhum dos testes.",
    };
}
