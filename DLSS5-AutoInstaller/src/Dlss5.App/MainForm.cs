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
    private readonly CheckBox _chkNativeDlss = new();
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
        MinimumSize = new Size(1180, 720);
        Size = new Size(1280, 840);
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
        _p0 = new Panel { Dock = DockStyle.Fill };

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

        var info = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            Top = 95,
            Left = 0,
            Width = 920,
            Height = 380,
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
            GraphicsApi.D3D9, GraphicsApi.D3D11, GraphicsApi.D3D12,
            GraphicsApi.Vulkan, GraphicsApi.D3D10, GraphicsApi.OpenGL,
        });
        _cboApi.SelectedIndexChanged += (_, _) => SyncProfileFromUi();
        _p1.Controls.Add(_cboApi);
        y += 36;

        _chkNativeDlss.Text = "O jogo já tem DLSS nativo (dispensa o Feeder)";
        _chkNativeDlss.SetBounds(230, y, 400, 24);
        _chkNativeDlss.CheckedChanged += (_, _) => SyncProfileFromUi();
        _p1.Controls.Add(_chkNativeDlss);
        y += 32;

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

        _chkRegistry.Text = "Aplicar o override de assinatura NGX no registro (precisa reiniciar o PC depois)";
        _chkRegistry.SetBounds(230, y, 640, 24);
        _chkRegistry.Checked = true;
        _chkRegistry.CheckedChanged += (_, _) => _options.ApplyRegistryOverride = _chkRegistry.Checked;
        _p1.Controls.Add(_chkRegistry);
        y += 28;

        _chkClean.Text = "Remover arquivos que atrapalham (sl.*.dll, nvngx_dlssg.dll, licenças)";
        _chkClean.SetBounds(230, y, 640, 24);
        _chkClean.Checked = true;
        _chkClean.CheckedChanged += (_, _) => _options.CleanForbidden = _chkClean.Checked;
        _p1.Controls.Add(_chkClean);
        y += 34;

        Ui.StyleReadOnlyBox(_txtNotes);
        _txtNotes.SetBounds(0, y, 920, 150);
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
        _profile.HasNativeDlss = _chkNativeDlss.Checked;
        if (!string.IsNullOrWhiteSpace(_txtRenderer.Text)) _profile.RendererFolder = _txtRenderer.Text;
        _profile.MvProvider = _options.MvProvider;
        UpdateMvAvailability();
        UpdateRouteLabel();
    }

    private void UpdateRouteLabel()
    {
        if (_profile is null) return;
        var route = _profile.Route;
        _lblRoute.ForeColor = route == InstallRoute.Unsupported ? Color.Firebrick : Color.DarkGreen;
        _lblRoute.Text = route switch
        {
            InstallRoute.A => "Caminho A — 64-bit: ReShade + addons direto na pasta do executável.",
            InstallRoute.B => "Caminho B — 32-bit D3D11: addon32 na raiz e o resto do Feeder dentro de host64\\.",
            InstallRoute.C => "Caminho C — 32-bit D3D9: dgVoodoo2 traduz para D3D11, mais o layout do caminho B.",
            _ => "Sem caminho suportado para esta combinação. " +
                 (_profile.Architecture == PeArchitecture.X86 && _profile.Api == GraphicsApi.Vulkan
                     ? "Jogo 32-bit em Vulkan não funciona: troque para D3D9 se o jogo permitir."
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

        _p4.Controls.Add(split);
        _p4.Controls.Add(bar);
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
            Process.Start(new ProcessStartInfo(_profile.RealExePath)
            {
                UseShellExecute = true,
                WorkingDirectory = _profile.ExeFolder,
            });
            _status.Text = "Jogo iniciado. Depois de fechar, clique em Verificar de novo para ler os logs.";
        }
        catch (Exception ex) { Warn("Não consegui abrir o jogo: " + ex.Message); }
    }

    private void RevertInstall()
    {
        if (_profile is null) return;
        var manifest = _manifest ?? InstallManifest.Load(_profile.ExeFolder);
        if (manifest is null)
        {
            Warn("Não encontrei o manifesto da instalação nesta pasta.");
            return;
        }
        if (MessageBox.Show(this,
                "Isto vai remover os arquivos instalados, restaurar backups e desfazer o override do registro.\r\n\r\nContinuar?",
                "Reverter instalação", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        var engine = new InstallerEngine(Log);
        ShowStep(3);
        _txtLog.Clear();
        try
        {
            engine.Revert(manifest, removeRegistryOverride: true);
            _manifest = null;
            _status.Text = "Reversão concluída. Reinicie o PC para o override sair de vez.";
        }
        catch (Exception ex) { Warn(ex.Message); }
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
        _chkNativeDlss.Checked = _profile.HasNativeDlss;
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
        if (kit.DgVoodooD3D9X86 is not null) have.Add("dgVoodoo");
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

    private void RunVerification()
    {
        if (_profile is null) return;
        _manifest ??= InstallManifest.Load(_profile.ExeFolder);

        _grid.Rows.Clear();
        foreach (var c in CheckpointVerifier.Verify(_profile, _manifest))
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
