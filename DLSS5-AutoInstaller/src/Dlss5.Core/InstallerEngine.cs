namespace Dlss5.Core;

/// <summary>Progresso de uma operação longa, para a interface mostrar etapa e barra.</summary>
public sealed record ProgressoDaOperacao(string Etapa, int Atual, int Total, string? Detalhe = null);

/// <summary>Resultado de uma instalação (ou reparo/atualização, que usam o mesmo caminho).</summary>
public sealed class ResultadoDaInstalacao
{
    public bool Sucesso { get; init; }
    public bool Cancelada { get; init; }
    public InstallManifest? Manifesto { get; init; }
    public string? Erro { get; init; }
    public string? EtapaDoErro { get; init; }
    public IReadOnlyList<Bloqueio> Bloqueios { get; init; } = Array.Empty<Bloqueio>();

    public List<string> Gravados { get; } = new();
    public List<string> JaEstavamIguais { get; } = new();
    public List<string> BackupsCriados { get; } = new();
    public List<string> BackupsPreservados { get; } = new();
    public List<string> Avisos { get; } = new();

    public bool RollbackExecutado { get; init; }
    public IReadOnlyList<string> FalhasDoRollback { get; init; } = Array.Empty<string>();
}

/// <summary>Resultado de uma desinstalação (por manifesto ou conservadora).</summary>
public sealed class ResultadoDaReversao
{
    /// <summary>Nada do mod sobrou e tudo que tinha backup válido voltou.</summary>
    public bool Sucesso { get; set; }
    public string? Erro { get; set; }
    public IReadOnlyList<Bloqueio> Bloqueios { get; set; } = Array.Empty<Bloqueio>();

    public List<string> Removidos { get; } = new();
    public List<string> Restaurados { get; } = new();
    /// <summary>Caminho — motivo. Arquivos que ficaram de propósito.</summary>
    public List<string> Preservados { get; } = new();
    /// <summary>Originais que não puderam voltar (backup ausente/inválido) — caminho — motivo.</summary>
    public List<string> NaoRestaurados { get; } = new();
    /// <summary>Caminho — erro. Quase sempre arquivo em uso.</summary>
    public List<string> Falhas { get; } = new();
    /// <summary>O que a conferência final ainda encontrou do mod.</summary>
    public List<string> Sobras { get; } = new();
    public bool OverrideRemovido { get; set; }
    public bool ManifestoRemovido { get; set; }

    public bool RestauracaoIncompleta => NaoRestaurados.Count > 0;
}

/// <summary>
/// Executa um plano e sabe desfazê-lo.
///
/// Instalação: pré-checagens antes de tocar em qualquer coisa; cada arquivo vai para um
/// temporário ao lado do destino e só então é movido por cima (troca atômica); o
/// manifesto é gravado ANTES da primeira modificação e depois de cada passo; qualquer
/// falha ou cancelamento desfaz o que esta execução fez. Executar de novo sobre uma
/// instalação válida não duplica nada nem refaz backups: arquivo igual é pulado, e o
/// backup de um original NUNCA é sobrescrito por um arquivo que já é do mod.
/// </summary>
public sealed partial class InstallerEngine
{
    private readonly Action<string> _log;
    private readonly Diario? _diario;

    /// <summary>
    /// Propriedade de cada arquivo que já existia ANTES desta execução começar. Decidida
    /// antes de gravar o manifesto, senão o próprio manifesto novo viraria "prova de que
    /// a pasta é nossa" e um ReShade.ini do usuário deixaria de receber backup.
    /// </summary>
    private Dictionary<string, OrigemDoArquivo> _origens = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Caminho do nvngx_dlss.dll DO KIT, usado como gabarito para reconhecer o
    /// "transplante" (um nvngx_dlss.dll na pasta do jogo byte a byte igual ao do kit,
    /// obra de versão antiga deste programa). Sem ele nada de nvngx_dlss.dll sai.
    /// </summary>
    public string? NvngxDlssDoKit { get; set; }

    public InstallerEngine(Action<string> log)
    {
        _log = log;
    }

    public InstallerEngine(Diario diario)
    {
        _diario = diario;
        _log = diario.Info;
    }

    /// <summary>Sufixo dos backups que criamos.</summary>
    public const string BackupSuffix = Propriedade.BackupSuffix;

    private void Tecnico(string texto)
    {
        if (_diario is not null) _diario.Tecnico(texto);
    }

    private void Aviso(string texto)
    {
        if (_diario is not null) _diario.Aviso(texto); else _log("Aviso: " + texto);
    }

    // ------------------------------------------------------------------ instalar

    /// <summary>Uma modificação já feita nesta execução e como desfazê-la.</summary>
    private abstract record Desfazer
    {
        public sealed record ApagarAdicionado(string Caminho) : Desfazer;
        /// <summary>O arquivo anterior está guardado em <paramref name="Guardado"/>; volta por cima.</summary>
        public sealed record DevolverAnterior(string Caminho, string Guardado, bool ApagarGuardadoDepois) : Desfazer;
        public sealed record DevolverRemovido(string Caminho, string Backup) : Desfazer;
        public sealed record ApagarPasta(string Pasta) : Desfazer;
        public sealed record DesligarOverride : Desfazer;
    }

    public ResultadoDaInstalacao Execute(InstallPlan plan, KitInventory kit) =>
        Execute(plan, kit, CancellationToken.None, null);

