using System.ComponentModel;
using GISEye.Core;
using GISEye.Resources.Models;

namespace GISEye.PredefinedUI.Abstractions;

/// <summary>
/// <see cref="GISEye.Models.ToolExecutionSession"/> 对面板 VM 暴露的最小面。
/// PredefinedUI 中的面板基类通过此接口访问会话状态，避免直接依赖 GISEye 具体类型。
/// </summary>
/// <remarks>
/// <see cref="Status"/> 是 protobuf 生成的 <see cref="GISEye.Resources.Models.RuntimeStatus"/> 枚举；
/// PredefinedUI 已引用 GISEye.Resources，可直接做枚举比较。
/// </remarks>
public interface IPanelSession : INotifyPropertyChanged
{
    /// <summary>当前会话的所有参数（含输入与输出）。</summary>
    IReadOnlyList<IPanelArgument> Arguments { get; }

    /// <summary>所属工具元信息（用于 DisplayName / Description / Extensions）。</summary>
    IPanelItem ToolInfo { get; }

    /// <summary>当前运行状态。</summary>
    RuntimeStatus Status { get; }

    /// <summary>状态描述文本。</summary>
    string? StatusText { get; }

    /// <summary>进度百分比（0-100，负数表示无限进度）。</summary>
    double ProgressPercentage { get; }

    /// <summary>工具是否正在运行。</summary>
    bool IsRunning { get; }

    /// <summary>是否正在向服务端发送参数验证请求。</summary>
    bool IsValidatingArguments { get; }

    /// <summary>异步启动工具执行。</summary>
    Task StartExecutionAsync();

    /// <summary>分离 UI 回调，后台继续运行。</summary>
    void Detach();

    /// <summary>取消正在进行的执行。</summary>
    void Cancel();

    /// <summary>执行完成事件（无论成功、取消或失败都会触发）。</summary>
    event Action? OnSessionCompleted;
}
