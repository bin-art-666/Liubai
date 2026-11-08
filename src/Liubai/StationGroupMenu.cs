using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

internal sealed partial class StationWindow
{
    GroupMenuSession? activeGroupMenu;
    int groupMenuGeneration;
    StationItem[] SelectedGroupItems() => CurrentItems().Where(i => selection.Contains(i.Id)).ToArray();
    StationItem[] GroupItems(StationItem item) => selection.Contains(item.Id) ? SelectedGroupItems() : [item];
    static Style GroupMenuStyle() => new(typeof(MenuFlyoutPresenter)) { Setters = { new Setter(FrameworkElement.MaxHeightProperty, 360d) } };

    void AttachGroupButton(Button button, Func<StationItem[]> targets)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = GroupMenuStyle() };
        var session = new GroupMenuSession(this, menu);
        button.Flyout = menu;
        menu.Opening += (_, _) => session.Populate(menu.Items, targets());
        bool overButton = false, overMenu = false, hoverOpen = false, suppressHover = false; int version = 0;
        async void CloseAfterLeave()
        {
            int current = ++version; await Task.Delay(450);
            if (current == version && hoverOpen && !overButton && !overMenu) menu.Hide();
        }
        button.PointerEntered += async (_, e) =>
        {
            if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
            overButton = true; if (suppressHover) return;
            int current = ++version, generation = groupMenuGeneration;
            await Task.Delay(250);
            if (current != version || generation != groupMenuGeneration || !IsOpen || destroyed || busy || ModalActive || DragActive || SearchActive || !button.IsLoaded || !button.IsEnabled || targets().Length == 0) return;
            hoverOpen = true; menu.ShowAt(button);
        };
        button.PointerExited += (_, _) => { overButton = false; suppressHover = false; CloseAfterLeave(); };
        button.Unloaded += (_, _) => { version++; overButton = false; menu.Hide(); };
        menu.Opened += (_, _) =>
        {
            // Native presenter supplies scrolling, keyboard navigation and edge placement.
            if (menu.Items.FirstOrDefault() is not DependencyObject first) return;
            for (var node = VisualTreeHelper.GetParent(first); node != null; node = VisualTreeHelper.GetParent(node))
                if (node is MenuFlyoutPresenter presenter)
                {
                    presenter.PointerEntered += Enter; presenter.PointerExited += Exit;
                    void Enter(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { overMenu = true; version++; }
                    void Exit(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
                    {
                        var p = e.GetCurrentPoint(presenter).Position;
                        if (p.X >= 0 && p.Y >= 0 && p.X < presenter.ActualWidth && p.Y < presenter.ActualHeight) return;
                        overMenu = false; CloseAfterLeave();
                    }
                    void Closed(object? sender, object e) { presenter.PointerEntered -= Enter; presenter.PointerExited -= Exit; menu.Closed -= Closed; }
                    menu.Closed += Closed; break;
                }
        };
        menu.Closed += (_, _) => { version++; suppressHover=overButton; hoverOpen = false; overMenu = false; };
    }
    void AddGroupSubmenu(MenuFlyout parent, Func<StationItem[]> targets)
    {
        var submenu = new MenuFlyoutSubItem { Text = "加入分组" };
        submenu.Resources[typeof(MenuFlyoutPresenter)] = GroupMenuStyle();
        var session = new GroupMenuSession(this, parent);
        parent.Items.Add(submenu);
        parent.Opening += (_, _) => session.Populate(submenu.Items, targets());
    }
    sealed class GroupMenuSession
    {
        readonly StationWindow owner; readonly MenuFlyout menu;
        IDisposable? lease;
        internal GroupMenuSession(StationWindow owner, MenuFlyout menu)
        {
            this.owner = owner; this.menu = menu;
            menu.Closed += (_, _) => Release();
        }
        void Release() { lease?.Dispose(); lease = null; if (owner.activeGroupMenu == this) owner.activeGroupMenu = null; }
        internal void Close() { menu.Hide(); Release(); }
        internal void Populate(IList<MenuFlyoutItemBase> entries, StationItem[] targets)
        {
            if (owner.activeGroupMenu != this) owner.activeGroupMenu?.Close();
            Release(); owner.activeGroupMenu = this; lease = owner.KeepOpen();
            entries.Clear(); var items = targets.DistinctBy(i => i.Id).ToArray();
            foreach (var group in App.WorkStore.Data.Scenes.OrderByDescending(s => s.Pinned))
            {
                int linked = items.Count(i => group.Items.Any(r => r.Kind == WorkKind.Station && r.Id == i.Id));
                string suffix = linked == 0 ? "" : linked == items.Length ? " · 已加入" : $" · 已有 {linked}/{items.Length} 项";
                var entry = new MenuFlyoutItem { Text = group.Name + suffix, Tag = group.Id, IsEnabled = items.Length > linked && !owner.store.ReadOnly && !App.WorkStore.ReadOnly };
                entry.Click += async (_, _) => { Close(); await owner.Run(() => owner.QuickJoinGroup(group.Id, items)); }; entries.Add(entry);
            }
            if (entries.Count == 0) entries.Add(new MenuFlyoutItem { Text = "暂无分组", IsEnabled = false });
            entries.Add(new MenuFlyoutSeparator());
            var create = new MenuFlyoutItem { Text = "＋ 新建分组并加入…", IsEnabled = items.Length > 0 && !owner.store.ReadOnly && !App.WorkStore.ReadOnly };
            create.Click += async (_, _) => { Close(); await owner.Run(() => owner.CreateGroupWithItems(items)); }; entries.Add(create);
        }
    }
    async Task QuickJoinGroup(Guid id, StationItem[] snapshot)
    {
        if (store.ReadOnly || App.WorkStore.ReadOnly) return;
        var group = App.WorkStore.Data.Scenes.FirstOrDefault(s => s.Id == id);
        if (group == null) { status.Text = "分组已移除，请重新选择"; return; }
        var ids = snapshot.Select(i => i.Id).ToHashSet();
        var items = store.Items.Where(i => ids.Contains(i.Id) && !group.Items.Any(r => r.Kind == WorkKind.Station && r.Id == i.Id)).ToArray();
        if (items.Length == 0) { status.Text = "所选项目已加入，或记录已移除"; return; }
        double offset = browserScroll.VerticalOffset;
        await Remember($"已将 {items.Length} 项加入{group.Name}", () => App.Work.Store.Link(id, items.Select(i => new WorkRef(WorkKind.Station, i.Id))));
        status.Text = $"已将 {items.Length} 项加入{group.Name}";
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { if (IsOpen && !SearchActive) browserScroll.ChangeView(null, offset, null, true); });
    }
    async Task CreateGroupWithItems(StationItem[] items)
    {
        var name = new TextBox { PlaceholderText = "例如：论文修改", MaxLength = 80 };
        var dialog = new PanelEntryDialog(root.XamlRoot, "新建分组并加入", "创建并加入", name, [name], () => WorkspaceNameError(name.Text), async () =>
        {
            await Remember("已新建分组并加入所选项目", async () =>
            {
                Guid id = await App.Work.Store.Create(name.Text, false);
                var ids = items.Select(i => i.Id).ToHashSet();
                await App.Work.Store.Link(id, store.Items.Where(i => ids.Contains(i.Id)).Select(i => new WorkRef(WorkKind.Station, i.Id)));
            });
            status.Text = "已创建分组并加入所选项目";
        });
        await dialog.Show();
    }
}
