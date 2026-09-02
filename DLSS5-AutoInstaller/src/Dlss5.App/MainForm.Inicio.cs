using System.Runtime.Versioning;
using Dlss5.Core;

namespace Dlss5.App;

/// <summary>
/// Tela inicial: pasta do jogo, estado detectado e as ações que fazem sentido para
/// aquele estado. É daqui que saem os fluxos de instalar, atualizar, reparar,
/// desinstalar e verificar — cada um independente do outro.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainForm
{
    private Panel _pInicio = new();
    private readonly TextBox _txtKit = new();
    private readonly TextBox _txtGame = new();
    private readonly Button _btnVerificarEstado = Ui.Primary(Textos.BotaoVerificarEstado);
    private readonly Label _lblEstadoTitulo = new();
    private readonly Label _lblEstadoResumo = new();
    private readonly TableLayoutPanel _fatos = new();
    private readonly Label _lblAvisos = new();
    private readonly Label _lblBloqueios = new();
    private readonly FlowLayoutPanel _acoes = Ui.Fila();
    private readonly TableLayoutPanel _cartaoEstado = Ui.Cartao();
    private readonly List<Button> _botoesDeAcao = new();

    private void BuildInicio()
    {
        _pInicio = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var coluna = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 4, 0),
        };
        coluna.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // --- Pastas
        var form = Ui.Formulario(3);

        _txtGame.Dock = DockStyle.Fill;
        _txtGame.Margin = new Padding(0, 4, 8, 4);
        _txtGame.AccessibleName = Textos.RotuloPastaDoJogo;
        _txtGame.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await InspecionarAsync(); }
        };
        var filaJogo = LinhaComBotoes(_txtGame,
            Botao(Textos.BotaoProcurar, async (_, _) => { if (Pick(_txtGame, "Selecione a pasta do jogo")) await InspecionarAsync(); }),
            _btnVerificarEstado);
        _btnVerificarEstado.Click += async (_, _) => await InspecionarAsync();
        form.Controls.Add(Ui.Rotulo(Textos.RotuloPastaDoJogo), 0, 0);
        form.Controls.Add(filaJogo, 1, 0);

        _txtKit.Dock = DockStyle.Fill;
        _txtKit.Margin = new Padding(0, 4, 8, 4);
        _txtKit.AccessibleName = Textos.RotuloPastaDoKit;
        var filaKit = LinhaComBotoes(_txtKit,
            Botao(Textos.BotaoProcurar, (_, _) => Pick(_txtKit, "Selecione a pasta com os arquivos do DLSS 5")));
        form.Controls.Add(Ui.Rotulo(Textos.RotuloPastaDoKit), 0, 1);
        form.Controls.Add(filaKit, 1, 1);
        var dicaKit = Ui.Paragrafo(Textos.DicaPastaDoKit, Ui.Muted, Ui.SmallFont);
        dicaKit.Margin = new Padding(0, 0, 0, 10);
        form.Controls.Add(dicaKit, 1, 2);

        coluna.Controls.Add(form);

        // --- Cartão de estado
        var corpo = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = new Padding(0),
        };
        corpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _lblEstadoTitulo.AutoSize = true;
        _lblEstadoTitulo.Dock = DockStyle.Top;
        _lblEstadoTitulo.Font = Ui.SubtitleFont;
        _lblEstadoTitulo.Margin = new Padding(0, 0, 0, 4);
        _lblEstadoTitulo.AccessibleRole = AccessibleRole.StaticText;

        _lblEstadoResumo.AutoSize = true;
        _lblEstadoResumo.Dock = DockStyle.Top;
        _lblEstadoResumo.ForeColor = Ui.Ink;
        _lblEstadoResumo.Margin = new Padding(0, 0, 0, 10);

        _fatos.Dock = DockStyle.Top;
        _fatos.AutoSize = true;
        _fatos.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _fatos.ColumnCount = 2;
        _fatos.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _fatos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _fatos.Margin = new Padding(0, 0, 0, 8);

        _lblAvisos.AutoSize = true;
        _lblAvisos.Dock = DockStyle.Top;
        _lblAvisos.ForeColor = Ui.Warn;
        _lblAvisos.BackColor = Ui.WarnBg;
        _lblAvisos.Padding = new Padding(10, 8, 10, 8);
        _lblAvisos.Margin = new Padding(0, 0, 0, 8);
        _lblAvisos.Visible = false;

        _lblBloqueios.AutoSize = true;
        _lblBloqueios.Dock = DockStyle.Top;
        _lblBloqueios.ForeColor = Ui.Bad;
        _lblBloqueios.BackColor = Ui.BadBg;
        _lblBloqueios.Padding = new Padding(10, 8, 10, 8);
        _lblBloqueios.Margin = new Padding(0, 0, 0, 8);
        _lblBloqueios.Visible = false;

        _acoes.Margin = new Padding(0, 4, 0, 0);

        corpo.Controls.Add(_lblEstadoTitulo);
        corpo.Controls.Add(_lblEstadoResumo);
        corpo.Controls.Add(_fatos);
        corpo.Controls.Add(_lblBloqueios);
        corpo.Controls.Add(_lblAvisos);
        corpo.Controls.Add(_acoes);
        _cartaoEstado.Controls.Add(corpo);
        coluna.Controls.Add(_cartaoEstado);

        // --- Como funciona + limitações
        var info = new TextBox();
        Ui.StyleReadOnlyBox(info);
        info.Dock = DockStyle.Top;
        info.Height = 190;
        info.Margin = new Padding(0, 4, 0, 0);
        info.Text = Textos.ComoFunciona + Textos.LimitacoesTitulo + "\r\n" +
                    ManualSteps.Limitations.Replace("Limitações estruturais (não são erros de configuração):\r\n", "");
        info.TabStop = false;
        coluna.Controls.Add(info);

        _pInicio.Controls.Add(coluna);
        MostrarEstadoVazio();
    }

    private static Button Botao(string texto, EventHandler onClick)
    {
        var b = Ui.Secondary(texto);
        b.Margin = new Padding(0, 4, 8, 4);
        b.Click += onClick;
        return b;
    }

    /// <summary>Caixa de texto que ocupa o resto da largura, com botões à direita que nunca são cortados.</summary>
    private static TableLayoutPanel LinhaComBotoes(Control campo, params Button[] botoes)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1 + botoes.Length,
            RowCount = 1,
            Margin = new Padding(0),
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.Controls.Add(campo, 0, 0);
        for (int i = 0; i < botoes.Length; i++)
        {
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            botoes[i].Margin = new Padding(0, 4, i == botoes.Length - 1 ? 0 : 8, 4);
            t.Controls.Add(botoes[i], i + 1, 0);
        }
        return t;
    }

    private bool Pick(TextBox target, string description)
    {
        using var dlg = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true };
        if (Directory.Exists(target.Text)) dlg.SelectedPath = target.Text;
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        target.Text = dlg.SelectedPath;
        return true;
    }

    // -------------------------------------------------------------- inspeção

    private void MostrarEstadoVazio()
    {
        _lblEstadoTitulo.Text = Textos.TituloDoEstado(ModState.SemJogo);
        _lblEstadoTitulo.ForeColor = Ui.Muted;
        _lblEstadoResumo.Text = "Aponte a pasta do jogo e clique em \"Verificar estado\" (ou pressione Enter).";
        _fatos.Controls.Clear();
        _lblAvisos.Visible = false;
        _lblBloqueios.Visible = false;
        _acoes.Controls.Clear();
        _botoesDeAcao.Clear();
        var b = Ui.Primary(Textos.BotaoDaAcao(AcaoDoMod.SelecionarOutroJogo));
        b.Click += async (_, _) => { if (Pick(_txtGame, "Selecione a pasta do jogo")) await InspecionarAsync(); };
        _acoes.Controls.Add(b);
        _botoesDeAcao.Add(b);
    }

    /// <summary>Inspeciona a pasta em segundo plano e redesenha o cartão de estado.</summary>
    private async Task InspecionarAsync()
    {
        if (_ocupado) { Status(Textos.OperacaoEmAndamento); return; }
        var pasta = _txtGame.Text.Trim();
        var kitPasta = _txtKit.Text.Trim();

        _lblEstadoTitulo.Text = Textos.Analisando;
        _lblEstadoTitulo.ForeColor = Ui.Muted;
        _lblEstadoResumo.Text = "Lendo executáveis, registro de instalação e arquivos do mod. Nada é alterado.";
        _fatos.Controls.Clear();
        _acoes.Controls.Clear();
        _botoesDeAcao.Clear();
        _lblAvisos.Visible = false;
        _lblBloqueios.Visible = false;
        Status(Textos.Analisando);
        SetOcupado(true);
        try
        {
            using var etapa = _diario.Etapa("Inspeção do estado do jogo");
            _diario.Tecnico($"Jogo: {pasta}; kit: {kitPasta}");
            var (estado, kit) = await Task.Run(() =>
            {
                KitInventory? k = null;
                if (Directory.Exists(kitPasta))
                {
                    try { k = KitResolver.Resolve(kitPasta); }
                    catch (Exception ex) { _diario.Aviso($"Kit não pôde ser lido: {ex.Message}"); }
                }
                return (EstadoDoMod.Inspecionar(pasta, k, _diario), k);
            });

            _estado = estado;
            _kit = kit;
            _manifest = estado.Manifesto;
            _profile = estado.Deteccao?.Profile;
            _candidates = estado.Deteccao?.Candidates ?? Array.Empty<ExeCandidate>();
            _diario.Info($"Estado detectado: {estado.Estado} — {estado.Resumo}");
            if (Directory.Exists(pasta)) { _settings.LastGameFolder = pasta; _settings.Save(); }
            if (Directory.Exists(kitPasta)) { _settings.KitFolder = kitPasta; }

            RenderizarEstado(estado);
            Status(Textos.TituloDoEstado(estado.Estado) + ".");
        }
        catch (Exception ex)
        {
            MostrarEstadoVazio();
            Erro("Não consegui analisar a pasta do jogo", ex);
        }
        finally
        {
            SetOcupado(false);
        }
    }

    private void RenderizarEstado(RelatorioDeEstado r)
    {
        var tom = TomDoEstado(r.Estado);
        _lblEstadoTitulo.Text = $"{Ui.SimboloDoEstado(tom)}  {Textos.TituloDoEstado(r.Estado)}";
        _lblEstadoTitulo.ForeColor = Ui.ForState(tom);
        _lblEstadoResumo.Text = r.Resumo;

        _fatos.SuspendLayout();
        _fatos.Controls.Clear();
        _fatos.RowStyles.Clear();
        void Fato(string rotulo, string valor, Color? cor = null)
        {
            int linha = _fatos.RowCount = _fatos.Controls.Count / 2 + 1;
            _fatos.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = new Label { Text = rotulo, AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(0, 2, 14, 2) };
            var v = new Label { Text = valor, AutoSize = true, Dock = DockStyle.Fill, ForeColor = cor ?? Ui.Ink, Margin = new Padding(0, 2, 0, 2) };
            _fatos.Controls.Add(l, 0, linha - 1);
            _fatos.Controls.Add(v, 1, linha - 1);
        }

        Fato(Textos.RotuloPastaDoJogo, r.GameFolder);
        if (r.RealExePath is not null)
            Fato(Textos.RotuloExecutavel, r.RealExePath);
        if (r.Architecture != PeArchitecture.Unknown)
            Fato(Textos.RotuloPerfil, $"{r.Architecture} / {r.Api} / rota {r.Route}" +
                                      (r.Route == InstallRoute.Unsupported ? "  (sem caminho de instalação para esta combinação)" : ""),
                r.Route == InstallRoute.Unsupported ? Ui.Bad : null);

        var instalado = r.Estado switch
        {
            ModState.Instalado or ModState.InstaladoDesatualizado => "Sim",
            ModState.InstalacaoIncompleta or ModState.InstalacaoInconsistente => "Parcialmente",
            ModState.ReversaoIncompleta => "Parcialmente (desinstalação não terminou)",
            ModState.VestigiosSemManifesto => "Arquivos encontrados, sem registro",
            ModState.NaoInstalado or ModState.SomenteBackups => "Não",
            _ => "Não foi possível determinar",
        };
        Fato(Textos.RotuloInstalado, instalado);
        if (r.Manifesto is { } m)
        {
            Fato(Textos.RotuloVersao, $"{Textos.TituloDoPrograma} {r.VersaoDoProgramaInstalada}" +
                                      (r.ProgramaMaisNovo ? "  (esta versão é mais nova)" : ""));
            Fato(Textos.RotuloData, m.InstalledUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") +
                                    $"  — {r.Corretos} arquivo(s) ok, {r.Ausentes} ausente(s), {r.Alterados} alterado(s)");
            Fato(Textos.RotuloBackup, m.BackedUpFiles.Count == 0
                ? "Nenhum arquivo do jogo precisou ser substituído"
                : $"{r.BackupsValidos.Count} válido(s)" + (r.BackupsProblematicos.Count > 0 ? $", {r.BackupsProblematicos.Count} com problema" : ""),
                r.BackupsProblematicos.Count > 0 ? Ui.Warn : null);
        }
        else if (r.BackupsOrfaos.Count > 0)
        {
            Fato(Textos.RotuloBackup, $"{r.BackupsOrfaos.Count} backup(s) .dlss5bak encontrados sem registro");
        }
        if (OperatingSystem.IsWindows())
            Fato(Textos.RotuloRegistro, r.OverrideNoRegistro ? "Aplicado (global no sistema)" : "Não aplicado");
        Fato(Textos.RotuloProximo, r.ProximoPasso, Ui.Info);
        _fatos.ResumeLayout();

        if (r.Bloqueios.Count > 0)
        {
            _lblBloqueios.Text = "Antes de qualquer ação:\r\n" + string.Join("\r\n",
                r.Bloqueios.Select(b => $"✖ {b.Titulo}: {b.Detalhe} → {b.OQueFazer}"));
            _lblBloqueios.Visible = true;
        }
        else _lblBloqueios.Visible = false;

        var avisos = new List<string>(r.Avisos);
        if (r.Conflitos.Count > 0) avisos.Add("Na pasta também há: " + string.Join("; ", r.Conflitos));
        if (avisos.Count > 0)
        {
            _lblAvisos.Text = string.Join("\r\n", avisos.Select(a => "⚠ " + a));
            _lblAvisos.Visible = true;
        }
        else _lblAvisos.Visible = false;

        AtualizarAcoesDoInicio();
    }

    private static CheckStatusKind TomDoEstado(ModState e) => e switch
    {
        ModState.Instalado => CheckStatusKind.Ok,
        ModState.NaoInstalado => CheckStatusKind.Info,
        ModState.InstaladoDesatualizado or ModState.VestigiosSemManifesto or ModState.SomenteBackups => CheckStatusKind.Warn,
        ModState.SemJogo => CheckStatusKind.Neutral,
        _ => CheckStatusKind.Bad,
    };

    /// <summary>Recria os botões de ação a partir do estado; só os válidos aparecem.</summary>
    private void AtualizarAcoesDoInicio()
    {
        if (_estado is null) return;
        _acoes.SuspendLayout();
        _acoes.Controls.Clear();
        _botoesDeAcao.Clear();

        bool primeiro = true;
        foreach (var acao in _estado.Acoes)
        {
            var a = acao;
            Button b = acao switch
            {
                AcaoDoMod.Desinstalar or AcaoDoMod.RemoverVestigios when primeiro => Ui.Danger_(Textos.BotaoDaAcao(acao)),
                _ when primeiro => Ui.Primary(Textos.BotaoDaAcao(acao)),
                _ => Ui.Secondary(Textos.BotaoDaAcao(acao)),
            };
            primeiro = false;
            var dica = Textos.DicaDaAcao(acao);
            if (dica.Length > 0)
            {
                _tooltip.SetToolTip(b, dica);
                b.AccessibleDescription = dica;
            }
            // Ação que modifica a pasta fica desabilitada enquanto houver bloqueio; a
            // explicação está no cartão vermelho logo acima, nunca só na cor do botão.
            bool modifica = acao is AcaoDoMod.Instalar or AcaoDoMod.AtualizarOuReconfigurar or AcaoDoMod.Desinstalar
                or AcaoDoMod.Reparar or AcaoDoMod.RemoverVestigios or AcaoDoMod.RestaurarBackups;
            b.Enabled = !_ocupado && !(modifica && _estado.Bloqueado);
            b.Click += async (_, _) => await ExecutarAcaoAsync(a);
            _acoes.Controls.Add(b);
            _botoesDeAcao.Add(b);
        }
        _acoes.ResumeLayout();
    }

    private readonly ToolTip _tooltip = new() { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 200 };

    // ------------------------------------------------------------ ações

    private async Task ExecutarAcaoAsync(AcaoDoMod acao)
    {
        if (_ocupado) { Status(Textos.OperacaoEmAndamento); return; }
        if (_estado is null) return;
        _diario.Info($"Ação escolhida: {acao}");
        try
        {
            switch (acao)
            {
                case AcaoDoMod.SelecionarOutroJogo:
                    if (Pick(_txtGame, "Selecione a pasta do jogo")) await InspecionarAsync();
                    break;
                case AcaoDoMod.VerDetalhes:
                    MostrarDetalhesDoEstado();
                    break;
                case AcaoDoMod.VerificarInstalacao:
                    IniciarVerificacao();
                    break;
                case AcaoDoMod.Instalar:
                    IniciarFluxoDeInstalacao(Fluxo.Instalar);
                    break;
                case AcaoDoMod.AtualizarOuReconfigurar:
                    IniciarFluxoDeInstalacao(Fluxo.Atualizar);
                    break;
                case AcaoDoMod.Reparar:
                    IniciarFluxoDeInstalacao(Fluxo.Reparar);
                    break;
                case AcaoDoMod.Desinstalar:
                    await IniciarDesinstalacaoAsync();
                    break;
                case AcaoDoMod.RemoverVestigios:
                    await IniciarRemocaoConservadoraAsync();
                    break;
                case AcaoDoMod.RestaurarBackups:
                    await IniciarRestauracaoDeBackupsAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            Erro("A ação não pôde ser iniciada", ex);
        }
    }

    private void MostrarDetalhesDoEstado()
    {
        var texto = Diagnostico.DescreverEstado(_estado) +
                    "\r\nLOG DESTA SESSÃO: " + (_diario.ArquivoAtual ?? "(só em memória — sem permissão na pasta de logs)");
        var r = Dialogos.Mostrar(this, "Detalhes do estado", _estado is null ? "Nenhum jogo inspecionado" : Textos.TituloDoEstado(_estado.Estado),
            texto, _estado is null ? CheckStatusKind.Neutral : TomDoEstado(_estado.Estado), null,
            new Dialogos.Opcao("Fechar", DialogResult.OK, Principal: true),
            new Dialogos.Opcao(Textos.BotaoExportarDiagnostico, DialogResult.Yes),
            new Dialogos.Opcao(Textos.BotaoAbrirLogs, DialogResult.Retry));
        if (r == DialogResult.Yes) ExportarDiagnostico();
        if (r == DialogResult.Retry) AbrirPastaDeLogs();
    }

    /// <summary>Verificação direta, sem instalar: usa o perfil detectado/gravado.</summary>
    private void IniciarVerificacao()
    {
        if (_profile is null) { Aviso("Não há executável detectado para verificar."); return; }
        _fluxo = Fluxo.Verificar;
        _manifest = _estado?.Manifesto;
        if (_manifest is not null) AplicarOpcoesDoManifesto(_manifest);
        MostrarTela(Tela.Verificacao);
        RunVerification();
    }

    /// <summary>Instalar, atualizar ou reparar: precisa do kit; passa pela detecção e pelo plano.</summary>
    private void IniciarFluxoDeInstalacao(Fluxo fluxo)
    {
        var kitPasta = _txtKit.Text.Trim();
        if (!Directory.Exists(kitPasta))
        {
            Aviso("Pasta do kit necessária", Textos.PastaDoKitNaoEncontrada);
            _txtKit.Focus();
            return;
        }
        if (_estado?.Deteccao is null || _profile is null || _candidates.Count == 0)
        {
            Aviso("Nenhum executável encontrado", "Aponte a pasta raiz do jogo (onde está o .exe).");
            return;
        }

        _kit ??= KitResolver.Resolve(kitPasta);
        if (!string.Equals(_kit.KitRoot, kitPasta, StringComparison.OrdinalIgnoreCase))
            _kit = KitResolver.Resolve(kitPasta);
        _settings.KitFolder = kitPasta;
        _settings.Save();

        _fluxo = fluxo;
        _manifest = _estado.Manifesto;
        if (fluxo is Fluxo.Atualizar or Fluxo.Reparar && _manifest is not null)
            AplicarOpcoesDoManifesto(_manifest);

        PreencherDeteccao(_estado.Deteccao, _kit);

        if (fluxo == Fluxo.Reparar)
        {
            // Reparar não pergunta nada de novo: o registro da instalação diz como foi
            // feita. O plano mostra o que vai ser reposto.
            GerarPlano();
            return;
        }
        MostrarTela(Tela.Deteccao);
    }

    private void AplicarOpcoesDoManifesto(InstallManifest m)
    {
        var o = m.OpcoesGravadas();
        _options.MvProvider = o.MvProvider;
        _options.OverlayKey = o.OverlayKey;
        _options.OverlayCtrl = o.OverlayCtrl;
        _options.OverlayShift = o.OverlayShift;
        _options.OverlayAlt = o.OverlayAlt;
        _options.ApplyRegistryOverride = o.ApplyRegistryOverride;
        _options.DgVoodooWatermark = o.DgVoodooWatermark;
        _cboMv.SelectedIndex = o.MvProvider == MvProvider.Drme ? 1 : 0;
        _chkCtrl.Checked = o.OverlayCtrl;
        _chkShift.Checked = o.OverlayShift;
        _chkAlt.Checked = o.OverlayAlt;
        _chkRegistry.Checked = o.ApplyRegistryOverride;
        _chkWatermark.Checked = o.DgVoodooWatermark;
        SelectOverlayKey(o.OverlayKey);
    }

    // ------------------------------------------------------ desinstalação

    private async Task IniciarDesinstalacaoAsync()
    {
        var manifest = _estado?.Manifesto;
        if (manifest is null) { Aviso("Não há registro de instalação para desinstalar. Use \"Remover vestígios\"."); return; }

        var rodando = Preflight.JogoRodando(manifest.RealExePath);
        if (rodando is not null) { Aviso("O jogo está aberto", Textos.JogoAberto(rodando)); return; }

        // Resumo específico: o que sai, o que volta, o que fica, o que não dá para restaurar.
        var r = _estado!;
        var sb = new System.Text.StringBuilder();
        var gravados = manifest.ArquivosGravados.Where(File.Exists).ToList();
        sb.AppendLine($"SERÃO REMOVIDOS ({gravados.Count} arquivos do mod, só os que ainda conferem com o registro):");
        foreach (var f in gravados.Take(40)) sb.AppendLine("   " + Rel(manifest.GameFolder, f));
        if (gravados.Count > 40) sb.AppendLine($"   … e mais {gravados.Count - 40}");
        sb.AppendLine();
        if (r.BackupsValidos.Count > 0)
        {
            sb.AppendLine($"SERÃO RESTAURADOS DO BACKUP ({r.BackupsValidos.Count} arquivos originais do jogo):");
            foreach (var f in r.BackupsValidos) sb.AppendLine("   " + Rel(manifest.GameFolder, f));
            sb.AppendLine();
        }
        if (r.BackupsProblematicos.Count > 0)
        {
            sb.AppendLine("NÃO PODERÃO SER RESTAURADOS (backup ausente ou inválido) — o arquivo do mod sai e o original terá que vir da loja (Steam → Verificar integridade):");
            foreach (var f in r.BackupsProblematicos) sb.AppendLine("   " + f);
            sb.AppendLine();
        }
        if (r.Alterados > 0)
            sb.AppendLine($"SERÃO PRESERVADOS: {r.Alterados} arquivo(s) que não são mais o que o mod gravou (alterados por outro programa).\r\n");
        sb.AppendLine("SEMPRE PRESERVADOS: saves, configurações do jogo, outros mods e qualquer arquivo que o programa não gravou.");
        sb.AppendLine("Também saem: logs e arquivos que o ReShade/Feeder criaram ao rodar, e o registro da instalação (manifesto).");
        if (manifest.RegistryOverrideApplied && OperatingSystem.IsWindows())
            sb.AppendLine("\r\nRegistro do Windows: o override de assinatura NGX foi aplicado por este programa. Marque abaixo para removê-lo também.");

        CheckBox? chk = null;
        if (manifest.RegistryOverrideApplied && OperatingSystem.IsWindows())
        {
            chk = new CheckBox
            {
                Text = Textos.RemoverOverride,
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };
            _tooltip.SetToolTip(chk, Textos.RemoverOverrideDica);
        }
        TableLayoutPanel? extra = null;
        if (chk is not null)
        {
            // TableLayoutPanel de uma coluna: as linhas entram na ordem em que são adicionadas
            // (caixa em cima, explicação embaixo) e a altura cresce com o conteúdo.
            extra = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1 };
            extra.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            extra.Controls.Add(chk);
            extra.Controls.Add(Ui.Paragrafo(Textos.RemoverOverrideDica, Ui.Muted, Ui.SmallFont));
        }

        bool ok = Dialogos.Confirmar(this, "Desinstalar DLSS 5",
            $"Desinstalar o DLSS 5 de \"{Path.GetFileName(manifest.GameFolder.TrimEnd(Path.DirectorySeparatorChar))}\" e restaurar os arquivos originais?",
            sb.ToString(), "Desinstalar e restaurar", perigosa: true, extra: extra);
        if (!ok) return;
        bool removerRegistro = chk?.Checked ?? false;

        _fluxo = Fluxo.Desinstalar;
        _manifest = manifest;
        MostrarTela(Tela.Execucao);
        var resultado = await RodarOperacaoAsync("Desinstalação", cancelavel: false,
            (ct, progresso) => new InstallerEngine(_diario).Revert(manifest, removerRegistro, ct, progresso));
        ConcluirReversao(resultado, "Desinstalação");
    }

    private async Task IniciarRemocaoConservadoraAsync()
    {
        var pasta = _estado?.GameFolder ?? _txtGame.Text.Trim();
        if (!Directory.Exists(pasta)) { Aviso(Textos.PastaDoJogoNaoEncontrada); return; }
        var rodando = Preflight.JogoRodando(_estado?.RealExePath);
        if (rodando is not null) { Aviso("O jogo está aberto", Textos.JogoAberto(rodando)); return; }

        SetOcupado(true);
        Status("Procurando arquivos do mod na pasta do jogo…");
        IReadOnlyList<string> achados;
        try { achados = await Task.Run(() => new InstallerEngine(_diario).EncontrarInstalacao(pasta)); }
        finally { SetOcupado(false); }

        if (achados.Count == 0)
        {
            Dialogos.Informar(this, Textos.TituloDoPrograma, "Nada a remover",
                $"Não achei nada deste programa em:\r\n{pasta}\r\n\r\nSe o jogo ainda se comporta como se o ReShade estivesse instalado, confira se apontou a pasta certa.");
            return;
        }

        var lista = string.Join("\r\n", achados.Select(a => "   " + Rel(pasta, a)));
        bool ok = Dialogos.Confirmar(this, "Remover vestígios",
            $"Remover {achados.Count} item(ns) comprovadamente do mod e devolver os backups ao lugar?",
            "Sem registro de instalação, o critério é conservador: só sai o que não tem como ser do jogo " +
            "(nomes exclusivos do kit, ReShade identificado pelo conteúdo, pastas do mod). Arquivos do jogo, saves e " +
            "arquivos desconhecidos NÃO são tocados. Backups .dlss5bak voltam ao nome original.\r\n\r\n" +
            "O override de assinatura no registro NÃO é alterado neste modo (ele é do sistema, não da pasta).\r\n\r\n" +
            lista, "Remover vestígios", perigosa: true);
        if (!ok) return;

        _fluxo = Fluxo.RemoverVestigios;
        MostrarTela(Tela.Execucao);
        var resultado = await RodarOperacaoAsync("Remoção conservadora", cancelavel: false, (ct, progresso) =>
        {
            var engine = new InstallerEngine(_diario);
            new Isolamento(_diario.Info).ReligarTudo(pasta);
            return engine.LimpezaConservadora(pasta, ct, progresso);
        });
        ConcluirReversao(resultado, "Remoção conservadora");
    }

    private async Task IniciarRestauracaoDeBackupsAsync()
    {
        var pasta = _estado?.GameFolder ?? _txtGame.Text.Trim();
        var backups = _estado?.BackupsOrfaos ?? new List<string>();
        if (backups.Count == 0) { Aviso("Não há backups .dlss5bak para devolver."); return; }
        var rodando = Preflight.JogoRodando(_estado?.RealExePath);
        if (rodando is not null) { Aviso("O jogo está aberto", Textos.JogoAberto(rodando)); return; }

        bool ok = Dialogos.Confirmar(this, "Devolver arquivos originais",
            $"Devolver {backups.Count} arquivo(s) original(is) ao lugar?",
            "Cada arquivo .dlss5bak volta ao nome original, por cima do que estiver lá (que é o arquivo do mod).\r\n\r\n" +
            string.Join("\r\n", backups.Select(b => "   " + Rel(pasta, b))), "Devolver originais");
        if (!ok) return;

        _fluxo = Fluxo.RestaurarBackups;
        MostrarTela(Tela.Execucao);
        var resultado = await RodarOperacaoAsync("Restauração de backups", cancelavel: false, (ct, progresso) =>
        {
            progresso.Report(new ProgressoDaOperacao("Restaurando arquivos originais", 1, 2));
            var engine = new InstallerEngine(_diario);
            var r = new ResultadoDaReversao();
            r.Restaurados.AddRange(engine.RestaurarBackupsOrfaos(pasta));
            progresso.Report(new ProgressoDaOperacao("Conferindo", 2, 2));
            r.Sobras.AddRange(engine.EncontrarInstalacao(pasta).Where(a => a.EndsWith(Propriedade.BackupSuffix, StringComparison.OrdinalIgnoreCase)));
            r.Sucesso = r.Sobras.Count == 0;
            r.ManifestoRemovido = true;
            return r;
        });
        ConcluirReversao(resultado, "Restauração de backups");
    }

    private static string Rel(string raiz, string caminho)
    {
        try { return Path.GetRelativePath(raiz, caminho); } catch { return caminho; }
    }
}
