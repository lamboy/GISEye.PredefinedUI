# GISEye.PredefinedUI

GISEye 的自定义面板 UI 库：面板 View / ViewModel、参数绑定、`panel_hint` 注册表，以及脱离 GISEye 宿主独立调试面板用的演示数据机制。

演示数据位于各面板目录下（如 `Panels/Mortgage/MortgageCaseDemo.cs`），支持两种模式：

- **演示用例（静态）**：只提供输入/输出样例值与匹配规则，不执行算法；
- **真实算法**：重写 `PanelDemoSession.UpdateArguments`（参数联动）和 `Execute`（算法），输入变化时真实重算。

## 环境要求

- .NET 10 SDK
- Avalonia 12.0.3
- GISEye.Core

## GISEye.Core 引用

项目会按以下优先级自动解析 `GISEye.Core`：

1. 被复制进 GISEye 子树（`src/GISEye.PredefinedUI`）时，使用兄弟项目 `..\GISEye.Core`；
2. 独立检出且本机存在 `..\GISEye\src\GISEye.Core` 时，引用本地源码；
3. 以上都不存在时，回退到 NuGet 包 `GISEye.Core`（浮动版本，取最新稳定版）。

本地 GISEye 检出不在默认位置时，可用环境变量覆盖：

```bash
set GISEyeRoot=D:\path\to\GISEye
```

或直接构建时传入：

```bash
dotnet build GISEye.PredefinedUI.csproj -p:GISEyeRoot=D:\path\to\GISEye
```

## 构建

```bash
# 类库
dotnet build GISEye.PredefinedUI.csproj

# Demo 宿主
dotnet build samples/GISEye.PredefinedUI.Demo/GISEye.PredefinedUI.Demo.csproj
```

## 运行 Demo

```bash
dotnet run --project samples/GISEye.PredefinedUI.Demo
```