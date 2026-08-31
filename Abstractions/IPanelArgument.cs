using System.ComponentModel;
using GISEye.Core;

namespace GISEye.PredefinedUI.Abstractions;

/// <summary>
/// <see cref="GISEye.Models.ToolArgument"/> 对面板 VM 暴露的最小面。
/// 通过此接口，面板可按名查找参数、读写值、订阅变化与校验错误事件。
/// </summary>
/// <remarks>
/// 写值时通过 <see cref="Value"/> setter；具体类型由运行时实际 <see cref="IValueType"/> 实现决定
/// （VTInt / VTDouble / VTString）。面板代码常用模式匹配：
/// <c>_arg?.Value is VTDouble d ? d.Value : 0.0</c>，运行时仍能匹配到具体类型。
/// </remarks>
public interface IPanelArgument : INotifyPropertyChanged
{
    /// <summary>参数名（与工具元数据声明一致，用于 FindArg）。</summary>
    string Name { get; }

    /// <summary>是否为输出参数。</summary>
    bool IsOutput { get; }

    /// <summary>参数当前值；面板读写此属性触发值变化与重算。</summary>
    IValueType? Value { get; }

    /// <summary>参数校验错误变更事件（与 INotifyDataErrorInfo.ErrorsChanged 同形）。</summary>
    event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
}
