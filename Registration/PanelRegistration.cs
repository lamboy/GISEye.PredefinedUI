using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using GISEye.Core;
using GISEye.PredefinedUI.Abstractions;
using GISEye.PredefinedUI.Panels;
using GISEye.PredefinedUI.Panels.Mortgage;

namespace GISEye.PredefinedUI.Registration;

/// <summary>
/// GISEye.PredefinedUI 面板系统的唯一公开类。承担三类角色：
/// 1. <see cref="RegisterBuiltInPanels"/>：GISEye 启动时一次性调用，注册内置面板的 DI 与工厂；
/// 2. <see cref="RegisterProvider"/>：第三方扩展注册自己的面板工厂；
/// 3. <see cref="CreatePanel"/>：GISEye.ToolExplorerViewModel 按 hint 查表并创建面板 VM。
/// </summary>
/// <remarks>
/// <para>本类同时维护 <see cref="ViewModelToViewMapping"/>：仅包含 PredefinedUI 内置/插件注册的
/// 面板 VM→View 映射，供 <c>GISEye.ViewLocator</c> 在主项目 <c>DIExtensions.ViewModelAndViewTypeMapping</c>
/// 未命中时作为兜底查询。两份映射并存而非合并，原因：<c>DIExtensions</c> 在 GISEye 中维护
/// 主应用 VM 映射；PredefinedUI 不引用 GISEye，自然无法更新主项目那份静态字典。</para>
/// </remarks>
public static class PanelRegistration
{
    // 内部工厂表：(HintKey, HintValue) → 给定 session + hints，返回面板 VM。
    private static readonly Dictionary<(string Key, string Value),
        Func<IPanelSession, IReadOnlyDictionary<string, string>, IPanelViewModelBase?>> _factories = new();

    // 演示来源表：(HintKey, HintValue) → 多个演示来源（静态用例 / 真实算法等），供 Demo 宿主切换。
    private static readonly Dictionary<(string Key, string Value), IReadOnlyList<PanelDemoProvider>> _demoProviders = new();

    // VM → View 映射（仅 PredefinedUI 注册的面板）。
    private static readonly Dictionary<Type, Type> _viewModelToViewMapping = new();

    /// <summary>外部读取 VM → View 映射（GISEye.ViewLocator 用作兜底查询）。</summary>
    public static IReadOnlyDictionary<Type, Type> ViewModelToViewMapping => _viewModelToViewMapping;

    /// <summary>
    /// GISEye.App 启动时调用一次；注册内置面板的 DI 与工厂。
    /// </summary>
    public static void RegisterBuiltInPanels(IServiceCollection services)
    {
        // 1. 内置面板 DI 注册（View + ViewModel 一对一绑定）。
        services.AddTransient<MortgagePanelView>();
        services.AddTransient<MortgagePanelViewModel>();
        _viewModelToViewMapping[typeof(MortgagePanelViewModel)] = typeof(MortgagePanelView);

        // 2. 内置面板工厂注册（直接 lambda，无需单独的 Provider 类）。
        RegisterProvider(
            ToolUIHints.PanelHint,
            "mortgage",
            (session, hints) => new MortgagePanelViewModel(session, hints),
            demos: new[]
            {
                new PanelDemoProvider("演示用例（静态）", MortgageCaseDemo.CreateSession),
                new PanelDemoProvider("真实算法", MortgageAlgorithmDemo.CreateSession),
            });
    }

    /// <summary>
    /// 注册单个面板工厂；插件扩展点。
    /// </summary>
    /// <param name="demos">
    /// 可选的演示来源列表（通常由面板同目录的 <c>XxxPanelDemo.cs</c> 提供），
    /// Demo 宿主可经 <see cref="GetDemoProviders"/> 枚举切换“静态用例 / 真实算法”等模式。
    /// </param>
    public static void RegisterProvider(
        string key,
        string value,
        Func<IPanelSession, IReadOnlyDictionary<string, string>, IPanelViewModelBase?> factory,
        IReadOnlyList<PanelDemoProvider>? demos = null)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        _factories[(key, value)] = factory;
        if (demos is { Count: > 0 })
            _demoProviders[(key, value)] = demos;
    }

    /// <summary>取该 hint 注册的演示来源列表；未注册 → 空列表。</summary>
    public static IReadOnlyList<PanelDemoProvider> GetDemoProviders(string key, string value)
        => _demoProviders.TryGetValue((key, value), out var demos)
            ? demos
            : Array.Empty<PanelDemoProvider>();

    /// <summary>创建该 hint 的默认演示会话（列表首个来源）；未注册 → null。</summary>
    public static IPanelSession? CreateDemoSession(string key, string value)
    {
        IReadOnlyList<PanelDemoProvider> demos = GetDemoProviders(key, value);
        return demos.Count > 0 ? demos[0].CreateSession() : null;
    }

    /// <summary>
    /// 是否存在该 hint 对应的面板工厂。
    /// 供界面决定是否显示"自定义面板"入口（工具声明了 hint 但没有 Provider 时不显示）。
    /// </summary>
    public static bool HasPanel(string key, string value)
        => !string.IsNullOrEmpty(value) && _factories.ContainsKey((key, value));

    /// <summary>按 hint 查表，返回对应面板 VM；未命中 → null（调用方自行决定提示或退回默认流程）。</summary>
    public static IPanelViewModelBase? CreatePanel(
        string key,
        string value,
        IReadOnlyDictionary<string, string> hints,
        IPanelSession session)
    {
        if (_factories.TryGetValue((key, value), out var factory))
            return factory(session, hints);
        return null;
    }
}
