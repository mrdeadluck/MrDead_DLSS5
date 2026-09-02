namespace Dlss5.Core;

/// <summary>Estado do mod no jogo selecionado. É a máquina de estados que a interface segue.</summary>
public enum ModState
{
    /// <summary>Nenhuma pasta apontada ainda.</summary>
    SemJogo,
    /// <summary>A pasta apontada não existe (jogo movido, unidade fora).</summary>
    JogoNaoEncontrado,
    /// <summary>A pasta existe mas não tem executável — não é a pasta de um jogo.</summary>
    JogoSemExecutavel,
    /// <summary>Nada do mod na pasta.</summary>
    NaoInstalado,
    /// <summary>Manifesto presente e todos os arquivos conferem.</summary>
    Instalado,
    /// <summary>Instalado e íntegro, mas feito por versão anterior do programa ou de um kit diferente.</summary>
    InstaladoDesatualizado,
    /// <summary>Manifesto diz "em andamento" ou faltam arquivos que ele lista.</summary>
    InstalacaoIncompleta,
    /// <summary>Arquivos listados existem mas não são mais o que foi gravado.</summary>
    InstalacaoInconsistente,
    /// <summary>Uma desinstalação parou no meio: manifesto marcado, parte dos arquivos ainda lá.</summary>
    ReversaoIncompleta,
    /// <summary>Arquivos do mod encontrados sem manifesto (versão antiga do programa ou instalação manual).</summary>
    VestigiosSemManifesto,
    /// <summary>Só sobraram backups .dlss5bak por devolver.</summary>
    SomenteBackups,
    /// <summary>Manifesto ilegível ou situação que exige diagnóstico antes de mexer.</summary>
    Desconhecido,
}

/// <summary>Ações que a interface pode oferecer. A primeira da lista é a principal.</summary>
public enum AcaoDoMod
{
    Instalar,
    AtualizarOuReconfigurar,
    Desinstalar,
    Reparar,
    RemoverVestigios,
    RestaurarBackups,
    SelecionarOutroJogo,
    VerDetalhes,
    VerificarInstalacao,
}

/// <summary>Situação de um arquivo esperado pelo manifesto.</summary>
public sealed record SituacaoDeArquivo(string Caminho, ConferenciaDeArquivo Situacao, string Papel);

/// <summary>
/// Tudo que a tela inicial precisa saber sobre o jogo apontado, calculado de uma vez e
/// sem tocar em nada.
/// </summary>
public sealed class RelatorioDeEstado
{
    public ModState Estado { get; set; } = ModState.SemJogo;
    public string GameFolder { get; set; } = "";
    public string? ExeFolder { get; set; }
    public string? RealExePath { get; set; }
    public PeArchitecture Architecture { get; set; }
    public GraphicsApi Api { get; set; }
    public InstallRoute Route { get; set; }
    public DetectionResult? Deteccao { get; set; }

    public InstallManifest? Manifesto { get; set; }
    public string? ManifestoCorrompidoEm { get; set; }
    public string? VersaoDoProgramaInstalada { get; set; }
    public DateTime? InstaladoEm { get; set; }
    public bool KitDiferente { get; set; }
    public bool ProgramaMaisNovo { get; set; }
    public bool JogoAtualizadoDepois { get; set; }
    public bool OverrideNoRegistro { get; set; }

    public List<SituacaoDeArquivo> Arquivos { get; } = new();
    public int Corretos => Arquivos.Count(a => a.Situacao == ConferenciaDeArquivo.Igual);
    public int Ausentes => Arquivos.Count(a => a.Situacao == ConferenciaDeArquivo.Ausente);
    public int Alterados => Arquivos.Count(a => a.Situacao == ConferenciaDeArquivo.Diferente);
    public int Ilegiveis => Arquivos.Count(a => a.Situacao == ConferenciaDeArquivo.Ilegivel);

    public List<string> BackupsValidos { get; } = new();
    public List<string> BackupsProblematicos { get; } = new();
    public List<string> Vestigios { get; } = new();
    public List<string> BackupsOrfaos { get; } = new();
    public List<string> Conflitos { get; } = new();
    public List<Bloqueio> Bloqueios { get; } = new();
    public List<string> Avisos { get; } = new();
    public List<AcaoDoMod> Acoes { get; } = new();

    public AcaoDoMod? AcaoPrincipal => Acoes.Count > 0 ? Acoes[0] : null;
    public bool Bloqueado => Bloqueios.Count > 0;
    public bool TemBackupValido => BackupsValidos.Count > 0;
    public bool PodeDesinstalar => Acoes.Contains(AcaoDoMod.Desinstalar);

