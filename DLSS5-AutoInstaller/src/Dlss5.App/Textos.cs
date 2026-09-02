using Dlss5.Core;

namespace Dlss5.App;

/// <summary>
/// Todos os textos da interface num lugar só: títulos, botões, mensagens e explicações.
/// Facilita revisar o tom, evitar termos técnicos sem explicação e traduzir depois.
/// </summary>
internal static class Textos
{
    public const string TituloDoPrograma = "DLSS 5 AutoInstaller";

    // ---- Passos (barra lateral)
    public const string PassoJogo = "Jogo e estado";
    public const string PassoDeteccao = "Detecção";
    public const string PassoPlano = "Plano";
    public const string PassoResumo = "Resumo";
    public const string PassoExecucao = "Execução";
    public const string PassoVerificacao = "Verificação";
    public const string PassoResultado = "Resultado";

    // ---- Tela inicial
    public const string InicioTitulo = "Jogo e estado do DLSS 5";
    public const string InicioDica =
        "Aponte a pasta do jogo. O programa verifica sozinho se o DLSS 5 já está instalado e mostra só as ações que fazem sentido para o estado atual.";
    public const string RotuloPastaDoJogo = "Pasta do jogo";
    public const string RotuloPastaDoKit = "Pasta do kit DLSS 5";
    public const string DicaPastaDoKit = "Necessária só para instalar, atualizar ou reparar. Para desinstalar, não precisa.";
    public const string BotaoProcurar = "Procurar…";
    public const string BotaoVerificarEstado = "Verificar estado";
    public const string Analisando = "Analisando a pasta do jogo…";

    public const string RotuloEstado = "Estado";
    public const string RotuloExecutavel = "Executável";
    public const string RotuloPerfil = "Arquitetura / API / rota";
    public const string RotuloInstalado = "DLSS 5 instalado";
    public const string RotuloVersao = "Instalado por";
    public const string RotuloData = "Instalado em";
    public const string RotuloBackup = "Backup dos originais";
    public const string RotuloRegistro = "Override no registro";
    public const string RotuloProximo = "Próximo passo";

    public static string TituloDoEstado(ModState e) => e switch
    {
        ModState.SemJogo => "Nenhum jogo selecionado",
        ModState.JogoNaoEncontrado => "Pasta do jogo não encontrada",
        ModState.JogoSemExecutavel => "Esta pasta não tem um jogo",
        ModState.NaoInstalado => "DLSS 5 não instalado",
        ModState.Instalado => "DLSS 5 instalado",
        ModState.InstaladoDesatualizado => "DLSS 5 instalado — atualização disponível",
        ModState.InstalacaoIncompleta => "Instalação incompleta",
        ModState.InstalacaoInconsistente => "Instalação inconsistente",
        ModState.ReversaoIncompleta => "Desinstalação incompleta",
        ModState.VestigiosSemManifesto => "Arquivos do mod sem registro de instalação",
        ModState.SomenteBackups => "Backups de originais por devolver",
        _ => "Estado desconhecido — diagnóstico necessário",
    };

    public static string BotaoDaAcao(AcaoDoMod a) => a switch
    {
        AcaoDoMod.Instalar => "Instalar DLSS 5",
        AcaoDoMod.AtualizarOuReconfigurar => "Atualizar ou reconfigurar",
        AcaoDoMod.Desinstalar => "Desinstalar e restaurar arquivos originais",
        AcaoDoMod.Reparar => "Reparar instalação",
        AcaoDoMod.RemoverVestigios => "Remover vestígios (modo conservador)",
        AcaoDoMod.RestaurarBackups => "Devolver arquivos originais",
        AcaoDoMod.SelecionarOutroJogo => "Selecionar outro jogo",
        AcaoDoMod.VerDetalhes => "Ver detalhes do problema",
        AcaoDoMod.VerificarInstalacao => "Verificar instalação",
        _ => a.ToString(),
    };

    public static string DicaDaAcao(AcaoDoMod a) => a switch
    {
        AcaoDoMod.Instalar => "Detecta o jogo, mostra o plano completo e só então copia os arquivos. Reversível.",
        AcaoDoMod.AtualizarOuReconfigurar => "Regrava os arquivos do mod com o kit atual e deixa mudar as opções. Não refaz backups dos originais.",
        AcaoDoMod.Desinstalar => "Remove só o que o programa gravou, devolve os originais dos backups e mostra o que foi feito. Não precisa do kit.",
        AcaoDoMod.Reparar => "Compara o registro da instalação com a pasta e repõe só o que falta ou mudou.",
        AcaoDoMod.RemoverVestigios => "Sem registro de instalação: lista o que é comprovadamente do mod, pede confirmação e remove. Arquivos desconhecidos ficam.",
        AcaoDoMod.RestaurarBackups => "Move cada arquivo .dlss5bak de volta ao nome original.",
        AcaoDoMod.SelecionarOutroJogo => "Escolher outra pasta.",
        AcaoDoMod.VerDetalhes => "Lista arquivo por arquivo o que está certo, ausente, alterado ou em conflito.",
        AcaoDoMod.VerificarInstalacao => "Confere arquivos, registro e logs do jogo, sem alterar nada.",
        _ => "",
    };

