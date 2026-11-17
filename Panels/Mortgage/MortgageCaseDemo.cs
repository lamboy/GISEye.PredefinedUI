using System;
using System.Collections.Generic;
using GISEye.Core;
using GISEye.PredefinedUI.Abstractions;
using GISEye.PredefinedUI.Panels;
using GISEye.ValueTypes;

namespace GISEye.PredefinedUI.Panels.Mortgage;

/// <summary>
/// Mortgage 面板的静态用例演示（只提供演示数据，不执行工具逻辑）。
/// 每组结果由输入参数（Method / Frequency）匹配决定，面板自动运行时会重新匹配并写入；
/// 会话每 5 秒自动轮换一组，便于观察各输入组合下的界面表现。
/// 新增自定义面板时照此在面板目录下放一个 <c>XxxCaseDemo.cs</c>（静态用例）
/// 或 <c>XxxAlgorithmDemo.cs</c>（真实算法）。
/// </summary>
internal static class MortgageCaseDemo
{
    public static IPanelSession CreateSession()
    {
        var session = new PanelDemoSession(
            "房贷计算器（演示）",
            description: null, // 由 Cases 自动生成说明
            extensions: new Dictionary<string, string>
            {
                [ToolUIHints.PanelHint] = "mortgage",
                [ToolUIHints.AccentColor] = "#0078D4",
            },
            cases: Cases,
            autoCycleInterval: TimeSpan.FromSeconds(5));

        // 输入参数
        session.AddInput("Principal", new VTDouble { Value = 100_000.0 });
        session.AddInput("AnnualRate", new VTDouble { Value = 4.9 });
        session.AddInput("Months", new VTInt { Value = 12 });
        session.AddInput("Method", new VTString { Value = "等额本息" });
        session.AddInput("Frequency", new VTString { Value = "Monthly" });

        // 输出参数（初值留空，由匹配到的规则写入）
        session.AddOutput("OutPayment", new VTDouble());
        session.AddOutput("OutInterest", new VTDouble());
        session.AddOutput("OutTotal", new VTDouble());
        session.AddOutput("OutSchedule", new VTString { Value = "" });
        return session;
    }

    // 规则按声明顺序匹配；当前 种子数据覆盖 Method × Frequency 的全部组合。
    private static readonly PanelDemoCase[] Cases = new[]
    {
        Case("等额本息", "Monthly", 8_556.17, 2_673.99, 102_673.99, EqualInstallmentSchedule),
        Case("等额本息", "BiWeekly", 4_278.08, 2_673.99, 102_673.99, EqualInstallmentSchedule),
        Case("等额本息", "Weekly", 2_139.04, 2_673.99, 102_673.99, EqualInstallmentSchedule),
        Case("等额本金", "Monthly", 8_741.67, 2_654.17, 102_654.17, EqualPrincipalSchedule),
        Case("等额本金", "BiWeekly", 4_370.83, 2_654.17, 102_654.17, EqualPrincipalSchedule),
        Case("等额本金", "Weekly", 2_185.42, 2_654.17, 102_654.17, EqualPrincipalSchedule),
    };

    private static PanelDemoCase Case(
        string method,
        string frequency,
        double payment,
        double interest,
        double total,
        string schedule)
        => new()
        {
            When =
            {
                ["Method"] = method,
                ["Frequency"] = frequency,
            },
            Outputs =
            {
                ["OutPayment"] = payment,
                ["OutInterest"] = interest,
                ["OutTotal"] = total,
                ["OutSchedule"] = schedule,
            },
        };

    private const string EqualInstallmentSchedule = """
        # 1: 还款    8556.17  本金    8147.83  利息    408.33  剩余    91852.17
        # 2: 还款    8556.17  本金    8181.10  利息    375.06  剩余    83671.06
        # 3: 还款    8556.17  本金    8214.51  利息    341.66  剩余    75456.55
        # 4: 还款    8556.17  本金    8248.05  利息    308.11  剩余    67208.50
        # 5: 还款    8556.17  本金    8281.73  利息    274.43  剩余    58926.77
        # 6: 还款    8556.17  本金    8315.55  利息    240.62  剩余    50611.22
        # 7: 还款    8556.17  本金    8349.50  利息    206.66  剩余    42261.72
        # 8: 还款    8556.17  本金    8383.60  利息    172.57  剩余    33878.12
        # 9: 还款    8556.17  本金    8417.83  利息    138.34  剩余    25460.29
        #10: 还款    8556.17  本金    8452.20  利息    103.96  剩余    17008.09
        #11: 还款    8556.17  本金    8486.72  利息     69.45  剩余     8521.37
        #12: 还款    8556.17  本金    8521.37  利息     34.80  剩余        0.00
        """;

    private const string EqualPrincipalSchedule = """
        # 1: 还款    8741.67  本金    8333.33  利息    408.33  剩余    91666.67
        # 2: 还款    8707.64  本金    8333.33  利息    374.31  剩余    83333.33
        # 3: 还款    8673.61  本金    8333.33  利息    340.28  剩余    75000.00
        # 4: 还款    8639.58  本金    8333.33  利息    306.25  剩余    66666.67
        # 5: 还款    8605.56  本金    8333.33  利息    272.22  剩余    58333.33
        # 6: 还款    8571.53  本金    8333.33  利息    238.19  剩余    50000.00
        # 7: 还款    8537.50  本金    8333.33  利息    204.17  剩余    41666.67
        # 8: 还款    8503.47  本金    8333.33  利息    170.14  剩余    33333.33
        # 9: 还款    8469.44  本金    8333.33  利息    136.11  剩余    25000.00
        #10: 还款    8435.42  本金    8333.33  利息    102.08  剩余    16666.67
        #11: 还款    8401.39  本金    8333.33  利息     68.06  剩余     8333.33
        #12: 还款    8367.36  本金    8333.33  利息     34.03  剩余        0.00
        """;
}