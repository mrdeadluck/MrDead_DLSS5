using System.Security.Cryptography;

namespace Dlss5.Core;

/// <summary>Em que estado está o mgsvtpp.exe em relação ao patch anti-hook.</summary>
public enum EstadoDoExeFox
{
    /// <summary>Não existe ou não deu para ler.</summary>
    Ausente,
    /// <summary>É o 1.0.15.4 (Steam, inglês) intacto: dá para remendar.</summary>
    Original,
    /// <summary>Já remendado (hash do exe remendado).</summary>
    Remendado,
    /// <summary>Outra versão, outro idioma ou já modificado por outra coisa: não se toca.</summary>
    Desconhecido,
}

/// <summary>
/// O alvo do patch: tudo que identifica o executável e os dois bytes que mudam. Trocável
/// só nos testes (um arquivo pequeno com hashes reais); no Windows é o do Phantom Pain.
/// </summary>
public sealed record AlvoDoPatch(
    long Tamanho, long Offset, byte[] BytesOriginais, byte[] BytesRemendados,
    string Sha256Original, string Sha256Remendado);

/// <summary>
/// O que se sabe da Fox Engine (Metal Gear Solid V: Ground Zeroes e The Phantom Pain).
///
/// O mecanismo, confirmado pelo Discord do RenoDX e pela wiki de modding do MGS V: a Fox
/// Engine confere se as próprias interfaces D3D11 foram enganchadas — fox::gr::dg::
/// CheckModuleHook. O ReShade engancha o ID3D11DeviceContext, a checagem acusa, e o jogo
/// SE FECHA de propósito antes de criar a swapchain. Parece o ReShade travando; é o jogo
/// se encerrando. É exatamente a assinatura do ReShade.log do Ground Zeroes: device criado,
/// addons registrados, 27 contextos adiados, saída limpa. E o teste "só o ReShade"
/// confirmou: sem addon nenhum o jogo também fecha. Não há nome de DLL que contorne isso.
///
/// A cura é um patch de DOIS BYTES no executável, publicado como MGSV-ReShade-AntiHook-
/// Patcher (código-fonte incluso, e guardado no kit em DLSS 5 Files\MGSV\): no offset
/// 0x2B90AB o salto condicional depois do CheckModuleHook (75 2D, jne) vira incondicional
/// (EB 2D, jmp), e o jogo ignora o resultado da checagem. Só vale para o mgsvtpp.exe
/// 1.0.15.4 Steam inglês, identificado por tamanho e SHA-256; qualquer outro exe não é
/// tocado. O programa aplica o patch nativamente — sem exe externo, sem SmartScreen e sem
/// o "Press Enter to close" do patcher, que travaria uma execução automática.
/// </summary>
public static class MotorFox
{
    private static readonly string[] Executaveis =
    {
        "mgsvtpp", "mgsvgz", "MgsGroundZeroes",
        "mgsvmgo",   // Metal Gear Online: mesma engine, mesma checagem
    };

    /// <summary>Sufixo do backup — o mesmo do patcher da comunidade, para um reconhecer o outro.</summary>
    public const string SufixoDoBackup = ".anti-hook-backup";

    public const string NomeDoPatcher = "MGSV-ReShade-AntiHook-Patcher.exe";

    /// <summary>O Phantom Pain 1.0.15.4 (Steam, inglês), conforme o patcher v1.0.</summary>
    public static readonly AlvoDoPatch PhantomPain = new(
        Tamanho: 166_517_760,
        Offset: 0x2B90AB,
        BytesOriginais: new byte[] { 0x75, 0x2D },
        BytesRemendados: new byte[] { 0xEB, 0x2D },
        Sha256Original: "085c2f82d1c963c40b3d2d55786661dfee2b18cbbf388a710c00fa76c5e9bb45",
        Sha256Remendado: "184e0d1abec30561eee4650cb7f913e838692ba30233e8aab5dcbce522d8c297");

    /// <summary>O alvo em uso. Os testes trocam por um arquivo pequeno com hashes reais.</summary>
    public static AlvoDoPatch Alvo { get; set; } = PhantomPain;

