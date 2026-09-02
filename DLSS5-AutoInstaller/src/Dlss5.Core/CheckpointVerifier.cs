using System.Globalization;

namespace Dlss5.Core;

/// <summary>
/// Checkpoints de verificação da spec 9. Os itens 1–8 saem de arquivos e registro;
/// 9–16 exigem o jogo aberto (viram Manual até os logs aparecerem).
/// </summary>
public static class CheckpointVerifier
{
    /// <summary>ReShade.log menor que isso = placeholder "não fui carregado" (spec 8.3).</summary>
    public const long ReShadeLogPlaceholderSize = 982;

    /// <param name="overrideNoBoot">
    /// O override estava no registro quando o Windows subiu (ver
    /// <see cref="SignatureOverride.EstadoNoBoot"/>). Com esse dado, o checkpoint 2 só
    /// acusa reinício pendente quando o estado de agora difere do que o driver leu no
    /// boot. Sem ele, cai na heurística antiga do carimbo do manifesto.
    /// </param>
    public static IReadOnlyList<CheckResult> Verify(
        GameProfile profile, InstallManifest? manifest, string? nvngxDlssDoKit = null,
        bool? overrideNoBoot = null)
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
        if (overrideNoBoot is bool noBoot)
        {
            // O dado bom: o driver lê a chave no boot, então o que importa é se o estado
            // de agora é o mesmo que ele viu subindo — não quando o manifesto foi gravado.
            reinicioPendente = status.AllSet && !noBoot;
            r.Add(new CheckResult(2, "Reinício após aplicar o override",
                !status.AllSet ? CheckStatus.Manual : reinicioPendente ? CheckStatus.Warning : CheckStatus.Pass,
                !status.AllSet
                    ? "Sem override aplicado."
                    : reinicioPendente
                        ? "O override foi aplicado depois que o Windows subiu: o driver ainda não leu a chave."
                        : "O Windows já subiu com o override aplicado — desinstalar e reinstalar hoje não muda isso.",
                reinicioPendente
                    ? "Se quiser, reinicie o Windows quando for cômodo — o driver lê essa chave na inicialização. O programa nunca reinicia sozinho."
                    : null));
        }
        else if (manifest?.RegistryOverrideAppliedUtc is { } appliedUtc)
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
        var ini = profile.ReShadeIniPath;
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
                    ? $"{Path.GetFileName(ini)} não tem a linha KeyOverlay."
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