    public const string ComoFunciona =
        "Como funciona\r\n" +
        "1. Aponte a pasta do jogo. O programa verifica o estado e mostra as ações possíveis.\r\n" +
        "2. Para instalar: confira a detecção (executável, arquitetura, API), veja o plano e confirme.\r\n" +
        "3. Acompanhe o progresso. Se algo falhar, tudo que foi alterado é desfeito automaticamente.\r\n" +
        "4. Para desinstalar: abra o programa, aponte o jogo e clique em Desinstalar. Não precisa do kit nem de reinstalar.\r\n" +
        "\r\nTudo que o programa grava fica registrado num manifesto na pasta do jogo (com tamanho e hash de cada arquivo). " +
        "Os arquivos originais que precisam ser substituídos são guardados como .dlss5bak e nunca são sobrescritos por uma reinstalação.\r\n\r\n";

    // ---- Detecção
    public const string DeteccaoTitulo = "Detecção";
    public const string DeteccaoDica = "Confira o que foi detectado. A API gráfica é o único palpite que pode errar: corrija se souber que o jogo usa outra.";

    // ---- Plano
    public const string PlanoTitulo = "Plano";
    public const string PlanoDica = "Exatamente o que será feito, antes de tocar em qualquer arquivo. Nada foi alterado ainda.";
    public const string ConfirmarConflitos = "Entendi: os arquivos listados em \"Conflitos\" serão substituídos, com backup dos originais.";

    // ---- Execução
    public const string ExecucaoTitulo = "Execução";
    public const string ExecucaoDicaInstalar = "Copiando arquivos, gerando configurações e validando. Não feche o programa até terminar.";
    public const string ExecucaoDicaDesinstalar = "Removendo componentes do mod e restaurando os originais. Não feche o programa até terminar.";
    public const string NaoFeche = "⚠ Operação em andamento — não feche o programa nem abra o jogo.";
    public const string BotaoCancelar = "Cancelar";
    public const string BotaoCopiarLog = "Copiar log";
    public const string BotaoAbrirLogs = "Abrir pasta de logs";
    public const string BotaoExportarDiagnostico = "Exportar diagnóstico";
    public const string BotaoVerDetalhes = "Ver detalhes";
    public const string BotaoCopiarErro = "Copiar erro";

    // ---- Verificação
    public const string VerificacaoTitulo = "Verificação e passos manuais";
    public const string VerificacaoDica = "O que dá para verificar por arquivo e registro já foi verificado. O resto está no roteiro abaixo.";

    // ---- Resultado
    public const string ResultadoTitulo = "Resultado";

    // ---- Navegação
    public const string BotaoVoltar = "‹ Voltar";
    public const string BotaoInicio = "Voltar ao início";
    public const string BotaoDetectar = "Detectar ›";
    public const string BotaoGerarPlano = "Gerar plano ›";
    public const string BotaoInstalar = "Instalar ›";
    public const string BotaoAtualizar = "Atualizar ›";
    public const string BotaoReparar = "Reparar ›";
    public const string BotaoVerificar = "Verificar ›";
    public const string BotaoConcluir = "Concluir";

    // ---- Mensagens
    public const string PastaDoJogoNaoEncontrada = "A pasta do jogo não existe. Confira o caminho ou use Procurar.";
    public const string PastaDoKitNaoEncontrada = "A pasta do kit DLSS 5 não existe. Ela é necessária para instalar, atualizar ou reparar. Aponte a pasta \"DLSS 5 Files\".";
    public const string OperacaoEmAndamento = "Há uma operação em andamento. Aguarde ela terminar.";
    public const string FecharDuranteOperacao =
        "Há uma operação em andamento. Fechar agora pode deixar a instalação incompleta " +
        "(o programa consegue reparar na próxima abertura, mas é melhor esperar).\r\n\r\nFechar mesmo assim?";

    public static string JogoAberto(string processo) =>
        $"O jogo parece estar aberto ({processo}.exe). Feche o jogo (e o cliente da loja, se ele mantiver o jogo aberto) e tente de novo. " +
        "Arquivos em uso não podem ser substituídos nem removidos.";

    public const string RemoverOverride =
        "Remover também o override de assinatura NGX do registro do Windows";
    public const string RemoverOverrideDica =
        "O override é global: vale para todos os jogos deste PC. Desmarque se você usa o DLSS 5 em outro jogo. " +
        "A mudança só faz efeito depois de reiniciar o Windows.";

    public const string LimitacoesTitulo = "Limitações do método (não são erros de configuração)";
}
