using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using GISEye.Core;
using GISEye.PredefinedUI.Abstractions;
using GISEye.PredefinedUI.Panels;
using GISEye.PredefinedUI.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace GISEye.PredefinedUI.Demo;

public partial class MainWindow : Window
{
    private const string PlayIcon = "M7,4 L19,12 L7,20 Z";
    private const string PauseIcon = "M7,4 L11,4 L11,20 L7,20 Z M14,4 L18,4 L18,20 L14,20 Z";

    private readonly ContentControl _panelHost;
    private readonly ComboBox _sourceSelector;
    private readonly PathIcon _autoIcon;
    private readonly Button _autoButton;
    private readonly Button _firstButton;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly TextBlock _caseCounter;

    private PanelDemoSession? _demoSession;

    public MainWindow()
    {
        InitializeComponent();

        // 走与 GISEye 宿主相同的注册 / 查表路径，验证 panel_hint → 面板工厂。
        PanelRegistration.RegisterBuiltInPanels(new ServiceCollection());

        _panelHost = this.FindControl<ContentControl>("PanelHost")!;
        _sourceSelector = this.FindControl<ComboBox>("DemoSourceSelector")!;
        _autoIcon = this.FindControl<PathIcon>("AutoIcon")!;
        _autoButton = this.FindControl<Button>("AutoButton")!;
        _firstButton = this.FindControl<Button>("FirstButton")!;
        _previousButton = this.FindControl<Button>("PreviousButton")!;
        _nextButton = this.FindControl<Button>("NextButton")!;
        _caseCounter = this.FindControl<TextBlock>("CaseCounter")!;

        // 演示来源由面板同目录的 XxxPanelDemo.cs 提供：静态用例数据 / 真实算法。
        IReadOnlyList<PanelDemoProvider> providers =
            PanelRegistration.GetDemoProviders(ToolUIHints.PanelHint, "mortgage");

        if (providers.Count == 0)
            throw new InvalidOperationException("面板 hint \"mortgage\" 未注册演示来源。");

        _sourceSelector.ItemsSource = providers;
        _sourceSelector.SelectedIndex = 0;
        _sourceSelector.SelectionChanged += OnDemoSourceChanged;

        ShowDemoSource(providers[0]);
    }

    private void OnDemoSourceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_sourceSelector.SelectedItem is PanelDemoProvider provider)
            ShowDemoSource(provider);
    }

    private void ShowDemoSource(PanelDemoProvider provider)
    {
        // 切走旧会话：停自动轮换并取消订阅，避免继续驱动工具条/面板。
        if (_demoSession != null)
        {
            _demoSession.PropertyChanged -= OnDemoSessionPropertyChanged;
            _demoSession.StopAutoCycle();
        }

        IPanelSession session = provider.CreateSession();
        _demoSession = session as PanelDemoSession;

        if (_demoSession != null)
            _demoSession.PropertyChanged += OnDemoSessionPropertyChanged;

        IReadOnlyDictionary<string, string> hints = session.ToolInfo.Extensions;
        if (!hints.TryGetValue(ToolUIHints.PanelHint, out string? panelHint)
            || string.IsNullOrEmpty(panelHint))
        {
            throw new InvalidOperationException(
                $"演示来源“{provider.Name}”缺少 {ToolUIHints.PanelHint}。");
        }

        IPanelViewModelBase? panel = PanelRegistration.CreatePanel(
            ToolUIHints.PanelHint, panelHint, hints, session);

        if (panel == null)
            throw new InvalidOperationException($"面板 hint \"{panelHint}\" 未注册。");

        // 面板的关闭按钮 → 关闭 Demo 窗口。
        if (panel is IToolPanel toolPanel)
            toolPanel.CloseRequested += Close;

        _panelHost.Content = panel;
        UpdateDemoToolbar();
    }

    private void OnDemoSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PanelDemoSession.CurrentDemoCaseIndex)
            or nameof(PanelDemoSession.IsAutoCycleRunning))
        {
            UpdateDemoToolbar();
        }
    }

    private void UpdateDemoToolbar()
    {
        PanelDemoSession? demo = _demoSession;

        if (demo == null)
        {
            _caseCounter.Text = "- / -";
            _autoIcon.Data = Geometry.Parse(PlayIcon);
            _autoButton.IsEnabled = false;
            SetStepperEnabled(false);
            return;
        }

        int total = demo.DemoCaseCount;
        int current = demo.CurrentDemoCaseIndex;
        _caseCounter.Text = total > 0
            ? (current >= 0 ? $"{current + 1} / {total}" : $"- / {total}")
            : "- / -";

        _autoIcon.Data = Geometry.Parse(demo.IsAutoCycleRunning ? PauseIcon : PlayIcon);
        _autoButton.IsEnabled = true;
        ToolTip.SetTip(_autoButton, demo.IsAutoCycleRunning ? "暂停自动演示" : "开始自动演示");
        SetStepperEnabled(total > 0);
    }

    private void SetStepperEnabled(bool enabled)
    {
        _firstButton.IsEnabled = enabled;
        _previousButton.IsEnabled = enabled;
        _nextButton.IsEnabled = enabled;
    }

    private void OnFirstClick(object? sender, RoutedEventArgs e) => _demoSession?.ShowFirstDemoCase();

    private void OnPreviousClick(object? sender, RoutedEventArgs e) => _demoSession?.ShowPreviousDemoCase();

    private void OnNextClick(object? sender, RoutedEventArgs e) => _demoSession?.ShowNextDemoCase();

    private void OnAutoClick(object? sender, RoutedEventArgs e)
    {
        if (_demoSession == null)
            return;

        if (_demoSession.IsAutoCycleRunning)
            _demoSession.StopAutoCycle();
        else
            _demoSession.StartAutoCycle();
    }
}