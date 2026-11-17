using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using GISEye.Core;
using GISEye.PredefinedUI.Abstractions;

namespace GISEye.PredefinedUI.Panels;

/// <summary>
/// 一条静态演示数据规则：描述“输入满足什么状态时，写入哪些值”。
/// 本身不含算法，只做静态映射，配合 <see cref="PanelDemoSession"/> 的静态用例模式使用。
/// </summary>
/// <remarks>
/// <para><see cref="When"/> 按参数当前值的文本比较（数值用不变文化格式），空表示不限制。</para>
/// <para><see cref="Changed"/> 用于模拟“参数联动”：只有列出的输入在本次运行中发生变化时才应用。</para>
/// <para><see cref="Inputs"/> 在 UpdateArguments 阶段写入（联动），<see cref="Outputs"/> 在 Execute 阶段写入（结果）。</para>
/// </remarks>
public sealed class PanelDemoCase
{
    /// <summary>状态条件：参数名 → 期望值；全部匹配才应用。</summary>
    public Dictionary<string, object> When { get; init; } = new();

    /// <summary>可选：这些输入中至少一个在本次运行中发生变化才应用（模拟参数联动）。</summary>
    public IReadOnlyList<string>? Changed { get; init; }

    /// <summary>模拟 UpdateArguments：联动写入输入参数。</summary>
    public Dictionary<string, object> Inputs { get; init; } = new();

    /// <summary>模拟 Execute：写入输出参数。</summary>
    public Dictionary<string, object> Outputs { get; init; } = new();

    /// <summary>可选说明；为空时根据条件与动作自动生成。</summary>
    public string? Description { get; init; }
}

/// <summary>
/// 一个演示来源（模式）：显示名称 + 会话工厂。
/// 用于在 Demo 宿主中并列展示“静态用例数据 / 真实算法”等不同实现。
/// </summary>
/// <param name="Name">显示在 Demo 宿主切换器中的名称。</param>
/// <param name="CreateSession">创建该来源的演示会话；每次调用应返回新实例。</param>
public sealed record PanelDemoProvider(string Name, Func<IPanelSession> CreateSession);

