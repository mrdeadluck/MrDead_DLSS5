using System.Diagnostics;
using System.Runtime.Versioning;
using Dlss5.Core;

namespace Dlss5.App;

/// <summary>Qual jornada o usuário escolheu na tela inicial. Define os passos e os botões.</summary>
internal enum Fluxo
{
    Nenhum,
    Instalar,
    Atualizar,
    Reparar,
    Desinstalar,
    RemoverVestigios,
    RestaurarBackups,
    Verificar,
}

internal enum Tela
{
    Inicio,
    Deteccao,
    Plano,
    Execucao,
    Verificacao,
    Resultado,
}

/// <summary>
/// Janela principal. Este arquivo tem só a moldura (barra lateral, título, rodapé), a
/// navegação e o controle de "ocupado"; cada tela vive num arquivo parcial próprio e a
/// regra de negócio fica toda em Dlss5.Core.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainForm : Form
{
    private readonly Diario _diario;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly InstallOptions _options = new();

    private RelatorioDeEstado? _estado;
    private GameProfile? _profile;
    private KitInventory? _kit;
    private InstallPlan? _plan;
    private InstallManifest? _manifest;
    private IReadOnlyList<ExeCandidate> _candidates = Array.Empty<ExeCandidate>();

    /// <summary>
    /// O override de assinatura estava no registro quando o Windows subiu? É o que decide
    /// se falta reiniciar — não o carimbo do manifesto, que cada Desinstalar/Instalar do
    /// dia renova sem mudar nada no driver.
    /// </summary>
    private bool? _overrideNoBoot;

    private Fluxo _fluxo = Fluxo.Nenhum;
    private Tela _tela = Tela.Inicio;
    private bool _ocupado;
    private CancellationTokenSource? _cts;

    // Moldura
    private readonly Label _stepTitle = new();
    private readonly Label _stepHint = new();
    private readonly Panel _content = new();
    private readonly Button _btnBack = new();
    private readonly Button _btnNext = new();
    private readonly Label _status = new();
    private readonly TableLayoutPanel _sidebarSteps = new();
    private readonly List<Label> _stepLabels = new();

    public MainForm(Diario diario)
    {
        _diario = diario;

        Text = Textos.TituloDoPrograma;
        StartPosition = FormStartPosition.CenterScreen;
        // Medidas em 96 DPI; o WinForms escala tudo (controles, margens, mínimos) para
        // 125/150/175/200 %. Sem isto os botões ficavam do mesmo tamanho em pixels e o
        // texto, maior, era cortado.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimumSize = new Size(900, 600);
        Size = new Size(1240, 820);
        Font = Ui.BodyFont;
        BackColor = Ui.Page;
        ForeColor = Ui.Ink;
        KeyPreview = true;

        BuildChrome();
        BuildInicio();
        BuildDeteccao();
        BuildPlano();
        BuildExecucao();
        BuildVerificacao();
        BuildResultado();

        CarregarPreferencias();
        _diario.LinhaVisivel += OnLinhaDoDiario;

        MostrarTela(Tela.Inicio);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        EncaixarNoMonitor();
        _diario.Tecnico($"Janela: {Width}x{Height}, DPI {DeviceDpi}, escala {DeviceDpi / 96.0:P0}, tela {Screen.FromControl(this).Bounds.Width}x{Screen.FromControl(this).Bounds.Height}");
        if (!string.IsNullOrWhiteSpace(_txtGame.Text)) _ = InspecionarAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_ocupado)
        {
            var fechar = Dialogos.Confirmar(this, Textos.TituloDoPrograma, "Operação em andamento",
                Textos.FecharDuranteOperacao, "Fechar mesmo assim", perigosa: true);
            if (!fechar) { e.Cancel = true; return; }
            _diario.Aviso("Programa fechado pelo usuário durante uma operação.");
            _cts?.Cancel();
        }
        SalvarPreferencias();
        base.OnFormClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Navegação por teclado: Alt+Esquerda volta; F1 abre a ajuda de detalhes.
        if (e.Alt && e.KeyCode == Keys.Left && _btnBack.Enabled && _btnBack.Visible) { Voltar(); e.Handled = true; }
        if (e.KeyCode == Keys.F1) { MostrarDetalhesDoEstado(); e.Handled = true; }
    }

    /// <summary>
    /// Encolhe a janela até caber na área útil da tela. Roda no OnLoad porque só aí o
    /// Windows já aplicou a escala do monitor.
    /// </summary>
    private void EncaixarNoMonitor()
    {
        var area = Screen.FromControl(this).WorkingArea;
        const int margem = 24;

        var minLargura = Math.Min(MinimumSize.Width, Math.Max(640, area.Width - margem));
        var minAltura = Math.Min(MinimumSize.Height, Math.Max(460, area.Height - margem));
        MinimumSize = new Size(minLargura, minAltura);

        Size = new Size(
            Math.Max(minLargura, Math.Min(Width, area.Width - margem)),
            Math.Max(minAltura, Math.Min(Height, area.Height - margem)));

        Location = new Point(
            area.X + Math.Max(0, (area.Width - Width) / 2),
            area.Y + Math.Max(0, (area.Height - Height) / 2));
    }

    // ---------------------------------------------------------------- chrome

    private void BuildChrome()
    {
        var sidebar = BuildSidebar();

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(24, 18, 24, 14),
            BackColor = Ui.Page,
        };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _stepTitle.Font = Ui.TitleFont;
        _stepTitle.ForeColor = Ui.Ink;
        _stepTitle.AutoSize = true;
        _stepTitle.Dock = DockStyle.Top;
        _stepTitle.Margin = new Padding(0, 0, 0, 4);

        _stepHint.AutoSize = true;
        _stepHint.Dock = DockStyle.Top;
        _stepHint.ForeColor = Ui.Muted;
        _stepHint.Margin = new Padding(0, 0, 0, 12);
        // Quebra de linha pela largura disponível: MaximumSize acompanha o painel.
        main.Resize += (_, _) => AjustarQuebra(main);

        _content.Dock = DockStyle.Fill;
        _content.BackColor = Ui.Card;
        _content.BorderStyle = BorderStyle.FixedSingle;
        _content.Padding = new Padding(16);
        _content.Margin = new Padding(0);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _status.AutoSize = true;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Ui.Muted;
        _status.Margin = new Padding(0, 0, 12, 0);
        _status.AutoEllipsis = true;
        _status.AutoSize = false;
        _status.Height = Ui.AlturaDoBotao;

        Ui.MakeSecondary(_btnBack, Textos.BotaoVoltar);
        _btnBack.Margin = new Padding(0, 0, 8, 0);
        _btnBack.Click += (_, _) => Voltar();

        Ui.MakePrimary(_btnNext, Textos.BotaoDetectar);
        _btnNext.Margin = new Padding(0);
        _btnNext.MinimumSize = new Size(150, Ui.AlturaDoBotao);
        _btnNext.Click += async (_, _) => await AvancarAsync();

        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(_btnBack, 1, 0);
        footer.Controls.Add(_btnNext, 2, 0);

        main.Controls.Add(_stepTitle, 0, 0);
        main.Controls.Add(_stepHint, 0, 1);
        main.Controls.Add(_content, 0, 2);
        main.Controls.Add(footer, 0, 3);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Ui.Page,
            Margin = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(sidebar, 0, 0);
        root.Controls.Add(main, 1, 0);
        Controls.Add(root);
    }

    private void AjustarQuebra(Control pai)
    {
        var largura = Math.Max(320, pai.ClientSize.Width - pai.Padding.Horizontal);
        _stepHint.MaximumSize = new Size(largura, 0);
        _stepTitle.MaximumSize = new Size(largura, 0);
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Ui.Sidebar,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var marca = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(22, 22, 12, 18),
            Margin = new Padding(0),
        };
        marca.Controls.Add(new Label
        {
            Text = "DLSS 5",
            Font = Ui.BrandFont,
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0),
        }, 0, 0);
        marca.Controls.Add(new Label
        {
            Text = "AutoInstaller " + AppInfo.VersaoComBuild,
            Font = Ui.SmallFont,
            ForeColor = Ui.SidebarIdle,
            AutoSize = true,
            Margin = new Padding(1, 0, 0, 0),
        }, 0, 1);

        _sidebarSteps.Dock = DockStyle.Top;
        _sidebarSteps.AutoSize = true;
        _sidebarSteps.ColumnCount = 1;
        _sidebarSteps.Margin = new Padding(0);
        _sidebarSteps.Padding = new Padding(0);

        var meio = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0) };
        meio.Controls.Add(_sidebarSteps);

        sidebar.Controls.Add(marca, 0, 0);
        sidebar.Controls.Add(meio, 0, 1);
        sidebar.Controls.Add(BuildSidebarFooter(), 0, 2);
        return sidebar;
    }

    /// <summary>Crédito do autor, preso ao pé da barra lateral.</summary>
    private static Control BuildSidebarFooter()
    {
        var rodape = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Ui.Sidebar,
            Padding = new Padding(18, 12, 12, 16),
            Margin = new Padding(0),
        };
        rodape.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rodape.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        const int lado = 52;
        var foto = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Ui.Sidebar,
            Size = new Size(lado, lado),
            Margin = new Padding(0, 0, 10, 0),
        };
        var imagem = Ui.LoadAvatar();
        if (imagem is not null)
        {
            foto.Image = imagem;
            var recorte = new System.Drawing.Drawing2D.GraphicsPath();
            recorte.AddEllipse(0, 0, lado, lado);
            foto.Region = new Region(recorte);
        }
        else
        {
            foto.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var fundo = new SolidBrush(Ui.Accent);
                e.Graphics.FillEllipse(fundo, 0, 0, foto.Width - 1, foto.Height - 1);
                using var fonte = new Font("Segoe UI", 14F, FontStyle.Bold);
                using var centro = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString("MD", fonte, Brushes.White, new RectangleF(0, 0, foto.Width, foto.Height), centro);
            };
        }
        rodape.Controls.Add(foto, 0, 0);
        rodape.SetRowSpan(foto, 2);
        rodape.Controls.Add(new Label
        {
            Text = "Desenvolvido por",
            Font = Ui.SmallFont,
            ForeColor = Ui.SidebarIdle,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
        }, 1, 0);
        rodape.Controls.Add(new Label
        {
            Text = "MrDead_",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0),
        }, 1, 1);
        return rodape;
    }

    /// <summary>Os passos do fluxo atual, na ordem, com o nome que aparece na barra lateral.</summary>
    private IReadOnlyList<(Tela Tela, string Nome)> PassosDoFluxo() => _fluxo switch
    {
        Fluxo.Instalar or Fluxo.Atualizar or Fluxo.Reparar => new[]
        {
            (Tela.Inicio, Textos.PassoJogo), (Tela.Deteccao, Textos.PassoDeteccao), (Tela.Plano, Textos.PassoPlano),
            (Tela.Execucao, Textos.PassoExecucao), (Tela.Verificacao, Textos.PassoVerificacao),
        },
        Fluxo.Desinstalar or Fluxo.RemoverVestigios or Fluxo.RestaurarBackups => new[]
        {
            (Tela.Inicio, Textos.PassoJogo), (Tela.Execucao, Textos.PassoExecucao), (Tela.Resultado, Textos.PassoResultado),
        },
        Fluxo.Verificar => new[] { (Tela.Inicio, Textos.PassoJogo), (Tela.Verificacao, Textos.PassoVerificacao) },
        _ => new[]
        {
            (Tela.Inicio, Textos.PassoJogo), (Tela.Deteccao, Textos.PassoDeteccao), (Tela.Plano, Textos.PassoPlano),
            (Tela.Execucao, Textos.PassoExecucao), (Tela.Verificacao, Textos.PassoVerificacao),
        },
    };

    /// <summary>Redesenha a lista de passos conforme o fluxo e marca o atual.</summary>
    private void AtualizarSidebar()
    {
        var passos = PassosDoFluxo();
        _sidebarSteps.SuspendLayout();
        _sidebarSteps.Controls.Clear();
        _stepLabels.Clear();
        _sidebarSteps.RowCount = passos.Count;
        _sidebarSteps.RowStyles.Clear();
        int atual = passos.ToList().FindIndex(p => p.Tela == _tela);
        for (int i = 0; i < passos.Count; i++)
        {
            bool corrente = i == atual;
            var lbl = new Label
            {
                Text = $"{i + 1}.   {passos[i].Nome}",
                AutoSize = true,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = corrente ? Ui.StepFontOn : Ui.StepFont,
                ForeColor = corrente ? Color.White : (i < atual ? Ui.SidebarDone : Ui.SidebarIdle),
                BackColor = corrente ? Ui.Accent : Ui.Sidebar,
                Padding = new Padding(14, 9, 8, 9),
                Margin = new Padding(0),
            };
            _sidebarSteps.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _sidebarSteps.Controls.Add(lbl, 0, i);
            _stepLabels.Add(lbl);
        }
        _sidebarSteps.ResumeLayout();
    }

    // ------------------------------------------------------------- navegação

    private void MostrarTela(Tela tela)
    {
        _tela = tela;
        _content.SuspendLayout();
        _content.Controls.Clear();
        (Panel panel, string title, string hint) = tela switch
        {
            Tela.Inicio => (_pInicio, Textos.InicioTitulo, Textos.InicioDica),
            Tela.Deteccao => (_pDeteccao, Textos.DeteccaoTitulo, Textos.DeteccaoDica),
            Tela.Plano => (_pPlano, Textos.PlanoTitulo, Textos.PlanoDica),
            Tela.Execucao => (_pExecucao, Textos.ExecucaoTitulo,
                _fluxo is Fluxo.Desinstalar or Fluxo.RemoverVestigios or Fluxo.RestaurarBackups
                    ? Textos.ExecucaoDicaDesinstalar : Textos.ExecucaoDicaInstalar),
            Tela.Verificacao => (_pVerificacao, Textos.VerificacaoTitulo, Textos.VerificacaoDica),
            _ => (_pResultado, Textos.ResultadoTitulo, ""),
        };
        _stepTitle.Text = title;
        _stepHint.Text = hint;
        _stepHint.Visible = hint.Length > 0;
        panel.Dock = DockStyle.Fill;
        _content.Controls.Add(panel);
        _content.ResumeLayout();
        AtualizarSidebar();
        AtualizarRodape();

        // Foco no primeiro controle útil da tela, para navegação por teclado.
        panel.SelectNextControl(panel, true, true, true, true);
    }

    /// <summary>Botões do rodapé conforme tela, fluxo e ocupação.</summary>
    private void AtualizarRodape()
    {
        _btnBack.Visible = _tela != Tela.Inicio;
        _btnBack.Enabled = !_ocupado;

        switch (_tela)
        {
            case Tela.Inicio:
                _btnNext.Visible = false;
                break;
            case Tela.Deteccao:
                _btnNext.Visible = true;
                _btnNext.Text = Textos.BotaoGerarPlano;
                _btnNext.Enabled = !_ocupado;
                break;
            case Tela.Plano:
                _btnNext.Visible = true;
                _btnNext.Text = _fluxo switch
                {
                    Fluxo.Atualizar => Textos.BotaoAtualizar,
                    Fluxo.Reparar => Textos.BotaoReparar,
                    _ => Textos.BotaoInstalar,
                };
                _btnNext.Enabled = !_ocupado && PlanoPodeRodar();
                break;
            case Tela.Execucao:
                _btnNext.Visible = true;
                _btnNext.Text = _fluxo is Fluxo.Desinstalar or Fluxo.RemoverVestigios or Fluxo.RestaurarBackups
                    ? "Ver resultado ›"
                    : Textos.BotaoVerificar;
                _btnNext.Enabled = !_ocupado && _execucaoTerminou;
                break;
            case Tela.Verificacao:
            case Tela.Resultado:
                _btnNext.Visible = true;
                _btnNext.Text = Textos.BotaoInicio;
                _btnNext.Enabled = !_ocupado;
                break;
        }
    }

    private void Voltar()
    {
        if (_ocupado) { Status(Textos.OperacaoEmAndamento); return; }
        var passos = PassosDoFluxo();
        int i = passos.ToList().FindIndex(p => p.Tela == _tela);
        if (i <= 0) return;
        // Depois de executar, "voltar" não reabre o plano: volta ao início e reinspeciona.
        if (_tela is Tela.Verificacao or Tela.Resultado || (_tela == Tela.Execucao && _execucaoTerminou)
            || passos[i - 1].Tela == Tela.Inicio)
        {
            IrParaInicio();
            return;
        }
        MostrarTela(passos[i - 1].Tela);
    }

    private async Task AvancarAsync()
    {
        if (_ocupado) { Status(Textos.OperacaoEmAndamento); return; }
        try
        {
            switch (_tela)
            {
                case Tela.Deteccao: GerarPlano(); break;
                case Tela.Plano: await ExecutarInstalacaoAsync(); break;
                case Tela.Execucao:
                    if (_fluxo is Fluxo.Desinstalar or Fluxo.RemoverVestigios or Fluxo.RestaurarBackups)
                        MostrarTela(Tela.Resultado);
                    else { MostrarTela(Tela.Verificacao); RunVerification(); }
                    break;
                case Tela.Verificacao:
                case Tela.Resultado:
                    IrParaInicio();
                    break;
            }
        }
        catch (Exception ex)
        {
            Erro("Falha inesperada", ex);
        }
    }

    private void IrParaInicio()
    {
        _fluxo = Fluxo.Nenhum;
        _plan = null;
        MostrarTela(Tela.Inicio);
        _ = InspecionarAsync();
    }

    // ------------------------------------------------------------- utilidades

    private void SetOcupado(bool ocupado)
    {
        _ocupado = ocupado;
        UseWaitCursor = ocupado;
        AtualizarRodape();
        AtualizarAcoesDoInicio();
        AtualizarBotoesDaExecucao();
    }

    private void Status(string texto)
    {
        _status.Text = texto;
        _diario.Tecnico("[status] " + texto);
    }

    private void Aviso(string cabecalho, string detalhe = "")
    {
        _status.Text = cabecalho;
        _diario.Aviso(cabecalho + (detalhe.Length > 0 ? " — " + detalhe : ""));
        Dialogos.Avisar(this, cabecalho, detalhe);
    }

    private void Erro(string contexto, Exception ex)
    {
        _diario.Erro(contexto, ex);
        _status.Text = contexto + ": " + ex.Message;
        var r = Dialogos.Mostrar(this, Textos.TituloDoPrograma, contexto,
            ex.Message + "\r\n\r\nDetalhes técnicos:\r\n" + ex + "\r\n\r\nLog: " + (_diario.ArquivoAtual ?? "(só em memória)"),
            CheckStatusKind.Bad, null,
            new Dialogos.Opcao("OK", DialogResult.OK, Principal: true),
            new Dialogos.Opcao(Textos.BotaoAbrirLogs, DialogResult.Retry));
        if (r == DialogResult.Retry) AbrirPastaDeLogs();
    }

    private void OnLinhaDoDiario(LinhaDeLog linha)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action<LinhaDeLog>(OnLinhaDoDiario), linha); return; }
        AppendLog(linha);
    }

    private void OpenFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { Aviso("Pasta não encontrada", folder ?? ""); return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Erro("Não consegui abrir a pasta", ex); }
    }

    private void AbrirPastaDeLogs() => OpenFolder(_diario.Pasta ?? Diario.PastaPadrao);

    /// <summary>
    /// Caminho do nvngx_dlss.dll DO KIT — o gabarito que permite reconhecer (e remover)
    /// o transplante que instalações antigas deixaram na pasta do jogo. Desinstalação e
    /// verificação podem rodar sem o kit apontado; aí o valor é nulo e nenhum
    /// nvngx_dlss.dll é tocado.
    /// </summary>
    private string? NvngxDoKit()
    {
        if (_kit?.NvngxDlss is { } pronto) return pronto;
        var pasta = _txtKit.Text.Trim();
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) return null;
        try { _kit = KitResolver.Resolve(pasta); } catch { return null; }
        return _kit.NvngxDlss;
    }

    /// <summary>Gabarito do REFramework do kit: sem ele a faxina não encosta em dinput8.dll.</summary>
    private string? ReFrameworkDoKit()
    {
        if (_kit?.ReFrameworkDinput8 is { } pronto) return pronto;
        var pasta = _txtKit.Text.Trim();
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) return null;
        try { _kit = KitResolver.Resolve(pasta); } catch { return null; }
        return _kit.ReFrameworkDinput8;
    }

    private void ExportarDiagnostico()
    {
        try
        {
            var zip = Diagnostico.Exportar(_diario, _estado);
            Status("Diagnóstico exportado: " + zip);
            Dialogos.Mostrar(this, Textos.TituloDoPrograma, "Diagnóstico exportado",
                "Arquivo gerado:\r\n" + zip + "\r\n\r\nEle contém o log desta sessão, o registro da instalação (se houver) e o relatório de estado. " +
                "Não contém senhas nem dados de conta.", CheckStatusKind.Ok, null,
                new Dialogos.Opcao("Abrir pasta", DialogResult.Yes, Principal: true),
                new Dialogos.Opcao("Fechar", DialogResult.OK));
            OpenFolder(Path.GetDirectoryName(zip));
        }
        catch (Exception ex) { Erro("Não consegui exportar o diagnóstico", ex); }
    }

    private void CarregarPreferencias()
    {
        _txtKit.Text = _settings.KitFolder ?? AppSettings.GuessKitFolder() ?? "";
        _txtGame.Text = _settings.LastGameFolder ?? "";
        if (Enum.TryParse<MvProvider>(_settings.MvProvider, out var mv)) _options.MvProvider = mv;
        _options.OverlayKey = _settings.OverlayKey;
        _options.OverlayCtrl = _settings.OverlayCtrl;
        _options.OverlayShift = _settings.OverlayShift;
        _options.OverlayAlt = _settings.OverlayAlt;
        _cboMv.SelectedIndex = MvProviders.Indice(_options.MvProvider);
        _chkCtrl.Checked = _options.OverlayCtrl;
        _chkShift.Checked = _options.OverlayShift;
        _chkAlt.Checked = _options.OverlayAlt;
        SelectOverlayKey(_options.OverlayKey);
    }

    private void SalvarPreferencias()
    {
        if (Directory.Exists(_txtKit.Text.Trim())) _settings.KitFolder = _txtKit.Text.Trim();
        if (Directory.Exists(_txtGame.Text.Trim())) _settings.LastGameFolder = _txtGame.Text.Trim();
        _settings.MvProvider = _options.MvProvider.ToString();
        if (_profile is not null)
        {
            _settings.Engine = _profile.Engine.ToString();
            _settings.PassCount = _profile.PassCount;
        }
        _settings.OverlayKey = _options.OverlayKey;
        _settings.OverlayCtrl = _options.OverlayCtrl;
        _settings.OverlayShift = _options.OverlayShift;
        _settings.OverlayAlt = _options.OverlayAlt;
        _settings.Save();
    }
}
