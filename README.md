# 留白 · 核心版

Windows 11 原生 WinUI 3 顶部待办栏，沿用 LumaBar 的青绿色与统一圆角样式。

## 保留的功能

- 默认常驻；在设置中选择「常驻」或「自动隐藏」，选择会保存到本机。
- 常驻时通过 Windows AppBar 预留顶部工作区，让桌面布局和最大化窗口避开顶栏；自动隐藏或正常退出时释放预留空间。
- 自动隐藏模式下，鼠标触碰主屏最上边时显示，移出后立即隐藏；设置打开期间保持显示，避免调整时消失。
- 右侧方框勾选图标打开待办，显示未完成数量。
- 输入后按 Enter 或点击「＋」添加；支持完成、编辑和删除。
- 待办自动保存至 `%LOCALAPPDATA%\Liubai\todos.json`，保留 `.bak` 备份，重启后继续使用。
- 最右侧齿轮打开轻量设置：显示方式、开机启动、数据文件夹、退出。

已移除独立设置大窗口、主题与材质选择、外观预览及设置草稿流程。应用配色跟随 Windows。不集成系统底部任务栏控制、自动明暗、多屏规则或配置导入。之前的迁移文档仅作历史参考。

## 运行与构建

双击 `dist/Liubai.exe`。移动或分发时保留整个 `dist` 文件夹。开机启动默认关闭，开启后请保持程序路径不变。

构建需要 Windows 11 x64 和 .NET 10 SDK。可使用已安装的 `dotnet`，或将便携 SDK 放入 `.tools/dotnet`。运行库采用 Windows App SDK 1.8 兼容分支，首次构建会自动还原 NuGet 依赖。

```powershell
# 退出留白后构建，旧发布目录会备份到 .build，使用全新目录发布。
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

# 核心行为检查
dotnet run --project .\tests\Liubai.Core.Tests\Liubai.Core.Tests.csproj
```

`--inspect` 固定显示界面供检查；`--show-settings` 在检查模式下直接打开轻量设置；`--data-dir <目录>` 使用独立测试数据。

`--inspect --verify-reservation --data-dir <独立测试目录>` 可验证常驻预留、自动隐藏释放及退出清理，结果写入该目录的 `appbar-check.json`。

边界避让参考 MyDockFinder 的公开「窗口边界」设置；实现采用 [Windows AppBar 官方接口](https://learn.microsoft.com/windows/win32/shell/application-desktop-toolbars)，不直接改写桌面图标位置。
