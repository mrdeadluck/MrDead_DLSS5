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
    /// <summary>
    /// Só o addon do RenoDX desligado; ReShade e Feeder continuam. É o teste do jogo
    /// com DLSS nativo que abre mas trava ao ligar o DLSS no MENU: o RenoDX é quem se
    /// pendura na chamada de NGX que o próprio jogo faz, e desligá-lo sozinho responde
    /// se é essa interceptação que está travando.
    /// </summary>
    SemRenodx,
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
            // Só o addon: em jogo 64-bit ele está na raiz, em 32-bit dentro de host64\.
            EstadoIsolamento.SemRenodx => new[]
            {
                Path.Combine(exeFolder, "renodx-dlss5.addon64"),
                Path.Combine(exeFolder, "host64", "renodx-dlss5.addon64"),
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

    /// <summary>
    /// Conclusão a partir das duas respostas. Separada porque a combinação é o que importa:
    /// cada teste sozinho não distingue "o dgVoodoo é rejeitado" de "o ReShade atrapalha o
    /// dgVoodoo", e essas duas causas pedem correções completamente diferentes.
    /// </summary>
    public static string Veredito(bool? abriuSemDgVoodoo, bool? abriuSemReShade)
    {
        if (abriuSemDgVoodoo is false)
            return "CONCLUSÃO: o problema NÃO é a instalação.\r\n\r\n" +
                   "O jogo recusou abrir mesmo com o dgVoodoo desligado, quando a pasta estava " +
                   "igual a antes de qualquer coisa ser instalada. A causa é do jogo nesta " +
                   "máquina — configuração de vídeo que ele gravou, monitor, ou resolução do " +
                   "desktop. Resolva isso primeiro; enquanto o jogo não abrir sozinho, não há o " +
                   "que o DLSS 5 possa fazer.";

        if (abriuSemDgVoodoo is true && abriuSemReShade is true)
            return "CONCLUSÃO: sozinhos os dois funcionam; juntos, não.\r\n\r\n" +
                   "O jogo abre sem o dgVoodoo e abre sem o ReShade, mas recusa com os dois. " +
                   "É conflito de carregamento: o ReShade entra no dxgi.dll que o próprio " +
                   "dgVoodoo usa para falar com o D3D11, e nessa ordem a criação do device " +
                   "falha. Não é configuração de placa — trocar VideoCard não vai resolver.";

        if (abriuSemDgVoodoo is true && abriuSemReShade is false)
            return "CONCLUSÃO: o dgVoodoo é rejeitado por este jogo.\r\n\r\n" +
                   "Sem ele o jogo abre; com ele o jogo recusa, mesmo sem o ReShade na jogada. " +
                   "Aí é o adaptador que o dgVoodoo apresenta que não serve para este jogo. " +
                   "Vale tentar: a opção de aceleração do jogo em \"D3D Software T&L\" (é a que " +
                   "dispensa T&L por hardware — o nome engana), e a caixa \"T&L por hardware\" " +
                   "aqui do lado nas duas posições, combinada com cada placa da lista.";

        return "Faltou responder um dos testes. Rode os dois para chegar a uma conclusão.";
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

        EstadoIsolamento.SemRenodx =>
            "RenoDX DESLIGADO. ReShade e Feeder continuam ativos.\r\n\r\n" +
            "O RenoDX é o addon que se pendura na chamada de DLSS que o PRÓPRIO jogo faz — " +
            "e essa interceptação é a suspeita de travar o jogo ao ligar o DLSS no menu e de " +
            "registrar \"ativo\" sem mudar nada na imagem.\r\n\r\n" +
            "Abra o jogo e vá direto ao menu de vídeo:\r\n" +
            "• Ligue o DLSS DO JOGO (Qualidade e depois Performance) e aplique.\r\n" +
            "   — Se agora ele liga SEM travar (e em Performance o FPS sobe), o culpado é o " +
            "gancho do RenoDX dentro do processo: com essa resposta o conserto vira regra no " +
            "instalador.\r\n" +
            "   — Se AINDA travar, o RenoDX está inocente e a causa é mais funda (driver, " +
            "override, arquivos do jogo).\r\n\r\n" +
            "Neste teste o Neural Rendering NÃO aplica — o Feed alimenta um addon que está " +
            "desligado. Isso é esperado, não é sintoma. O mesmo botão religa o RenoDX.",

        _ => "Instalação religada por inteiro. Nenhum arquivo foi apagado em nenhum dos testes.",
    };
}
