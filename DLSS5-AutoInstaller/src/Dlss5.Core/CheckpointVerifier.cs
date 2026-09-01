namespace Dlss5.Core;

/// <summary>
/// Checkpoints de verificação da spec 9. Os itens 1–8 saem de arquivos e registro;
/// 9–16 exigem o jogo aberto (viram Manual até os logs aparecerem).
/// </summary>
public static class CheckpointVerifier
{
    /// <summary>ReShade.log menor que isso = placeholder "não fui carregado" (spec 8.3).</summary>
    public const long ReShadeLogPlaceholderSize = 982;

    public static IReadOnlyList<CheckResult> Verify(
        GameProfile profile, InstallManifest? manifest, string? nvngxDlssDoKit = null)
    {
        var r = new List<CheckResult>();
        var exe = profile.ExeFolder;
        var route = profile.Route;

        // 1 — override no registro
        var status = SignatureOverride.Query();
        r.Add(new CheckResult(1, "Override de assinatura NGX no registro",
            status.AllSet ? CheckStatus.Pass : CheckStatus.Fail,
            status.AllSet
                ? "As 3 chaves estão com DWORD 1."
                : "Faltando em: " + string.Join(", ",
                    status.Entries.Where(e => !e.Set).Select(e => "HKLM\\" + e.Path)),
            status.AllSet ? null : "Rode o programa como administrador e aplique o override."));

        // 2 — reiniciou depois do override
        bool reinicioPendente = false;
        if (manifest?.RegistryOverrideAppliedUtc is { } appliedUtc)
        {
            bool rebooted = SignatureOverride.RebootedSinceEnable(appliedUtc);
            reinicioPendente = !rebooted;
            r.Add(new CheckResult(2, "Reinício após aplicar o override",
                rebooted ? CheckStatus.Pass : CheckStatus.Warning,
                rebooted
                    ? "O PC foi reiniciado depois de aplicar o override."
                    : "O override foi aplicado nesta sessão e o PC ainda não foi reiniciado.",
                rebooted ? null : "Se quiser, reinicie o Windows quando for cômodo — o driver lê essa chave na inicialização. O programa nunca reinicia sozinho."));
        }
        else
        {
            r.Add(new CheckResult(2, "Reinício após aplicar o override",
                status.AllSet ? CheckStatus.Warning : CheckStatus.Manual,
                status.AllSet
                    ? "Override já estava aplicado antes desta instalação; se nunca reiniciou depois, reinicie."
                    : "Sem override aplicado por este programa.",
                "Na dúvida, reinicie o PC uma vez após aplicar o override."));
        }

        // 4 — exe real e arquitetura
        r.Add(profile.RealExePath is not null && profile.Architecture != PeArchitecture.Unknown
            ? new CheckResult(4, "Executável real identificado", CheckStatus.Pass,
                $"{Path.GetFileName(profile.RealExePath)} ({profile.Architecture}, {profile.Api}) → rota {route}")
            : new CheckResult(4, "Executável real identificado", CheckStatus.Fail,
                "Não identificado.", "Selecione manualmente o executável real do jogo."));

        // 9 — a tecla que o ReShade.ini REALMENTE gravou
        //
        // A tecla escolhida é lembrada entre execuções: basta ter trocado uma vez para
        // todo jogo instalado depois usar a nova, e o sintoma vira "o Home não abre" em
        // vários jogos ao mesmo tempo, sem nada de errado na instalação. Ler do arquivo
        // (e não da tela) é o que transforma isso numa linha visível.
        var ini = Path.Combine(exe, "ReShade.ini");
        if (File.Exists(ini))
        {
            string textoIni;
            try { textoIni = ReadShared(ini); } catch { textoIni = ""; }
            var tecla = ReShadeConfigWriter.LerTeclaDoOverlay(textoIni);
            bool ehHome = tecla is not null
                          && tecla.Value.VirtualKey == ReShadeConfigWriter.KeyHome
                          && !tecla.Value.Ctrl && !tecla.Value.Shift && !tecla.Value.Alt;
            r.Add(new CheckResult(9, "Tecla que abre o painel do ReShade",
                tecla is null ? CheckStatus.Warning : CheckStatus.Manual,
                tecla is null
                    ? "ReShade.ini não tem a linha KeyOverlay."
                    : $"É {ReShadeConfigWriter.DescribeKey(tecla.Value.VirtualKey, tecla.Value.Ctrl, tecla.Value.Shift, tecla.Value.Alt)}" +
                      (ehHome ? "." : " — NÃO é Home."),
                ehHome
                    ? "No jogo, aperte Home."
                    : "É esta a tecla a apertar no jogo. Para voltar ao Home, mude na tela de " +
                      "detecção e instale de novo — a escolha fica guardada entre execuções."));
        }

        // 12 — quem mais está disputando o DXGI agora
        var overlays = Overlays.Detectar(Overlays.ProcessosRodando());
        r.Add(overlays.Count == 0
            ? new CheckResult(12, "Sobreposições concorrendo pelo DXGI", CheckStatus.Pass,
                "Nenhuma sobreposição conhecida rodando agora.")
            : new CheckResult(12, "Sobreposições concorrendo pelo DXGI", CheckStatus.Warning,
                "Rodando agora: " + string.Join(", ", overlays.Select(o => o.Nome)) +
                ". Elas carregam o DXGI antes do ReShade e ficam com a interceptação.",
                string.Join("  |  ", overlays.Select(o => $"{o.Nome}: {o.ComoDesligar}"))));

        // 3 — o DLSS do próprio jogo: o kit não mexe nele. O que este checkpoint vigia de
        // verdade é o nvngx_dlss.dll do jogo, que desinstalações antigas chegaram a apagar
        // (e sem ele o menu de DLSS some e o jogo pode nem abrir).
        if (profile.HasNativeDlss && profile.NeedsFeeder)
        {
            var caminhoDll = Path.Combine(exe, "nvngx_dlss.dll");
            if (!File.Exists(caminhoDll))
            {
                r.Add(new CheckResult(3, "DLSS do jogo: nvngx_dlss.dll SUMIU da pasta", CheckStatus.Fail,
                    "O jogo tem DLSS próprio mas o nvngx_dlss.dll não está na pasta — uma " +
                    "desinstalação antiga apagou. Sem ele as opções de DLSS somem do menu e o " +
                    "jogo pode travar na abertura. O kit não põe o dele no lugar: a versão do " +
                    "kit não casa com o jogo.",
                    "Steam → clique direito no jogo → Propriedades → Arquivos instalados → " +
                    "Verificar integridade dos arquivos do jogo. Depois clique em Verificar de novo."));
            }
            else if (TransplanteDlss.EhDoKit(caminhoDll, nvngxDlssDoKit))
            {
                // O estágio final da saga do transplante: o arquivo EXISTE, então o
                // checkpoint antigo dizia "tudo certo" — mas ele é byte a byte o DO KIT,
                // posto ali por instalação antiga. É o estado que faz motor que carrega
                // o DLL na inicialização congelar antes da janela (o demo do Onimusha
                // nem abria), e a verificação de integridade sozinha nem sempre repõe o
                // original — em demo o depot pode não cobrir o arquivo.
                r.Add(new CheckResult(3, "DLSS do jogo: o nvngx_dlss.dll da pasta é o DO KIT", CheckStatus.Fail,
                    "O arquivo é byte a byte igual ao nvngx_dlss.dll do kit: uma instalação " +
                    "antiga o pôs no lugar do DLL do jogo. Com ele o menu de DLSS quebra e há " +
                    "motor que trava ANTES de criar a janela — o jogo nem abre.",
                    "Use Desinstalar (reverter) ou Desfazer tudo (forçado) — agora eles removem " +
                    "este arquivo. Depois: Steam → Propriedades → Arquivos instalados → Verificar " +
                    "integridade, abra o jogo sem instalar nada para confirmar, e só então reinstale."));
            }
            else
            {
                r.Add(new CheckResult(3, "DLSS do jogo: como você preferir", CheckStatus.Manual,
                    "O jogo tem DLSS próprio e o kit NÃO mexe nele: pode deixar ligado ou desligado " +
                    "no menu, inclusive baixando a qualidade para ganhar FPS. O Neural Rendering " +
                    $"entra por cima, pelo Feeder (o jogo roda em {profile.Api})."));
            }
        }
        else if (profile.UsesRenodxDirectPath)
        {
            r.Add(new CheckResult(3, "DLSS do jogo tem que ficar LIGADO", CheckStatus.Manual,
                "Aqui é o contrário: em D3D12 o RenoDX se pendura na chamada de DLSS que o próprio jogo faz. " +
                "Sem o jogo pedir DLSS, não existe chamada para interceptar.",
                "Opções gráficas do jogo → ligue o DLSS (qualquer modo). Sem isso o RenoDX fica em " +
                "\"HOOKS ARMED / NO DLSS CREATE SEEN\"."));
        }

        // 6 — arquitetura do ReShade instalado. O nome muda com a API: num jogo OpenGL
        // procurar por dxgi.dll acusaria falha mesmo com a instalação correta.
        var hook = profile.ReShadeHookName;
        var dxgi = Path.Combine(exe, hook);
        if (File.Exists(dxgi))
        {
            var arch = PeFile.GetArchitecture(dxgi);
            bool ok = arch == profile.Architecture;
            r.Add(new CheckResult(6, $"{hook} do jogo com a arquitetura do exe",
                ok ? CheckStatus.Pass : CheckStatus.Fail,
                $"{hook} é {arch}, exe é {profile.Architecture}.",
                ok ? null : "Arquitetura trocada: reinstale para extrair a versão certa do ReShade."));
        }
        else
        {
            r.Add(new CheckResult(6, $"{hook} do jogo", CheckStatus.Fail,
                $"{hook} não está na pasta do exe.", "Rode a instalação."));
        }

        if (route is InstallRoute.B or InstallRoute.C)
        {
            var hostDxgi = Path.Combine(exe, "host64", "dxgi.dll");
            if (File.Exists(hostDxgi))
            {
                var arch = PeFile.GetArchitecture(hostDxgi);
                bool ok = arch == PeArchitecture.X64;
                r.Add(new CheckResult(6, "host64\\dxgi.dll é x64",
                    ok ? CheckStatus.Pass : CheckStatus.Fail,
                    $"host64\\dxgi.dll é {arch}.",
                    ok ? null : "Precisa ser o ReShade x64."));
            }
            else
            {
                r.Add(new CheckResult(6, "host64\\dxgi.dll é x64", CheckStatus.Fail,
                    "host64\\dxgi.dll não existe.", "Rode a instalação."));
            }

            // addon32 na raiz e NENHUM .addon64 lá (armadilha do Tomb Raider)
            bool addon32 = File.Exists(Path.Combine(exe, "dlss5-feed.addon32"));
            var strayAddon64 = Directory.Exists(exe)
                ? Directory.EnumerateFiles(exe, "*.addon64", SearchOption.TopDirectoryOnly).ToList()
                : new List<string>();
            r.Add(new CheckResult(10, "Layout 32-bit correto",
                addon32 && strayAddon64.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail,
                addon32
                    ? (strayAddon64.Count == 0
                        ? "addon32 na raiz e nenhum .addon64 solto."
                        : "Há .addon64 na raiz: " + string.Join(", ", strayAddon64.Select(Path.GetFileName)))
                    : "dlss5-feed.addon32 não está na pasta do exe.",
                strayAddon64.Count > 0
                    ? "Em jogo x86 os .addon64 vão SÓ dentro de host64\\ — na raiz deixam a aba Add-ons vazia."
                    : null));
        }

        // 11 — efeitos encontráveis
        var feedFx = Path.Combine(exe, "reshade-shaders", "Shaders", "DLSS5_Feed.fx");
        r.Add(new CheckResult(11, "Shaders no lugar",
            File.Exists(feedFx) ? CheckStatus.Pass : CheckStatus.Fail,
            File.Exists(feedFx)
                ? "reshade-shaders\\Shaders\\DLSS5_Feed.fx presente."
                : "DLSS5_Feed.fx não encontrado.",
            File.Exists(feedFx) ? null : "Rode a instalação (ou reinstale: o desinstalador do ReShade apaga reshade-shaders\\)."));

        // 13 — preset com o provedor acima do Feed
        //
        // Só faz sentido quando o Feeder está instalado. No caminho direto do RenoDX
        // (D3D12 com DLSS nativo) o preset sai VAZIO de propósito — não há efeito de
        // ReShade participando — e exigir a linha ali fazia uma instalação correta
        // aparecer como FALHA em vermelho.
        var preset = Path.Combine(exe, "ReShadePreset.ini");
        if (!profile.NeedsFeeder)
        {
            r.Add(new CheckResult(13, "Preset do ReShade", CheckStatus.NotApplicable,
                "Sem efeitos, como esperado: em D3D12 com DLSS nativo quem trabalha é o addon " +
                "do RenoDX, não um shader do ReShade.",
                "A aba Início do ReShade fica vazia neste caminho. O que importa está na aba " +
                "Complementos, em DLSS 5 Neural Rendering."));
        }
        else if (File.Exists(preset))
        {
            var text = File.ReadAllText(preset);
            var techLine = text.Split('\n')
                .FirstOrDefault(l => l.StartsWith("Techniques=", StringComparison.OrdinalIgnoreCase))?.Trim();
            bool hasFeed = techLine?.Contains("DLSS5_Feed@", StringComparison.OrdinalIgnoreCase) == true;
            bool hasMv = techLine is not null &&
                         (techLine.Contains("DRME@", StringComparison.OrdinalIgnoreCase) ||
                          techLine.Contains("MartysMods_Launchpad@", StringComparison.OrdinalIgnoreCase));
            bool mvFirst = false;
            if (techLine is not null && hasFeed && hasMv)
            {
                int mvIdx = Math.Max(techLine.IndexOf("DRME@", StringComparison.OrdinalIgnoreCase),
                                     techLine.IndexOf("MartysMods_Launchpad@", StringComparison.OrdinalIgnoreCase));
                int feedIdx = techLine.IndexOf("DLSS5_Feed@", StringComparison.OrdinalIgnoreCase);
                mvFirst = mvIdx >= 0 && mvIdx < feedIdx;
            }
            r.Add(new CheckResult(13, "Provedor de MV marcado ACIMA do DLSS 5 Feed",
                hasFeed && hasMv && mvFirst ? CheckStatus.Pass : CheckStatus.Fail,
                techLine ?? "linha Techniques= ausente",
                hasFeed && hasMv && mvFirst
                    ? null
                    : "O preset deve listar o provedor de MV antes do DLSS5_Feed."));
        }
        else
        {
            r.Add(new CheckResult(13, "ReShadePreset.ini", CheckStatus.Fail,
                "Preset não encontrado.", "Rode a instalação."));
        }

        // 5 — dgVoodoo (rota C)
        if (route == InstallRoute.C)
        {
            var renderer = profile.RendererFolder ?? exe;
            var wrapper = profile.DgVoodooWrapperName;
            var d3d9 = Path.Combine(renderer, wrapper);
            var conf = Path.Combine(renderer, "dgVoodoo.conf");
            bool d3d9Ok = File.Exists(d3d9) && PeFile.GetArchitecture(d3d9) == PeArchitecture.X86;
            r.Add(new CheckResult(5, "dgVoodoo2 na pasta do renderizador",
                d3d9Ok ? CheckStatus.Pass : CheckStatus.Fail,
                d3d9Ok ? $"{wrapper} (x86) em {renderer}" : $"{wrapper} x86 ausente em {renderer}",
                d3d9Ok ? null : $"No Source o {wrapper} vai em bin\\, não na raiz."));

            if (File.Exists(conf))
            {
                var text = File.ReadAllText(conf);
                bool passthru = text.Contains("DisableAndPassThru", StringComparison.OrdinalIgnoreCase)
                                && !ValueIs(text, "DisableAndPassThru", "true");
                r.Add(new CheckResult(5, "dgVoodoo.conf ajustado",
                    passthru ? CheckStatus.Pass : CheckStatus.Fail,
                    passthru ? "DisableAndPassThru=false (dgVoodoo ativo)." : "DisableAndPassThru ainda está true.",
                    passthru ? null : "Com passthru=true o dgVoodoo não faz nada — causa nº 1 de 'não acontece nada'."));
            }

            r.Add(new CheckResult(5, "Marca d'água do dgVoodoo na tela", CheckStatus.Manual,
                "Abra o jogo: a marca d'água do dgVoodoo tem que aparecer.",
                "Sem marca d'água, o dgVoodoo não está interceptando."));
        }

        // 7/8 — ReShade carregou e viu o swapchain (lê o log se já existir)
        r.AddRange(VerifyReShadeLog(exe, profile.GameFolder, reinicioPendente));

        // 14/15/16 — dependem do jogo rodando
        r.AddRange(VerifyFeedLogs(exe, route, profile.NeedsFeeder));

        return r;
    }