        // 3 — o DLSS do próprio jogo: o kit não mexe nele. O que este checkpoint vigia é o
        // nvngx_dlss.dll do jogo — e só acusa problema quando há PROVA de que este programa
        // mexeu nele (backup guardado, ou o arquivo do kit posto no lugar do original).
        // A ausência do arquivo, sozinha, não é problema nenhum: é o normal em jogo que
        // carrega o DLSS pelo driver.
        if (profile.HasNativeDlss)
        {
            // Nos dois caminhos o DLSS do jogo precisa ser o DELE: no direto o RenoDX se
            // pendura na chamada que o jogo faz, e no Feeder o NGX é o mesmo.
            var caminhoDll = Path.Combine(exe, "nvngx_dlss.dll");
            bool naPasta = File.Exists(caminhoDll);

            // "Não está na pasta" NÃO é o mesmo que "sumiu". Jogo de Streamline (sl.dlss.dll,
            // nvngx_dlssg.dll) carrega o runtime de DLSS pelo driver, e nunca traz esse arquivo
            // no depot: o RE9 foi reinstalado do zero e continuou sem ele, abrindo normalmente.
            // Acusar sumiço sem prova mandava o usuário verificar integridade para repor um
            // arquivo que a Steam não tem para devolver. Prova de sumiço é uma só: o backup que
            // ESTE programa guarda quando tira o original do lugar.
            bool guardamosOOriginal =
                File.Exists(caminhoDll + Propriedade.BackupSuffix) ||
                (manifest?.BackedUpFiles.ContainsKey(caminhoDll) ?? false) ||
                (manifest?.RemovedFiles.ContainsKey(caminhoDll) ?? false);

            // Frase para colar nos vereditos de baixo quando o arquivo não está na pasta e
            // isso é o normal do jogo — assim o usuário não fica procurando o que não falta.
            const string VemDoDriver =
                " O jogo não traz nvngx_dlss.dll na pasta, e não precisa: nesses jogos o runtime " +
                "de DLSS vem do driver da NVIDIA (é o Streamline que pede). Não há o que repor aqui.";

            if (!naPasta && guardamosOOriginal)
            {
                r.Add(new CheckResult(3, "DLSS do jogo: fomos nós que tiramos o nvngx_dlss.dll", CheckStatus.Fail,
                    "O nvngx_dlss.dll não está na pasta, e existe backup dele guardado por este " +
                    "programa: quem o tirou do lugar fomos nós, e ele não voltou. Sem o arquivo as " +
                    "opções de DLSS podem sumir do menu.",
                    "Use Desinstalar (reverter) ou Desfazer tudo (forçado): os dois devolvem o " +
                    "arquivo .dlss5bak ao lugar. Depois clique em Verificar de novo."));
            }
            else if (naPasta && TransplanteDlss.EhDoKit(caminhoDll, nvngxDlssDoKit))
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
            else if (profile.UsesRenodxDirectPath)
            {
                r.Add(new CheckResult(3, "DLSS do jogo tem que ficar LIGADO", CheckStatus.Manual,
                    "Caminho direto: em D3D12 o RenoDX se pendura na chamada de DLSS que o próprio jogo faz. " +
                    "Sem o jogo pedir DLSS, não existe chamada para interceptar." + (naPasta ? "" : VemDoDriver),
                    "Opções gráficas do jogo → ligue o DLSS (qualquer modo). Sem isso o RenoDX fica em " +
                    "\"HOOKS ARMED / NO DLSS CREATE SEEN\"."));
            }
            else
            {
                // A regra "como você preferir" caiu com evidência: Onimusha e GTA 5. O Feeder
                // inicializa um NGX próprio dentro do processo; com o DLSS do jogo ligado, o
                // NGX do jogo e o nosso colidem — trava depois da tela inicial, ou ao aplicar
                // o DLSS no menu. Desligado, o jogo não chama o NGX e o Feeder trabalha como
                // nos jogos sem DLSS.
                r.Add(new CheckResult(3, "DLSS do jogo: DESLIGADO neste caminho", CheckStatus.Manual,
                    "Caminho do Feeder num jogo com DLSS próprio: o Feeder roda um NGX dele dentro do " +
                    "processo, e com o DLSS do jogo ligado os dois colidem — o jogo trava depois da tela " +
                    $"inicial ou ao aplicar o DLSS no menu (o jogo roda em {profile.Api})." + (naPasta ? "" : VemDoDriver),
                    "Opções gráficas do jogo → DLSS desligado (faça isso com o jogo limpo, ou com o ReShade " +
                    "desligado pelo Isolar a causa). O Neural Rendering entra pelo Feed, sem o DLSS do jogo."));
            }
        }

        // 18 — QUAL nvngx_dlssnr.dll está no jogo. Foi o que faltou no RE9: todos os outros
        // itens OK, o addon dizendo ACTIVE, e o arquivo era um remendo do original de RTX 50
        // rodando numa RTX 4070 Ti — processava, dizia OK e não desenhava nada. O nome e a
        // versão não separam os builds; o hash separa.
        {
            string? textoDoLog = null;
            try { if (File.Exists(profile.ReShadeLogPath)) textoDoLog = ReadShared(profile.ReShadeLogPath); }
            catch { }
            var serie = RuntimeNr.SerieRtxNoLog(textoDoLog);

            var pastas = route is InstallRoute.B or InstallRoute.C
                ? new[] { Path.Combine(exe, "host64") }
                : new[] { exe };
            foreach (var pasta in pastas)
            {
                var caminho = Path.Combine(pasta, RuntimeNr.Arquivo);
                if (!File.Exists(caminho)) continue;   // a ausência já é acusada pelo item de arquivos

                var build = RuntimeNr.Identificar(caminho);
                var (falha, texto) = RuntimeNr.Avaliar(build, serie);
                if (!falha && RuntimeNr.AddonMarcouComoDesconhecido(textoDoLog))
                {
                    // O arquivo é bom mas o log é de uma rodada com o arquivo antigo: o
                    // usuário trocou e ainda não abriu o jogo. Não é falha, é "abra e veja".
                    texto += " (O ReShade.log atual ainda é de uma rodada com outro runtime.)";
                }
                r.Add(new CheckResult(18, "Runtime do DLSS 5 (nvngx_dlssnr.dll) é o certo para a placa",
                    falha ? CheckStatus.Fail : CheckStatus.Pass,
                    texto,
                    falha ? RuntimeNr.ComoTrocar : null));
            }
        }

