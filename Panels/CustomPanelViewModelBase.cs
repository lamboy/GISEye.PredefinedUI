using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GISEye.PredefinedUI.Abstractions;
using GISEye.Resources.Models;

namespace GISEye.PredefinedUI.Panels;

/// <summary>
/// 自定义面板 VM 基类：实现 <see cref="IToolPanel"/> 的 <c>CloseCommand</c> 契约，
/// 把 <c>Start</c> / <c>Close</c> / 状态代理到底层 <see cref="IPanelSession"/>。
/// 子类只需专注于 UX 设计（参数呈现方式、运行反馈形式、结果展示位置）。
/// </summary>
/// <remarks>
/// <para>UX 自由度（框架不约束）：</para>
/// <list type="bullet">
///   <item>参数呈现：<c>Session.Arguments</c> / <see cref="Inputs"/> + <see cref="Outputs"/>，或自定义布局</item>
///   <item>参数配置入口：内联字段 / "设置"按钮触发子对话框 / 选项卡切换 / 任意形式</item>
///   <item>运行反馈：进度条 + 状态文本 / 实时图表 / 流式日志 / 任意形式</item>
///   <item>结果展示：<see cref="Outputs"/> 内联呈现 / "查看结果"按钮 / 任意形式</item>
/// </list>
/// <para>本类不再继承 GISEye.ViewModelBase（独立项目无法依赖 GISEye 具体类型），
/// 直接继承 <see cref="ObservableObject"/> + 实现 <see cref="IPanelViewModelBase"/>；
/// <c>Header</c> 通过 IPanelViewModelBase 隐式不要求（标记接口契约无成员）。</para>
/// </remarks>
public abstract partial class CustomPanelViewModelBase : ObservableObject, IToolPanel, IPanelViewModelBase
{
    /// <summary>底层会话；面板 VM 通过它读写参数、订阅状态、触发执行。</summary>
    public IPanelSession Session { get; }

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _description;
    [ObservableProperty] private bool _isExecuting;
    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private string? _statusText;

    /// <summary>输入参数过滤（<c>!IsOutput</c>）。</summary>
    public IEnumerable<IPanelArgument> Inputs => Session.Arguments.Where(a => !a.IsOutput);

    /// <summary>输出参数过滤（<c>IsOutput == true</c>）。</summary>
    public IEnumerable<IPanelArgument> Outputs => Session.Arguments.Where(a => a.IsOutput);

    public bool IsRunning => Session.IsRunning;

    /// <summary>工具已完成且存在输出参数。</summary>
    public bool HasResult => Session.Status >= RuntimeStatus.Completed && Outputs.Any();

    protected CustomPanelViewModelBase(IPanelSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _displayName = session.ToolInfo.DisplayName;
        _description = session.ToolInfo.Description;

        // 监听 session 状态变化 → 同步到 VM（与 ToolRunViewModel.cs:51-73 同构）
        session.PropertyChanged += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(IPanelSession.Status):
                    case nameof(IPanelSession.StatusText):
                        StatusText = session.StatusText;
                        IsExecuting = session.IsRunning;
                        OnPropertyChanged(nameof(HasResult));
                        break;
                    case nameof(IPanelSession.ProgressPercentage):
                        ProgressPercentage = session.ProgressPercentage;
                        break;
                    case nameof(IPanelSession.IsRunning):
                        IsExecuting = session.IsRunning;
                        break;
                }
            });
        };
    }

    /// <summary>运行命令：触发 <c>Session.StartExecutionAsync()</c>——结果原地显示。</summary>
    [RelayCommand]
    private async Task Start()
    {
        _ = Session.StartExecutionAsync();
    }

    /// <summary>
    /// X / 关闭按钮命令（满足 <see cref="IToolPanel.CloseCommand"/> 契约）。
    /// 若 <see cref="Session.IsRunning"/> → <c>Session.Detach()</c>（后台继续）；然后触发 <see cref="CloseRequested"/>。
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        if (Session.IsRunning)
            Session.Detach();
        CloseRequested?.Invoke();
    }

    /// <inheritdoc />
    public event Action? CloseRequested;

    // 显式接口实现：源生成器生成的 CloseCommand 是 IAsyncRelayCommand，
    // IToolPanel 要求 ICommand；通过显式实现满足接口契约。
    ICommand IToolPanel.CloseCommand => CloseCommand;
}