    private static bool ValueIs(string iniText, string key, string value)
    {
        foreach (var line in iniText.Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            if (!line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            if (line[(eq + 1)..].Trim().Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// O ReShade grava o log ao lado da DLL que foi carregada. Se ele não está na pasta do
    /// exe mas existe em OUTRA pasta do jogo, isso não é "não carregou" — é "carregou em
    /// outro lugar", e essa outra pasta é onde a instalação deveria ter ido. Achar o
    /// arquivo é bem mais barato do que deduzir a estrutura de cada engine.
    /// </summary>
    private static string? LogEmOutraPasta(string exeFolder, string? gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder)) return null;
        try
        {
            return Directory.EnumerateFiles(gameFolder, "ReShade.log", SearchOption.AllDirectories)
                .FirstOrDefault(p => !string.Equals(
                    Path.GetDirectoryName(p), exeFolder, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<CheckResult> VerifyReShadeLog(
        string exeFolder, string? gameFolder = null, bool reinicioPendente = false)
    {
        var log = Path.Combine(exeFolder, "ReShade.log");
        if (!File.Exists(log))
        {
            var noutraPasta = LogEmOutraPasta(exeFolder, gameFolder);
            if (noutraPasta is not null)
            {
                yield return new CheckResult(7, "ReShade carregou — mas em outra pasta", CheckStatus.Fail,
                    $"ReShade.log não está na pasta do exe, e sim em {Path.GetDirectoryName(noutraPasta)}.",
                    "É ali que o processo que renderiza roda. Volte à Detecção, aponte no botão " +
                    "\"Outro...\" o executável dessa pasta e instale de novo.");
                yield break;
            }

            yield return new CheckResult(7, "ReShade carregou", CheckStatus.Manual,
                "ReShade.log ainda não existe — abra o jogo uma vez.",
                "Depois de abrir o jogo, volte aqui e clique em Verificar de novo. Se você JÁ abriu " +
                "e o arquivo continua não existindo, o ReShade não foi carregado: quase sempre é " +
                "uma sobreposição que pegou o DXGI antes (EA App/Origin, Discord, RivaTuner/MSI " +
                "Afterburner, Steam, NVIDIA App) — desligue todas e teste de novo. Se ainda assim " +
                "não aparecer, o executável escolhido não é o que renderiza: procure Binaries\\Win64 " +
                "ou um *-Shipping.exe e aponte no botão \"Outro...\" da tela de detecção.");
            yield break;
        }

        var size = new FileInfo(log).Length;
        string text;
        try { text = ReadShared(log); } catch { text = ""; }

        bool loaded = size > ReShadeLogPlaceholderSize
                      && text.Contains("ReShade", StringComparison.OrdinalIgnoreCase);
        yield return new CheckResult(7, "ReShade carregou",
            loaded ? CheckStatus.Pass : CheckStatus.Fail,
            loaded ? $"ReShade.log com {size} bytes." : $"ReShade.log com {size} bytes (placeholder).",
            loaded ? null : "Arquitetura do dxgi.dll errada, local errado, ou outro módulo tomou o nome dxgi.dll.");

        // 14 — o DLSS 5 chegou a rodar? É a única pergunta que interessa, e até agora o
        // programa não sabia responder: ele conferia arquivo, não resultado.
        var renodx = RenodxLog.Ler(text);
        if (renodx is not null)
        {
            var estado = renodx.AssinaturaRecusada ? CheckStatus.Fail
                       : renodx.Ativo ? CheckStatus.Pass
                       : renodx.HooksSemUso ? CheckStatus.Fail
                       : CheckStatus.Warning;

            // O padrão que enganou todo mundo: com o override gravado mas o PC sem
            // reiniciar, o log registra "ativo" e a imagem não muda NADA — o driver só
            // carrega a chave no boot, e o que roda no lugar é um caminho vazio. Um
            // "OK" aqui nesse estado é mentira; vira aviso até o reinício acontecer.
            if (estado == CheckStatus.Pass && reinicioPendente)
            {
                yield return new CheckResult(14, "DLSS 5 aplicado na imagem", CheckStatus.Warning,
                    renodx.Resumo + " Porém o PC não foi reiniciado desde o override: esse \"ativo\" " +
                    "pode ser vazio — log dizendo que aplicou e imagem inalterada.",
                    "Se a imagem não mudou, reinicie o PC quando puder e teste de novo (o programa nunca reinicia sozinho).");
            }
            else
            yield return new CheckResult(14, "DLSS 5 aplicado na imagem", estado, renodx.Resumo,
                estado == CheckStatus.Pass
                    ? "Está funcionando. É DLAA: mesma resolução de render e de saída, então não " +
                      "há ganho de FPS e a diferença é de imagem. No caminho direto (D3D12 com DLSS " +
                      "nativo) a aba Início do ReShade fica VAZIA de propósito — quem trabalha é o " +
                      "addon, na aba Complementos."
                    : renodx.AssinaturaRecusada
                        ? "Aplique o override no registro e reinicie o PC quando puder — o driver só lê essa chave na inicialização."
                        : renodx.HooksSemUso
                            ? "O RenoDX só enxerga NGX em D3D12. Fora disso quem trabalha é o Feeder; " +
                              "confirme que o DLSS 5 Feed está marcado no preset."
                            : "Abra o jogo, jogue alguns segundos e verifique de novo.");
        }

        bool sawSwapchain = text.Contains("CreateSwapChain", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("Recreated runtime environment", StringComparison.OrdinalIgnoreCase);
        yield return new CheckResult(8, "ReShade viu o swapchain",
            sawSwapchain ? CheckStatus.Pass : (loaded ? CheckStatus.Fail : CheckStatus.Manual),
            sawSwapchain
                ? "Log tem CreateSwapChain / Recreated runtime environment."
                : "Log sem CreateSwapChain: o ReShade carregou mas não é a factory do renderizador.",
            sawSwapchain ? null : "O dxgi.dll tem que estar na pasta do EXE. Overlays podem estar chegando antes.");
    }

    private static IEnumerable<CheckResult> VerifyFeedLogs(
        string exeFolder, InstallRoute route, bool needsFeeder = true)
    {
        if (!needsFeeder)
        {
            yield return new CheckResult(15, "Feeder", CheckStatus.NotApplicable,
                "Não instalado neste caminho: o RenoDX se pendura direto na chamada de DLSS do jogo.",
                null);
            yield break;
        }

        var feedLog = Path.Combine(exeFolder, "dlss5-feed.log");
        if (!File.Exists(feedLog))
        {
            yield return new CheckResult(15, "Feeder entregando frames", CheckStatus.Manual,
                "dlss5-feed.log ainda não existe — abra o jogo com os efeitos marcados.",
                null);
            yield break;
        }

        string text;
        try { text = ReadShared(feedLog); } catch { text = ""; }

        bool ready = text.Contains("feature ready", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("DLAA", StringComparison.OrdinalIgnoreCase);
        bool delivered = text.Contains("delivered", StringComparison.OrdinalIgnoreCase);
        yield return new CheckResult(15, "Feeder entregando frames",
            ready && delivered ? CheckStatus.Pass : CheckStatus.Warning,
            ready && delivered
                ? "Log mostra feature pronta e frames entregues."
                : "Log existe mas ainda sem 'feature ready ... DLAA' + 'frame N delivered'.",
            ready && delivered ? null : "Veja o diagnóstico abaixo.");

        if (route is InstallRoute.B or InstallRoute.C)
        {
            var hostLog = Path.Combine(exeFolder, "host64", "dlss5-feed-host.log");
            yield return new CheckResult(16, "Processo auxiliar host64 rodando",
                File.Exists(hostLog) ? CheckStatus.Pass : CheckStatus.Manual,
                File.Exists(hostLog) ? "host64\\dlss5-feed-host.log presente." : "Log do host ainda não existe.",
                null);
        }
    }

    /// <summary>Lê um log que pode estar aberto pelo jogo.</summary>
    internal static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }
}