/// <summary>
/// 自定义面板的演示数据会话，支持两种模式（可单独或混合使用）：
/// <list type="number">
///   <item><b>静态用例数据</b>：只声明 <see cref="PanelDemoCase"/> 与输入/输出样例值，不写算法，按输入匹配切换；</item>
///   <item><b>真实算法</b>：继承本类并重写 <see cref="UpdateArguments"/>（参数联动，对应 <c>ITool.UpdateArguments</c>）
///         与 <see cref="Execute"/>（算法执行，对应 <c>ITool.Execute</c>）。</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>模式一：静态用例数据（无算法）</b>——与面板 VM 同目录，一个面板一个文件：</para>
/// <code><![CDATA[
/// internal static class XxxPanelDemo
/// {
///     public static IPanelSession CreateSession()
///     {
///         var session = new PanelDemoSession("显示名", description: null, extensions: hints, cases: Cases);
///         session.AddInput("Amount", new VTDouble { Value = 100 });
///         session.AddInput("Tax", new VTDouble());
///         session.AddOutput("Result", new VTDouble());
///         return session;
///     }
///
///     private static readonly PanelDemoCase[] Cases =
///     {
///         new()
///         {
///             When = { ["Amount"] = 200 },        // 触发条件（输入匹配）
///             Inputs = { ["Tax"] = 0 },           // 模拟 UpdateArguments：联动其他输入
///             Outputs = { ["Result"] = 400 },     // 模拟 Execute：写入结果
///         },
///     };
/// }
/// ]]></code>
///
/// <para><b>模式二：真实算法 + 参数联动</b>——继承本类重写两个阶段，调用顺序与 <c>ITool</c> 一致：</para>
/// <code><![CDATA[
/// internal sealed class XxxPanelDemo : PanelDemoSession
/// {
///     public XxxPanelDemo() : base("显示名", description: null, extensions: hints)
///     {
///         AddInput("Principal", new VTDouble { Value = 100_000.0 });
///         AddInput("Months", new VTInt { Value = 240 });
///         AddOutput("OutPayment", new VTDouble());
///         AddOutput("OutTotal", new VTDouble());
///     }
///
///     // 对应 ITool.UpdateArguments：参数联动（可修改任意输入参数）
///     protected override void UpdateArguments()
///     {
///         int months = Arg<VTInt>("Months").Value;
///         if (months % 12 != 0)
///             SetArgument("Months", (int)Math.Ceiling(months / 12.0) * 12);
///     }
///
///     // 对应 ITool.Execute：真实算法（读取输入 → 写入输出）
///     protected override void Execute()
///     {
///         double principal = Arg<VTDouble>("Principal").Value;
///         int months = Arg<VTInt>("Months").Value;
///         double payment = Math.Round(principal / months, 2);
///
///         Arg<VTDouble>("OutPayment").Value = payment;
///         Arg<VTDouble>("OutTotal").Value = payment * months;
///         ExecutionSummary = $"等额本金：{months} 期";
///     }
/// }
/// ]]></code>
///
/// <para>每次输入变化、自动轮换或手动切换用例时，框架都按 <c>UpdateArguments → Execute</c> 顺序执行。
/// 真实算法模式下仍可传 <c>cases</c>：切换用例会先把 <see cref="PanelDemoCase.When"/> 的输入条件写回面板，
/// 再由真实联动/算法计算输出。重写两个阶段后若仍想保留静态规则，可在重写方法中调用 <c>base</c>。</para>
///
/// <para>注册面板时把会话工厂交给 <c>PanelRegistration.RegisterProvider</c> 的 <c>demoSessionFactory</c>，
/// Demo 宿主即可经 <c>PanelRegistration.CreateDemoSession</c> 取到；<c>description</c> 传 null 时由 <c>cases</c> 自动生成说明。</para>
/// </remarks>
public class PanelDemoSession : IPanelSession
{
    private readonly List<IPanelArgument> _arguments = new();
    private readonly Dictionary<string, DemoArgument> _argumentsByName = new();
    private readonly Dictionary<string, string> _lastInputValues = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<PanelDemoCase> _cases;
    private readonly TimeSpan? _autoCycleInterval;

    private IReadOnlySet<string> _changedInputs = new HashSet<string>();

    private Timer? _cycleTimer;
    private int _cycleIndex = -1;
    private bool _autoCycleStarted;

    private PanelSessionStatus _status = PanelSessionStatus.Pending;
    private string? _statusText = "就绪";
    private double _progressPercentage;
    private bool _isRunning;

    public PanelDemoSession(
        string displayName,
        string? description = null,
        IReadOnlyDictionary<string, string>? extensions = null,
        IReadOnlyList<PanelDemoCase>? cases = null,
        TimeSpan? autoCycleInterval = null)
    {
        _cases = cases ?? Array.Empty<PanelDemoCase>();
        _autoCycleInterval = autoCycleInterval;

        string toolDescription = description ?? BuildToolDescription(_cases);
        if (autoCycleInterval is { } interval)
            toolDescription += $"（每 {interval.TotalSeconds:0.#} 秒自动切换）";

        ToolInfo = new DemoToolItem(
            displayName,
            toolDescription,
            extensions ?? new Dictionary<string, string>());
    }

    public IReadOnlyList<IPanelArgument> Arguments => _arguments;

    public IPanelItem ToolInfo { get; }

    public PanelSessionStatus Status
    {
        get => _status;
        private set => Set(ref _status, value, nameof(Status));
    }

