using System.Runtime.Versioning;
using Dlss5.Core;

namespace Dlss5.App;

/// <summary>Tela do plano: o que será feito, impedimentos, avisos e conflitos — antes de tocar em qualquer arquivo.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainForm
{
    private Panel _pPlano = new();
    private readonly Label _lblResumoDoPlano = new();
    private readonly TextBox _txtBlockers = new();
    private readonly CheckBox _chkConflitos = new();
    private readonly ListBox _lstPlan = new();

    private void BuildPlano()
    {
        _pPlano = new Panel { Dock = DockStyle.Fill };

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _lblResumoDoPlano.AutoSize = true;
        _lblResumoDoPlano.Dock = DockStyle.Fill;
        _lblResumoDoPlano.Font = Ui.BoldFont;
        _lblResumoDoPlano.Margin = new Padding(0, 0, 0, 8);

        Ui.StyleReadOnlyBox(_txtBlockers);
        _txtBlockers.Dock = DockStyle.Fill;
        _txtBlockers.Height = 150;
        _txtBlockers.MinimumSize = new Size(0, 60);
        _txtBlockers.Margin = new Padding(0, 0, 0, 8);
        _txtBlockers.Visible = false;

        _chkConflitos.Text = Textos.ConfirmarConflitos;
        _chkConflitos.AutoSize = true;
        _chkConflitos.Visible = false;
        _chkConflitos.Margin = new Padding(0, 0, 0, 8);
        _chkConflitos.CheckedChanged += (_, _) => AtualizarRodape();

        _lstPlan.Dock = DockStyle.Fill;
        _lstPlan.IntegralHeight = false;
        _lstPlan.BorderStyle = BorderStyle.FixedSingle;
        _lstPlan.BackColor = Ui.Card;
        _lstPlan.ForeColor = Ui.Ink;
        _lstPlan.Font = Ui.BodyFont;
        _lstPlan.HorizontalScrollbar = true;
        _lstPlan.Margin = new Padding(0);
        _lstPlan.AccessibleName = "Ações do plano";

        t.Controls.Add(_lblResumoDoPlano, 0, 0);
        t.Controls.Add(_txtBlockers, 0, 1);
        t.Controls.Add(_chkConflitos, 0, 2);
        t.Controls.Add(_lstPlan, 0, 3);
        _pPlano.Controls.Add(t);
    }

    private bool PlanoPodeRodar() =>
        _plan is not null && _plan.CanRun && (_plan.Conflitos.Count == 0 || _chkConflitos.Checked);

    private void GerarPlano()
    {
        if (_profile is null || _kit is null) { Aviso("Faça a detecção primeiro."); return; }
        SyncProfileFromUi();

        using var etapa = _diario.Etapa("Geração do plano");
        _plan = InstallPlanBuilder.Build(_profile, _kit, _options);
        _diario.Tecnico($"Plano: {_plan.Actions.Count} ações, {_plan.Blockers.Count} impedimentos, {_plan.Warnings.Count} avisos, {_plan.Conflitos.Count} conflitos");

        _lstPlan.Items.Clear();
        foreach (var a in _plan.Actions) _lstPlan.Items.Add(Simbolo(a.Kind) + "  " + a.Description);

        var modo = _fluxo switch
        {
            Fluxo.Atualizar => "Atualizar/reconfigurar",
            Fluxo.Reparar => "Reparar",
            _ => "Instalar",
        };
        _lblResumoDoPlano.Text = _plan.CanRun
            ? $"{modo}: {_plan.ResumoCurto()} Arquivos já iguais são pulados; originais substituídos ganham backup .dlss5bak; qualquer falha desfaz tudo."
            : $"{modo}: há impedimentos — nada será alterado até resolvê-los.";

        var lines = new List<string>();
        if (_plan.Blockers.Count > 0)
        {
            lines.Add("IMPEDIMENTOS (resolva antes de instalar):");
            lines.AddRange(_plan.Blockers.Select(b => "  ✖ " + b));
        }
        if (_plan.Conflitos.Count > 0)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add("CONFLITOS — arquivos que já existem e não são deste programa:");
            lines.AddRange(_plan.Conflitos.Select(c => "  ⚠ " + c));
        }
        if (_plan.Warnings.Count > 0)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add("AVISOS (não impedem instalar):");
            lines.AddRange(_plan.Warnings.Select(w => "  ℹ " + w));
        }
        _txtBlockers.Visible = lines.Count > 0;
        _txtBlockers.Lines = lines.ToArray();
        _txtBlockers.ForeColor = _plan.Blockers.Count > 0 ? Ui.Bad : Ui.Ink;

        _chkConflitos.Visible = _plan.Conflitos.Count > 0;
        _chkConflitos.Checked = false;

        SalvarPreferencias();

        MostrarTela(Tela.Plano);
        Status(_plan.CanRun
            ? $"{_plan.Actions.Count} ações prontas. Nada foi alterado ainda."
            : "Resolva os impedimentos antes de instalar.");
    }

    private static string Simbolo(PlanActionKind k) => k switch
    {
        PlanActionKind.CopyFile => "📄",
        PlanActionKind.ExtractReShadeDll => "📦",
        PlanActionKind.WriteGeneratedFile => "📝",
        PlanActionKind.PatchDgVoodooConf => "🔧",
        PlanActionKind.DeleteForbiddenFile => "🗑",
        PlanActionKind.RegistryOverride => "🔑",
        _ => "•",
    };

    /// <summary>Confirmação específica e execução da instalação/atualização/reparo com progresso e rollback.</summary>
    private async Task ExecutarInstalacaoAsync()
    {
        if (_plan is null || _kit is null || !PlanoPodeRodar()) { Aviso("O plano não pode ser executado ainda."); return; }
        var plan = _plan;
        var kit = _kit;

        var rodando = Preflight.JogoRodando(plan.Profile.RealExePath);
        if (rodando is not null) { Aviso("O jogo está aberto", Textos.JogoAberto(rodando)); return; }

        var verbo = _fluxo switch { Fluxo.Atualizar => "Atualizar", Fluxo.Reparar => "Reparar", _ => "Instalar" };
        var resumo = $"{plan.ResumoCurto()}\r\n\r\nPasta: {plan.Profile.ExeFolder}\r\n" +
                     (plan.Conflitos.Count > 0 ? $"\r\nConflitos aceitos: {plan.Conflitos.Count} arquivo(s) existente(s) serão substituídos com backup.\r\n" : "") +
                     (plan.Actions.Any(a => a.Kind == PlanActionKind.RegistryOverride)
                         ? "\r\nRegistro: o override de assinatura NGX será aplicado (global; faz efeito após reiniciar o Windows).\r\n" : "") +
                     "\r\nSe qualquer etapa falhar, tudo que foi alterado é desfeito automaticamente.";
        if (!Dialogos.Confirmar(this, $"{verbo} DLSS 5", $"{verbo} o DLSS 5 em \"{Path.GetFileName(plan.Profile.GameFolder.TrimEnd(Path.DirectorySeparatorChar))}\"?", resumo, verbo))
            return;

        MostrarTela(Tela.Execucao);
        var nome = _fluxo switch { Fluxo.Atualizar => "Atualização", Fluxo.Reparar => "Reparo", _ => "Instalação" };
        var r = await RodarOperacaoAsync(nome, cancelavel: true,
            (ct, progresso) => new InstallerEngine(_diario).Execute(plan, kit, ct, progresso));

        _manifest = r.Manifesto;
        if (r.Sucesso)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{nome} concluída.");
            sb.AppendLine($"Gravados: {r.Gravados.Count}. Já estavam iguais (pulados): {r.JaEstavamIguais.Count}. Backups novos: {r.BackupsCriados.Count}. Backups anteriores preservados: {r.BackupsPreservados.Count}.");
            foreach (var a in r.Avisos) sb.AppendLine("⚠ " + a);
            ConcluirExecucao(true, sb.ToString());
            Status($"{nome} concluída. Verificando…");
            MostrarTela(Tela.Verificacao);
            RunVerification();
        }
        else if (r.Bloqueios.Count > 0)
        {
            var texto = string.Join("\r\n\r\n", r.Bloqueios.Select(b => $"✖ {b.Titulo}\r\n{b.Detalhe}\r\nO que fazer: {b.OQueFazer}"));
            ConcluirExecucao(false, "A operação não começou: nada foi alterado.\r\n\r\n" + texto);
            Dialogos.Mostrar(this, Textos.TituloDoPrograma, "A operação não começou — nada foi alterado", texto, CheckStatusKind.Warn);
        }
        else
        {
            var texto = (r.Cancelada ? "Cancelada pelo usuário" : $"Falha na etapa \"{r.EtapaDoErro}\"") + ".\r\n\r\n" +
                        (r.Cancelada ? "" : "O que aconteceu: " + r.Erro + "\r\n\r\n") +
                        (r.FalhasDoRollback.Count == 0
                            ? "Tudo que tinha sido alterado foi desfeito: a pasta do jogo está como antes."
                            : "O rollback NÃO conseguiu desfazer tudo:\r\n" + string.Join("\r\n", r.FalhasDoRollback.Select(f => "  " + f)) +
                              "\r\n\r\nA instalação ficou registrada como incompleta. Na tela inicial use \"Reparar instalação\" ou \"Desinstalar\" depois de fechar o que está usando os arquivos.") +
                        "\r\n\r\nO que você pode fazer: feche o jogo, o cliente da loja e overlays; confira o antivírus (ele pode ter bloqueado um arquivo do kit); tente de novo." +
                        "\r\n\r\nDetalhes técnicos no log: " + (_diario.ArquivoAtual ?? "(só em memória)");
            ConcluirExecucao(false, texto);
            Dialogos.Mostrar(this, Textos.TituloDoPrograma, r.Cancelada ? "Operação cancelada e desfeita" : "A instalação falhou e foi desfeita",
                texto, r.Cancelada ? CheckStatusKind.Warn : CheckStatusKind.Bad, null,
                new Dialogos.Opcao("OK", DialogResult.OK, Principal: true),
                new Dialogos.Opcao(Textos.BotaoAbrirLogs, DialogResult.Retry));
        }
    }
}
