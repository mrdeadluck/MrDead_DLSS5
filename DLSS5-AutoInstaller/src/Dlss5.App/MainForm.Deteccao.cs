using System.Runtime.Versioning;
using Dlss5.Core;

namespace Dlss5.App;

/// <summary>Tela de detecção: executável, arquitetura, API, DLSS nativo e opções da instalação.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainForm
{
    private Panel _pDeteccao = new();
    private readonly ComboBox _cboExe = new();
    private readonly ComboBox _cboArch = new();
    private readonly ComboBox _cboApi = new();
    private readonly Label _lblNative = new();
    private readonly Label _lblNativeWhy = new();
    private readonly CheckBox _chkDireto = new();
    private readonly ComboBox _cboEngine = new();
    private readonly NumericUpDown _numPasses = new();
    private readonly Label _lblEngineNote = new();
    /// <summary>Os motores listados no combo, na ordem dos itens (muda com a arquitetura).</summary>
    private List<NeuralEngine> _motoresNaTela = new(Motores.Disponiveis(PeArchitecture.X64));
    private bool _remontandoMotores;
    private readonly CheckBox _chkReFramework = new();
    private readonly ComboBox _cboReShadeNome = new();
    private readonly Label _lblDicaReShadeNome = new();
    private readonly Label _lblReShadeNome = new();
    private readonly Button _btnNativeAjustar = Ui.Secondary("Ajustar…");
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
    private readonly CheckBox _chkWatermark = new();
    private readonly CheckBox _chkJanela = new();
    private readonly ComboBox _cboTecla = new();
    private readonly TextBox _txtNotes = new();
    private TableLayoutPanel _formDeteccao = new();

    private void BuildDeteccao()
    {
        _pDeteccao = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var form = Ui.Formulario(16);
        _formDeteccao = form;
        int linha = 0;

        // Executável
        _cboExe.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboExe.Dock = DockStyle.Fill;
        _cboExe.Margin = new Padding(0, 4, 8, 4);
        _cboExe.SelectedIndexChanged += (_, _) => OnExeChanged();
        form.Controls.Add(Ui.Rotulo("Executável real"), 0, linha);
        form.Controls.Add(LinhaComBotoes(_cboExe, Botao("Outro…", (_, _) => BrowseExe())), 1, linha++);
        form.Controls.Add(Dica("O programa que renderiza o jogo. Em jogos Unreal é o *-Shipping.exe dentro de Binaries\\Win64, não o atalho da raiz."), 1, linha++);

        // Arquitetura + API na mesma linha (quebra em janela estreita)
        _cboArch.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboArch.Items.AddRange(new object[] { PeArchitecture.X86, PeArchitecture.X64 });
        _cboArch.Width = 120;
        _cboArch.SelectedIndexChanged += (_, _) => SyncProfileFromUi();
        _cboApi.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboApi.Items.AddRange(new object[]
        {
            GraphicsApi.D3D8, GraphicsApi.D3D9, GraphicsApi.D3D11, GraphicsApi.D3D12,
            GraphicsApi.Vulkan, GraphicsApi.OpenGL, GraphicsApi.D3D10,
        });
        _cboApi.Width = 130;
        _cboApi.SelectedIndexChanged += (_, _) => SyncProfileFromUi();
        var filaArch = Ui.Fila();
        filaArch.Controls.Add(_cboArch);
        filaArch.Controls.Add(new Label { Text = "API gráfica", AutoSize = true, Margin = new Padding(16, 8, 8, 0) });
        filaArch.Controls.Add(_cboApi);
        _cboArch.Margin = new Padding(0, 4, 0, 4);
        _cboApi.Margin = new Padding(0, 4, 0, 4);
        form.Controls.Add(Ui.Rotulo("Arquitetura"), 0, linha);
        form.Controls.Add(filaArch, 1, linha++);
        form.Controls.Add(Dica("Lidas do executável. A API é a única que pode estar errada: mude se souber que o jogo usa outra (em 64-bit, D3D11 e D3D12 dão no mesmo)."), 1, linha++);

        // DLSS nativo
        _lblNative.AutoSize = true;
        _lblNative.Font = Ui.BoldFont;
        _lblNative.Margin = new Padding(0, 8, 8, 0);
        var filaNative = Ui.Fila();
        filaNative.Controls.Add(_lblNative);
        _btnNativeAjustar.Margin = new Padding(8, 2, 0, 2);
        _btnNativeAjustar.Click += (_, _) => AjustarDlssNativo();
        filaNative.Controls.Add(_btnNativeAjustar);
        form.Controls.Add(Ui.Rotulo("DLSS nativo do jogo"), 0, linha);
        form.Controls.Add(filaNative, 1, linha++);
        _lblNativeWhy.AutoSize = true;
        _lblNativeWhy.Dock = DockStyle.Fill;
        _lblNativeWhy.ForeColor = Ui.Muted;
        _lblNativeWhy.Font = Ui.SmallFont;
        _lblNativeWhy.Margin = new Padding(0, 0, 0, 6);
        form.Controls.Add(_lblNativeWhy, 1, linha++);

        _chkDireto.Text = "Usar o Feeder em vez do caminho direto — experimental: em jogo com DLSS nativo o Feeder colide com o NGX do jogo";
        Ui.ComQuebra(_chkDireto);
        _chkDireto.Visible = false;
        _chkDireto.Margin = new Padding(0, 0, 0, 6);
        _chkDireto.CheckedChanged += (_, _) => SyncProfileFromUi();
        form.Controls.Add(_chkDireto, 1, linha++);

        // RE Engine com proteção anti-adulteração: o ReShade injetado como dxgi.dll faz o
        // jogo abrir a própria tela de erro antes de criar qualquer DLSS. Hospedado no
        // REFramework ele passa — foi assim que o RE9 abriu com o painel funcionando.
        _chkReFramework.Text = "Instalar o REFramework junto — só para jogo que recusa o ReShade na abertura (RE Requiem, Dragon's Dogma 2). " +
                               "Em RE4, RE Village e MH Wilds deixe DESMARCADO: ali ele derruba o jogo.";
        Ui.ComQuebra(_chkReFramework);
        _chkReFramework.Visible = false;
        _chkReFramework.Margin = new Padding(0, 0, 0, 6);
        _chkReFramework.CheckedChanged += (_, _) => SyncProfileFromUi();
        form.Controls.Add(_chkReFramework, 1, linha++);

        // O nome com que o ReShade entra. Quase sempre dxgi.dll; mas há jogo que recusa
        // exatamente esse nome (MGS V, Fox Engine: com o dxgi.dll na pasta o executável
        // nem abre) e aceita o nome da própria API.
        _lblReShadeNome.Text = "ReShade entra como:";
        _lblReShadeNome.AutoSize = true;
        _lblReShadeNome.Margin = new Padding(0, 8, 0, 6);
        _cboReShadeNome.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboReShadeNome.Width = 200;
        _cboReShadeNome.Margin = new Padding(0, 4, 0, 6);
        _cboReShadeNome.SelectedIndexChanged += (_, _) => SyncProfileFromUi();
        Ui.Adaptavel(_cboReShadeNome, 160);
        var filaNome = Ui.Fila();
        filaNome.Controls.Add(_cboReShadeNome);
        filaNome.Controls.Add(_lblDicaReShadeNome);
        _lblDicaReShadeNome.Text = "dxgi.dll serve em quase tudo. Troque só se o jogo não abrir com ele.";
        _lblDicaReShadeNome.AutoSize = true;
        _lblDicaReShadeNome.ForeColor = Ui.Muted;
        _lblDicaReShadeNome.Font = Ui.SmallFont;
        _lblDicaReShadeNome.Margin = new Padding(8, 8, 0, 0);
        Ui.QuebrarNoPai(_lblDicaReShadeNome);
        form.Controls.Add(_lblReShadeNome, 0, linha);
        form.Controls.Add(filaNome, 1, linha++);

        // Renderizador
        _txtRenderer.Dock = DockStyle.Fill;
        _txtRenderer.Margin = new Padding(0, 4, 8, 4);
        _txtRenderer.TextChanged += (_, _) => SyncProfileFromUi();
        form.Controls.Add(Ui.Rotulo("Pasta do renderizador"), 0, linha);
        form.Controls.Add(_txtRenderer, 1, linha++);
        form.Controls.Add(Dica("Onde o dgVoodoo vai (só rota C). Igual à pasta do exe, exceto em jogos da engine Source (bin\\)."), 1, linha++);

        // Rota
        _lblRoute.AutoSize = true;
        _lblRoute.Dock = DockStyle.Fill;
        _lblRoute.Font = Ui.BoldFont;
        _lblRoute.Margin = new Padding(0, 6, 0, 10);
        form.Controls.Add(Ui.Rotulo("Caminho de instalação"), 0, linha);
        form.Controls.Add(_lblRoute, 1, linha++);

        // Motor do Neural Rendering (qual addon) e passadas
        _cboEngine.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboEngine.Items.AddRange(_motoresNaTela.Select(m => (object)Motores.Rotulo(m)).ToArray());
        _cboEngine.SelectedIndex = 0;
        _cboEngine.Width = 420;
        _cboEngine.Margin = new Padding(0, 4, 8, 4);
        _cboEngine.SelectedIndexChanged += (_, _) => { if (!_remontandoMotores) SyncProfileFromUi(); };
        Ui.Adaptavel(_cboEngine, 200);   // largura do item mais longo, mas nunca maior que a coluna
        _numPasses.Minimum = ShortFuseDlss.PassesMin;
        _numPasses.Maximum = ShortFuseDlss.PassesMax;
        _numPasses.Value = ShortFuseDlss.PassesPadrao;
        _numPasses.Width = 52;
        _numPasses.Margin = new Padding(0, 4, 8, 4);
        _numPasses.ValueChanged += (_, _) => { if (!_remontandoMotores) SyncProfileFromUi(); };
        _lblEngineNote.AutoSize = true;
        _lblEngineNote.ForeColor = Ui.Muted;
        _lblEngineNote.Margin = new Padding(0, 8, 0, 0);
        Ui.QuebrarNoPai(_lblEngineNote);
        var filaEngine = Ui.Fila();
        filaEngine.Controls.Add(_cboEngine);
        filaEngine.Controls.Add(new Label { Text = "Passadas", AutoSize = true, Margin = new Padding(8, 8, 6, 0) });
        filaEngine.Controls.Add(_numPasses);
        filaEngine.Controls.Add(_lblEngineNote);
        form.Controls.Add(Ui.Rotulo("Motor do DLSS 5"), 0, linha);
        form.Controls.Add(filaEngine, 1, linha++);
        form.Controls.Add(Dica("Passadas múltiplas (o \"x2\" que a comunidade mostra): em 64-bit, o ShortFuse (renodx-dlss.addon64 fabrica a chamada de DLSS sozinho, 1 a 10 passadas). Em 32-bit o consumidor neural mora no host64: OptiScaler DLSS-NR (1 a 5 passadas, vem no kit) ou Deep Fried Chicken (1 a 30, arquivos do Discord). Cada passada custa o mesmo que a primeira. Krish + Feeder é o caminho testado até aqui, uma passada."), 1, linha++);

        // Motion vectors
        _cboMv.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboMv.Items.AddRange(MvProviders.Ordem.Select(p => (object)MvProviders.Rotulo(p)).ToArray());
        _cboMv.SelectedIndex = MvProviders.Indice(MvProviders.Padrao);
        _cboMv.Width = 300;
        _cboMv.Margin = new Padding(0, 4, 8, 4);
        Ui.Adaptavel(_cboMv, 180);
        _cboMv.SelectedIndexChanged += (_, _) =>
        {
            if (_cboMv.SelectedIndex >= 0) _options.MvProvider = MvProviders.Ordem[_cboMv.SelectedIndex];
            if (_profile is not null) _profile.MvProvider = _options.MvProvider;
        };
        _lblMvNote.AutoSize = true;
        _lblMvNote.ForeColor = Ui.Muted;
        _lblMvNote.Margin = new Padding(0, 8, 0, 0);
        Ui.QuebrarNoPai(_lblMvNote);
        var filaMv = Ui.Fila();
        filaMv.Controls.Add(_cboMv);
        filaMv.Controls.Add(_lblMvNote);
        form.Controls.Add(Ui.Rotulo("Motion vectors"), 0, linha);
        form.Controls.Add(filaMv, 1, linha++);
        form.Controls.Add(Dica("Efeito do ReShade que estima o movimento para o DLSS 5. Launchpad foi validado em mais jogos; DRME é a alternativa se houver ghosting. Impacto: leve custo de GPU."), 1, linha++);

        // Tecla do overlay
        _cboKey.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboKey.MaxDropDownItems = 18;
        _cboKey.Width = 220;
        _cboKey.Margin = new Padding(0, 4, 8, 4);
        Ui.Adaptavel(_cboKey, 160);
        foreach (var k in ReShadeConfigWriter.OverlayKeys) _cboKey.Items.Add(k.Label);
        _cboKey.SelectedIndex = 0;
        _cboKey.SelectedIndexChanged += (_, _) => SyncOverlayKeyFromUi();
        foreach (var (chk, texto) in new[] { (_chkCtrl, "Ctrl"), (_chkShift, "Shift"), (_chkAlt, "Alt") })
        {
            chk.Text = texto;
            chk.AutoSize = true;
            chk.Margin = new Padding(0, 8, 10, 0);
            chk.CheckedChanged += (_, _) => SyncOverlayKeyFromUi();
        }
        _lblKeyNote.AutoSize = true;
        _lblKeyNote.ForeColor = Ui.Muted;
        _lblKeyNote.Margin = new Padding(0, 8, 0, 0);
        Ui.QuebrarNoPai(_lblKeyNote);
        var filaKey = Ui.Fila();
        filaKey.Controls.Add(_cboKey);
        filaKey.Controls.Add(_chkCtrl);
        filaKey.Controls.Add(_chkShift);
        filaKey.Controls.Add(_chkAlt);
        filaKey.Controls.Add(_lblKeyNote);
        // Tecla de liga/desliga do DLSS 5 (alternância da technique "DLSS 5 Feed" no preset).
        _cboTecla.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboTecla.MaxDropDownItems = 18;
        _cboTecla.Width = 220;
        _cboTecla.Margin = new Padding(0, 4, 8, 4);
        Ui.Adaptavel(_cboTecla, 160);
        _cboTecla.Items.Add("Nenhuma");
        foreach (var k in ReShadeConfigWriter.OverlayKeys) _cboTecla.Items.Add(k.Label);
        _cboTecla.SelectedIndexChanged += (_, _) =>
        {
            int i = _cboTecla.SelectedIndex;
            _options.TeclaLigaDesliga = i <= 0 ? 0 : ReShadeConfigWriter.OverlayKeys[i - 1].VirtualKey;
        };
        var filaTecla = Ui.Fila();
        filaTecla.Controls.Add(_cboTecla);
        form.Controls.Add(Ui.Rotulo("Tecla liga/desliga do DLSS 5"), 0, linha);
        form.Controls.Add(filaTecla, 1, linha++);
        form.Controls.Add(Dica("Aperta no jogo e o DLSS 5 (DLAA + Neural Rendering) desliga; aperta de novo e volta — sem abrir o painel, para comparar antes/depois. É a tecla de alternância que o ReShade dá à technique \"DLSS 5 Feed\" (gravada no preset). F6 é a mesma tecla do NR do RenoDX em jogo 64-bit. Não pode ser a tecla do painel."), 1, linha++);
        form.Controls.Add(Ui.Rotulo("Tecla do painel do ReShade"), 0, linha);
        form.Controls.Add(filaKey, 1, linha++);
        form.Controls.Add(Dica("Abre o painel do ReShade dentro do jogo. Se o jogo capturar a tecla, use uma combinação (ex.: Ctrl+Shift+Home). A escolha fica guardada para as próximas instalações."), 1, linha++);

        // Opções
        _chkRegistry.Text = "Aplicar o override de assinatura NGX no registro do Windows (recomendado)";
        Ui.ComQuebra(_chkRegistry);
        _chkRegistry.Checked = true;
        _chkRegistry.Margin = new Padding(0, 6, 0, 0);
        _chkRegistry.CheckedChanged += (_, _) => _options.ApplyRegistryOverride = _chkRegistry.Checked;
        _chkClean.Text = "Mover para backup um instalador do ReShade esquecido na pasta do jogo";
        Ui.ComQuebra(_chkClean);
        _chkClean.Checked = true;
        _chkClean.Margin = new Padding(0, 4, 0, 0);
        _chkClean.CheckedChanged += (_, _) => _options.CleanForbidden = _chkClean.Checked;
        _chkWatermark.Text = "Marca d'água do dgVoodoo (só rota C) — selo no canto para confirmar que ele está ativo";
        Ui.ComQuebra(_chkWatermark);
        _chkWatermark.Checked = true;
        _chkWatermark.Margin = new Padding(0, 4, 0, 0);
        _chkWatermark.CheckedChanged += (_, _) => _options.DgVoodooWatermark = _chkWatermark.Checked;
        _chkJanela.Text = "Forçar o jogo em janela sem borda — para jogo que só abre em tela cheia (evita o congelamento do host64)";
        Ui.ComQuebra(_chkJanela);
        _chkJanela.Checked = false;
        _chkJanela.Margin = new Padding(0, 4, 0, 0);
        _chkJanela.CheckedChanged += (_, _) => _options.ForcarJanela = _chkJanela.Checked;
        var opcoes = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
        // Percent 100: as caixas e as dicas são medidas na largura da coluna e quebram linha.
        opcoes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        opcoes.Controls.Add(_chkRegistry);
        opcoes.Controls.Add(Dica("Sem isso o driver NVIDIA recusa o nvngx_dlssnr.dll (erro 0xBAD00007). Vale para o sistema inteiro; anti-cheat pode tratar como violação — não use em jogos online com anti-cheat. Faz efeito após reiniciar o Windows uma vez."));
        opcoes.Controls.Add(_chkClean);
        opcoes.Controls.Add(Dica("Nunca toca em arquivo do jogo. O instalador vai para .dlss5bak e volta na desinstalação."));
        opcoes.Controls.Add(_chkWatermark);
        opcoes.Controls.Add(_chkJanela);
        opcoes.Controls.Add(Dica("Vale para QUALQUER jogo 32-bit que sobe o host64, tenha ou não opção de janela. Em tela cheia exclusiva o host64 (janela atrás do jogo) e o painel brigam com o swapchain do jogo: o Enslaved congelou no aperto de mão com o host. Grava [APP] ForceWindowed=1 no ReShade.ini do jogo e põe na pasta o swapchain_override.addon32 (exemplo do próprio ReShade 6, que é quem lê essa chave; sozinho o ReShade 6 a ignora) — e, na rota C, FullScreenMode=false no dgVoodoo.conf. O jogo abre em janela sem borda do tamanho da tela achando que está em tela cheia. Se o jogo tiver opção própria de janela, pode usar a do jogo."));
        form.Controls.Add(Ui.Rotulo("Opções"), 0, linha);
        form.Controls.Add(opcoes, 1, linha++);

        // Notas da detecção
        Ui.StyleReadOnlyBox(_txtNotes);
        _txtNotes.Dock = DockStyle.Fill;
        _txtNotes.Margin = new Padding(0, 10, 0, 0);
        _txtNotes.MinimumSize = new Size(0, 90);
        _txtNotes.Height = 160;
        form.Controls.Add(Ui.Rotulo("Notas da detecção"), 0, linha);
        form.Controls.Add(_txtNotes, 1, linha++);

        _pDeteccao.Controls.Add(form);
        // ClientSizeChanged, não Resize: também dispara quando a barra de rolagem aparece.
        _pDeteccao.ClientSizeChanged += (_, _) => AjustarAlturaDasNotas();
        form.SizeChanged += (_, _) => AjustarAlturaDasNotas();
    }

    /// <summary>As notas ocupam o que sobrar da altura; abaixo de um mínimo, a tela rola.</summary>
    private void AjustarAlturaDasNotas()
    {
        int usado = _formDeteccao.Height - _txtNotes.Height;
        int livre = _pDeteccao.ClientSize.Height - usado - Ui.Px(this, 8);
        int altura = Math.Max(Ui.Px(this, 90), livre);
        if (_txtNotes.Height != altura) _txtNotes.Height = altura;
    }

    private static Label Dica(string texto)
    {
        var l = Ui.Paragrafo(texto, Ui.Muted, Ui.SmallFont);
        l.Dock = DockStyle.Fill;
        l.Margin = new Padding(0, 0, 0, 8);
        return l;
    }

    /// <summary>
    /// Enquanto a tela é preenchida a partir de uma detecção nova, os eventos dos controles
    /// NÃO podem gravar no perfil: cada atribuição dispara SyncProfileFromUi, e os campos
    /// ainda não preenchidos guardam o texto da rodada anterior. Foi assim que o MGS V
    /// perdeu o d3d11.dll e o Titanfall 2 perdeu a pasta do renderizador — a detecção
    /// acertava, a nota na tela dizia bin\x64_retail, e o campo "Pasta do renderizador"
    /// gravava a raiz da rodada anterior por cima antes de ser preenchido.
    /// </summary>
    private bool _preenchendoDeteccao;

    private void PreencherDeteccao(DetectionResult detection, KitInventory kit)
    {
        _profile = detection.Profile;
        _candidates = detection.Candidates;

        _preenchendoDeteccao = true;
        try
        {
            PreencherControles(detection, kit);
        }
        finally
        {
            _preenchendoDeteccao = false;
        }

        SyncProfileFromUi();
        _diario.Tecnico($"Detecção: {_profile.RealExePath} {_profile.Architecture} {_profile.Api} rota {_profile.Route} nativo={_profile.HasNativeDlss} renderizador={_profile.RendererFolder}");
    }

    private void PreencherControles(DetectionResult detection, KitInventory kit)
    {
        PopulateCandidates(_profile!.RealExePath);
        _cboArch.SelectedItem = _profile.Architecture;
        _cboApi.SelectedItem = _profile.Api;
        _chkDireto.Checked = _profile.PreferirFeeder;
        // Motor e passadas: de uma instalação anterior quando é Atualizar/Reparar; senão a
        // última escolha guardada nas preferências.
        if (_fluxo is Fluxo.Atualizar or Fluxo.Reparar && _manifest is not null
            && Enum.TryParse<NeuralEngine>(_manifest.Engine, out var motorGravado))
        {
            _profile.Engine = motorGravado;
            _profile.PassCount = Motores.Limitar(motorGravado, _manifest.PassCount);
        }
        else
        {
            if (Enum.TryParse<NeuralEngine>(_settings.Engine, out var motorPreferido)) _profile.Engine = motorPreferido;
            _profile.PassCount = Motores.Limitar(_profile.Engine, _settings.PassCount);
        }
        // Motor que não vale para esta arquitetura (ShortFuse em x86, OptiScaler em x64) cai no Krish.
        if (!Motores.Aplicavel(_profile.Engine, _profile.Architecture)) _profile.Engine = NeuralEngine.RenodxDlss5Feeder;
        RemontarMotores();
        _chkReFramework.Checked = _profile.UsarReFramework;
        PopularNomesDeReShade();
        _txtRenderer.Text = _profile.RendererFolder ?? _profile.ExeFolder;
        // O provedor escolhido precisa estar no kit; se não está, cai no primeiro disponível
        // (VORT vem no kit; LumeniteFX só se o usuário baixou).
        _options.MvProvider = MvProviders.Resolver(kit, _options.MvProvider);
        _cboMv.SelectedIndex = MvProviders.Indice(_options.MvProvider);
        _chkRegistry.Checked = _options.ApplyRegistryOverride;
        _chkClean.Checked = _options.CleanForbidden;
        _chkWatermark.Checked = _options.DgVoodooWatermark;
        // Jogo 32-bit que sobe o host64 (rotas B e C): em tela cheia EXCLUSIVA o jogo congela no
        // aperto de mão com o host (Enslaved). Numa instalação nova já deixamos "forçar janela"
        // marcado — é o padrão seguro (o README do Feeder diz "windowed is smoother") e evita que
        // reinstalar não mude nada. Em Atualizar/Reparar respeitamos a escolha gravada no manifesto.
        if (_fluxo is not (Fluxo.Atualizar or Fluxo.Reparar)
            && _profile.Route is InstallRoute.B or InstallRoute.C)
            _options.ForcarJanela = true;
        _chkJanela.Checked = _options.ForcarJanela;
        SelectOverlayKey(_options.OverlayKey);
        SelectTeclaLigaDesliga(_options.TeclaLigaDesliga);
        UpdateMvAvailability();

        var notes = new List<string>(detection.Notes);
        notes.Add($"Este executável: {AppInfo.Nome} {AppInfo.VersaoComBuild}.");
        if (_profile.LauncherExePath is not null)
            notes.Add($"Launcher detectado ({Path.GetFileName(_profile.LauncherExePath)}) — a instalação aponta para o exe real, não para ele.");
        if (kit.ReFrameworkDinput8 is null)
            notes.Add("REFramework NÃO está no kit. Para hospedar o ReShade nele, ponha o dinput8.dll " +
                      "(x64) do REFramework em qualquer subpasta de " + kit.KitRoot + " — por exemplo " +
                      "numa pasta REFramework\\ — e detecte de novo.");
        notes.Add("RE Engine (re_chunk_*.pak na pasta): " + (_profile.EhReEngine ? "SIM" : "NÃO") +
                  " — é a engine em que o REFramework carrega. " + ReFramework.QuandoMarcar +
                  " O ReShade continua sendo a DLL ao lado do executável: o REFramework, quando entra, " +
                  "só desarma a checagem de integridade.");
        notes.Add($"Kit: {DescribeKit(kit)}");
        foreach (var p in kit.Problems) notes.Add("ATENÇÃO: " + p);
        if (_manifest is not null)
            notes.Add($"Instalação anterior registrada em {_manifest.InstalledUtc.ToLocalTime():dd/MM/yyyy HH:mm} (rota {_manifest.Route}); os backups dos originais serão preservados.");
        _txtNotes.Lines = notes.ToArray();
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
        var ordered = _candidates.OrderByDescending(c => c.Score).ToList();
        foreach (var c in ordered)
        {
            var rel = _profile is null ? c.Path : SafeRelative(_profile.GameFolder, c.Path);
            _cboExe.Items.Add($"{rel}   [{c.Arch}, {c.Size / 1024:N0} KB]");
        }
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
        if (string.IsNullOrWhiteSpace(_txtRenderer.Text) || !Directory.Exists(_txtRenderer.Text))
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

    private void SelectTeclaLigaDesliga(int virtualKey)
    {
        var keys = ReShadeConfigWriter.OverlayKeys;
        int idx = 0;
        for (int i = 0; i < keys.Count; i++)
            if (keys[i].VirtualKey == virtualKey) { idx = i + 1; break; }
        _cboTecla.SelectedIndex = idx;
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
        SyncOverlayKeyFromUi();
    }

    private void UpdateMvAvailability()
    {
        bool feederUsed = _profile is null || _profile.NeedsFeeder;
        _cboMv.Enabled = feederUsed;
        _lblMvNote.Text = feederUsed ? string.Empty
            : _profile!.UsesShortFuse ? "não usado: o RenoDX DLSS do ShortFuse gera os guias sozinho"
            : "não usado: em D3D12 com DLSS nativo quem trabalha é o RenoDX";
        UpdateEngineAvailability();
    }

    /// <summary>Refaz a lista de motores para a arquitetura do perfil e seleciona o motor atual.</summary>
    private void RemontarMotores()
    {
        var arch = _profile?.Architecture ?? PeArchitecture.X64;
        var motores = Motores.Disponiveis(arch).ToList();
        var atual = _profile?.Engine ?? NeuralEngine.RenodxDlss5Feeder;
        if (!motores.Contains(atual)) atual = NeuralEngine.RenodxDlss5Feeder;
        _remontandoMotores = true;
        try
        {
            if (!motores.SequenceEqual(_motoresNaTela))
            {
                _motoresNaTela = motores;
                _cboEngine.Items.Clear();
                _cboEngine.Items.AddRange(motores.Select(m => (object)Motores.Rotulo(m)).ToArray());
                Ui.AjustarLargura(_cboEngine, 200);
            }
            _cboEngine.SelectedIndex = Math.Max(0, motores.IndexOf(atual));
            _numPasses.Maximum = Math.Max(Motores.PassesMin, Motores.PassesMax(atual));
            _numPasses.Value = Math.Clamp(_profile?.PassCount ?? ShortFuseDlss.PassesPadrao, (int)_numPasses.Minimum, (int)_numPasses.Maximum);
        }
        finally { _remontandoMotores = false; }
    }

    private void UpdateEngineAvailability()
    {
        if (_profile is null) return;
        var motor = _profile.MotorEfetivo;
        bool passadas = motor != NeuralEngine.RenodxDlss5Feeder;
        _cboEngine.Enabled = true;
        _numPasses.Enabled = passadas;
        _lblEngineNote.Text = motor switch
        {
            NeuralEngine.RenodxDlssShortFuse => _profile.Architecture == PeArchitecture.X86
                ? $"{ShortFuseDlss.Addon} entra em host64\\ (intercepta a chamada de DLSS do host); {_profile.PassCount} passada(s); validado no SH2 EE — painel com Home na janela do host"
                : $"{ShortFuseDlss.Addon} substitui o Krish e o Feeder nesta pasta; {_profile.PassCount} passada(s)",
            NeuralEngine.OptiScalerNr => $"OptiScaler DLSS-NR entra em host64\\ como winmm.dll; {_profile.PassCount} passada(s); menu na tecla Insert (janela do host). No SH2 EE as passadas extras não mudaram a imagem — prefira o ShortFuse",
            NeuralEngine.DeepFriedChicken => $"Deep Fried Chicken entra em host64\\ no lugar do renodx-dlss5; {_profile.PassCount} passada(s); o plano cobra os três arquivos se faltarem no kit",
            _ => _profile.Architecture == PeArchitecture.X86
                ? "uma passada. Para x2+ em 32-bit escolha OptiScaler DLSS-NR ou Deep Fried Chicken (vão para o host64)"
                : string.Empty,
        };
    }

    private void SyncProfileFromUi()
    {
        if (_profile is null || _preenchendoDeteccao) return;
        if (_cboArch.SelectedItem is PeArchitecture a) _profile.Architecture = a;
        if (_cboApi.SelectedItem is GraphicsApi g) _profile.Api = g;
        if (!string.IsNullOrWhiteSpace(_txtRenderer.Text)) _profile.RendererFolder = _txtRenderer.Text;
        _profile.AtualizarD3d8ViaD3D9();
        _profile.MvProvider = _options.MvProvider;
        if (_cboEngine.SelectedIndex >= 0 && _cboEngine.SelectedIndex < _motoresNaTela.Count)
            _profile.Engine = _motoresNaTela[_cboEngine.SelectedIndex];
        _profile.PassCount = (int)_numPasses.Value;
        // A arquitetura pode ter mudado no combo: a lista de motores acompanha.
        if (!Motores.Disponiveis(_profile.Architecture).SequenceEqual(_motoresNaTela)
            || _numPasses.Maximum != Math.Max(Motores.PassesMin, Motores.PassesMax(_profile.MotorEfetivo)))
        {
            if (!Motores.Aplicavel(_profile.Engine, _profile.Architecture)) _profile.Engine = NeuralEngine.RenodxDlss5Feeder;
            RemontarMotores();
            _profile.PassCount = (int)_numPasses.Value;
        }
        _profile.PreferirFeeder = _chkDireto.Visible && _chkDireto.Checked;
        _profile.UsarReFramework = _chkReFramework.Visible && _chkReFramework.Checked;
        if (_cboReShadeNome.Visible && _cboReShadeNome.SelectedItem is string nomeReShade)
            _profile.NomeDoReShadeEscolhido = nomeReShade;
        UpdateMvAvailability();
        UpdateNativeLabel();
        UpdateRouteLabel();
    }

    private void UpdateNativeLabel()
    {
        if (_profile is null) return;
        bool sim = _profile.HasNativeDlss;
        _lblNative.Text = (sim ? "SIM" : "NÃO") + (_profile.NativeDlssOverridden ? "  (alterado por você)" : "  (detectado)");
        _lblNative.ForeColor = _profile.NativeDlssOverridden ? Ui.Warn : (sim ? Ui.Ok : Ui.Ink);
        var porque = _profile.NativeDlss?.Resumo ?? "sem detecção";
        _lblNativeWhy.Text = porque + Environment.NewLine + ConsequenciaDoNativo();
        _chkDireto.Visible = _profile.HasNativeDlss && _profile.Api == GraphicsApi.D3D12 && !_profile.UsesShortFuse;
        PopularNomesDeReShade();

        // O REFramework é x64 e só carrega em RE Engine — mas quem decide é o usuário, não
        // o palpite. Já escondi essa caixa duas vezes: primeiro atrás da detecção de RE
        // Engine (re_chunk_*.pak), depois atrás de o kit ter o arquivo. Nas duas o usuário
        // ficou sem a opção E sem saber por quê. Ela aparece; quem cobra o que falta é o
        // plano, que sabe dizer onde pôr o dinput8.dll.
        bool cabeReFramework = _profile.Architecture == PeArchitecture.X64;
        if (_chkReFramework.Visible != cabeReFramework)
        {
            _chkReFramework.Visible = cabeReFramework;
            if (!cabeReFramework) _chkReFramework.Checked = false;
        }
    }

    private string ConsequenciaDoNativo()
    {
        if (_profile is null) return string.Empty;
        return _profile.UsesRenodxDirectPath
            ? "Efeito: caminho direto (padrão em D3D12 com DLSS nativo) — o RenoDX se pendura no DLSS do próprio jogo; deixe o DLSS do jogo LIGADO."
            : _profile.HasNativeDlss
                ? "Efeito: o Feeder é instalado e roda um NGX próprio; com DLSS nativo, deixe o DLSS do jogo DESLIGADO. Arquivos de DLSS do jogo nunca são apagados."
                : "Efeito: o Feeder é instalado (é ele quem roda o DLSS 5). Arquivos de DLSS do jogo nunca são apagados.";
    }

    private void AjustarDlssNativo()
    {
        if (_profile is null) return;
        bool novo = !_profile.HasNativeDlss;
        var texto =
            $"O programa detectou: {(_profile.HasNativeDlss ? "SIM" : "NÃO")}.\r\n" +
            $"Motivo: {_profile.NativeDlss?.Resumo ?? "sem detecção"}.\r\n\r\n" +
            "O que muda: apenas se o Feeder é instalado ou não. Nenhum arquivo do jogo é apagado em nenhum dos dois casos.\r\n\r\n" +
            "Na dúvida, deixe como está — a detecção lê os arquivos do jogo, não chuta.";
        if (!Dialogos.Confirmar(this, "Ajustar DLSS nativo", $"Mudar para {(novo ? "SIM" : "NÃO")}?", texto, "Mudar")) return;
        _profile.HasNativeDlss = novo;
        _profile.NativeDlssOverridden = true;
        SyncProfileFromUi();
    }

    private string ConsumidorNoRotulo() => _profile?.MotorEfetivo switch
    {
        NeuralEngine.OptiScalerNr => $" Consumidor neural: OptiScaler DLSS-NR, {_profile!.PassCount} passada(s).",
        NeuralEngine.DeepFriedChicken => $" Consumidor neural: Deep Fried Chicken, {_profile!.PassCount} passada(s).",
        NeuralEngine.RenodxDlssShortFuse when _profile!.Architecture == PeArchitecture.X86 => $" Consumidor neural: RenoDX DLSS (ShortFuse) dentro do host64, {_profile!.PassCount} passada(s).",
        _ => string.Empty,
    };

    private void UpdateRouteLabel()
    {
        if (_profile is null) return;
        var route = _profile.Route;
        _lblRoute.ForeColor = route == InstallRoute.Unsupported ? Ui.Bad : Ui.Ok;
        _lblRoute.Text = route switch
        {
            InstallRoute.A => _profile.UsesShortFuse
                ? $"✔ Caminho A (motor ShortFuse) — 64-bit: ReShade ({_profile.ReShadeHookName}) + {ShortFuseDlss.Addon} na pasta do executável, {_profile.PassCount} passada(s) de Neural Rendering."
                : $"✔ Caminho A — 64-bit: ReShade ({_profile.ReShadeHookName}) + addons direto na pasta do executável.",
            InstallRoute.B => $"✔ Caminho B — 32-bit {_profile.Api}: addon32 na raiz (ReShade como {_profile.ReShadeHookName}) e o resto do Feeder dentro de host64\\." + ConsumidorNoRotulo(),
            InstallRoute.C => $"✔ Caminho C — 32-bit {_profile.Api}: dgVoodoo2 ({_profile.DgVoodooWrapperName}) traduz para D3D11, mais o layout do caminho B."
                + (_profile.D3d8ViaD3D9 ? " O D3D8.dll da pasta é a mod (d3d8to9) e fica: o dgVoodoo entra como D3D9.dll atrás dela." : string.Empty)
                + ConsumidorNoRotulo(),
            _ => "✖ Sem caminho suportado para esta combinação. " +
                 (_profile.Architecture == PeArchitecture.X86 && _profile.Api == GraphicsApi.Vulkan
                     ? "Jogo 32-bit em Vulkan não tem caminho neste instalador (o Feeder faz via DXVK, à mão): troque para D3D9, D3D11 ou OpenGL se o jogo permitir."
                     : "Confira arquitetura e API."),
        };
    }

    /// <summary>
    /// Enche a lista com os nomes que a API do jogo aceita e marca o que vale agora.
    /// </summary>
    private void PopularNomesDeReShade()
    {
        if (_profile is null) return;

        // Antes o REFramework escondia esta lista, porque no desenho antigo ele hospedava
        // o ReShade e o nome não valia de nada. Agora os dois convivem na pasta e o
        // ReShade continua sendo a DLL que o jogo carrega: o nome importa nos dois casos.
        var nomes = _profile.NomesDeReShadePossiveis;
        bool cabe = nomes.Count > 1;

        _lblReShadeNome.Visible = cabe;
        _cboReShadeNome.Visible = cabe;
        _lblDicaReShadeNome.Visible = cabe;
        if (!cabe) return;

        var atual = _profile.ReShadeHookName;
        if (_cboReShadeNome.Items.Count != nomes.Count
            || !_cboReShadeNome.Items.Cast<string>().SequenceEqual(nomes))
        {
            _cboReShadeNome.Items.Clear();
            foreach (var n in nomes) _cboReShadeNome.Items.Add(n);
            Ui.AjustarLargura(_cboReShadeNome, 160);
        }
        int i = nomes.ToList().IndexOf(atual);
        _cboReShadeNome.SelectedIndex = i < 0 ? 0 : i;
    }

    private static string DescribeKit(KitInventory kit)
    {
        var have = new List<string>();
        if (kit.NvngxDlssnr is not null) have.Add("nvngx_dlssnr");
        if (kit.NvngxDlss is not null) have.Add("nvngx_dlss");
        if (kit.RenodxAddon64 is not null) have.Add("renodx");
        if (kit.RenodxDlssShortFuse is not null) have.Add("renodx-dlss (ShortFuse)");
        if (kit.HasOptiScalerNr) have.Add("OptiScaler DLSS-NR");
        if (kit.HasDeepFriedChicken) have.Add("Deep Fried Chicken");
        if (kit.FeedAddon64 is not null) have.Add("feed64");
        if (kit.FeedAddon32 is not null) have.Add("feed32");
        if (kit.FeedHost64Exe is not null) have.Add("host64");
        if (kit.DxgiX64 is not null) have.Add("dxgi-x64");
        if (kit.DxgiX86 is not null) have.Add("dxgi-x86");
        if (kit.ReShadeSetup is not null) have.Add("instalador ReShade");
        if (kit.ShadersDir is not null) have.Add("shaders");
        if (kit.HasVort) have.Add("VORT");
        if (kit.HasLaunchpad) have.Add("Launchpad");
        if (kit.HasLumenite) have.Add("LumeniteFX");
        if (kit.HasDrme) have.Add("DRME");
        if (kit.DgVoodooD3D9X86 is not null) have.Add("dgVoodoo-d3d9");
        if (kit.DgVoodooD3D8X86 is not null) have.Add("dgVoodoo-d3d8");
        if (kit.ReFrameworkDinput8 is not null) have.Add("REFramework");
        return string.Join(", ", have);
    }
}