        // 19 — Fox Engine: o patch anti-hook está no executável? Sem ele nada abaixo importa.
        if (MotorFox.EhFoxEngine(profile.RealExePath))
        {
            var exeFox = profile.RealExePath!;
            var estado = MotorFox.EstadoDoExe(exeFox);
            bool patch = MotorFox.PatchAplicado(exeFox);
            string detalhe = estado switch
            {
                EstadoDoExeFox.Remendado => "O hash do mgsvtpp.exe é o do exe remendado: o CheckModuleHook está desviado.",
                EstadoDoExeFox.Original => "O mgsvtpp.exe é o 1.0.15.4 Steam inglês INTACTO: a checagem ainda está ativa e o jogo " +
                                           "vai se fechar com o ReShade. (Se você tinha aplicado o patch, a Steam restaurou o exe.)",
                EstadoDoExeFox.Desconhecido => patch
                    ? $"Existe {Path.GetFileName(MotorFox.CaminhoDoBackup(exeFox))} ao lado, mas o exe não tem o hash do remendo " +
                      $"conhecido ({MotorFox.DescreverExe(exeFox)})."
                    : MotorFox.ExeNaoCoberto(exeFox),
                _ => "Não achei o executável.",
            };
            string? dica = estado switch
            {
                EstadoDoExeFox.Remendado => "Atenção: a verificação de integridade da Steam restaura o exe original e desfaz o patch.",
                EstadoDoExeFox.Original => MotorFox.PatcherCobre(exeFox)
                    ? "Instale de novo: a instalação aplica o patch sozinha, antes de qualquer outro arquivo."
                    : MotorFox.SemPatcherParaGz,
                _ => patch ? null : MotorFox.PatcherCobre(exeFox) ? null : MotorFox.SemPatcherParaGz,
            };
            r.Add(new CheckResult(19, "Patch anti-hook da Fox Engine (CheckModuleHook)",
                estado == EstadoDoExeFox.Remendado || (estado == EstadoDoExeFox.Desconhecido && patch)
                    ? CheckStatus.Pass : CheckStatus.Fail,
                detalhe, dica));
        }

        // 6 — arquitetura do ReShade instalado. O nome muda com a API: num jogo OpenGL
        // procurar por dxgi.dll acusaria falha mesmo com a instalação correta.
        if (profile.UsarReFramework)
        {
            var dinput = ReFramework.CaminhoDinput8(exe);
            var reshade = Path.Combine(profile.PastaDoReShade, profile.ReShadeHookName);
            bool temRef = File.Exists(dinput);
            bool temReShade = File.Exists(reshade);

            r.Add(new CheckResult(6, "REFramework ao lado do ReShade",
                temRef && temReShade ? CheckStatus.Pass : CheckStatus.Fail,
                temRef
                    ? (temReShade
                        ? $"dinput8.dll e {profile.ReShadeHookName} na pasta do exe."
                        : $"dinput8.dll está lá, mas falta o {profile.ReShadeHookName}.")
                    : "dinput8.dll (REFramework) não está na pasta do exe.",
                temRef && temReShade
                    ? null
                    : "Rode a instalação. Os dois moram juntos na pasta do exe: o REFramework desarma a " +
                      "checagem de integridade e o ReShade é a DLL que o jogo carrega."));

            // 17 — o log do PRÓPRIO REFramework. Quando "o jogo abre e o Home não faz
            // nada", é este arquivo que separa as três causas possíveis, e nenhuma delas
            // dá para adivinhar olhando a pasta: (a) o REFramework não carregou; (b) ele
            // carregou mas guarda os dados em %APPDATA% porque não conseguiu escrever na
            // pasta do jogo — e aí varre um reframework\plugins que não é o nosso; (c) ele
            // carregou o ReShade e o problema é adiante.
            var pastaRef = ReFramework.PastaEmUso(exe, profile.RealExePath);
            if (pastaRef is null)
            {
                r.Add(new CheckResult(17, "REFramework carregou no jogo", CheckStatus.Fail,
                    $"O {ReFramework.LogDoFramework} não existe nem na pasta do jogo nem em " +
                    $"{ReFramework.PastaAppData(profile.RealExePath)} — o REFramework não chegou a rodar.",
                    "Abra o jogo uma vez e verifique de novo. Se continuar assim, o jogo não está " +
                    "carregando o dinput8.dll da pasta: confira se ele está junto do exe que renderiza."));
            }
            else
            {
                bool naPastaDoJogo = string.Equals(pastaRef, exe, StringComparison.OrdinalIgnoreCase);
                string textoRefLog;
                try { textoRefLog = ReadShared(Path.Combine(pastaRef, ReFramework.LogDoFramework)); }
                catch { textoRefLog = ""; }
                bool desarmou = textoRefLog.Contains("REFramework entry", StringComparison.OrdinalIgnoreCase);

                r.Add(new CheckResult(17, "REFramework rodou no jogo",
                    naPastaDoJogo && desarmou ? CheckStatus.Pass : CheckStatus.Fail,
                    !desarmou
                        ? $"Existe um {ReFramework.LogDoFramework} em {pastaRef}, mas sem o registro de entrada do REFramework."
                        : naPastaDoJogo
                            ? "O REFramework entrou no jogo e é ele que desarma a checagem de integridade. " +
                              "Se o ReShade ainda não abrir, a causa é do ReShade, não dele."
                            : $"O REFramework NÃO consegue escrever na pasta do jogo e caiu para {pastaRef}. " +
                              "Ele ainda desarma a checagem, mas a configuração dele fica fora da pasta do jogo.",
                    naPastaDoJogo && desarmou
                        ? null
                        : naPastaDoJogo
                            ? "Abra o jogo uma vez com esta instalação e verifique de novo."
                            : "Dê permissão de escrita na pasta do jogo ao seu usuário do Windows " +
                              "(Propriedades → Segurança) e abra o jogo de novo."));
            }

            // Aqui morava um veredito que sobrou do desenho antigo, quando o ReShade ia
            // DENTRO do REFramework: ele acusava "sobrou o dxgi.dll da injeção direta" e
            // mandava apagar o arquivo. No desenho de hoje o REFramework entra AO LADO do
            // ReShade, e esse dxgi.dll é o ReShade — a tela ficava dizendo, ao mesmo tempo,
            // que ele está certo (item 6) e que precisa ser apagado. Quem seguisse a dica
            // apagava a instalação que estava funcionando.
        }

