namespace Dlss5.Core;

public sealed class InstallPlan
{
    public required GameProfile Profile { get; init; }
    public required InstallOptions Options { get; init; }
    public List<PlanAction> Actions { get; } = new();
    public List<string> Blockers { get; } = new();

    /// <summary>Coisas que não impedem instalar, mas que o usuário precisa saber.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Arquivos que já existem no destino e NÃO são deste programa: serão substituídos,
    /// com backup. A interface exige que o usuário veja esta lista antes de instalar.
    /// </summary>
    public List<string> Conflitos { get; } = new();

    /// <summary>Arquivos de outros mods/injetores na pasta (só aviso; nunca são tocados).</summary>
    public List<string> OutrosMods { get; } = new();

    /// <summary>Manifesto de uma instalação anterior na mesma pasta, se houver.</summary>
    public InstallManifest? InstalacaoAnterior { get; set; }

    public bool CanRun => Blockers.Count == 0 && Actions.Count > 0;

    /// <summary>Resumo do que será feito, em números, para a confirmação.</summary>
    public string ResumoCurto()
    {
        int copias = Actions.Count(a => a.Kind is PlanActionKind.CopyFile or PlanActionKind.ExtractReShadeDll);
        int gerados = Actions.Count(a => a.Kind is PlanActionKind.WriteGeneratedFile or PlanActionKind.PatchDgVoodooConf);
        int removidos = Actions.Count(a => a.Kind == PlanActionKind.DeleteForbiddenFile);
        bool registro = Actions.Any(a => a.Kind == PlanActionKind.RegistryOverride);
        var partes = new List<string>();
        if (copias > 0) partes.Add($"{copias} cópia(s) de arquivo/pasta");
        if (gerados > 0) partes.Add($"{gerados} arquivo(s) de configuração gerado(s)");
        if (removidos > 0) partes.Add($"{removidos} arquivo(s) movido(s) para backup");
        if (Conflitos.Count > 0) partes.Add($"{Conflitos.Count} arquivo(s) existente(s) substituído(s) com backup");
        if (registro) partes.Add("override de assinatura no registro (HKLM)");
        return string.Join(", ", partes) + ".";
    }
}

