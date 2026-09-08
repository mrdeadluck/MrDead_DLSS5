namespace Dlss5.App;

/// <summary>
/// Paleta e fábricas de controle. Fica tudo aqui para a interface inteira ter a mesma
/// cara e para as fontes serem criadas uma vez só.
///
/// Regras de layout que valem para o programa inteiro:
/// • nada de posição absoluta — TableLayoutPanel/FlowLayoutPanel com Dock/AutoSize;
/// • todo TableLayoutPanel de uma coluna declara a coluna como Percent 100. Sem isso a
///   coluna é AutoSize, mede cada filho sem limite de largura e o conteúdo fica mais largo
///   que a janela (foi o que cortava o diálogo de desinstalar pela direita);
/// • botão sempre AutoSize com largura mínima, para o texto nunca ser cortado;
/// • rótulos longos quebram linha na largura do pai (Paragrafo); caixas de seleção com
///   texto longo usam Caixa(), porque o CheckBox do WinForms não quebra sozinho;
/// • ComboBox com lista longa usa Adaptavel(): largura natural, mas nunca maior que o pai;
/// • medidas em pixels de 96 DPI: o formulário usa AutoScaleMode.Dpi e escala o que existe
///   na hora em que a janela é criada. O que é criado depois (botões de ação, fatos do
///   estado, passos da barra lateral) passa por EscalarPara() antes de entrar na janela.
/// </summary>
internal static class Ui
{
    public static readonly Color Page = Color.FromArgb(246, 247, 250);
    public static readonly Color Card = Color.White;
    public static readonly Color Line = Color.FromArgb(222, 226, 233);
    public static readonly Color Ink = Color.FromArgb(26, 30, 38);
    public static readonly Color Muted = Color.FromArgb(96, 104, 118);

    public static readonly Color Accent = Color.FromArgb(0, 103, 192);
    public static readonly Color AccentHover = Color.FromArgb(0, 88, 164);
    public static readonly Color AccentPressed = Color.FromArgb(0, 70, 132);
    public static readonly Color Danger = Color.FromArgb(176, 35, 30);
    public static readonly Color DangerHover = Color.FromArgb(150, 28, 24);

    public static readonly Color Sidebar = Color.FromArgb(27, 32, 43);
    public static readonly Color SidebarDone = Color.FromArgb(196, 205, 219);
    public static readonly Color SidebarIdle = Color.FromArgb(138, 147, 163);

    // Estados.
    public static readonly Color Ok = Color.FromArgb(15, 118, 62);
    public static readonly Color Bad = Color.FromArgb(176, 35, 30);
    public static readonly Color Warn = Color.FromArgb(146, 97, 0);
    public static readonly Color Info = Color.FromArgb(46, 80, 140);
    public static readonly Color OkBg = Color.FromArgb(232, 246, 238);
    public static readonly Color BadBg = Color.FromArgb(252, 235, 234);
    public static readonly Color WarnBg = Color.FromArgb(255, 246, 224);
    public static readonly Color InfoBg = Color.FromArgb(234, 241, 252);

    public static readonly Font TitleFont = new("Segoe UI", 16F, FontStyle.Bold);
    public static readonly Font SubtitleFont = new("Segoe UI", 12.5F, FontStyle.Bold);
    public static readonly Font BrandFont = new("Segoe UI", 15F, FontStyle.Bold);
    public static readonly Font BodyFont = new("Segoe UI", 9.5F);
    public static readonly Font BoldFont = new("Segoe UI", 9.5F, FontStyle.Bold);
    public static readonly Font StepFont = new("Segoe UI", 10F);
    public static readonly Font StepFontOn = new("Segoe UI", 10F, FontStyle.Bold);
    public static readonly Font SmallFont = new("Segoe UI", 8.5F);
    public static readonly Font MonoFont = new("Consolas", 9.5F);

    /// <summary>Altura mínima confortável para clique (96 DPI).</summary>
    public const int AlturaDoBotao = 34;
    public const int LarguraMinimaDoBotao = 110;

    /// <summary>
    /// DPI do sistema quando o programa abriu. As fontes em pontos são desenhadas pelo GDI
    /// nessa escala; quando a janela vai para um monitor de escala diferente, o WinForms
    /// troca as fontes dos controles existentes e este valor diz quanto uma fonte nova
    /// precisa crescer para acompanhar.
    /// </summary>
    public static int DpiInicial { get; set; } = 96;

    /// <summary>Pixels lógicos (96 DPI) → pixels do monitor em que o controle está.</summary>
    public static int Px(Control c, int logico) => c.LogicalToDeviceUnits(logico);

