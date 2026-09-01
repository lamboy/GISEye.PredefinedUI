using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using GISEye.Core;
using GISEye.PredefinedUI.Abstractions;
using GISEye.ValueTypes;

namespace GISEye.PredefinedUI.Panels;

/// <summary>
/// <see cref="IPanelSession"/> 参数 ↔ 面板 VM 属性的绑定实例。
/// 输入参数双向同步（VM → 参数走 <see cref="VTBase.TryFromString"/> 规范路径，
/// 自动触发会话的防抖服务端验证）；输出参数单向 arg → VM。
/// </summary>
/// <remarks>
/// 错误合并策略（统一经 <see cref="CustomPanelViewModelBase"/> 的
/// <see cref="INotifyDataErrorInfo"/> 反馈给绑定控件）：
/// <list type="bullet">
///   <item>类型转换失败（VM 值无法写入参数，或参数值无法读回 VM 属性类型）</item>
///   <item>绑定属性上的验证特性（<see cref="ValidationAttribute"/>，仅输入参数）</item>
///   <item>参数自身的校验错误（客户端必填 + 服务端返回，经 <see cref="INotifyDataErrorInfo"/> 读取）</item>
/// </list>
/// 生命周期与所属 VM 一致，不需要显式释放。
/// </remarks>
internal sealed class PanelArgumentBinding<T>
{
    private readonly CustomPanelViewModelBase _owner;
    private readonly IPanelArgument _argument;
    private readonly string _propertyName;
    private readonly Func<T> _getter;
    private readonly Action<T> _setter;
    private readonly IReadOnlyList<ValidationAttribute> _validationAttributes;

    /// <summary>双向同步回环抑制标志（推送期间忽略对端事件）。</summary>
    private bool _syncing;

    /// <summary>最近一次转换错误（转换成功后清除）。</summary>
    private string? _conversionError;

    public PanelArgumentBinding(
        CustomPanelViewModelBase owner,
        IPanelArgument argument,
        string propertyName,
        Func<T> getter,
        Action<T> setter,
        IReadOnlyList<ValidationAttribute> validationAttributes)
    {
        _owner = owner;
        _argument = argument;
        _propertyName = propertyName;
        _getter = getter;
        _setter = setter;
        _validationAttributes = validationAttributes;
    }

    /// <summary>挂接事件订阅并做初始拉取（覆盖 reattach 场景：会话可能已有值/结果）。</summary>
    public void Attach()
    {
        _owner.PropertyChanged += OnOwnerPropertyChanged;
        _argument.PropertyChanged += OnArgumentPropertyChanged;
        _argument.ErrorsChanged += OnArgumentErrorsChanged;

        PullFromArgument();
        RefreshErrors();
    }

    // ---- VM → 参数（仅输入参数）----

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncing || _argument.IsOutput || e.PropertyName != _propertyName)
            return;
        PushToArgument();
    }

    private void PushToArgument()
    {
        T value = _getter();
        _syncing = true;
        try
        {
            string? text = Convert.ToString(value, CultureInfo.CurrentCulture);
            if (_argument.Value is VTBase vt && vt.ToString() != text)
            {
                // 与 ToolArgument.Value setter 同语义：ToString → TryFromString
                _conversionError = vt.TryFromString(text ?? "")
                    ? null
                    : $"无法将 \"{text}\" 转换为参数「{_argument.Name}」的值类型";
            }
        }
        finally
        {
            _syncing = false;
        }

        RefreshErrors();
        // 写入成功后通知宿主（默认用于触发自动运行）
        if (_conversionError == null)
            _owner.NotifyBoundInputChanged(_argument.Name);
    }

    // ---- 参数 → VM（输入与输出均适用）----

    private void OnArgumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncing || e.PropertyName != "Value")
            return;
        RunOnUi(PullFromArgument);
    }

    private void PullFromArgument()
    {
        IValueType? vt = _argument.Value;
        if (vt == null)
            return;

        if (!TryConvertFrom(vt, out T? value))
        {
            _conversionError = $"参数「{_argument.Name}」的值 \"{vt}\" 无法转换为属性 {_propertyName}（{typeof(T).Name}）";
            RefreshErrors();
            return;
        }

        _conversionError = null;
        _syncing = true;
        try
        {
            _setter(value!);
        }
        finally
        {
            _syncing = false;
        }
        RefreshErrors();
    }

    /// <summary>IValueType → T：优先精确类型匹配，回退字符串解析。</summary>
    private static bool TryConvertFrom(IValueType vt, out T? value)
    {
        value = default;
        switch (vt)
        {
            case VTDouble d when typeof(T) == typeof(double):
                value = (T)(object)d.Value;
                return true;
            case VTInt i when typeof(T) == typeof(int):
                value = (T)(object)i.Value;
                return true;
            case VTString s when typeof(T) == typeof(string):
                value = (T)(object)s.Value;
                return true;
            case VTBase b when typeof(T) == typeof(string):
                value = (T)(object)(b.ToString() ?? "");
                return true;
            case VTBase b:
                try
                {
                    value = (T)Convert.ChangeType(b.ToString(), typeof(T), CultureInfo.CurrentCulture);
                    return true;
                }
                catch
                {
                    return false;
                }
            default:
                return false;
        }
    }

    // ---- 错误合并 ----

    private void OnArgumentErrorsChanged(object? sender, DataErrorsChangedEventArgs e) =>
        RunOnUi(RefreshErrors);

    private void RefreshErrors()
    {
        var errors = new List<string>();

        if (_conversionError != null)
            errors.Add(_conversionError);

        // 绑定属性上的验证特性（仅对输入参数执行）
        if (!_argument.IsOutput && _validationAttributes.Count > 0)
        {
            var context = new ValidationContext(_owner) { MemberName = _propertyName };
            foreach (var attribute in _validationAttributes)
            {
                try
                {
                    attribute.Validate(_getter(), context);
                }
                catch (ValidationException ex)
                {
                    errors.Add(ex.Message);
                }
            }
        }

        // 参数自身错误（客户端必填 + 服务端校验），映射到绑定属性
        if (_argument is INotifyDataErrorInfo notifyErrors)
        {
            foreach (var error in notifyErrors.GetErrors(null))
            {
                string? message = error is ValidationResult result ? result.ErrorMessage : error?.ToString();
                if (!string.IsNullOrEmpty(message) && !errors.Contains(message))
                    errors.Add(message);
            }
        }

        _owner.SetBindingErrors(_propertyName, errors);
    }

    /// <summary>参数端事件可能来自非 UI 线程（服务端回写），统一回到 UI 线程。</summary>
    private static void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