    public ResultadoDaInstalacao Execute(
        InstallPlan plan, KitInventory kit, CancellationToken ct, IProgress<ProgressoDaOperacao>? progresso)
    {
        var profile = plan.Profile;
        var exe = profile.ExeFolder;
        string etapa = "Pré-checagens";
        void Reportar(string nome, int atual, int total, string? detalhe = null)
        {
            etapa = nome;
            progresso?.Report(new ProgressoDaOperacao(nome, atual, total, detalhe));
        }

        // Total: pré-checagens + ações + verificação + limpeza.
        int total = plan.Actions.Count + 3;
        Reportar("Verificando o jogo e a pasta", 0, total);

        // ---- 1. Pré-checagens: nada é tocado enquanto algo puder impedir.
        var anterior = InstallManifest.Find(profile.GameFolder, exe, out var manifestoCorrompido);
        if (manifestoCorrompido is not null)
            Aviso($"Havia um manifesto ilegível em {manifestoCorrompido}; ele será substituído pelo desta operação.");
        string? manifestoAnteriorJson = null;
        if (anterior is not null)
        {
            try { manifestoAnteriorJson = File.ReadAllText(anterior.Caminho); } catch { }
            Tecnico($"Instalação anterior encontrada ({anterior.AppVersion ?? "v1"}, {anterior.InstalledUtc:u}); backups dos originais serão preservados.");
        }

        var alvos = AlvosDoPlano(plan).ToList();
        var bloqueios = Preflight.Checar(exe, profile.RendererFolder, profile.RealExePath,
            alvos.Where(File.Exists), Preflight.BytesNecessarios(plan.Actions));
        if (bloqueios.Count > 0)
        {
            foreach (var b in bloqueios) _log($"Bloqueado: {b.Titulo} — {b.Detalhe}");
            return new ResultadoDaInstalacao { Sucesso = false, Bloqueios = bloqueios, EtapaDoErro = etapa, Erro = "A operação não começou: há impedimentos." };
        }

        _origens = alvos.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(a => a, a => Propriedade.Classificar(a, anterior, paraInstalar: true), StringComparer.OrdinalIgnoreCase);
        foreach (var (a, o) in _origens) Tecnico($"Já existe no destino: {a} → {o}");

        var manifest = InstallManifest.Para(plan, kit);
        if (anterior is not null) manifest.HerdarDe(anterior);
        var resultado = new ResultadoDaInstalacao { Sucesso = true, Manifesto = manifest };
        var desfazer = new List<Desfazer>();

        try
        {
            // Diário no disco antes da primeira modificação: se o programa cair, sobra o rastro.
            manifest.Save(exe);
            Tecnico($"Manifesto de operação {manifest.OperationId} gravado em {manifest.Caminho}");
        }
        catch (Exception ex)
        {
            return new ResultadoDaInstalacao
            {
                Sucesso = false, EtapaDoErro = etapa,
                Erro = $"Não consegui gravar o manifesto em {exe}: {ex.Message}. Sem ele a instalação não seria reversível, então nada foi alterado.",
            };
        }

        try
        {
            int i = 0;
            foreach (var action in plan.Actions)
            {
                ct.ThrowIfCancellationRequested();
                i++;
                Reportar(RotuloDaEtapa(action), i, total, action.Description);

                switch (action.Kind)
                {
                    case PlanActionKind.DeleteForbiddenFile:
                        RemoveForbidden(action.TargetPath!, manifest, desfazer);
                        break;

                    case PlanActionKind.CopyFile:
                        if (Directory.Exists(action.SourcePath))
                            CopyDirectory(action.SourcePath!, action.TargetPath!, manifest, anterior, desfazer, resultado, ct);
                        else
                            Gravar(action.TargetPath!, manifest, anterior, desfazer, resultado,
                                tmp => File.Copy(action.SourcePath!, tmp, overwrite: true),
                                identicoA: action.SourcePath);
                        break;

                    case PlanActionKind.ExtractReShadeDll:
                        Gravar(action.TargetPath!, manifest, anterior, desfazer, resultado,
                            tmp => ReShadeExtractor.ExtractTo(action.SourcePath!, profile.Architecture, tmp));
                        break;

                    case PlanActionKind.WriteGeneratedFile:
                    {
                        var conteudo = ConteudoGerado(action.TargetPath!, plan, action.SourcePath);
                        Gravar(action.TargetPath!, manifest, anterior, desfazer, resultado,
                            tmp => File.WriteAllText(tmp, conteudo), conteudoTexto: conteudo);
                        break;
                    }

                    case PlanActionKind.PatchDgVoodooConf:
                    {
                        var perfil = DgVoodooConfigurator.ProfileFor(profile.Api);
                        var patched = DgVoodooConfigurator.Patch(File.ReadAllText(action.SourcePath!), perfil,
                            hardwareTnL: null);
                        if (!plan.Options.DgVoodooWatermark)
                            patched = DgVoodooConfigurator.DefinirChave(patched, "DirectX", "dgVoodooWatermark", "false");
                        Gravar(action.TargetPath!, manifest, anterior, desfazer, resultado,
                            tmp => File.WriteAllText(tmp, patched), conteudoTexto: patched);
                        var missing = DgVoodooConfigurator.MissingKeys(patched, perfil);
                        if (missing.Count > 0)
                            Aviso($"Chaves não encontradas no dgVoodoo.conf: {string.Join(", ", missing)}");
                        break;
                    }

                    case PlanActionKind.RegistryOverride:
                        AplicarOverride(manifest, desfazer);
                        break;
                }

                manifest.Save(exe);
            }

            // ---- Verificação final: o disco tem que bater com o manifesto.
            ct.ThrowIfCancellationRequested();
            Reportar("Validando a instalação", total - 2, total);
            var divergentes = new List<string>();
            foreach (var (caminho, rec) in manifest.Files)
            {
                var conf = rec.Conferir(caminho);
                if (conf != ConferenciaDeArquivo.Igual) divergentes.Add($"{caminho} ({conf})");
            }
            if (divergentes.Count > 0)
                throw new InvalidOperationException(
                    "A verificação final encontrou arquivos diferentes do esperado (antivírus ou outro programa mexeu durante a cópia): " +
                    string.Join("; ", divergentes.Take(5)));

            // ---- Limpeza dos temporários desta execução.
            Reportar("Finalizando e limpando arquivos temporários", total - 1, total);
            foreach (var d in desfazer.OfType<Desfazer.DevolverAnterior>().Where(d => d.ApagarGuardadoDepois))
            {
                try { if (File.Exists(d.Guardado)) File.Delete(d.Guardado); }
                catch (Exception ex) { Aviso($"Temporário não removido: {d.Guardado} ({ex.Message})"); }
            }
            LimparTemporarios(exe);

            manifest.Status = StatusDoManifesto.Concluida;
            manifest.Save(exe);
            Reportar("Concluído", total, total);
            _log($"Manifesto salvo em {manifest.Caminho}");
            return resultado;
        }
        catch (Exception ex)
        {
            bool cancelada = ex is OperationCanceledException;
            if (cancelada) _log("Operação cancelada pelo usuário. Desfazendo o que já tinha sido feito...");
            else if (_diario is not null) _diario.Erro($"Falha na etapa \"{etapa}\"", ex);
            else _log($"ERRO na etapa \"{etapa}\": {ex.Message}");

            Reportar("Desfazendo alterações (rollback)", total, total);
            var falhas = Rollback(desfazer);

            // O estado registrado tem que ser o estado real.
            try
            {
                if (falhas.Count == 0)
                {
                    if (manifestoAnteriorJson is not null && anterior is not null)
                        File.WriteAllText(anterior.Caminho, manifestoAnteriorJson);
                    else
                        File.Delete(manifest.Caminho);
                }
                else
                {
                    manifest.Status = StatusDoManifesto.InstalacaoIncompleta;
                    manifest.Save(exe);
                }
            }
            catch (Exception ex2) { Aviso($"Não consegui atualizar o manifesto depois do rollback: {ex2.Message}"); }

            LimparTemporarios(exe);

            var r = new ResultadoDaInstalacao
            {
                Sucesso = false,
                Cancelada = cancelada,
                Manifesto = falhas.Count == 0 ? anterior : manifest,
                Erro = cancelada ? "Cancelado pelo usuário." : ex.Message,
                EtapaDoErro = etapa,
                RollbackExecutado = true,
                FalhasDoRollback = falhas,
            };
            r.Avisos.AddRange(resultado.Avisos);
            return r;
        }
    }

