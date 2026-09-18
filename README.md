# WARDOGS Tactical Navigator

一个从零重做的 Windows 10/11 x64 WARDOGS 综合运输 / 导航 / 火控助手。

**安全边界：只读取屏幕像素，不读取游戏内存，不注入，不 Hook，不安装驱动。**

## 当前功能

- 游戏窗口区域校准：当前位置坐标区域 / 目标坐标区域按窗口比例保存，兼容 1080p / 1440p / 4K。
- 本地 OCR：Tesseract 只识别坐标数字；首次使用会下载公开的 `eng.traineddata`。
- 真实地图：缓存 `wardogs-fdc-web` 中的 Bakurani / Ozeti / Zestafona WebP。
- **Phase 3 自动道路识别**：首次进入地图时自动分析真实地图像素，生成初始 Road Graph；自动边带置信度且默认为未验证。
- **Phase 2 精确 Road Graph**：手工点路 + 实车轨迹学习，生成长期保存的道路节点/路口/道路边。
- 地面导航优先级：**已验证/实车 Road Graph → 自动 Road Graph → 真实地图像素 A* → 直线回退**；飞机使用直线。
- Road Graph 起终点会投影/吸附到最近道路边，再在道路网络内部 A*。
- 道路类型：Primary / Secondary / Track / Bridge；可记录封路、风险、验证状态和实车通过次数。
- **Phase 4 车辆级导航**：不同车辆拥有不同 Primary / Secondary / Track / Bridge 通过效率；ETA 也按车型和道路类型修正。
- 路线偏好：Fastest / Shortest / Safe。
- **临时危险区**：可在当前位置快速标记 300m / 15min 危险圆；Safe/Fastest 会按车型风险容忍度调整路线，地图直接显示危险范围。
- 实时导航：当前位置变化后更新 HUD；偏离路线约 150m 自动重新规划；Windows TTS 播报。
- **普通导航也会本地自学习**：OCR 轨迹会按 Road Graph 路段归属，记录通过次数；强实车证据可把 auto 道路自动升级为 trace/verified。
- 经济系统：与路线系统完全分离。经济层自己选择车辆、目的地、载荷、单程/往返；路线层只负责“怎么去”。
- 火控基础：距离、方位角、0–6400 方向密位。
- **DeepSeek 导航学习**：本地先聚合具体路段/车型的实际距离、耗时、观测速度、偏航和重规划数据，再用 JSON Output 让 DeepSeek 返回结构化路段建议。
- AI 学习写回是可撤销的独立层：车型速度修正、AI 风险修正与原始道路数据分开保存；默认不自动应用。
- 可选实验模式：至少累计 3 次完成导航后，每新增 3 次完成导航才调用一次 DeepSeek，并只自动应用置信度 ≥ 82% 的建议。
- API Key 使用 Windows DPAPI 按当前用户加密保存；断网/无 Key 时 OCR、经济、道路学习、导航和火控仍然工作。

## 架构

```text
Screen Capture -> Local OCR -> Current/Target XY
                         |
                         +-> EconomyEngine ---------> selected vehicle/destination/load
                         |        (deterministic)
                         |
                         +-> RoutePlanner ----------> vehicle/risk-aware Road Graph -> HUD/TTS
                         |        (independent)
                         |
                         +-> Local Learning ---------> edge traversal / speed / deviation evidence
                         |                                  |
                         |                                  +-> DeepSeek JSON learning suggestions
                         |                                               |
                         |                                               +-> reversible AI road layer
                         |
                         +-> FireControl -----------> range / azimuth / direction mils
```

**经济最优不等于路线最短。**  
代码中 `EconomyEngine` 不依赖 `RoutePlanner` 或 AI。

## DeepSeek

程序调用官方 OpenAI-compatible API：

- Base URL: `https://api.deepseek.com`
- Chat endpoint: `/chat/completions`
- 默认模型：`deepseek-flash`
- 可选：`deepseek-v4-pro`

API Key 不提交仓库。

## 构建

