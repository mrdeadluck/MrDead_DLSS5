using System.Runtime.Versioning;
using Dlss5.Core;

namespace Dlss5.App;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Um diário por sessão: tudo que acontece vai para o arquivo (com rotação) e o
        // que o usuário deve ver vai para a tela. Sem permissão na pasta de logs ele
        // continua em memória — nunca impede o programa de abrir.
        using var diario = new Diario();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            diario.Erro("Erro não tratado", ex ?? new Exception(e.ExceptionObject?.ToString() ?? "desconhecido"));
            MostrarErroFatal(ex, diario);
        };
        Application.ThreadException += (_, e) =>
        {
            diario.Erro("Erro não tratado na interface", e.Exception);
            MostrarErroFatal(e.Exception, diario);
        };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.Run(new MainForm(diario));
        diario.Tecnico("Programa encerrado.");
    }

    private static void MostrarErroFatal(Exception? ex, Diario diario)
    {
        try
        {
            Dialogos.Mostrar(null, Textos.TituloDoPrograma, "Erro inesperado",
                (ex?.Message ?? "desconhecido") +
                "\r\n\r\nNenhuma operação de arquivo continua depois de um erro assim: se estava instalando, abra o programa de novo — " +
                "a tela inicial mostra o estado real e oferece Reparar ou Desinstalar.\r\n\r\n" +
                "Detalhes técnicos:\r\n" + ex +
                "\r\n\r\nLog: " + (diario.ArquivoAtual ?? "(só em memória)"),
                CheckStatusKind.Bad);
        }
        catch
        {
            MessageBox.Show("Erro inesperado:\r\n\r\n" + ex, Textos.TituloDoPrograma, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
