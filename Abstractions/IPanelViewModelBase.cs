using System.ComponentModel;

namespace GISEye.PredefinedUI.Abstractions;

/// <summary>
/// 标记接口：可被 <c>ViewLocator</c> 解析到对应 View 的面板 VM。GISEye 内置 VM 与
/// PredefinedUI 中的面板 VM 都实现此接口，使两套 VM 可共用一套 ViewModel → View 映射机制。
/// </summary>
/// <remarks>
/// 命名空间：<c>GISEye.PredefinedUI.Abstractions</c>。
/// 由 GISEye.<see cref="GISEye.ViewLocator"/> 通过 <c>is IPanelViewModelBase</c> 判定；
/// <see cref="GISEye.Helpers.DIExtensions.ViewModelAndViewTypeMapping"/> 维护 VM → View 类型映射。
/// </remarks>
public interface IPanelViewModelBase : INotifyPropertyChanged
{
}