```powershell
dotnet restore WardogsNavigator.csproj
dotnet test tests/WardogsNavigator.Tests/WardogsNavigator.Tests.csproj -c Release
dotnet publish WardogsNavigator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

然后运行：

```text
publish\WardogsNavigator.exe
```

## 第一次使用

1. WARDOGS 使用窗口化或无边框。
2. 打开“校准”，确认窗口标题关键字。
3. 分别框选游戏 UI 中的“当前位置坐标”和“目标标记坐标”区域。
4. 回到导航页，先点“从屏幕读取当前位置/目标”验证。
5. 点“规划路线”；需要连续导航时开启“实时导航”。
6. “经济”页点“经济自主选择”，双击一个方案即可把该方案的目的地交给独立导航模块。
7. 在“导航”页选择实际车辆。不同车辆会对土路、支路、桥梁产生不同路径成本和 ETA。
8. 遇到临时危险时，可点击“当前位置危险 300m/15min”，路线会立即重算；可随时清除当前地图临时危险。
9. 进入“道路校准”页：
   - **自动分析当前地图**：分析地图道路概率，生成初始自动 Road Graph；重复执行只替换旧 auto 数据。
   - **手工点路**：选择道路类型后开启，在左侧地图沿真实道路依次点击；“断开下一段”用于切换到另一条不相连道路。
   - **实车学习**：开启后正常驾驶，程序继续只读已校准的屏幕坐标；停止后会自动过滤抖动/异常跳点、简化轨迹并合并到 Road Graph。
   - 即使不专门开启“实车学习”，正常实时导航也会记录路段级通过证据；样本足够强时自动道路会升级为 `trace/verified`。
10. DeepSeek 页可执行“AI分析导航经验”查看结构化建议；确认后点“应用高置信建议”。实验性自动学习默认关闭。

## 路线精度说明

道路识别目前是**真实地图像素驱动的道路偏好栅格**，不是官方 navmesh。地图版本或画面风格变化后，需要继续调道路像素权重或导入人工校准道路图；程序在地图加载失败时会明确标记“直线回退”，不会把回退路线伪装成道路导航。


## Phase 2 Road Graph 数据

每张地图的 Road Graph 保存在当前 Windows 用户目录：

```text
%LOCALAPPDATA%\WardogsNavigator\roadgraphs\
```

文件为普通 JSON。可以备份、版本管理或在不同电脑之间复制。

道路颜色：

- 青色：Primary 主路
- 蓝色：Secondary 支路
- 灰色：Track 土路/小路
- 橙色：Bridge/关键穿越
- 红色虚线：Blocked 禁行
- 紫色小点：道路节点/路口

实车学习只使用屏幕 OCR 得到的坐标轨迹，不访问游戏内存。OCR 短距离抖动会被忽略，大幅异常跳点会被拒绝，最终轨迹通过 RDP 简化后再写入道路网。


## Phase 3 自动道路识别

自动识别不会把所有“像道路的颜色”直接当成真道路。处理流程：

1. 将真实地图缩放到分析栅格。
2. 对每个像素计算道路概率，降低水域、深色区域权重。
3. 做局部平滑，减少文字、标记和噪点影响。
4. 分块寻找道路中心候选点，避免生成数万节点。
5. 只连接邻近且整条连线道路概率达到阈值的候选节点。
6. 自动边记录 `AutoScore`，默认 `Verified=false`。
7. 手工道路和实车轨迹永远优先且不会被“重新自动分析”覆盖。

地图显示中，自动道路使用较细的虚线，透明度随识别置信度变化；实车/手工验证道路使用更实、更醒目的道路线。


## Phase 4 车辆 / 风险 / AI 自学习

Phase 4 把导航成本从“道路长度”升级为：

```text
道路距离
× 车型道路效率
× 该车型该路段的已学习速度修正
× 道路可信度
× 静态风险
× 临时危险区风险
```

不同车辆可因此得到不同 Fastest/Safe 路线。乌拉尔在 Track 上的基础效率明显低于越野车型，而飞机仍使用直接航线。

### 本地导航经验

实时导航会保存到：

```text
%LOCALAPPDATA%\WardogsNavigator\learning\
```

每次导航可记录：

- 计划/实际距离与耗时
- 重规划次数
- 最大偏航
- 实际经过的 Road Graph edge
- 每条 edge 的采样数、实际行驶距离、观测秒数、观测速度、最大路段偏差

这些数据先在本地聚合，DeepSeek 不会收到无关 OCR 原始画面或完整屏幕内容。

### DeepSeek 学习安全规则

- 使用 `response_format: {"type":"json_object"}` 获取结构化 JSON。
- 模型只能引用输入中真实存在的 `edgeId` 与 `vehicleId`。
- `speedMultiplier` 会限制在 0.55–1.25。
- AI 风险修正限制在 -0.20–0.20，并保存为独立 `AiRiskAdjustment`，不会覆盖基础 `Risk`。
- 默认人工应用门槛为 72%；实验自动学习门槛为 82%。
- “清除 AI 路段学习”可以完全移除 AI 速度/风险修正，不影响手工道路、自动道路或真实行驶验证结果。
- AI 不会直接修改经济公式，也不会生成新的道路坐标；道路几何只能来自地图自动提取、手工点路或真实 OCR 驾驶轨迹。
