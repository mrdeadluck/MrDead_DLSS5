using System.Diagnostics;
using System.Runtime.Versioning;
using Dlss5.Core;

namespace Dlss5.App;

[SupportedOSPlatform("windows")]
public sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly InstallOptions _options = new();

    private GameProfile? _profile;
    private KitInventory? _kit;
    private InstallPlan? _plan;
    private InstallManifest? _manifest;
    private IReadOnlyList<ExeCandidate> _candidates = Array.Empty<ExeCandidate>();

    // Layout
    private readonly Label _stepTitle = new();
    private readonly Label _stepHint = new();
    private readonly Panel _content = new();
    private readonly Button _btnBack = new();
    private readonly Button _btnNext = new();
    private readonly Label _status = new();

    // Passo 0 — pastas
    private readonly TextBox _txtKit = new();
    private readonly TextBox _txtGame = new();

    // Passo 1 — perfil
    private readonly ComboBox _cboExe = new();
    private readonly ComboBox _cboArch = new();
    private readonly ComboBox _cboApi = new();
    private readonly Label _lblNative = new();
    private readonly Label _lblNativeWhy = new();
    private readonly CheckBox _chkDireto = new()
    {
        Text = "Usar o caminho direto do RenoDX (sem Feeder) — experimental: só marque se já viu funcionar",
        AutoSize = false,
        Width = 680,
        Height = 22,
        Visible = false,
    };
    private readonly Button _btnNativeAjustar = Ui.Secondary("Ajustar");
    private readonly TextBox _txtRenderer = new();
    private readonly Label _lblRoute = new();
    private readonly ComboBox _cboMv = new();
    private readonly Label _lblMvNote = new();
    private readonly ComboBox _cboKey = new();
    private readonly CheckBox _chkCtrl = new();
    private readonly CheckBox _chkShift = new();
    private readonly CheckBox _chkAlt = new();
    private readonly Label _lblKeyNote = new();
    private readonly CheckBox _chkRegistry = new();
    private readonly CheckBox _chkClean = new();
    private readonly TextBox _txtNotes = new();

    // Passo 2 — plano
    private readonly ListBox _lstPlan = new();
    private readonly TextBox _txtBlockers = new();

    // Passo 3 — execução
    private readonly TextBox _txtLog = new();

    // Passo 4 — verificação
    private readonly Panel _barraDgVoodoo = new();
    private readonly ComboBox _cboPlaca = new();
    private readonly Label _lblDgVoodoo = new()
    {
        Text = "dgVoodoo finge ser:",
        AutoSize = false,
        Width = 130,
        Height = 26,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private Button _btnPlaca = new();
    private Button _btnPainelDg = new();
    private Button _btnTestarConf = new();
    private readonly CheckBox _chkTnL = new()
    {
        Text = "T&&L por hardware",
        Checked = true,
        AutoSize = false,
        Width = 130,
        Height = 26,
    };
    private EstadoIsolamento _isolamento = EstadoIsolamento.Tudo;
    private bool? _abriuSemDgVoodoo;
    private bool? _abriuSemReShade;
    private readonly DataGridView _grid = new();
    private readonly TextBox _txtGuide = new();

    private readonly Panel _sidebar = new();
    private readonly Label[] _stepLabels = new Label[5];
    private static readonly string[] StepNames =
        { "Pastas", "Detecção", "Plano", "Instalação", "Verificação" };

    private int _step;
    private const int LastStep = 4;

    public MainForm()
    {
        Text = "DLSS 5 AutoInstaller";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(940, 600);
        Size = new Size(1240, 820);
        Font = Ui.BodyFont;
        BackColor = Ui.Page;
        ForeColor = Ui.Ink;

        BuildChrome();
        BuildStep0();
        BuildStep1();
        BuildStep2();
        BuildStep3();
        BuildStep4();

        _txtKit.Text = _settings.KitFolder ?? AppSettings.GuessKitFolder() ?? "";
        _txtGame.Text = _settings.LastGameFolder ?? "";
        if (Enum.TryParse<MvProvider>(_settings.MvProvider, out var mv))
            _options.MvProvider = mv;
        _options.OverlayKey = _settings.OverlayKey;
        _options.OverlayCtrl = _settings.OverlayCtrl;
        _options.OverlayShift = _settings.OverlayShift;
        _options.OverlayAlt = _settings.OverlayAlt;
        _cboMv.SelectedIndex = _options.MvProvider == MvProvider.Drme ? 1 : 0;
        _chkCtrl.Checked = _options.OverlayCtrl;
        _chkShift.Checked = _options.OverlayShift;
        _chkAlt.Checked = _options.OverlayAlt;
        SelectOverlayKey(_options.OverlayKey);

        ShowStep(0);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        EncaixarNoMonitor();
        AjustarQuebraDoTexto();
    }

    /// <summary>
    /// Encolhe a janela até caber na área útil da tela. Roda no OnLoad porque só aí o
    /// Windows já aplicou a escala do monitor: um mínimo definido em pixels de projeto
    /// vira um mínimo bem maior a 125% ou 150%, e a janela acaba não cabendo na tela
    /// nem podendo ser reduzida — foi o que aconteceu num notebook.
    /// </summary>
    private void EncaixarNoMonitor()
    {
        var area = Screen.FromControl(this).WorkingArea;
        const int margem = 40;

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

    /// <summary>A linha de explicação quebra conforme a largura disponível, não num valor fixo.</summary>
    private void AjustarQuebraDoTexto()
    {
        var largura = Math.Max(320, _content.ClientSize.Width - 8);
        if (_stepHint.MaximumSize.Width != largura)
            _stepHint.MaximumSize = new Size(largura, 0);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        AjustarQuebraDoTexto();
    }

    // ---------------------------------------------------------------- chrome

    private void BuildChrome()
    {
        BuildSidebar();

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(24, 20, 24, 16),
            BackColor = Ui.Page,
        };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _stepTitle.Font = Ui.TitleFont;
        _stepTitle.ForeColor = Ui.Ink;
        _stepTitle.AutoSize = true;
        _stepTitle.Margin = new Padding(0, 0, 0, 4);

        // AutoSize + MaximumSize deixa o texto quebrar linha sem ser cortado pela linha AutoSize.
        _stepHint.AutoSize = true;
        _stepHint.MaximumSize = new Size(880, 0);
        _stepHint.ForeColor = Ui.Muted;
        _stepHint.Margin = new Padding(0, 0, 0, 12);

        _content.Dock = DockStyle.Fill;
        _content.BackColor = Ui.Card;
        _content.BorderStyle = BorderStyle.FixedSingle;
        _content.Padding = new Padding(16);

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

        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Ui.Muted;

        Ui.MakeSecondary(_btnBack, "< Voltar");
        _btnBack.Size = new Size(110, 36);
        _btnBack.Margin = new Padding(0, 0, 8, 0);
        _btnBack.Click += (_, _) => ShowStep(_step - 1);

        Ui.MakePrimary(_btnNext, "Avançar >");
        _btnNext.Size = new Size(160, 36);
        _btnNext.Click += async (_, _) => await NextAsync();

        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(_btnBack, 1, 0);
        footer.Controls.Add(_btnNext, 2, 0);

        main.Controls.Add(_stepTitle, 0, 0);
        main.Controls.Add(_stepHint, 0, 1);
        main.Controls.Add(_content, 0, 2);
        main.Controls.Add(footer, 0, 3);

        // TableLayoutPanel em vez de Dock: a posição da barra lateral não depende
        // da ordem em que os controles entram na coleção.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Ui.Page,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(_sidebar, 0, 0);
        root.Controls.Add(main, 1, 0);
        Controls.Add(root);
    }

    private void BuildSidebar()
    {
        _sidebar.Dock = DockStyle.Fill;
        _sidebar.BackColor = Ui.Sidebar;
        _sidebar.Margin = new Padding(0);

        _sidebar.Controls.Add(new Label
        {
            Text = "DLSS 5",
            Font = Ui.BrandFont,
            ForeColor = Color.White,
            AutoSize = false,
            Bounds = new Rectangle(22, 24, 170, 26),
        });
        _sidebar.Controls.Add(new Label
        {
            Text = "AutoInstaller",
            Font = Ui.BodyFont,
            ForeColor = Ui.SidebarIdle,
            AutoSize = false,
            Bounds = new Rectangle(23, 50, 170, 20),
        });

        BuildSidebarFooter();

        int y = 96;
        for (int i = 0; i < StepNames.Length; i++)
        {
            var lbl = new Label
            {
                Text = $"{i + 1}.   {StepNames[i]}",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Ui.StepFont,
                ForeColor = Ui.SidebarIdle,
                Padding = new Padding(14, 0, 0, 0),
                Bounds = new Rectangle(0, y, 210, 36),
            };
            _sidebar.Controls.Add(lbl);
            _stepLabels[i] = lbl;
            y += 40;
        }
    }

    /// <summary>Crédito do autor, preso ao pé da barra lateral.</summary>
    private void BuildSidebarFooter()
    {
        var rodape = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 92,
            BackColor = Ui.Sidebar,
        };

        const int lado = 56;
        var foto = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Ui.Sidebar,
            Bounds = new Rectangle(20, 16, lado, lado),
        };

        var imagem = Ui.LoadAvatar();
        if (imagem is not null)
        {
            foto.Image = imagem;
            // Recorte circular: fica com cara de avatar em vez de foto quadrada.
            var recorte = new System.Drawing.Drawing2D.GraphicsPath();
            recorte.AddEllipse(0, 0, lado, lado);
            foto.Region = new Region(recorte);
        }
        else
        {
            // Sem a imagem embutida, desenha um monograma no lugar.
            foto.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var fundo = new SolidBrush(Ui.Accent);
                e.Graphics.FillEllipse(fundo, 0, 0, lado - 1, lado - 1);
                using var fonte = new Font("Segoe UI", 15F, FontStyle.Bold);
                using var centro = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                e.Graphics.DrawString("MD", fonte, Brushes.White,
                    new RectangleF(0, 0, lado, lado), centro);
            };
        }
        rodape.Controls.Add(foto);

        rodape.Controls.Add(new Label
        {
            Text = "Desenvolvido por",
            Font = Ui.SmallFont,
            ForeColor = Ui.SidebarIdle,
            AutoSize = false,
            Bounds = new Rectangle(88, 26, 116, 16),
        });
        rodape.Controls.Add(new Label
        {
            Text = "MrDead_",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = false,
            Bounds = new Rectangle(88, 42, 116, 24),
        });

        _sidebar.Controls.Add(rodape);
    }

    /// <summary>Marca o passo atual na barra lateral e apaga os que ainda não chegaram.</summary>
    private void UpdateSidebar()
    {
        for (int i = 0; i < _stepLabels.Length; i++)
        {
            bool current = i == _step;
            _stepLabels[i].BackColor = current ? Ui.Accent : Ui.Sidebar;
            _stepLabels[i].ForeColor = current
                ? Color.White
                : (i < _step ? Ui.SidebarDone : Ui.SidebarIdle);
            _stepLabels[i].Font = current ? Ui.StepFontOn : Ui.StepFont;
        }
    }

    private static Label Caption(string text, int top, int left = 0, int width = 220) => new()
    {
        Text = text,
        Top = top + 3,
        Left = left,
        Width = width,
        AutoSize = false,
    };

    private static Button MakeButton(string text, int top, int left, int width, EventHandler onClick)
    {
        var b = Ui.Secondary(text);
        b.SetBounds(left, top - 1, width, 29);
        b.Click += onClick;
        return b;
    }

    // --------------------------------------------------------------- passo 0

    private Panel _p0 = new();

    private void BuildStep0()
    {
        _p0 = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        _p0.Controls.Add(Caption("Pasta do kit (arquivos DLSS 5):", 10, 0, 240));
        _txtKit.SetBounds(250, 8, 560, 25);
        _txtKit.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _p0.Controls.Add(_txtKit);
        _p0.Controls.Add(MakeButton("Procurar...", 8, 820, 100, (_, _) => Pick(_txtKit, "Selecione a pasta com os arquivos do DLSS 5")));

        _p0.Controls.Add(Caption("Pasta do jogo:", 50, 0, 240));
        _txtGame.SetBounds(250, 48, 560, 25);
        _txtGame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _p0.Controls.Add(_txtGame);
        _p0.Controls.Add(MakeButton("Procurar...", 48, 820, 100, (_, _) => Pick(_txtGame, "Selecione a pasta do jogo")));

        // Botão de socorro, disponível ANTES de qualquer detecção: se uma instalação
        // anterior deixou sujeira, é aqui que se resolve — sem manifesto, sem detectar
        // nada, sem depender de o programa lembrar do que fez.
        var limpar = MakeButton("Desfazer tudo nesta pasta", 88, 250, 220, (_, _) => FaxinaCompleta());
        _p0.Controls.Add(limpar);
        _p0.Controls.Add(new Label
        {
            Text = "Deu errado, sobrou arquivo ou o overlay continua aparecendo? Este botão procura e remove "
                 + "tudo que este programa possa ter deixado na pasta do jogo — e devolve ao lugar os arquivos do jogo.",
            ForeColor = Ui.Muted,
            Font = Ui.SmallFont,
            Bounds = new Rectangle(480, 88, 440, 34),
            AutoSize = false,
        });

        var info = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            Top = 132,
            Left = 0,
            Width = 920,
            Height = 343,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Text =
                "Como funciona\r\n" +
                "-----------------------------------------------------------------\r\n" +
                "1. Aponte a pasta do kit (onde estão nvngx_dlssnr.dll, os addons, o dgVoodoo2 e a pasta reshade-shaders).\r\n" +
                "2. Aponte a pasta do jogo. O programa acha o executável real, lê a arquitetura no cabeçalho PE,\r\n" +
                "   deduz a API gráfica e escolhe o caminho de instalação (A, B ou C).\r\n" +
                "3. Ele copia cada arquivo para o lugar certo, gera o ReShade.ini e o preset com o provedor de\r\n" +
                "   motion vectors já marcado ACIMA do DLSS 5 Feed, ajusta o dgVoodoo.conf quando necessário e\r\n" +
                "   aplica o override de assinatura no registro.\r\n" +
                "4. No fim ele verifica tudo que dá para verificar por arquivo/registro e te guia no resto.\r\n\r\n" +
                ManualSteps.Limitations,
        };
        _p0.Controls.Add(info);
    }

    private void Pick(TextBox target, string description)
    {
        using var dlg = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true };
        if (Directory.Exists(target.Text)) dlg.SelectedPath = target.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
    }

    // --------------------------------------------------------------- passo 1

    private Panel _p1 = new();

    private void BuildStep1()
    {
        _p1 = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 8;

        _p1.Controls.Add(Caption("Executável real:", y));
        _cboExe.SetBounds(230, y, 580, 25);
        _cboExe.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboExe.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _cboExe.SelectedIndexChanged += (_, _) => OnExeChanged();
        _p1.Controls.Add(_cboExe);
        _p1.Controls.Add(MakeButton("Outro...", y, 820, 90, (_, _) => BrowseExe()));
        y += 36;

        _p1.Controls.Add(Caption("Arquitetura:", y));
        _cboArch.SetBounds(230, y, 160, 25);
        _cboArch.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboArch.Items.AddRange(new object[] { PeArchitecture.X86, PeArchitecture.X64 });
        _cboArch.SelectedIndexChanged += (_, _) => SyncProfileFromUi();
        _p1.Controls.Add(_cboArch);

        _p1.Controls.Add(Caption("API gráfica:", y, 420, 110));
        _cboApi.SetBounds(540, y, 160, 25);
        _cboApi.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboApi.Items.AddRange(new object[]
        {
            GraphicsApi.D3D8, GraphicsApi.D3D9, GraphicsApi.D3D11, GraphicsApi.D3D12,
            GraphicsApi.Vulkan, GraphicsApi.OpenGL, GraphicsApi.D3D10,
        });
        _cboApi.SelectedIndexChanged += (_, _) => SyncProfileFromUi();
        _p1.Controls.Add(_cboApi);
        y += 36;

        // Isto NÃO é uma pergunta. É resultado de leitura de arquivo, e o usuário não tem
        // como saber a resposta melhor que o programa — ele nem deveria precisar pensar
        // nisso. Fica como veredito com a razão do lado, e só muda por decisão explícita.
        _p1.Controls.Add(Caption("DLSS nativo do jogo:", y));
        _lblNative.SetBounds(230, y + 3, 500, 20);
        _lblNative.Font = Ui.BoldFont;
        _p1.Controls.Add(_lblNative);

        _btnNativeAjustar.SetBounds(740, y - 1, 90, 27);
        _btnNativeAjustar.Click += (_, _) => AjustarDlssNativo();
        _p1.Controls.Add(_btnNativeAjustar);
        y += 30;

        _lblNativeWhy.SetBounds(230, y, 680, 32);
        _lblNativeWhy.ForeColor = Ui.Muted;
        _lblNativeWhy.Font = Ui.SmallFont;
        _p1.Controls.Add(_lblNativeWhy);
        y += 36;

        // Empírico, não teórico: o Feeder funcionou em 30+ jogos; o caminho direto
        // registrou "ok" no log com a tela inalterada. Então o Feeder é o padrão em
        // TODO caso, e o direto só entra por escolha consciente.
        _chkDireto.Location = new Point(230, y);
        _chkDireto.CheckedChanged += (_, _) => SyncProfileFromUi();
        _p1.Controls.Add(_chkDireto);
        y += 28;

        _p1.Controls.Add(Caption("Pasta do renderizador:", y));
        _txtRenderer.SetBounds(230, y, 580, 25);
        _txtRenderer.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _txtRenderer.TextChanged += (_, _) => SyncProfileFromUi();
        _p1.Controls.Add(_txtRenderer);
        y += 34;

        _lblRoute.SetBounds(230, y, 680, 46);
        _lblRoute.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _p1.Controls.Add(_lblRoute);
        y += 54;

        _p1.Controls.Add(Caption("Motion vectors:", y));
        _cboMv.SetBounds(230, y, 260, 25);
        _cboMv.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboMv.Items.AddRange(new object[] { "Launchpad (iMMERSE)", "DRME (MotionEstimation)" });
        _cboMv.SelectedIndex = 0;
        _cboMv.SelectedIndexChanged += (_, _) =>
            _options.MvProvider = _cboMv.SelectedIndex == 1 ? MvProvider.Drme : MvProvider.Launchpad;
        _p1.Controls.Add(_cboMv);

        _lblMvNote.SetBounds(500, y + 5, 415, 20);
        _lblMvNote.ForeColor = SystemColors.GrayText;
        _p1.Controls.Add(_lblMvNote);
        y += 34;

        _p1.Controls.Add(Caption("Tecla do overlay:", y));
        _cboKey.SetBounds(230, y, 260, 25);
        _cboKey.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboKey.MaxDropDownItems = 18;
        foreach (var k in ReShadeConfigWriter.OverlayKeys) _cboKey.Items.Add(k.Label);
        _cboKey.SelectedIndex = 0;
        _cboKey.SelectedIndexChanged += (_, _) => SyncOverlayKeyFromUi();
        _p1.Controls.Add(_cboKey);

        _chkCtrl.Text = "Ctrl";
        _chkCtrl.SetBounds(500, y + 3, 52, 22);
        _chkCtrl.CheckedChanged += (_, _) => SyncOverlayKeyFromUi();
        _p1.Controls.Add(_chkCtrl);

        _chkShift.Text = "Shift";
        _chkShift.SetBounds(556, y + 3, 56, 22);
        _chkShift.CheckedChanged += (_, _) => SyncOverlayKeyFromUi();
        _p1.Controls.Add(_chkShift);

        _chkAlt.Text = "Alt";
        _chkAlt.SetBounds(616, y + 3, 46, 22);
        _chkAlt.CheckedChanged += (_, _) => SyncOverlayKeyFromUi();
        _p1.Controls.Add(_chkAlt);

        _lblKeyNote.SetBounds(668, y + 5, 247, 20);
        _lblKeyNote.ForeColor = SystemColors.GrayText;
        _p1.Controls.Add(_lblKeyNote);
        y += 36;

        _chkRegistry.Text = "Aplicar o override de assinatura NGX no registro (na 1ª vez, reinicie o PC quando puder)";
        _chkRegistry.SetBounds(230, y, 640, 24);
        _chkRegistry.Checked = true;
        _chkRegistry.CheckedChanged += (_, _) => _options.ApplyRegistryOverride = _chkRegistry.Checked;
        _p1.Controls.Add(_chkRegistry);
        y += 28;

        _chkClean.Text = "Remover restos de instalação anterior (nunca mexe em arquivo do jogo)";
        _chkClean.SetBounds(230, y, 640, 24);
        _chkClean.Checked = true;
        _chkClean.CheckedChanged += (_, _) => _options.CleanForbidden = _chkClean.Checked;
        _p1.Controls.Add(_chkClean);
        y += 34;

        Ui.StyleReadOnlyBox(_txtNotes);
        _txtNotes.SetBounds(0, y, 920, 150);
        _txtNotes.MinimumSize = new Size(0, 110);
        _txtNotes.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _p1.Controls.Add(_txtNotes);
    }

    private void BrowseExe()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Executáveis (*.exe)|*.exe",
            Title = "Selecione o executável real do jogo",
            InitialDirectory = _profile?.GameFolder ?? "",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var arch = PeFile.GetArchitecture(dlg.FileName);
        long size = 0;
        try { size = new FileInfo(dlg.FileName).Length; } catch { }
        var extra = new ExeCandidate(dlg.FileName, arch, size, int.MaxValue);
        _candidates = _candidates.Concat(new[] { extra }).ToList();
        PopulateCandidates(extra.Path);
    }

    private void PopulateCandidates(string? selectPath)
    {
        _cboExe.Items.Clear();
        foreach (var c in _candidates.OrderByDescending(c => c.Score))
        {
            var rel = _profile is null ? c.Path : SafeRelative(_profile.GameFolder, c.Path);
            _cboExe.Items.Add($"{rel}   [{c.Arch}, {c.Size / 1024:N0} KB]");
        }
        var ordered = _candidates.OrderByDescending(c => c.Score).ToList();
        int idx = selectPath is null
            ? 0
            : ordered.FindIndex(c => string.Equals(c.Path, selectPath, StringComparison.OrdinalIgnoreCase));
        if (_cboExe.Items.Count > 0)
            _cboExe.SelectedIndex = idx < 0 ? 0 : idx;
    }

    private static string SafeRelative(string base_, string full)
    {
        try { return Path.GetRelativePath(base_, full); } catch { return full; }
    }

    private void OnExeChanged()
    {
        if (_profile is null || _cboExe.SelectedIndex < 0) return;
        var ordered = _candidates.OrderByDescending(c => c.Score).ToList();
        if (_cboExe.SelectedIndex >= ordered.Count) return;
        var chosen = ordered[_cboExe.SelectedIndex];

        _profile.RealExePath = chosen.Path;
        _profile.Architecture = chosen.Arch;
        _cboArch.SelectedItem = chosen.Arch;
        if (string.IsNullOrWhiteSpace(_txtRenderer.Text) ||
            !Directory.Exists(_txtRenderer.Text))
            _txtRenderer.Text = _profile.ExeFolder;
        SyncProfileFromUi();
    }

    private void SyncOverlayKeyFromUi()
    {
        int idx = _cboKey.SelectedIndex;
        if (idx >= 0 && idx < ReShadeConfigWriter.OverlayKeys.Count)
            _options.OverlayKey = ReShadeConfigWriter.OverlayKeys[idx].VirtualKey;
        _options.OverlayCtrl = _chkCtrl.Checked;
        _options.OverlayShift = _chkShift.Checked;
        _options.OverlayAlt = _chkAlt.Checked;
        _lblKeyNote.Text = "= " + _options.OverlayKeyLabel;
    }

    private void SelectOverlayKey(int virtualKey)
    {
        var keys = ReShadeConfigWriter.OverlayKeys;
        int idx = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].VirtualKey != virtualKey) continue;
            idx = i;
            break;
        }
        _cboKey.SelectedIndex = idx;
    }

    /// <summary>
    /// Com DLSS nativo o Feeder não entra, então o provedor de motion vectors não é usado —
    /// deixar o campo ligado só sugeriria uma escolha que não tem efeito nenhum.
    /// </summary>
    private void UpdateMvAvailability()
    {
        bool feederUsed = _profile is null || _profile.NeedsFeeder;
        _cboMv.Enabled = feederUsed;
        _lblMvNote.Text = feederUsed
            ? string.Empty
            : "não usado: em D3D12 com DLSS nativo quem trabalha é o RenoDX";
    }

    private void SyncProfileFromUi()
    {
        if (_profile is null) return;
        if (_cboArch.SelectedItem is PeArchitecture a) _profile.Architecture = a;
        if (_cboApi.SelectedItem is GraphicsApi g) _profile.Api = g;
        if (!string.IsNullOrWhiteSpace(_txtRenderer.Text)) _profile.RendererFolder = _txtRenderer.Text;
        _profile.MvProvider = _options.MvProvider;
        _profile.PreferirCaminhoDireto = _chkDireto.Visible && _chkDireto.Checked;
        UpdateMvAvailability();
        UpdateNativeLabel();
        UpdateRouteLabel();
    }

    /// <summary>Mostra o veredito do detector e a evidência que o sustenta.</summary>
    private void UpdateNativeLabel()
    {
        if (_profile is null) return;
        bool sim = _profile.HasNativeDlss;

        _lblNative.Text = (sim ? "SIM" : "NÃO") +
                          (_profile.NativeDlssOverridden ? "  (alterado por você)" : "  (detectado)");
        _lblNative.ForeColor = _profile.NativeDlssOverridden ? Ui.Warn : (sim ? Ui.Ok : Ui.Ink);

        var porque = _profile.NativeDlss?.Resumo ?? "sem detecção";
        _lblNativeWhy.Text = porque + Environment.NewLine + ConsequenciaDoNativo();

        _chkDireto.Visible = _profile.HasNativeDlss && _profile.Api == GraphicsApi.D3D12;
    }

    /// <summary>
    /// O que essa resposta muda de verdade. Sem isso ela parece um interruptor perigoso —
    /// e ela não é: o programa não apaga arquivo de DLSS do jogo em nenhum dos dois casos.
    /// </summary>
    private string ConsequenciaDoNativo()
    {
        if (_profile is null) return string.Empty;
        return _profile.UsesRenodxDirectPath
            ? "Efeito: o Feeder não é instalado — em D3D12 o RenoDX se pendura no DLSS do próprio jogo."
            : "Efeito: o Feeder é instalado (é ele quem roda o DLSS 5). Arquivos de DLSS do jogo nunca são apagados.";
    }

    /// <summary>Deixa contrariar o detector, mas só de propósito e sabendo o que muda.</summary>
    private void AjustarDlssNativo()
    {
        if (_profile is null) { Warn("Faça a detecção primeiro."); return; }

        bool novo = !_profile.HasNativeDlss;
        var texto =
            $"O programa detectou: {(_profile.HasNativeDlss ? "SIM" : "NÃO")}.\r\n" +
            $"Motivo: {_profile.NativeDlss?.Resumo ?? "sem detecção"}.\r\n\r\n" +
            $"Mudar para {(novo ? "SIM" : "NÃO")}?\r\n\r\n" +
            "O que muda: apenas se o Feeder é instalado ou não. Nenhum arquivo do jogo é " +
            "apagado em nenhum dos dois casos.\r\n\r\n" +
            "Na dúvida, deixe como está — a detecção lê os arquivos do jogo, não chuta.";

        if (MessageBox.Show(this, texto, "Ajustar DLSS nativo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _profile.HasNativeDlss = novo;
        _profile.NativeDlssOverridden = true;
        SyncProfileFromUi();
    }

    private void UpdateRouteLabel()
    {
        if (_profile is null) return;
        var route = _profile.Route;
        _lblRoute.ForeColor = route == InstallRoute.Unsupported ? Color.Firebrick : Color.DarkGreen;
        _lblRoute.Text = route switch
        {
            InstallRoute.A => $"Caminho A — 64-bit: ReShade ({_profile.ReShadeHookName}) + addons direto na pasta do executável.",
            InstallRoute.B => "Caminho B — 32-bit D3D11: addon32 na raiz e o resto do Feeder dentro de host64\\.",
            InstallRoute.C => $"Caminho C — 32-bit {_profile.Api}: dgVoodoo2 ({_profile.DgVoodooWrapperName}) traduz para D3D11, mais o layout do caminho B.",
            _ => "Sem caminho suportado para esta combinação. " +
                 (_profile.Architecture == PeArchitecture.X86 &&
                  _profile.Api is GraphicsApi.Vulkan or GraphicsApi.OpenGL
                     ? $"Jogo 32-bit em {_profile.Api} não funciona (o addon32 só aceita D3D11): troque para D3D9 ou D3D11 se o jogo permitir."
                     : "Confira arquitetura e API."),
        };
    }

    // --------------------------------------------------------------- passo 2

    private Panel _p2 = new();

    private void BuildStep2()
    {
        _p2 = new Panel { Dock = DockStyle.Fill };

        Ui.StyleReadOnlyBox(_txtBlockers);
        _txtBlockers.ForeColor = Ui.Bad;
        _txtBlockers.Dock = DockStyle.Top;
        _txtBlockers.Height = 90;
        _txtBlockers.Visible = false;

        _lstPlan.Dock = DockStyle.Fill;
        _lstPlan.IntegralHeight = false;
        _lstPlan.BorderStyle = BorderStyle.FixedSingle;
        _lstPlan.BackColor = Ui.Card;
        _lstPlan.ForeColor = Ui.Ink;
        _lstPlan.Font = Ui.BodyFont;

        _p2.Controls.Add(_lstPlan);
        _p2.Controls.Add(_txtBlockers);
    }

    // --------------------------------------------------------------- passo 3

    private Panel _p3 = new();

    private void BuildStep3()
    {
        _p3 = new Panel { Dock = DockStyle.Fill };
        Ui.StyleReadOnlyBox(_txtLog, mono: true);
        _txtLog.ScrollBars = ScrollBars.Both;
        _txtLog.WordWrap = false;
        _txtLog.Dock = DockStyle.Fill;
        _p3.Controls.Add(_txtLog);
    }

    // --------------------------------------------------------------- passo 4

    private Panel _p4 = new();

    private void BuildStep4()
    {
        _p4 = new Panel { Dock = DockStyle.Fill };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 120,
            Panel2MinSize = 120,
        };
        // SplitterDistance só é válido depois que o controle tem tamanho real.
        split.HandleCreated += (_, _) =>
        {
            try { split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Height / 2); }
            catch (InvalidOperationException) { /* tamanho ainda incompatível: fica no padrão */ }
            catch (ArgumentOutOfRangeException) { }
        };

        // DataGridView e não ListView: o ListView em modo Details corta o texto, e é
        // justamente na coluna de detalhe/correção que está o que precisa ser lido.
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
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 34;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 245, 249);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Muted;
        _grid.ColumnHeadersDefaultCellStyle.Font = Ui.BoldFont;
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
        _grid.DefaultCellStyle.Font = Ui.BodyFont;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.DefaultCellStyle.Padding = new Padding(6, 6, 6, 6);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(233, 241, 251);
        _grid.DefaultCellStyle.SelectionForeColor = Ui.Ink;

        _grid.ColumnCount = 4;
        _grid.Columns[0].HeaderText = "Estado";
        _grid.Columns[0].FillWeight = 11;
        _grid.Columns[1].HeaderText = "Verificação";
        _grid.Columns[1].FillWeight = 25;
        _grid.Columns[2].HeaderText = "Detalhe";
        _grid.Columns[2].FillWeight = 35;
        _grid.Columns[3].HeaderText = "Como corrigir";
        _grid.Columns[3].FillWeight = 29;

        Ui.StyleReadOnlyBox(_txtGuide);
        _txtGuide.Dock = DockStyle.Fill;

        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_txtGuide);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 8, 0, 0) };
        bar.Controls.Add(MakeButton("Verificar de novo", 8, 0, 150, (_, _) => RunVerification()));
        bar.Controls.Add(MakeButton("Abrir pasta do jogo", 8, 0, 150, (_, _) => OpenFolder(_profile?.ExeFolder)));
        bar.Controls.Add(MakeButton("Abrir o jogo", 8, 0, 120, (_, _) => LaunchGame()));
        bar.Controls.Add(MakeButton("Desinstalar (reverter)", 8, 0, 170, (_, _) => RevertInstall()));
        bar.Controls.Add(MakeButton("Desfazer tudo (forçado)", 8, 0, 170, (_, _) => FaxinaCompleta()));
        bar.Controls.Add(MakeButton("Reiniciar o PC (opcional)", 8, 0, 180, (_, _) => ReiniciarSeOUsuarioQuiser()));

        // Trocar a placa que o dgVoodoo finge ser é o ajuste que resolve jogo antigo que
        // recusa o adaptador, e não dá para saber de antemão qual valor cada jogo aceita.
        // Aqui a troca é reescrita direto no conf: o teste seguinte é reabrir o jogo, sem
        // desinstalar e instalar tudo de novo a cada tentativa.
        var barraDg = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(0, 6, 0, 0) };
        barraDg.Controls.Add(MakeButton("Isolar a causa", 4, 0, 130, (_, _) => IsolarCausa()));
        barraDg.Controls.Add(_lblDgVoodoo);
        _lblDgVoodoo.Visible = false;
        _cboPlaca.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboPlaca.Width = 250;
        foreach (var (rotulo, _) in DgVoodooConfigurator.Placas) _cboPlaca.Items.Add(rotulo);
        _cboPlaca.SelectedIndex = 0;
        barraDg.Controls.Add(_cboPlaca);
        barraDg.Controls.Add(_chkTnL);
        _btnPlaca = MakeButton("Aplicar e testar", 4, 0, 140, (_, _) => TrocarPlacaDgVoodoo());
        _btnPainelDg = MakeButton("Painel do dgVoodoo", 4, 0, 150, (_, _) => AbrirPainelDgVoodoo());
        _btnTestarConf = MakeButton("O conf é lido?", 4, 0, 130, (_, _) => TestarLeituraDoConf());
        barraDg.Controls.Add(_btnPlaca);
        barraDg.Controls.Add(_btnTestarConf);
        barraDg.Controls.Add(_btnPainelDg);

        barraDg.Dock = DockStyle.Fill;
        _barraDgVoodoo.Dock = DockStyle.Bottom;
        _barraDgVoodoo.Height = 80;   // duas linhas: em janela estreita a barra quebra
        _barraDgVoodoo.Controls.Add(barraDg);

        _p4.Controls.Add(split);
        _p4.Controls.Add(bar);
        _p4.Controls.Add(_barraDgVoodoo);
    }

    /// <summary>
    /// Bisseção: desliga uma peça de cada vez e diz o que cada resultado significa.
    /// Quando o jogo nem abre, os suspeitos são três — dgVoodoo, ReShade e o próprio jogo
    /// — e trocar configuração no escuro não conclui nada. Renomear é reversível.
    /// </summary>
    private void IsolarCausa()
    {
        if (_profile is null) { Warn("Faça a detecção primeiro."); return; }

        var rodando = ProcessoDoJogoRodando(_profile.RealExePath);
        if (rodando is not null)
        {
            Warn($"Feche o jogo ({rodando}.exe) antes: arquivo em uso não é renomeado.");
            return;
        }

        // Antes de avançar, colhe o resultado do teste que estava em andamento. Sem isso o
        // usuário atravessa as etapas e chega ao fim sem conclusão nenhuma — foi o que
        // aconteceu no primeiro uso: o teste 2 passou batido.
        if (_isolamento != EstadoIsolamento.Tudo)
        {
            var pergunta = _isolamento == EstadoIsolamento.SemDgVoodoo
                ? "Com o dgVoodoo DESLIGADO, o jogo abriu?"
                : "Com o ReShade DESLIGADO (e o dgVoodoo ligado), o jogo abriu?";
            var abriu = MessageBox.Show(this, pergunta, "Resultado do teste",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

            if (_isolamento == EstadoIsolamento.SemDgVoodoo) _abriuSemDgVoodoo = abriu;
            else _abriuSemReShade = abriu;
        }
        else
        {
            // Começando uma rodada nova: as respostas antigas não valem mais.
            _abriuSemDgVoodoo = null;
            _abriuSemReShade = null;
        }

        var proximo = _isolamento switch
        {
            EstadoIsolamento.Tudo => EstadoIsolamento.SemDgVoodoo,
            EstadoIsolamento.SemDgVoodoo => EstadoIsolamento.SemReShade,
            _ => EstadoIsolamento.Tudo,
        };

        // Sem dgVoodoo na jogada, o teste dele não existe: pula direto para o ReShade.
        if (proximo == EstadoIsolamento.SemDgVoodoo && !_profile.NeedsDgVoodoo)
            proximo = EstadoIsolamento.SemReShade;

        // Se o jogo já não abre sem o dgVoodoo, a instalação está fora de suspeita e o
        // teste seguinte não acrescenta nada.
        if (proximo == EstadoIsolamento.SemReShade && _abriuSemDgVoodoo is false)
            proximo = EstadoIsolamento.Tudo;

        ShowStep(3);
        _txtLog.Clear();
        try
        {
            var iso = new Isolamento(Log);
            iso.Aplicar(proximo, _profile.ExeFolder, _profile.RendererFolder ?? _profile.ExeFolder);
            _isolamento = proximo;

            _status.Text = proximo switch
            {
                EstadoIsolamento.SemDgVoodoo => "Teste 1 de 2: dgVoodoo desligado. Abra o jogo e volte aqui.",
                EstadoIsolamento.SemReShade => "Teste 2 de 2: ReShade desligado. Abra o jogo e volte aqui.",
                _ => "Instalação religada por inteiro.",
            };

            var texto = proximo == EstadoIsolamento.Tudo
                ? Isolamento.Veredito(_abriuSemDgVoodoo, _abriuSemReShade) +
                  "\r\n\r\n" + Isolamento.Leitura(proximo)
                : Isolamento.Leitura(proximo) +
                  "\r\n\r\nDepois de abrir o jogo, clique em \"Isolar a causa\" de novo: " +
                  "ele pergunta o que aconteceu e passa ao próximo teste.";

            MessageBox.Show(this, texto, "Isolar a causa",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { Warn(ex.Message); }
    }

    /// <summary>
    /// Descobre se o dgVoodoo está mesmo lendo o dgVoodoo.conf que geramos.
    ///
    /// Toda a afinação até aqui — placa, T&L, resoluções, VRAM — parte de uma suposição
    /// que nunca foi verificada: a de que o arquivo é lido. Se não for, o wrapper roda com
    /// os padrões dele e nada do que foi escrito teve efeito, o que explicaria por que
    /// nenhuma tentativa mudou coisa alguma.
    ///
    /// O teste usa DisableAndPassThru: com ele em true o dgVoodoo sai da frente e repassa
    /// tudo ao Direct3D do Windows, então o jogo TEM que abrir. Se não abrir, o arquivo
    /// não está sendo lido — e o rumo passa a ser outro.
    /// </summary>
    private void TestarLeituraDoConf()
    {
        var pasta = _profile?.RendererFolder ?? _profile?.ExeFolder;
        var conf = string.IsNullOrWhiteSpace(pasta) ? null : Path.Combine(pasta, "dgVoodoo.conf");
        if (conf is null || !File.Exists(conf)) { Warn($"dgVoodoo.conf não está em {pasta}."); return; }

        var rodando = ProcessoDoJogoRodando(_profile?.RealExePath);
        if (rodando is not null) { Warn($"Feche o jogo ({rodando}.exe) antes."); return; }

        try
        {
            var texto = File.ReadAllText(conf);
            bool emTeste = string.Equals(
                DgVoodooConfigurator.LerChave(texto, "DirectX", "DisableAndPassThru"),
                "true", StringComparison.OrdinalIgnoreCase);

            if (!emTeste)
            {
                File.WriteAllText(conf,
                    DgVoodooConfigurator.DefinirChave(texto, "DirectX", "DisableAndPassThru", "true"));
                _status.Text = "Passthru ligado. Abra o jogo e volte a clicar em \"O conf é lido?\".";
                MessageBox.Show(this,
                    "Gravei DisableAndPassThru = true.\r\n\r\n" +
                    "Com isso o dgVoodoo repassa tudo ao Direct3D do Windows — ele sai da frente " +
                    "sem sair da pasta. Se o conf estiver sendo lido, o jogo TEM que abrir.\r\n\r\n" +
                    "Abra o jogo agora e clique neste botão de novo.",
                    "O conf é lido?", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool abriu = MessageBox.Show(this, "Com o passthru ligado, o jogo abriu?",
                "O conf é lido?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

            File.WriteAllText(conf,
                DgVoodooConfigurator.DefinirChave(File.ReadAllText(conf), "DirectX", "DisableAndPassThru", "false"));

            _status.Text = abriu
                ? "O conf é lido: os ajustes daqui têm efeito."
                : "O conf NÃO é lido: os ajustes daqui são inertes.";

            MessageBox.Show(this, abriu
                ? "O dgVoodoo LÊ o conf.\r\n\r\nEntão placa, T&L e resoluções realmente têm " +
                  "efeito, e vale varrer as combinações: para cada placa da lista, teste com a " +
                  "caixa \"T&L por hardware\" marcada e desmarcada.\r\n\r\n" +
                  "Passthru devolvido para false."
                : "O dgVoodoo NÃO está lendo o conf.\r\n\r\nCom passthru=true ele deveria ter " +
                  "saído da frente e o jogo deveria abrir. Como não abriu, o arquivo não chega " +
                  "até ele — e TUDO que foi ajustado aqui até agora foi inerte.\r\n\r\n" +
                  "Isso muda o rumo: o problema deixa de ser qual valor usar e passa a ser o " +
                  "arquivo não ser encontrado. Me mande este resultado.\r\n\r\n" +
                  "Passthru devolvido para false.",
                "O conf é lido?", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { Warn(ex.Message); }
    }

    /// <summary>Reescreve o dgVoodoo.conf com outra placa, sem reinstalar nada.</summary>
    private void TrocarPlacaDgVoodoo()
    {
        var pasta = _profile?.RendererFolder ?? _profile?.ExeFolder;
        var conf = string.IsNullOrWhiteSpace(pasta) ? null : Path.Combine(pasta, "dgVoodoo.conf");
        if (conf is null || !File.Exists(conf))
        {
            Warn($"dgVoodoo.conf não está em {pasta}. Instale primeiro.");
            return;
        }

        var rodando = ProcessoDoJogoRodando(_profile?.RealExePath);
        if (rodando is not null)
        {
            Warn($"Feche o jogo ({rodando}.exe) antes: ele lê o conf ao abrir.");
            return;
        }

        int idx = _cboPlaca.SelectedIndex;
        if (idx < 0 || idx >= DgVoodooConfigurator.Placas.Count) return;
        var (rotulo, valor) = DgVoodooConfigurator.Placas[idx];

        try
        {
            var perfil = DgVoodooConfigurator.ProfileFor(_profile!.Api);
            var texto = DgVoodooConfigurator.Patch(File.ReadAllText(conf), perfil, valor, _chkTnL.Checked);
            File.WriteAllText(conf, texto);
            _status.Text = $"dgVoodoo agora se apresenta como {rotulo}. Abra o jogo e veja se muda.";
            MessageBox.Show(this,
                $"Gravado: VideoCard = {valor}\r\n" +
                $"T&L por hardware: {(_chkTnL.Checked ? "sim" : "não")}\r\n{conf}\r\n\r\n" +
                "Abra o jogo. Se continuar recusando o adaptador, volte aqui e tente a próxima " +
                "da lista — nada precisa ser reinstalado entre uma tentativa e outra.",
                "Placa trocada", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { Warn("Não consegui gravar o dgVoodoo.conf: " + ex.Message); }
    }

    private void OpenFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Warn(ex.Message); }
    }

    private void LaunchGame()
    {
        if (_profile?.RealExePath is null || !File.Exists(_profile.RealExePath)) return;
        try
        {
            // Jogo com DRM da Steam recusa ser aberto pelo .exe ("Application load error
            // 5:0000065434"): o wrapper exige ter sido lançado pelo cliente. Então quando
            // o jogo está numa biblioteca da Steam, quem abre é a Steam.
            var appId = SteamGame.FindAppId(_profile.GameFolder);
            if (appId is not null)
            {
                Process.Start(new ProcessStartInfo(SteamGame.RunUrl(appId)) { UseShellExecute = true });
                _status.Text = $"Pedido à Steam para abrir o jogo (AppID {appId}). " +
                               "Depois de fechar, clique em Verificar de novo.";
                return;
            }

            Process.Start(new ProcessStartInfo(_profile.RealExePath)
            {
                UseShellExecute = true,
                WorkingDirectory = _profile.ExeFolder,
            });
            _status.Text = "Jogo iniciado. Depois de fechar, clique em Verificar de novo para ler os logs.";
        }
        catch (Exception ex) { Warn("Não consegui abrir o jogo: " + ex.Message); }
    }

    /// <summary>Nome do processo se o jogo estiver rodando, senão null.</summary>
    private static string? ProcessoDoJogoRodando(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        var nome = Path.GetFileNameWithoutExtension(exePath);
        try
        {
            return Process.GetProcessesByName(nome).Length > 0 ? nome : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Caminho do nvngx_dlss.dll DO KIT — o gabarito que permite reconhecer (e remover)
    /// o transplante que instalações antigas deixaram na pasta do jogo. A faxina e a
    /// verificação podem rodar antes da detecção, então o kit é resolvido aqui sob
    /// demanda a partir da pasta digitada.
    /// </summary>
    private string? NvngxDoKit()
    {
        if (_kit?.NvngxDlss is { } pronto) return pronto;
        var pasta = _txtKit.Text.Trim();
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) return null;
        _kit = KitResolver.Resolve(pasta);
        return _kit.NvngxDlss;
    }

    private void RevertInstall()
    {
        if (_profile is null) return;

        var manifest = _manifest ?? InstallManifest.Find(_profile.GameFolder, _profile.ExeFolder);
        bool semManifesto = manifest is null;
        if (semManifesto)
        {
            // Sem manifesto ainda dá para desfazer o essencial: devolver os backups e
            // limpar o que ficou. Recusar aqui deixaria o jogo quebrado sem saída.
            manifest = new InstallManifest
            {
                GameFolder = _profile.GameFolder,
                ExeFolder = _profile.ExeFolder,
            };
        }

        // Arquivo aberto pelo jogo não é apagado, e a falha vira só uma linha no log.
        var rodando = ProcessoDoJogoRodando(_profile.RealExePath);
        if (rodando is not null)
        {
            Warn($"O jogo parece estar aberto ({rodando}.exe). Feche o jogo e a Steam " +
                 "antes de desinstalar, senão os arquivos ficam travados e não saem.");
            return;
        }

        var aviso = semManifesto
            ? "Não achei o manifesto desta instalação.\r\n\r\nMesmo assim posso devolver ao lugar " +
              "todo arquivo do jogo que tenha sido movido para backup (.dlss5bak) e apagar o que o " +
              "ReShade deixou para trás.\r\n\r\nContinuar?"
            : "Isto vai remover os arquivos instalados, devolver os backups ao lugar e desfazer o " +
              "override do registro.\r\n\r\nContinuar?";

        if (MessageBox.Show(this, aviso, "Reverter instalação",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        var engine = new InstallerEngine(Log) { NvngxDlssDoKit = NvngxDoKit() };
        ShowStep(3);
        _txtLog.Clear();
        try
        {
            var sobras = engine.Revert(manifest!, removeRegistryOverride: !semManifesto);
            _manifest = null;

            if (sobras.Count > 0)
            {
                // Arquivo em uso não é apagado. Sem dizer isso na cara, o usuário só
                // descobre quando o overlay do ReShade aparece no jogo.
                _status.Text = $"{sobras.Count} arquivo(s) não saíram — veja o log.";
                var resposta = MessageBox.Show(this,
                    "A remoção não conseguiu apagar tudo:\r\n\r\n" +
                    string.Join("\r\n", sobras.Take(12)) +
                    (sobras.Count > 12 ? $"\r\n... e mais {sobras.Count - 12}" : "") +
                    "\r\n\r\nQuase sempre é arquivo em uso — feche o jogo E a Steam.\r\n\r\n" +
                    "Quer que eu procure e remova tudo na pasta do jogo inteira agora?",
                    "Sobraram arquivos", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (resposta == DialogResult.Yes) FaxinaCompleta();
            }
            else
            {
                _status.Text = semManifesto
                    ? "Backups devolvidos e restos apagados. Confira as opções do jogo."
                    : "Reversão concluída. Reinicie o PC para o override sair de vez.";
            }
        }
        catch (Exception ex) { Warn(ex.Message); }
    }

    /// <summary>
    /// Abre o painel do dgVoodoo já na pasta certa. É por ele que se troca a placa que o
    /// wrapper finge ser — o ajuste que resolve o jogo antigo que recusa o adaptador — e
    /// a troca vale na hora, sem reinstalar nada.
    /// </summary>
    private void AbrirPainelDgVoodoo()
    {
        var pasta = _profile?.RendererFolder ?? _profile?.ExeFolder;
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta))
        {
            Warn("Faça a detecção primeiro.");
            return;
        }

        var cpl = Path.Combine(pasta, "dgVoodooCpl.exe");
        if (!File.Exists(cpl))
        {
            Warn($"dgVoodooCpl.exe não está em {pasta}. Ele só é instalado nos jogos que " +
                 "precisam do dgVoodoo (32-bit em DirectX 8 ou 9).");
            return;
        }

        try
        {
            // O painel edita o dgVoodoo.conf da pasta de onde é aberto: sem o
            // WorkingDirectory certo ele mexeria no arquivo errado.
            Process.Start(new ProcessStartInfo(cpl) { UseShellExecute = true, WorkingDirectory = pasta });
            _status.Text = "Painel do dgVoodoo aberto. Aba DirectX → VideoCard. Salve e reabra o jogo.";
        }
        catch (Exception ex) { Warn("Não consegui abrir o painel: " + ex.Message); }
    }

    /// <summary>
    /// Desfaz a instalação varrendo a pasta do jogo, sem depender de manifesto nem de
    /// detecção. É o botão que faltava quando a reversão normal não deu conta e a única
    /// saída foi apagar arquivo por arquivo no Explorer.
    /// </summary>
    private void FaxinaCompleta()
    {
        var pasta = _profile?.GameFolder ?? _txtGame.Text.Trim();
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta))
        {
            Warn("Aponte a pasta do jogo primeiro.");
            return;
        }

        var rodando = ProcessoDoJogoRodando(_profile?.RealExePath);
        if (rodando is not null)
        {
            Warn($"O jogo parece estar aberto ({rodando}.exe). Feche o jogo e a Steam antes: " +
                 "arquivo em uso não é apagado.");
            return;
        }

        var engine = new InstallerEngine(Log) { NvngxDlssDoKit = NvngxDoKit() };

        // Varrer a pasta de um jogo grande leva alguns segundos; sem isso a janela
        // parece travada.
        _status.Text = "Procurando arquivos deste programa na pasta do jogo...";
        IReadOnlyList<string> achados;
        UseWaitCursor = true;
        Application.DoEvents();
        try { achados = engine.EncontrarInstalacao(pasta); }
        finally { UseWaitCursor = false; }

        if (achados.Count == 0)
        {
            MessageBox.Show(this,
                $"Não achei nada deste programa em:\r\n{pasta}\r\n\r\n" +
                "A pasta está limpa. Se o jogo ainda se comporta como se o ReShade estivesse " +
                "instalado, confira se você apontou a pasta certa.",
                "Nada a remover", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var lista = string.Join("\r\n", achados.Take(20)) +
                    (achados.Count > 20 ? $"\r\n... e mais {achados.Count - 20}" : "");
        if (MessageBox.Show(this,
                $"Vou devolver ao lugar os arquivos do jogo que foram substituídos e remover estes " +
                $"{achados.Count} item(ns):\r\n\r\n{lista}\r\n\r\n" +
                "Só sai o que é comprovadamente deste programa — arquivos do próprio jogo não são " +
                "tocados.\r\n\r\nContinuar?",
                "Desfazer tudo nesta pasta", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            != DialogResult.Yes)
            return;

        ShowStep(3);
        _txtLog.Clear();
        UseWaitCursor = true;
        try
        {
            new Isolamento(Log).ReligarTudo(pasta);
            var sobras = engine.LimpezaTotal(pasta);
            _manifest = null;
            _isolamento = EstadoIsolamento.Tudo;
            if (sobras.Count > 0)
            {
                _status.Text = $"{sobras.Count} item(ns) não saíram — veja o log.";
                MessageBox.Show(this,
                    "Ainda restaram:\r\n\r\n" + string.Join("\r\n", sobras.Take(12)) +
                    "\r\n\r\nIsso é arquivo em uso. Feche o jogo e a Steam pelo Gerenciador de " +
                    "Tarefas (ou reinicie o PC) e clique de novo.",
                    "Sobraram arquivos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                _status.Text = "Pasta limpa: não sobrou nada deste programa.";
                MessageBox.Show(this,
                    "Pronto — a pasta do jogo está como estava antes.\r\n\r\n" +
                    "O override de assinatura no registro é do sistema, não da pasta, e continua " +
                    "aplicado. Ele não atrapalha nenhum jogo; para tirar, use Desinstalar (reverter) " +
                    "numa instalação com manifesto.",
                    "Desfeito", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex) { Warn(ex.Message); }
        finally { UseWaitCursor = false; }
    }

    // ------------------------------------------------------------- navegação

    private void ShowStep(int step)
    {
        if (step < 0 || step > LastStep) return;
        _step = step;

        _content.Controls.Clear();
        (Panel panel, string title, string hint) = step switch
        {
            0 => (_p0, "1. Pastas", "Aponte a pasta do kit e a pasta do jogo. O resto o programa descobre sozinho."),
            1 => (_p1, "2. Detecção", "Confira o que foi detectado. Corrija a API se souber que o jogo usa outra — é o único palpite que pode errar."),
            2 => (_p2, "3. Plano", "Exatamente o que vai ser feito, antes de tocar em qualquer arquivo."),
            3 => (_p3, "4. Instalação", "Copiando arquivos, gerando configurações e aplicando o override."),
            _ => (_p4, "5. Verificação e passos manuais", "O que dá para verificar por arquivo já foi verificado. O resto está no roteiro abaixo."),
        };
        _stepTitle.Text = title;
        _stepHint.Text = hint;
        UpdateSidebar();
        _content.Controls.Add(panel);
        panel.Dock = DockStyle.Fill;

        _btnBack.Enabled = step > 0;
        _btnNext.Text = step switch
        {
            0 => "Detectar >",
            1 => "Gerar plano >",
            2 => "Instalar >",
            3 => "Verificar >",
            _ => "Concluir",
        };
        _btnNext.Enabled = true;
    }

    private async Task NextAsync()
    {
        try
        {
            switch (_step)
            {
                case 0: DoDetect(); break;
                case 1: DoPlan(); break;
                case 2: await DoInstallAsync(); break;
                case 3: RunVerification(); ShowStep(4); break;
                default: Close(); break;
            }
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }
    }

    private void DoDetect()
    {
        var kitFolder = _txtKit.Text.Trim();
        var gameFolder = _txtGame.Text.Trim();

        if (!Directory.Exists(kitFolder)) { Warn("Pasta do kit não encontrada."); return; }
        if (!Directory.Exists(gameFolder)) { Warn("Pasta do jogo não encontrada."); return; }

        _settings.KitFolder = kitFolder;
        _settings.LastGameFolder = gameFolder;
        _settings.Save();

        _status.Text = "Analisando...";
        Application.DoEvents();

        _kit = KitResolver.Resolve(kitFolder);
        var detection = GameDetector.Detect(gameFolder);
        _profile = detection.Profile;
        _candidates = detection.Candidates;

        if (_candidates.Count == 0) { Warn("Nenhum executável encontrado na pasta do jogo."); return; }

        PopulateCandidates(_profile.RealExePath);
        _cboArch.SelectedItem = _profile.Architecture;
        _cboApi.SelectedItem = _profile.Api;
        _chkDireto.Checked = false;
        _txtRenderer.Text = _profile.RendererFolder ?? _profile.ExeFolder;
        _cboMv.SelectedIndex = _options.MvProvider == MvProvider.Drme ? 1 : 0;
        SelectOverlayKey(_options.OverlayKey);
        UpdateMvAvailability();

        // Se o kit só tem um dos provedores, força o que existe.
        if (_kit.HasLaunchpad && !_kit.HasDrme) _cboMv.SelectedIndex = 0;
        else if (_kit.HasDrme && !_kit.HasLaunchpad) _cboMv.SelectedIndex = 1;

        var notes = new List<string>(detection.Notes);
        if (_profile.LauncherExePath is not null)
            notes.Add($"Launcher detectado ({Path.GetFileName(_profile.LauncherExePath)}) — a instalação aponta para o exe real, não para ele.");
        notes.Add($"Kit: {DescribeKit(_kit)}");
        foreach (var p in _kit.Problems) notes.Add("ATENÇÃO: " + p);
        _txtNotes.Lines = notes.ToArray();

        SyncProfileFromUi();
        _status.Text = "Detecção concluída.";
        ShowStep(1);
    }

    private static string DescribeKit(KitInventory kit)
    {
        var have = new List<string>();
        if (kit.NvngxDlssnr is not null) have.Add("nvngx_dlssnr");
        if (kit.NvngxDlss is not null) have.Add("nvngx_dlss");
        if (kit.RenodxAddon64 is not null) have.Add("renodx");
        if (kit.FeedAddon64 is not null) have.Add("feed64");
        if (kit.FeedAddon32 is not null) have.Add("feed32");
        if (kit.FeedHost64Exe is not null) have.Add("host64");
        if (kit.DxgiX64 is not null) have.Add("dxgi-x64");
        if (kit.DxgiX86 is not null) have.Add("dxgi-x86");
        if (kit.ReShadeSetup is not null) have.Add("instalador ReShade");
        if (kit.ShadersDir is not null) have.Add("shaders");
        if (kit.HasLaunchpad) have.Add("Launchpad");
        if (kit.HasDrme) have.Add("DRME");
        if (kit.DgVoodooD3D9X86 is not null) have.Add("dgVoodoo-d3d9");
        if (kit.DgVoodooD3D8X86 is not null) have.Add("dgVoodoo-d3d8");
        return string.Join(", ", have);
    }

    private void DoPlan()
    {
        if (_profile is null || _kit is null) { Warn("Faça a detecção primeiro."); return; }
        SyncProfileFromUi();

        _plan = InstallPlanBuilder.Build(_profile, _kit, _options);

        _lstPlan.Items.Clear();
        foreach (var a in _plan.Actions) _lstPlan.Items.Add(a.Description);

        if (_plan.Blockers.Count > 0 || _plan.Warnings.Count > 0)
        {
            var lines = new List<string>();
            if (_plan.Blockers.Count > 0)
            {
                lines.Add("Impedimentos:");
                lines.AddRange(_plan.Blockers);
            }
            if (_plan.Warnings.Count > 0)
            {
                if (lines.Count > 0) lines.Add("");
                lines.Add("Avisos (não impedem instalar):");
                lines.AddRange(_plan.Warnings);
            }
            _txtBlockers.Visible = true;
            _txtBlockers.Lines = lines.ToArray();
        }
        else
        {
            _txtBlockers.Visible = false;
        }

        _settings.MvProvider = _options.MvProvider.ToString();
        _settings.OverlayKey = _options.OverlayKey;
        _settings.OverlayCtrl = _options.OverlayCtrl;
        _settings.OverlayShift = _options.OverlayShift;
        _settings.OverlayAlt = _options.OverlayAlt;
        _settings.Save();

        ShowStep(2);
        _btnNext.Enabled = _plan.CanRun;
        _status.Text = _plan.CanRun
            ? $"{_plan.Actions.Count} ações prontas."
            : "Resolva os impedimentos antes de instalar.";
    }

    private async Task DoInstallAsync()
    {
        if (_plan is null || _kit is null || !_plan.CanRun) { Warn("Plano inválido."); return; }

        ShowStep(3);
        _txtLog.Clear();
        _btnNext.Enabled = false;
        _btnBack.Enabled = false;
        _status.Text = "Instalando...";

        var plan = _plan;
        var kit = _kit;
        try
        {
            _manifest = await Task.Run(() => new InstallerEngine(Log).Execute(plan, kit));
            _status.Text = "Instalação concluída.";
            Log("");
            Log("== Instalação concluída. Clique em Verificar. ==");
        }
        catch (Exception ex)
        {
            Log("");
            Log("ERRO: " + ex.Message);
            _status.Text = "Falhou. Veja o log.";
        }
        finally
        {
            _btnNext.Enabled = true;
            _btnBack.Enabled = true;
        }
    }

    /// <summary>
    /// Reinício SOMENTE por escolha do usuário: este método roda apenas pelo clique no
    /// botão "Reiniciar o PC (opcional)". O programa nunca reinicia nem agenda reinício
    /// sozinho, e não abre alerta nenhum por conta própria — a pendência de reinício
    /// aparece só como aviso amarelo na linha 2 da tabela de verificação.
    /// </summary>
    private void ReiniciarSeOUsuarioQuiser()
    {
        var resposta = MessageBox.Show(this,
            "Reiniciar o PC agora?\r\n\r\n" +
            "É opcional. O driver da NVIDIA lê o override de assinatura quando o Windows " +
            "inicia; se o DLSS 5 já está aplicando nos seus jogos, não precisa reiniciar.\r\n\r\n" +
            "Se confirmar, o Windows reinicia em 60 segundos (para cancelar depois, " +
            "execute: shutdown /a).",
            "Reiniciar o PC (opcional)", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (resposta != DialogResult.Yes) return;
        try
        {
            Process.Start(new ProcessStartInfo("shutdown",
                "/r /t 60 /c \"DLSS 5: reinício pedido pelo usuário para o driver ler o override.\"")
            { UseShellExecute = true, CreateNoWindow = true });
            _status.Text = "Reinício agendado para 60 segundos (cancelar: shutdown /a).";
        }
        catch (Exception ex) { Warn("Não consegui agendar o reinício: " + ex.Message); }
    }

    private void RunVerification()
    {
        if (_profile is null) return;
        _manifest ??= InstallManifest.Load(_profile.ExeFolder);
        _lblDgVoodoo.Visible = _profile.NeedsDgVoodoo;
        _cboPlaca.Visible = _profile.NeedsDgVoodoo;
        _btnPlaca.Visible = _profile.NeedsDgVoodoo;
        _btnPainelDg.Visible = _profile.NeedsDgVoodoo;
        _chkTnL.Visible = _profile.NeedsDgVoodoo;
        _btnTestarConf.Visible = _profile.NeedsDgVoodoo;

        _grid.Rows.Clear();
        var resultados = CheckpointVerifier.Verify(_profile, _manifest, NvngxDoKit()).ToList();
        foreach (var c in resultados)
        {
            int i = _grid.Rows.Add(StateText(c.State), $"{c.Number}. {c.Title}",
                c.Detail, c.FixHint ?? "");
            var row = _grid.Rows[i];
            var color = Ui.ForState(c.State switch
            {
                CheckStatus.Pass => CheckStatusKind.Ok,
                CheckStatus.Fail => CheckStatusKind.Bad,
                CheckStatus.Warning => CheckStatusKind.Warn,
                CheckStatus.Manual => CheckStatusKind.Info,
                _ => CheckStatusKind.Neutral,
            });

            // Só o estado leva cor forte; o resto fica legível em preto, e a linha que
            // falhou ganha destaque no nome da verificação.
            row.Cells[0].Style.ForeColor = color;
            row.Cells[0].Style.SelectionForeColor = color;
            row.Cells[0].Style.Font = Ui.BoldFont;
            if (c.State is CheckStatus.Fail or CheckStatus.Warning)
            {
                row.Cells[1].Style.ForeColor = color;
                row.Cells[1].Style.SelectionForeColor = color;
            }
            if (c.State == CheckStatus.Pass)
                row.Cells[2].Style.ForeColor = Ui.Muted;
        }

        // Reinício pendente vira só a linha 2 em amarelo na tabela — nenhum popup, nenhum
        // reinício automático. Quem decide reiniciar é o usuário, pelo botão opcional.

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
        _status.Text = "Verificação atualizada.";
    }

    private static string StateText(CheckStatus s) => s switch
    {
        CheckStatus.Pass => "OK",
        CheckStatus.Fail => "FALHA",
        CheckStatus.Warning => "ATENÇÃO",
        CheckStatus.Manual => "MANUAL",
        _ => "N/A",
    };

    private void Log(string line)
    {
        if (_txtLog.InvokeRequired)
        {
            _txtLog.BeginInvoke(new Action<string>(Log), line);
            return;
        }
        _txtLog.AppendText(line + Environment.NewLine);
    }

    private void Warn(string message)
    {
        _status.Text = message;
        MessageBox.Show(this, message, "DLSS 5 AutoInstaller",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
