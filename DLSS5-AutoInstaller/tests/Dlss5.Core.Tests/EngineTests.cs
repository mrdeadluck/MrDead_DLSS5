using Dlss5.Core;
using Xunit;

namespace Dlss5.Core.Tests;

/// <summary>
/// Um kit e um jogo falsos em pastas temporárias. Nenhum teste toca em instalação real.
/// </summary>
internal sealed class Cenario : IDisposable
{
    public string Raiz { get; }
    public string Kit { get; }
    public string Jogo { get; }
    public KitInventory Inventario { get; }

    public Cenario()
    {
        Raiz = Path.Combine(Path.GetTempPath(), "dlss5eng_" + Guid.NewGuid().ToString("N"));
        Kit = Path.Combine(Raiz, "kit");
        Jogo = Path.Combine(Raiz, "jogo");
        Directory.CreateDirectory(Kit);
        Directory.CreateDirectory(Jogo);
        Directory.CreateDirectory(Path.Combine(Kit, "reshade-shaders", "Shaders"));
        Directory.CreateDirectory(Path.Combine(Kit, "reshade-shaders", "Textures"));

        string K(string nome, string conteudo)
        {
            var p = Path.Combine(Kit, nome);
            File.WriteAllText(p, conteudo);
            return p;
        }

        File.WriteAllText(Path.Combine(Kit, "reshade-shaders", "Shaders", "DLSS5_Feed.fx"), "feed");
        File.WriteAllText(Path.Combine(Kit, "reshade-shaders", "Shaders", "MartysMods_LAUNCHPAD.fx"), "launchpad");
        File.WriteAllText(Path.Combine(Kit, "reshade-shaders", "Textures", "lut.png"), "png");
        EscreverExeFalso(Path.Combine(Jogo, "jogo.exe"));

        Inventario = new KitInventory
        {
            KitRoot = Kit,
            NvngxDlssnr = K("nvngx_dlssnr.dll", "dlssnr v1"),
            NvngxDlss = K("nvngx_dlss.dll", "dlss v1"),
            RenodxAddon64 = K("renodx-dlss5.addon64", "renodx v1"),
            FeedAddon64 = K("dlss5-feed.addon64", "feed64 v1"),
            FeedAddon32 = K("dlss5-feed.addon32", "feed32 v1"),
            FeedHost64Exe = K("dlss5-feed-host64.exe", "host v1"),
            DxgiX64 = K("dxgi64.dll", "ReShade 6.8.0 x64"),
            DxgiX86 = K("dxgi32.dll", "ReShade 6.8.0 x86"),
            ShadersDir = Path.Combine(Kit, "reshade-shaders"),
            HasLaunchpad = true,
        };
    }

    /// <summary>Cabeçalho PE mínimo (MZ + PE\0\0 + Machine) para o detector aceitar o exe.</summary>
    public static void EscreverExeFalso(string caminho, bool x64 = true, int tamanhoExtra = 0)
    {
        var b = new byte[0x60 + tamanhoExtra];
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        BitConverter.GetBytes(0x40u).CopyTo(b, 0x3C);
        b[0x40] = (byte)'P'; b[0x41] = (byte)'E'; b[0x42] = 0; b[0x43] = 0;
        BitConverter.GetBytes(x64 ? (ushort)0x8664 : (ushort)0x014C).CopyTo(b, 0x44);
        File.WriteAllBytes(caminho, b);
    }

    public GameProfile Perfil(PeArchitecture arch = PeArchitecture.X64, GraphicsApi api = GraphicsApi.D3D12) => new()
    {
        GameFolder = Jogo,
        RealExePath = Path.Combine(Jogo, "jogo.exe"),
        Architecture = arch,
        Api = api,
        RendererFolder = Jogo,
    };

    public InstallOptions Opcoes() => new() { ApplyRegistryOverride = false };

    public InstallPlan Plano(GameProfile? perfil = null) =>
        InstallPlanBuilder.Build(perfil ?? Perfil(), Inventario, Opcoes());

    public string NoJogo(string nome) => Path.Combine(Jogo, nome);

    public void Dispose()
    {
        try { Directory.Delete(Raiz, true); } catch { }
    }
}