    /// <summary>
    /// Escala um controle criado em 96 DPI para uma janela que JÁ passou pelo autoscale do
    /// WinForms. O autoscale roda uma vez, na criação da janela; o que é adicionado depois
    /// chega em pixels de 96 DPI e, em 125/150 %, ficaria com margens, mínimos e fontes
    /// menores que o resto. Idempotente por controle: chame uma vez, antes de adicionar.
    /// </summary>
    public static T EscalarPara<T>(T controle, Control janela) where T : Control
    {
        float fator = janela.DeviceDpi / 96f;
        if (Math.Abs(fator - 1f) > 0.01f) controle.Scale(new SizeF(fator, fator));

        float fonte = janela.DeviceDpi / (float)DpiInicial;
        if (Math.Abs(fonte - 1f) > 0.01f) EscalarFontes(controle, fonte);
        return controle;
    }

    private static void EscalarFontes(Control c, float fator)
    {
        // Só fontes definidas explicitamente (TitleFont, StepFont…). Um controle sem fonte
        // própria ainda responde a fonte padrão do sistema e, ao entrar na janela, herda a
        // fonte do formulário — que o WinForms já reescalou; escalar aqui dobraria.
        var f = c.Font;
        if (!f.Equals(Control.DefaultFont))
            c.Font = new Font(f.FontFamily, f.Size * fator, f.Style, f.Unit, f.GdiCharSet, f.GdiVerticalFont);
        foreach (Control filho in c.Controls) EscalarFontes(filho, fator);
    }

    /// <summary>Botão da ação principal do passo.</summary>
    public static Button Primary(string text) => MakePrimary(new Button(), text);

    /// <summary>Botão secundário: fundo branco com borda discreta.</summary>
    public static Button Secondary(string text) => MakeSecondary(new Button(), text);

    /// <summary>Botão de ação destrutiva (desinstalar, remover).</summary>
    public static Button Danger_(string text)
    {
        var b = new Button();
        Base(b, text);
        b.BackColor = Danger;
        b.ForeColor = Color.White;
        b.FlatAppearance.BorderColor = Danger;
        b.FlatAppearance.MouseOverBackColor = DangerHover;
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(120, 22, 18);
        return b;
    }

    public static Button MakePrimary(Button b, string text)
    {
        Base(b, text);
        b.BackColor = Accent;
        b.ForeColor = Color.White;
        b.FlatAppearance.BorderColor = Accent;
        b.FlatAppearance.MouseOverBackColor = AccentHover;
        b.FlatAppearance.MouseDownBackColor = AccentPressed;
        return b;
    }

