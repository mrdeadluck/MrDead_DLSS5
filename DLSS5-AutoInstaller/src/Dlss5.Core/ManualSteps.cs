namespace Dlss5.Core;

public sealed record ManualStep(int Order, string Title, string Detail, bool CriticalBeforeLaunch);

/// <summary>
/// O que o programa não consegue fazer com segurança (spec 11.3) vira roteiro guiado:
/// overlays, opções de inicialização, confirmação visual e ajustes de depth.
/// </summary>
public static class ManualSteps
{
    public static IReadOnlyList<ManualStep> For(GameProfile profile, InstallOptions options)
    {
        var steps = new List<ManualStep>();
        int n = 1;

        if (options.ApplyRegistryOverride)
            steps.Add(new ManualStep(n++, "Reiniciar o Windows (opcional — uma vez só)",
                "O driver da NVIDIA lê o override de assinatura na inicialização do Windows. Só faz " +
                "diferença na primeira vez que o override é aplicado; se o DLSS 5 já aplica nos seus " +
                "jogos, não precisa. O programa nunca reinicia o PC sozinho.", false));

        steps.Add(new ManualStep(n++, "Desligar as sobreposições (overlays)",
            "Steam → clique direito no jogo → Propriedades → desmarque a sobreposição. " +
            "NVIDIA App → Configurações → Sobreposição no jogo → desligar. " +
            "EA App/Origin (Titanfall, Battlefield, Need for Speed), Discord e " +
            "RivaTuner/MSI Afterburner fazem a mesma coisa e são os que mais passam " +
            "despercebidos — o RivaTuner costuma subir junto com o Windows. " +
            "Isso não dá para automatizar de forma confiável: a Steam restaura o arquivo do overlay ao reabrir. " +
            "Overlays podem carregar o DXGI antes do ReShade e roubar a interceptação.", false));

        // Sob o EAC nada disto abaixo acontece: a DLL do ReShade nem carrega.
        if (EasyAntiCheat.Presente(profile.GameFolder, profile.ExeFolder))
            steps.Add(new ManualStep(n++, "Tirar o Easy Anti-Cheat do caminho (só para jogar OFFLINE)",
                EasyAntiCheat.Aviso + "\r\n\r\n" + EasyAntiCheat.ComoAbrir, true));

        if (profile.HasNativeDlss && profile.NeedsFeeder)
            steps.Add(new ManualStep(n++, "DESLIGAR o DLSS nas opções do jogo",
                "Este jogo tem DLSS próprio e o kit NÃO mexe nele — mas o Feeder roda um NGX dele dentro " +
                "do processo, e com o DLSS do jogo ligado os dois colidem: o jogo trava depois da tela " +
                "inicial (Onimusha) ou ao aplicar o DLSS no menu (GTA 5). Desligue o DLSS do jogo antes de " +
                "abrir com o kit instalado; o Neural Rendering entra pelo Feed, como nos jogos sem DLSS.", true));

        if (profile.UsesRenodxDirectPath)
            steps.Add(new ManualStep(n++, "LIGAR o DLSS nas opções do jogo",
                "Neste caso o Feeder não é instalado: em D3D12 o RenoDX se pendura na chamada de DLSS que o " +
                "próprio jogo faz. Se o DLSS do jogo ficar desligado não existe chamada nenhuma para " +
                "interceptar, e o RenoDX fica em \"HOOKS ARMED / NO DLSS CREATE SEEN\" sem aplicar nada. " +
                "Ligue o DLSS no menu do jogo (qualquer modo) antes de esperar resultado.", true));

        // Depth pelo Generic Depth e DLAA do Feeder: só existem no caminho do Feeder. No
        // direto o RenoDX recebe depth e motion vectors do contrato NGX do jogo, e o
        // Generic Depth fica desligado de propósito (a cópia antes dos clears derrubou o RE9).
        if (profile.NeedsFeeder)
            steps.Add(new ManualStep(n++, "Desligar MSAA/SSAA nas opções gráficas do jogo",
                "O Generic Depth não enxerga um depth buffer multisampled, e SSAA conflita com o DLAA. " +
                "FXAA e SMAA são pós-processo e podem continuar ligados.", true));

        if (profile.SuggestedLaunchOptions is { } launchOpts)
            steps.Add(new ManualStep(n++, $"Adicionar a opção de inicialização {launchOpts}",
                $"Steam → Propriedades do jogo → Opções de inicialização → digite {launchOpts}. " +
                "Isso força o D3D9, que é obrigatório porque Vulkan 32-bit não é suportado. " +
                "REMOVA essa opção depois da primeira execução, senão o jogo reseta as configurações toda vez.", true));

        if (profile.Api == GraphicsApi.D3D8)
            steps.Add(new ManualStep(n++, "Se o jogo recusar o adaptador de vídeo",
                "Jogo de DirectX 8 checa a placa antes de abrir, e o cartão virtual do dgVoodoo se " +
                "identifica como ele mesmo — daí mensagens como \"requires a DirectX 8 compatible " +
                "display adapter\". O programa já grava o perfil Legado (AdapterIDType=nvidia, " +
                "MSD3DDeviceNames=true, VRAM 256 MB), que resolve a maioria dos casos. Se persistir, " +
                "abra o Painel do dgVoodoo (botão na tela de verificação), aba DirectX, e troque " +
                "VideoCard: tente geforce_ti_4800, depois ati_radeon_8500. É só salvar e reabrir o " +
                "jogo — nada precisa ser reinstalado.\r\n\r\n" +
                "Se nada disso resolver, o culpado costuma ser a configuração que o PRÓPRIO jogo " +
                "gravou. Esses jogos têm tela de vídeo onde ficam salvos o nome da placa e o modo " +
                "de aceleração (\"D3D Hardware T&L\"), e nenhum dos dois bate com o adaptador do " +
                "dgVoodoo. Use \"Isolar a causa\" para desligar o dgVoodoo, abra o jogo — ele volta " +
                "a abrir —, ajuste ali a resolução para uma clássica e a aceleração para a opção " +
                "sem T&L, e só então religue o dgVoodoo.", false));

        if (profile.NeedsDgVoodoo)
            steps.Add(new ManualStep(n++, "Conferir a marca d'água do dgVoodoo",
                "Abra o jogo: a marca d'água do dgVoodoo tem que aparecer na tela. " +
                "É o único teste confiável de que ele está interceptando o Direct3D. " +
                "Sem ela, o D3D9.dll está na pasta errada (no Source vai em bin\\) ou o passthru continua ligado.", false));

        if (profile.NeedsFeeder)
        {
            steps.Add(new ManualStep(n++, "Abrir o jogo e conferir o painel do ReShade",
                $"No jogo, aperte {options.OverlayKeyLabel} para abrir o ReShade. " +
                "Na aba Início, o provedor de motion vectors e o DLSS 5 Feed já vêm marcados na ordem certa " +
                "(o programa gerou o preset). Confirme na aba Complementos que o DLSS 5 Feed aparece listado" +
                (profile.Route == InstallRoute.A ? " junto com o DLSS 5 Neural Rendering." : ". Em jogo 32-bit, as opções neurais ficam DENTRO do painel do DLSS 5 Feed, no grupo 'on the host', com botão Apply."), false));

            steps.Add(new ManualStep(n++, "Conferir o depth buffer",
                "Na aba Complementos → Generic Depth, confirme que o buffer da cena está selecionado e não está " +
                "marcado como Multisampled. Se a imagem ficar estranha, ative o DisplayDepth.fx para ver o depth: " +
                "se estiver invertido ou de cabeça para baixo, marque RESHADE_DEPTH_INPUT_IS_REVERSED / IS_UPSIDE_DOWN.", false));
        }
        else
        {
            steps.Add(new ManualStep(n++, "Abrir o jogo e conferir o painel do ReShade",
                $"No jogo, aperte {options.OverlayKeyLabel} para abrir o ReShade. A aba Início fica VAZIA de " +
                "propósito: quem trabalha é o addon, na aba Complementos → DLSS 5 Neural Rendering. O texto de " +
                "estado lá embaixo tem que dizer \"ACTIVE - NR INJECTED\"; \"HOOKS ARMED / NO DLSS CREATE SEEN\" " +
                "significa que o jogo não pediu DLSS (ligue no menu). F6 liga e desliga o efeito para comparar.", false));
        }

        steps.Add(new ManualStep(n++, "Voltar aqui e clicar em Verificar",
            "Com o jogo já aberto uma vez, o programa consegue ler os logs e dizer exatamente o que falta.", false));

        return steps;
    }

    /// <summary>Limitações que o usuário precisa saber antes de esperar ganho de FPS (spec 1).</summary>
    public static string Limitations =>
        "Limitações estruturais (não são erros de configuração):\r\n" +
        "• É DLAA only: a resolução de render é igual à de saída — NÃO existe ganho de performance.\r\n" +
        "• A HUD é processada junto com a cena.\r\n" +
        "• Motion vectors estimados geram ghosting em movimento rápido.\r\n" +
        "• O override de assinatura é global no sistema; anti-cheat (EAC, BattlEye) pode tratar como violação " +
        "de integridade. Não use em jogos online com anti-cheat.\r\n" +
        "• Em placas Ada (RTX 40) o consumo de VRAM é o pior caso: teste primeiro em 1080p, em janela.";
}
