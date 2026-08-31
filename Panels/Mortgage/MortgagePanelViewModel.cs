using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GISEye.Core;
using GISEye.PredefinedUI.Abstractions;
using GISEye.PredefinedUI.Panels;
using GISEye.Resources.Models;
using GISEye.ValueTypes;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace GISEye.PredefinedUI.Panels.Mortgage;

/// <summary>
/// panel_hint = "mortgage" 的单窗口面板 VM：
/// 1. 工具参数强类型属性 + 固定控件绑定
/// 2. 输入变化自动触发工具执行（与"实时预览"等价的计算时机，但走工具真实路径）
/// 3. LiveCharts 折线图展示前 12 期还款计划（本金/利息/每期还款趋势）
/// </summary>
/// <remarks>
/// 不依赖本地计算函数；所有数值与图表均来自工具执行结果（<see cref="IPanelSession"/>）。
/// 预置页面为工具定制 UI：直接按参数名绑定到固定控件（NumericUpDown / ComboBox / TextBox）。</remarks>
public sealed partial class MortgagePanelViewModel : CustomPanelViewModelBase
{

    [GeneratedRegex(@"^#\s*(\d+):\s*还款\s*(?<p>[\d.]+)\s*本金\s*(?<pr>[\d.]+)\s*利息\s*(?<i>[\d.]+)\s*剩余\s*(?<r>[\d.]+)", RegexOptions.Compiled)]
    private static partial Regex ScheduleLineRegex();

    private static readonly Regex s_scheduleLineRegex = ScheduleLineRegex();

    private readonly IPanelArgument? _principalArg;
    private readonly IPanelArgument? _annualRateArg;
    private readonly IPanelArgument? _monthsArg;
    private readonly IPanelArgument? _methodArg;
    private readonly IPanelArgument? _frequencyArg;
    private readonly IPanelArgument? _outPaymentArg;
    private readonly IPanelArgument? _outInterestArg;
    private readonly IPanelArgument? _outTotalArg;
    private readonly IPanelArgument? _outScheduleArg;

    /// <summary>还款方式选项（与 MortgageCalculatorTool.s_validMethods 对应）</summary>
    public IReadOnlyList<string> MethodOptions { get; } = new[] { "等额本息", "等额本金" };

    /// <summary>还款频率选项（与 MortgageCalculatorTool.s_validFrequencies 对应）</summary>
    public IReadOnlyList<string> FrequencyOptions { get; } = new[] { "Monthly", "BiWeekly", "Weekly" };

    /// <summary>从 hints["ui.accent_color"] 解析的画刷，供 XAML <c>Background="{Binding AccentBrush}"</c> 直接绑定。</summary>
    public IBrush AccentBrush { get; }

    // ---- 图表数据 ----