/// <summary>
/// Deriva a lista de cópias e arquivos gerados a partir do perfil (spec 6 e 11.4).
/// Não toca em disco — só descreve o que será feito.
/// </summary>
public static class InstallPlanBuilder
{
    public static InstallPlan Build(GameProfile profile, KitInventory kit, InstallOptions options)
    {
        var plan = new InstallPlan { Profile = profile, Options = options };
        var route = profile.Route;

        if (route == InstallRoute.Unsupported)
        {
            plan.Blockers.Add(DescribeUnsupported(profile));
            return plan;
        }

        var missing = kit.MissingFor(route, profile.UsesRenodxDirectPath, profile.Api, profile.UsesShortFuse, profile.MotorEfetivo);
        if (missing.Count > 0)
        {
            foreach (var m in missing)
                plan.Blockers.Add("Falta no kit: " + m);
        }
        foreach (var p in kit.Problems)
            plan.Blockers.Add(p);

        if (profile.NeedsFeeder && kit.ShadersDir is not null && kit.HasAnyMvProvider)
        {
            if (!MvProviders.Disponivel(kit, options.MvProvider))
                plan.Blockers.Add($"Provedor de motion vectors escolhido ({options.MvProvider}) não está no kit: " +
                    $"falta reshade-shaders\\Shaders\\{MvProviders.ArquivoFx(options.MvProvider)}. " +
                    (options.MvProvider == MvProvider.LumeniteKernel
                        ? "O LumeniteFX não pode ser redistribuído: baixe em github.com/umar-afzaal/LumeniteFX e copie a pasta Shaders para o kit."
                        : "Escolha outro provedor na tela de detecção."));
            else if (options.MvProvider == MvProvider.Drme)
                plan.Warnings.Add("DRME (MotionEstimation.fx) não compila no ReShade 6.8 (erro X3020): o efeito aparece " +
                    "ligado mas não escreve nada, e o DLSS roda sem vetores de movimento. Prefira VORT ou Launchpad.");
        }

        // O runtime do kit, pelo hash. É aviso e não bloqueio porque o original serve em
        // RTX 50 — mas é o aviso que teria poupado o RE9 de dias de teste.
        if (kit.NvngxDlssnr is not null)
        {
            var build = RuntimeNr.Identificar(kit.NvngxDlssnr);
            var (falha, texto) = RuntimeNr.Avaliar(build, serieRtx: null);
            if (falha)
                plan.Warnings.Add($"{RuntimeNr.Arquivo} do kit: {texto} {RuntimeNr.ComoTrocar}");
        }

        // O Feeder do kit, pela versão gravada no arquivo (o 0.5.0 não gravava nenhuma). É o
        // que derruba o jogo na troca de configuração; aviso, não bloqueio, porque sem
        // mexer nas configurações com o DLSS 5 ligado ele funciona.
        if (kit.FeedAddon64 is not null && profile.NeedsFeeder)
        {
            var versaoFeeder = FeederKit.VersaoDoArquivo(kit.FeedAddon64);
            if (FeederKit.Antiga(versaoFeeder))
                plan.Warnings.Add(FeederKit.AvisoKitAntigo(versaoFeeder));
        }

        var exe = profile.ExeFolder;
        var host64 = Path.Combine(exe, "host64");
        var shadersTarget = Path.Combine(exe, "reshade-shaders");

        // Instalação anterior nesta pasta: decide o que já é nosso (reinstalar/reparar).
        plan.InstalacaoAnterior = InstallManifest.Find(profile.GameFolder, profile.ExeFolder);

        void Copy(string? src, string dstFolder, string dstName)
        {
            if (src is null) return;
            var dst = Path.Combine(dstFolder, dstName);
            plan.Actions.Add(new PlanAction(PlanActionKind.CopyFile,
                $"Copiar {dstName} → {Rel(profile, dst)}", src, dst));
        }

        // NUNCA por cima do arquivo do jogo. O nvngx_dlss.dll do kit é UMA versão de DLSS;
        // o jogo que já tem DLSS traz A DELE, casada com o resto do Streamline do jogo.
        // Sobrescrever quebra o DLSS do jogo (as opções somem do menu) e faz o NGX recusar
        // a runtime com 0xBAD00007 — foi o estrago recorrente em Forza, GTA 5 e Onimusha.
        // Quando o jogo já tem o arquivo, é o dele que fica.
        void CopySemSobrescreverDoJogo(string? src, string dstFolder, string dstName)
        {
            if (src is null) return;
            var existente = Path.Combine(dstFolder, dstName);
            // Se o que está lá foi gravado por NÓS (manifesto + hash conferem), continua
            // sendo nosso: entra no plano (o motor pula se estiver igual) e segue rastreado
            // para sair na desinstalação.
            if (File.Exists(existente) && plan.InstalacaoAnterior is not null
                && Propriedade.Classificar(existente, plan.InstalacaoAnterior) == OrigemDoArquivo.Nosso)
            {
                Copy(src, dstFolder, dstName);
                return;
            }
            if (File.Exists(existente))
            {
                plan.Warnings.Add($"{dstName} já existe na pasta (é do jogo) — mantido. " +
                    "O kit não sobrescreve o DLSS do próprio jogo: é isso que fazia as opções de " +
                    "DLSS sumirem do menu depois de instalar.");
                return;
            }
            Copy(src, dstFolder, dstName);
        }

        // Limpeza dos proibidos primeiro (spec 3.7 / prólogo).
        if (options.CleanForbidden)
        {
            foreach (var f in ForbiddenFiles.FindPresent(exe))
                plan.Actions.Add(new PlanAction(PlanActionKind.DeleteForbiddenFile,
                    $"Remover arquivo proibido {Path.GetFileName(f)}", null, f));
        }

        // ReShade na pasta do exe, arquitetura = exe. O NOME depende da API: o jogo só
        // carrega a DLL que ele mesmo procura (dxgi.dll no Direct3D, opengl32.dll no OpenGL).
        var dxgiArch = profile.Architecture;
        var dxgiSrc = dxgiArch == PeArchitecture.X64 ? kit.DxgiX64 : kit.DxgiX86;

        // O REFramework entra JUNTO com o ReShade, não no lugar dele. O binário dele traz
        // IntegrityCheckBypass — com patch nomeado para o RE9 — e é isso que desarma a
        // checagem que derruba o jogo quando há uma DLL a mais na pasta. Hospedar o ReShade
        // dentro de reframework\plugins foi invenção minha e não é o caminho que funciona:
        // o ReShade continua sendo a DLL ao lado do executável, como em qualquer outro jogo.
        if (profile.UsarReFramework)
        {
            if (kit.ReFrameworkDinput8 is null)
            {
                plan.Blockers.Add(
                    "Falta no kit: dinput8.dll do REFramework (x64). Baixe a nightly em " +
                    "github.com/praydog/REFramework-nightly/releases e ponha o dinput8.dll em qualquer " +
                    "subpasta do kit (" + kit.KitRoot + ") — por exemplo numa pasta REFramework\\. " +
                    "Sem ele o ReShade não roda em jogo da RE Engine: é o REFramework que desarma a " +
                    "checagem de integridade.");
            }
            else
            {
                Copy(kit.ReFrameworkDinput8, exe, ReFramework.Dinput8);
                if (kit.ReFrameworkRevision is not null)
                    Copy(kit.ReFrameworkRevision, exe, ReFramework.RevisionFile);
            }

            // Aviso, não desvio: a pasta sem re_chunk_*.pak pode ser um jogo da RE Engine
            // que guarda os dados noutro lugar, e quem escolheu a caixa é quem sabe. O que
            // não pode é a instalação mudar por causa do palpite.
            if (!profile.EhReEngine)
                plan.Warnings.Add(
                    "Esta pasta não tem re_chunk_*.pak, então não parece um jogo da RE Engine — e o " +
                    "REFramework só carrega nela. Se o jogo for RE Engine mesmo assim (dados noutra " +
                    "pasta), siga; se não for, o ReShade não vai carregar por este caminho e o certo " +
                    "é desmarcar a caixa e instalar pela injeção direta.");

            plan.Warnings.Add(
                "Modo REFramework: ele entra como dinput8.dll AO LADO do ReShade, não no lugar " +
                "dele. O binário traz um desarme de checagem de integridade (com patch nomeado " +
                "para o RE9), e é isso que deixa a DLL do ReShade conviver com o jogo.");
        }

        {
            var hook = profile.ReShadeHookName;

            // Sobra de instalação anterior com OUTRO nome: ela continua sendo carregada
            // pelo jogo e continua sendo ReShade. No MGS V, instalar como d3d11.dll e
            // deixar o dxgi.dll antigo é não consertar nada — o arquivo que impede o jogo
            // de abrir segue na pasta. Só sai o que o conteúdo prova ser ReShade.
            var pastaDoHook = profile.PastaDoReShade;
            bool hookForaDaRaiz = !string.Equals(pastaDoHook, exe, StringComparison.OrdinalIgnoreCase);
            if (hookForaDaRaiz)
            {
                plan.Warnings.Add($"A DLL do ReShade ({hook}) vai para {Rel(profile, pastaDoHook)}, a pasta do " +
                                  "renderizador — na raiz ela nunca carrega neste jogo. ReShade.ini, addons e " +
                                  "shaders ficam na raiz, ao lado do exe, e o ReShade.ini ganha [INSTALL] " +
                                  "BasePath apontando para a raiz: sem isso o ReShade usaria a pasta da DLL como " +
                                  "base e não acharia efeito nem addon nenhum.");

                // O que o ReShade criou ao lado da DLL numa rodada sem o BasePath (ini vazio,
                // preset, log) sai — com backup, como tudo que o plano remove.
                foreach (var sobra in new[] { "ReShade.ini", "ReShadePreset.ini", "ReShade.log" })
                {
                    var caminho = Path.Combine(pastaDoHook, sobra);
                    if (File.Exists(caminho))
                        plan.Actions.Add(new PlanAction(PlanActionKind.DeleteForbiddenFile,
                            $"Remover {Rel(profile, caminho)} (criado pelo ReShade ao lado da DLL; a base é a raiz)",
                            null, caminho));
                }
            }

            // Sobra de ReShade em qualquer das duas pastas, com qualquer nome que não seja
            // o hook no lugar certo, sai. Foi o Titanfall 2: o dxgi.dll da raiz, inerte,
            // ficava lá enquanto o de bin\x64_retail entrava.
            foreach (var pasta in new[] { pastaDoHook, exe }.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var outro in Isolamento.NomesDeReShade)
            {
                bool ehOHook = outro.Equals(hook, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(pasta, pastaDoHook, StringComparison.OrdinalIgnoreCase);
                if (ehOHook) continue;
                var caminho = Path.Combine(pasta, outro);
                if (!File.Exists(caminho) || !Propriedade.ContemTexto(caminho, "ReShade")) continue;

                plan.Actions.Add(new PlanAction(PlanActionKind.DeleteForbiddenFile,
                    $"Remover ReShade antigo em {Rel(profile, caminho)} (agora ele entra como {Rel(profile, Path.Combine(pastaDoHook, hook))})",
                    null, caminho));
            }

            if (dxgiSrc is not null)
            {
                Copy(dxgiSrc, pastaDoHook, hook);
            }
            else if (kit.ReShadeSetup is not null)
            {
                plan.Actions.Add(new PlanAction(PlanActionKind.ExtractReShadeDll,
                    $"Extrair ReShade ({dxgiArch}) do instalador → {Rel(profile, Path.Combine(pastaDoHook, hook))}",
                    kit.ReShadeSetup, Path.Combine(pastaDoHook, hook)));
            }
        }

        // ReShade.ini + preset gerados.
        plan.Actions.Add(new PlanAction(PlanActionKind.WriteGeneratedFile,
            profile.UsarReFramework
                ? $"Gerar {ReFramework.ReShadeIni} (é o ini que o ReShade hospedado lê)"
                : "Gerar ReShade.ini",
            null, profile.ReShadeIniPath));
        plan.Actions.Add(new PlanAction(PlanActionKind.WriteGeneratedFile,
            profile.NeedsFeeder
                ? $"Gerar ReShadePreset.ini (MV = {options.MvProvider}, acima do DLSS 5 Feed)"
                : profile.UsesShortFuse
                    ? $"Gerar ReShadePreset.ini (sem efeitos: o RenoDX DLSS do ShortFuse trabalha sozinho, {profile.PassCount} passada(s))"
                    : "Gerar ReShadePreset.ini (sem efeitos: com DLSS nativo em D3D12 o RenoDX se pendura na chamada do próprio jogo)",
            null, Path.Combine(exe, "ReShadePreset.ini")));

        // Pasta de shaders: vai nos dois caminhos.
        //
        // Ela já ficou de fora do caminho direto, na suspeita de que compilar os .fx
        // derrubava o RE9. A suspeita caiu: o RE9 caía pela proteção anti-adulteração do
        // próprio jogo (com o REFramework hospedando o ReShade, ele abre). E sem a pasta
        // o ReShade abre reclamando na aba Início — "nenhum arquivo de efeito (.fx)
        // encontrado nos caminhos de pesquisa" — o que parece defeito e não é. Com a
        // pasta no lugar e EffectLoadSkipping=1 (preset vazio no direto), os arquivos
        // existem e mesmo assim nenhum é compilado.
        if (kit.ShadersDir is not null)
            plan.Actions.Add(new PlanAction(PlanActionKind.CopyFile,
                $"Copiar pasta reshade-shaders → {Rel(profile, shadersTarget)}",
                kit.ShadersDir, shadersTarget));

        if (route == InstallRoute.A)
        {
            // 64-bit: tudo na pasta do exe.
            //
            // Dois motores que não convivem: o renodx-dlss do ShortFuse fabrica a chamada de
            // DLSS sozinho e faz 1 a 10 passadas; o renodx-dlss5 do Krish (com o Feeder quando
            // o jogo não tem DLSS em D3D12) faz uma. Ao trocar de motor o addon do outro sai
            // da pasta, com backup — os dois carregados juntos disputam o mesmo NGX.
            void RemoverRival(string nome, string porque)
            {
                var caminho = Path.Combine(exe, nome);
                if (!File.Exists(caminho)) return;
                plan.Actions.Add(new PlanAction(PlanActionKind.DeleteForbiddenFile,
                    $"Remover {Rel(profile, caminho)} ({porque}; vai para backup)", null, caminho));
            }

            if (profile.UsesShortFuse)
            {
                Copy(kit.RenodxDlssShortFuse, exe, ShortFuseDlss.Addon);
                RemoverRival("renodx-dlss5.addon64", "o RenoDX DLSS do ShortFuse não convive com o addon do Krish");
                RemoverRival("dlss5-feed.addon64", "o RenoDX DLSS do ShortFuse não convive com o Feeder");
                plan.Warnings.Add(ShortFuseDlss.AvisoDoPlano(profile.PassCount));
            }
            else
            {
                if (profile.NeedsFeeder)
                    Copy(kit.FeedAddon64, exe, "dlss5-feed.addon64");
                Copy(kit.RenodxAddon64, exe, "renodx-dlss5.addon64");
                RemoverRival(ShortFuseDlss.Addon, "o addon do Krish e o Feeder não convivem com o RenoDX DLSS do ShortFuse");
            }
            Copy(kit.NvngxDlssnr, exe, "nvngx_dlssnr.dll");
            if (profile.HasNativeDlss)
            {
                // Jogo com DLSS próprio: o único nvngx_dlss.dll que funciona aqui é o DELE,
                // casado com o resto dos arquivos do jogo. A versão do kit nessa pasta trava
                // o jogo na abertura (o motor carrega a DLL na inicialização e não aceita
                // outra). Nem quando o arquivo falta o kit põe o dele: faltar significa que
                // uma desinstalação antiga apagou o do jogo, e o conserto é a verificação de
                // integridade da Steam, não um transplante. O Feeder usa o do jogo.
                var dllNaPasta = Path.Combine(exe, "nvngx_dlss.dll");
                if (!File.Exists(dllNaPasta))
                    plan.Warnings.Add(
                        "O jogo tem DLSS próprio mas o nvngx_dlss.dll dele NÃO está na pasta — " +
                        "uma desinstalação antiga apagou. O kit NÃO põe o dele no lugar: a versão " +
                        "do kit não casa com o jogo e faz o jogo travar na abertura. Antes de jogar: " +
                        "Steam → clique direito no jogo → Propriedades → Arquivos instalados → " +
                        "Verificar integridade dos arquivos do jogo.");
                else if (TransplanteDlss.EhDoKit(dllNaPasta, kit.NvngxDlss))
                    // Pior que faltar: o arquivo presente é o DO KIT, transplantado por
                    // instalação antiga. Instalar por cima não conserta nada — motor que
                    // carrega o DLL na inicialização continua travando antes da janela.
                    plan.Warnings.Add(
                        "O nvngx_dlss.dll desta pasta é o DO KIT (byte a byte igual): uma instalação " +
                        "antiga o pôs no lugar do DLL do jogo, e é isso que quebra o menu de DLSS e " +
                        "faz motor que carrega a DLL na inicialização travar antes de abrir a janela. " +
                        "Antes de jogar: use Desinstalar (ou Remover vestígios) — eles removem este " +
                        "arquivo — e depois Steam → Verificar integridade para repor o original do jogo.");
            }
            else
            {
                CopySemSobrescreverDoJogo(kit.NvngxDlss, exe, "nvngx_dlss.dll");
            }
        }
        else
        {
            // 32-bit (B/C): addon32 na raiz; o resto do Feeder dentro de host64\.
            Copy(kit.FeedAddon32, exe, "dlss5-feed.addon32");
            Copy(kit.FeedHost64Exe, host64, "dlss5-feed-host64.exe");
            Copy(kit.DxgiX64, host64, "dxgi.dll");
            Copy(kit.NvngxDlssnr, host64, "nvngx_dlssnr.dll");
            CopySemSobrescreverDoJogo(kit.NvngxDlss, host64, "nvngx_dlss.dll");

            // O consumidor neural do host64\: exatamente um. Os outros saem, com backup — dois
            // consumidores no mesmo processo disputam o NGX (o OptiScaler captura toda carga de
            // nvngx; o Chicken fica inerte se acha o RenoDX; o Krish e o OptiScaler dobram a passada).
            void RemoverDoHost(string nome, string porque, string? prova = null)
            {
                var caminho = Path.Combine(host64, nome);
                if (!File.Exists(caminho)) return;
                if (prova is not null && !Propriedade.ContemTexto(caminho, prova)) return;
                plan.Actions.Add(new PlanAction(PlanActionKind.DeleteForbiddenFile,
                    $"Remover {Rel(profile, caminho)} ({porque}; vai para backup)", null, caminho));
            }
            void RemoverOptiScalerDoHost(string porque)
            {
                foreach (var proxy in new[] { OptiScalerNr.Proxy, "version.dll", "dbghelp.dll", "winhttp.dll", "wininet.dll", OptiScalerNr.Dll })
                    RemoverDoHost(proxy, porque, OptiScalerNr.Marca);
                RemoverDoHost(OptiScalerNr.Ini, porque);
                RemoverDoHost(OptiScalerNr.Shim, porque);
                RemoverDoHost(OptiScalerNr.ReShade64, porque, "ReShade");
            }
            void RemoverShortFuseDoHost(string porque) => RemoverDoHost(ShortFuseDlss.Addon, porque);
            void RemoverChickenDoHost(string porque)
            {
                foreach (var f in new[] { DeepFriedChicken.Addon, DeepFriedChicken.Nvngx, DeepFriedChicken.Cfg })
                    RemoverDoHost(f, porque);
            }
            void RemoverKrishDoHost(string porque)
            {
                try
                {
                    if (Directory.Exists(host64))
                        foreach (var f in Directory.EnumerateFiles(host64, "renodx-dlss5*.addon64"))
                            RemoverDoHost(Path.GetFileName(f), porque);
                }
                catch { }
            }

            if (profile.UsesOptiScalerNr)
            {
                // OptiScaler DLSS-NR entra como winmm.dll (o host importa winmm.dll e version.dll ao
                // iniciar; com outro nome ele não está no processo quando a primeira chamada de NGX
                // acontece). O ini vem do kit com [DlssNr] ligada e as passadas; o encaminhador
                // nvngx.dll_dlssnr.dll é o que o modelo exige do chamador; o Agility SDK vai junto
                // porque o host é D3D12.
                if (profile.PassCount > 1 && !OptiScalerNr.SuportaPassadas(LerTexto(kit.OptiScalerNrIni)))
                {
                    plan.Blockers.Add(
                        $"O OptiScaler do kit ({kit.OptiScalerNrIni}) não tem a chave Passes em [DlssNr]: é um build de UMA " +
                        "passada (o fork v0.2.0-patch1 do GitHub), e a passada extra pedida não aconteceria — o painel do " +
                        "Feeder até mostraria \"Passes=2\", lendo o ini, mas o modelo rodaria uma vez. Use o kit com o " +
                        "OptiScaler v10.0.0-pre1 (pasta \"OptiScaler-DLSSNR-v10.0.0-pre1 ...\", do 7z do Discord) ou peça 1 passada.");
                    return plan;
                }
                Copy(kit.OptiScalerNrDll, host64, OptiScalerNr.Proxy);
                Copy(kit.OptiScalerNrShim, host64, OptiScalerNr.Shim);
                // O OptiScaler toma o dxgi.dll do System32 antes do host carregar o seu; o ReShade
                // do host entra por ele (LoadReshade=true no ini gerado) com este nome.
                Copy(kit.DxgiX64, host64, OptiScalerNr.ReShade64);
                if (kit.OptiScalerNrAgility is not null)
                    Copy(kit.OptiScalerNrAgility, Path.Combine(host64, OptiScalerNr.AgilityRel), OptiScalerNr.AgilityDll);
                plan.Actions.Add(new PlanAction(PlanActionKind.WriteGeneratedFile,
                    $"Gerar host64\\{OptiScalerNr.Ini} ([DlssNr] Enabled=true, Passes={profile.PassCount}, Dx12Upscaler=dlss, spoof desligado, LoadReshade=true)",
                    kit.OptiScalerNrIni, Path.Combine(host64, OptiScalerNr.Ini)));
                RemoverKrishDoHost("o consumidor escolhido é o OptiScaler DLSS-NR");
                RemoverChickenDoHost("o consumidor escolhido é o OptiScaler DLSS-NR");
                RemoverShortFuseDoHost("o consumidor escolhido é o OptiScaler DLSS-NR");
                plan.Warnings.Add(
                    $"Motor OptiScaler DLSS-NR no host64 ({profile.PassCount} passada(s)): o OptiScaler toma a chamada de DLSS que o " +
                    "Feeder faz, faz o upscaling (DLSS) e roda o Neural Rendering N vezes. É o suporte novo do Feeder 0.15 — " +
                    "checado pelo projeto dele, não por este. O menu do OptiScaler abre com Insert na janela do host. " +
                    "O kit traz o OptiScaler v10.0.0-pre1 (04/09/2026), o build que tem a chave Passes; o modelo original pede RTX 50 e com o nvngx_dlssnr.dll SF-v2 do kit roda em RTX 20/30/40. " +
                    "Se travar, volte a 1 passada antes de trocar de motor. No Silent Hill 2 EE (09/09/2026) o log mostrou as passadas construídas, " +
                    "mas na tela não houve diferença de x1 para x4 — quem entregou o x2+ visível foi o RenoDX DLSS (ShortFuse) dentro do host64.");
            }
            else if (profile.UsesDeepFriedChicken)
            {
                Copy(kit.DfcAddon64, host64, DeepFriedChicken.Addon);
                Copy(kit.DfcNvngx, host64, DeepFriedChicken.Nvngx);
                plan.Actions.Add(new PlanAction(PlanActionKind.WriteGeneratedFile,
                    $"Gerar host64\\{DeepFriedChicken.Cfg} (layers={profile.PassCount}, enabled=1, arm=1)",
                    kit.DfcCfg, Path.Combine(host64, DeepFriedChicken.Cfg)));
                RemoverKrishDoHost("o consumidor escolhido é o Deep Fried Chicken (ele fica inerte se acha o RenoDX)");
                RemoverOptiScalerDoHost("o consumidor escolhido é o Deep Fried Chicken");
                RemoverShortFuseDoHost("o consumidor escolhido é o Deep Fried Chicken");
                plan.Warnings.Add(
                    $"Motor Deep Fried Chicken no host64 ({profile.PassCount} passada(s)): " + DeepFriedChicken.PassoManual(profile.PassCount));
            }
            else if (profile.UsesShortFuseNoHost64)
            {
                // EXPERIMENTAL: o addon do ShortFuse dentro do host64. Ver ShortFuseNoHost64.
                Copy(kit.RenodxDlssShortFuse, host64, ShortFuseDlss.Addon);
                var iniHost = Path.Combine(host64, ShortFuseNoHost64.Ini);
                plan.Actions.Add(new PlanAction(PlanActionKind.WriteGeneratedFile,
                    $"Gerar host64\\ReShade.ini ([ADDON] LoadFromDllMain={ShortFuseDlss.Addon}, [{ShortFuseDlss.Secao}] {ShortFuseDlss.ChavePassadas}={profile.PassCount}; o resto do ini fica)",
                    null, iniHost));
                RemoverKrishDoHost("o consumidor escolhido é o RenoDX DLSS do ShortFuse (dois addons de NR no host dobrariam a passada)");
                RemoverOptiScalerDoHost("o consumidor escolhido é o RenoDX DLSS do ShortFuse");
                RemoverChickenDoHost("o consumidor escolhido é o RenoDX DLSS do ShortFuse");
                plan.Warnings.Add(
                    $"Motor RenoDX DLSS (ShortFuse) DENTRO do host64 ({profile.PassCount} passada(s)): validado no Silent Hill 2 EE " +
                    "(09/09/2026) — foi o motor em que o x2+ apareceu na tela. O addon intercepta a chamada de DLSS que o host faz; o Feeder não o reconhece como consumidor " +
                    "(o host loga \"renodx-dlss5*.addon64 not found\" e segue servindo DLAA). A prova de que rodou é o " +
                    "host64\\ReShade.log (item 25 da verificação). Se o host cair, teste menos passadas.");
            }
            else
            {
                Copy(kit.RenodxAddon64, host64, "renodx-dlss5.addon64");
                RemoverOptiScalerDoHost("o consumidor escolhido é o addon do Krish");
                RemoverChickenDoHost("o consumidor escolhido é o addon do Krish");
                RemoverShortFuseDoHost("o consumidor escolhido é o addon do Krish");
            }
        }

        // d3dcompiler_47.dll do jogo velho demais para cs_5_1 (Spider-Man Remastered traz o
        // do SDK do Windows 8.1): o addon compila o shader do NR com ele e falha em silêncio.
        // Vai a cópia do Windows por cima, com backup do original (volta na desinstalação).
        if (route == InstallRoute.A)
        {
            var compilador = Path.Combine(exe, CompiladorD3D.Arquivo);
            if (CompiladorD3D.Antigo(compilador))
            {
                var doSistema = CompiladorD3D.DoSistema();
                if (doSistema is not null)
                {
                    plan.Actions.Add(new PlanAction(PlanActionKind.CopyFile,
                        $"Trocar o {CompiladorD3D.Arquivo} do jogo ({CompiladorD3D.Descrever(compilador)}) pelo do Windows " +
                        $"({CompiladorD3D.Descrever(doSistema)}) — o original vai para backup",
                        doSistema, compilador));
                    plan.Warnings.Add(CompiladorD3D.PorQueTrocar(compilador) +
                        " O plano troca pela cópia do Windows; o original volta na desinstalação.");
                }
                else
                {
                    plan.Warnings.Add(CompiladorD3D.PorQueTrocar(compilador) +
                        " Não achei a cópia do Windows em System32 para pôr no lugar.");
                }
            }
        }

        // Rota C: dgVoodoo na pasta do renderizador (exe ou bin\ no Source).
        if (route == InstallRoute.C)
        {
            var renderer = profile.RendererFolder ?? exe;

            // DirectX 8 com o D3D8.dll ocupado por uma mod que converte para DirectX 9 e
            // prefere um d3d9.dll local (Silent Hill 2 Enhanced Edition): a mod fica, e o
            // dgVoodoo entra como D3D9.dll ao lado dela. Ver D3d8to9Wrapper.
            var marcaD3d8to9 = profile.AtualizarD3d8ViaD3D9();
            if (marcaD3d8to9 is not null)
            {
                plan.Warnings.Add(
                    $"O D3D8.dll desta pasta é {D3d8to9Wrapper.Descrever(marcaD3d8to9)}. Ele FICA: converte o " +
                    "jogo para DirectX 9 (d3d8to9) e carrega de preferência um d3d9.dll da própria pasta, " +
                    "então o dgVoodoo entra como D3D9.dll ao lado dele — a mod continua inteira e o dgVoodoo " +
                    "traduz o DirectX 9 dela para D3D11, onde o ReShade e o Feeder entram. Se a mod estiver " +
                    "com d3d8to9 = 0 no d3d8.ini, volte para 1 (é o padrão): sem isso o D3D9.dll não é usado.");
            }
            var wrapperSrc = profile.DgVoodooWrapperName.Equals("D3D8.dll", StringComparison.OrdinalIgnoreCase)
                ? kit.DgVoodooD3D8X86 : kit.DgVoodooD3D9X86;

            // O dgVoodoo só funciona com ESTE nome de arquivo — e ele pode já estar ocupado
            // por outro wrapper que o usuário pôs ali de propósito. Foi o Dead Space 2: o
            // jogo não abria em CPU com mais de 10 núcleos, o DxWrapper (d3d9.dll +
            // dxwrapper.dll) resolvia, e a instalação copiou o dgVoodoo por cima em
            // silêncio — o conserto sumiu e o jogo voltou a não abrir. Com o DxWrapper os
            // dois convivem encadeados (ver DxWrapperChain); com um wrapper desconhecido,
            // sobrescrever é apostar com o jogo do usuário, e o plano recusa.
            var wrapper = profile.DgVoodooWrapperName;
            switch (OcupanteDe(renderer, wrapper))
            {
                case Ocupante.Outro:
                    plan.Blockers.Add(
                        $"Já existe um {wrapper} nesta pasta que não é o dgVoodoo — outro wrapper, ou um " +
                        "arquivo que veio com o jogo. O dgVoodoo precisa exatamente desse nome, e instalar " +
                        "por cima substitui o que o jogo está usando hoje. Descubra de onde ele veio antes: " +
                        "se for um conserto que você pôs ali, os dois não convivem; se for sobra de outra " +
                        "ferramenta, remova-o e instale de novo.");
                    return plan;

                case Ocupante.DxWrapper:
                    var encadeado = Path.Combine(renderer, profile.DgVoodooChainedName);
                    var ini = Path.Combine(renderer, DxWrapperChain.IniPara(wrapper));
                    Copy(wrapperSrc, renderer, profile.DgVoodooChainedName);
                    plan.Actions.Add(new PlanAction(PlanActionKind.WriteGeneratedFile,
                        $"Encadear DxWrapper → dgVoodoo: RealDllPath em {Rel(profile, ini)}",
                        encadeado, ini));
                    plan.Warnings.Add(
                        $"O {wrapper} desta pasta é o DxWrapper (no Dead Space 2, é o conserto que faz o jogo " +
                        "abrir em CPU com mais de 10 núcleos). Ele FICA. O dgVoodoo entra ao lado como " +
                        $"{profile.DgVoodooChainedName}, e o {DxWrapperChain.IniPara(wrapper)} (o ini que o stub do " +
                        "DxWrapper lê) ganha um RealDllPath apontando para " +
                        "ele: o DxWrapper carrega o dgVoodoo em vez do d3d9 do Windows. A marca d'água do " +
                        "dgVoodoo na tela continua sendo a prova de que a corrente fechou.");
                    break;

                default:
                    Copy(wrapperSrc, renderer, wrapper);
                    break;
            }
            Copy(kit.DgVoodooCpl, renderer, "dgVoodooCpl.exe");
            if (kit.DgVoodooConf is not null)
                plan.Actions.Add(new PlanAction(PlanActionKind.PatchDgVoodooConf,
                    $"Copiar e ajustar dgVoodoo.conf → {Rel(profile, Path.Combine(renderer, "dgVoodoo.conf"))}",
                    kit.DgVoodooConf, Path.Combine(renderer, "dgVoodoo.conf")));
        }

        // As sl.*.dll e a nvngx_dlssg.dll são do jogo, não do kit. O programa não encosta
        // nelas — só avisa, porque elas explicam de onde vem o DLSS nativo.
        var doJogo = ForbiddenFiles.FindGameOwned(exe);
        if (doJogo.Count > 0)
        {
            plan.Warnings.Add(
                "A pasta tem arquivos de DLSS do próprio jogo (" + string.Join(", ", doJogo) + "). " +
                "Eles são mantidos: é por eles que o jogo chega ao DLSS, e removê-los faria as opções " +
                "de DLSS sumirem do menu.");
        }

        if (profile.HasNativeDlss && !profile.UsesRenodxDirectPath)
        {
            plan.Warnings.Add(
                "Caminho do Feeder num jogo com DLSS próprio: o kit NÃO mexe no DLSS do jogo, mas o " +
                "Feeder roda um NGX dele dentro do processo, e com o DLSS do jogo LIGADO os dois colidem " +
                "— o jogo trava depois da tela inicial (Onimusha) ou ao aplicar o DLSS no menu (GTA 5). " +
                "Antes de abrir: opções gráficas do jogo → DLSS desligado. O Neural Rendering entra pelo " +
                "Feed. Se as opções de DLSS sumirem do menu, é sinal de instalação ANTERIOR que trocou o " +
                "nvngx_dlss.dll do jogo: Desinstalar e verificação de integridade da Steam.");
        }

        if (profile.UsesRenodxDirectPath)
        {
            plan.Warnings.Add(
                "Caminho direto (D3D12 + DLSS nativo, o padrão neste caso): o RenoDX processa a chamada " +
                "de DLSS que o próprio jogo faz, então o DLSS do jogo fica LIGADO no menu, no modo que " +
                "você quiser. Sem o Feeder não há segundo NGX no processo — foi o que fez o Onimusha abrir " +
                "e interceptar. Confira o resultado alternando com F6 dentro do jogo; a aba Complementos " +
                "do ReShade mostra \"ACTIVE - NR INJECTED\" quando está aplicando.");
        }


        if (profile.Api == GraphicsApi.OpenGL)
        {
            plan.Warnings.Add(profile.Architecture == PeArchitecture.X86
                ? "OpenGL 32-bit: o projeto do Feeder validou este caminho (Worms Ultimate Mayhem e KOTOR) — o addon32 " +
                  "manda o quadro para o mesmo host64 dos jogos D3D11, e o ReShade entra como opengl32.dll. Ninguém " +
                  "deste projeto rodou um jogo GL ainda. Provedor de vetores: só o LumeniteFX Kernel foi confirmado " +
                  "compilando no OpenGL (VORT e Launchpad não foram testados lá); se o item 13 reclamar, troque o provedor."
                : "OpenGL 64-bit: o Feeder faz o caminho em processo (relatado funcionando no MX Bikes pelo projeto dele; " +
                  "ninguém deste projeto rodou). O ReShade entra como opengl32.dll. O addon do Krish precisa armar " +
                  "num processo onde o ReShade é opengl32.dll — o autor do Feeder registra isso como não medido. " +
                  "Se o jogo tiver opção de renderizador DirectX, PREFIRA ela. Depois de abrir o jogo uma vez, " +
                  "clique em Verificar: o log dirá se o addon foi aceito.");
        }
        if (profile.Api == GraphicsApi.D3D10)
        {
            plan.Warnings.Add(
                "Direct3D 10 (32-bit): o Feeder 0.13.1+ aceita nativo, sem dgVoodoo. O único provedor de vetores que " +
                "compila em shader model 4 é o LumeniteFX Kernel (não vem no kit — licença proíbe redistribuir; ver " +
                "VERSOES.md). Com VORT ou Launchpad o DLSS roda sem vetores (nítido parado, borrado em movimento).");
        }

        if (profile.Api == GraphicsApi.D3D8)
        {
            plan.Warnings.Add(
                "DirectX 8: quem traduz é o dgVoodoo2 (D3D8.dll → D3D11), exatamente como no D3D9. " +
                "O dgVoodoo.conf é gravado no perfil Legado — VRAM em 256 MB, AdapterIDType=nvidia e " +
                "MSD3DDeviceNames=true — porque jogo dessa época inspeciona o adaptador antes de criar " +
                "o device e recusa o cartão virtual do dgVoodoo (o Max Payne responde \"requires a " +
                "DirectX 8 compatible display adapter\"). Se mesmo assim aparecer essa mensagem, use o " +
                "botão \"Painel do dgVoodoo\" na tela de verificação e troque VideoCard para " +
                "geforce_ti_4800 ou ati_radeon_8500 — não precisa reinstalar.");
        }

        if (EaJavelin.EhJavelin(exe))
            plan.Warnings.Add(EaJavelin.Aviso + " " + EaJavelin.ComoAbrir);

        // Easy Anti-Cheat: os arquivos são os mesmos, mas o jogo só os carrega com o EAC
        // fora. Aviso, não bloqueio — e o programa não toca em arquivo de anticheat.
        if (EasyAntiCheat.Encontrar(profile.GameFolder, exe) is { } eac)
            plan.Warnings.Add(EasyAntiCheat.Nota(eac, profile.GameFolder));

        if (MotorFox.EhFoxEngine(profile.RealExePath) && !MotorFox.PatchAplicado(profile.RealExePath))
        {
            var exeFox = profile.RealExePath!;
            if (!MotorFox.PatcherCobre(exeFox))
            {
                plan.Blockers.Add(MotorFox.Aviso + " " + MotorFox.SemPatcherParaGz);
            }
            else if (MotorFox.EstadoDoExe(exeFox) == EstadoDoExeFox.Original)
            {
                // O patch entra ANTES de tudo: se falhar, a instalação para sem deixar nada
                // na pasta (a reversão desfaz o que veio antes — e antes não veio nada).
                plan.Actions.Insert(0, new PlanAction(PlanActionKind.PatchMgsvExe,
                    "Aplicar o patch anti-hook no mgsvtpp.exe (desvia o CheckModuleHook; backup mgsvtpp.exe" +
                    MotorFox.SufixoDoBackup + ")", null, exeFox));
                plan.Warnings.Add(MotorFox.Aviso + " " + MotorFox.PatchAutomatico);
            }
            else
            {
                // Exe de outra versão/idioma: bloqueio com tamanho e hash, para a resposta
                // não depender de mais uma rodada de "não abriu".
                plan.Blockers.Add(MotorFox.Aviso + " " + MotorFox.ExeNaoCoberto(exeFox));
            }
        }

        if (profile.Api == GraphicsApi.Vulkan && profile.Architecture == PeArchitecture.X64)
        {
            plan.Warnings.Add(
                "Vulkan: o ReShade não entra como dxgi.dll — ele é um layer global, instalado pelo " +
                "instalador oficial do ReShade (é preciso marcar o jogo lá). O dxgi.dll copiado aqui " +
                "fica sem uso; os addons, os shaders e o ReShade.ini com AddonPath continuam corretos.");
        }

        // Override de assinatura no registro.
        if (options.ApplyRegistryOverride)
            plan.Actions.Add(new PlanAction(PlanActionKind.RegistryOverride,
                "Aplicar override de assinatura NGX no registro (HKLM, 3 chaves)", null, null));

        DetectarConflitos(plan);
        return plan;
    }

    /// <summary>
    /// Arquivos preexistentes que não são nossos viram conflito explícito (serão
    /// substituídos com backup, e o usuário precisa saber). Outros injetores viram aviso.
    /// </summary>
    private static void DetectarConflitos(InstallPlan plan)
    {
        var profile = plan.Profile;
        var anterior = plan.InstalacaoAnterior;

        foreach (var alvo in InstallerEngine.AlvosDoPlano(plan).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(alvo)) continue;
            var origem = Propriedade.Classificar(alvo, anterior, paraInstalar: true);
            if (origem != OrigemDoArquivo.DoJogoOuTerceiro) continue;
            // Dentro de reshade-shaders\ e host64\ pode haver arquivo do usuário com o
            // mesmo nome de um do kit: também é conflito, mas com backup igual.
            var backup = alvo + Propriedade.BackupSuffix;
            plan.Conflitos.Add(File.Exists(backup) || (anterior?.BackedUpFiles.ContainsKey(alvo) ?? false)
                ? $"{Rel(profile, alvo)} — já existe, foi alterado depois da instalação anterior; o backup ORIGINAL já guardado será preservado"
                : $"{Rel(profile, alvo)} — já existe e não é deste programa; será substituído e o original guardado em {Path.GetFileName(backup)}");
        }

        foreach (var (nome, desc) in Propriedade.OutrosInjetores)
        {
            var p = Path.Combine(profile.ExeFolder, nome);
            if (File.Exists(p)) plan.OutrosMods.Add($"{nome} — {desc}");
        }
        if (plan.OutrosMods.Count > 0)
            plan.Warnings.Add("Outros mods ou injetores na pasta: " + string.Join("; ", plan.OutrosMods) +
                              ". Eles não são tocados, mas podem disputar o mesmo gancho gráfico com o ReShade. " +
                              "Se o DLSS 5 não aparecer, desative-os para testar.");
    }

