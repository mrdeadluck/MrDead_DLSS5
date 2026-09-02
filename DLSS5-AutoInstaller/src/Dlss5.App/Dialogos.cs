using System.Runtime.Versioning;

namespace Dlss5.App;

/// <summary>
/// Diálogos do programa. Um MessageBox corta texto longo e não rola; aqui a caixa é
/// redimensionável, tem rolagem, o texto pode ser copiado e os botões nunca são cortados.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Dialogos
{
    /// <summary>Uma opção de rodapé: rótulo, resultado e se é a principal.</summary>
    public sealed record Opcao(string Texto, DialogResult Resultado, bool Principal = false, bool Perigosa = false);

    /// <summary>
    /// Mostra um texto longo com botões. <paramref name="extra"/> permite um controle
    /// adicional acima dos botões (uma caixa de seleção, por exemplo).
    /// </summary>
    public static DialogResult Mostrar(
        IWin32Window? dono, string titulo, string cabecalho, string texto,
        CheckStatusKind tom = CheckStatusKind.Info, Control? extra = null, params Opcao[] opcoes)
    {
        if (opcoes.Length == 0) opcoes = new[] { new Opcao("OK", DialogResult.OK, Principal: true) };

        using var f = new Form
        {
            Text = titulo,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false,
            MaximizeBox = true,
            ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96F, 96F),
            Font = Ui.BodyFont,
            BackColor = Ui.Page,
            MinimumSize = new Size(420, 260),
            Size = new Size(720, 520),
            KeyPreview = true,
        };

        var raiz = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16),
        };
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var cab = new Label
        {
            Text = $"{Ui.SimboloDoEstado(tom)}  {cabecalho}",
            AutoSize = true,
            Font = Ui.SubtitleFont,
            ForeColor = Ui.ForState(tom),
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 10),
        };
        // O rótulo quebra linha conforme a largura da janela.
        f.Resize += (_, _) => cab.MaximumSize = new Size(Math.Max(200, f.ClientSize.Width - 40), 0);
        cab.MaximumSize = new Size(Math.Max(200, f.ClientSize.Width - 40), 0);

        var corpo = new TextBox();
        Ui.StyleReadOnlyBox(corpo);
        corpo.Dock = DockStyle.Fill;
        corpo.Text = texto;
        corpo.Margin = new Padding(0, 0, 0, 10);
        corpo.ScrollBars = ScrollBars.Both;
        corpo.WordWrap = true;

        var extraHost = new Panel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0) };
        if (extra is not null)
        {
            extra.Dock = DockStyle.Top;
            extraHost.Controls.Add(extra);
        }

        var fila = Ui.Fila();
        fila.FlowDirection = FlowDirection.RightToLeft;
        fila.Margin = new Padding(0, 6, 0, 0);
        Button? principal = null;
        foreach (var o in opcoes.Reverse())
        {
            var b = o.Perigosa ? Ui.Danger_(o.Texto) : o.Principal ? Ui.Primary(o.Texto) : Ui.Secondary(o.Texto);
            b.Margin = new Padding(8, 0, 0, 0);
            b.DialogResult = o.Resultado;
            b.Click += (_, _) => { f.DialogResult = o.Resultado; f.Close(); };
            fila.Controls.Add(b);
            if (o.Principal) principal = b;
        }
        var copiar = Ui.Secondary("Copiar texto");
        copiar.Margin = new Padding(8, 0, 0, 0);
        copiar.Click += (_, _) =>
        {
            try { Clipboard.SetText(cabecalho + Environment.NewLine + Environment.NewLine + texto); } catch { }
        };
        fila.Controls.Add(copiar);

        raiz.Controls.Add(cab, 0, 0);
        raiz.Controls.Add(corpo, 0, 1);
        raiz.Controls.Add(extraHost, 0, 2);
        raiz.Controls.Add(fila, 0, 3);
        f.Controls.Add(raiz);

        if (principal is not null) f.AcceptButton = principal;
        var cancel = opcoes.FirstOrDefault(o => o.Resultado is DialogResult.Cancel or DialogResult.No);
        f.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                f.DialogResult = cancel?.Resultado ?? DialogResult.Cancel;
                f.Close();
            }
        };

        f.Shown += (_, _) => (principal ?? (Control)copiar).Focus();
        return f.ShowDialog(dono);
    }

    public static void Informar(IWin32Window? dono, string titulo, string cabecalho, string texto) =>
        Mostrar(dono, titulo, cabecalho, texto, CheckStatusKind.Info);

    public static void Avisar(IWin32Window? dono, string cabecalho, string texto = "") =>
        Mostrar(dono, Textos.TituloDoPrograma, cabecalho, texto, CheckStatusKind.Warn);

    public static void Falha(IWin32Window? dono, string cabecalho, string texto) =>
        Mostrar(dono, Textos.TituloDoPrograma, cabecalho, texto, CheckStatusKind.Bad);

    public static bool Confirmar(IWin32Window? dono, string titulo, string cabecalho, string texto,
        string textoDoBotao, bool perigosa = false, Control? extra = null)
    {
        var r = Mostrar(dono, titulo, cabecalho, texto, perigosa ? CheckStatusKind.Warn : CheckStatusKind.Info, extra,
            new Opcao(textoDoBotao, DialogResult.Yes, Principal: !perigosa, Perigosa: perigosa),
            new Opcao("Cancelar", DialogResult.Cancel));
        return r == DialogResult.Yes;
    }

    /// <summary>Pergunta de sim/não simples (respostas de teste de isolamento, por exemplo).</summary>
    public static bool Pergunta(IWin32Window? dono, string titulo, string pergunta, string detalhe = "")
    {
        var r = Mostrar(dono, titulo, pergunta, detalhe, CheckStatusKind.Info, null,
            new Opcao("Sim", DialogResult.Yes, Principal: true),
            new Opcao("Não", DialogResult.No));
        return r == DialogResult.Yes;
    }
}