    // 还款总期数
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChartTitle))]
    private int _totalPeriods;

    public string ChartTitle => $"还款计划趋势（{TotalPeriods} 期）";

    public ObservableCollection<string> XLabels { get; } = new();
    public ObservableCollection<double> PrincipalSeries { get; } = new();
    public ObservableCollection<double> InterestSeries { get; } = new();
    public ObservableCollection<double> PaymentSeries { get; } = new();

    public ISeries[] Series { get; }
    public ICartesianAxis[] XAxes { get; }
    public ICartesianAxis[] YAxes { get; }

    /// <summary>工具是否正在计算（用于显示 loading）。</summary>
    [ObservableProperty] private bool _isComputing;

    /// <summary>工具执行错误信息（如有）。</summary>
    [ObservableProperty] private string? _errorMessage;

    public MortgagePanelViewModel(IPanelSession session, IReadOnlyDictionary<string, string> hints)
        : base(session)
    {
        AccentBrush = ParseAccentBrush(hints);

        _principalArg = FindArg("Principal");
        _annualRateArg = FindArg("AnnualRate");
        _monthsArg = FindArg("Months");
        _methodArg = FindArg("Method");
        _frequencyArg = FindArg("Frequency");
        _outPaymentArg = FindArg("OutPayment");
        _outInterestArg = FindArg("OutInterest");
        _outTotalArg = FindArg("OutTotal");
        _outScheduleArg = FindArg("OutSchedule");

        // 折线图系列：直接绑定 ObservableCollection，LiveCharts 监听 INotifyCollectionChanged 自动更新
        var principalPaint = new SolidColorPaint(SKColors.SteelBlue) { StrokeThickness = 2 };
        var interestPaint = new SolidColorPaint(SKColors.OrangeRed) { StrokeThickness = 2 };
        var paymentPaint = new SolidColorPaint(SKColors.MediumSeaGreen) { StrokeThickness = 2 };

        Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Name = "本金",
                Values = PrincipalSeries,
                Stroke = principalPaint,
                GeometryStroke = principalPaint,
                GeometrySize = 4,
                Fill = null,
            },
            new LineSeries<double>
            {
                Name = "利息",
                Values = InterestSeries,
                Stroke = interestPaint,
                GeometryStroke = interestPaint,
                GeometrySize = 4,
                Fill = null,
            },
            new LineSeries<double>
            {
                Name = "每期还款",
                Values = PaymentSeries,
                Stroke = paymentPaint,
                GeometryStroke = paymentPaint,
                GeometrySize = 4,
                Fill = null,
            },
        };

        XAxes = new ICartesianAxis[]
        {
            new Axis
            {
                Labeler = value => $"#{value + 1:N0}期",
                Labels = XLabels,
            }
        };
        YAxes = new ICartesianAxis[]
        {
            new Axis()
        };

        // 转发 IPanelArgument.Value 变化 → 输入 setter 触发自动运行；输出 setter 更新图表
        ForwardInputValueChanges(_principalArg, nameof(Principal), () => _ = RunAsync());
        ForwardInputValueChanges(_annualRateArg, nameof(AnnualRate), () => _ = RunAsync());
        ForwardInputValueChanges(_monthsArg, nameof(Months), () => _ = RunAsync());
        ForwardInputValueChanges(_methodArg, nameof(Method), () => _ = RunAsync());
        ForwardInputValueChanges(_frequencyArg, nameof(Frequency), () => _ = RunAsync());

        // 输出变化 → 更新图表 + 触发 PropertyChanged
        ForwardOutputValueChanges(_outPaymentArg, nameof(OutPayment));
        ForwardOutputValueChanges(_outInterestArg, nameof(OutInterest), nameof(InterestPercent), nameof(PrincipalPercent), nameof(InterestLabel), nameof(PrincipalLabel));
        ForwardOutputValueChanges(_outTotalArg, nameof(OutTotal), nameof(InterestPercent), nameof(PrincipalPercent), nameof(InterestLabel), nameof(PrincipalLabel));
        if (_outScheduleArg != null)
        {
            _outScheduleArg.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "Value")
                {
                    OnPropertyChanged(nameof(OutSchedule));
                    UpdateChartFromSchedule();
                }
            };
        }

        // 同步读取 session 现有输出（reattach 场景：session 可能已有完成结果，
        // 此时不再有 PropertyChanged 事件，必须主动填充一次）
        RefreshOutputsFromSession();

        // 初始自动运行：直接启动（ExecuteTask 处于 UI 线程；RunAsync 内部 await 不阻塞 UI）。
        // 不用 Dispatcher.Post —— 避免窗口 Show 期间 Post 任务执行时序的不确定性。
        _ = RunAsync();
    }

    private static IBrush ParseAccentBrush(IReadOnlyDictionary<string, string> hints)
    {
        if (hints.TryGetValue(ToolUIHints.AccentColor, out var hex) && !string.IsNullOrEmpty(hex))
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { /* fallthrough */ }
        }
        return new SolidColorBrush(Color.Parse("#0078D4"));
    }

    private IPanelArgument? FindArg(string name) =>
        Session.Arguments.FirstOrDefault(a => a.Name == name);

    private void ForwardInputValueChanges(IPanelArgument? arg, string propertyName, Action onChanged)
    {
        if (arg == null) return;
        arg.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Value")
            {
                OnPropertyChanged(propertyName);
                onChanged();
            }
        };
    }

    private void ForwardOutputValueChanges(IPanelArgument? arg, params string[] propertyNames)
    {
        if (arg == null) return;
        arg.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Value")
            {
                foreach (var propertyName in propertyNames)
                    OnPropertyChanged(propertyName);
            }
        };
    }

    private async Task RunAsync()
    {
        // 上次执行仍在进行时跳过（结果到达时会由事件链或完成回调刷新）
        if (Session.IsRunning) return;
        try
        {
            IsComputing = true;
            ErrorMessage = null;
            await Session.StartExecutionAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsComputing = false;
            // 执行结束主动刷新输出：兜底事件链（值相同时 IPanelArgument 不触发 PropertyChanged）
            RefreshOutputsFromSession();
        }
    }

    /// <summary>
    /// 从 session 当前状态主动刷新全部输出展示（数字 + 摘要 + 图表）。
    /// 与 PropertyChanged 事件链互斥兜底：事件链失效时此方法保证 UI 与 session 输出一致。
    /// </summary>
    private void RefreshOutputsFromSession()
    {
        OnPropertyChanged(nameof(OutPayment));
        OnPropertyChanged(nameof(OutInterest));
        OnPropertyChanged(nameof(OutTotal));
        OnPropertyChanged(nameof(OutSchedule));
        OnPropertyChanged(nameof(InterestPercent));
        OnPropertyChanged(nameof(PrincipalPercent));
        OnPropertyChanged(nameof(InterestLabel));
        OnPropertyChanged(nameof(PrincipalLabel));
        UpdateChartFromSchedule();
    }

    private void UpdateChartFromSchedule()
    {
        TotalPeriods = 0;
        XLabels.Clear();
        PrincipalSeries.Clear();
        InterestSeries.Clear();
        PaymentSeries.Clear();

        var schedule = OutSchedule;
        if (string.IsNullOrEmpty(schedule)) return;

        foreach (var line in schedule.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            TotalPeriods++;
            var m = s_scheduleLineRegex.Match(line.Trim());
            if (!m.Success) continue;
            int period = int.Parse(m.Groups[1].Value);
            double payment = double.Parse(m.Groups["p"].Value);
            double principal = double.Parse(m.Groups["pr"].Value);
            double interest = double.Parse(m.Groups["i"].Value);
            XLabels.Add($"{period}");
            PaymentSeries.Add(payment);
            PrincipalSeries.Add(principal);
            InterestSeries.Add(interest);
        }
    }

    // ---- 输入参数（双向）----

    public double Principal
    {
        get => _principalArg?.Value is VTDouble d ? d.Value : 0.0;
        set { if (_principalArg?.Value is VTDouble d) d.Value = value; }
    }

    public double AnnualRate
    {
        get => _annualRateArg?.Value is VTDouble d ? d.Value : 0.0;
        set { if (_annualRateArg?.Value is VTDouble d) d.Value = value; }
    }

    public int Months
    {
        get => _monthsArg?.Value is VTInt i ? i.Value : 0;
        set { if (_monthsArg?.Value is VTInt i) i.Value = value; }
    }

    public string Method
    {
        get => _methodArg?.Value is VTString s ? s.Value : "";
        set { if (_methodArg?.Value is VTString s) s.Value = value ?? ""; }
    }

    public string Frequency
    {
        get => _frequencyArg?.Value is VTString s ? s.Value : "";
        set { if (_frequencyArg?.Value is VTString s) s.Value = value ?? ""; }
    }

    // ---- 输出参数（只读）----

    public double OutPayment => _outPaymentArg?.Value is VTDouble d ? d.Value : 0.0;
    public double OutInterest => _outInterestArg?.Value is VTDouble d ? d.Value : 0.0;
    public double OutTotal => _outTotalArg?.Value is VTDouble d ? d.Value : 0.0;
    public string OutSchedule => _outScheduleArg?.Value is VTString s ? s.Value : "";

    // ---- 预览摘要（与工具输出对齐）----

    /// <summary>利息占总还款额百分比（0-100），供 ProgressBar.Value 绑定。</summary>
    public double InterestPercent
    {
        get
        {
            if (OutTotal <= 0) return 0;
            return OutInterest / OutTotal * 100;
        }
    }

    /// <summary>本金占总还款额百分比（0-100），供 ProgressBar.Value 绑定。</summary>
    public double PrincipalPercent
    {
        get
        {
            return 100 - InterestPercent;
        }
    }

    /// <summary>利息标签：用于本金/利息构成条下方右侧。</summary>
    public string InterestLabel => $"利息 ¥{OutInterest:N0}  {InterestPercent:F0}%";

    /// <summary>本金标签：用于本金/利息构成条下方左侧。</summary>
    public string PrincipalLabel => $"本金 ¥{Principal:N0}  {PrincipalPercent:F0}%";
}
