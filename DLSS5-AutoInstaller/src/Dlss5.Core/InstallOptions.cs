namespace Dlss5.Core;

/// <summary>Opções escolhidas pelo usuário antes de gerar o plano.</summary>
public sealed class InstallOptions
{
    public MvProvider MvProvider { get; set; } = MvProviders.Padrao;

    /// <summary>Tecla do overlay do ReShade (qualquer Virtual-Key Code; Home por padrão).</summary>
    public int OverlayKey { get; set; } = ReShadeConfigWriter.KeyHome;

    /// <summary>Modificadores da tecla do overlay (ex.: Ctrl+Shift+Home).</summary>
    public bool OverlayCtrl { get; set; }
    public bool OverlayShift { get; set; }
    public bool OverlayAlt { get; set; }

    /// <summary>Combinação escrita por extenso, para instruções.</summary>
    public string OverlayKeyLabel =>
        ReShadeConfigWriter.DescribeKey(OverlayKey, OverlayCtrl, OverlayShift, OverlayAlt);

    /// <summary>Aplicar o override de assinatura no registro (precisa de admin + reboot).</summary>
    public bool ApplyRegistryOverride { get; set; } = true;

    /// <summary>Remover os arquivos proibidos (spec 3.7) da pasta do exe.</summary>
    public bool CleanForbidden { get; set; } = true;

    /// <summary>Marca d'água do dgVoodoo ligada (prova de vida; desligar depois).</summary>
    public bool DgVoodooWatermark { get; set; } = true;
}
