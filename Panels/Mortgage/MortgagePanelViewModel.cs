using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GISEye.Core;
using GISEye.PredefinedUI.Abstractions;
using GISEye.PredefinedUI.Panels;
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
/// 参数通过 <see cref="CustomPanelViewModelBase.BindArgument{T}"/> 绑定到 [ObservableProperty] 属性：
/// 输入双向同步（变化触发自动运行），输出单向填充；验证特性与参数服务端校验错误
/// 经基类 INotifyDataErrorInfo 自动反馈到输入控件。</remarks>
public sealed partial class MortgagePanelViewModel : CustomPanelViewModelBase
{

    [GeneratedRegex(@"^#\s*(\d+):\s*还款\s*(?<p>[\d.]+)\s*本金\s*(?<pr>[\d.]+)\s*利息\s*(?<i>[\d.]+)\s*剩余\s*(?<r>[\d.]+)", RegexOptions.Compiled)]
    private static partial Regex ScheduleLineRegex();

    private static readonly Regex s_scheduleLineRegex = ScheduleLineRegex();

    // ---- 输入参数（BindArgument 双向绑定；验证特性在 BindArgument 调用处传入）----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrincipalLabel))]
    private double _principal;

    [ObservableProperty]
    private double _annualRate;

    [ObservableProperty]
    private int _months;

    [ObservableProperty]
    private string _method = "";

    [ObservableProperty]
    private string _frequency = "";

    // ---- 输出参数（BindArgument 单向：参数 → VM）----

    [ObservableProperty]
    private double _outPayment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InterestPercent))]
    [NotifyPropertyChangedFor(nameof(PrincipalPercent))]
    [NotifyPropertyChangedFor(nameof(InterestLabel))]
    [NotifyPropertyChangedFor(nameof(PrincipalLabel))]
    private double _outInterest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InterestPercent))]
    [NotifyPropertyChangedFor(nameof(PrincipalPercent))]
    [NotifyPropertyChangedFor(nameof(InterestLabel))]
    [NotifyPropertyChangedFor(nameof(PrincipalLabel))]
    private double _outTotal;

    [ObservableProperty]
    private string _outSchedule = "";

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

        // 参数绑定：输入双向（写入后经 OnBoundInputChanged 触发自动运行）；输出单向填充 VM 属性。
        // BindArgument 内部做初始拉取，覆盖 reattach 场景（session 可能已有完成结果）。
        BindArgument("Principal", () => Principal, v => Principal = v,
            new RangeAttribute(0.01, 1_000_000_000) { ErrorMessage = "贷款本金需大于 0" });
        BindArgument("AnnualRate", () => AnnualRate, v => AnnualRate = v,
            new RangeAttribute(0, 100) { ErrorMessage = "年利率需在 0 ~ 100 之间" });
        BindArgument("Months", () => Months, v => Months = v,
            new RangeAttribute(1, 1200) { ErrorMessage = "还款期数需在 1 ~ 1200 个月之间" });
        BindArgument("Method", () => Method, v => Method = v);
        BindArgument("Frequency", () => Frequency, v => Frequency = v);
        BindArgument("OutPayment", () => OutPayment, v => OutPayment = v);
        BindArgument("OutInterest", () => OutInterest, v => OutInterest = v);
        BindArgument("OutTotal", () => OutTotal, v => OutTotal = v);
        BindArgument("OutSchedule", () => OutSchedule, v => OutSchedule = v);

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

    /// <summary>输入参数经绑定写入后自动重新运行（与"实时预览"等价的计算时机，走工具真实路径）。</summary>
    protected override void OnBoundInputChanged(string argumentName) => _ = RunAsync();

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
            // 无需兜底刷新：输出参数由绑定层在参数值变化时推送到 VM 属性
        }
    }

    /// <summary>OutSchedule 绑定值变化时重绘还款计划趋势图。</summary>
    partial void OnOutScheduleChanged(string value) => UpdateChartFromSchedule();

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
