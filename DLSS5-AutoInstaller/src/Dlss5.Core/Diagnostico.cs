using System.IO.Compression;
using System.Text;

namespace Dlss5.Core;

/// <summary>
/// Pacote de diagnóstico para suporte: log da sessão, manifesto do jogo (se houver),
/// relatório de estado e preferências. Sem senhas, tokens ou dados pessoais — o único
/// dado identificável é o caminho da pasta do jogo, que costuma conter o nome do usuário
/// e é necessário para entender o problema.
/// </summary>
public static class Diagnostico
{
    public static string PastaPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DLSS5-AutoInstaller", "diagnosticos");

    /// <summary>Gera o zip e devolve o caminho dele.</summary>
    public static string Exportar(Diario diario, RelatorioDeEstado? estado, string? pastaDestino = null)
    {
        pastaDestino ??= PastaPadrao;
        Directory.CreateDirectory(pastaDestino);
        var zip = Path.Combine(pastaDestino, $"diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        using var arquivo = ZipFile.Open(zip, ZipArchiveMode.Create);

        Escrever(arquivo, "estado.txt", DescreverEstado(estado));
        Escrever(arquivo, "log-sessao.txt", diario.LerTudo());

        if (diario.Pasta is not null)
        {
            try
            {
                foreach (var log in new DirectoryInfo(diario.Pasta).GetFiles("dlss5-*.log")
                             .OrderByDescending(f => f.LastWriteTimeUtc).Skip(1).Take(3))
                {
                    try { arquivo.CreateEntryFromFile(log.FullName, "logs-anteriores/" + log.Name); } catch { }
                }
            }
            catch { }
        }

        if (estado?.Manifesto is not null)
        {
            try
            {
                var caminho = estado.Manifesto.Caminho;
                if (File.Exists(caminho)) arquivo.CreateEntryFromFile(caminho, InstallManifest.FileName);
            }
            catch { }
        }
        if (estado?.ManifestoCorrompidoEm is not null && File.Exists(estado.ManifestoCorrompidoEm))
        {
            try { arquivo.CreateEntryFromFile(estado.ManifestoCorrompidoEm, "manifesto-corrompido.json"); } catch { }
        }

        try
        {
            var settings = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DLSS5-AutoInstaller", "settings.json");
            if (File.Exists(settings)) arquivo.CreateEntryFromFile(settings, "settings.json");
        }
        catch { }

        return zip;
    }

    private static void Escrever(ZipArchive zip, string nome, string conteudo)
    {
        var entry = zip.CreateEntry(nome);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(conteudo);
    }

    /// <summary>Relatório de estado em texto, arquivo por arquivo. Também é o "Ver detalhes" da interface.</summary>
    public static string DescreverEstado(RelatorioDeEstado? r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppInfo.Nome} {AppInfo.Versao} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Sistema: {AppInfo.SistemaOperacional}");
        if (r is null)
        {
            sb.AppendLine("Nenhum jogo inspecionado.");
            return sb.ToString();
        }

        sb.AppendLine();
        sb.AppendLine($"Estado: {r.Estado}");
        sb.AppendLine($"Resumo: {r.Resumo}");
        sb.AppendLine($"Próximo passo: {r.ProximoPasso}");
        sb.AppendLine($"Pasta do jogo: {r.GameFolder}");
        sb.AppendLine($"Pasta do executável: {r.ExeFolder}");
        sb.AppendLine($"Executável: {r.RealExePath}");
        sb.AppendLine($"Arquitetura / API / rota: {r.Architecture} / {r.Api} / {r.Route}");
        sb.AppendLine($"Override no registro: {(r.OverrideNoRegistro ? "sim" : "não")}");
        sb.AppendLine($"Ações oferecidas: {string.Join(", ", r.Acoes)}");

        if (r.Manifesto is { } m)
        {
            sb.AppendLine();
            sb.AppendLine("MANIFESTO");
            sb.AppendLine($"  Caminho: {m.Caminho}");
            sb.AppendLine($"  Versão do manifesto: {m.Version}; programa: {m.AppVersion ?? "(não registrado)"}; operação: {m.OperationId ?? "-"}; status: {m.Status}");
            sb.AppendLine($"  Instalado em: {m.InstalledUtc:u}; atualizado: {m.UpdatedUtc:u}");
            sb.AppendLine($"  Rota {m.Route}, {m.Architecture}, {m.Api}, MV={m.MvProvider}, DLSS nativo={m.HasNativeDlss}");
            sb.AppendLine($"  Kit: {m.KitRoot} [{m.KitVersion}]  (diferente do apontado agora: {r.KitDiferente})");
            sb.AppendLine($"  Override aplicado por este programa: {m.RegistryOverrideApplied} ({m.RegistryOverrideAppliedUtc:u})");
            sb.AppendLine($"  Jogo atualizado depois da instalação: {r.JogoAtualizadoDepois}");
        }
        if (r.ManifestoCorrompidoEm is not null)
            sb.AppendLine($"Manifesto ILEGÍVEL em: {r.ManifestoCorrompidoEm}");

        if (r.Arquivos.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"ARQUIVOS DA INSTALAÇÃO ({r.Corretos} corretos, {r.Ausentes} ausentes, {r.Alterados} alterados, {r.Ilegiveis} ilegíveis)");
            foreach (var a in r.Arquivos.OrderBy(a => a.Situacao == ConferenciaDeArquivo.Igual).ThenBy(a => a.Caminho))
                sb.AppendLine($"  [{Rotulo(a.Situacao)}] {a.Caminho} ({a.Papel})");
        }
        if (r.BackupsValidos.Count > 0 || r.BackupsProblematicos.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("BACKUPS DOS ORIGINAIS");
            foreach (var b in r.BackupsValidos) sb.AppendLine($"  [ok] {b}");
            foreach (var b in r.BackupsProblematicos) sb.AppendLine($"  [PROBLEMA] {b}");
        }
        if (r.Vestigios.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("VESTÍGIOS (arquivos do mod encontrados por nome)");
            foreach (var v in r.Vestigios) sb.AppendLine("  " + v);
        }
        if (r.BackupsOrfaos.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("BACKUPS ÓRFÃOS (.dlss5bak por devolver)");
            foreach (var v in r.BackupsOrfaos) sb.AppendLine("  " + v);
        }
        if (r.Conflitos.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("CONFLITOS / OUTROS MODS");
            foreach (var c in r.Conflitos) sb.AppendLine("  " + c);
        }
        if (r.Bloqueios.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("BLOQUEIOS");
            foreach (var b in r.Bloqueios) sb.AppendLine($"  {b.Titulo}: {b.Detalhe} → {b.OQueFazer}");
        }
        if (r.Avisos.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("AVISOS");
            foreach (var a in r.Avisos) sb.AppendLine("  " + a);
        }
        return sb.ToString();
    }

    private static string Rotulo(ConferenciaDeArquivo c) => c switch
    {
        ConferenciaDeArquivo.Igual => "ok",
        ConferenciaDeArquivo.Ausente => "AUSENTE",
        ConferenciaDeArquivo.Diferente => "ALTERADO",
        _ => "ILEGÍVEL",
    };
}
