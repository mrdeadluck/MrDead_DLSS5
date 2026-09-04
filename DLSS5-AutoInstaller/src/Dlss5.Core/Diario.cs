using System.Diagnostics;
using System.Text;

namespace Dlss5.Core;

/// <summary>Nível de uma linha do diário.</summary>
public enum NivelDeLog
{
    /// <summary>Detalhe técnico: só vai para o arquivo.</summary>
    Tecnico,
    /// <summary>Informação que o usuário vê na tela.</summary>
    Info,
    Aviso,
    Erro,
}

/// <summary>Uma linha registrada, já com horário.</summary>
public sealed record LinhaDeLog(DateTime Hora, NivelDeLog Nivel, string Texto);

/// <summary>
/// Diário da sessão: grava em arquivo (com rotação) e repassa à interface as linhas que
/// o usuário deve ver. É o único caminho de log do programa — motor, interface e
/// exceções globais escrevem aqui.
///
/// Se a pasta de logs não puder ser criada (permissão, disco), o diário continua
/// funcionando em memória e avisa uma vez; nunca derruba a operação por causa do log.
/// </summary>
public sealed class Diario : IDisposable
{
    public const int MaximoDeArquivos = 10;
    public const long TamanhoMaximoPorArquivo = 2L * 1024 * 1024;

    private readonly object _trava = new();
    private readonly StringBuilder _memoria = new();
    private StreamWriter? _escritor;
    private long _bytesGravados;
    private bool _avisouFalhaDeArquivo;

    /// <summary>Linhas visíveis (Info/Aviso/Erro) chegam aqui, na thread de quem registrou.</summary>
    public event Action<LinhaDeLog>? LinhaVisivel;

    /// <summary>Pasta onde os logs ficam; null quando não foi possível criar.</summary>
    public string? Pasta { get; }

    /// <summary>Arquivo em uso agora; null quando o diário está só em memória.</summary>
    public string? ArquivoAtual { get; private set; }

    public static string PastaPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DLSS5-AutoInstaller", "logs");

    public Diario(string? pasta = null)
    {
        pasta ??= PastaPadrao;
        try
        {
            Directory.CreateDirectory(pasta);
            Pasta = pasta;
            AbrirArquivoNovo();
        }
        catch (Exception ex)
        {
            Pasta = null;
            _escritor = null;
            _memoria.AppendLine($"[diário] Sem acesso à pasta de logs ({pasta}): {ex.Message}. Registrando só em memória.");
        }
        Cabecalho();
    }

    private void AbrirArquivoNovo()
    {
        if (Pasta is null) return;
        Rotacionar(Pasta);
        var nome = $"dlss5-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log";
        ArquivoAtual = Path.Combine(Pasta, nome);
        _escritor = new StreamWriter(new FileStream(ArquivoAtual, FileMode.Create, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false))
        { AutoFlush = true };
        _bytesGravados = 0;
    }