    public string? StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value, nameof(StatusText));
    }

    public double ProgressPercentage
    {
        get => _progressPercentage;
        private set => Set(ref _progressPercentage, value, nameof(ProgressPercentage));
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => Set(ref _isRunning, value, nameof(IsRunning));
    }

    public bool IsValidatingArguments => false;

    /// <summary>演示用例总数。</summary>
    public int DemoCaseCount => _cases.Count;

    /// <summary>当前用例索引（0 基；-1 表示尚未匹配到用例）。</summary>
    public int CurrentDemoCaseIndex => _cycleIndex;

    /// <summary>是否正在按间隔自动切换。</summary>
    public bool IsAutoCycleRunning => _cycleTimer != null;

    /// <summary>自动切换间隔；未配置 → null。</summary>
    public TimeSpan? AutoCycleInterval => _autoCycleInterval;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? OnSessionCompleted;

    /// <summary>声明一个输入参数（<c>IsOutput = false</c>）。</summary>
    public void AddInput(string name, IValueType value)
    {
        AddArgument(name, value, isOutput: false);
        _lastInputValues[name] = ValueText(value);
    }

    /// <summary>声明一个输出参数（<c>IsOutput = true</c>），初值可留空，由匹配到的规则写入。</summary>
    public void AddOutput(string name, IValueType value) => AddArgument(name, value, isOutput: true);

    /// <summary>
    /// 模拟一次执行：等待片刻后按当前输入匹配演示规则，写入联动输入与输出值。
    /// 没有任何规则匹配时保持当前值，并在状态文本中提示。
    /// </summary>
    public async Task StartExecutionAsync()
    {
        if (IsRunning)
            return;

        IsRunning = true;
        Status = PanelSessionStatus.Executing;
        StatusText = "正在执行演示数据…";
        ProgressPercentage = -1;

        try
        {
            await Task.Delay(120);
            if (Status == PanelSessionStatus.Cancelled)
                return;

            ExecutionSummary = null;
            RunExecutionPhases();
            Status = PanelSessionStatus.Completed;
            StatusText = ExecutionSummary ?? "演示执行完成";
            ProgressPercentage = 100;
            StartAutoCycleIfNeeded();
        }
        catch (Exception ex)
        {
            Status = PanelSessionStatus.Failed;
            StatusText = $"演示数据错误：{ex.Message}";
            ProgressPercentage = 0;
        }
        finally
        {
            RememberInputValues();
            IsRunning = false;
            OnSessionCompleted?.Invoke();
        }
    }

    /// <summary>
    /// 参数联动阶段，对应 <c>ITool.UpdateArguments</c>：每次执行前先调用，子类可修改输入参数。
    /// 默认应用所有匹配 <see cref="PanelDemoCase"/> 的 <see cref="PanelDemoCase.Inputs"/>。
    /// </summary>
    protected virtual void UpdateArguments() => ApplyCases(applyInputs: true);

    /// <summary>
    /// 算法执行阶段，对应 <c>ITool.Execute</c>：读取输入并写入输出。
    /// 默认应用所有匹配 <see cref="PanelDemoCase"/> 的 <see cref="PanelDemoCase.Outputs"/>；
    /// 子类可重写为真实算法，通过 <see cref="Arg{T}"/> / <see cref="SetArgument"/> 读写参数。
    /// </summary>
    protected virtual void Execute()
    {
        IReadOnlyList<string> matched = ApplyCases(applyInputs: false);

        if (matched.Count > 0)
            ExecutionSummary = "演示：" + string.Join("；", matched);
        else if (ExecutionSummary == null)
            ExecutionSummary = "无匹配演示数据（保持当前值）";
    }

    /// <summary>先联动（UpdateArguments）再执行（Execute），与 ITool 的调用顺序一致。</summary>
    private void RunExecutionPhases()
    {
        _changedInputs = GetChangedInputs();
        UpdateArguments();
        Execute();
        SyncCurrentCaseIndex();
    }

    /// <summary>真实算法模式下也保持工具条“当前用例”与输入状态一致。</summary>
    private void SyncCurrentCaseIndex()
    {
        if (_cases.Count == 0)
            return;

        int previousIndex = _cycleIndex;
        for (int i = 0; i < _cases.Count; i++)
        {
            if (MatchesState(_cases[i]))
                _cycleIndex = i;
        }

        if (_cycleIndex != previousIndex)
            OnPropertyChanged(nameof(CurrentDemoCaseIndex));
    }

    /// <summary>最近一次执行说明；默认为静态规则摘要，子类可覆盖。</summary>
    protected string? ExecutionSummary { get; set; }

    /// <summary>按名取参数值对象，直接读写 <c>Value</c>（写入会通知面板绑定层）。</summary>
    protected T Arg<T>(string name) where T : class, IValueType
    {
        DemoArgument argument = GetArgument(name);
        return argument.Value is T value
            ? value
            : throw new InvalidOperationException(
                $"演示参数“{name}”的类型不是 {typeof(T).Name}。");
    }

    /// <summary>按名写入参数值（按参数声明的类型解析，可用于模拟参数联动）。</summary>
    protected void SetArgument(string name, object value) => ApplyValue(GetArgument(name), value);

    /// <summary>分离 UI 时停止自动切换，避免后台继续改动演示值。</summary>
    public void Detach() => StopAutoCycle();

    public void Cancel()
    {
        StopAutoCycle();
        IsRunning = false;
        Status = PanelSessionStatus.Cancelled;
        StatusText = "已取消";
        ProgressPercentage = 0;
    }

    /// <summary>立即切换到下一组演示数据（循环）。</summary>
    public void ShowNextDemoCase() => ShowDemoCaseRelative(1);

    /// <summary>立即切换到上一组演示数据（循环）。</summary>
    public void ShowPreviousDemoCase() => ShowDemoCaseRelative(-1);

    /// <summary>立即切换到第一组演示数据。</summary>
    public void ShowFirstDemoCase() => ShowDemoCaseAt(0);

    private void ShowDemoCaseRelative(int step)
    {
        if (_cases.Count == 0)
            return;

        int index = _cycleIndex < 0
            ? (step < 0 ? _cases.Count - 1 : 0)
            : (_cycleIndex + step + _cases.Count) % _cases.Count;

        ShowDemoCaseAt(index);
    }

    /// <summary>
    /// 应用指定演示用例：先写入 <see cref="PanelDemoCase.When"/> 中的输入条件（保证界面与结果一致），
    /// 再应用 <see cref="PanelDemoCase.Inputs"/> / <see cref="PanelDemoCase.Outputs"/>。
    /// </summary>
    private void ShowDemoCaseAt(int index)
    {
        if (IsRunning || _cases.Count == 0 || Status == PanelSessionStatus.Cancelled)
            return;

        bool indexChanged = _cycleIndex != index;
        _cycleIndex = index;
        PanelDemoCase demoCase = _cases[index];

        IsRunning = true;
        try
        {
            // 把该组的输入条件写回面板；绑定层的自动运行会因 IsRunning 而被忽略。
            foreach (var (name, value) in demoCase.When)
            {
                DemoArgument argument = GetArgument(name);
                if (!argument.IsOutput)
                    ApplyValue(argument, value);
            }

            // 先写入 When 中的输入条件，再按 ITool 的顺序联动 + 执行。
            ExecutionSummary = null;
            RunExecutionPhases();

            Status = PanelSessionStatus.Completed;
            StatusText = $"演示 {_cycleIndex + 1}/{_cases.Count}：{Describe(demoCase)}";
            ProgressPercentage = 100;
        }
        catch (Exception ex)
        {
            Status = PanelSessionStatus.Failed;
            StatusText = $"演示数据错误：{ex.Message}";
            ProgressPercentage = 0;
        }
        finally
        {
            RememberInputValues();
            IsRunning = false;
            if (indexChanged)
                OnPropertyChanged(nameof(CurrentDemoCaseIndex));
            RestartAutoCycleTimer();
        }
    }

    private void StartAutoCycleIfNeeded()
    {
        if (_autoCycleStarted)
            return;

        _autoCycleStarted = true;
        StartAutoCycle();
    }

    /// <summary>开始按 <see cref="AutoCycleInterval"/> 自动轮换演示用例。</summary>
    public void StartAutoCycle()
    {
        if (_cycleTimer != null || _autoCycleInterval is not { } interval || _cases.Count == 0)
            return;

        _cycleTimer = new Timer(
            _ => Dispatcher.UIThread.Post(() => ShowNextDemoCase()),
            state: null,
            dueTime: interval,
            period: interval);
        OnPropertyChanged(nameof(IsAutoCycleRunning));
    }

    /// <summary>停止自动轮换（保留当前用例）。</summary>
    public void StopAutoCycle()
    {
        if (_cycleTimer == null)
            return;

        _cycleTimer.Dispose();
        _cycleTimer = null;
        OnPropertyChanged(nameof(IsAutoCycleRunning));
    }

    private void RestartAutoCycleTimer()
    {
        if (_cycleTimer != null && _autoCycleInterval is { } interval)
            _cycleTimer.Change(interval, interval);
    }

    /// <summary>按声明顺序应用所有匹配规则的输入（联动）或输出（结果）。</summary>
    private IReadOnlyList<string> ApplyCases(bool applyInputs)
    {
        var matched = new List<string>();
        int previousIndex = _cycleIndex;

        for (int i = 0; i < _cases.Count; i++)
        {
            PanelDemoCase demoCase = _cases[i];
            if (!Matches(demoCase))
                continue;

            IReadOnlyDictionary<string, object> values = applyInputs
                ? demoCase.Inputs
                : demoCase.Outputs;

            ApplyValues(values, expectOutput: !applyInputs);

            if (!applyInputs)
                _cycleIndex = i; // 手动改输入后，工具条显示的当前用例跟随命中的规则

            if (values.Count > 0)
                matched.Add(Describe(demoCase));
        }

        if (!applyInputs && _cycleIndex != previousIndex)
            OnPropertyChanged(nameof(CurrentDemoCaseIndex));

        return matched;
    }

    private bool Matches(PanelDemoCase demoCase)
    {
        if (demoCase.Changed is { Count: > 0 } required
            && !required.Any(_changedInputs.Contains))
        {
            return false;
        }

        return MatchesState(demoCase);
    }

    /// <summary>只比较 <see cref="PanelDemoCase.When"/> 状态，不检查 Changed 门控。</summary>
    private bool MatchesState(PanelDemoCase demoCase)
    {
        foreach (var (name, expected) in demoCase.When)
        {
            DemoArgument argument = GetArgument(name);
            if (!string.Equals(ValueText(argument.Value), FormatValue(expected), StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private void ApplyValues(IReadOnlyDictionary<string, object> values, bool expectOutput)
    {
        foreach (var (name, value) in values)
        {
            DemoArgument argument = GetArgument(name);
            if (argument.IsOutput != expectOutput)
            {
                throw new InvalidOperationException(
                    $"演示数据把参数“{name}”当作{(expectOutput ? "输出" : "输入")}使用，但声明相反。");
            }

            ApplyValue(argument, value);
        }
    }

    private static void ApplyValue(DemoArgument argument, object value)
    {
        string text = FormatValue(value);
        if (argument.Value == null || !argument.Value.TryFromString(text))
            throw new InvalidOperationException($"演示值 \"{text}\" 无法写入参数“{argument.Name}”。");
    }

    private HashSet<string> GetChangedInputs()
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (DemoArgument argument in _argumentsByName.Values)
        {
            if (argument.IsOutput)
                continue;

            if (!_lastInputValues.TryGetValue(argument.Name, out string? last)
                || !string.Equals(last, ValueText(argument.Value), StringComparison.Ordinal))
            {
                changed.Add(argument.Name);
            }
        }

        return changed;
    }

    private void RememberInputValues()
    {
        foreach (DemoArgument argument in _argumentsByName.Values)
        {
            if (!argument.IsOutput)
                _lastInputValues[argument.Name] = ValueText(argument.Value);
        }
    }

    private DemoArgument GetArgument(string name)
        => _argumentsByName.TryGetValue(name, out DemoArgument? argument)
            ? argument
            : throw new InvalidOperationException($"演示数据引用了不存在的参数“{name}”。");

    private void AddArgument(string name, IValueType value, bool isOutput)
    {
        var argument = new DemoArgument(name, value, isOutput);
        _arguments.Add(argument);
        _argumentsByName.Add(name, argument);
    }

    private void Set<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // ---- 文本比较与说明生成 ----

    private static string ValueText(IValueType? value) => value?.ToString() ?? "";

    private static string FormatValue(object value) => value switch
    {
        null => "",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? "",
    };

    private static string FormatDisplayValue(object value)
    {
        string text = FormatValue(value);
        int lineEnd = text.IndexOfAny(new[] { '\r', '\n' });
        if (lineEnd >= 0)
            text = text[..lineEnd] + "…";
        return text.Length <= 32 ? text : text[..31] + "…";
    }

    private static string FormatTrigger(PanelDemoCase demoCase)
    {
        var parts = new List<string>();

        if (demoCase.Changed is { Count: > 0 } changed)
            parts.Add($"{string.Join("/", changed)} 变更");

        foreach (var (name, value) in demoCase.When)
            parts.Add($"{name}={FormatDisplayValue(value)}");

        return parts.Count == 0 ? "其他输入" : string.Join(" & ", parts);
    }

    private static string Describe(PanelDemoCase demoCase)
    {
        if (!string.IsNullOrWhiteSpace(demoCase.Description))
            return demoCase.Description!;

        var actions = new List<string>();
        foreach (var (name, value) in demoCase.Inputs)
            actions.Add($"{name}={FormatDisplayValue(value)}");
        foreach (var (name, value) in demoCase.Outputs)
            actions.Add($"{name}={FormatDisplayValue(value)}");

        string trigger = FormatTrigger(demoCase);
        return actions.Count == 0
            ? trigger
            : $"{trigger} → {string.Join(", ", actions)}";
    }

    private static string BuildToolDescription(IReadOnlyList<PanelDemoCase> cases)
    {
        if (cases.Count == 0)
            return "演示数据";

        var triggers = new List<string>(cases.Count);
        foreach (PanelDemoCase demoCase in cases)
            triggers.Add(FormatTrigger(demoCase));

        return "演示数据（按输入切换）：" + string.Join("；", triggers);
    }

    private sealed class DemoArgument : IPanelArgument
    {
        public DemoArgument(string name, IValueType value, bool isOutput)
        {
            Name = name;
            Value = value;
            IsOutput = isOutput;

            // 与 ToolArgument 一致：VT 值变化 → 参数 PropertyChanged → 面板绑定层回填 VM。
            if (value is INotifyPropertyChanged notify)
                notify.PropertyChanged += (_, _) =>
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }

        public string Name { get; }

        public bool IsOutput { get; }

        public IValueType? Value { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        // 演示数据不模拟参数级校验错误，接口事件保留但不会触发。
#pragma warning disable CS0067
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
#pragma warning restore CS0067
    }

    private sealed class DemoToolItem : IPanelItem
    {
        public DemoToolItem(string displayName, string description,
            IReadOnlyDictionary<string, string> extensions)
        {
            DisplayName = displayName;
            Description = description;
            Extensions = extensions;
        }

        public string DisplayName { get; }

        public string Description { get; }

        public IReadOnlyDictionary<string, string> Extensions { get; }
    }
}