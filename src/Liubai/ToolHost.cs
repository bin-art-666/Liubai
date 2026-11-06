namespace Liubai;

internal sealed record ToolAction<T>(Func<Task> Invoke, Func<bool>? Active = null,
    Func<T, bool>? Accepts = null, Func<T, Task<bool>>? Drop = null, Action? Peek = null);

// Owned by one UI host. Payloads are explicit user actions, never ambient clipboard data.
internal sealed class ToolHost<T>(ToolCatalog catalog, Func<bool> available, Action<string, Exception> failed)
{
    readonly Dictionary<string, ToolAction<T>> actions = new(StringComparer.Ordinal);
    readonly HashSet<string> running = new(StringComparer.Ordinal);
    readonly Dictionary<string, TaskCompletionSource> pending = new(StringComparer.Ordinal);
    bool sealedHost, stopped;
    internal void Register(string id, ToolAction<T> action)
    {
        if (sealedHost || !catalog.Contains(id) || actions.ContainsKey(id)) throw new InvalidOperationException("工具重复注册或注册已结束。");
        var definition = catalog[id];
        if (action.Invoke == null || definition.Has(ToolCapabilities.Drop) != (action.Drop != null)
            || definition.Has(ToolCapabilities.Drop) != (action.Accepts != null)
            || definition.Has(ToolCapabilities.Peek) != (action.Peek != null)
            || definition.Kind == ToolKind.Panel && action.Active == null) throw new InvalidDataException("工具能力与处理接口不匹配。");
        actions.Add(id, action);
    }
    internal void Seal()
    {
        if (actions.Count != catalog.Items.Count) throw new InvalidOperationException("有工具尚未接入宿主。");
        sealedHost = true;
    }
    bool Ready(string id)
    {
        if (!sealedHost || stopped || !actions.ContainsKey(id) || running.Contains(id)) return false;
        try { return available(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { failed(id, ex); return false; }
    }
    internal bool Active(string id)
    {
        if (stopped || !actions.TryGetValue(id, out var action)) return false;
        try { return action.Active?.Invoke() == true; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { failed(id, ex); return false; }
    }
    internal async Task<bool> Invoke(string id)
    {
        if (!Ready(id)) return false;
        running.Add(id);
        pending.Add(id,new(TaskCreationOptions.RunContinuationsAsynchronously));
        try { await actions[id].Invoke(); return true; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { failed(id, ex); return false; }
        finally { Complete(id); }
    }
    internal bool Accepts(string id, T payload)
    {
        if (!Ready(id)) return false;
        try { return actions[id].Accepts?.Invoke(payload) == true; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { failed(id, ex); return false; }
    }
    internal async Task<bool> Drop(string id, T payload)
    {
        if (!Accepts(id, payload)) return false;
        running.Add(id);
        pending.Add(id,new(TaskCreationOptions.RunContinuationsAsynchronously));
        try { return await actions[id].Drop!(payload); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { failed(id, ex); return false; }
        finally { Complete(id); }
    }
    internal bool Peek(string id)
    {
        if (!Ready(id) || actions[id].Peek == null) return false;
        try { actions[id].Peek!(); return true; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { failed(id, ex); return false; }
    }
    internal void Stop() => stopped = true;
    void Complete(string id)
    {
        running.Remove(id); var completion=pending[id]; pending.Remove(id); completion.SetResult();
    }
    internal async Task WaitForIdle()
    { while(pending.Count>0) await Task.WhenAll(pending.Values.Select(p=>p.Task).ToArray()); }
}

internal sealed record PanelParticipant(string Id, Func<bool> Modal, Action<bool> Hide, Func<Task<bool>> Save, Action Release);
internal sealed class PanelLifecycle(Action<string, Exception> failed)
{
    readonly List<PanelParticipant> panels = [];
    bool released;
    internal void Register(PanelParticipant panel)
    {
        if (released || string.IsNullOrWhiteSpace(panel.Id) || panels.Any(p => p.Id == panel.Id)) throw new InvalidOperationException("面板生命周期重复注册。");
        panels.Add(panel);
    }
    internal bool Modal
    {
        get
        {
            if(released)return false;
            foreach (var panel in panels)
            {
                try { if (panel.Modal()) return true; }
                catch (Exception ex) when (ex is not OutOfMemoryException) { failed(panel.Id, ex); return true; }
            }
            return false;
        }
    }
    internal void Hide(bool includeDetached)
    {
        if (released) return;
        foreach (var panel in panels)
            try { panel.Hide(includeDetached); } catch (Exception ex) when (ex is not OutOfMemoryException) { failed(panel.Id, ex); }
    }
    internal async Task<bool> Save()
    {
        if (released) return false;
        foreach (var panel in panels)
            try { if (!await panel.Save()) return false; } catch (Exception ex) when (ex is not OutOfMemoryException) { failed(panel.Id, ex); return false; }
        return true;
    }
    internal void Release()
    {
        if (released) return;
        released = true;
        foreach (var panel in panels.AsEnumerable().Reverse())
            try { panel.Release(); } catch (Exception ex) when (ex is not OutOfMemoryException) { failed(panel.Id, ex); }
    }
}