    /// <summary>Mantém no máximo N arquivos: os mais antigos saem.</summary>
    public static void Rotacionar(string pasta, int maximo = MaximoDeArquivos)
    {
        try
        {
            var arquivos = new DirectoryInfo(pasta).GetFiles("dlss5-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            foreach (var velho in arquivos.Skip(Math.Max(0, maximo - 1)))
            {
                try { velho.Delete(); } catch { /* em uso: fica para a próxima */ }
            }
        }
        catch
        {
            // sem permissão de listar: segue sem rotação
        }
    }

    private void Cabecalho()
    {
        Tecnico($"{AppInfo.Nome} {AppInfo.Versao}");
        Tecnico($"Sistema: {AppInfo.SistemaOperacional}");
        Tecnico($"Processo: {Environment.ProcessId}, 64-bit: {Environment.Is64BitProcess}");
        Tecnico($"Início: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} (UTC{DateTimeOffset.Now.Offset:hh\\:mm})");
    }

    public void Tecnico(string texto) => Registrar(NivelDeLog.Tecnico, texto);
    public void Info(string texto) => Registrar(NivelDeLog.Info, texto);
    public void Aviso(string texto) => Registrar(NivelDeLog.Aviso, texto);
    public void Erro(string texto) => Registrar(NivelDeLog.Erro, texto);

    /// <summary>Exceção com stack trace no arquivo e mensagem curta na tela.</summary>
    public void Erro(string contexto, Exception ex)
    {
        Registrar(NivelDeLog.Erro, $"{contexto}: {ex.Message}");
        Registrar(NivelDeLog.Tecnico, ex.ToString());
    }

    /// <summary>Marca o início de uma etapa e devolve um cronômetro que registra a duração ao terminar.</summary>
    public Etapa Etapa(string nome)
    {
        Info($"▶ {nome}");
        return new Etapa(this, nome);
    }

    public void Registrar(NivelDeLog nivel, string texto)
    {
        var linha = new LinhaDeLog(DateTime.Now, nivel, texto);
        var formatada = $"{linha.Hora:HH:mm:ss.fff} [{Rotulo(nivel)}] {texto}";

        lock (_trava)
        {
            try
            {
                if (_escritor is not null)
                {
                    _escritor.WriteLine(formatada);
                    _bytesGravados += formatada.Length + 2;
                    if (_bytesGravados > TamanhoMaximoPorArquivo)
                    {
                        _escritor.Dispose();
                        AbrirArquivoNovo();
                        _escritor?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [tec] (continuação — arquivo anterior atingiu o limite)");
                    }
                }
                else
                {
                    _memoria.AppendLine(formatada);
                    if (_memoria.Length > 4_000_000) _memoria.Remove(0, 1_000_000);
                }
            }
            catch (Exception ex)
            {
                _escritor = null;
                _memoria.AppendLine(formatada);
                if (!_avisouFalhaDeArquivo)
                {
                    _avisouFalhaDeArquivo = true;
                    _memoria.AppendLine($"[diário] Falha ao gravar o arquivo de log: {ex.Message}. Continuando em memória.");
                }
            }
        }

        if (nivel != NivelDeLog.Tecnico) LinhaVisivel?.Invoke(linha);
    }

    /// <summary>Tudo que ficou só em memória (quando o arquivo não pôde ser usado).</summary>
    public string ConteudoEmMemoria
    {
        get { lock (_trava) return _memoria.ToString(); }
    }

    /// <summary>Conteúdo do arquivo atual (ou da memória), para "Copiar" e "Exportar diagnóstico".</summary>
    public string LerTudo()
    {
        lock (_trava)
        {
            try
            {
                if (ArquivoAtual is not null && File.Exists(ArquivoAtual))
                {
                    using var fs = new FileStream(ArquivoAtual, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs);
                    return sr.ReadToEnd() + _memoria;
                }
            }
            catch { /* cai para a memória */ }
            return _memoria.ToString();
        }
    }

    private static string Rotulo(NivelDeLog n) => n switch
    {
        NivelDeLog.Tecnico => "tec",
        NivelDeLog.Info => "inf",
        NivelDeLog.Aviso => "AVI",
        NivelDeLog.Erro => "ERR",
        _ => "???",
    };

    public void Dispose()
    {
        lock (_trava)
        {
            try { _escritor?.Dispose(); } catch { }
            _escritor = null;
        }
    }
}

/// <summary>Cronômetro de uma etapa; registra a duração ao ser descartado.</summary>
public sealed class Etapa : IDisposable
{
    private readonly Diario _diario;
    private readonly string _nome;
    private readonly Stopwatch _relogio = Stopwatch.StartNew();
    private bool _fechada;

    internal Etapa(Diario diario, string nome)
    {
        _diario = diario;
        _nome = nome;
    }

    public void Dispose()
    {
        if (_fechada) return;
        _fechada = true;
        _diario.Tecnico($"◀ {_nome} ({_relogio.ElapsedMilliseconds} ms)");
    }
}
