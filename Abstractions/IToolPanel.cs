using System.Windows.Input;

namespace GISEye.PredefinedUI.Abstractions;

/// <summary>
/// 自定义工具面板 VM 必须实现的最小契约。框架在 X 按钮触发时调用 <see cref="CloseCommand"/>。
/// </summary>
/// <remarks>
/// <para>现有 <c>ToolExecuteViewModel</c> / <c>ToolRunViewModel</c> 不需要实现此接口——它们的
/// closingAction 在 <c>ToolExplorerViewModel</c> 里硬编码调用 <c>CancelCommand</c> / <c>DetachCommand</c>，
/// 与本接口无冲突。<see cref="IToolPanel"/> 只约束自定义面板 VM。</para>
/// <para>推荐基类 <c>CustomPanelViewModelBase</c> 已实现该契约；新模板直接继承即可。</para>
/// </remarks>
public interface IToolPanel
{
    /// <summary>
    /// X 按钮命令。VM 自主决定语义：仅关闭 / 后台运行（Detach）/ 取消执行（Cancel）。
    /// 典型实现：若 <c>Session.IsRunning</c> → <c>Session.Detach()</c>；否则仅关闭。
    /// </summary>
    ICommand CloseCommand { get; }

    /// <summary>
    /// 面板请求关闭事件。宿主（独立窗口或弹窗）订阅此事件以触发自身关闭。
    /// 替代直接依赖导航服务，让面板 VM 与宿主实现完全解耦。
    /// </summary>
    event Action? CloseRequested;
}
