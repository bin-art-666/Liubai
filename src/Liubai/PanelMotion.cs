using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace Liubai;

// Animate the XAML content only. Never delay Hide(), focus dismissal or resize commits.
internal static class PanelMotion
{
    internal static void Reset(FrameworkElement content)
    {
        try
        {
            // Translation must exist before stopping it, including on the first open.
            ElementCompositionPreview.SetIsTranslationEnabled(content, true);
            var visual = ElementCompositionPreview.GetElementVisual(content);
            visual.StopAnimation("Opacity"); visual.Opacity = 1;
            visual.StopAnimation("Translation"); visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
        catch (COMException) { /* A closing/disconnected composition target must not block dismissal. */ }
    }
    internal static void Enter(FrameworkElement content)
    {
        try { Animate(content); }
        catch (COMException) { Reset(content); }
    }
    static void Animate(FrameworkElement content)
    {
        Reset(content);
        if (PanelTheme.Current.Motion == UiMotion.Off || !new UISettings().AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(content);
        var compositor = visual.Compositor;
        var duration = TimeSpan.FromMilliseconds(PanelTheme.Value<double>("EnterMilliseconds"));
        using var easing = compositor.CreateCubicBezierEasingFunction(new(.2f, .8f), new(.2f, 1));
        using var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, 0); opacity.InsertKeyFrame(1, 1, easing); opacity.Duration = duration;
        using var translation = compositor.CreateVector3KeyFrameAnimation();
        translation.InsertKeyFrame(0, new Vector3(0, (float)PanelTheme.Value<double>("EnterOffset"), 0));
        translation.InsertKeyFrame(1, Vector3.Zero, easing); translation.Duration = duration;
        visual.StartAnimation("Opacity", opacity); visual.StartAnimation("Translation", translation);
    }
}
