# 输入弹窗键盘规则

中转站新建／编辑分组、加入分组、新建待办／笔记及文字／显示名称编辑共用 `PanelEntryDialog`。

- 打开时聚焦首个输入框；单行输入采用原生默认主按钮响应 Enter，Esc 使用系统取消行为。
- 多行输入保留 Enter 换行，Ctrl+Enter 提交。混合名称和说明的弹窗中，名称可 Enter 保存，说明可正常换行。
- 空白名称禁用提交，同名分组（去掉两端空白、不区分大小写、编辑时排除自身）在输入框下提示并保留内容。
- 监听各输入框的 IME composition；组合期间及结束按键尚未释放时不允许提交。释放后短定时器停止，不常驻轮询。
- 保存期间禁止再次提交和关闭，输入暂时只读；保存成功才收起，失败保留弹窗及内容供重试。

原生 workbench 隔离检查覆盖实际创建弹窗、焦点、默认按钮、空白／重复名称、主按钮创建、取消无记录、多行内容、组合状态门禁和并发提交去重。组合门禁检查模拟组合生命周期，并不替代不同输入法的实际使用验证。

API 依据：[ContentDialog 默认按钮](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.contentdialog.defaultbutton)、[TextBox composition 结束事件](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.textbox.textcompositionended)。
