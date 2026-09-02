using System.Diagnostics;
using System.Runtime.Versioning;
using Dlss5.Core;

namespace Dlss5.App;

/// <summary>
/// Verificação: checkpoints por arquivo/registro/log, roteiro de passos manuais e as
/// ferramentas de diagnóstico (isolar a causa, trocar placa do dgVoodoo, painel).
/// Também a tela de resultado das desinstalações.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainForm
{
    private Panel _pVerificacao = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _txtGuide = new();
    private readonly Label _lblResumoVerificacao = new();
    private readonly FlowLayoutPanel _barraDgVoodoo = Ui.Fila();
    private readonly ComboBox _cboPlaca = new();
    private readonly CheckBox _chkTnL = new();
    private readonly Button _btnRenodx = Ui.Secondary("Testar sem o RenoDX");
    private readonly Button _btnFeeder = Ui.Secondary("Testar sem o Feeder");
    private readonly Button _btnSoReShade = Ui.Secondary("Testar só o ReShade");
    // O runtime do DLSS 5 pelo hash: quando o do kit é remendo/original-só-RTX-50, este
    // botão baixa o build do ShortFuse que o RHI instala e o põe no kit, conferido.
    private readonly Button _btnRuntime = Ui.Secondary("Baixar o runtime do RHI (166 MB)");
    // Caminho direto: a chave EnableHooks do RenoDX, trocada no ReShade.ini sem reinstalar.
    private readonly FlowLayoutPanel _barraHooks = Ui.Fila();
    private readonly ComboBox _cboHooks = new();
    private EstadoIsolamento _isolamento = EstadoIsolamento.Tudo;
    private bool? _abriuSemDgVoodoo;
    private bool? _abriuSemReShade;

    private Panel _pResultado = new();
    private readonly Label _lblResultadoTitulo = new();
    private readonly TextBox _txtResultado = new();

    private void BuildVerificacao()
    {
        _pVerificacao = new Panel { Dock = DockStyle.Fill };

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblResumoVerificacao.AutoSize = true;
        _lblResumoVerificacao.Dock = DockStyle.Fill;
        _lblResumoVerificacao.Font = Ui.BoldFont;
        _lblResumoVerificacao.Margin = new Padding(0, 0, 0, 8);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 100,
            Panel2MinSize = 100,
            Margin = new Padding(0, 0, 0, 8),
        };
        split.HandleCreated += (_, _) =>
        {
            try { split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Height * 55 / 100); }
            catch (InvalidOperationException) { }
            catch (ArgumentOutOfRangeException) { }
        };

        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Ui.Card;
        _grid.BorderStyle = BorderStyle.None;
        _grid.GridColor = Ui.Line;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 245, 249);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Muted;
        _grid.ColumnHeadersDefaultCellStyle.Font = Ui.BoldFont;
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 4, 0, 4);
        _grid.DefaultCellStyle.Font = Ui.BodyFont;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.DefaultCellStyle.Padding = new Padding(6, 6, 6, 6);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(233, 241, 251);
        _grid.DefaultCellStyle.SelectionForeColor = Ui.Ink;
        _grid.AccessibleName = "Resultado da verificação";

        _grid.ColumnCount = 4;
        _grid.Columns[0].HeaderText = "Estado";
        _grid.Columns[0].FillWeight = 12;
        _grid.Columns[1].HeaderText = "Verificação";
        _grid.Columns[1].FillWeight = 25;
        _grid.Columns[2].HeaderText = "Detalhe";
        _grid.Columns[2].FillWeight = 35;
        _grid.Columns[3].HeaderText = "Como corrigir";
        _grid.Columns[3].FillWeight = 28;
        // Duplo clique abre a linha inteira num diálogo legível (texto longo).
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var r = _grid.Rows[e.RowIndex];
            Dialogos.Informar(this, "Verificação", $"{r.Cells[0].Value} — {r.Cells[1].Value}",
                $"{r.Cells[2].Value}\r\n\r\nComo corrigir:\r\n{r.Cells[3].Value}");
        };

        Ui.StyleReadOnlyBox(_txtGuide);
        _txtGuide.Dock = DockStyle.Fill;

        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_txtGuide);

        var bar = Ui.Fila();
        bar.Controls.Add(Botao("Verificar de novo", (_, _) => RunVerification()));
        bar.Controls.Add(Botao("Abrir pasta do jogo", (_, _) => OpenFolder(_profile?.ExeFolder)));
        bar.Controls.Add(Botao("Abrir o jogo", (_, _) => LaunchGame()));
        bar.Controls.Add(Botao("Isolar a causa…", (_, _) => IsolarCausa()));
        _btnRenodx.Margin = new Padding(0, 4, 8, 4);
        _btnRenodx.Click += (_, _) => TestarSemRenodx();
        bar.Controls.Add(_btnRenodx);

        // O Feeder é o suspeito número um quando o jogo NÃO ABRE, e até agora não havia
        // como desligar só ele: a bisseção pulava direto para o ReShade inteiro.
        _btnFeeder.Margin = new Padding(0, 4, 8, 4);
        _btnFeeder.Click += (_, _) => TestarSemFeeder();
        bar.Controls.Add(_btnFeeder);

        // Os dois addons de uma vez: sem este degrau, testar um a um nunca inocenta os dois.
        _btnSoReShade.Margin = new Padding(0, 4, 8, 4);
        _btnSoReShade.Click += (_, _) => TestarSoOReShade();
        bar.Controls.Add(_btnSoReShade);
        _btnRuntime.Margin = new Padding(0, 4, 8, 4);
        _btnRuntime.Click += (_, _) => _ = BaixarRuntimeDoRhiAsync();
        bar.Controls.Add(_btnRuntime);
        bar.Controls.Add(Botao("Reiniciar o PC (opcional)…", (_, _) => ReiniciarSeOUsuarioQuiser()));
        bar.Controls.Add(Botao(Textos.BotaoAbrirLogs, (_, _) => AbrirPastaDeLogs()));
        bar.Controls.Add(Botao(Textos.BotaoExportarDiagnostico, (_, _) => ExportarDiagnostico()));

        // Ferramentas do dgVoodoo (só rota C): trocar a placa que ele finge ser.
        var lblDg = new Label { Text = "dgVoodoo finge ser:", AutoSize = true, Margin = new Padding(0, 10, 8, 0) };
        _cboPlaca.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboPlaca.Width = 260;
        _cboPlaca.Margin = new Padding(0, 4, 8, 4);
        foreach (var (rotulo, _) in DgVoodooConfigurator.Placas) _cboPlaca.Items.Add(rotulo);
        _cboPlaca.SelectedIndex = 0;
        _chkTnL.Text = "T&&L por hardware";
        _chkTnL.Checked = true;
        _chkTnL.AutoSize = true;
        _chkTnL.Margin = new Padding(0, 9, 8, 0);
        _barraDgVoodoo.Controls.Add(lblDg);
        _barraDgVoodoo.Controls.Add(_cboPlaca);
        _barraDgVoodoo.Controls.Add(_chkTnL);
        _barraDgVoodoo.Controls.Add(Botao("Aplicar e testar", (_, _) => TrocarPlacaDgVoodoo()));
        _barraDgVoodoo.Controls.Add(Botao("O conf é lido?", (_, _) => TestarLeituraDoConf()));
        _barraDgVoodoo.Controls.Add(Botao("Painel do dgVoodoo", (_, _) => AbrirPainelDgVoodoo()));
        _barraDgVoodoo.Visible = false;

        // EnableHooks do RenoDX: só aparece no caminho direto, onde o addon é quem trabalha.
        _barraHooks.Controls.Add(new Label { Text = "Hooks do RenoDX:", AutoSize = true, Margin = new Padding(0, 10, 8, 0) });
        _cboHooks.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboHooks.Width = 380;
        _cboHooks.Margin = new Padding(0, 4, 8, 4);
        foreach (var v in RenodxIni.Valores) _cboHooks.Items.Add(RenodxIni.Descricao(v));
        _cboHooks.SelectedIndex = 0;
        _barraHooks.Controls.Add(_cboHooks);
        _barraHooks.Controls.Add(Botao("Aplicar hooks", (_, _) => TrocarHooksDoRenodx()));
        _barraHooks.Visible = false;

        t.RowCount = 5;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(_lblResumoVerificacao, 0, 0);
        t.Controls.Add(split, 0, 1);
        t.Controls.Add(bar, 0, 2);
        t.Controls.Add(_barraDgVoodoo, 0, 3);
        t.Controls.Add(_barraHooks, 0, 4);
        _pVerificacao.Controls.Add(t);
    }

    private void BuildResultado()
    {
        _pResultado = new Panel { Dock = DockStyle.Fill };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblResultadoTitulo.AutoSize = true;
        _lblResultadoTitulo.Dock = DockStyle.Fill;
        _lblResultadoTitulo.Font = Ui.SubtitleFont;
        _lblResultadoTitulo.Margin = new Padding(0, 0, 0, 8);

        Ui.StyleReadOnlyBox(_txtResultado);
        _txtResultado.Dock = DockStyle.Fill;
        _txtResultado.Margin = new Padding(0, 0, 0, 8);
        _txtResultado.AccessibleName = "Relatório do resultado";

        var bar = Ui.Fila();
        bar.Controls.Add(Botao("Abrir pasta do jogo", (_, _) => OpenFolder(_estado?.ExeFolder ?? _txtGame.Text)));
        bar.Controls.Add(Botao("Copiar relatório", (_, _) =>
        {
            try { Clipboard.SetText(_txtResultado.Text); Status("Relatório copiado."); } catch { }
        }));
        bar.Controls.Add(Botao(Textos.BotaoAbrirLogs, (_, _) => AbrirPastaDeLogs()));
        bar.Controls.Add(Botao(Textos.BotaoExportarDiagnostico, (_, _) => ExportarDiagnostico()));

        t.Controls.Add(_lblResultadoTitulo, 0, 0);
        t.Controls.Add(_txtResultado, 0, 1);
        t.Controls.Add(bar, 0, 2);
        _pResultado.Controls.Add(t);
    }

    private void MostrarResultado(CheckStatusKind tom, string titulo, string texto)
    {
        _lblResultadoTitulo.Text = $"{Ui.SimboloDoEstado(tom)}  {titulo}";
        _lblResultadoTitulo.ForeColor = Ui.ForState(tom);
        _txtResultado.Text = texto;
    }

    // ---------------------------------------------------------- verificação

    private void RunVerification()
    {
        if (_profile is null) { Aviso("Não há jogo detectado para verificar."); return; }
        _manifest ??= InstallManifest.Find(_profile.GameFolder, _profile.ExeFolder);
        _barraDgVoodoo.Visible = _profile.NeedsDgVoodoo;
        bool direto = _profile.UsesRenodxDirectPath;
        _barraHooks.Visible = direto;
        if (direto) SincronizarHooksDoRenodx();
        AtualizarBotaoRenodx();

        using var etapa = _diario.Etapa("Verificação");
        _grid.Rows.Clear();
        var resultados = CheckpointVerifier.Verify(_profile, _manifest, NvngxDoKit(), _overrideNoBoot).ToList();
        int ok = 0, falhas = 0, avisos = 0;
        foreach (var c in resultados)
        {
            var kind = c.State switch
            {
                CheckStatus.Pass => CheckStatusKind.Ok,
                CheckStatus.Fail => CheckStatusKind.Bad,
                CheckStatus.Warning => CheckStatusKind.Warn,
                CheckStatus.Manual => CheckStatusKind.Info,
                _ => CheckStatusKind.Neutral,
            };
            if (kind == CheckStatusKind.Ok) ok++;
            else if (kind == CheckStatusKind.Bad) falhas++;
            else if (kind == CheckStatusKind.Warn) avisos++;

            int i = _grid.Rows.Add($"{Ui.SimboloDoEstado(kind)} {StateText(c.State)}", $"{c.Number}. {c.Title}", c.Detail, c.FixHint ?? "");
            var row = _grid.Rows[i];
            var color = Ui.ForState(kind);
            row.Cells[0].Style.ForeColor = color;
            row.Cells[0].Style.SelectionForeColor = color;
            row.Cells[0].Style.Font = Ui.BoldFont;
            if (c.State is CheckStatus.Fail or CheckStatus.Warning)
            {
                row.Cells[1].Style.ForeColor = color;
                row.Cells[1].Style.SelectionForeColor = color;
            }
            if (c.State == CheckStatus.Pass) row.Cells[2].Style.ForeColor = Ui.Muted;
            _diario.Tecnico($"Checkpoint {c.Number} {c.Title}: {c.State} — {c.Detail}");
        }
        _lblResumoVerificacao.Text = $"{ok} ok, {falhas} falha(s), {avisos} aviso(s), {resultados.Count - ok - falhas - avisos} manual/não aplicável. " +
                                     "Dê dois cliques numa linha para ler o texto completo.";
        _lblResumoVerificacao.ForeColor = falhas > 0 ? Ui.Bad : avisos > 0 ? Ui.Warn : Ui.Ok;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("PASSOS MANUAIS — o que o programa não consegue fazer por você");
        sb.AppendLine("================================================================");
        foreach (var s in ManualSteps.For(_profile, _options))
        {
            sb.AppendLine($"{s.Order}. {s.Title}{(s.CriticalBeforeLaunch ? "   [FAZER ANTES DE ABRIR O JOGO]" : "")}");
            sb.AppendLine("   " + s.Detail);
            sb.AppendLine();
        }

        var diagnoses = SymptomDiagnoser.Diagnose(_profile);
        if (diagnoses.Count > 0)
        {
            sb.AppendLine("DIAGNÓSTICO A PARTIR DOS LOGS");
            sb.AppendLine("================================================================");
            foreach (var d in diagnoses)
            {
                sb.AppendLine($"• {d.Symptom}  ({d.Source})");
                sb.AppendLine($"  Causa: {d.Cause}");
                sb.AppendLine($"  Correção: {d.Fix}");
                sb.AppendLine();
            }
        }

        sb.AppendLine(ManualSteps.Limitations);
        _txtGuide.Text = sb.ToString();
        Status("Verificação atualizada.");
    }

    private static string StateText(CheckStatus s) => s switch
    {
        CheckStatus.Pass => "OK",
        CheckStatus.Fail => "FALHA",
        CheckStatus.Warning => "ATENÇÃO",
        CheckStatus.Manual => "MANUAL",
        _ => "N/A",
    };

    private void LaunchGame()
    {
        if (_profile?.RealExePath is null || !File.Exists(_profile.RealExePath)) { Aviso("Executável do jogo não encontrado."); return; }
        try
        {
            if (EaJavelin.EhJavelin(_profile.ExeFolder))
            {
                // Pela Steam o anticheat sobe antes do jogo e o ReShade não entra. O jogo
                // abre pelo Launcher do Live Editor, que o usuário aponta uma vez.
                var launcher = _settings.LiveEditorLauncher;
                if (!EaJavelin.PareceLiveEditor(launcher))
                {
                    Dialogos.Informar(this, "Jogo sob EA Javelin Anticheat", "Abrir pelo Live Editor",
                        EaJavelin.Aviso + "\r\n\r\n" + EaJavelin.ComoAbrir + "\r\n\r\nAponte o Launcher.exe do Live Editor.");
                    using var dlg = new OpenFileDialog
                    {
                        Title = "Launcher.exe do FC Live Editor",
                        Filter = "Launcher do Live Editor|Launcher.exe|Executáveis|*.exe",
                        CheckFileExists = true,
                    };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    launcher = dlg.FileName;
                    if (!EaJavelin.PareceLiveEditor(launcher) &&
                        !Dialogos.Pergunta(this, "Não parece o Live Editor",
                            $"Não achei {EaJavelin.DllDoLiveEditor} ao lado desse arquivo. Usar assim mesmo?"))
                        return;
                    _settings.LiveEditorLauncher = launcher;
                    _settings.Save();
                }
                Process.Start(new ProcessStartInfo(launcher!) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(launcher)! });
                Status("Live Editor aberto: inicie o jogo por ele (sem o anticheat). Depois de fechar, clique em Verificar de novo.");
                return;
            }

            var appId = SteamGame.FindAppId(_profile.GameFolder);
            if (appId is not null)
            {
                Process.Start(new ProcessStartInfo(SteamGame.RunUrl(appId)) { UseShellExecute = true });
                Status($"Pedido à Steam para abrir o jogo (AppID {appId}). Depois de fechar, clique em Verificar de novo.");
                return;
            }
            Process.Start(new ProcessStartInfo(_profile.RealExePath) { UseShellExecute = true, WorkingDirectory = _profile.ExeFolder });
            Status("Jogo iniciado. Depois de fechar, clique em Verificar de novo para ler os logs.");
        }
        catch (Exception ex) { Erro("Não consegui abrir o jogo", ex); }
    }

    private void ReiniciarSeOUsuarioQuiser()
    {
        var ok = Dialogos.Confirmar(this, "Reiniciar o PC (opcional)", "Reiniciar o PC agora?",
            "É opcional. O driver da NVIDIA lê o override de assinatura quando o Windows inicia; se o DLSS 5 já está " +
            "aplicando nos seus jogos, não precisa reiniciar.\r\n\r\nSe confirmar, o Windows reinicia em 60 segundos " +
            "(para cancelar depois, execute: shutdown /a).", "Reiniciar em 60 s", perigosa: true);
        if (!ok) return;
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/r /t 60 /c \"DLSS 5: reinício pedido pelo usuário para o driver ler o override.\"")
            { UseShellExecute = true, CreateNoWindow = true });
            Status("Reinício agendado para 60 segundos (cancelar: shutdown /a).");
        }
        catch (Exception ex) { Erro("Não consegui agendar o reinício", ex); }
    }

    // ---------------------------------------------------- ferramentas dgVoodoo

    private void IsolarCausa()
    {
        if (_profile is null) { Aviso("Faça a detecção primeiro."); return; }
        if (_ocupado) { Status(Textos.OperacaoEmAndamento); return; }

        var rodando = Preflight.JogoRodando(_profile.RealExePath);
        if (rodando is not null) { Aviso("O jogo está aberto", $"Feche o jogo ({rodando}.exe) antes: arquivo em uso não é renomeado."); return; }

        // O teste do RenoDX não entra aqui: ele é avulso (botão próprio) e não tem
        // pergunta desta bisseção.
        if (_isolamento is EstadoIsolamento.SemDgVoodoo or EstadoIsolamento.SemReShade)
        {
            var pergunta = _isolamento == EstadoIsolamento.SemDgVoodoo
                ? "Com o dgVoodoo DESLIGADO, o jogo abriu?"
                : _profile.NeedsDgVoodoo
                    ? "Com o ReShade DESLIGADO (e o dgVoodoo ligado), o jogo abriu?"
                    : "Com o ReShade DESLIGADO, o jogo abriu e rodou?";
            var abriu = Dialogos.Pergunta(this, "Resultado do teste", pergunta);
            if (_isolamento == EstadoIsolamento.SemDgVoodoo) _abriuSemDgVoodoo = abriu;
            else _abriuSemReShade = abriu;
        }
        else
        {
            _abriuSemDgVoodoo = null;
            _abriuSemReShade = null;
        }

        var proximo = _isolamento switch
        {
            // Vindo do teste avulso do RenoDX, a bisseção começa do zero (Aplicar religa).
            EstadoIsolamento.Tudo or EstadoIsolamento.SemRenodx => EstadoIsolamento.SemDgVoodoo,
            EstadoIsolamento.SemDgVoodoo => EstadoIsolamento.SemReShade,
            _ => EstadoIsolamento.Tudo,
        };
        if (proximo == EstadoIsolamento.SemDgVoodoo && !_profile.NeedsDgVoodoo) proximo = EstadoIsolamento.SemReShade;
        if (proximo == EstadoIsolamento.SemReShade && _abriuSemDgVoodoo is false) proximo = EstadoIsolamento.Tudo;

        try
        {
            using var etapa = _diario.Etapa("Isolar a causa");
            var iso = new Isolamento(_diario.Info);
            iso.Aplicar(proximo, _profile.ExeFolder, _profile.RendererFolder ?? _profile.ExeFolder);
            _isolamento = proximo;

            bool temDg = _profile.NeedsDgVoodoo;
            Status(proximo switch
            {
                EstadoIsolamento.SemDgVoodoo => "Teste 1 de 2: dgVoodoo desligado. Abra o jogo e volte aqui.",
                EstadoIsolamento.SemReShade => temDg
                    ? "Teste 2 de 2: ReShade desligado. Abra o jogo e volte aqui."
                    : "Teste único: ReShade desligado. Abra o jogo e volte aqui.",
                _ => "Instalação religada por inteiro.",
            });

            var texto = proximo == EstadoIsolamento.Tudo
                ? Isolamento.Veredito(_abriuSemDgVoodoo, _abriuSemReShade, temDg) + "\r\n\r\n" + Isolamento.Leitura(proximo, temDg)
                : Isolamento.Leitura(proximo, temDg) + "\r\n\r\nDepois de abrir o jogo, clique em \"Isolar a causa\" de novo: ele pergunta o que aconteceu e passa ao próximo teste.";
            Dialogos.Informar(this, "Isolar a causa", proximo == EstadoIsolamento.Tudo ? "Conclusão" : "Teste em andamento", texto);
            AtualizarBotaoRenodx();
        }
        catch (Exception ex) { Erro("Não consegui alterar os arquivos do teste", ex); }
    }

    /// <summary>
    /// Teste avulso do caso "abre, mas o DLSS do MENU do jogo trava ao ligar": desliga só
    /// o addon do RenoDX — quem se pendura na chamada de NGX que o próprio jogo faz —
    /// mantendo ReShade e Feeder ativos. Foi o padrão do GTA 5 depois da recuperação: DLL
    /// original de volta e o travamento continuou, o que tira o arquivo da lista de
    /// suspeitos e deixa a interceptação dentro do processo. O mesmo botão religa.
    /// </summary>
    private void TestarSemRenodx()
    {
        if (_profile is null) { Aviso("Faça a detecção primeiro."); return; }
        if (_ocupado) { Status(Textos.OperacaoEmAndamento); return; }
        var rodando = Preflight.JogoRodando(_profile.RealExePath);
        if (rodando is not null) { Aviso("O jogo está aberto", $"Feche o jogo ({rodando}.exe) antes: arquivo em uso não é renomeado."); return; }

        var proximo = _isolamento == EstadoIsolamento.SemRenodx ? EstadoIsolamento.Tudo : EstadoIsolamento.SemRenodx;
        try
        {
            using var etapa = _diario.Etapa("Testar sem o RenoDX");
            new Isolamento(_diario.Info).Aplicar(proximo, _profile.ExeFolder, _profile.RendererFolder ?? _profile.ExeFolder);
            _isolamento = proximo;
            AtualizarBotaoRenodx();
            Status(proximo == EstadoIsolamento.SemRenodx
                ? "RenoDX desligado. Abra o jogo e teste o DLSS no MENU do jogo."
                : "RenoDX religado.");
            Dialogos.Informar(this, "Testar sem o RenoDX", proximo == EstadoIsolamento.SemRenodx ? "RenoDX desligado" : "RenoDX religado",
                Isolamento.Leitura(proximo, _profile.NeedsDgVoodoo));
        }
        catch (Exception ex) { Erro("Não consegui alterar os arquivos do teste", ex); }
    }

    /// <summary>
    /// Teste avulso do caso "instalei e o jogo não abre mais": desliga só o Feeder,
    /// mantendo ReShade e RenoDX. O Feeder inicializa um NGX dentro do processo do jogo
    /// e foi quem derrubou Onimusha e GTA 5 — mas não havia degrau para ele, então a
    /// bisseção acusava o ReShade inteiro e o culpado escapava.
    /// </summary>
    private void TestarSemFeeder()
    {
        if (_profile is null) { Aviso("Faça a detecção primeiro."); return; }
        if (_ocupado) { Status(Textos.OperacaoEmAndamento); return; }
        var rodando = Preflight.JogoRodando(_profile.RealExePath);
        if (rodando is not null) { Aviso("O jogo está aberto", $"Feche o jogo ({rodando}.exe) antes: arquivo em uso não é renomeado."); return; }

        var proximo = _isolamento == EstadoIsolamento.SemFeeder ? EstadoIsolamento.Tudo : EstadoIsolamento.SemFeeder;
        try
        {
            using var etapa = _diario.Etapa("Testar sem o Feeder");
            new Isolamento(_diario.Info).Aplicar(proximo, _profile.ExeFolder, _profile.RendererFolder ?? _profile.ExeFolder);
            _isolamento = proximo;
            AtualizarBotaoRenodx();
            Status(proximo == EstadoIsolamento.SemFeeder
                ? "Feeder desligado. Abra o jogo e veja se ele abre."
                : "Feeder religado.");
            Dialogos.Informar(this, "Testar sem o Feeder",
                proximo == EstadoIsolamento.SemFeeder ? "Feeder desligado" : "Feeder religado",
                Isolamento.Leitura(proximo, _profile.NeedsDgVoodoo));
        }
        catch (Exception ex) { Erro("Não consegui alterar os arquivos do teste", ex); }
    }

    /// <summary>
    /// Desliga os DOIS addons e deixa só o ReShade. Fecha a pergunta que os testes
    /// separados não fecham: com dois addons na pasta, desligar um de cada vez nunca
    /// inocenta os dois ao mesmo tempo.
    /// </summary>
    private void TestarSoOReShade()
    {
        if (_profile is null) { Aviso("Faça a detecção primeiro."); return; }
        if (_ocupado) { Status(Textos.OperacaoEmAndamento); return; }
        var rodando = Preflight.JogoRodando(_profile.RealExePath);
        if (rodando is not null) { Aviso("O jogo está aberto", $"Feche o jogo ({rodando}.exe) antes: arquivo em uso não é renomeado."); return; }

        var proximo = _isolamento == EstadoIsolamento.SoOReShade ? EstadoIsolamento.Tudo : EstadoIsolamento.SoOReShade;
        try
        {
            using var etapa = _diario.Etapa("Testar só o ReShade");
            new Isolamento(_diario.Info).Aplicar(proximo, _profile.ExeFolder, _profile.RendererFolder ?? _profile.ExeFolder);
            _isolamento = proximo;
            AtualizarBotaoRenodx();
            Status(proximo == EstadoIsolamento.SoOReShade
                ? "Addons desligados. Abra o jogo e veja se ele abre."
                : "Addons religados.");
            Dialogos.Informar(this, "Testar só o ReShade",
                proximo == EstadoIsolamento.SoOReShade ? "Addons desligados" : "Addons religados",
                Isolamento.Leitura(proximo, _profile.NeedsDgVoodoo));
        }
        catch (Exception ex) { Erro("Não consegui alterar os arquivos do teste", ex); }
    }

    /// <summary>
    /// Baixa o nvngx_dlssnr.dll recomendado (o que o RHI instala) para a pasta do kit,
    /// conferindo o hash antes de trocar. Depois é só instalar de novo: o motor copia o
    /// arquivo novo e atualiza o manifesto. Foi o que faltou no RE9 — o kit trazia um
    /// remendo do original de RTX 50, e a RTX 4070 Ti rodava, dizia OK e não desenhava.
    /// </summary>
    private async Task BaixarRuntimeDoRhiAsync()
    {
        if (_ocupado) { Status(Textos.OperacaoEmAndamento); return; }
        var pastaKit = _txtKit.Text.Trim();
        if (string.IsNullOrWhiteSpace(pastaKit) || !Directory.Exists(pastaKit))
        {
            Aviso("Aponte a pasta do kit primeiro", "O runtime é gravado na pasta do kit (DLSS 5 Files), na tela inicial.");
            return;
        }

        KitInventory kit;
        try { kit = KitResolver.Resolve(pastaKit); }
        catch (Exception ex) { Erro("Não consegui ler a pasta do kit", ex); return; }
        var destino = kit.NvngxDlssnr ?? Path.Combine(pastaKit, RuntimeNr.Arquivo);

        var atual = RuntimeNr.Identificar(destino);
        var (falha, texto) = RuntimeNr.Avaliar(atual, null);
        if (!Dialogos.Confirmar(this, "Baixar o runtime do RHI",
                atual is null && !File.Exists(destino)
                    ? "O kit não tem nvngx_dlssnr.dll"
                    : falha ? "O runtime do kit não serve para a maioria das placas" : "O runtime do kit já é um build bom",
                (File.Exists(destino) ? texto + "\r\n\r\n" : "") +
                $"Baixar {RuntimeNr.Recomendado} de {RuntimeNr.UrlRecomendado} (~110 MB compactado) e gravar em:\r\n{destino}\r\n\r\n" +
                "O arquivo atual fica guardado como .dlss5prev. O hash é conferido antes da troca. " +
                "Depois, clique em Instalar de novo (Atualizar) para o jogo receber o arquivo novo.",
                "Baixar"))
            return;

        SetOcupado(true);
        try
        {
            using var etapa = _diario.Etapa("Baixar o runtime do RHI");
            var progresso = new Progress<string>(Status);
            var build = await RuntimeNr.BaixarParaOKit(destino, progresso);
            _kit = null;   // o inventário do kit mudou: quem precisar resolve de novo
            _diario.Info($"nvngx_dlssnr.dll do kit trocado para {build.Nome} ({build.Sha256[..12]}...)");
            Status($"Kit atualizado: {build.Nome}. Agora instale de novo para o jogo receber o arquivo.");
            Dialogos.Informar(this, "Runtime baixado", $"{RuntimeNr.Arquivo} do kit agora é o {build.Nome}",
                build.Leitura + "\r\n\r\nPróximo passo: volte à tela inicial e clique em Instalar (Atualizar). " +
                "Depois abra o jogo: no painel do addon o runtime deve aparecer como 310.8.SF.0, e o item 18 da " +
                "verificação passa a OK.");
        }
        catch (Exception ex) { Erro("Não consegui baixar o runtime", ex); }
        finally { SetOcupado(false); }
    }

    /// <summary>O texto do botão diz o que o clique fará, não o estado atual.</summary>
    private void AtualizarBotaoRenodx()
    {
        _btnRenodx.Text = _isolamento == EstadoIsolamento.SemRenodx ? "Religar o RenoDX" : "Testar sem o RenoDX";
        _btnFeeder.Text = _isolamento == EstadoIsolamento.SemFeeder ? "Religar o Feeder" : "Testar sem o Feeder";
        // Sem Feeder instalado (caminho direto) o teste não existe.
        _btnFeeder.Visible = _profile?.NeedsFeeder ?? false;
        _btnSoReShade.Text = _isolamento == EstadoIsolamento.SoOReShade
            ? "Religar os addons"
            : "Testar só o ReShade";
    }

    /// <summary>Mostra na lista o valor que o ReShade.ini da pasta tem de fato.</summary>
    private void SincronizarHooksDoRenodx()
    {
        if (_profile is null) return;
        var ini = Path.Combine(_profile.ExeFolder, "ReShade.ini");
        int valor = RenodxIni.Padrao;
        try { if (File.Exists(ini)) valor = RenodxIni.Ler(File.ReadAllText(ini)) ?? RenodxIni.Padrao; }
        catch { /* sem leitura, fica o padrão */ }
        int i = RenodxIni.Valores.ToList().IndexOf(valor);
        _cboHooks.SelectedIndex = i < 0 ? 0 : i;
    }

    /// <summary>
    /// Regrava só a chave EnableHooks na seção [RenoDX.DLSS5] do ReShade.ini. É o ajuste
    /// que o próprio addon pede em jogo com Streamline (1) e o teste que deixa o addon
    /// carregado sem gancho nenhum (0) — sem desinstalar e instalar a cada tentativa.
    /// </summary>
    private void TrocarHooksDoRenodx()
    {
        if (_profile is null) { Aviso("Faça a detecção primeiro."); return; }
        var rodando = Preflight.JogoRodando(_profile.RealExePath);
        if (rodando is not null) { Aviso("O jogo está aberto", $"Feche o jogo ({rodando}.exe) antes: o ReShade regrava o ReShade.ini ao sair e desfaria a troca."); return; }
        var ini = Path.Combine(_profile.ExeFolder, "ReShade.ini");
        if (!File.Exists(ini)) { Aviso("ReShade.ini não está na pasta do exe — instale primeiro."); return; }

        int valor = RenodxIni.Valores[Math.Max(0, _cboHooks.SelectedIndex)];
        try
        {
            File.WriteAllText(ini, RenodxIni.Gravar(File.ReadAllText(ini), valor));
            _diario.Info($"ReShade.ini: EnableHooks={valor}");
            Status($"ReShade.ini: EnableHooks={valor}. Abra o jogo e verifique de novo.");
            Dialogos.Informar(this, "Hooks do RenoDX", $"EnableHooks = {valor} gravado", RenodxIni.Leitura(valor));
        }
        catch (Exception ex) { Erro("Não consegui gravar o ReShade.ini", ex); }
    }

    private string? ConfDoDgVoodoo()
    {
        var pasta = _profile?.RendererFolder ?? _profile?.ExeFolder;
        var conf = string.IsNullOrWhiteSpace(pasta) ? null : Path.Combine(pasta, "dgVoodoo.conf");
        if (conf is null || !File.Exists(conf)) { Aviso($"dgVoodoo.conf não está em {pasta}.", "Ele só é instalado nos jogos que precisam do dgVoodoo (32-bit em DirectX 8 ou 9)."); return null; }
        var rodando = Preflight.JogoRodando(_profile?.RealExePath);
        if (rodando is not null) { Aviso("O jogo está aberto", $"Feche o jogo ({rodando}.exe) antes: ele lê o conf ao abrir."); return null; }
        return conf;
    }

    private void TestarLeituraDoConf()
    {
        var conf = ConfDoDgVoodoo();
        if (conf is null) return;
        try
        {
            var texto = File.ReadAllText(conf);
            bool emTeste = string.Equals(DgVoodooConfigurator.LerChave(texto, "DirectX", "DisableAndPassThru"), "true", StringComparison.OrdinalIgnoreCase);
            if (!emTeste)
            {
                File.WriteAllText(conf, DgVoodooConfigurator.DefinirChave(texto, "DirectX", "DisableAndPassThru", "true"));
                Status("Passthru ligado. Abra o jogo e volte a clicar em \"O conf é lido?\".");
                Dialogos.Informar(this, "O conf é lido?", "Gravei DisableAndPassThru = true",
                    "Com isso o dgVoodoo repassa tudo ao Direct3D do Windows — ele sai da frente sem sair da pasta. " +
                    "Se o conf estiver sendo lido, o jogo TEM que abrir.\r\n\r\nAbra o jogo agora e clique neste botão de novo.");
                return;
            }
            bool abriu = Dialogos.Pergunta(this, "O conf é lido?", "Com o passthru ligado, o jogo abriu?");
            File.WriteAllText(conf, DgVoodooConfigurator.DefinirChave(File.ReadAllText(conf), "DirectX", "DisableAndPassThru", "false"));
            Status(abriu ? "O conf é lido: os ajustes daqui têm efeito." : "O conf NÃO é lido: os ajustes daqui são inertes.");
            Dialogos.Informar(this, "O conf é lido?", abriu ? "O dgVoodoo LÊ o conf" : "O dgVoodoo NÃO está lendo o conf",
                abriu
                    ? "Então placa, T&L e resoluções realmente têm efeito, e vale varrer as combinações: para cada placa da lista, teste com a caixa \"T&L por hardware\" marcada e desmarcada.\r\n\r\nPassthru devolvido para false."
                    : "Com passthru=true ele deveria ter saído da frente e o jogo deveria abrir. Como não abriu, o arquivo não chega até ele — e TUDO que foi ajustado aqui até agora foi inerte.\r\n\r\n" +
                      "Isso muda o rumo: o problema deixa de ser qual valor usar e passa a ser o arquivo não ser encontrado. Exporte o diagnóstico e envie.\r\n\r\nPassthru devolvido para false.");
        }
        catch (Exception ex) { Erro("Não consegui gravar o dgVoodoo.conf", ex); }
    }

    private void TrocarPlacaDgVoodoo()
    {
        var conf = ConfDoDgVoodoo();
        if (conf is null) return;
        int idx = _cboPlaca.SelectedIndex;
        if (idx < 0 || idx >= DgVoodooConfigurator.Placas.Count) return;
        var (rotulo, valor) = DgVoodooConfigurator.Placas[idx];
        try
        {
            var perfil = DgVoodooConfigurator.ProfileFor(_profile!.Api);
            File.WriteAllText(conf, DgVoodooConfigurator.Patch(File.ReadAllText(conf), perfil, valor, _chkTnL.Checked));
            _diario.Info($"dgVoodoo.conf: VideoCard={valor}, T&L={_chkTnL.Checked}");
            Status($"dgVoodoo agora se apresenta como {rotulo}. Abra o jogo e veja se muda.");
            Dialogos.Informar(this, "Placa trocada", $"Gravado: VideoCard = {valor}",
                $"T&L por hardware: {(_chkTnL.Checked ? "sim" : "não")}\r\n{conf}\r\n\r\nAbra o jogo. Se continuar recusando o adaptador, volte aqui e tente a próxima da lista — nada precisa ser reinstalado entre uma tentativa e outra.");
        }
        catch (Exception ex) { Erro("Não consegui gravar o dgVoodoo.conf", ex); }
    }

    private void AbrirPainelDgVoodoo()
    {
        var pasta = _profile?.RendererFolder ?? _profile?.ExeFolder;
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) { Aviso("Faça a detecção primeiro."); return; }
        var cpl = Path.Combine(pasta, "dgVoodooCpl.exe");
        if (!File.Exists(cpl)) { Aviso($"dgVoodooCpl.exe não está em {pasta}.", "Ele só é instalado nos jogos que precisam do dgVoodoo (32-bit em DirectX 8 ou 9)."); return; }
        try
        {
            Process.Start(new ProcessStartInfo(cpl) { UseShellExecute = true, WorkingDirectory = pasta });
            Status("Painel do dgVoodoo aberto. Aba DirectX → VideoCard. Salve e reabra o jogo.");
        }
        catch (Exception ex) { Erro("Não consegui abrir o painel", ex); }
    }
}
