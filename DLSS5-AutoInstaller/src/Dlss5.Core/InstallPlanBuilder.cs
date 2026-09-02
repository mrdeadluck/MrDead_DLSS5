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

        var missing = kit.MissingFor(route, profile.UsesRenodxDirectPath, profile.Api);
        if (missing.Count > 0)
        {
            foreach (var m in missing)
                plan.Blockers.Add("Falta no kit: " + m);
        }
        foreach (var p in kit.Problems)
            plan.Blockers.Add(p);

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
            foreach (var outro in Isolamento.NomesDeReShade)
            {
                if (outro.Equals(hook, StringComparison.OrdinalIgnoreCase)) continue;
                var caminho = Path.Combine(exe, outro);
                if (!File.Exists(caminho) || !Propriedade.ContemTexto(caminho, "ReShade")) continue;

                plan.Actions.Add(new PlanAction(PlanActionKind.DeleteForbiddenFile,
                    $"Remover ReShade antigo com o nome {outro} (agora ele entra como {hook})",
                    null, caminho));
            }

            if (dxgiSrc is not null)
            {
                Copy(dxgiSrc, exe, hook);
            }
            else if (kit.ReShadeSetup is not null)
            {
                plan.Actions.Add(new PlanAction(PlanActionKind.ExtractReShadeDll,
                    $"Extrair ReShade ({dxgiArch}) do instalador → {Rel(profile, Path.Combine(exe, hook))}",
                    kit.ReShadeSetup, Path.Combine(exe, hook)));
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
            if (profile.NeedsFeeder)
                Copy(kit.FeedAddon64, exe, "dlss5-feed.addon64");
            Copy(kit.RenodxAddon64, exe, "renodx-dlss5.addon64");
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
            Copy(kit.RenodxAddon64, host64, "renodx-dlss5.addon64");
            Copy(kit.NvngxDlssnr, host64, "nvngx_dlssnr.dll");
            CopySemSobrescreverDoJogo(kit.NvngxDlss, host64, "nvngx_dlss.dll");
        }

        // Rota C: dgVoodoo na pasta do renderizador (exe ou bin\ no Source).
        if (route == InstallRoute.C)
        {
            var renderer = profile.RendererFolder ?? exe;
            var wrapperSrc = profile.Api == GraphicsApi.D3D8 ? kit.DgVoodooD3D8X86 : kit.DgVoodooD3D9X86;

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
            plan.Warnings.Add(
                "OpenGL está FORA da matriz validada da especificação (seção 2), e o addon do Feeder " +
                "anuncia suporte a D3D11/D3D12/Vulkan. O ReShade é instalado com o nome certo " +
                "(opengl32.dll) e deve carregar e abrir o overlay, mas o DLSS 5 pode não engatar. " +
                "Se o jogo tiver opção de renderizador DirectX nas configurações, PREFIRA ela — " +
                "aí o caminho é o validado. Depois de abrir o jogo uma vez, volte e clique em " +
                "Verificar: o log dirá se o addon foi aceito.");
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
            return "Direct3D 10 não é suportado por este fluxo.";
        if (p.Api == GraphicsApi.OpenGL && p.Architecture == PeArchitecture.X86)
            return "Jogo 32-bit em OpenGL não é suportado: o addon32 do Feeder só aceita Direct3D 11. " +
                   "Se o jogo oferecer um renderizador DirectX nas configurações, troque para ele.";
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