    /// <summary>Uma linha, para o cartão de estado.</summary>
    public string Resumo { get; set; } = "";

    /// <summary>O que o programa sugere fazer agora.</summary>
    public string ProximoPasso { get; set; } = "";
}

/// <summary>
/// Inspeciona a pasta do jogo e diz em que estado o mod está. Não modifica nada.
/// Centraliza a decisão de quais ações fazem sentido — a interface só obedece.
/// </summary>
public static class EstadoDoMod
{
    public static RelatorioDeEstado Inspecionar(string? gameFolder, KitInventory? kit = null, Diario? diario = null)
    {
        var r = new RelatorioDeEstado { GameFolder = gameFolder?.Trim() ?? "" };

        if (string.IsNullOrWhiteSpace(r.GameFolder))
        {
            r.Estado = ModState.SemJogo;
            r.Resumo = "Nenhum jogo selecionado.";
            r.ProximoPasso = "Aponte a pasta onde o jogo está instalado.";
            r.Acoes.Add(AcaoDoMod.SelecionarOutroJogo);
            return r;
        }
        if (!Directory.Exists(r.GameFolder))
        {
            r.Estado = ModState.JogoNaoEncontrado;
            r.Resumo = "A pasta não existe (jogo movido, desinstalado ou unidade desconectada).";
            r.ProximoPasso = "Selecione a pasta atual do jogo.";
            r.Acoes.Add(AcaoDoMod.SelecionarOutroJogo);
            return r;
        }

        diario?.Tecnico($"Inspecionando {r.GameFolder}");

        // Manifesto primeiro: se existe, ele diz onde a instalação foi feita.
        var manifesto = InstallManifest.Find(r.GameFolder, null, out var corrompidoEm);
        r.Manifesto = manifesto;
        r.ManifestoCorrompidoEm = corrompidoEm;

        // Detecção do jogo (executável, arquitetura, API). Reaproveitada pela instalação.
        DetectionResult? deteccao = null;
        try { deteccao = GameDetector.Detect(r.GameFolder); }
        catch (Exception ex) { diario?.Aviso($"Detecção do jogo falhou: {ex.Message}"); }
        r.Deteccao = deteccao;

        var perfil = deteccao?.Profile;
        if (manifesto is not null && manifesto.RealExePath is not null && File.Exists(manifesto.RealExePath) && perfil is not null)
        {
            // A instalação já decidiu qual é o exe; a detecção de agora pode divergir
            // (jogo Unreal, launcher) e o manifesto é quem manda.
            perfil.RealExePath = manifesto.RealExePath;
            var arch = PeFile.GetArchitecture(manifesto.RealExePath);
            if (arch != PeArchitecture.Unknown) perfil.Architecture = arch;
            if (Enum.TryParse<GraphicsApi>(manifesto.Api, out var api) && api != GraphicsApi.Unknown) perfil.Api = api;
            perfil.RendererFolder = manifesto.RendererFolder ?? perfil.RendererFolder;
            perfil.HasNativeDlss = manifesto.HasNativeDlss;
            perfil.PreferirFeeder = manifesto.PreferirFeeder;
        }

        r.ExeFolder = manifesto?.ExeFolder is { } ef && Directory.Exists(ef) ? ef : perfil?.ExeFolder ?? r.GameFolder;
        r.RealExePath = perfil?.RealExePath ?? manifesto?.RealExePath;
        r.Architecture = perfil?.Architecture ?? PeArchitecture.Unknown;
        r.Api = perfil?.Api ?? GraphicsApi.Unknown;
        r.Route = perfil?.Route ?? InstallRoute.Unsupported;

        // Vestígios e backups (varredura por nome, conservadora).
        var engine = new InstallerEngine(_ => { }) { NvngxDlssDoKit = kit?.NvngxDlss, ReFrameworkDoKit = kit?.ReFrameworkDinput8 };
        var achados = engine.EncontrarInstalacao(r.GameFolder, estrito: true);
        foreach (var a in achados)
        {
            if (a.EndsWith(Propriedade.BackupSuffix, StringComparison.OrdinalIgnoreCase)) r.BackupsOrfaos.Add(a);
            else if (!a.EndsWith(InstallManifest.FileName, StringComparison.OrdinalIgnoreCase)) r.Vestigios.Add(a);
        }
        // Arquivos com cara de ReShade sem nenhuma peça do kit por perto: pode ser um
        // ReShade do usuário ou resto de instalação antiga. Não decide o estado; avisa.
        var ambiguos = engine.EncontrarInstalacao(r.GameFolder, estrito: false)
            .Where(a => !achados.Contains(a, StringComparer.OrdinalIgnoreCase))
            .Where(a => !a.EndsWith(Propriedade.BackupSuffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (ambiguos.Count > 0 && manifesto is null)
            r.Avisos.Add("Arquivos do ReShade sem peças do kit por perto (" +
                         string.Join(", ", ambiguos.Take(4).Select(Path.GetFileName)) +
                         (ambiguos.Count > 4 ? ", …" : "") +
                         "): pode ser um ReShade seu ou resto de uma instalação antiga. Instalar substitui com backup; " +
                         "\"Remover vestígios\" mostra a lista e pergunta antes de apagar.");

        // Registro (só no Windows).
        if (OperatingSystem.IsWindows())
        {
            try { r.OverrideNoRegistro = SignatureOverride.Query().AllSet; } catch { }
        }

        // Bloqueios de agora (jogo aberto, pasta sem escrita).
        var rodando = Preflight.JogoRodando(r.RealExePath);
        if (rodando is not null)
            r.Bloqueios.Add(new Bloqueio("O jogo está aberto",
                $"O processo {rodando}.exe está em execução.",
                "Feche o jogo antes de instalar, reparar ou desinstalar."));
        if (r.ExeFolder is not null && !Preflight.PastaGravavel(r.ExeFolder, out var motivo))
            r.Bloqueios.Add(new Bloqueio("Sem permissão para gravar na pasta do jogo",
                $"{r.ExeFolder}: {motivo}.",
                "Execute o programa como administrador ou ajuste as permissões da pasta."));

        // Outros mods/injetores: só aviso.
        if (r.ExeFolder is not null)
        {
            foreach (var (nome, desc) in Propriedade.OutrosInjetores)
            {
                var p = Path.Combine(r.ExeFolder, nome);
                if (File.Exists(p)) r.Conflitos.Add($"{nome} — {desc}");
            }
            foreach (var (nome, prova) in Propriedade.PrecisamDeProva)
            {
                var p = Path.Combine(r.ExeFolder, nome);
                if (File.Exists(p) && !Propriedade.ContemTexto(p, prova)
                    && (manifesto is null || !manifesto.Files.ContainsKey(p)))
                    r.Conflitos.Add($"{nome} — já existe na pasta e NÃO é o ReShade deste kit (outro mod ou wrapper). Instalar substitui esse arquivo, guardando backup.");
            }
        }

        if (corrompidoEm is not null && manifesto is null)
        {
            r.Estado = ModState.Desconhecido;
            r.Resumo = "Há um registro de instalação ilegível na pasta do jogo.";
            r.ProximoPasso = "Veja os detalhes. É possível remover os componentes do mod no modo conservador (só arquivos comprovadamente do mod).";
            r.Avisos.Add($"Manifesto ilegível: {corrompidoEm}");
            r.Acoes.Add(AcaoDoMod.VerDetalhes);
            if (r.Vestigios.Count > 0 || r.BackupsOrfaos.Count > 0) r.Acoes.Add(AcaoDoMod.RemoverVestigios);
            r.Acoes.Add(AcaoDoMod.SelecionarOutroJogo);
            return r;
        }

        if (manifesto is not null)
        {
            AvaliarComManifesto(r, manifesto, kit);
        }
        else if (r.Vestigios.Count > 0)
        {
            r.Estado = ModState.VestigiosSemManifesto;
            r.Resumo = $"Arquivos do mod encontrados ({r.Vestigios.Count}), mas sem registro de instalação (versão antiga do programa ou instalação manual).";
            r.ProximoPasso = "Remova os vestígios no modo conservador (só o que é comprovadamente do mod) ou instale por cima para gerar um registro novo.";
            r.Acoes.Add(AcaoDoMod.RemoverVestigios);
            if (r.Route != InstallRoute.Unsupported) r.Acoes.Add(AcaoDoMod.Instalar);
            r.Acoes.Add(AcaoDoMod.VerDetalhes);
        }
        else if (r.BackupsOrfaos.Count > 0)
        {
            r.Estado = ModState.SomenteBackups;
            r.Resumo = $"Sobraram {r.BackupsOrfaos.Count} backup(s) de arquivos originais (.dlss5bak) por devolver.";
            r.ProximoPasso = "Devolva os originais ao lugar. Depois a pasta fica como antes do mod.";
            r.Acoes.Add(AcaoDoMod.RestaurarBackups);
            r.Acoes.Add(AcaoDoMod.VerDetalhes);
        }
        else if (deteccao is null || deteccao.Candidates.Count == 0)
        {
            r.Estado = ModState.JogoSemExecutavel;
            r.Resumo = "Nenhum executável encontrado nesta pasta.";
            r.ProximoPasso = "Aponte a pasta onde está o .exe do jogo (a raiz da instalação).";
            r.Acoes.Add(AcaoDoMod.SelecionarOutroJogo);
        }
        else
        {
            r.Estado = ModState.NaoInstalado;
            r.Resumo = "DLSS 5 não está instalado neste jogo.";
            if (r.Route == InstallRoute.Unsupported)
            {
                r.ProximoPasso = "A combinação detectada não tem caminho de instalação; confira a arquitetura e a API na tela de detecção.";
                r.Acoes.Add(AcaoDoMod.Instalar);   // a tela de detecção deixa corrigir a API
                r.Acoes.Add(AcaoDoMod.VerDetalhes);
            }
            else
            {
                r.ProximoPasso = "Instale o DLSS 5. Antes de gravar qualquer arquivo o programa mostra o plano completo.";
                r.Acoes.Add(AcaoDoMod.Instalar);
            }
            if (ambiguos.Count > 0) r.Acoes.Add(AcaoDoMod.RemoverVestigios);
        }

        if (deteccao is not null && deteccao.Candidates.Count == 0 && r.Estado != ModState.JogoSemExecutavel)
            r.Avisos.Add("Nenhum executável encontrado nesta pasta — a instalação foi feita em outra subpasta ou o jogo foi removido.");

        r.Acoes.Add(AcaoDoMod.SelecionarOutroJogo);
        return r;
    }

    private static void AvaliarComManifesto(RelatorioDeEstado r, InstallManifest m, KitInventory? kit)
    {
        r.VersaoDoProgramaInstalada = m.AppVersion ?? "anterior à 1.1 (sem versão registrada)";
        r.InstaladoEm = m.InstalledUtc;

        foreach (var f in m.ArquivosGravados)
            r.Arquivos.Add(new SituacaoDeArquivo(f, m.ConferirGravado(f), m.BackedUpFiles.ContainsKey(f) ? "substituído" : "adicionado"));

        foreach (var (original, backup) in m.BackedUpFiles)
        {
            var conf = m.ConferirBackup(original);
            if (conf == ConferenciaDeArquivo.Igual) r.BackupsValidos.Add(original);
            else r.BackupsProblematicos.Add($"{original} — backup {Descrever(conf)} ({backup})");
        }

        // Jogo atualizado depois da instalação?
        try
        {
            if (m.RealExePath is not null && File.Exists(m.RealExePath))
            {
                var fi = new FileInfo(m.RealExePath);
                if ((m.GameExeModifiedUtc is { } quando && fi.LastWriteTimeUtc > quando.AddMinutes(1))
                    || (m.GameExeSize is { } tam && fi.Length != tam))
                    r.JogoAtualizadoDepois = true;
            }
        }
        catch { }

        r.ProgramaMaisNovo = VersaoMenor(m.AppVersion, AppInfo.Versao);
        r.KitDiferente = kit is not null && m.KitVersion is not null && !string.Equals(kit.Fingerprint(), m.KitVersion, StringComparison.Ordinal);

        bool emAndamento = m.Status == StatusDoManifesto.InstalacaoEmAndamento || m.Status == StatusDoManifesto.InstalacaoIncompleta;
        bool revertendo = m.Status == StatusDoManifesto.ReversaoEmAndamento || m.Status == StatusDoManifesto.ReversaoIncompleta;

        if (revertendo)
        {
            r.Estado = ModState.ReversaoIncompleta;
            r.Resumo = "Uma desinstalação anterior não terminou: parte dos componentes ainda está na pasta.";
            r.ProximoPasso = "Desinstale de novo para terminar a remoção e restaurar o que faltou.";
            r.Acoes.Add(AcaoDoMod.Desinstalar);
            r.Acoes.Add(AcaoDoMod.VerDetalhes);
        }
        else if (emAndamento || r.Ausentes > 0)
        {
            r.Estado = ModState.InstalacaoIncompleta;
            r.Resumo = emAndamento
                ? "A instalação foi interrompida antes de terminar (programa fechado, queda de energia ou erro)."
                : $"Faltam {r.Ausentes} arquivo(s) que a instalação tinha gravado (removidos à mão, por antivírus ou por atualização do jogo).";
            r.ProximoPasso = "Repare a instalação (repõe só o que falta) ou desinstale para restaurar o jogo.";
            r.Acoes.Add(AcaoDoMod.Reparar);
            r.Acoes.Add(AcaoDoMod.Desinstalar);
            r.Acoes.Add(AcaoDoMod.VerDetalhes);
        }
        else if (r.Alterados > 0 || r.Ilegiveis > 0)
        {
            r.Estado = ModState.InstalacaoInconsistente;
            r.Resumo = $"{r.Alterados + r.Ilegiveis} arquivo(s) do mod não são mais o que foi gravado (alterados por outro programa, atualização ou à mão).";
            r.ProximoPasso = "Veja os detalhes. Reparar regrava os arquivos do mod; desinstalar remove só o que ainda é comprovadamente do mod.";
            r.Acoes.Add(AcaoDoMod.VerDetalhes);
            r.Acoes.Add(AcaoDoMod.Reparar);
            r.Acoes.Add(AcaoDoMod.Desinstalar);
        }
        else if (r.ProgramaMaisNovo || r.KitDiferente || m.EhVersaoAntiga)
        {
            r.Estado = ModState.InstaladoDesatualizado;
            r.Resumo = r.KitDiferente
                ? "DLSS 5 instalado, mas o kit apontado tem arquivos diferentes dos instalados (atualização disponível)."
                : "DLSS 5 instalado por uma versão anterior do programa.";
            r.ProximoPasso = "Atualize para regravar com os arquivos e o registro atuais, ou desinstale.";
            r.Acoes.Add(AcaoDoMod.AtualizarOuReconfigurar);
            r.Acoes.Add(AcaoDoMod.Desinstalar);
            r.Acoes.Add(AcaoDoMod.VerificarInstalacao);
        }
        else
        {
            r.Estado = ModState.Instalado;
            r.Resumo = $"DLSS 5 instalado e íntegro ({r.Corretos} arquivo(s) conferidos).";
            r.ProximoPasso = "Pronto para jogar. Você pode verificar a instalação, reconfigurar ou desinstalar.";
            r.Acoes.Add(AcaoDoMod.Desinstalar);
            r.Acoes.Add(AcaoDoMod.AtualizarOuReconfigurar);
            r.Acoes.Add(AcaoDoMod.VerificarInstalacao);
        }

        if (r.JogoAtualizadoDepois)
            r.Avisos.Add("O executável do jogo mudou depois da instalação (atualização do jogo). Se o mod parou de funcionar, use Reparar; se o jogo passou a ter DLSS próprio, prefira desinstalar e instalar de novo.");
        if (r.BackupsProblematicos.Count > 0)
            r.Avisos.Add($"{r.BackupsProblematicos.Count} backup(s) de originais com problema — a desinstalação vai avisar o que não pôde ser restaurado.");
        if (m.RegistryOverrideApplied && OperatingSystem.IsWindows() && !r.OverrideNoRegistro)
            r.Avisos.Add("O override de assinatura consta como aplicado, mas não está no registro (removido por outra instalação/desinstalação). Reparar ou Atualizar reaplica.");
        if (r.Vestigios.Any(v => !m.ArquivosGravados.Contains(v, StringComparer.OrdinalIgnoreCase) && !Propriedade.RestosDeExecucao.Any(n => v.EndsWith(n, StringComparison.OrdinalIgnoreCase)) && !Propriedade.EstaEmPastaNossa(v) && !v.EndsWith(Path.DirectorySeparatorChar)))
            r.Avisos.Add("Há arquivos do mod fora do registro desta instalação (instalação antiga em outra subpasta?). A desinstalação remove só o registrado; use \"Remover vestígios\" depois, se sobrar algo.");
    }

    private static string Descrever(ConferenciaDeArquivo c) => c switch
    {
        ConferenciaDeArquivo.Igual => "íntegro",
        ConferenciaDeArquivo.Diferente => "alterado",
        ConferenciaDeArquivo.Ausente => "ausente",
        _ => "ilegível",
    };

    /// <summary>a &lt; b em comparação semântica simples (x.y.z). Nulos contam como mais antigos.</summary>
    public static bool VersaoMenor(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(b)) return false;
        if (string.IsNullOrWhiteSpace(a)) return true;
        return Version.TryParse(Limpar(a), out var va) && Version.TryParse(Limpar(b), out var vb) && va < vb;

        static string Limpar(string v)
        {
            int i = v.IndexOfAny(new[] { '-', '+', ' ' });
            var s = i > 0 ? v[..i] : v;
            return s.Count(c => c == '.') == 0 ? s + ".0" : s;
        }
    }
}