public class InstalacaoSeguraTests
{
    [Fact]
    public void GravaManifestoV2ComHashesEStatusConcluido()
    {
        using var c = new Cenario();
        var r = new InstallerEngine(_ => { }).Execute(c.Plano(), c.Inventario);

        Assert.True(r.Sucesso, r.Erro);
        var m = InstallManifest.Load(c.Jogo);
        Assert.NotNull(m);
        Assert.Equal(InstallManifest.VersaoAtual, m!.Version);
        Assert.Equal(StatusDoManifesto.Concluida, m.Status);
        Assert.NotNull(m.AppVersion);
        Assert.NotNull(m.KitVersion);
        Assert.Contains(c.NoJogo("dxgi.dll"), m.AddedFiles);
        Assert.True(m.Files.ContainsKey(c.NoJogo("dxgi.dll")));
        Assert.Equal(ConferenciaDeArquivo.Igual, m.ConferirGravado(c.NoJogo("dxgi.dll")));
        Assert.True(File.Exists(c.NoJogo("ReShade.ini")));
        Assert.True(File.Exists(Path.Combine(c.Jogo, "reshade-shaders", "Shaders", "DLSS5_Feed.fx")));
        // Nenhum temporário ficou para trás.
        Assert.Empty(Directory.EnumerateFiles(c.Jogo, "*.dlss5tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(c.Jogo, "*.dlss5prev", SearchOption.AllDirectories));
    }

    [Fact]
    public void ReinstalarNaoDestroiOBackupDoOriginal()
    {
        // O bug crítico da versão anterior: na segunda instalação o "backup" era refeito a
        // partir do arquivo que já era do mod, e a desinstalação "restaurava" o próprio mod.
        using var c = new Cenario();
        File.WriteAllText(c.NoJogo("ReShade.ini"), "ini DO USUARIO");

        var engine = new InstallerEngine(_ => { });
        Assert.True(engine.Execute(c.Plano(), c.Inventario).Sucesso);
        Assert.Equal("ini DO USUARIO", File.ReadAllText(c.NoJogo("ReShade.ini" + Propriedade.BackupSuffix)));

        // Segunda instalação com tecla diferente: o ReShade.ini muda, o backup NÃO.
        var opcoes = c.Opcoes();
        opcoes.OverlayKey = ReShadeConfigWriter.KeyInsert;
        var plano2 = InstallPlanBuilder.Build(c.Perfil(), c.Inventario, opcoes);
        var r2 = engine.Execute(plano2, c.Inventario);
        Assert.True(r2.Sucesso, r2.Erro);
        Assert.Equal("ini DO USUARIO", File.ReadAllText(c.NoJogo("ReShade.ini" + Propriedade.BackupSuffix)));
        Assert.Contains("KeyOverlay=45", File.ReadAllText(c.NoJogo("ReShade.ini")));

        // Desinstalar devolve o do usuário.
        var rev = engine.Revert(InstallManifest.Load(c.Jogo)!, removeRegistryOverride: false);
        Assert.True(rev.Sucesso, rev.Erro);
        Assert.Equal("ini DO USUARIO", File.ReadAllText(c.NoJogo("ReShade.ini")));
        Assert.False(File.Exists(c.NoJogo("dxgi.dll")));
        Assert.False(File.Exists(c.NoJogo(InstallManifest.FileName)));
    }

    [Fact]
    public void ReinstalarEhIdempotente()
    {
        using var c = new Cenario();
        var engine = new InstallerEngine(_ => { });
        Assert.True(engine.Execute(c.Plano(), c.Inventario).Sucesso);
        var m1 = InstallManifest.Load(c.Jogo)!;

        var r2 = engine.Execute(c.Plano(), c.Inventario);
        Assert.True(r2.Sucesso, r2.Erro);
        Assert.Empty(r2.Gravados);
        Assert.NotEmpty(r2.JaEstavamIguais);
        Assert.Empty(r2.BackupsCriados);

        var m2 = InstallManifest.Load(c.Jogo)!;
        Assert.Equal(m1.AddedFiles.Count, m2.AddedFiles.Count);
        Assert.Equal(m1.AddedFiles.Count, m1.AddedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Empty(Directory.EnumerateFiles(c.Jogo, "*" + Propriedade.BackupSuffix, SearchOption.AllDirectories));
    }

    [Fact]
    public void FalhaNoMeioDesfazTudoESemManifesto()
    {
        using var c = new Cenario();
        File.WriteAllText(c.NoJogo("dxgi.dll"), "dxgi DO JOGO");

        var plano = c.Plano();
        // Última ação com origem inexistente: falha depois de tudo o mais ter sido gravado.
        plano.Actions.Add(new PlanAction(PlanActionKind.CopyFile, "quebra",
            Path.Combine(c.Kit, "nao-existe.dll"), c.NoJogo("nao-existe.dll")));

        var r = new InstallerEngine(_ => { }).Execute(plano, c.Inventario);

        Assert.False(r.Sucesso);
        Assert.True(r.RollbackExecutado);
        Assert.Empty(r.FalhasDoRollback);
        Assert.NotNull(r.EtapaDoErro);
        Assert.False(File.Exists(c.NoJogo(InstallManifest.FileName)));
        Assert.False(File.Exists(c.NoJogo("ReShade.ini")));
        Assert.False(File.Exists(c.NoJogo("renodx-dlss5.addon64")));
        Assert.False(Directory.Exists(Path.Combine(c.Jogo, "reshade-shaders")));
        // O original voltou e o backup foi consumido.
        Assert.Equal("dxgi DO JOGO", File.ReadAllText(c.NoJogo("dxgi.dll")));
        Assert.False(File.Exists(c.NoJogo("dxgi.dll" + Propriedade.BackupSuffix)));
        Assert.Empty(Directory.EnumerateFiles(c.Jogo, "*.dlss5tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void CancelamentoDepoisDeAlgumaAlteracaoFazRollback()
    {
        using var c = new Cenario();
        using var cts = new CancellationTokenSource();
        int passos = 0;
        var progresso = new Progress<ProgressoDaOperacao>();   // não usado: precisa ser síncrono
        var sincrono = new ProgressoSincrono(p => { if (++passos == 3) cts.Cancel(); });

        var r = new InstallerEngine(_ => { }).Execute(c.Plano(), c.Inventario, cts.Token, sincrono);

        Assert.False(r.Sucesso);
        Assert.True(r.Cancelada);
        Assert.True(r.RollbackExecutado);
        Assert.False(File.Exists(c.NoJogo(InstallManifest.FileName)));
        Assert.DoesNotContain(Directory.EnumerateFiles(c.Jogo), f => Path.GetFileName(f) != "jogo.exe");
    }

    [Fact]
    public void BackupNaoPodeSerCriadoAOperacaoNaoComeca()
    {
        // Pasta inexistente no destino do plano não é o caso; aqui o que se garante é que
        // o manifesto é gravado ANTES da primeira modificação: com a pasta do exe sem
        // acesso, nada é tocado.
        using var c = new Cenario();
        var perfil = c.Perfil();
        perfil.RealExePath = Path.Combine(c.Jogo, "nao-existe", "jogo.exe");
        var plano = InstallPlanBuilder.Build(perfil, c.Inventario, c.Opcoes());

        var r = new InstallerEngine(_ => { }).Execute(plano, c.Inventario);

        Assert.False(r.Sucesso);
        Assert.NotEmpty(r.Bloqueios);
        Assert.DoesNotContain(Directory.EnumerateFiles(c.Jogo), f => Path.GetFileName(f) != "jogo.exe");
    }

    [Fact]
    public void ArquivoExistenteQueNaoEhNossoViraConflitoNoPlano()
    {
        using var c = new Cenario();
        File.WriteAllText(c.NoJogo("dxgi.dll"), "wrapper de outro mod");
        File.WriteAllText(c.NoJogo("dinput8.dll"), "asi loader");

        var plano = c.Plano();

        Assert.Contains(plano.Conflitos, x => x.Contains("dxgi.dll"));
        Assert.Contains(plano.OutrosMods, x => x.Contains("dinput8.dll"));
        Assert.Contains(plano.Warnings, w => w.Contains("injetores"));
    }

    [Fact]
    public void BackupOrfaoDeInstalacaoAntigaEhAdotadoENaoSobrescrito()
    {
        using var c = new Cenario();
        // Instalação antiga (sem manifesto): dxgi do ReShade na pasta + backup do original.
        File.WriteAllText(c.NoJogo("dxgi.dll"), "ReShade velho");
        File.WriteAllText(c.NoJogo("dxgi.dll" + Propriedade.BackupSuffix), "dxgi ORIGINAL do jogo");

        var engine = new InstallerEngine(_ => { });
        var r = engine.Execute(c.Plano(), c.Inventario);
        Assert.True(r.Sucesso, r.Erro);
        Assert.Equal("dxgi ORIGINAL do jogo", File.ReadAllText(c.NoJogo("dxgi.dll" + Propriedade.BackupSuffix)));

        var m = InstallManifest.Load(c.Jogo)!;
        // dxgi velho era "nosso" pela heurística: substituído sem backup novo; o órfão
        // continua lá e é devolvido na faxina/reversão.
        var rev = engine.Revert(m, false);
        Assert.Equal("dxgi ORIGINAL do jogo", File.ReadAllText(c.NoJogo("dxgi.dll")));
        Assert.True(rev.Sucesso, rev.Erro);
    }

    /// <summary>IProgress síncrono: o Progress&lt;T&gt; padrão posta no SynchronizationContext e chegaria tarde.</summary>
    private sealed class ProgressoSincrono : IProgress<ProgressoDaOperacao>
    {
        private readonly Action<ProgressoDaOperacao> _a;
        public ProgressoSincrono(Action<ProgressoDaOperacao> a) => _a = a;
        public void Report(ProgressoDaOperacao value) => _a(value);
    }
}

public class DesinstalacaoTests
{
    [Fact]
    public void DesinstalarRemoveTudoRestauraEApagaOManifesto()
    {
        using var c = new Cenario();
        File.WriteAllText(c.NoJogo("ReShade.ini"), "do usuario");
        var engine = new InstallerEngine(_ => { });
        Assert.True(engine.Execute(c.Plano(), c.Inventario).Sucesso);
        // O jogo rodou e o ReShade escreveu logs.
        File.WriteAllText(c.NoJogo("ReShade.log"), "log");

        var r = engine.Revert(InstallManifest.Load(c.Jogo)!, removeRegistryOverride: false);

        Assert.True(r.Sucesso, r.Erro);
        Assert.True(r.ManifestoRemovido);
        Assert.Empty(r.Sobras);
        Assert.Contains(c.NoJogo("ReShade.ini"), r.Restaurados);
        Assert.Equal("do usuario", File.ReadAllText(c.NoJogo("ReShade.ini")));
        Assert.False(File.Exists(c.NoJogo("ReShade.log")));
        Assert.False(Directory.Exists(Path.Combine(c.Jogo, "reshade-shaders")));
        Assert.True(File.Exists(c.NoJogo("jogo.exe")));
        Assert.Equal(new[] { "jogo.exe", "ReShade.ini" },
            Directory.GetFiles(c.Jogo).Select(Path.GetFileName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void NaoApagaArquivoQueDeixouDeSerDoMod()
    {
        using var c = new Cenario();
        var engine = new InstallerEngine(_ => { });
        Assert.True(engine.Execute(c.Plano(), c.Inventario).Sucesso);

        // Outro programa trocou o dxgi.dll por um que não é o ReShade.
        File.WriteAllText(c.NoJogo("dxgi.dll"), "wrapper de OUTRO mod");

        var r = engine.Revert(InstallManifest.Load(c.Jogo)!, false);

        Assert.True(File.Exists(c.NoJogo("dxgi.dll")));
        Assert.Equal("wrapper de OUTRO mod", File.ReadAllText(c.NoJogo("dxgi.dll")));
        Assert.Contains(r.Preservados, p => p.Contains("dxgi.dll"));
        Assert.False(File.Exists(c.NoJogo("renodx-dlss5.addon64")));
        Assert.True(r.Sucesso, r.Erro);   // preservar de propósito não é falha
    }

    [Fact]
    public void BackupAusenteNaoFingeRestauracao()
    {
        using var c = new Cenario();
        File.WriteAllText(c.NoJogo("ReShade.ini"), "do usuario");
        var engine = new InstallerEngine(_ => { });
        Assert.True(engine.Execute(c.Plano(), c.Inventario).Sucesso);
        File.Delete(c.NoJogo("ReShade.ini" + Propriedade.BackupSuffix));

        var r = engine.Revert(InstallManifest.Load(c.Jogo)!, false);

        Assert.True(r.RestauracaoIncompleta);
        Assert.Contains(r.NaoRestaurados, n => n.Contains("ReShade.ini"));
        Assert.False(File.Exists(c.NoJogo("ReShade.ini")));   // o do mod saiu; o original não existe mais
        Assert.DoesNotContain(r.Restaurados, x => x.EndsWith("ReShade.ini"));
    }

    [Fact]
    public void BackupAlteradoNaoEhUsadoParaRestaurar()
    {
        using var c = new Cenario();
        File.WriteAllText(c.NoJogo("ReShade.ini"), "do usuario");
        var engine = new InstallerEngine(_ => { });
        Assert.True(engine.Execute(c.Plano(), c.Inventario).Sucesso);
        File.WriteAllText(c.NoJogo("ReShade.ini" + Propriedade.BackupSuffix), "backup corrompido");

        var r = engine.Revert(InstallManifest.Load(c.Jogo)!, false);

        Assert.True(r.RestauracaoIncompleta);
        Assert.Contains(r.NaoRestaurados, n => n.Contains("não é confiável"));
    }

    [Fact]
    public void ManifestoAntigoSemHashAindaDesinstala()
    {
        using var c = new Cenario();
        File.WriteAllText(c.NoJogo("dxgi.dll"), "ReShade 6.8.0");
        File.WriteAllText(c.NoJogo("renodx-dlss5.addon64"), "x");
        File.WriteAllText(c.NoJogo("nvngx_dlss.dll"), "kit");
        File.WriteAllText(c.NoJogo("nvngx_dlss.dll" + Propriedade.BackupSuffix), "DO JOGO");
        var v1 = $$"""
        {
          "Version": "1",
          "GameFolder": {{System.Text.Json.JsonSerializer.Serialize(c.Jogo)}},
          "ExeFolder": {{System.Text.Json.JsonSerializer.Serialize(c.Jogo)}},
          "AddedFiles": [ {{System.Text.Json.JsonSerializer.Serialize(c.NoJogo("dxgi.dll"))}}, {{System.Text.Json.JsonSerializer.Serialize(c.NoJogo("renodx-dlss5.addon64"))}} ],
          "BackedUpFiles": { {{System.Text.Json.JsonSerializer.Serialize(c.NoJogo("nvngx_dlss.dll"))}}: {{System.Text.Json.JsonSerializer.Serialize(c.NoJogo("nvngx_dlss.dll" + Propriedade.BackupSuffix))}} }
        }
        """;
        File.WriteAllText(c.NoJogo(InstallManifest.FileName), v1);

        var m = InstallManifest.Load(c.Jogo);
        Assert.NotNull(m);
        Assert.True(m!.EhVersaoAntiga);

        var r = new InstallerEngine(_ => { }).Revert(m, false);

        Assert.True(r.Sucesso, r.Erro);
        Assert.False(File.Exists(c.NoJogo("dxgi.dll")));
        Assert.False(File.Exists(c.NoJogo("renodx-dlss5.addon64")));
        Assert.Equal("DO JOGO", File.ReadAllText(c.NoJogo("nvngx_dlss.dll")));
    }

    [Fact]
    public void ReparoRepoeSoOQueFalta()
    {
        using var c = new Cenario();
        var engine = new InstallerEngine(_ => { });
        Assert.True(engine.Execute(c.Plano(), c.Inventario).Sucesso);
        File.Delete(c.NoJogo("renodx-dlss5.addon64"));
        File.WriteAllText(c.NoJogo("nvngx_dlssnr.dll"), "corrompido pelo antivirus");

        var estadoAntes = EstadoDoMod.Inspecionar(c.Jogo);
        Assert.Equal(ModState.InstalacaoIncompleta, estadoAntes.Estado);
        Assert.Contains(AcaoDoMod.Reparar, estadoAntes.Acoes);

        var r = engine.Execute(c.Plano(), c.Inventario);

        Assert.True(r.Sucesso, r.Erro);
        Assert.Equal(2, r.Gravados.Count);
        Assert.Equal("renodx v1", File.ReadAllText(c.NoJogo("renodx-dlss5.addon64")));
        Assert.Equal("dlssnr v1", File.ReadAllText(c.NoJogo("nvngx_dlssnr.dll")));
        Assert.Equal(ModState.Instalado, EstadoDoMod.Inspecionar(c.Jogo).Estado);
    }
}

public class EstadoDoModTests
{
    [Fact]
    public void SemPastaEPastaInexistente()
    {
        Assert.Equal(ModState.SemJogo, EstadoDoMod.Inspecionar("").Estado);
        var r = EstadoDoMod.Inspecionar(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(ModState.JogoNaoEncontrado, r.Estado);
        Assert.Equal(AcaoDoMod.SelecionarOutroJogo, r.AcaoPrincipal);
    }

    [Fact]
    public void PastaSemExecutavelNaoEhJogo()
    {
        using var c = new Cenario();
        File.Delete(c.NoJogo("jogo.exe"));
        Assert.Equal(ModState.JogoSemExecutavel, EstadoDoMod.Inspecionar(c.Jogo).Estado);
    }

    [Fact]
    public void NaoInstaladoOfereceInstalar()
    {
        using var c = new Cenario();
        var r = EstadoDoMod.Inspecionar(c.Jogo);
        Assert.Equal(ModState.NaoInstalado, r.Estado);
        Assert.Equal(AcaoDoMod.Instalar, r.AcaoPrincipal);
        Assert.DoesNotContain(AcaoDoMod.Desinstalar, r.Acoes);
    }

    [Fact]
    public void InstaladoOfereceDesinstalarSemPrecisarDoKit()
    {
        using var c = new Cenario();
        Assert.True(new InstallerEngine(_ => { }).Execute(c.Plano(), c.Inventario).Sucesso);

        var r = EstadoDoMod.Inspecionar(c.Jogo);   // sem kit

        Assert.Equal(ModState.Instalado, r.Estado);
        Assert.Equal(AcaoDoMod.Desinstalar, r.AcaoPrincipal);
        Assert.True(r.Corretos > 0);
        Assert.Equal(0, r.Ausentes);
        Assert.NotNull(r.VersaoDoProgramaInstalada);
    }

    [Fact]
    public void ArquivoAlteradoViraInconsistente()
    {
        using var c = new Cenario();
        Assert.True(new InstallerEngine(_ => { }).Execute(c.Plano(), c.Inventario).Sucesso);
        File.WriteAllText(c.NoJogo("dxgi.dll"), "trocado");

        var r = EstadoDoMod.Inspecionar(c.Jogo);
        Assert.Equal(ModState.InstalacaoInconsistente, r.Estado);
        Assert.Equal(1, r.Alterados);
        Assert.Contains(AcaoDoMod.Desinstalar, r.Acoes);
        Assert.Contains(AcaoDoMod.Reparar, r.Acoes);
    }

    [Fact]
    public void ManifestoEmAndamentoEhInstalacaoIncompleta()
    {
        using var c = new Cenario();
        Assert.True(new InstallerEngine(_ => { }).Execute(c.Plano(), c.Inventario).Sucesso);
        var m = InstallManifest.Load(c.Jogo)!;
        m.Status = StatusDoManifesto.InstalacaoEmAndamento;
        m.Save();

        Assert.Equal(ModState.InstalacaoIncompleta, EstadoDoMod.Inspecionar(c.Jogo).Estado);

        m.Status = StatusDoManifesto.ReversaoIncompleta;
        m.Save();
        var r = EstadoDoMod.Inspecionar(c.Jogo);
        Assert.Equal(ModState.ReversaoIncompleta, r.Estado);
        Assert.Equal(AcaoDoMod.Desinstalar, r.AcaoPrincipal);
    }

    [Fact]
    public void KitDiferenteEhDesatualizado()
    {
        using var c = new Cenario();
        Assert.True(new InstallerEngine(_ => { }).Execute(c.Plano(), c.Inventario).Sucesso);
        File.WriteAllText(c.Inventario.RenodxAddon64!, "renodx v2 maior");

        var r = EstadoDoMod.Inspecionar(c.Jogo, c.Inventario);
        Assert.Equal(ModState.InstaladoDesatualizado, r.Estado);
        Assert.Equal(AcaoDoMod.AtualizarOuReconfigurar, r.AcaoPrincipal);
        Assert.Contains(AcaoDoMod.Desinstalar, r.Acoes);
    }

    [Fact]
    public void VestigiosSemManifestoSaoLegado()
    {
        using var c = new Cenario();
        File.WriteAllText(c.NoJogo("renodx-dlss5.addon64"), "x");
        File.WriteAllText(c.NoJogo("ReShade.ini"), "x");

        var r = EstadoDoMod.Inspecionar(c.Jogo);
        Assert.Equal(ModState.VestigiosSemManifesto, r.Estado);
        Assert.Equal(AcaoDoMod.RemoverVestigios, r.AcaoPrincipal);
        Assert.Equal(2, r.Vestigios.Count);
    }

    [Fact]
    public void SomenteBackupsOfereceRestaurar()
    {
        using var c = new Cenario();
        File.WriteAllText(c.NoJogo("sl.interposer.dll" + Propriedade.BackupSuffix), "original");

        var r = EstadoDoMod.Inspecionar(c.Jogo);
        Assert.Equal(ModState.SomenteBackups, r.Estado);
        Assert.Equal(AcaoDoMod.RestaurarBackups, r.AcaoPrincipal);
    }

    [Fact]
    public void ManifestoCorrompidoEhDesconhecido()
    {
        using var c = new Cenario();
        File.WriteAllText(c.NoJogo(InstallManifest.FileName), "{ isto não é json");
        File.WriteAllText(c.NoJogo("renodx-dlss5.addon64"), "x");

        var r = EstadoDoMod.Inspecionar(c.Jogo);
        Assert.Equal(ModState.Desconhecido, r.Estado);
        Assert.NotNull(r.ManifestoCorrompidoEm);
        Assert.Equal(AcaoDoMod.VerDetalhes, r.AcaoPrincipal);
        Assert.Contains(AcaoDoMod.RemoverVestigios, r.Acoes);
    }

    [Fact]
    public void JogoAtualizadoDepoisViraAviso()
    {
        using var c = new Cenario();
        Assert.True(new InstallerEngine(_ => { }).Execute(c.Plano(), c.Inventario).Sucesso);
        Cenario.EscreverExeFalso(c.NoJogo("jogo.exe"), tamanhoExtra: 4096);   // a loja atualizou o exe

        var r = EstadoDoMod.Inspecionar(c.Jogo);
        Assert.True(r.JogoAtualizadoDepois);
        Assert.Contains(r.Avisos, a => a.Contains("atualização"));
    }

    [Fact]
    public void OutroInjetorNaPastaViraConflito()
    {
        using var c = new Cenario();
        File.WriteAllText(c.NoJogo("dxgi.dll"), "wrapper qualquer");
        var r = EstadoDoMod.Inspecionar(c.Jogo);
        Assert.Equal(ModState.NaoInstalado, r.Estado);
        Assert.Contains(r.Conflitos, x => x.Contains("dxgi.dll"));
    }

    [Theory]
    [InlineData(null, "1.1.0", true)]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("1.2.0", "1.1.0", false)]
    [InlineData("1.1.0+abc", "1.1.0", false)]
    public void ComparacaoDeVersao(string? a, string b, bool esperado) =>
        Assert.Equal(esperado, EstadoDoMod.VersaoMenor(a, b));
}

public class ManifestoV2Tests
{
    [Fact]
    public void SalvaAtomicoESemTemporario()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5man2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var m = new InstallManifest { GameFolder = dir, ExeFolder = dir };
            m.Save(dir);
            Assert.True(File.Exists(InstallManifest.CaminhoEm(dir)));
            Assert.False(File.Exists(InstallManifest.CaminhoEm(dir) + ".tmp"));
            Assert.NotNull(m.UpdatedUtc);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DicionariosVoltamInsensiveisAMaiusculas()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5man2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var m = new InstallManifest { GameFolder = dir, ExeFolder = dir };
            m.BackedUpFiles[Path.Combine(dir, "ReShade.ini")] = Path.Combine(dir, "ReShade.ini.dlss5bak");
            m.Save(dir);
            var lido = InstallManifest.Load(dir)!;
            Assert.True(lido.BackedUpFiles.ContainsKey(Path.Combine(dir, "RESHADE.INI")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadDistingueAusenteDeCorrompido()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlss5man2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(InstallManifest.Load(dir, out var corrompido));
            Assert.False(corrompido);
            File.WriteAllText(InstallManifest.CaminhoEm(dir), "###");
            Assert.Null(InstallManifest.Load(dir, out corrompido));
            Assert.True(corrompido);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OpcoesEPerfilGravadosVoltamIguais()
    {
        using var c = new Cenario();
        var opcoes = c.Opcoes();
        opcoes.OverlayKey = 112; opcoes.OverlayCtrl = true; opcoes.MvProvider = MvProvider.Drme;
        var plano = InstallPlanBuilder.Build(c.Perfil(PeArchitecture.X86, GraphicsApi.D3D11), c.Inventario, opcoes);
        var m = InstallManifest.Para(plano, c.Inventario);

        var o2 = m.OpcoesGravadas();
        Assert.Equal(112, o2.OverlayKey);
        Assert.True(o2.OverlayCtrl);
        Assert.Equal(MvProvider.Drme, o2.MvProvider);
        Assert.False(o2.ApplyRegistryOverride);

        var p2 = m.PerfilGravado()!;
        Assert.Equal(PeArchitecture.X86, p2.Architecture);
        Assert.Equal(GraphicsApi.D3D11, p2.Api);
        Assert.Equal(InstallRoute.B, p2.Route);
    }
}

public class DiarioTests
{
    [Fact]
    public void GravaEmArquivoComCabecalhoERotaciona()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "dlss5log_" + Guid.NewGuid().ToString("N"));
        try
        {
            for (int i = 0; i < 12; i++)
            {
                using var d = new Diario(pasta);
                d.Info($"execução {i}");
            }
            var arquivos = Directory.GetFiles(pasta, "dlss5-*.log");
            Assert.True(arquivos.Length <= Diario.MaximoDeArquivos, $"ficaram {arquivos.Length}");

            using var d2 = new Diario(pasta);
            var visiveis = new List<LinhaDeLog>();
            d2.LinhaVisivel += visiveis.Add;
            d2.Tecnico("só no arquivo");
            d2.Aviso("na tela");
            d2.Erro("contexto", new InvalidOperationException("boom"));
            var texto = d2.LerTudo();
            Assert.Contains(AppInfo.Nome, texto);
            Assert.Contains("só no arquivo", texto);
            Assert.Contains("InvalidOperationException", texto);
            Assert.Equal(2, visiveis.Count);   // aviso + erro; o técnico não vai para a tela
        }
        finally { try { Directory.Delete(pasta, true); } catch { } }
    }

    [Fact]
    public void SemPastaDeLogContinuaEmMemoria()
    {
        // Um ARQUIVO no lugar da pasta: CreateDirectory falha e o diário cai para memória.
        var caminho = Path.Combine(Path.GetTempPath(), "dlss5log_" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(caminho, "ocupado");
        try
        {
            using var d = new Diario(caminho);
            d.Info("ainda registra");
            Assert.Null(d.Pasta);
            Assert.Contains("ainda registra", d.LerTudo());
            Assert.Contains("Sem acesso", d.ConteudoEmMemoria);
        }
        finally { File.Delete(caminho); }
    }
}

public class PreflightTests
{
    [Fact]
    public void ArquivoAbertoComExclusividadeApareceComoEmUso()
    {
        var f = Path.GetTempFileName();
        try
        {
            using (new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Contains(f, Preflight.ArquivosEmUso(new[] { f }));
            }
            Assert.Empty(Preflight.ArquivosEmUso(new[] { f }));
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void PastaInexistenteNaoEhGravavel()
    {
        Assert.False(Preflight.PastaGravavel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), out var motivo));
        Assert.NotNull(motivo);
        Assert.True(Preflight.PastaGravavel(Path.GetTempPath(), out _));
    }

    [Fact]
    public void BytesNecessariosSomaOrigens()
    {
        using var c = new Cenario();
        var plano = c.Plano();
        Assert.True(Preflight.BytesNecessarios(plano.Actions) > 0);
    }
}

public class MotorShortFuseTests
{
    private static GameProfile PerfilSf(Cenario c, GraphicsApi api = GraphicsApi.D3D11, int passes = 2)
    {
        var p = c.Perfil(PeArchitecture.X64, api);
        p.Engine = NeuralEngine.RenodxDlssShortFuse;
        p.PassCount = passes;
        return p;
    }

    [Fact]
    public void PerfilDecideOMotor()
    {
        using var c = new Cenario();
        var p = PerfilSf(c, GraphicsApi.D3D12);
        p.HasNativeDlss = true;
        Assert.True(p.UsesShortFuse);
        Assert.False(p.UsesRenodxDirectPath);
        Assert.False(p.NeedsFeeder);

        // 32-bit ignora a escolha: o addon do ShortFuse e o NGX são x64.
        var x86 = c.Perfil(PeArchitecture.X86, GraphicsApi.D3D11);
        x86.Engine = NeuralEngine.RenodxDlssShortFuse;
        Assert.False(x86.UsesShortFuse);
        Assert.True(x86.NeedsFeeder);
    }

    [Fact]
    public void KitSemOAddonDoShortFuseBloqueia()
    {
        using var c = new Cenario();
        var plano = InstallPlanBuilder.Build(PerfilSf(c), c.Inventario, c.Opcoes());
        Assert.Contains(plano.Blockers, b => b.Contains(ShortFuseDlss.Addon, StringComparison.Ordinal));
        // E o kit sem Feeder nem shaders NÃO é problema para este motor.
        var inv = new KitInventory { KitRoot = "k", NvngxDlssnr = "a", NvngxDlss = "b", RenodxDlssShortFuse = "c", DxgiX64 = "d" };
        Assert.Empty(inv.MissingFor(InstallRoute.A, nativeDlss: false, shortFuse: true));
        Assert.NotEmpty(inv.MissingFor(InstallRoute.A, nativeDlss: false));
    }

    [Fact]
    public void PlanoCopiaSoOAddonDoShortFuseETiraOsRivais()
    {
        using var c = new Cenario();
        var sf = Path.Combine(c.Kit, ShortFuseDlss.Addon);
        File.WriteAllText(sf, "shortfuse");
        c.Inventario.RenodxDlssShortFuse = sf;
        // Sobra de uma instalação Krish + Feeder na pasta do jogo.
        File.WriteAllText(Path.Combine(c.Jogo, "renodx-dlss5.addon64"), "krish");
        File.WriteAllText(Path.Combine(c.Jogo, "dlss5-feed.addon64"), "feed");

        var plano = InstallPlanBuilder.Build(PerfilSf(c, passes: 3), c.Inventario, c.Opcoes());
        Assert.Empty(plano.Blockers);
        var copias = plano.Actions.Where(a => a.Kind == PlanActionKind.CopyFile).Select(a => Path.GetFileName(a.TargetPath!)).ToList();
        Assert.Contains(ShortFuseDlss.Addon, copias);
        Assert.DoesNotContain("renodx-dlss5.addon64", copias);
        Assert.DoesNotContain("dlss5-feed.addon64", copias);
        Assert.Contains("nvngx_dlssnr.dll", copias);
        var removidos = plano.Actions.Where(a => a.Kind == PlanActionKind.DeleteForbiddenFile).Select(a => Path.GetFileName(a.TargetPath!)).ToList();
        Assert.Contains("renodx-dlss5.addon64", removidos);
        Assert.Contains("dlss5-feed.addon64", removidos);
        Assert.Contains(plano.Warnings, w => w.Contains("3 passada", StringComparison.Ordinal));

        // O manifesto guarda motor e passadas, e devolve no perfil.
        var m = InstallManifest.Para(plano, c.Inventario);
        Assert.Equal(nameof(NeuralEngine.RenodxDlssShortFuse), m.Engine);
        Assert.Equal(3, m.PassCount);
        var perfil = m.PerfilGravado()!;
        Assert.Equal(NeuralEngine.RenodxDlssShortFuse, perfil.Engine);
        Assert.Equal(3, perfil.PassCount);
    }

    [Fact]
    public void MotorKrishTiraOAddonDoShortFuseDaPasta()
    {
        using var c = new Cenario();
        File.WriteAllText(Path.Combine(c.Jogo, ShortFuseDlss.Addon), "shortfuse");
        var plano = InstallPlanBuilder.Build(c.Perfil(PeArchitecture.X64, GraphicsApi.D3D11), c.Inventario, c.Opcoes());
        var removidos = plano.Actions.Where(a => a.Kind == PlanActionKind.DeleteForbiddenFile).Select(a => Path.GetFileName(a.TargetPath!)).ToList();
        Assert.Contains(ShortFuseDlss.Addon, removidos);
    }

    [Fact]
    public void IniDoShortFuseTemLoadFromDllMainEPassadas()
    {
        var ini = ReShadeConfigWriter.BuildReShadeIni(feederUsed: false, shortFuse: true, passCount: 4);
        Assert.Contains($"LoadFromDllMain={ShortFuseDlss.Addon}", ini);
        Assert.Contains("[RENODX-DLSS]", ini);
        Assert.Contains("DirectNeuralRenderingPassCount=4", ini);
        Assert.DoesNotContain("[RenoDX.DLSS5]", ini);
        Assert.Equal(4, ShortFuseDlss.LerPassadas(ini));
        // Limites: 1 a 10.
        Assert.Equal(10, ShortFuseDlss.LerPassadas(ReShadeConfigWriter.BuildReShadeIni(feederUsed: false, shortFuse: true, passCount: 50)));
        Assert.Equal(1, ShortFuseDlss.LerPassadas(ReShadeConfigWriter.BuildReShadeIni(feederUsed: false, shortFuse: true, passCount: 0)));
        // O ini de sempre não muda.
        var krish = ReShadeConfigWriter.BuildReShadeIni(feederUsed: false);
        Assert.DoesNotContain("LoadFromDllMain", krish);
        Assert.Null(ShortFuseDlss.LerPassadas(krish));
        Assert.Contains("[RenoDX.DLSS5]", krish);
    }

    [Fact]
    public void LogDoShortFuseViraCheckpoint14()
    {
        var ok = ShortFuseLog.Ler("Registered add-on \"RenoDX DLSS\" v0.52\nRenoDX DLSS attached; ReShade logical unload will be ignored.\n" +
                                  "RenoDX DLSS-NR source evaluation completed: source=swapchain application_frame=12 size=2560x1440 replace_source=1 return_output=1.");
        Assert.Equal(CheckStatus.Pass, ok.Checkpoint14(2, reinicioPendente: false).State);
        Assert.Equal(CheckStatus.Warning, ok.Checkpoint14(2, reinicioPendente: true).State);

        var reinicio = ShortFuseLog.Ler("Registered add-on \"RenoDX DLSS\"\nAdded renodx-dlss.addon64 to ADDON.LoadFromDllMain in ReShade.ini. Restart required.");
        Assert.Equal(CheckStatus.Warning, reinicio.Checkpoint14(2, false).State);
        Assert.Contains("reinício", reinicio.Checkpoint14(2, false).Detail);

        var semRuntime = ShortFuseLog.Ler("Registered add-on \"RenoDX DLSS\"\nRenoDX DLSS could not attach the direct nvngx_dlssnr.dll runtime.");
        Assert.Equal(CheckStatus.Fail, semRuntime.Checkpoint14(2, false).State);

        var nada = ShortFuseLog.Ler("Registered add-on \"Generic Depth\"\nCreateSwapChain");
        Assert.Equal(CheckStatus.Fail, nada.Checkpoint14(2, false).State);
    }

    [Fact]
    public void PassosManuaisFalamDoPassCount()
    {
        using var c = new Cenario();
        var passos = ManualSteps.For(PerfilSf(c, passes: 2), c.Opcoes());
        Assert.Contains(passos, s => s.Title.Contains("Pass Count", StringComparison.Ordinal) && s.Detail.Contains("RenoDX DLSS", StringComparison.Ordinal));
        Assert.DoesNotContain(passos, s => s.Title.Contains("DESLIGAR o DLSS", StringComparison.Ordinal));
    }
}
