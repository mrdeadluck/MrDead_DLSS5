using System.Runtime.Versioning;
using Dlss5.Core;

namespace Dlss5.App;

/// <summary>
/// Tela de execução: etapa atual, barra de progresso, log visível ao usuário e
/// cancelamento (só onde é seguro). Também o executor de operações longas, que roda o
/// motor fora da thread da interface e bloqueia ações concorrentes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainForm
{
    private Panel _pExecucao = new();
    private readonly Label _lblEtapa = new();
    private readonly ProgressBar _barra = new();
    private readonly Label _lblNaoFeche = new();
    private readonly TextBox _txtLog = new();
    private readonly Button _btnCancelar = Ui.Secondary(Textos.BotaoCancelar);
    private readonly Label _lblResultadoDaExecucao = new();
    private bool _execucaoTerminou;
    private string _ultimoErro = "";

    private void BuildExecucao()
    {
        _pExecucao = new Panel { Dock = DockStyle.Fill };

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = new Padding(0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _lblEtapa.AutoSize = true;
        _lblEtapa.Dock = DockStyle.Fill;
        _lblEtapa.Font = Ui.SubtitleFont;
        _lblEtapa.Margin = new Padding(0, 0, 0, 6);
        _lblEtapa.AccessibleRole = AccessibleRole.StatusBar;

        _barra.Dock = DockStyle.Fill;
        _barra.Height = 18;
        _barra.Margin = new Padding(0, 0, 0, 8);
        _barra.Style = ProgressBarStyle.Marquee;

        _lblNaoFeche.Text = Textos.NaoFeche;
        _lblNaoFeche.AutoSize = true;
        _lblNaoFeche.Dock = DockStyle.Fill;
        _lblNaoFeche.ForeColor = Ui.Warn;
        _lblNaoFeche.BackColor = Ui.WarnBg;
        _lblNaoFeche.Padding = new Padding(10, 6, 10, 6);
        _lblNaoFeche.Margin = new Padding(0, 0, 0, 8);

        _lblResultadoDaExecucao.AutoSize = true;
        _lblResultadoDaExecucao.Dock = DockStyle.Fill;
        _lblResultadoDaExecucao.Padding = new Padding(10, 8, 10, 8);
        _lblResultadoDaExecucao.Margin = new Padding(0, 0, 0, 8);
        _lblResultadoDaExecucao.Visible = false;

        var fila = Ui.Fila();
        fila.Margin = new Padding(0, 0, 0, 8);
        _btnCancelar.Click += (_, _) => Cancelar();
        fila.Controls.Add(_btnCancelar);
        fila.Controls.Add(Botao(Textos.BotaoCopiarLog, (_, _) => CopiarLog()));
        fila.Controls.Add(Botao(Textos.BotaoAbrirLogs, (_, _) => AbrirPastaDeLogs()));
        fila.Controls.Add(Botao(Textos.BotaoExportarDiagnostico, (_, _) => ExportarDiagnostico()));
        fila.Controls.Add(Botao(Textos.BotaoCopiarErro, (_, _) =>
        {
            try { Clipboard.SetText(_ultimoErro.Length > 0 ? _ultimoErro : _txtLog.Text); } catch { }
            Status("Copiado para a área de transferência.");
        }));

        Ui.StyleReadOnlyBox(_txtLog, mono: true);
        _txtLog.ScrollBars = ScrollBars.Both;
        _txtLog.WordWrap = false;
        _txtLog.Dock = DockStyle.Fill;
        _txtLog.Margin = new Padding(0);
        _txtLog.AccessibleName = "Log da operação";

        t.Controls.Add(_lblEtapa, 0, 0);
        t.Controls.Add(_barra, 0, 1);
        t.Controls.Add(_lblNaoFeche, 0, 2);
        t.Controls.Add(_lblResultadoDaExecucao, 0, 3);
        t.Controls.Add(fila, 0, 4);
        t.Controls.Add(_txtLog, 0, 5);
        _pExecucao.Controls.Add(t);
    }

    private void IniciarExecucao(string nome, bool cancelavel)
    {
        _execucaoTerminou = false;
        _ultimoErro = "";
        _txtLog.Clear();
        _lblEtapa.Text = nome + " — preparando…";
        _lblEtapa.ForeColor = Ui.Ink;
        _barra.Style = ProgressBarStyle.Marquee;
        _barra.Value = 0;
        _lblNaoFeche.Visible = true;
        _lblResultadoDaExecucao.Visible = false;
        _btnCancelar.Visible = cancelavel;
        _btnCancelar.Enabled = cancelavel;
        AtualizarBotoesDaExecucao();
    }

    private void AtualizarProgresso(ProgressoDaOperacao p)
    {
        _lblEtapa.Text = p.Total > 0 && p.Atual <= p.Total
            ? $"{p.Etapa}  ({p.Atual}/{p.Total})"
            : p.Etapa;
        if (p.Total > 0)
        {
            _barra.Style = ProgressBarStyle.Continuous;
            _barra.Maximum = p.Total;
            _barra.Value = Math.Clamp(p.Atual, 0, p.Total);
        }
        else
        {
            _barra.Style = ProgressBarStyle.Marquee;
        }
        if (!string.IsNullOrWhiteSpace(p.Detalhe)) Status(p.Detalhe);
        // Rollback não pode ser interrompido: some o botão.
        if (p.Etapa.Contains("rollback", StringComparison.OrdinalIgnoreCase)) _btnCancelar.Enabled = false;
    }

    private void ConcluirExecucao(bool sucesso, string resumo)
    {
        _execucaoTerminou = true;
        _lblNaoFeche.Visible = false;
        _barra.Style = ProgressBarStyle.Continuous;
        _barra.Maximum = 1;
        _barra.Value = 1;
        _lblEtapa.Text = sucesso ? "✔ Concluído" : "✖ Não concluído";
        _lblEtapa.ForeColor = sucesso ? Ui.Ok : Ui.Bad;
        _lblResultadoDaExecucao.Text = resumo;
        _lblResultadoDaExecucao.ForeColor = sucesso ? Ui.Ok : Ui.Bad;
        _lblResultadoDaExecucao.BackColor = sucesso ? Ui.OkBg : Ui.BadBg;
        _lblResultadoDaExecucao.Visible = true;
        if (!sucesso) _ultimoErro = resumo + "\r\n\r\n" + _txtLog.Text;
        _btnCancelar.Visible = false;
        AtualizarBotoesDaExecucao();
        AtualizarRodape();
    }

    private void AtualizarBotoesDaExecucao()
    {
        _btnCancelar.Enabled = _ocupado && _btnCancelar.Visible && _cts is { IsCancellationRequested: false };
    }

    private void Cancelar()
    {
        if (_cts is null || _cts.IsCancellationRequested) return;
        if (!Dialogos.Confirmar(this, Textos.TituloDoPrograma, "Cancelar a operação?",
                "O que já foi alterado será desfeito (rollback) e a pasta do jogo volta ao estado anterior.", "Cancelar operação", perigosa: true))
            return;
        _cts.Cancel();
        _btnCancelar.Enabled = false;
        _lblEtapa.Text = "Cancelando — aguardando ponto seguro para desfazer…";
        _diario.Aviso("Cancelamento pedido pelo usuário.");
    }

    private void AppendLog(LinhaDeLog l)
    {
        var prefixo = l.Nivel switch
        {
            NivelDeLog.Aviso => "AVISO  ",
            NivelDeLog.Erro => "ERRO   ",
            _ => "       ",
        };
        _txtLog.AppendText($"{l.Hora:HH:mm:ss} {prefixo}{l.Texto}{Environment.NewLine}");
    }

    private void CopiarLog()
    {
        try
        {
            Clipboard.SetText(_txtLog.Text.Length > 0 ? _txtLog.Text : _diario.LerTudo());
            Status("Log copiado para a área de transferência.");
        }
        catch (Exception ex) { Erro("Não consegui copiar", ex); }
    }

    /// <summary>
    /// Roda uma operação do motor fora da thread da interface, com progresso, bloqueio
    /// de ações concorrentes e cancelamento (quando permitido).
    /// </summary>
    private async Task<T> RodarOperacaoAsync<T>(string nome, bool cancelavel, Func<CancellationToken, IProgress<ProgressoDaOperacao>, T> operacao)
    {
        if (_ocupado) throw new InvalidOperationException(Textos.OperacaoEmAndamento);
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetOcupado(true);
        IniciarExecucao(nome, cancelavel);
        Status(nome + " em andamento…");
        var progresso = new Progress<ProgressoDaOperacao>(AtualizarProgresso);
        using var etapa = _diario.Etapa(nome);
        _diario.Tecnico($"Estado antes: {_estado?.Estado}");
        try
        {
            return await Task.Run(() => operacao(token, progresso), CancellationToken.None);
        }
        finally
        {
            SetOcupado(false);
        }
    }

    /// <summary>Fecha uma desinstalação/remoção/restauração: preenche o resultado e avança.</summary>
    private void ConcluirReversao(ResultadoDaReversao r, string nome)
    {
        if (r.Bloqueios.Count > 0)
        {
            var bloqueios = string.Join("\r\n\r\n", r.Bloqueios.Select(b => $"✖ {b.Titulo}\r\n{b.Detalhe}\r\nO que fazer: {b.OQueFazer}"));
            ConcluirExecucao(false, "A operação não começou: nada foi alterado.\r\n\r\n" + bloqueios);
            Dialogos.Mostrar(this, Textos.TituloDoPrograma, "A operação não começou — nada foi alterado", bloqueios, CheckStatusKind.Warn);
            return;
        }

        var sb = new System.Text.StringBuilder();
        if (r.Erro is not null) sb.AppendLine("ERRO: " + r.Erro).AppendLine();
        sb.AppendLine(r.Sucesso ? $"{nome} concluída. O DLSS 5 não está mais instalado neste jogo." : $"{nome} terminou com pendências.");
        sb.AppendLine();
        sb.AppendLine($"Removidos: {r.Removidos.Count}   Restaurados: {r.Restaurados.Count}   Preservados: {r.Preservados.Count}   Não restaurados: {r.NaoRestaurados.Count}   Falhas: {r.Falhas.Count}");
        if (r.OverrideRemovido) sb.AppendLine("Override de assinatura removido do registro (faz efeito após reiniciar o Windows).");

        void Lista(string titulo, IEnumerable<string> itens, string? rodape = null)
        {
            var l = itens.ToList();
            if (l.Count == 0) return;
            sb.AppendLine().AppendLine(titulo);
            foreach (var i in l) sb.AppendLine("   " + i);
            if (rodape is not null) sb.AppendLine("   " + rodape);
        }
        Lista("REMOVIDOS:", r.Removidos);
        Lista("RESTAURADOS (originais devolvidos):", r.Restaurados);
        Lista("PRESERVADOS (ficaram de propósito):", r.Preservados);
        Lista("NÃO RESTAURADOS — o original não pôde voltar:", r.NaoRestaurados,
            "→ Reponha pela loja: Steam → clique direito no jogo → Propriedades → Arquivos instalados → Verificar integridade dos arquivos do jogo.");
        Lista("FALHAS (quase sempre arquivo em uso):", r.Falhas, "→ Feche o jogo e o cliente da loja (ou reinicie o PC) e desinstale de novo.");
        Lista("AINDA NA PASTA (a conferência final encontrou):", r.Sobras);
        sb.AppendLine().AppendLine("Log completo: " + (_diario.ArquivoAtual ?? "(só em memória)"));

        var texto = sb.ToString();
        ConcluirExecucao(r.Sucesso, r.Sucesso
            ? $"{nome} concluída: {r.Removidos.Count} removido(s), {r.Restaurados.Count} restaurado(s), {r.Preservados.Count} preservado(s)."
            : $"{nome} terminou com pendências — veja o resultado.");
        MostrarResultado(r.Sucesso ? CheckStatusKind.Ok : (r.Falhas.Count > 0 || r.Sobras.Count > 0 ? CheckStatusKind.Bad : CheckStatusKind.Warn),
            r.Sucesso ? "DLSS 5 não instalado" : $"{nome} com pendências", texto);
        MostrarTela(Tela.Resultado);
        Status(r.Sucesso ? $"{nome} concluída." : $"{nome} terminou com pendências.");
    }
}