    public static Button MakeSecondary(Button b, string text)
    {
        Base(b, text);
        b.BackColor = Card;
        b.ForeColor = Ink;
        b.FlatAppearance.BorderColor = Line;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 242, 248);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(226, 232, 241);
        return b;
    }

    private static void Base(Button b, string text)
    {
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.Font = BodyFont;
        b.Cursor = Cursors.Hand;
        b.UseVisualStyleBackColor = false;
        // O texto manda no tamanho: nunca é cortado. A largura mínima dá área de clique
        // confortável mesmo para rótulos curtos.
        b.AutoSize = true;
        b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        b.MinimumSize = new Size(LarguraMinimaDoBotao, AlturaDoBotao);
        b.Padding = new Padding(12, 4, 12, 4);
        b.Margin = new Padding(0, 0, 8, 8);
        b.FlatAppearance.BorderSize = 1;
        b.TabStop = true;
        // Foco visível por teclado: a borda engrossa quando o botão está focado.
        b.GotFocus += (_, _) => b.FlatAppearance.BorderSize = 2;
        b.LostFocus += (_, _) => b.FlatAppearance.BorderSize = 1;
        b.EnabledChanged += (_, _) => b.Cursor = b.Enabled ? Cursors.Hand : Cursors.Default;
    }

    /// <summary>Rótulo de campo (coluna esquerda). Quebra linha acima de 150 px para a coluna
    /// da direita sobrar em janela estreita.</summary>
    public static Label Rotulo(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(150, 0),
        Margin = new Padding(0, 8, 12, 4),
        ForeColor = Ink,
    };

    /// <summary>Texto corrido que quebra linha conforme a largura disponível.</summary>
    public static Label Paragrafo(string text, Color? cor = null, Font? fonte = null) => QuebrarNoPai(new Label
    {
        Text = text,
        AutoSize = true,
        // AutoSize + Dock: a largura vem do pai e a altura é calculada com quebra de linha.
        Dock = DockStyle.Top,
        ForeColor = cor ?? Muted,
        Font = fonte ?? BodyFont,
        Margin = new Padding(0, 2, 0, 6),
    });

    /// <summary>
    /// Faz um rótulo AutoSize quebrar linha na largura do pai, onde quer que ele esteja.
    /// Numa coluna Percent o TableLayoutPanel já limita a largura; numa coluna AutoSize, num
    /// Panel ou num FlowLayoutPanel ele mediria o texto inteiro numa linha só e sairia da
    /// janela. O MaximumSize acompanha o pai a cada layout (inclusive quando a barra de
    /// rolagem aparece e a área útil encolhe).
    /// </summary>
    public static Label QuebrarNoPai(Label rotulo)
    {
        Control? pai = null;
        void Ajustar(object? s, EventArgs e)
        {
            if (rotulo.Parent is not { } p) return;
            int largura = p.ClientSize.Width - p.Padding.Horizontal - rotulo.Margin.Horizontal;
            if (largura < 40) return;   // ainda sem tamanho útil
            var max = new Size(largura, 0);
            if (rotulo.MaximumSize != max) rotulo.MaximumSize = max;
        }
        rotulo.ParentChanged += (_, _) =>
        {
            if (pai is not null) pai.Layout -= Ajustar;
            pai = rotulo.Parent;
            if (pai is null) return;
            pai.Layout += Ajustar;
            Ajustar(null, EventArgs.Empty);
        };
        return rotulo;
    }

    /// <summary>
    /// Caixa de seleção cujo texto quebra linha na largura do pai. O CheckBox com AutoSize
    /// não quebra: em janela estreita ou escala alta o fim do texto simplesmente some pela
    /// direita. Aqui a largura vem do Dock e a altura é recalculada a cada mudança de
    /// largura, texto ou fonte. Use dentro de TableLayoutPanel (não em FlowLayoutPanel).
    /// </summary>
    public static CheckBox Caixa(string texto, bool marcada = false) =>
        ComQuebra(new CheckBox { Text = texto, Checked = marcada });

    /// <summary>Aplica a quebra de linha de <see cref="Caixa"/> a uma caixa já existente.</summary>
    public static CheckBox ComQuebra(CheckBox c)
    {
        c.AutoSize = false;
        c.Dock = DockStyle.Top;
        c.Margin = new Padding(0, 4, 0, 4);
        c.Padding = new Padding(0, 1, 0, 1);
        void Ajustar(object? s, EventArgs e) => AjustarAlturaDaCaixa(c);
        c.Resize += Ajustar;
        c.TextChanged += Ajustar;
        c.FontChanged += Ajustar;
        c.ParentChanged += Ajustar;
        AjustarAlturaDaCaixa(c);
        return c;
    }

    private static void AjustarAlturaDaCaixa(CheckBox c)
    {
        if (c.Width <= 0 || c.Text.Length == 0) return;
        // O CheckBox mede o texto com quebra de palavra quando recebe uma largura proposta.
        var ideal = c.GetPreferredSize(new Size(c.Width, 0));
        int umaLinha = c.Font.Height + c.Padding.Vertical + 6;
        int altura = Math.Max(ideal.Height, umaLinha);
        bool varias = altura > umaLinha * 3 / 2;
        // Uma linha: caixa e texto centrados como de costume. Várias: caixa alinhada à
        // primeira linha, como nos diálogos do Windows.
        var alinhamento = varias ? ContentAlignment.TopLeft : ContentAlignment.MiddleLeft;
        if (c.CheckAlign != alinhamento) c.CheckAlign = alinhamento;
        if (c.TextAlign != alinhamento) c.TextAlign = alinhamento;
        if (c.Height != altura) c.Height = altura;
    }

    /// <summary>
    /// ComboBox com largura natural (o item mais longo cabe inteiro), mas nunca maior que o
    /// pai. Substitui larguras fixas em pixels, que em janela estreita saíam da tela e em
    /// escala alta cortavam o texto do item.
    /// </summary>
    public static ComboBox Adaptavel(ComboBox cbo, int larguraMinima = 120)
    {
        Control? pai = null;
        void Ajustar(object? s, EventArgs e) => AjustarLargura(cbo, larguraMinima);
        cbo.ParentChanged += (_, _) =>
        {
            if (pai is not null) pai.Layout -= Ajustar;
            pai = cbo.Parent;
            if (pai is null) return;
            pai.Layout += Ajustar;
            Ajustar(null, EventArgs.Empty);
        };
        cbo.FontChanged += Ajustar;
        return cbo;
    }

    /// <summary>Recalcula a largura de um ComboBox adaptável (chame depois de trocar os itens).</summary>
    public static void AjustarLargura(ComboBox cbo, int larguraMinima = 120)
    {
        if (cbo.Parent is not { } p) return;
        int disponivel = p.ClientSize.Width - p.Padding.Horizontal - cbo.Margin.Horizontal;
        if (disponivel < 40) return;   // ainda sem tamanho útil
        int natural = LarguraNatural(cbo);
        int minimo = Math.Min(Px(cbo, larguraMinima), disponivel);
        int largura = Math.Clamp(natural, minimo, disponivel);
        if (cbo.Width != largura) cbo.Width = largura;
    }

    private static int LarguraNatural(ComboBox cbo)
    {
        int texto = 0;
        foreach (var item in cbo.Items)
            texto = Math.Max(texto, TextRenderer.MeasureText(item?.ToString() ?? string.Empty, cbo.Font).Width);
        return texto + SystemInformation.VerticalScrollBarWidth + Px(cbo, 12);
    }

    /// <summary>Linha de campo: rótulo + controle que ocupa o resto da largura.</summary>
    public static TableLayoutPanel Formulario(int linhas)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = linhas,
            Margin = new Padding(0),
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < linhas; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return t;
    }

    /// <summary>Fila de botões que quebra linha quando a janela é estreita.</summary>
    public static FlowLayoutPanel Fila() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        Margin = new Padding(0),
        Padding = new Padding(0),
    };

    /// <summary>Caixa de texto de uma linha que ocupa a largura da coluna.</summary>
    public static TextBox Campo() => new()
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 4, 8, 4),
        Font = BodyFont,
    };

    /// <summary>Cartão com borda e fundo branco (TableLayoutPanel: cresce com o conteúdo de forma confiável).</summary>
    public static TableLayoutPanel Cartao()
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Card,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 12),
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    /// <summary>Caixa de texto de leitura (logs, notas, roteiro) com a mesma aparência.</summary>
    public static void StyleReadOnlyBox(TextBox box, bool mono = false)
    {
        box.Multiline = true;
        box.ReadOnly = true;
        box.ScrollBars = ScrollBars.Vertical;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = Card;
        box.ForeColor = Ink;
        box.Font = mono ? MonoFont : BodyFont;
    }

    /// <summary>
    /// Foto do autor, embutida no executável (assets/mrdead.png). Devolve null quando
    /// o arquivo não foi adicionado ao projeto — aí a interface desenha um monograma.
    /// </summary>
    public static Image? LoadAvatar()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var recursos = asm.GetManifestResourceNames();
            var nome = recursos.FirstOrDefault(n =>
                           n.EndsWith("mrdead.png", StringComparison.OrdinalIgnoreCase))
                       ?? recursos.FirstOrDefault(n =>
                           n.Contains(".assets.", StringComparison.OrdinalIgnoreCase) &&
                           n.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
            if (nome is null) return null;

            using var stream = asm.GetManifestResourceStream(nome);
            if (stream is null) return null;

            using var original = Image.FromStream(stream);
            return new Bitmap(original);
        }
        catch
        {
            return null;
        }
    }

    public static Color ForState(CheckStatusKind kind) => kind switch
    {
        CheckStatusKind.Ok => Ok,
        CheckStatusKind.Bad => Bad,
        CheckStatusKind.Warn => Warn,
        CheckStatusKind.Info => Info,
        _ => Muted,
    };

    public static Color BackgroundForState(CheckStatusKind kind) => kind switch
    {
        CheckStatusKind.Ok => OkBg,
        CheckStatusKind.Bad => BadBg,
        CheckStatusKind.Warn => WarnBg,
        CheckStatusKind.Info => InfoBg,
        _ => Card,
    };

    /// <summary>Prefixo textual do estado: cor nunca é o único sinal.</summary>
    public static string SimboloDoEstado(CheckStatusKind kind) => kind switch
    {
        CheckStatusKind.Ok => "✔",
        CheckStatusKind.Bad => "✖",
        CheckStatusKind.Warn => "⚠",
        CheckStatusKind.Info => "ℹ",
        _ => "•",
    };
}

/// <summary>Sabores de estado usados para colorir tabela e cartões.</summary>
internal enum CheckStatusKind
{
    Neutral,
    Ok,
    Bad,
    Warn,
    Info,
}
