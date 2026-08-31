using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using GISEye.Core;
using GISEye.PredefinedUI.Abstractions;
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
            (session, hints) => new MortgagePanelViewModel(session, hints));
    }

    /// <summary>注册单个面板工厂；插件扩展点。</summary>
    public static void RegisterProvider(
        string key,
        string value,
        Func<IPanelSession, IReadOnlyDictionary<string, string>, IPanelViewModelBase?> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        _factories[(key, value)] = factory;
    }

    /// <summary>按 hint 查表，返回对应面板 VM；未命中 → null（调用方走默认两阶段流程）。</summary>
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