    private static string RotuloDaEtapa(PlanAction a) => a.Kind switch
    {
        PlanActionKind.DeleteForbiddenFile => "Removendo arquivos que não devem ficar na pasta",
        PlanActionKind.CopyFile => "Copiando arquivos do mod",
        PlanActionKind.ExtractReShadeDll => "Extraindo o ReShade",
        PlanActionKind.WriteGeneratedFile => "Gerando configurações",
        PlanActionKind.PatchDgVoodooConf => "Ajustando o dgVoodoo",
        PlanActionKind.RegistryOverride => "Aplicando o override de assinatura no registro",
        _ => "Aplicando",
    };

    /// <summary>Todos os caminhos de arquivo que o plano vai gravar.</summary>
    public static IEnumerable<string> AlvosDoPlano(InstallPlan plan)
    {
        foreach (var a in plan.Actions)
        {
            if (a.TargetPath is null) continue;
            if (a.Kind == PlanActionKind.CopyFile && Directory.Exists(a.SourcePath))
            {
                IEnumerable<string> arquivos;
                try { arquivos = Directory.EnumerateFiles(a.SourcePath!, "*", SearchOption.AllDirectories); }
                catch { continue; }
                foreach (var f in arquivos)
                    yield return Path.Combine(a.TargetPath, Path.GetRelativePath(a.SourcePath!, f));
            }
            else
            {
                yield return a.TargetPath;
            }
        }
    }

