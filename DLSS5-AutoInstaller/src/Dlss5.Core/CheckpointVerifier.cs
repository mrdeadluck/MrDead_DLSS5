namespace Dlss5.Core;

/// <summary>
/// Checkpoints de verificação da spec 9. Os itens 1–8 saem de arquivos e registro;
/// 9–16 exigem o jogo aberto (viram Manual até os logs aparecerem).
/// </summary>
public static class CheckpointVerifier
{
    /// <summary>ReShade.log menor que isso = placeholder "não fui carregado" (spec 8.3).</summary>
    public const long ReShadeLogPlaceholderSize = 982;

    public static IReadOnlyList<CheckResult> Verify(GameProfile profile, InstallManifest? manifest)
    {
        var r = new List<CheckResult>();
        var exe = profile.ExeFolder;
        var route = profile.Route;

        // 1 — override no registro
        var status = SignatureOverride.Query();
        r.Add(new CheckResult(1, "Override de assinatura NGX no registro",
            status.AllSet ? CheckState.Pass : CheckState.Fail,
            status.AllSet
                ? "As 3 chaves estão com DWORD 1."
                : "Faltando em: " + string.Join(", ",
                    status.Entries.Where(e => !e.Set).Select(e => "HKLM\\" + e.Path)),
            status.AllSet ? null : "Rode o programa como administrador e aplique o override."));

        // 2 — reiniciou depois do override
        if (manifest?.RegistryOverrideAppliedUtc is { } appliedUtc)
        {
            bool rebooted = SignatureOverride.RebootedSinceEnable(appliedUtc);
            r.Add(new CheckResult(2, "Reinício após aplicar o override",
                rebooted ? CheckState.Pass : CheckState.Fail,
                rebooted
                    ? "O PC foi reiniciado depois de aplicar o override."
                    : "O override foi aplicado nesta sessão e o PC ainda não foi reiniciado.",
                rebooted ? null : "Reinicie o Windows — o driver só lê essa chave na inicialização."));
        }
        else
        {
            r.Add(new CheckResult(2, "Reinício após aplicar o override",
                status.AllSet ? CheckState.Warning : CheckState.Manual,
                status.AllSet
                    ? "Override já estava aplicado antes desta instalação; se nunca reiniciou depois, reinicie."
                    : "Sem override aplicado por este programa.",
                "Na dúvida, reinicie o PC uma vez após aplicar o override."));
        }

        // 4 — exe real e arquitetura
        r.Add(profile.RealExePath is not null && profile.Architecture != PeArchitecture.Unknown
            ? new CheckResult(4, "Executável real identificado", CheckState.Pass,
                $"{Path.GetFileName(profile.RealExePath)} ({profile.Architecture}, {profile.Api}) → rota {route}")
            : new CheckResult(4, "Executável real identificado", CheckState.Fail,
                "Não identificado.", "Selecione manualmente o executável real do jogo."));

        // 6 — arquitetura dos dxgi.dll instalados
        var dxgi = Path.Combine(exe, "dxgi.dll");
        if (File.Exists(dxgi))
        {
            var arch = PeFile.GetArchitecture(dxgi);
            bool ok = arch == profile.Architecture;
            r.Add(new CheckResult(6, "dxgi.dll do jogo com a arquitetura do exe",
                ok ? CheckState.Pass : CheckState.Fail,
                $"dxgi.dll é {arch}, exe é {profile.Architecture}.",
                ok ? null : "Arquitetura trocada: reinstale para extrair a versão certa do ReShade."));
        }
        else
        {
            r.Add(new CheckResult(6, "dxgi.dll do jogo", CheckState.Fail,
                "dxgi.dll não está na pasta do exe.", "Rode a instalação."));
        }

        if (route is InstallRoute.B or InstallRoute.C)
        {
            var hostDxgi = Path.Combine(exe, "host64", "dxgi.dll");
            if (File.Exists(hostDxgi))
            {
                var arch = PeFile.GetArchitecture(hostDxgi);
                bool ok = arch == PeArchitecture.X64;
                r.Add(new CheckResult(6, "host64\\dxgi.dll é x64",
                    ok ? CheckState.Pass : CheckState.Fail,
                    $"host64\\dxgi.dll é {arch}.",
                    ok ? null : "Precisa ser o ReShade x64."));
            }
            else
            {
                r.Add(new CheckResult(6, "host64\\dxgi.dll é x64", CheckState.Fail,
                    "host64\\dxgi.dll não existe.", "Rode a instalação."));
            }

            // addon32 na raiz e NENHUM .addon64 lá (armadilha do Tomb Raider)
            bool addon32 = File.Exists(Path.Combine(exe, "dlss5-feed.addon32"));
            var strayAddon64 = Directory.Exists(exe)
                ? Directory.EnumerateFiles(exe, "*.addon64", SearchOption.TopDirectoryOnly).ToList()
                : new List<string>();
            r.Add(new CheckResult(10, "Layout 32-bit correto",
                addon32 && strayAddon64.Count == 0 ? CheckState.Pass : CheckState.Fail,
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
            File.Exists(feedFx) ? CheckState.Pass : CheckState.Fail,
            File.Exists(feedFx)
                ? "reshade-shaders\\Shaders\\DLSS5_Feed.fx presente."
                : "DLSS5_Feed.fx não encontrado.",
            File.Exists(feedFx) ? null : "Rode a instalação (ou reinstale: o desinstalador do ReShade apaga reshade-shaders\\)."));

        // 13 — preset com o provedor acima do Feed
        var preset = Path.Combine(exe, "ReShadePreset.ini");
        if (File.Exists(preset))
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
                hasFeed && hasMv && mvFirst ? CheckState.Pass : CheckState.Fail,
                techLine ?? "linha Techniques= ausente",
                hasFeed && hasMv && mvFirst
                    ? null
                    : "O preset deve listar o provedor de MV antes do DLSS5_Feed."));
        }
        else
        {
            r.Add(new CheckResult(13, "ReShadePreset.ini", CheckState.Fail,
                "Preset não encontrado.", "Rode a instalação."));
        }

        // 5 — dgVoodoo (rota C)
        if (route == InstallRoute.C)
        {
            var renderer = profile.RendererFolder ?? exe;
            var d3d9 = Path.Combine(renderer, "D3D9.dll");
            var conf = Path.Combine(renderer, "dgVoodoo.conf");
            bool d3d9Ok = File.Exists(d3d9) && PeFile.GetArchitecture(d3d9) == PeArchitecture.X86;
            r.Add(new CheckResult(5, "dgVoodoo2 na pasta do renderizador",
                d3d9Ok ? CheckState.Pass : CheckState.Fail,
                d3d9Ok ? $"D3D9.dll (x86) em {renderer}" : $"D3D9.dll x86 ausente em {renderer}",
                d3d9Ok ? null : "No Source o D3D9.dll vai em bin\\, não na raiz."));

            if (File.Exists(conf))
            {
                var text = File.ReadAllText(conf);
                bool passthru = text.Contains("DisableAndPassThru", StringComparison.OrdinalIgnoreCase)
                                && !ValueIs(text, "DisableAndPassThru", "true");
                r.Add(new CheckResult(5, "dgVoodoo.conf ajustado",
                    passthru ? CheckState.Pass : CheckState.Fail,
                    passthru ? "DisableAndPassThru=false (dgVoodoo ativo)." : "DisableAndPassThru ainda está true.",
                    passthru ? null : "Com passthru=true o dgVoodoo não faz nada — causa nº 1 de 'não acontece nada'."));
            }

            r.Add(new CheckResult(5, "Marca d'água do dgVoodoo na tela", CheckState.Manual,
                "Abra o jogo: a marca d'água do dgVoodoo tem que aparecer.",
                "Sem marca d'água, o dgVoodoo não está interceptando."));
        }

        // 7/8 — ReShade carregou e viu o swapchain (lê o log se já existir)
        r.AddRange(VerifyReShadeLog(exe));

        // 14/15/16 — dependem do jogo rodando
        r.AddRange(VerifyFeedLogs(exe, route));

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

    private static IEnumerable<CheckResult> VerifyReShadeLog(string exeFolder)
    {
        var log = Path.Combine(exeFolder, "ReShade.log");
        if (!File.Exists(log))
        {
            yield return new CheckResult(7, "ReShade carregou", CheckState.Manual,
                "ReShade.log ainda não existe — abra o jogo uma vez.",
                "Depois de abrir o jogo, volte aqui e clique em Verificar de novo.");
            yield break;
        }

        var size = new FileInfo(log).Length;
        string text;
        try { text = ReadShared(log); } catch { text = ""; }

        bool loaded = size > ReShadeLogPlaceholderSize
                      && text.Contains("ReShade", StringComparison.OrdinalIgnoreCase);
        yield return new CheckResult(7, "ReShade carregou",
            loaded ? CheckState.Pass : CheckState.Fail,
            loaded ? $"ReShade.log com {size} bytes." : $"ReShade.log com {size} bytes (placeholder).",
            loaded ? null : "Arquitetura do dxgi.dll errada, local errado, ou outro módulo tomou o nome dxgi.dll.");

        bool sawSwapchain = text.Contains("CreateSwapChain", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("Recreated runtime environment", StringComparison.OrdinalIgnoreCase);
        yield return new CheckResult(8, "ReShade viu o swapchain",
            sawSwapchain ? CheckState.Pass : (loaded ? CheckState.Fail : CheckState.Manual),
            sawSwapchain
                ? "Log tem CreateSwapChain / Recreated runtime environment."
                : "Log sem CreateSwapChain: o ReShade carregou mas não é a factory do renderizador.",
            sawSwapchain ? null : "O dxgi.dll tem que estar na pasta do EXE. Overlays podem estar chegando antes.");
    }

    private static IEnumerable<CheckResult> VerifyFeedLogs(string exeFolder, InstallRoute route)
    {
        var feedLog = Path.Combine(exeFolder, "dlss5-feed.log");
        if (!File.Exists(feedLog))
        {
            yield return new CheckResult(15, "Feeder entregando frames", CheckState.Manual,
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
            ready && delivered ? CheckState.Pass : CheckState.Warning,
            ready && delivered
                ? "Log mostra feature pronta e frames entregues."
                : "Log existe mas ainda sem 'feature ready ... DLAA' + 'frame N delivered'.",
            ready && delivered ? null : "Veja o diagnóstico abaixo.");

        if (route is InstallRoute.B or InstallRoute.C)
        {
            var hostLog = Path.Combine(exeFolder, "host64", "dlss5-feed-host.log");
            yield return new CheckResult(16, "Processo auxiliar host64 rodando",
                File.Exists(hostLog) ? CheckState.Pass : CheckState.Manual,
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
