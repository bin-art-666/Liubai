using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Liubai;

// Each window owns its backdrop. System policy still controls transparency fallback.
public sealed class GlassBackdrop : SystemBackdrop
{
    MaterialOptions material;
    public GlassBackdrop(GlassStyle style) : this(MaterialOptions.ForPreset(style)) { }
    public GlassBackdrop(MaterialOptions material) => this.material = material;

    public void Update(MaterialOptions material)
    {
        this.material = material;
        UpdatePalette();
    }
    DesktopAcrylicController? controller;
    internal bool IsReady => controller != null || !DesktopAcrylicController.IsSupported();
    SystemBackdropConfiguration? configuration;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        if (!DesktopAcrylicController.IsSupported()) return;
        configuration = GetDefaultSystemBackdropConfiguration(target, root);
        controller = new DesktopAcrylicController();
        UpdatePalette();
        controller.AddSystemBackdropTarget(target);
        controller.SetSystemBackdropConfiguration(configuration);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, root);
        UpdatePalette();
    }

    void UpdatePalette()
    {
        if (controller == null || configuration == null) return;
        bool dark = configuration.Theme == SystemBackdropTheme.Dark;
        controller.Kind = material.Thin ? DesktopAcrylicKind.Thin : DesktopAcrylicKind.Base;
        uint rgb = Convert.ToUInt32(material.TintHex[1..], 16);
        controller.TintColor = material.FollowTheme
            ? (dark ? Color.FromArgb(255, 20, 29, 28) : Color.FromArgb(255, 246, 251, 249))
            : Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        controller.FallbackColor = dark ? Color.FromArgb(255, 31, 39, 38) : Color.FromArgb(255, 239, 245, 242);
        controller.TintOpacity = material.TintOpacity;
        controller.LuminosityOpacity = material.LuminosityOpacity;
        // A desktop bar stays visually consistent when focus moves to another app.
        configuration.IsInputActive = true;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        controller?.RemoveSystemBackdropTarget(target);
        controller?.Dispose();
        controller = null;
        configuration = null;
        base.OnTargetDisconnected(target);
    }
}
