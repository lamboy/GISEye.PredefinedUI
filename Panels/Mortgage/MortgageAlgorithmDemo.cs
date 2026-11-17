using System;
using System.Collections.Generic;
using System.Text;
using GISEye.Core;
using GISEye.PredefinedUI.Abstractions;
using GISEye.PredefinedUI.Panels;
using GISEye.ValueTypes;

namespace GISEye.PredefinedUI.Panels.Mortgage;

/// <summary>
/// Mortgage 面板的“真实算法”演示：重写 <see cref="PanelDemoSession.UpdateArguments"/>
/// 与 <see cref="PanelDemoSession.Execute"/>，行为镜像 <c>GISEye.Calculator.MortgageCalculatorTool</c>。
/// 与 <see cref="MortgageCaseDemo"/>（静态用例数据）并列注册，由 Demo 宿主切换。
/// </summary>
internal static class MortgageAlgorithmDemo
{
    public static IPanelSession CreateSession() => new AlgorithmSession();

    private sealed class AlgorithmSession : PanelDemoSession
    {
        public AlgorithmSession()
            : base(
                "房贷计算器（真实算法）",
                "真实算法：UpdateArguments 做频率→期数联动，Execute 按等额本息/等额本金实时计算；"
                + "修改 Principal / AnnualRate / Months 也会重算。",
                new Dictionary<string, string>
                {
                    [ToolUIHints.PanelHint] = "mortgage",
                    [ToolUIHints.AccentColor] = "#0078D4",
                },
                cases: Cases,
                autoCycleInterval: TimeSpan.FromSeconds(5))
        {
            AddInput("Principal", new VTDouble { Value = 100_000.0 });
            AddInput("AnnualRate", new VTDouble { Value = 4.9 });
            AddInput("Months", new VTInt { Value = 240 });
            AddInput("Method", new VTString { Value = "等额本息" });
            AddInput("Frequency", new VTString { Value = "Monthly" });

            AddOutput("OutPayment", new VTDouble());
            AddOutput("OutInterest", new VTDouble());
            AddOutput("OutTotal", new VTDouble());
            AddOutput("OutSchedule", new VTString { Value = "" });
        }

        /// <summary>对应 ITool.UpdateArguments：自动纠正与频率联动。</summary>
        protected override void UpdateArguments()
        {
            if (Arg<VTDouble>("Principal").Value <= 0)
                SetArgument("Principal", 1.0);

            double rate = Arg<VTDouble>("AnnualRate").Value;
            if (rate < 0)
                SetArgument("AnnualRate", 0);
            else if (rate > 36)
                SetArgument("AnnualRate", 36);

            if (Arg<VTString>("Method").Value is not ("等额本息" or "等额本金"))
                SetArgument("Method", "等额本息");

            string frequency = Arg<VTString>("Frequency").Value;
            int factor = frequency switch
            {
                "BiWeekly" => 2,
                "Weekly" => 4,
                _ => 1,
            };
            if (factor == 1 && frequency != "Monthly")
                SetArgument("Frequency", "Monthly");

            int months = Arg<VTInt>("Months").Value;
            if (months <= 0)
            {
                months = 1;
                SetArgument("Months", months);
            }

            if (months % factor != 0)
                SetArgument("Months", (int)Math.Ceiling(months / (double)factor) * factor);
        }

        /// <summary>对应 ITool.Execute：等额本息 / 等额本金计算。</summary>
        protected override void Execute()
        {
            double principal = Arg<VTDouble>("Principal").Value;
            double rate = Arg<VTDouble>("AnnualRate").Value;
            int months = Arg<VTInt>("Months").Value;
            string method = Arg<VTString>("Method").Value;
            string frequency = Arg<VTString>("Frequency").Value;

            double monthlyRate = rate / 100.0 / 12.0;
            var schedule = new StringBuilder();
            double totalPayment, totalInterest, periodPayment;

            if (method == "等额本金")
            {
                double principalPerPeriod = principal / months;
                totalPayment = 0;
                totalInterest = 0;

                for (int i = 1; i <= months; i++)
                {
                    double remaining = principal - principalPerPeriod * (i - 1);
                    double interest = remaining * monthlyRate;
                    double payment = principalPerPeriod + interest;
                    totalPayment += payment;
                    totalInterest += interest;

                    PrintPeriod(schedule, i, payment, principalPerPeriod, interest, remaining - principalPerPeriod);
                }

                periodPayment = principalPerPeriod + principal * monthlyRate;
            }
            else if (monthlyRate == 0)
            {
                periodPayment = principal / months;
                totalPayment = principal;
                totalInterest = 0;

                for (int i = 1; i <= months; i++)
                    PrintPeriod(schedule, i, periodPayment, periodPayment, 0, principal - periodPayment * i);
            }
            else
            {
                double factor = Math.Pow(1 + monthlyRate, months);
                periodPayment = principal * monthlyRate * factor / (factor - 1);
                totalPayment = periodPayment * months;
                totalInterest = totalPayment - principal;

                double remaining = principal;
                for (int i = 1; i <= months; i++)
                {
                    double interest = remaining * monthlyRate;
                    double principalPart = periodPayment - interest;
                    remaining -= principalPart;
                    PrintPeriod(schedule, i, periodPayment, principalPart, interest, Math.Max(0, remaining));
                }
            }

            if (frequency != "Monthly")
            {
                double p = periodPayment;
                periodPayment = frequency switch
                {
                    "BiWeekly" => p / 2.0,
                    "Weekly" => p / 4.0,
                    _ => p,
                };
            }

            Arg<VTDouble>("OutPayment").Value = Math.Round(periodPayment, 2);
            Arg<VTDouble>("OutInterest").Value = Math.Round(totalInterest, 2);
            Arg<VTDouble>("OutTotal").Value = Math.Round(totalPayment, 2);
            Arg<VTString>("OutSchedule").Value = schedule.ToString();
            ExecutionSummary = $"真实算法：{method} / {months} 期 / {frequency}";
        }

        private static void PrintPeriod(StringBuilder sb,
            int i, double payment, double principal, double interest, double remaining)
        {
            sb.AppendLine(
                $"#{i,2}: 还款 {payment,10:F2}  "
                + $"本金 {principal,10:F2}  利息 {interest,9:F2}  剩余 {remaining,11:F2}");
        }
    }

    // 自动/手动切换用例时只写输入条件，结果由真实算法计算。
    private static readonly PanelDemoCase[] Cases = new[]
    {
        Case("等额本息", "Monthly"),
        Case("等额本息", "BiWeekly"),
        Case("等额本息", "Weekly"),
        Case("等额本金", "Monthly"),
        Case("等额本金", "BiWeekly"),
        Case("等额本金", "Weekly"),
    };

    private static PanelDemoCase Case(string method, string frequency)
        => new()
        {
            When =
            {
                ["Method"] = method,
                ["Frequency"] = frequency,
            },
            Description = $"{method} · {frequency}",
        };
}