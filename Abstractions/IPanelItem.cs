namespace GISEye.PredefinedUI.Abstractions;

/// <summary>
/// <see cref="GISEye.Models.ToolItem"/> 对面板 VM 暴露的最小面。
/// 仅用于面板标题与 hint 字典查询；其它元数据面板不需要。
/// </summary>
public interface IPanelItem
{
    /// <summary>工具显示名（多语言解析后）。</summary>
    string DisplayName { get; }

    /// <summary>工具描述。</summary>
    string Description { get; }

    /// <summary>工具的 gRPC 扩展（key 形如 <c>ui.accent_color</c>、<c>ui.panel_hint</c>）。</summary>
    IReadOnlyDictionary<string, string> Extensions { get; }
}
