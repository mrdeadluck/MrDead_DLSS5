namespace Dlss5.App;

/// <summary>
/// Paleta e fábricas de controle. Fica tudo aqui para a interface inteira ter a mesma
/// cara e para as fontes serem criadas uma vez só.
///
/// Regras de layout que valem para o programa inteiro:
/// • nada de posição absoluta — TableLayoutPanel/FlowLayoutPanel com Dock/AutoSize;
/// • botão sempre AutoSize com largura mínima, para o texto nunca ser cortado;
/// • rótulos longos com AutoSize + MaximumSize para quebrar linha;
/// • medidas em pixels de 96 DPI: o formulário usa AutoScaleMode.Dpi e escala tudo.
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

    /// <summary>Rótulo de campo (coluna esquerda).</summary>
    public static Label Rotulo(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 8, 12, 4),
        ForeColor = Ink,
    };

    /// <summary>Texto corrido que quebra linha conforme a largura disponível.</summary>
    public static Label Paragrafo(string text, Color? cor = null, Font? fonte = null) => new()
    {
        Text = text,
        AutoSize = true,
        // AutoSize + Dock: a largura vem do pai e a altura é calculada com quebra de linha.
        Dock = DockStyle.Top,
        ForeColor = cor ?? Muted,
        Font = fonte ?? BodyFont,
        Margin = new Padding(0, 2, 0, 6),
    };

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
