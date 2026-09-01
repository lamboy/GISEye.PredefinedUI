using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GISEye.PredefinedUI.Abstractions;

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
public abstract partial class CustomPanelViewModelBase : ObservableObject, IToolPanel, IPanelViewModelBase, INotifyDataErrorInfo
{
    /// <summary>底层会话；面板 VM 通过它读写参数、订阅状态、触发执行。</summary>
    public IPanelSession Session { get; }

    /// <summary>活跃的参数绑定实例（生命周期与本 VM 一致，仅用于保持引用）。</summary>
    private readonly List<object> _bindings = new();

    /// <summary>绑定层上报的属性错误（转换失败 / 验证特性 / 参数服务端校验）。</summary>
    private readonly Dictionary<string, List<string>> _bindingErrors = new();

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
    public bool HasResult => Session.Status >= PanelSessionStatus.Completed && Outputs.Any();

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

    // ---- 参数绑定（BindArgument）----

    /// <summary>
    /// 将 <see cref="Session"/> 中名为 <paramref name="argumentName"/> 的参数绑定到本 VM 的属性。
    /// 输入参数双向同步（VM 写入自动触发会话防抖验证）；输出参数单向（参数 → VM）。
    /// 类型转换失败、验证特性、参数服务端校验错误统一经
    /// <see cref="INotifyDataErrorInfo"/> 反馈给绑定控件（DataValidationErrors 自动装饰）。
    /// </summary>
    /// <typeparam name="T">绑定属性的类型（如 double / int / string）。</typeparam>
    /// <param name="argumentName">参数名（与工具元数据声明一致）。</param>
    /// <param name="property">绑定属性访问表达式，如 <c>() => Principal</c>；属性名从中提取。</param>
    /// <param name="setter">属性赋值委托，如 <c>v => Principal = v</c>（必须走生成的 setter 以触发变更通知）。</param>
    /// <param name="validationAttributes">
    /// 输入验证特性（如 <c>new RangeAttribute(1, 100)</c>）。
    /// 注：MVVM Toolkit 要求 ObservableValidator 才允许在 [ObservableProperty] 字段上挂验证特性，
    /// 而基类为合并绑定外部错误使用手写 INotifyDataErrorInfo，故验证特性经此参数传入。
    /// </param>
    protected void BindArgument<T>(string argumentName, Expression<Func<T>> property, Action<T> setter,
        params ValidationAttribute[] validationAttributes)
    {
        var argument = Session.Arguments.FirstOrDefault(a => a.Name == argumentName)
            ?? throw new InvalidOperationException($"参数 '{argumentName}' 在会话参数列表中不存在。");
        var propertyInfo = (property.Body as MemberExpression)?.Member as PropertyInfo
            ?? throw new ArgumentException("绑定目标必须是属性访问表达式，如 () => Principal。", nameof(property));

        // 反射取值（避免 Expression.Compile 在 NativeAOT 下依赖动态代码）
        Func<T> getter = () => (T)propertyInfo.GetValue(this)!;
        var attributes = CollectValidationAttributes(propertyInfo).Concat(validationAttributes).ToList();
        var binding = new PanelArgumentBinding<T>(
            this, argument, propertyInfo.Name, getter, setter, attributes);
        _bindings.Add(binding);
        binding.Attach();
    }

    /// <summary>
    /// 输入绑定成功把 VM 属性写入参数后回调。默认空实现；
    /// 子类可重写以触发自动运行（如 <c>_ = RunAsync();</c>）。
    /// </summary>
    protected virtual void OnBoundInputChanged(string argumentName) { }

    /// <summary>供绑定层调用：转发到 <see cref="OnBoundInputChanged"/>。</summary>
    internal void NotifyBoundInputChanged(string argumentName) => OnBoundInputChanged(argumentName);

    /// <summary>供绑定层调用：更新某属性的合并错误列表并触发 <see cref="ErrorsChanged"/>。</summary>
    internal void SetBindingErrors(string propertyName, IReadOnlyList<string> errors)
    {
        bool changed;
        if (errors.Count == 0)
        {
            changed = _bindingErrors.Remove(propertyName);
        }
        else
        {
            changed = !_bindingErrors.TryGetValue(propertyName, out var existing)
                || !existing.SequenceEqual(errors);
            if (changed)
                _bindingErrors[propertyName] = new List<string>(errors);
        }

        if (changed)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    /// <summary>
    /// 收集属性上的验证特性（若属性上确有 <see cref="ValidationAttribute"/> 则读取，
    /// 并回退检查 <c>_camelCase</c> 后备字段）。常规路径是 BindArgument 的显式参数。
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "面板 VM 的后备字段由源生成器产生，不会被裁剪；读取验证特性仅需元数据。")]
    private IReadOnlyList<ValidationAttribute> CollectValidationAttributes(PropertyInfo propertyInfo)
    {
        var attributes = propertyInfo.GetCustomAttributes<ValidationAttribute>(true).ToList();
        if (attributes.Count == 0)
        {
            string fieldName = "_" + char.ToLowerInvariant(propertyInfo.Name[0]) + propertyInfo.Name[1..];
            var field = GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                attributes.AddRange(field.GetCustomAttributes<ValidationAttribute>(true));
        }
        return attributes;
    }

    // ---- INotifyDataErrorInfo ----

    /// <inheritdoc />
    public bool HasErrors => _bindingErrors.Count > 0;

    /// <inheritdoc />
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <inheritdoc />
    public IEnumerable GetErrors(string? propertyName) =>
        propertyName != null && _bindingErrors.TryGetValue(propertyName, out var errors)
            ? errors
            : Enumerable.Empty<string>();

    // 显式接口实现：源生成器生成的 CloseCommand 是 IAsyncRelayCommand，
    // IToolPanel 要求 ICommand；通过显式实现满足接口契约。
    ICommand IToolPanel.CloseCommand => CloseCommand;
}