    public static bool EhFoxEngine(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        var exe = Path.GetFileNameWithoutExtension(exePath);
        return Executaveis.Any(n => n.Equals(exe, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>O patch conhecido cobre só o The Phantom Pain (mgsvtpp.exe).</summary>
    public static bool PatcherCobre(string? exePath) =>
        !string.IsNullOrWhiteSpace(exePath) &&
        Path.GetFileNameWithoutExtension(exePath).Equals("mgsvtpp", StringComparison.OrdinalIgnoreCase);

    public static string CaminhoDoBackup(string exePath) => exePath + SufixoDoBackup;

    // Hash de 166 MB leva ~1 s; a detecção, o plano e a verificação perguntam várias vezes
    // pelo mesmo arquivo. Cache por (caminho, tamanho, data): muda o arquivo, muda a chave.
    private static readonly Dictionary<string, string> CacheDeHash = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Trava = new();

    private static string? HashDe(string caminho)
    {
        try
        {
            var fi = new FileInfo(caminho);
            if (!fi.Exists) return null;
            var chave = $"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            lock (Trava) { if (CacheDeHash.TryGetValue(chave, out var h)) return h; }
            using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            lock (Trava) { CacheDeHash[chave] = hash; }
            return hash;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Estado do exe frente ao patch, pelo hash — a única identificação que não mente.</summary>
    public static EstadoDoExeFox EstadoDoExe(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return EstadoDoExeFox.Ausente;
        var hash = HashDe(exePath);
        if (hash is null) return EstadoDoExeFox.Ausente;
        if (hash.Equals(Alvo.Sha256Remendado, StringComparison.OrdinalIgnoreCase)) return EstadoDoExeFox.Remendado;
        if (hash.Equals(Alvo.Sha256Original, StringComparison.OrdinalIgnoreCase)) return EstadoDoExeFox.Original;
        return EstadoDoExeFox.Desconhecido;
    }

    /// <summary>
    /// O exe já está remendado. Pelo hash; ou, se o hash não bate com o remendado conhecido,
    /// pelo backup do patcher da comunidade ao lado (outra versão do patch pode existir).
    /// </summary>
    public static bool PatchAplicado(string? exePath) =>
        !string.IsNullOrWhiteSpace(exePath) &&
        (EstadoDoExe(exePath) == EstadoDoExeFox.Remendado || File.Exists(CaminhoDoBackup(exePath)));

    /// <summary>Tamanho e hash do exe, para a mensagem de "não é o 1.0.15.4".</summary>
    public static string DescreverExe(string exePath)
    {
        try
        {
            var fi = new FileInfo(exePath);
            var hash = HashDe(exePath);
            return $"{fi.Length:N0} bytes, SHA-256 {(hash is null ? "?" : hash[..12] + "…")}";
        }
        catch { return "?"; }
    }

    /// <summary>
    /// Aplica o patch: confere tamanho, hash e os dois bytes no offset; guarda o backup;
    /// escreve; confere o hash final e, se não bater, devolve o backup. Idempotente: já
    /// remendado, não faz nada. Exe desconhecido é recusado sem tocar em nada.
    /// </summary>
    public static void AplicarPatch(string exePath, Action<string>? log = null)
    {
        var estado = EstadoDoExe(exePath);
        if (estado == EstadoDoExeFox.Remendado)
        {
            log?.Invoke("Patch anti-hook já aplicado (hash do exe remendado); nada a fazer.");
            return;
        }
        if (estado == EstadoDoExeFox.Ausente)
            throw new InvalidOperationException($"Não achei ou não consegui ler {exePath}.");
        if (estado == EstadoDoExeFox.Desconhecido)
            throw new InvalidOperationException(
                $"O {Path.GetFileName(exePath)} não é o 1.0.15.4 Steam inglês ({DescreverExe(exePath)}); " +
                "o patch conhecido só cobre esse executável, e outro exe não é tocado. Confira a versão e o " +
                "idioma do jogo na Steam.");

        var fi = new FileInfo(exePath);
        if (fi.Length != Alvo.Tamanho)
            throw new InvalidOperationException($"Tamanho inesperado do exe ({fi.Length:N0} bytes). Nada foi alterado.");

        var lidos = new byte[Alvo.BytesOriginais.Length];
        using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fs.Position = Alvo.Offset;
            if (fs.Read(lidos, 0, lidos.Length) != lidos.Length || !lidos.AsSpan().SequenceEqual(Alvo.BytesOriginais))
                throw new InvalidOperationException("Bytes inesperados no ponto do patch. Nada foi alterado.");
        }

        var backup = CaminhoDoBackup(exePath);
        if (File.Exists(backup))
        {
            var hb = HashDe(backup);
            if (!Alvo.Sha256Original.Equals(hb, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Já existe {Path.GetFileName(backup)} com outro conteúdo. Nada foi alterado.");
        }
        else
        {
            File.Copy(exePath, backup, overwrite: false);
            log?.Invoke($"Backup do exe original guardado: {Path.GetFileName(backup)}");
        }

        using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            fs.Position = Alvo.Offset;
            fs.Write(Alvo.BytesRemendados, 0, Alvo.BytesRemendados.Length);
            fs.Flush(true);
        }

        if (EstadoDoExe(exePath) != EstadoDoExeFox.Remendado)
        {
            File.Copy(backup, exePath, overwrite: true);
            throw new InvalidOperationException(
                "O hash depois do patch não bateu com o esperado; o exe original foi devolvido do backup.");
        }
        log?.Invoke($"Patch anti-hook aplicado em {Path.GetFileName(exePath)} (offset 0x{Alvo.Offset:X}: " +
                    $"{Convert.ToHexString(Alvo.BytesOriginais)} → {Convert.ToHexString(Alvo.BytesRemendados)}).");
    }

    /// <summary>Frase da tela de detecção.</summary>
    public const string Aviso =
        "Fox Engine (Metal Gear Solid V): o jogo confere se as interfaces D3D11 foram enganchadas " +
        "(fox::gr::dg::CheckModuleHook) e, como o ReShade engancha o ID3D11DeviceContext, ele se " +
        "FECHA de propósito antes de criar a swapchain — sem mensagem, parecendo travamento. " +
        "Nenhum nome de DLL contorna isso: a checagem olha o gancho, não o arquivo.";

    /// <summary>O que a instalação faz por conta própria no Phantom Pain 1.0.15.4.</summary>
    public const string PatchAutomatico =
        "A instalação aplica o patch anti-hook no mgsvtpp.exe ela mesma, antes de qualquer outro " +
        "arquivo: dois bytes no offset 0x2B90AB (75 2D → EB 2D), o mesmo remendo do " +
        "MGSV-ReShade-AntiHook-Patcher da comunidade, com o backup mgsvtpp.exe" + SufixoDoBackup +
        " guardado ao lado. Só vale para o 1.0.15.4 Steam inglês (tamanho e SHA-256 conferidos); " +
        "outro exe não é tocado. Desinstalar não desfaz o patch — o jogo abre normalmente com ele; " +
        "para voltar ao original, renomeie o backup por cima do exe. A verificação de integridade " +
        "da Steam restaura o exe original e desfaz o patch: se fizer, instale de novo.";

    /// <summary>Exe que não é o 1.0.15.4 inglês: não há o que fazer sem outro patch.</summary>
    public static string ExeNaoCoberto(string exePath) =>
        $"O {Path.GetFileName(exePath)} desta pasta não é o 1.0.15.4 Steam inglês ({DescreverExe(exePath)}): " +
        "o patch conhecido tem offset e hash desse executável, e o programa não remenda outro. Confira na " +
        "Steam a versão e o idioma do jogo (o exe inglês tem 166.517.760 bytes). Se a Steam acabou de " +
        "atualizar o jogo, o patch precisa ser redescoberto para a versão nova.";

    /// <summary>Ground Zeroes não tem patch conhecido.</summary>
    public const string SemPatcherParaGz =
        "O patch conhecido cobre só o The Phantom Pain (mgsvtpp.exe 1.0.15.4). Para o Ground Zeroes " +
        "não há patch publicado: o CheckModuleHook fecha o jogo com qualquer ReShade, e o programa não " +
        "vai instalar para você testar de novo o que já está provado. Se surgir um patch para o " +
        "MgsGroundZeroes.exe, o backup .anti-hook-backup ao lado do exe libera a instalação.";
}
