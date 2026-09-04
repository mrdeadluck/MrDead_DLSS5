using System.Diagnostics;

namespace Dlss5.Core;

/// <summary>Uma sobreposição conhecida e como desligá-la sem fechar o programa.</summary>
/// <param name="Processo">Nome do processo, sem .exe.</param>
/// <param name="Nome">Como o usuário chama.</param>
/// <param name="ComoDesligar">Onde fica a opção.</param>
/// <param name="PrecisaFicarAberto">
/// O programa precisa continuar rodando para o jogo abrir. Distinção que importa: mandar
/// "feche o EA App" num jogo da EA é conselho impossível de seguir — o que sai é a
/// sobreposição, não o programa.
/// </param>
public sealed record Overlay(string Processo, string Nome, string ComoDesligar, bool PrecisaFicarAberto);

/// <summary>
/// Sobreposições que competem pelo DXGI.
///
/// Elas carregam o DXGI antes do ReShade e ficam com a interceptação; o sintoma é o
/// ReShade.log nunca aparecer, com a instalação inteira correta. "Desligue os overlays"
/// como conselho genérico não ajuda ninguém: o que resolve é dizer quais estão rodando
/// AGORA nesta máquina, e onde fica a opção de cada um.
/// </summary>
public static class Overlays
{
    public static readonly Overlay[] Conhecidos =
    {
        new("EADesktop", "EA App",
            "EA App → Configurações → Aplicativo → desligue a sobreposição no jogo. " +
            "O EA App PRECISA continuar aberto (sem ele o jogo não abre) — sai só a sobreposição.",
            PrecisaFicarAberto: true),
        new("Origin", "Origin",
            "Origin → Configurações → Origin no jogo → desligar. O Origin continua aberto.",
            PrecisaFicarAberto: true),
        new("steam", "Sobreposição da Steam",
            "Steam → clique direito no jogo → Propriedades → desmarque a sobreposição. " +
            "A Steam continua aberta.",
            PrecisaFicarAberto: true),

        new("RTSS", "RivaTuner Statistics Server",
            "Feche pelo ícone ao lado do relógio. Ele sobe junto com o Windows, então é o " +
            "que mais passa despercebido — e injeta em tudo.",
            PrecisaFicarAberto: false),
        new("MSIAfterburner", "MSI Afterburner",
            "Quem injeta é o RivaTuner que vem com ele; feche os dois durante o teste.",
            PrecisaFicarAberto: false),
        new("Discord", "Discord",
            "Discord → Configurações → Sobreposição de jogo → desligar.",
            PrecisaFicarAberto: false),
        new("NVIDIA Share", "Sobreposição da NVIDIA (ShadowPlay)",
            "NVIDIA App → Configurações → Sobreposição no jogo → desligar.",
            PrecisaFicarAberto: false),
        new("Overwolf", "Overwolf",
            "Feche durante o teste.", PrecisaFicarAberto: false),
        new("Medal", "Medal",
            "Feche durante o teste.", PrecisaFicarAberto: false),
        new("obs64", "OBS Studio",
            "A captura de jogo do OBS também engancha; feche durante o teste.",
            PrecisaFicarAberto: false),
        new("GameBar", "Xbox Game Bar",
            "Configurações do Windows → Jogos → Xbox Game Bar → desligar.",
            PrecisaFicarAberto: false),
    };

    /// <summary>Quais das conhecidas estão nesta lista de processos.</summary>
    public static IReadOnlyList<Overlay> Detectar(IEnumerable<string> processos)
    {
        var rodando = new HashSet<string>(processos, StringComparer.OrdinalIgnoreCase);
        return Conhecidos.Where(o => rodando.Contains(o.Processo)).ToList();
    }

    /// <summary>Nomes dos processos vivos agora. Falha silenciosa vira lista vazia.</summary>
    public static IReadOnlyList<string> ProcessosRodando()
    {
        try
        {
            return Process.GetProcesses().Select(p =>
            {
                try { return p.ProcessName; } catch { return ""; }
            }).Where(n => n.Length > 0).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
