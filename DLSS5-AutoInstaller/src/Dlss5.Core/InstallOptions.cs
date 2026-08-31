namespace Dlss5.Core;

/// <summary>Opções escolhidas pelo usuário antes de gerar o plano.</summary>
public sealed class InstallOptions
{
    public MvProvider MvProvider { get; set; } = MvProvider.Launchpad;

    /// <summary>Tecla do overlay do ReShade (Home por padrão, Insert como alternativa).</summary>
    public int OverlayKey { get; set; } = ReShadeConfigWriter.KeyHome;

    /// <summary>Aplicar o override de assinatura no registro (precisa de admin + reboot).</summary>
    public bool ApplyRegistryOverride { get; set; } = true;

    /// <summary>Remover os arquivos proibidos (spec 3.7) da pasta do exe.</summary>
    public bool CleanForbidden { get; set; } = true;

    /// <summary>Marca d'água do dgVoodoo ligada (prova de vida; desligar depois).</summary>
    public bool DgVoodooWatermark { get; set; } = true;
}