        // A arquitetura do ReShade vale nos dois casos: com REFramework ou sem ele, é a
        // mesma DLL que o jogo carrega.
        {
            var hook = profile.ReShadeHookName;
            var dxgi = Path.Combine(profile.PastaDoReShade, hook);
            if (File.Exists(dxgi))
            {
                var arch = PeFile.GetArchitecture(dxgi);
                bool ok = arch == profile.Architecture;
                r.Add(new CheckResult(6, $"{hook} do jogo com a arquitetura do exe",
                    ok ? CheckStatus.Pass : CheckStatus.Fail,
                    $"{hook} é {arch}, exe é {profile.Architecture}.",
                    ok ? null : "Arquitetura trocada: reinstale para extrair a versão certa do ReShade."));
            }
            else if (!profile.UsarReFramework)
            {
                // Com REFramework o item acima já cobrou a ausência, e repetir vira ruído.
                r.Add(new CheckResult(6, $"{hook} do jogo", CheckStatus.Fail,
                    $"{hook} não está na pasta do exe.", "Rode a instalação."));
            }
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

        // 12 — o addon é encontrável a partir de onde o ReShade mora
        //
        // Hospedado no REFramework o ReShade fica em reframework\plugins e resolve
        // AddonPath a partir dali: com o ".\" de sempre ele procura o addon dentro de
        // plugins\, não acha, e o painel abre sem a aba do DLSS 5 — foi exatamente o que
        // aconteceu no primeiro teste do RE9 (jogo abriu, Home abriu, DLSS não carregou).
        if (profile.UsarReFramework)
        {
            var addon = Path.Combine(exe, "renodx-dlss5.addon64");
            string textoRef;
            try { textoRef = File.Exists(profile.ReShadeIniPath) ? ReadShared(profile.ReShadeIniPath) : ""; }
            catch { textoRef = ""; }
            bool absoluto = textoRef.Contains("AddonPath=" + exe, StringComparison.OrdinalIgnoreCase);

            r.Add(new CheckResult(12, "Addon alcançável pelo ReShade",
                File.Exists(addon) ? CheckStatus.Pass : CheckStatus.Fail,
                File.Exists(addon)
                    ? "renodx-dlss5.addon64 está na pasta do exe, que é onde o AddonPath aponta."
                    : "renodx-dlss5.addon64 não está na pasta do exe.",
                File.Exists(addon) ? null : "Rode a instalação de novo."));
        }

        // 11 — efeitos encontráveis. Sem a pasta, o ReShade abre reclamando na aba
        // Início mesmo no caminho direto, onde nenhum efeito seria usado.
        var feedFx = Path.Combine(exe, "reshade-shaders", "Shaders", "DLSS5_Feed.fx");
        r.Add(new CheckResult(11, "Shaders no lugar",
            File.Exists(feedFx) ? CheckStatus.Pass : CheckStatus.Fail,
            File.Exists(feedFx)
                ? "reshade-shaders\\Shaders\\DLSS5_Feed.fx presente."
                : "DLSS5_Feed.fx não encontrado — o ReShade vai abrir reclamando que não há efeitos.",
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
            // Encadeado atrás do DxWrapper, o dgVoodoo tem outro nome — e o D3D9.dll da
            // pasta é o DxWrapper, que passaria neste teste sem ser o que interessa.
            bool encadeado = DxWrapperChain.Encadeado(renderer, wrapper);
            var nomeDg = encadeado ? profile.DgVoodooChainedName : wrapper;
            var d3d9 = Path.Combine(renderer, nomeDg);
            var conf = Path.Combine(renderer, "dgVoodoo.conf");
            bool d3d9Ok = File.Exists(d3d9) && PeFile.GetArchitecture(d3d9) == PeArchitecture.X86;
            r.Add(new CheckResult(5, "dgVoodoo2 na pasta do renderizador",
                d3d9Ok ? CheckStatus.Pass : CheckStatus.Fail,
                d3d9Ok
                    ? $"{nomeDg} (x86) em {renderer}" + (encadeado ? " — encadeado atrás do DxWrapper." : "")
                    : $"{nomeDg} x86 ausente em {renderer}",
                d3d9Ok ? null : $"No Source o {wrapper} vai em bin\\, não na raiz."));

            if (encadeado)
            {
                var iniDxw = Path.Combine(renderer, DxWrapperChain.IniPara(wrapper));
                string? real = null;
                try { if (File.Exists(iniDxw)) real = DxWrapperChain.LerRealDllPath(ReadShared(iniDxw)); } catch { }
                bool fechada = real is not null && Path.GetFileName(real)
                    .Equals(nomeDg, StringComparison.OrdinalIgnoreCase);
                r.Add(new CheckResult(5, "DxWrapper encadeado ao dgVoodoo (RealDllPath)",
                    fechada ? CheckStatus.Pass : CheckStatus.Fail,
                    fechada
                        ? $"{Path.GetFileName(iniDxw)} aponta para {nomeDg}: o DxWrapper carrega o dgVoodoo, não o d3d9 do Windows."
                        : real is null
                            ? $"{Path.GetFileName(iniDxw)} sem RealDllPath: o DxWrapper carrega o d3d9 do Windows e o dgVoodoo fica fora."
                            : $"RealDllPath aponta para {real}, não para {nomeDg}.",
                    fechada ? null : $"Rode a instalação de novo — ela grava o RealDllPath no {Path.GetFileName(iniDxw)}."));
            }

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
        // 20 — DLL fora da raiz: o ReShade.ini da raiz tem que redirecionar a base para a raiz.
        if (profile.ReShadeForaDaRaiz)
        {
            string? basePath = null;
            try { if (File.Exists(profile.ReShadeIniPath)) basePath = ReShadeConfigWriter.LerBasePath(ReadShared(profile.ReShadeIniPath)); }
            catch { }
            bool ok = basePath is not null && string.Equals(
                Path.GetFullPath(basePath).TrimEnd('\\', '/'), Path.GetFullPath(exe).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
            r.Add(new CheckResult(20, "Base do ReShade redirecionada para a raiz ([INSTALL] BasePath)",
                ok ? CheckStatus.Pass : CheckStatus.Fail,
                ok ? $"ReShade.ini da raiz tem BasePath={basePath}: ini, log, shaders e addons são lidos da raiz mesmo com a DLL em {Path.GetFileName(profile.PastaDoReShade)}."
                   : basePath is null
                        ? "O ReShade.ini da raiz não tem [INSTALL] BasePath: com a DLL fora da raiz, o ReShade usa a pasta da DLL como base e não acha efeito nem addon nenhum (\"Nenhum arquivo de efeito encontrado\")."
                        : $"O BasePath do ReShade.ini ({basePath}) não aponta para a raiz ({exe}).",
                ok ? null : "Instale de novo (Atualizar): o ini é regravado com o BasePath certo."));
        }

        r.AddRange(VerifyReShadeLog(exe, profile.GameFolder, reinicioPendente, profile.ReShadeLogPath,
            profile.ReShadeHookName, profile.UsarReFramework, profile.RealExePath, profile.PastaDoReShade));

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
        string exeFolder, string? gameFolder = null, bool reinicioPendente = false,
        string? logPath = null, string nomeDoReShade = "dxgi.dll", bool hospedado = false,
        string? exePath = null, string? pastaDoReShade = null)
    {
        // Hospedado no REFramework, o ReShade grava o log ao lado da própria DLL.
        var log = logPath ?? Path.Combine(exeFolder, "ReShade.log");
        if (!File.Exists(log))
        {
            var noutraPasta = LogEmOutraPasta(exeFolder, gameFolder);
            if (noutraPasta is not null)
            {
                var pastaDoLog = Path.GetDirectoryName(noutraPasta);
                bool aoLadoDaDll = pastaDoReShade is not null &&
                                   string.Equals(pastaDoLog, pastaDoReShade, StringComparison.OrdinalIgnoreCase);
                yield return new CheckResult(7, "ReShade carregou — mas em outra pasta", CheckStatus.Fail,
                    aoLadoDaDll
                        ? $"ReShade.log nasceu ao lado da DLL ({pastaDoLog}), não na raiz: o ReShade usou a pasta da DLL " +
                          "como base, então não leu o ini, os shaders nem os addons da raiz."
                        : $"ReShade.log não está na pasta do exe, e sim em {pastaDoLog}.",
                    aoLadoDaDll
                        ? "Instale de novo (Atualizar): o ReShade.ini da raiz é regravado com [INSTALL] BasePath " +
                          "apontando para a raiz, e o ini/log que o ReShade deixou ao lado da DLL são removidos."
                        : "É ali que o processo que renderiza roda. Volte à Detecção, aponte no botão " +
                          "\"Outro...\" o executável dessa pasta e instale de novo.");
                yield break;
            }

            if (EaJavelin.EhJavelin(exeFolder))
            {
                // Sob o EA Javelin o dxgi.dll não carrega e o log nunca nasce. Mandar
                // desligar overlays aqui seria a rodada perdida de sempre.
                yield return new CheckResult(7, "ReShade carregou", CheckStatus.Fail,
                    "ReShade.log não existe — e este jogo abre sob o EA Javelin Anticheat, que não deixa o " +
                    "dxgi.dll do ReShade carregar.", EaJavelin.ComoAbrir);
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
                       : renodx.CaiuAntesDoDlss ? CheckStatus.Fail
                       : renodx.FechouSemJanela ? CheckStatus.Fail
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
                    // "Está aplicando" e "eu vejo diferença" são coisas diferentes, e o
                    // log não decide a segunda. Quando o addon confirma que está vivo e o
                    // olho não acha nada, o que falta é COMO comparar — e o que atrapalha
                    // a comparação está no próprio log.
                    ? "Está funcionando. É DLAA: mesma resolução de render e de saída, então não " +
                      "há ganho de FPS e a diferença é de imagem. No caminho direto (D3D12 com DLSS " +
                      "nativo) a aba Início do ReShade fica VAZIA de propósito — quem trabalha é o " +
                      "addon, na aba Complementos.  |  NÃO ESTÁ VENDO DIFERENÇA? Primeiro olhe o item 18: " +
                      "com o runtime errado para a placa o addon diz ACTIVE e não desenha nada. Depois compare parado, não " +
                      "jogando: numa cena com rosto, cabelo ou vegetação perto, aperte F5 (salva " +
                      "print), F6 (desliga o NR), F5 de novo, e abra os dois prints lado a lado com " +
                      "zoom. Em movimento e a olho nu a diferença passa batida." +
                      (renodx.FrameGeneration
                          ? "  |  A GERAÇÃO DE QUADROS ESTÁ LIGADA neste jogo, e o addon avisa no log " +
                            "que não encosta nela: os quadros gerados saem SEM o Neural Rendering. " +
                            "Desligue a geração de quadros nas opções do jogo enquanto compara — com " +
                            "ela ligada, metade do que você vê não passou pelo DLSS 5."
                          : "") +
                      "  |  Também desligue, só para comparar, o granulado de filme (film grain), o " +
                      "desfoque de movimento e a aberração cromática: eles entram DEPOIS do DLSS 5 e " +
                      "cobrem justamente o detalhe que ele acrescenta." +
                      "  |  No painel do addon (aba Complementos → DLSS 5 Neural Rendering) existem 3 " +
                      "estilos — Default, Natural e Cinematic — e 3 presets. O Natural é o mais " +
                      "discreto dos três: para ENXERGAR a diferença, troque para Cinematic e suba a " +
                      "intensidade; depois volte para o que você preferir."
                    : renodx.AssinaturaRecusada
                        ? "Aplique o override no registro e reinicie o PC quando puder — o driver só lê essa chave na inicialização."
                        : renodx.CaiuAntesDoDlss
                            // RE9: três logs, a mesma assinatura — runtime recriado, 1 a 3 s
                            // de silêncio, tela de erro do jogo, nenhum "feature create".
                            // O gancho de DLSS nem foi chamado; o suspeito é quem roda antes.
                            ? "Não é o gancho de DLSS. Bisseção, com o jogo fechado: 1) \"Testar sem o RenoDX\" e " +
                              "abra o jogo — se ainda cair, 2) \"Isolar a causa\" desliga o ReShade (dxgi.dll). Se aí " +
                              "o jogo roda, é o ReShade dentro deste jogo: desligue TODAS as sobreposições (Steam, Xbox " +
                              "Game Bar, NVIDIA App, Discord, RivaTuner) e teste de novo. Se cair mesmo sem nada, é o " +
                              "jogo nesta máquina (integridade dos arquivos, driver). OBS.: jogo com proteção " +
                              "anti-adulteração (Denuvo/RE Engine, como o RE9) recusa a injeção direta do ReShade — " +
                              "renomear para dinput8.dll não muda isso; o caminho que funciona é carregar por dentro do " +
                              "REFramework ou do Special K, fora do app. Em jogo D3D12 ainda existe uma saída que " +
                              "DISPENSA o ReShade inteiro: o OptiScaler com suporte a DLSSNR " +
                              "(github.com/Dagherbou/OptiScaler_DLSSNR) — sem ReShade não há DLL para a proteção " +
                              "recusar, e o desempenho é maior. Só serve em D3D12."
                        : renodx.FechouSemJanela
                            // MGS V Ground Zeroes: device criado, addons registrados, 27
                            // contextos adiados e saída limpa sem swapchain. Não é travamento
                            // — é o jogo se fechando, que é o que a proteção da Fox Engine faz.
                            ? (MotorFox.EhFoxEngine(exePath)
                                ? MotorFox.Aviso + " " + (MotorFox.PatchAplicado(exePath)
                                    ? "O backup do patch está na pasta, mas o jogo fechou assim mesmo: confira se o patcher " +
                                      "aceitou o exe (ele só cobre o 1.0.15.4 Steam inglês) e se a Steam não restaurou o " +
                                      "executável original por cima (verificação de integridade desfaz o patch)."
                                    : MotorFox.PatcherCobre(exePath)
                                        ? (MotorFox.EstadoDoExe(exePath) == EstadoDoExeFox.Original
                                            ? "O exe é o 1.0.15.4 intacto: instale de novo — a instalação aplica o patch sozinha, antes de tudo."
                                            : MotorFox.ExeNaoCoberto(exePath!))
                                        : MotorFox.SemPatcherParaGz)
                                : "O jogo se fechou sozinho depois de criar o device — não travou. Isso é " +
                                  "proteção anti-adulteração recusando a DLL, ou uma sobreposição brigando " +
                                  "pela API. Com o jogo fechado: 1) \"Testar só o ReShade\" (desliga os dois " +
                                  "addons); se abrir, o problema é addon. 2) Se não abrir, desligue TODAS as " +
                                  "sobreposições (Steam, Xbox Game Bar, NVIDIA App, Discord, RivaTuner) e repita.")
                        : renodx.HooksSemUso
                            ? "O RenoDX só enxerga NGX em D3D12. Fora disso quem trabalha é o Feeder; " +
                              "confirme que o DLSS 5 Feed está marcado no preset."
                        : renodx.PedeStreamlineHooks
                            ? "Na barra abaixo, troque \"Hooks do RenoDX\" para 1 (NGX + Streamline), aplique e abra " +
                              "o jogo de novo. Se com 1 o jogo cair na abertura, volte para 2."
                            : "Abra o jogo, jogue alguns segundos e verifique de novo.");
        }

        // Log gravado numa rodada SEM o RenoDX (teste de isolamento): não diz nada sobre o
        // DLSS 5, mas ainda diz se o jogo caiu — e uma queda sem o addon na jogada é a
        // prova de que ele está fora de suspeita.
        else if (loaded && AddonRenodxNaPasta(exeFolder))
        {
            var seg = RenodxLog.SegundosAteDialogo(text);
            // Duas razões MUITO diferentes para um log sem o RenoDX, e chamar as duas de
            // "teste de isolamento" é chute vendido como fato: ou o addon está renomeado
            // (o teste, mesmo), ou ele está lá, ligado, e o ReShade nem chegou a carregar
            // addon nenhum porque não achou o swapchain — que é o caso do MGS V.
            bool desligadoPeloTeste = AddonRenodxDesligado(exeFolder);
            bool viuSwapchain = text.Contains("CreateSwapChain", StringComparison.OrdinalIgnoreCase)
                                || text.Contains("Recreated runtime environment", StringComparison.OrdinalIgnoreCase);

            var detalhe = desligadoPeloTeste
                ? "O ReShade.log atual foi gravado com o RenoDX DESLIGADO (teste de isolamento): não diz nada sobre o DLSS 5."
                : viuSwapchain
                    ? "O ReShade carregou e viu o swapchain, mas o log não tem nenhuma linha do addon do RenoDX."
                    : "O ReShade carregou mas nunca chegou ao swapchain, então NENHUM addon foi carregado — " +
                      "o RenoDX está na pasta e nem teve chance de rodar. O problema é antes do DLSS 5.";

            if (seg is not null)
                detalhe += $" O jogo abriu a tela de erro {seg.Value.ToString("0.0", CultureInfo.InvariantCulture)} s " +
                           "depois de o runtime do ReShade subir.";

            yield return new CheckResult(14, "DLSS 5 aplicado na imagem",
                desligadoPeloTeste && seg is null ? CheckStatus.Manual : CheckStatus.Fail,
                detalhe,
                desligadoPeloTeste
                    ? "Religue o RenoDX, abra o jogo e verifique de novo."
                    : viuSwapchain
                        ? "Confira se o renodx-dlss5.addon64 está na pasta do exe e se o AddonPath do ini aponta para ela."
                        : "Resolva o item 8 primeiro: sem swapchain não há DLSS 5. Se o jogo NEM ABRE, use " +
                          "\"Isolar a causa\" para saber se é o ReShade ou o jogo nesta máquina.");
        }

        bool sawSwapchain = text.Contains("CreateSwapChain", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("Recreated runtime environment", StringComparison.OrdinalIgnoreCase);
        yield return new CheckResult(8, "ReShade viu o swapchain",
            sawSwapchain ? CheckStatus.Pass : (loaded ? CheckStatus.Fail : CheckStatus.Manual),
            sawSwapchain
                ? "Log tem CreateSwapChain / Recreated runtime environment."
                : "Log sem CreateSwapChain: o ReShade carregou mas não é a factory do renderizador.",
            sawSwapchain
                ? null
                : hospedado
                    ? $"O {ReFramework.ReShadePlugin} está em reframework\\plugins e carregou, mas não viu o swapchain. " +
                      "Overlays podem estar chegando antes: desligue todas e teste de novo."
                    : $"O {nomeDoReShade} tem que estar na pasta do EXE (é o nome escolhido na Detecção) " +
                      "e ser o primeiro a pegar a API. Overlays podem estar chegando antes: desligue todas e teste de " +
                      "novo. Se o jogo NEM ABRE com este arquivo na pasta, o problema não é ordem: é o jogo recusando " +
                      "a DLL — use \"Isolar a causa\" para confirmar.");
    }

    /// <summary>O addon está na pasta mas RENOMEADO pelo teste de isolamento.</summary>
    private static bool AddonRenodxDesligado(string exeFolder)
    {
        foreach (var pasta in new[] { exeFolder, Path.Combine(exeFolder, "host64") })
            if (File.Exists(Path.Combine(pasta, "renodx-dlss5.addon64" + Isolamento.Sufixo)))
                return true;
        return false;
    }

    /// <summary>O addon do RenoDX está na pasta — ligado, ou desligado pelo isolamento.</summary>
    private static bool AddonRenodxNaPasta(string exeFolder)
    {
        foreach (var pasta in new[] { exeFolder, Path.Combine(exeFolder, "host64") })
        {
            if (File.Exists(Path.Combine(pasta, "renodx-dlss5.addon64"))) return true;
            if (File.Exists(Path.Combine(pasta, "renodx-dlss5.addon64" + Isolamento.Sufixo))) return true;
        }
        return false;
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
