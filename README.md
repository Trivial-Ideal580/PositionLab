# 持仓分析与因子实验室

## 下载与运行

- **可运行版本**：在 [Releases](https://github.com/Trivial-Ideal580/PositionLab/releases) 下载 `PositionLab.exe`，双击启动，无需安装。
- **从源码构建**：克隆仓库后在 PowerShell 运行 `./build.ps1`，使用 Windows 自带的 .NET Framework 编译器生成 `PositionLab.exe`。

适用于运行 .NET Framework 4.x 的 Windows 桌面环境。应用无需 Node.js、Python、券商账户或网络。此工作台与相邻的 market 模拟交易台使用独立入口和数据。本仓库只收录源码，`build.ps1` 的编译产物不进版本控制。

## 快速体验

1. 启动后可切换日 K 线、均线、布林带、MACD、RSI、成交量、动量和波动率。图表支持近 60、120、250 个交易日，悬停可查看当日数值。「展开明细」显示完整的指标表，右上角可搜索和切换标的。
2. 选择 DEMO-A，进入「相关性与概率」并点击「拟合与验证」。
3. 进入「策略审计与回测」，在演示区间内运行默认均线策略。将鼠标移到图表上查看指定交易日的数值。两个导出按钮可分别保存逐日净值和全量成交。
4. 进入「持仓透视」，点击「加载演示持仓」和「分析组合」，切换「持仓明细」或「配置与风格」查看结果。
5. 在「数据与声明」中保存示例文件。可据此准备真实数据，再通过界面导入。

## 文件与计算口径

- 行情 CSV 要求列名为 `symbol,date,open,high,low,close,volume`，同一标的日期唯一。
- 基本面与信息面 CSV 要求列名为 `symbol,available_date,roe,pe,sentiment`。输入收盘时已知的逐日快照，ROE 用百分点，情绪取 −1 到 1。模型将情绪作为输入数值，原始新闻需先整理为情绪快照。
- 持仓 CSV 要求列名为 `symbol,quantity,cost,bought_date,sector`。支持同标的多批次长仓和单一币种现金。核算基于共同估值日。入场分析只用买入交易日前一次收盘的均线状态，并报告可匹配数据的覆盖率。
- CSV 使用 UTF-8 和逗号分隔，日期使用 `YYYY-MM-DD`，数值使用小数点。行情和持仓成本需使用一致的价格口径。
- 策略使用 UTF-8 `.rule` 或 `.txt` 文件，包含 `NAME=`、`BUY=`、`SELL=`，允许字段、数值、比较、AND 和 OR。例 `BUY=CLOSE > MA20 AND CLOSE[1] <= MA20[1]`。含未来引用或未识别函数的文件会被拒绝。
- 回测支持单标的、整单位、仅做多和满仓入场，使用比例费率和滑点。基准按首日收盘归一化。持仓风格来自现有持仓，风险曲线指标按当前权重回看。更多口径见程序内「数据与声明」。

## 复建与验证

在 PowerShell 运行 `./build.ps1` 可重编。若系统提示「禁止运行脚本」，改用 `powershell -ExecutionPolicy Bypass -File .\build.ps1`。自检需要已存在的输出目录和 Windows 桌面环境，例如以下命令会将测试结果、示例文件和界面截图写到本目录的 verify 文件夹。

```powershell
$qa = New-Item -ItemType Directory -Force verify
Start-Process -FilePath .\PositionLab.exe -ArgumentList @('--verify', ('"' + $qa.FullName + '"')) -Wait
Get-Content (Join-Path $qa.FullName 'verification.txt')
```

本机检查覆盖 CSV 往返和拒绝无效输入、指标与分位、未来引用拦截、追加未来数据后的回测不变性、成交及费用对账、样本外模型、持仓估值，以及 20 万条合成序列的指标和回测。界面自检覆盖五个工作页。

## 免责声明与安全

仅供研究和教学。评分、回测和概率估计不构成投资建议、收益承诺或交易指令。模型和静态审计依赖输入数据的时点、复权口径和完整性，使用者需独立核验。没有券商连接或自动交易。输入和计算结果保存在当前进程内，需要留档时请显式导出，并妥善保管含持仓的文件。

## License

MIT，见 [LICENSE](LICENSE)。


