# 持仓分析与因子实验室

[![build](https://github.com/Trivial-Ideal580/PositionLab/actions/workflows/build.yml/badge.svg)](https://github.com/Trivial-Ideal580/PositionLab/actions/workflows/build.yml)

面向 A 股持仓与因子研究的本地桌面工作台：行情与基本面导入、技术指标与因子图表、概率模型与滚动验证、策略审计与回测、持仓透视与组合风险。C# / .NET Framework 4.x，单文件、零依赖、离线运行。

## 下载与运行

- **可运行版本**：在 [Releases](https://github.com/Trivial-Ideal580/PositionLab/releases) 下载 `PositionLab.exe`，双击启动，无需安装。
- **从源码构建**：克隆仓库后在 PowerShell 运行 `./build.ps1`，使用 Windows 自带的 .NET Framework 编译器生成 `PositionLab.exe`。

适用于运行 .NET Framework 4.x 的 Windows 桌面环境。应用无需 Node.js、Python、券商账户或网络。此工作台与相邻的 market 模拟交易台使用独立入口和数据。本仓库只收录源码，`build.ps1` 的编译产物不进版本控制。

## 快速体验

1. 启动后可切换日 K 线、均线、布林带、MACD、RSI、成交量、动量和波动率。图表支持近 60、120、250 个交易日，悬停可查看当日数值。「展开明细」显示完整的指标表，右上角可搜索和切换标的。
2. 选择 DEMO-A，进入「相关性与概率」并点击「拟合与验证」，再切到「滚动验证」看逐折明细。
3. 进入「策略审计与回测」，在演示区间内运行默认均线策略。将鼠标移到图表上查看指定交易日的数值。两个导出按钮可分别保存逐日净值和全量成交。
4. 进入「持仓透视」，点击「加载演示持仓」和「分析组合」，切换「持仓明细」或「配置与风格」查看结果。
5. 在「数据与声明」中保存示例文件。可据此准备真实数据，再通过界面导入。

## 验证方法

预测模型给出两个口径，界面同时显示：

- **单次切分**：按时间 70/30 切分，训练与测试之间留空一条，仅作参考。
- **滚动验证（主口径）**：按时间顺序扩窗重训，每折至少 180 条训练样本、60 条测试样本，测试窗口互不重叠（样本多时自动拉大步长，最多 40 折），每折训练与测试之间留空一条；汇总各折合并样本的准确率、AUC、Brier，并保留逐折明细与多数类基准。

滚动验证不使用任何未来数据：自检会先把行情截断到 400 根再重跑，要求两次运行中同起点各折的准确率、AUC、Brier 逐一相等。

**演示行情是合成随机序列，不含可学习的前瞻信号。** 因此演示中的准确率会落在 50% 附近、甚至低于多数类基准——这是预期结果，不是缺陷：它只用来演示流程与口径，不能据此判断模型能力。真实研究需要按 `available_date` 准备好每日收盘时已知的 ROE、PE 与情绪快照，同一套指标才有意义。

## 文件与计算口径

- 行情 CSV 要求列名为 `symbol,date,open,high,low,close,volume`，同一标的日期唯一。
- 基本面与信息面 CSV 要求列名为 `symbol,available_date,roe,pe,sentiment`。输入收盘时已知的逐日快照，ROE 用百分点，情绪取 −1 到 1。模型将情绪作为输入数值，原始新闻需先整理为情绪快照。
- 持仓 CSV 要求列名为 `symbol,quantity,cost,bought_date,sector`。支持同标的多批次长仓和单一币种现金。核算基于共同估值日。入场分析只用买入交易日前一次收盘的均线状态，并报告可匹配数据的覆盖率。
- CSV 使用 UTF-8 和逗号分隔，日期使用 `YYYY-MM-DD`，数值使用小数点。行情和持仓成本需使用一致的价格口径。
- 策略使用 UTF-8 `.rule` 或 `.txt` 文件，包含 `NAME=`、`BUY=`、`SELL=`，允许字段、数值、比较、AND 和 OR。例 `BUY=CLOSE > MA20 AND CLOSE[1] <= MA20[1]`。含未来引用或未识别函数的文件会被拒绝。
- 回测支持单标的、整单位、仅做多和满仓入场，使用比例费率和滑点。基准按首日收盘归一化。持仓风格来自现有持仓，风险曲线指标按当前权重回看。更多口径见程序内「数据与声明」。
- 指标口径：RSI14 与 ATR14 用 Wilder 平滑；EMA 以首根收盘价为种子，序列前段与以 SMA 为种子的实现会有小幅差异；布林带用总体标准差（除以 20）；年化按 252 个交易日与样本标准差。跨平台比对前请先统一这些口径。
- 同一标的两根 K 线间隔超过 10 个自然日会计入缺口提示。程序不补停牌与缺失交易日，缺口处的 `RET1` 只是两端收盘价之差，会放大波动与 ATR。

## 代码结构

- `src/Terminal.cs`：窗口骨架、导航、页面切换、后台任务与设置存取
- `src/Terminal.Pages.cs`：五个工作页的布局与交互
- `src/Terminal.UI.cs`：按钮、表格、卡片等控件构件
- `src/Terminal.Verify.cs`：界面自测
- `src/Analytics.cs`：概率模型与滚动验证、组合分析、演示数据
- `src/Data.cs`：CSV 读取、指标计算、分位与统计
- `src/Strategy.cs`：规则解析、静态审计与回测
- `src/Plot.cs`、`src/FactorCharts.cs`：自绘图表与主题
- `src/Settings.cs`：界面状态持久化

`Terminal` 用 `partial` 拆成四个文件；`build.ps1` 按 `src/*.cs` 通配，新增源码文件不需要改工程。

## 设置与签名

界面状态（分位回看窗口、图表范围与类型、费率、滑点、资金、现金、窗口位置与最大化）保存在 `%LOCALAPPDATA%\PositionLab\settings.ini`，删除该文件即可恢复默认值。自检模式不读写该文件，避免污染本地配置。

仓库不保存私钥，签名材料需要自己准备：

```powershell
./scripts/new-dev-cert.ps1                                                   # 自签名开发证书，输出到 certs/（已忽略）
./build.ps1 -Pfx .\certs\dev-signing.pfx -PfxPassword positionlab -AllowUntrusted
```

自签名证书只证明签名链路可用，**不会被 Windows 信任，SmartScreen 仍会拦截**。正式分发需要 OV/EV 代码签名证书；在仓库 Secrets 里配置 `SIGNING_PFX_BASE64` 与 `SIGNING_PFX_PASSWORD`，CI 会自动签名，并把签名主体写进 Release 说明。

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

本机检查覆盖 CSV 往返和拒绝无效输入、指标与分位、滚动均线与定义式重算的一致性、未来引用拦截、追加未来数据后的回测不变性、成交及费用对账、滚动验证的折结构与无未来数据、缺口检测、设置文件往返与容错、持仓估值，以及 20 万条合成序列的指标和回测。界面自检覆盖五个工作页、滚动验证表格与最小窗口尺寸下的裁切检查。

CI 会验证同样的东西：推送到 `main` 或发起 PR 时用 `dotnet build` 与 `build.ps1` 双路径编译并跑无界面自检；推送 `v*` 标签时额外创建 Release，附带 `PositionLab.exe`、SHA-256、文件版本与签名状态。

## 免责声明与安全

仅供研究和教学。评分、回测和概率估计不构成投资建议、收益承诺或交易指令。模型和静态审计依赖输入数据的时点、复权口径和完整性，使用者需独立核验。没有券商连接或自动交易。输入和计算结果保存在当前进程内，需要留档时请显式导出，并妥善保管含持仓的文件。演示行情为合成随机序列，不含可预测信号，演示中的模型准确率接近随机水平属预期。

## License

MIT，见 [LICENSE](LICENSE)。
