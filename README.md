# 持仓分析与因子实验室

[![build](https://github.com/Trivial-Ideal580/PositionLab/actions/workflows/build.yml/badge.svg)](https://github.com/Trivial-Ideal580/PositionLab/actions/workflows/build.yml)

面向 A 股持仓与因子研究的本地桌面工作台：读同花顺／Wind／通达信导出的行情与财报、技术指标与因子图表、概率模型与滚动验证（含财报基本面相关性）、表达式策略与多标的组合回测（含资金池目标权重再平衡）、策略对比、一键审计报告（含块自举显著性）、持仓透视与组合风险。C# / .NET Framework 4.x，单文件、零依赖、离线运行。

## 下载与运行

- **可运行版本**：在 [Releases](https://github.com/Trivial-Ideal580/PositionLab/releases) 下载 `PositionLab.exe`，双击启动，无需安装。
- **从源码构建**：克隆仓库后在 PowerShell 运行 `./build.ps1`，使用 Windows 自带的 .NET Framework 编译器生成 `PositionLab.exe`。

适用于运行 .NET Framework 4.x 的 Windows 桌面环境。应用无需 Node.js、Python、券商账户或网络。此工作台与相邻的 market 模拟交易台使用独立入口和数据。本仓库只收录源码，`build.ps1` 的编译产物不进版本控制。

## 快速体验

1. 启动后可切换日 K 线、均线、布林带、MACD、RSI、成交量、动量和波动率。图表支持近 60、120、250 个交易日，悬停可查看当日数值。「展开明细」显示完整的指标表，右上角可搜索和切换标的。
2. 选择 DEMO-A，进入「相关性与概率」并点击「拟合与验证」，再切到「滚动验证」看逐折明细。导入财报后，同一页的「分析基本面」会给出综合基本面、各指标与股价的相关程度和滚动相关曲线。
3. 进入「策略审计与回测」，在演示区间内运行默认均线策略。「回测范围」可切到「全部标的（等权）」，把资金平均分配到每个标的独立持仓；「对比策略」会用策略库里的四条预设（并自动并入当前编辑中的规则）批量回测，叠加净值曲线并列出收益、回撤、夏普、胜率等指标；「导出报告」把审计结论、检查项、分标明细、净值图与显著性直方图写成 `audit.md` / `audit.html`。两个导出按钮可分别保存逐日净值和全量成交（成交表带标的与 `rebalance` 标记列）。
4. 进入「财报基本面」，导入同花顺导出的财报（`.xls`／`.xlsx`／`.csv`）。用「可见截至」确认哪些报告期在当时可见，查看单季与 TTM、利润率与 ROE；「接入概率模型」用财报推导的 ROE／PE 跑一次滚动验证。
5. 进入「持仓透视」，点击「加载演示持仓」和「分析组合」，切换「持仓明细」或「配置与风格」查看结果。
6. 在「数据与声明」中保存示例文件。可据此准备真实数据，再通过界面导入。

## 验证方法

预测模型给出两个口径，界面同时显示：

- **单次切分**：按时间 70/30 切分，训练与测试之间留空一条，仅作参考。
- **滚动验证（主口径）**：按时间顺序扩窗重训，每折至少 180 条训练样本、60 条测试样本，测试窗口互不重叠（样本多时自动拉大步长，最多 40 折），每折训练与测试之间留空一条；汇总各折合并样本的准确率、AUC、Brier，并保留逐折明细与多数类基准。

滚动验证不使用任何未来数据：自检会先把行情截断到 400 根再重跑，要求两次运行中同起点各折的准确率、AUC、Brier 逐一相等。

**演示行情是合成随机序列，不含可学习的前瞻信号。** 因此演示中的准确率会落在 50% 附近、甚至低于多数类基准——这是预期结果，不是缺陷：它只用来演示流程与口径，不能据此判断模型能力。真实研究需要按 `available_date` 准备好每日收盘时已知的 ROE、PE 与情绪快照，同一套指标才有意义。

## 基本面相关性

导入财报后，「相关性与概率」页出现「基本面相关性」视图：

- **综合基本面（0–100）**：取 ROE（TTM）、销售毛利率、销售净利率、资产负债率、营收与净利 TTM 同比、现金流质量，按公司自身历史分位等权合成；资产负债率与相关方向相反。每个报告期只使用披露日当时可见的往期做分位，避免未来数据；至少要有 3 个可用指标才给分。
- **各指标与股价的相关程度**：给出 Pearson r——与收盘价（水平）、与 20 日收益（`CLOSE / CLOSE[20] - 1`，同期共变，不看未来），以及近一年、近三年两个固定区间的 r。PE 只展示最新值，不参与相关计算——PE 的分子就是收盘价，和股价相关没有意义。
- **不同时期相关度的变化**：滚动窗口默认 250 个交易日（可调 60–1000），对综合分、ROE、净利率、营收同比、净利同比分别计算与 20 日收益的滚动相关并画成折线。窗口内指标没有变化（例如同一报告期持续数月）时留空，不硬算 0。
- 模型未导入 CSV 快照时，「拟合与验证」自动使用财报推导的 ROE 与 PE（情绪取 0）；CSV 快照优先。

相关只描述同期共变，不等于因果或预测；样本过短、指标近似常数、价格趋势强时 r 会失真，需要结合报告期、行业和样本量判断。

## 文件与计算口径

- 行情支持 `CSV`、`TXT`、`XLSX` 与 `XLS`（Excel 97-2003，原生解析，不要求另存）。程序自动探测编码（UTF-8／GBK）、分隔符和表头别名，可直接读同花顺、Wind、通达信的导出：日期列可叫 `日期`／`交易日期`／`date`／`trade_dt`，收盘列可叫 `收盘`／`收盘价`／`close`；能跳过表头前的标题行、识别千分位与「万／亿」后缀、把 Excel 序列日期转成日期。没有标的列时用文件名作为代码（如 `600000.csv`、`600000.SH.csv`）；`600000.SH` 会归一为 `600000`。同一标的日期重复时保留最后一条并计数。
- 财报支持同花顺／Wind 导出的 `.xls`、`.xlsx` 与 `.csv`：单公司多期、科目在行、报告期在列，数值为年初至今累计。程序自动找表头、按日期升序重排、识别 20+ 科目的别名（`净利润(元)`、`扣非净利润(元)`、`营业总收入(元)`、`基本每股收益(元)`、`每股净资产(元)`、`销售毛利率`、`销售净利率`、`净资产收益率`、`资产负债率`、`流动比率`、`产权比率` 等），并丢弃超出表头宽度的多余单元格与重复报告期。单季由相邻累计值相减得到（一季报直接取累计），TTM 为最近四个单季之和，同比按同口径（单季对单季、TTM 对 TTM），基期不为正时留空。披露日按法定披露期限推算：一季报 4 月 30 日、半年报 8 月 31 日、三季报 10 月 31 日、年报次年 4 月 30 日；文件没有公告日期，界面与导出统一标注「按法定期限估计」，可见截止日之前的 K 线或模型样本取不到更晚的报告期。导入后可在「相关性与概率」页分析综合基本面与各指标和股价的相关性。命令行：`.\PositionLab.exe --statements .\300033_main_report.xls .\out`，产出 `statement.csv` 与 `statement-summary.txt`。
- 基本面与信息面 CSV 要求列名为 `symbol,available_date,roe,pe,sentiment`。输入收盘时已知的逐日快照，ROE 用百分点，情绪取 −1 到 1。模型将情绪作为输入数值，原始新闻需先整理为情绪快照。
- 持仓 CSV 要求列名为 `symbol,quantity,cost,bought_date,sector`。支持同标的多批次长仓和单一币种现金。核算基于共同估值日。入场分析只用买入交易日前一次收盘的均线状态，并报告可匹配数据的覆盖率。
- CSV 使用 UTF-8 和逗号分隔，日期使用 `YYYY-MM-DD`，数值使用小数点。行情和持仓成本需使用一致的价格口径。
- 策略使用 UTF-8 `.rule` 或 `.txt` 文件，包含 `NAME=`、`BUY=`、`SELL=`。支持四则运算与括号、比较符 `> < >= <= = == !=`、逻辑 `AND OR NOT`，字段可带历史下标（`CLOSE[1]`，最多回看 252 日，负下标直接拒绝）。函数：`ABS`、`MIN`、`MAX`、`IF`、`CROSS`、`CROSSDOWN`、`SUM`、`AVG`、`COUNT`（窗口 1–252）。例 `BUY=CROSS(MA5, MA20) AND COUNT(CLOSE > MA60, 5) >= 3`。缺指标处返回空值并跳过，未识别字段、负向回看和未知函数在解析期就被拒绝。
- 回测支持单标的，或把全部标的按等权组合回测：资金平均分配到每个标的，各自独立持仓、不做跨标的再平衡，组合净值为各标的净值之和，基准为等权买入持有；标的多于 30 个时按代码排序取前 30 个，界面会显示实际参与数量。规则回测不做跨标的再平衡；脚本可用 `INIT pool=1` 与 `TARGET` 在资金池内按目标权重再平衡（见下文「策略脚本」）。整单位、仅做多，使用比例费率和滑点。策略对比在同一区间与费用参数下批量回测多条规则，列出收益、年化、最大回撤、夏普、成交笔数、胜率与持仓占比。持仓风格来自现有持仓，风险曲线指标按当前权重回看。更多口径见程序内「数据与声明」。
- 指标口径：RSI14 与 ATR14 用 Wilder 平滑；EMA 以首根收盘价为种子，序列前段与以 SMA 为种子的实现会有小幅差异；布林带用总体标准差（除以 20）；年化按 252 个交易日与样本标准差。跨平台比对前请先统一这些口径。
- 同一标的两根 K 线间隔超过 10 个自然日会计入缺口提示。程序不补停牌与缺失交易日，缺口处的 `RET1` 只是两端收盘价之差，会放大波动与 ATR。

## 代码结构

- `src/Terminal.cs`：窗口骨架、导航、页面切换、后台任务与设置存取
- `src/Terminal.Pages.cs`：六个工作页的布局与交互
- `src/Terminal.UI.cs`：按钮、表格、卡片等控件构件
- `src/Terminal.Verify.cs`：界面自测
- `src/Analytics.cs`：概率模型与滚动验证、组合分析、演示数据
- `src/Data.cs`：CSV 读取、指标计算、分位与统计
- `src/Statement.cs`：财报考表语义层（XLS/XLSX/CSV、科目别名、单季/TTM、披露日与可见性）
- `src/Fundamental.cs`：综合基本面评分、指标与股价相关、滚动相关与分区间相关
- `src/Xls.cs`：BIFF8 `.xls` 读取（CFB 容器、SST 跨记录续接、RK/MULRK/NUMBER）
- `src/Significance.cs`：块自举显著性检验
- `src/Strategy.cs`：表达式解析（四则运算、函数、三值逻辑）、静态审计与单标的回测
- `src/PortfolioBacktest.cs`：多标的等权组合回测、策略对比与预设策略库
- `src/MarketImport.cs`：同花顺／Wind／通达信导出适配（编码、分隔符、表头别名、XLSX 读取）
- `src/Audit.cs`：审计引擎与 Markdown／HTML 报告生成
- `src/PythonConvert.cs`：Python（pandas／numpy／talib 子集）到规则语言或脚本的转换器
- `src/Script.cs`：脚本语言（状态、条件、循环、资金池再平衡、分笔下单）的解析与执行引擎
- `src/Plot.cs`、`src/FactorCharts.cs`：自绘图表与主题
- `src/Settings.cs`：界面状态持久化

`Terminal` 用 `partial` 拆成四个文件；`build.ps1` 按 `src/*.cs` 通配，新增源码文件不需要改工程。

## 从 Python 迁移

策略页「导入策略 ▾ → 从 Python 代码」，或命令行：

```powershell
.\PositionLab.exe --convert .\strategy.py .\out
```

把常见的 pandas / numpy / talib 策略翻译成规则语言：纯表达式输出 `.rule`；含 `if/else` 或 `for` 时自动改用脚本模式，输出 `.strategy`（规则语言本身不支持控制流）。两种情况都会写出 `convert-report.txt`。

| Python 写法 | 转换结果 |
| --- | --- |
| `df['close']`、`df.close` | `CLOSE` |
| `close.rolling(20).mean()` | `AVG(CLOSE, 20)` |
| `close.shift(1)`（列） | `CLOSE[1]` |
| `x.shift(1)`（表达式） | `LAG(x, 1)` |
| `close.pct_change()` | `RET1` |
| `close.ewm(span=12).mean()` | `EMA12` |
| `np.where(c, a, b)` | `IF(c, a, b)` |
| `np.maximum(a, b)` / `np.minimum` | `MAX(a, b)` / `MIN(a, b)` |
| `ta.SMA(x, 20)` / `ta.CROSS(a, b)` | `AVG(x, 20)` / `CROSS(a, b)` |
| `ta.RSI(close, 14)` | `RSI14` |
| `a & b`、`a \| b`、`~a` | `a AND b`、`a OR b`、`NOT a` |

信号变量命名为 `buy`/`sell`、`entry`/`exit`，或写成 `return buy, sell`；同一文件内的中间变量会自动内联。例如：

```python
close = df['close']
ma5, ma20 = close.rolling(5).mean(), close.rolling(20).mean()
buy = (ma5 > ma20) & (ma5.shift(1) <= ma20.shift(1))
sell = ma5 < ma20
```

转换成 `BUY=AVG(CLOSE, 5) > AVG(CLOSE, 20) AND LAG(AVG(CLOSE, 5), 1) <= LAG(AVG(CLOSE, 20), 1)`。

**不支持**：循环、条件分支、类、自定义函数、多返回值函数、`rolling().std()/max()/min()`、以及任意状态逻辑（持仓计数、逐笔加减仓）。只要出现无法翻译的语句，转换直接判失败并逐条列出原因——不输出可能失真的规则。转换结果还会先交给规则解析器编译校验，不通过不落盘。

## 策略脚本（.strategy）

表达式规则是无状态的逐根判断；需要**状态与循环**时改用脚本：

```
NAME=双均线加仓
PARAM fast=5
PARAM slow=20
VAR adds=0
ON BAR
  IF CROSS(AVG(CLOSE, fast), AVG(CLOSE, slow)) AND adds < 3 THEN
    BUY 0.5
    SET adds = adds + 1
  END
  IF CROSSDOWN(AVG(CLOSE, fast), AVG(CLOSE, slow)) THEN
    SELL 1
    SET adds = 0
  END
END
```

- `PARAM` 常量、`VAR` 跨 K 线状态、`LET` 每根重置的局部变量、`SET` 修改状态
- `IF … THEN … [ELSE …] END`、`FOR i = a TO b [STEP s] … END`（单次执行上限 200 次，防止死循环）
- `BUY 比例` / `SELL 比例` 按当前现金／持仓比例下单，**在下一根 K 线开盘成交**，仍不看未来
- 状态变量不能出现在 `LAG／SUM／AVG／COUNT／CROSS` 内部——那会把"现在"的值当成历史值，解析期直接拒绝

运行：界面「导入策略 ▾ → 从策略脚本（.strategy）」加载后直接运行回测；命令行 `PositionLab.exe --strategy 策略.strategy 行情.csv 输出目录`，产出 `strategy-result.txt`、`strategy-equity.csv`、`strategy-fills.csv`。

默认每个标的独立资金槽（资金等分）。在脚本开头加 `INIT pool=1` 改为**共享资金池**：所有标的共用一个现金账户，谁先触发谁占用，卖出回款回到池子；同一交易日多个信号按代码顺序依次成交。

资金池模式还能做**目标权重再平衡**：

```
NAME=双标的月度再平衡
INIT pool=1 rebalance=monthly
TARGET 510300 = 0.6
TARGET 511010 = 0.4
ON BAR
  IF CLOSE > MA60 THEN
    BUY 0.2
  END
END
```

- `INIT rebalance=monthly|weekly|<N 根>`：分别取当月／当周最后一个交易日收盘，或每 N 个交易日；再平衡日收盘按目标权重折算目标市值
- `TARGET 代码 = 权重`：权重取 0–1（也可写 `60%`），合计须 ≤1，差额留作现金；未列出的标的保持原仓位
- 调仓单在次根开盘成交，先卖后买，成交价照常计滑点与费用，成交记录标记 `rebalance`；`maxposition` 仍生效
- 目标权重只用于 `pool=1`；缺 `TARGET` 或权重合计超过 1 会在解析期直接拒绝

限制：循环只用于计算，不能逐根回放历史；没有自定义函数。

## 审计报告

界面「策略审计与回测」→「导出报告」，或命令行：

```powershell
.\PositionLab.exe --audit .\ma20.rule .\600000.csv .\out 8 5 1000000
# 参数：策略文件 行情文件 输出目录 [费用bp] [滑点bp] [资金]
```

退出码 `0` 通过、`2` 有警告、`1` 有失败或报错，可直接用于批处理与 CI。产出 `audit.md`、`audit.html`（含净值 SVG 与显著性直方图）、`audit-equity.csv`、`audit-significance.csv`、`audit-summary.txt`。

报告内容：数据识别结果（编码、分隔符、列映射、丢弃与去重行数）、规则静态审计（引用字段、函数、最大回看）、绩效与等权基准、分标明细、八项检查与结论：

| 检查 | 判定 |
| --- | --- |
| 规则可解析 | 买入/卖出表达式通过解析与静态审计 |
| 无未来引用 | 最大回看 ≤ 252，负下标在解析期即被拒绝 |
| 样本长度 | 少于 120 根 K 线时告警 |
| 数据连续性 | 存在 >10 自然日缺口时告警 |
| 策略触发 | 零成交判失败，少于 5 笔告警 |
| 截断不变性 | 截到 70% 重跑，重叠日净值偏差须 < 1e-9 |
| 成本敏感性 | 双倍成本下收益转负时告警 |
| 显著性 | 块自举平移入场信号 200 次，经验 p > 0.1 时告警「与随机择时无法区分」 |

**显著性**把入场／离场信号沿时间轴环形平移（块自举，保留信号的聚集结构），在同一价格路径上重跑 200 次，得到零假设下的收益分布，再算实际收益的经验 p 值：p 是「随机择时不优于实际策略」的尾部概率，越大越说明收益可能只是入场时点的运气。随机种子固定，同一输入可复现；`audit-significance.csv` 保存全部 200 次抽样，`audit.html` 里画出分布直方图并标出实际收益。

**截断不变性**是整份报告的核心：它不检查策略赚不赚钱，只检查结果有没有偷看未来。

## 复建与验证

```powershell
./build.ps1                     # 读取 VERSION 文件里的版本号
./build.ps1 -Version 1.1.0      # 显式指定版本，写入 exe 的文件版本与产品版本
powershell -ExecutionPolicy Bypass -File .\build.ps1   # 系统禁止执行脚本时
dotnet build PositionLab.csproj -c Release              # MSBuild 工程，供 IDE 与分析器使用
```

交付产物由 `build.ps1` 用系统自带 csc 生成（零依赖、免安装 SDK）；`PositionLab.csproj` 指向同一批源码，供 Visual Studio / 分析器使用，CI 两个路径都会编译。

编译产物带 `AssemblyFileVersion` / `AssemblyInformationalVersion`，`(Get-Item .\PositionLab.exe).VersionInfo` 可查。

自检有两种模式：`--verify-nogui` 只跑数据、指标、回测与模型断言，可用于无桌面环境（含 CI）；`--verify` 额外跑界面自测并截图，需要 Windows 桌面环境。两者都会把结果写到指定目录的 `verification.txt`。

```powershell
$qa = New-Item -ItemType Directory -Force verify
Start-Process -FilePath .\PositionLab.exe -ArgumentList @('--verify-nogui', ('"' + $qa.FullName + '"')) -Wait
Get-Content (Join-Path $qa.FullName 'verification.txt')
```

本机检查覆盖 CSV 往返和拒绝无效输入、指标与分位、滚动均线与定义式重算的一致性、表达式引擎（`CROSS` 等价性、算术优先级、`IF/COUNT/AVG`、除零判空、参数个数校验、`Inspect` 提取）、导入适配（GBK 表头、Wind 标题行与千分位、XLSX 序列日期、BIFF8 `.xls` 的 SST 跨记录续接）、财报语义层（16 期报告期升序、单季相减、TTM、法定披露日、披露前不可见、累计与单季对账、statement.csv 与摘要）、财报推导的概率模型快照、基本面分析（综合分边界、可见期映射、相关区间与滚动窗口）、资金池再平衡（月度调仓、成交标记、目标权重 ±1 手、非法 TARGET／rebalance 拒绝）、块自举显著性（分布长度、p 值范围、直方图产物）、未来引用拦截、追加未来数据后的回测不变性、成交及费用对账、组合回测的资金分配与净值可加性、单标的组合与单次回测的一致性、策略对比的行数与曲线起点、空策略库与缺失标的的拒绝、审计结论与五份产物、滚动验证的折结构与无未来数据、缺口检测、设置文件往返与容错、持仓估值，以及 20 万条合成序列的指标和回测。界面自检覆盖六个工作页、多标的回测、策略对比、滚动验证表格、财报页、基本面相关性视图与最小窗口尺寸下的裁切检查。

CI 会验证同样的东西：推送到 `main` 或发起 PR 时用 `dotnet build` 与 `build.ps1` 双路径编译并跑无界面自检；推送 `v*` 标签时额外创建 Release，附带 `PositionLab.exe`、SHA-256、文件版本与签名状态。

## 免责声明与安全

仅供研究和教学。评分、回测和概率估计不构成投资建议、收益承诺或交易指令。模型和静态审计依赖输入数据的时点、复权口径和完整性，使用者需独立核验。没有券商连接或自动交易。输入和计算结果保存在当前进程内，需要留档时请显式导出，并妥善保管含持仓的文件。演示行情为合成随机序列，不含可预测信号，演示中的模型准确率接近随机水平属预期。

## License

MIT，见 [LICENSE](LICENSE)。
