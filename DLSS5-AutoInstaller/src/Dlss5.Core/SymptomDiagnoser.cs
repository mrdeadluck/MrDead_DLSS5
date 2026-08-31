namespace Dlss5.Core;

public sealed record Diagnosis(string Symptom, string Cause, string Fix, string Source);

/// <summary>
/// Mapeia sintomas dos logs para causa e correção (tabela da spec 10).
/// Lê ReShade.log, dlss5-feed.log e os logs do host64.
/// </summary>
public static class SymptomDiagnoser
{
    private sealed record Rule(string Needle, string Symptom, string Cause, string Fix);

    private static readonly Rule[] Rules =
    {
        new("only Direct3D 11 games are supported",
            "addon32 recusou o jogo",
            "Jogo x86 rodando em Vulkan (ou API não-D3D11).",
            "Force D3D9 no jogo e use o dgVoodoo2 (rota C). No Source: -dxlevel 95."),

        new("no known texMotionVectors provider found",
            "Nenhum provedor de motion vectors visível",
            "O provedor não está instalado, não foi marcado, ou não compilou.",
            "Confirme o provedor na pasta reshade-shaders\\Shaders e marcado ACIMA do DLSS 5 Feed. " +
            "Procure 'error X' no ReShade.log."),

        new("not installed",
            "Motion vectors: none (not installed)",
            "O .fx do provedor não está na pasta de shaders.",
            "Copie MotionEstimation.fx (DRME) ou MartysMods_LAUNCHPAD.fx + MartysMods\\*.fxh."),

        new("installed but DISABLED",
            "Provedor instalado mas desativado",
            "A técnica do provedor não está marcada.",
            "Marque o provedor na aba Início, acima do DLSS 5 Feed, e clique em Re-enable."),

        new("WAITING FOR NGX MODULES",
            "WAITING FOR NGX MODULES",
            "O Feed nunca entregou um frame válido ao addon.",
            "Resolva motion vectors e depth primeiro (checkpoints 12–14)."),

        new("0xBAD00007",
            "NGX failure 0xBAD00007",
            "O NGX nunca recebeu um evaluate válido, ou falta o override/reinício.",
            "Confira o override no registro + reinício, e depois os motion vectors."),

        new("Multisampled",
            "Depth buffer multisampled",
            "MSAA ligado no jogo — o Generic Depth não enxerga buffer multisampled.",
            "Desligue MSAA/SSAA nas opções do jogo (FXAA/SMAA podem ficar)."),

        new("error X3020",
            "Shader não compila (X3020)",
            "DRME em Vulkan: 'cannot sample from texture that is also used as render target'.",
            "Troque o provedor para o Launchpad, ou rode o jogo em D3D11/D3D12."),

        new("STANDBY",
            "Painel em STANDBY",
            "Feature ainda não recriada (warm-up) ou assinatura/override pendente.",
            "Espere ~10 s no jogo (rebuild no frame 180). Se persistir, cheque override + reinício."),

        new("ran out of video memory",
            "Sem memória de vídeo (dgVoodoo)",
            "VRAM do dgVoodoo baixa demais (padrão 256 MB).",
            "Ajuste VRAM=1024 no dgVoodoo.conf (o programa já faz isso)."),
    };

    public static IReadOnlyList<Diagnosis> Diagnose(GameProfile profile)
    {
        var results = new List<Diagnosis>();
        var exe = profile.ExeFolder;

        var logs = new (string Path, string Label)[]
        {
            (Path.Combine(exe, "ReShade.log"), "ReShade.log"),
            (Path.Combine(exe, "dlss5-feed.log"), "dlss5-feed.log"),
            (Path.Combine(exe, "host64", "dlss5-feed-host.log"), "host64\\dlss5-feed-host.log"),
            (Path.Combine(exe, "host64", "ReShade.log"), "host64\\ReShade.log"),
        };

        foreach (var (path, label) in logs)
        {
            if (!File.Exists(path)) continue;
            string text;
            try { text = CheckpointVerifier.ReadShared(path); } catch { continue; }

            foreach (var rule in Rules)
            {
                if (!text.Contains(rule.Needle, StringComparison.OrdinalIgnoreCase)) continue;
                if (results.Any(d => d.Symptom == rule.Symptom)) continue;
                results.Add(new Diagnosis(rule.Symptom, rule.Cause, rule.Fix, label));
            }

            // Erros de compilação de shader aparecem como "error X####".
            if (label == "ReShade.log")
            {
                foreach (var line in text.Split('\n'))
                {
                    if (!line.Contains("error X", StringComparison.Ordinal)) continue;
                    var trimmed = line.Trim();
                    if (results.Any(d => d.Symptom.Contains(trimmed, StringComparison.Ordinal))) continue;
                    results.Add(new Diagnosis(
                        "Erro de compilação de shader",
                        trimmed.Length > 200 ? trimmed[..200] : trimmed,
                        "Troque o provedor de motion vectors ou remova o .fx problemático da pasta.",
                        label));
                    break;
                }
            }
        }

        // Sinais estruturais que não vêm de log.
        var addon64InRoot = profile.Route is InstallRoute.B or InstallRoute.C
            && Directory.Exists(exe)
            && Directory.EnumerateFiles(exe, "*.addon64", SearchOption.TopDirectoryOnly).Any();
        if (addon64InRoot)
            results.Add(new Diagnosis(
                "Aba Add-ons vazia (provável)",
                ".addon64 na raiz de um jogo 32-bit — o ReShade ignora em silêncio.",
                "Mova todos os .addon64 para host64\\.",
                "layout de arquivos"));

        return results;
    }
}
