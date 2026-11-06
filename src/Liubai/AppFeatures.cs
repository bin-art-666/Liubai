namespace Liubai;

public partial class App
{
    static AiOptionsStore? aiOptions;
    internal static AiOptionsStore AiOptions
    {
        get
        {
            if (aiOptions == null)
            {
                aiOptions = new(Path.Combine(DataDirectory, "ai.json"));
                aiOptions.Changed += () => AiChanged?.Invoke();
            }
            return aiOptions;
        }
    }
    static readonly FeatureModule<AiService> aiModule = new(() =>
    {
        var service = new AiService(DataDirectory, AiOptions);
        service.Changed += () => AiChanged?.Invoke();
        return service;
    });
    static readonly FeatureModule<GuardService> guardModule = new(() =>
    {
        var service = new GuardService(GuardOptions, Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        service.Changed += () => GuardChanged?.Invoke();
        return service;
    });
    internal static AiService? CurrentAi => aiModule.Current;
    internal static GuardService? CurrentGuard => guardModule.Current;
    internal static event Action? AiChanged, GuardChanged;
    internal static string AiStatus => CurrentAi?.Status ?? "点击展开 AI 面板，右上角可放大为独立窗口。";
    internal static string GuardStatus => CurrentGuard?.Status ?? "已关闭（摄像头未开启）";
    internal static void HideAi(bool includeDetached = true) => CurrentAi?.Hide(includeDetached);
    internal static void SelectAi(string provider) => AiOptions.Save(AiOptions.Data with { Provider = provider });
}