    private const long OrcamentoWrapper = 32L * 1024 * 1024;

    /// <summary>Quem está com o nome que o dgVoodoo precisa.</summary>
    private enum Ocupante { Ninguem, DxWrapper, Outro }

    private static string? LerTexto(string? caminho)
    {
        try { return caminho is not null && File.Exists(caminho) ? File.ReadAllText(caminho) : null; }
        catch { return null; }
    }

    /// <summary>
    /// Um dgVoodoo já instalado (nosso ou não) conta como ninguém: é o mesmo programa,
    /// pode ser sobrescrito como sempre, e há backup.
    /// </summary>
    private static Ocupante OcupanteDe(string renderer, string wrapper)
    {
        var existente = Path.Combine(renderer, wrapper);
        if (!File.Exists(existente)) return Ocupante.Ninguem;

        // A varredura já cobre ASCII/UTF-16 e minúsculas: "DxWrapper" acha "dxwrapper.dll".
        var marcas = ApiDetector.ScanForMarkers(existente, new[] { "dgVoodoo", "DxWrapper" }, OrcamentoWrapper);
        if (marcas.Contains("dgVoodoo")) return Ocupante.Ninguem;

        return marcas.Contains("DxWrapper") || DxWrapperChain.DxWrapperPresente(renderer)
            ? Ocupante.DxWrapper
            : Ocupante.Outro;
    }

