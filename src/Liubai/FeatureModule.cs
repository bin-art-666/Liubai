namespace Liubai;

// Accessed on the UI dispatcher. Reading state never constructs the feature.
internal sealed class FeatureModule<T>(Func<T> factory) where T : class
{
    T? value;
    internal T? Current => value;
    internal string? LastError { get; private set; }
    internal T Get()
    {
        if (value != null) return value;
        try { value = factory(); LastError = null; return value; }
        catch (Exception ex) { LastError = ex.Message; throw; }
    }
    internal void Release(Action<T> stop)
    {
        if (value == null) return;
        stop(value); value = null;
    }
}