    /// <param name="realDllPath">
    /// Só para o ini do stub do DxWrapper (d3d9.ini): o dgVoodoo encadeado que o RealDllPath
    /// deve apontar. Um ini que já era do usuário é preservado — só essa linha muda (o
    /// original vai para backup e a desinstalação devolve o arquivo dele).
    /// </param>
    private static string ConteudoGerado(string target, InstallPlan plan, string? realDllPath = null)
    {
        if (realDllPath is not null)
        {
            string? existente = null;
            try { if (File.Exists(target)) existente = File.ReadAllText(target); } catch { }
            return DxWrapperChain.GerarIni(existente, realDllPath);
        }
        var name = Path.GetFileName(target);
        return name.Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase)
               || name.Equals(ReFramework.ReShadeIni, StringComparison.OrdinalIgnoreCase)
            ? ReShadeConfigWriter.BuildReShadeIni(
                plan.Options.OverlayKey, plan.Options.OverlayCtrl, plan.Options.OverlayShift, plan.Options.OverlayAlt,
                feederUsed: plan.Profile.NeedsFeeder,
                // Hospedado no REFramework o ini fica longe da pasta do jogo: sem caminho
                // absoluto o ReShade procuraria shaders e addons dentro de plugins\.
                baseDir: null,
                renodxHooks: plan.Profile.HooksDoRenodx)
            : ReShadeConfigWriter.BuildPresetIni(plan.Options.MvProvider, feederUsed: plan.Profile.NeedsFeeder);
    }

    /// <summary>
    /// Grava um arquivo com segurança: prepara num temporário ao lado, decide o que fazer
    /// com o que já existe no destino (pular, guardar o anterior ou fazer backup do
    /// original) e só então move por cima.
    /// </summary>
    private void Gravar(
        string target, InstallManifest manifest, InstallManifest? anterior, List<Desfazer> desfazer,
        ResultadoDaInstalacao resultado, Action<string> preparar, string? identicoA = null, string? conteudoTexto = null)
    {
        EnsureDirFor(target, manifest, desfazer);

        if (File.Exists(target))
        {
            // Idempotência: igual ao que vamos gravar → não toca.
            bool igual = identicoA is not null
                ? FileRecord.MesmoConteudo(identicoA, target)
                : conteudoTexto is not null && ConteudoIgual(target, conteudoTexto);
            if (igual)
            {
                manifest.RegistrarGravado(target);
                resultado.JaEstavamIguais.Add(target);
                Tecnico($"Já estava igual: {target}");
                return;
            }

            var origem = _origens.TryGetValue(target, out var o) ? o : OrigemDoArquivo.DoJogoOuTerceiro;
            if (origem == OrigemDoArquivo.DoJogoOuTerceiro)
            {
                GuardarOriginal(target, manifest, desfazer, resultado);
            }
            else
            {
                // Arquivo nosso (desta ou de instalação antiga): guarda só para poder
                // desfazer ESTA execução; some no fim.
                var prev = target + Propriedade.PrevSuffix;
                if (File.Exists(prev)) File.Delete(prev);
                File.Move(target, prev);
                desfazer.Add(new Desfazer.DevolverAnterior(target, prev, ApagarGuardadoDepois: true));
                Tecnico($"Versão anterior do mod guardada temporariamente: {Path.GetFileName(prev)}");
            }
        }
        else
        {
            desfazer.Add(new Desfazer.ApagarAdicionado(target));
        }

        var tmp = target + Propriedade.TempSuffix;
        try
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            preparar(tmp);
            File.Move(tmp, target, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }

        manifest.RegistrarGravado(target);
        resultado.Gravados.Add(target);
        _log($"Gravado: {Path.GetFileName(target)} → {Path.GetDirectoryName(target)}");
    }

    private static bool ConteudoIgual(string path, string texto)
    {
        try { return string.Equals(File.ReadAllText(path), texto, StringComparison.Ordinal); }
        catch { return false; }
    }

    /// <summary>
    /// Backup do original — só quando o arquivo não é nosso e ainda não tem backup.
    /// Um backup existente é o original de verdade e NUNCA é refeito por cima.
    /// </summary>
    private void GuardarOriginal(string target, InstallManifest manifest, List<Desfazer> desfazer, ResultadoDaInstalacao resultado)
    {
        var backup = target + Propriedade.BackupSuffix;

        if (manifest.BackedUpFiles.TryGetValue(target, out var existente) && File.Exists(existente))
        {
            // Já guardado por uma instalação anterior: o que está no destino agora foi
            // trocado por alguém depois de nós; o original segue sendo o do backup.
            resultado.BackupsPreservados.Add(target);
            resultado.Avisos.Add($"{Path.GetFileName(target)} foi alterado depois da instalação anterior; " +
                                 "o backup do original foi preservado e é ele que volta ao desinstalar.");
            desfazer.Add(new Desfazer.DevolverAnterior(target, GuardarPrev(target), ApagarGuardadoDepois: true));
            return;
        }

        if (File.Exists(backup))
        {
            // Backup órfão (instalação antiga sem manifesto). É o original; adota.
            manifest.BackedUpFiles[target] = backup;
            try { manifest.Backups[target] = FileRecord.Capturar(backup); } catch { }
            resultado.BackupsPreservados.Add(target);
            _log($"Backup anterior encontrado e preservado: {Path.GetFileName(backup)}");
            desfazer.Add(new Desfazer.DevolverAnterior(target, GuardarPrev(target), ApagarGuardadoDepois: true));
            return;
        }

        // Backup novo. Se falhar, a operação inteira para: nunca sobrescrever sem cópia.
        File.Copy(target, backup, overwrite: false);
        manifest.BackedUpFiles[target] = backup;
        manifest.Backups[target] = FileRecord.Capturar(backup);
        resultado.BackupsCriados.Add(target);
        desfazer.Add(new Desfazer.DevolverAnterior(target, backup, ApagarGuardadoDepois: false));
        _log($"Backup do original: {Path.GetFileName(backup)}");

        static string GuardarPrev(string target)
        {
            var prev = target + Propriedade.PrevSuffix;
            if (File.Exists(prev)) File.Delete(prev);
            File.Move(target, prev);
            return prev;
        }
    }

    private void CopyDirectory(
        string sourceDir, string targetDir, InstallManifest manifest, InstallManifest? anterior,
        List<Desfazer> desfazer, ResultadoDaInstalacao resultado, CancellationToken ct)
    {
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
            manifest.AddedDirectories.Add(targetDir);
            desfazer.Add(new Desfazer.ApagarPasta(targetDir));
        }
        int n = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(sourceDir, file);
            var dst = Path.Combine(targetDir, rel);
            var f = file;
            Gravar(dst, manifest, anterior, desfazer, resultado, tmp => File.Copy(f, tmp, overwrite: true), identicoA: f);
            n++;
        }
        _log($"Pasta {Path.GetFileName(sourceDir)} → {targetDir} ({n} arquivos)");
    }

    private void RemoveForbidden(string path, InstallManifest manifest, List<Desfazer> desfazer)
    {
        if (!File.Exists(path)) return;
        var backup = path + Propriedade.BackupSuffix;
        if (File.Exists(backup)) File.Delete(backup);
        File.Move(path, backup);
        manifest.RemovedFiles[path] = backup;
        desfazer.Add(new Desfazer.DevolverRemovido(path, backup));
        _log($"Removido (backup guardado): {Path.GetFileName(path)}");
    }

    private void AplicarOverride(InstallManifest manifest, List<Desfazer> desfazer)
    {
        if (!OperatingSystem.IsWindows()) return;
        var status = SignatureOverride.Query();
        if (status.AllSet)
        {
            _log("Override de assinatura NGX já estava aplicado no registro; nada a fazer.");
            if (!manifest.RegistryOverrideApplied)
            {
                // Estava lá antes de nós (outra instalação ou à mão). Registramos que
                // existe, sem data — o checkpoint de reinício trata isso como "já aplicado".
                manifest.RegistryOverrideApplied = true;
            }
            return;
        }
        SignatureOverride.Enable();
        manifest.RegistryOverrideApplied = true;
        manifest.RegistryOverrideAppliedUtc = DateTime.UtcNow;
        desfazer.Add(new Desfazer.DesligarOverride());
        _log("Override de assinatura NGX aplicado no registro (o driver só lê na inicialização do Windows).");
    }

    private static void EnsureDirFor(string filePath, InstallManifest manifest, List<Desfazer> desfazer)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir) || Directory.Exists(dir)) return;
        Directory.CreateDirectory(dir);
        manifest.AddedDirectories.Add(dir);
        desfazer.Add(new Desfazer.ApagarPasta(dir));
    }

    /// <summary>Desfaz, na ordem inversa, tudo que esta execução fez. Devolve o que resistiu.</summary>
    private List<string> Rollback(List<Desfazer> desfazer)
    {
        var falhas = new List<string>();
        for (int i = desfazer.Count - 1; i >= 0; i--)
        {
            try
            {
                switch (desfazer[i])
                {
                    case Desfazer.ApagarAdicionado a:
                        if (File.Exists(a.Caminho)) { File.Delete(a.Caminho); _log($"Rollback: apagado {a.Caminho}"); }
                        break;
                    case Desfazer.DevolverAnterior d:
                        if (File.Exists(d.Guardado))
                        {
                            // Tanto o .dlss5prev quanto um backup criado NESTA execução voltam
                            // por cima e somem: a pasta fica exatamente como estava antes.
                            File.Move(d.Guardado, d.Caminho, overwrite: true);
                            _log($"Rollback: devolvido {d.Caminho}");
                        }
                        break;
                    case Desfazer.DevolverRemovido r:
                        if (File.Exists(r.Backup)) { File.Move(r.Backup, r.Caminho, overwrite: true); _log($"Rollback: devolvido {r.Caminho}"); }
                        break;
                    case Desfazer.ApagarPasta p:
                        if (Directory.Exists(p.Pasta) && !Directory.EnumerateFileSystemEntries(p.Pasta).Any())
                            Directory.Delete(p.Pasta);
                        break;
                    case Desfazer.DesligarOverride:
                        if (OperatingSystem.IsWindows()) { SignatureOverride.Disable(); _log("Rollback: override do registro removido"); }
                        break;
                }
            }
            catch (Exception ex)
            {
                var alvo = desfazer[i] switch
                {
                    Desfazer.ApagarAdicionado a => a.Caminho,
                    Desfazer.DevolverAnterior d => d.Caminho,
                    Desfazer.DevolverRemovido r => r.Caminho,
                    Desfazer.ApagarPasta p => p.Pasta,
                    _ => "registro",
                };
                falhas.Add($"{alvo} — {ex.Message}");
                Aviso($"Rollback não conseguiu desfazer {alvo}: {ex.Message}");
            }
        }
        if (falhas.Count == 0) _log("Rollback concluído: a pasta voltou ao estado anterior.");
        return falhas;
    }

    /// <summary>Apaga temporários (.dlss5tmp / .dlss5prev) que alguma execução interrompida possa ter deixado.</summary>
    public void LimparTemporarios(string? pasta)
    {
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) return;
        foreach (var sufixo in new[] { Propriedade.TempSuffix, Propriedade.PrevSuffix })
        {
            IEnumerable<string> lixo;
            try { lixo = Directory.EnumerateFiles(pasta, "*" + sufixo, SearchOption.AllDirectories).ToList(); }
            catch { continue; }
            foreach (var f in lixo)
            {
                try { File.Delete(f); Tecnico($"Temporário removido: {f}"); }
                catch (Exception ex) { Aviso($"Temporário não removido: {f} ({ex.Message})"); }
            }
        }
    }

    // ---------------------------------------------------------------- conferir

    /// <summary>Tudo que a instalação pode ter deixado na pasta do exe (nomes).</summary>
    private static readonly string[] NossosArquivos =
    {
        "dxgi.dll", "opengl32.dll", "ReShade.ini", "ReShade.log", "ReShadePreset.ini",
        "ReShade64.json", "ReShade32.json", "ReShade64_XR.json", "ReShade32_XR.json",
        "renodx-dlss5.addon64", "nvngx_dlssnr.dll",
        "dlss5-feed.addon64", "dlss5-feed.addon32", "dlss5-feed.cfg", "dlss5-feed.log",
        "D3D9.dll", "D3D8.dll", "dgVoodoo.conf", "dgVoodooCpl.exe",
        "dgVoodoo_D3D9.dll", "dgVoodoo_D3D8.dll",
    };

    /// <summary>
    /// Confere o que ainda está na pasta depois de reverter. Só entra na lista o que dá
    /// para afirmar que é do mod: um dxgi.dll do próprio jogo, por exemplo, fica de fora.
    /// </summary>
    public IReadOnlyList<string> ConferirSobras(string? exeFolder)
    {
        var sobras = new List<string>();
        if (string.IsNullOrWhiteSpace(exeFolder) || !Directory.Exists(exeFolder)) return sobras;

        foreach (var nome in NossosArquivos)
        {
            var caminho = Path.Combine(exeFolder, nome);
            if (!File.Exists(caminho)) continue;
            bool generico = Propriedade.PrecisamDeProva.Any(p => p.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase))
                            || Propriedade.PrecisamDeProvaEEscolta.Any(p => p.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase))
                            || Propriedade.DgVoodooComEscolta.Any(p => p.Equals(nome, StringComparison.OrdinalIgnoreCase));
            if (!generico || Propriedade.EhNossoPorHeuristica(caminho)) sobras.Add(caminho);
        }

        // O transplante que resistiu (arquivo em uso) é sobra como qualquer outra.
        var transplante = Path.Combine(exeFolder, "nvngx_dlss.dll");
        if (TransplanteDlss.EhDoKit(transplante, NvngxDlssDoKit)) sobras.Add(transplante);

        // Modo REFramework: o hospedeiro (só se for o do kit) e o ReShade dentro dele.
        var dinput = ReFramework.CaminhoDinput8(exeFolder);
        if (ReFramework.EhDoKit(dinput, ReFrameworkDoKit)) sobras.Add(dinput);
        foreach (var caminho in new[] { ReFramework.CaminhoPlugin(exeFolder), ReFramework.CaminhoIni(exeFolder) })
            if (File.Exists(caminho)) sobras.Add(caminho);

        // Um ini do DxWrapper ainda apontando para o dgVoodoo é sobra perigosa: com o
        // dgVoodoo fora, o DxWrapper tentaria carregar um arquivo que não existe.
        sobras.AddRange(InisEncadeadosEm(exeFolder));

        foreach (var pasta in Propriedade.PastasNossas)
        {
            var caminho = Path.Combine(exeFolder, pasta);
            if (Directory.Exists(caminho)) sobras.Add(caminho + Path.DirectorySeparatorChar);
        }
        try
        {
            sobras.AddRange(Directory.EnumerateFiles(exeFolder, "*" + BackupSuffix, SearchOption.AllDirectories));
        }
        catch
        {
            // sem permissão de leitura: o que já foi listado basta
        }
        return sobras;
    }

    /// <summary>
    /// Devolve ao lugar todo arquivo *.dlss5bak encontrado na pasta e diz quais voltaram.
    /// Um arquivo restaurado é do JOGO outra vez, e a limpeza não pode apagá-lo depois.
    /// </summary>
    public IReadOnlyList<string> RestaurarBackupsOrfaos(string? pasta)
    {
        var restaurados = new List<string>();
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) return restaurados;

        List<string> backups;
        try
        {
            backups = Directory.EnumerateFiles(pasta, "*" + BackupSuffix, SearchOption.AllDirectories).ToList();
        }
        catch
        {
            return restaurados;
        }

        foreach (var backup in backups)
        {
            var original = backup[..^BackupSuffix.Length];
            try
            {
                if (!File.Exists(backup)) continue;
                if (new FileInfo(backup).Length == 0)
                {
                    Aviso($"Backup vazio ignorado (não é um original válido): {backup}");
                    continue;
                }
                File.Move(backup, original, overwrite: true);
                restaurados.Add(original);
                _log($"Devolvido ao lugar: {original}");
            }
            catch (Exception ex)
            {
                Aviso($"não consegui devolver {original}: {ex.Message}");
            }
        }
        return restaurados;
    }

    /// <summary>Apaga os arquivos que só aparecem depois que o jogo roda uma vez.</summary>
    public void LimparRestosDeExecucao(string? exeFolder)
    {
        if (string.IsNullOrWhiteSpace(exeFolder) || !Directory.Exists(exeFolder)) return;

        foreach (var nome in Propriedade.RestosDeExecucao)
        {
            foreach (var alvo in new[]
                     {
                         Path.Combine(exeFolder, nome),
                         Path.Combine(exeFolder, "host64", nome),
                     })
            {
                try
                {
                    if (!File.Exists(alvo)) continue;
                    File.Delete(alvo);
                    _log($"Apagado (gerado ao rodar): {alvo}");
                }
                catch (Exception ex)
                {
                    Aviso($"{alvo}: {ex.Message}");
                }
            }
        }

        foreach (var pasta in Propriedade.PastasNossas)
        {
            try
            {
                var alvo = Path.Combine(exeFolder, pasta);
                if (Directory.Exists(alvo) && !Directory.EnumerateFileSystemEntries(alvo).Any())
                    Directory.Delete(alvo);
            }
            catch
            {
                // pasta com conteúdo do usuário: fica
            }
        }
    }

    // ---------------------------------------------------------------- reverter

    public ResultadoDaReversao Revert(InstallManifest manifest, bool removeRegistryOverride) =>
        Revert(manifest, removeRegistryOverride, CancellationToken.None, null);

    /// <summary>
    /// Desfaz a instalação a partir do manifesto. Remove só o que consegue confirmar que
    /// é do mod, restaura só a partir de backup íntegro, preserva o resto e diz
    /// exatamente o que aconteceu com cada arquivo.
    /// </summary>
    public ResultadoDaReversao Revert(
        InstallManifest manifest, bool removeRegistryOverride, CancellationToken ct,
        IProgress<ProgressoDaOperacao>? progresso)
    {
        var r = new ResultadoDaReversao();
        var exe = manifest.ExeFolder;
        int total = 7;
        void Reportar(string etapa, int n, string? detalhe = null) =>
            progresso?.Report(new ProgressoDaOperacao(etapa, n, total, detalhe));

        Reportar("Verificando o jogo", 0);
        var rodando = Preflight.JogoRodando(manifest.RealExePath);
        var bloqueios = new List<Bloqueio>();
        if (rodando is not null)
            bloqueios.Add(new Bloqueio("O jogo está aberto",
                $"O processo {rodando}.exe está em execução; arquivos em uso não saem.",
                "Feche o jogo (e o cliente da loja, se ele mantiver o jogo aberto) e tente de novo."));
        if (Directory.Exists(exe) && !Preflight.PastaGravavel(exe, out var motivo))
            bloqueios.Add(new Bloqueio("Sem permissão para gravar na pasta do jogo", $"{exe}: {motivo}",
                "Execute como administrador ou ajuste as permissões da pasta."));
        if (bloqueios.Count > 0)
        {
            r.Bloqueios = bloqueios;
            r.Erro = "A desinstalação não começou: há impedimentos.";
            foreach (var b in bloqueios) _log($"Bloqueado: {b.Titulo} — {b.Detalhe}");
            return r;
        }

        try
        {
            manifest.Status = StatusDoManifesto.ReversaoEmAndamento;
            try { manifest.Save(exe); } catch (Exception ex) { Aviso($"Manifesto não atualizado: {ex.Message}"); }

            // 1. Remover o que gravamos.
            Reportar("Removendo componentes do mod", 1);
            var gravados = manifest.ArquivosGravados.ToList();
            foreach (var file in gravados)
            {
                ct.ThrowIfCancellationRequested();
                if (manifest.BackedUpFiles.ContainsKey(file)) continue;   // tratado no passo 2
                RemoverSeForNosso(file, manifest, r);
            }

            // 2. Restaurar os originais.
            Reportar("Restaurando arquivos originais", 2);
            foreach (var (original, backup) in manifest.BackedUpFiles.ToList())
            {
                ct.ThrowIfCancellationRequested();
                RestaurarOriginal(original, backup, manifest, r);
            }

            // 3. Devolver os arquivos proibidos que tiramos.
            foreach (var (original, backup) in manifest.RemovedFiles.ToList())
            {
                try
                {
                    if (!File.Exists(backup)) continue;
                    File.Move(backup, original, overwrite: true);
                    manifest.RemovedFiles.Remove(original);
                    r.Restaurados.Add(original);
                    _log($"Devolvido: {original}");
                }
                catch (Exception ex) { r.Falhas.Add($"{original} — {ex.Message}"); Aviso($"{original}: {ex.Message}"); }
            }

            // 4. Restos que o ReShade/Feeder criam ao rodar e backups órfãos.
            Reportar("Limpando arquivos gerados pelo mod", 3);
            LimparRestosDeExecucao(exe);
            foreach (var devolvido in RestaurarBackupsOrfaos(exe)) r.Restaurados.Add(devolvido);
            if (!string.Equals(exe, manifest.GameFolder, StringComparison.OrdinalIgnoreCase))
                foreach (var devolvido in RestaurarBackupsOrfaos(manifest.GameFolder)) r.Restaurados.Add(devolvido);
            LimparTemporarios(exe);

            // O transplante de instalação antiga não consta em manifesto nenhum: se o
            // nvngx_dlss.dll que ficou é byte a byte o do kit, ele NÃO é do jogo — sai
            // agora, para a verificação de integridade da Steam poder repor o original.
            // Depois dos backups: um original recém-devolvido tem bytes diferentes e fica.
            if (RemoverTransplante(exe))
            {
                r.Removidos.Add(Path.Combine(exe, "nvngx_dlss.dll"));
                r.NaoRestaurados.Add(Path.Combine(exe, "nvngx_dlss.dll") + " — era o DO KIT (transplante de instalação antiga); o original do jogo não existe mais");
            }

            // Ini do DxWrapper que encadeava ao dgVoodoo: com o dgVoodoo fora, um
            // RealDllPath pendurado faria o jogo voltar a não abrir, por culpa nossa.
            foreach (var pasta in new[] { exe, manifest.RendererFolder }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var ini in InisEncadeadosEm(pasta!).ToList())
                    if (DesencadearDxWrapper(ini)) r.Removidos.Add(ini);

            // 5. Pastas que criamos, se ficaram vazias.
            foreach (var dir in manifest.AddedDirectories.OrderByDescending(d => d.Length).ToList())
            {
                try
                {
                    if (!Directory.Exists(dir)) { manifest.AddedDirectories.Remove(dir); continue; }
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                        manifest.AddedDirectories.Remove(dir);
                        _log($"Pasta removida: {dir}");
                    }
                    else
                    {
                        r.Preservados.Add($"{dir}{Path.DirectorySeparatorChar} — a pasta tem arquivos que não são do mod");
                    }
                }
                catch (Exception ex) { r.Falhas.Add($"{dir} — {ex.Message}"); }
            }

            // 6. Registro.
            Reportar("Desfazendo o override do registro", 4);
            if (removeRegistryOverride && manifest.RegistryOverrideApplied && OperatingSystem.IsWindows())
            {
                SignatureOverride.Disable();
                r.OverrideRemovido = !SignatureOverride.Query().Entries.Any(e => e.Set);
                if (r.OverrideRemovido) _log("Override de assinatura removido do registro (o driver relê na próxima inicialização).");
                else r.Falhas.Add("registro — o override não pôde ser removido de todas as chaves");
                manifest.RegistryOverrideApplied = !r.OverrideRemovido;
            }

            // 7. Conferência final.
            Reportar("Conferindo o resultado", 5);
            // Um arquivo devolvido do backup é do jogo/usuário de novo, mesmo que tenha
            // nome de ReShade — não é sobra.
            var sobras = ConferirSobras(exe)
                .Where(s => !r.Preservados.Any(p => p.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                .Where(s => !r.Restaurados.Contains(s, StringComparer.OrdinalIgnoreCase))
                .ToList();
            r.Sobras.AddRange(sobras);

            bool limpo = sobras.Count == 0 && r.Falhas.Count == 0;
            if (limpo)
            {
                try { File.Delete(manifest.Caminho); r.ManifestoRemovido = true; }
                catch (Exception ex) { r.Falhas.Add($"{manifest.Caminho} — {ex.Message}"); }
            }
            else
            {
                manifest.Status = StatusDoManifesto.ReversaoIncompleta;
                try { manifest.Save(exe); } catch (Exception ex) { Aviso($"Manifesto não atualizado: {ex.Message}"); }
            }

            r.Sucesso = limpo && r.ManifestoRemovido;
            Reportar(r.Sucesso ? "Concluído" : "Concluído com pendências", total);

            _log("");
            _log($"Removidos: {r.Removidos.Count}. Restaurados: {r.Restaurados.Count}. Preservados: {r.Preservados.Count}.");
            if (r.NaoRestaurados.Count > 0)
            {
                _log("ATENÇÃO — originais que NÃO puderam ser restaurados:");
                foreach (var n in r.NaoRestaurados) _log("   " + n);
            }
            if (sobras.Count > 0)
            {
                _log("ATENÇÃO: estes arquivos NÃO foram removidos:");
                foreach (var f in sobras) _log("   " + f);
                _log("Feche o jogo e a Steam e tente de novo, ou apague à mão.");
            }
            else
            {
                _log("Conferido: nenhum arquivo do mod sobrou na pasta.");
            }
            return r;
        }
        catch (OperationCanceledException)
        {
            manifest.Status = StatusDoManifesto.ReversaoIncompleta;
            try { manifest.Save(exe); } catch { }
            r.Erro = "Cancelado pelo usuário. O que já tinha sido removido continua removido; use Desinstalar de novo para terminar.";
            _log(r.Erro);
            return r;
        }
        catch (Exception ex)
        {
            manifest.Status = StatusDoManifesto.ReversaoIncompleta;
            try { manifest.Save(exe); } catch { }
            r.Erro = ex.Message;
            if (_diario is not null) _diario.Erro("Falha na desinstalação", ex); else _log("ERRO: " + ex.Message);
            return r;
        }
    }

    private void RemoverSeForNosso(string file, InstallManifest manifest, ResultadoDaReversao r)
    {
        if (!File.Exists(file))
        {
            manifest.AddedFiles.Remove(file);
            manifest.Files.Remove(file);
            return;
        }
        var origem = Propriedade.Classificar(file, manifest);
        if (origem == OrigemDoArquivo.DoJogoOuTerceiro)
        {
            r.Preservados.Add($"{file} — o conteúdo não é mais o que o mod gravou (atualização do jogo ou outro programa); preservado");
            Aviso($"Preservado (alterado por outro programa): {file}");
            manifest.AddedFiles.Remove(file);
            manifest.Files.Remove(file);
            return;
        }
        try
        {
            File.Delete(file);
            manifest.AddedFiles.Remove(file);
            manifest.Files.Remove(file);
            r.Removidos.Add(file);
            _log($"Apagado: {file}");
        }
        catch (Exception ex)
        {
            r.Falhas.Add($"{file} — {ex.Message}");
            Aviso($"{file}: {ex.Message}");
        }
    }

    private void RestaurarOriginal(string original, string backup, InstallManifest manifest, ResultadoDaReversao r)
    {
        var conf = manifest.ConferirBackup(original);
        bool atualEhNosso = !File.Exists(original)
                            || Propriedade.Classificar(original, manifest) != OrigemDoArquivo.DoJogoOuTerceiro;

        if (conf == ConferenciaDeArquivo.Igual)
        {
            if (!atualEhNosso)
            {
                r.Preservados.Add($"{original} — foi trocado por outro programa depois da instalação; mantido, e o backup ficou em {Path.GetFileName(backup)}");
                Aviso($"Preservado (não é mais o arquivo do mod): {original}");
                return;
            }
            try
            {
                File.Copy(backup, original, overwrite: true);
                File.Delete(backup);
                manifest.BackedUpFiles.Remove(original);
                manifest.Backups.Remove(original);
                manifest.Files.Remove(original);
                r.Restaurados.Add(original);
                _log($"Restaurado: {original}");
            }
            catch (Exception ex)
            {
                r.Falhas.Add($"{original} — {ex.Message}");
                Aviso($"{original}: {ex.Message}");
            }
            return;
        }

        // Backup ausente ou inválido: não fingir que restaurou.
        var motivo = conf switch
        {
            ConferenciaDeArquivo.Ausente => "o backup não existe mais",
            ConferenciaDeArquivo.Diferente => "o backup foi alterado depois de criado e não é confiável",
            _ => "o backup não pôde ser lido",
        };
        if (atualEhNosso && File.Exists(original))
        {
            try
            {
                File.Delete(original);
                r.Removidos.Add(original);
                _log($"Apagado (era do mod): {original}");
            }
            catch (Exception ex) { r.Falhas.Add($"{original} — {ex.Message}"); }
        }
        r.NaoRestaurados.Add($"{original} — {motivo}");
        Aviso($"Original não restaurado ({motivo}): {original}");
        manifest.BackedUpFiles.Remove(original);
        manifest.Backups.Remove(original);
        manifest.Files.Remove(original);
    }
}
