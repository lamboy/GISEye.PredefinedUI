namespace GISEye.PredefinedUI.Abstractions;

/// <summary>
/// 面板会话状态：与 protobuf 生成的 <c>RuntimeStatus</c> 枚举值一一对应（int 兼容，便于直接 cast）。
/// </summary>
/// <remarks>
/// 数值含义固定；面板代码不需要关心 protobuf 生成的 <c>RuntimeStatus</c> 类型。
/// </remarks>
public enum PanelSessionStatus
{
    Pending = 0,
    Executing = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
    Failed = 5,
}
