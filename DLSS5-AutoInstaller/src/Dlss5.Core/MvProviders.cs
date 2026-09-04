namespace Dlss5.Core;

/// <summary>
/// O que cada provedor de motion vectors precisa: o arquivo .fx no kit, o nome real da
/// technique (lido do .fx — se mudar, o preset silenciosamente não ativa nada) e o valor
/// de DLSS5_MV_PROVIDER que o DLSS5_Feed.fx espera para ler a textura certa.
/// </summary>
public static class MvProviders
{
    /// <summary>O provedor que o Feed.fx 0.13 recomenda e que vem no kit (licença MIT).</summary>
    public const MvProvider Padrao = MvProvider.Vort;

    /// <summary>Na ordem em que a tela oferece: o padrão primeiro, o legado por último.</summary>
    public static IReadOnlyList<MvProvider> Ordem { get; } = new[]
    {
        MvProvider.Vort, MvProvider.Launchpad, MvProvider.LumeniteKernel, MvProvider.Drme,
    };

    public static int Indice(MvProvider p)
    {
        for (int i = 0; i < Ordem.Count; i++) if (Ordem[i] == p) return i;
        return 0;
    }

    public static string Rotulo(MvProvider p) => p switch
    {
        MvProvider.Vort => "VORT Motion — recomendado (vem no kit)",
        MvProvider.Launchpad => "Launchpad (iMMERSE)",
        MvProvider.LumeniteKernel => "LumeniteFX Kernel (baixar à parte)",
        _ => "DRME (antigo — não compila no ReShade 6.8)",
    };

    /// <summary>
    /// Valor da definição DLSS5_MV_PROVIDER no DLSS5_Feed.fx: 0 = texMotionVectors (DRME,
    /// qUINT, dh_uber_motion), 1 = iMMERSE Launchpad, 2 = VORT, 3 = LumeniteFX Kernel
    /// (4 = LumeniteFX QuantMotion, que o instalador não oferece).
    /// </summary>
    public static int Definicao(MvProvider p) => p switch
    {
        MvProvider.Launchpad => 1,
        MvProvider.Vort => 2,
        MvProvider.LumeniteKernel => 3,
        _ => 0,
    };

    /// <summary>Arquivo .fx que precisa existir em reshade-shaders\Shaders.</summary>
    public static string ArquivoFx(MvProvider p) => p switch
    {
        MvProvider.Launchpad => "MartysMods_LAUNCHPAD.fx",
        MvProvider.Vort => "vort_Motion.fx",
        MvProvider.LumeniteKernel => "lumenite_Kernel.fx",
        _ => "MotionEstimation.fx",
    };

    /// <summary>Entrada da lista Techniques do preset: technique@arquivo.</summary>
    public static string Technique(MvProvider p) => p switch
    {
        MvProvider.Launchpad => "MartysMods_Launchpad@MartysMods_LAUNCHPAD.fx",
        MvProvider.Vort => "vort_MotionEffects@vort_Motion.fx",
        MvProvider.LumeniteKernel => "Lumenite_Kernel@lumenite_Kernel.fx",
        _ => "DRME@MotionEstimation.fx",
    };

    public static bool Disponivel(KitInventory kit, MvProvider p) => p switch
    {
        MvProvider.Launchpad => kit.HasLaunchpad,
        MvProvider.Vort => kit.HasVort,
        MvProvider.LumeniteKernel => kit.HasLumenite,
        _ => kit.HasDrme,
    };

    /// <summary>O provedor pedido se o kit o tem; senão o primeiro disponível na ordem da tela.</summary>
    public static MvProvider Resolver(KitInventory kit, MvProvider pedido)
    {
        if (Disponivel(kit, pedido)) return pedido;
        foreach (var p in Ordem) if (Disponivel(kit, p)) return p;
        return pedido;
    }
}