    private static string DescribeUnsupported(GameProfile p)
    {
        if (p.Architecture == PeArchitecture.X86 && p.Api == GraphicsApi.Vulkan)
            return "Jogo 32-bit em Vulkan não é suportado (o addon32 exige Direct3D 11). " +
                   "Se o jogo também oferecer D3D9, troque a API para D3D9 (rota C).";
        if (p.Api == GraphicsApi.D3D10)
            return "Direct3D 10 em executável 64-bit não tem caminho no Feeder (em 32-bit tem, pelo host64). " +
                   "Se o jogo oferecer D3D11 ou D3D9 nas configurações, troque para ele.";
        if (p.Api == GraphicsApi.D3D8 && p.Architecture == PeArchitecture.X64)
            return "DirectX 8 em executável 64-bit não existe na prática, e o dgVoodoo2 só traz o " +
                   "wrapper x86. Confira a arquitetura e a API detectadas.";
        if (p.Architecture == PeArchitecture.Unknown)
            return "Arquitetura do executável não identificada. Selecione o exe real do jogo.";
        return "Combinação de arquitetura/API sem caminho suportado.";
    }

    private static string Rel(GameProfile p, string full)
    {
        try { return Path.GetRelativePath(p.GameFolder, full); }
        catch { return full; }
    }
}
