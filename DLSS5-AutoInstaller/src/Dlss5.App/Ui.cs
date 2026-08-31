namespace Dlss5.App;

/// <summary>
/// Paleta e fábricas de controle. Fica tudo aqui para a interface inteira ter a mesma
/// cara e para as fontes serem criadas uma vez só, em vez de a cada repintura.
/// </summary>
internal static class Ui
{
    public static readonly Color Page = Color.FromArgb(246, 247, 250);
    public static readonly Color Card = Color.White;
    public static readonly Color Line = Color.FromArgb(222, 226, 233);
    public static readonly Color Ink = Color.FromArgb(26, 30, 38);
    public static readonly Color Muted = Color.FromArgb(106, 114, 128);

    public static readonly Color Accent = Color.FromArgb(0, 103, 192);
    public static readonly Color AccentHover = Color.FromArgb(0, 88, 164);

    public static readonly Color Sidebar = Color.FromArgb(27, 32, 43);
    public static readonly Color SidebarDone = Color.FromArgb(196, 205, 219);
    public static readonly Color SidebarIdle = Color.FromArgb(138, 147, 163);

    // Estados dos checkpoints.
    public static readonly Color Ok = Color.FromArgb(15, 118, 62);
    public static readonly Color Bad = Color.FromArgb(176, 35, 30);
    public static readonly Color Warn = Color.FromArgb(146, 97, 0);
    public static readonly Color Info = Color.FromArgb(46, 80, 140);

    public static readonly Font TitleFont = new("Segoe UI", 16F, FontStyle.Bold);
    public static readonly Font BrandFont = new("Segoe UI", 15F, FontStyle.Bold);
    public static readonly Font BodyFont = new("Segoe UI", 9.5F);
    public static readonly Font BoldFont = new("Segoe UI", 9.5F, FontStyle.Bold);
    public static readonly Font StepFont = new("Segoe UI", 10F);
    public static readonly Font StepFontOn = new("Segoe UI", 10F, FontStyle.Bold);
    public static readonly Font SmallFont = new("Segoe UI", 8.5F);
    public static readonly Font MonoFont = new("Consolas", 9.5F);

    /// <summary>Botão da ação principal do passo.</summary>
    public static Button Primary(string text) => MakePrimary(new Button(), text);

    /// <summary>Botão secundário: fundo branco com borda discreta.</summary>
    public static Button Secondary(string text) => MakeSecondary(new Button(), text);

    /// <summary>Aplica o visual primário a um botão que já existe.</summary>
    public static Button MakePrimary(Button b, string text)
    {
        Base(b, text);
        b.BackColor = Accent;
        b.ForeColor = Color.White;
        b.FlatAppearance.BorderColor = Accent;
        b.FlatAppearance.MouseOverBackColor = AccentHover;
        return b;
    }

    /// <summary>Aplica o visual secundário a um botão que já existe.</summary>
    public static Button MakeSecondary(Button b, string text)
    {
        Base(b, text);
        b.BackColor = Card;
        b.ForeColor = Ink;
        b.FlatAppearance.BorderColor = Line;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 242, 248);
        return b;
    }

    private static void Base(Button b, string text)
    {
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.Font = BodyFont;
        b.Cursor = Cursors.Hand;
        b.UseVisualStyleBackColor = false;
        b.AutoSize = false;
        b.FlatAppearance.BorderSize = 1;
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

    public static Color ForState(CheckStatusKind kind) => kind switch
    {
        CheckStatusKind.Ok => Ok,
        CheckStatusKind.Bad => Bad,
        CheckStatusKind.Warn => Warn,
        CheckStatusKind.Info => Info,
        _ => Muted,
    };
}

/// <summary>Sabores de estado usados só para colorir a tabela de verificação.</summary>
internal enum CheckStatusKind
{
    Neutral,
    Ok,
    Bad,
    Warn,
    Info,
}
