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
- **Phase 7 导航性能 / 学习稳定版**：1 秒当前位置刷新 + 鲁棒 OCR 位置滤波；目标标记 OCR 降频到约 4 秒；重规划 4 秒冷却；路线匹配加入朝向评分；Road Graph A* 复用邻接表并加入转弯代价。
- **置信度学习模型**：每个“车型 + road edge”保存样本数、有效权重、均值、方差、置信度；单次异常对模型的影响被限制，多次稳定驾驶后才逐渐获得高权重。
- DeepSeek 现在读取本地学习置信度，只处理本地模型无法解释的残差异常；车型级 AI 置信度独立保存，低置信建议只弱影响或不影响路线。
- **Phase 6 高德式实时导航**：目的地锁定、路线匹配、路口级转向、动态剩余距离/ETA、分级语音提示、持续偏航后从当前位置自动重规划。
- 当前路线会区分“已走过 / 剩余”，HUD 显示大号方向箭头、下一动作距离、路线进度和实时偏差。
- 游戏目标标记可自动跟随；新目标需要连续两次 OCR 一致才切换，单次误读不会改变目的地；目标 OCR 暂时失败时继续使用上一次有效目的地。
- 偏航采用连续样本判定：约 45m 开始提示偏离，约 75m 连续 2 次才重规划；单次 OCR 跳点不会立即改路线。
- **本地路线自学习**：每次真实驾驶按“车型 + edge”计算实际速度与预期速度差，使用 EWMA 更新本地速度模型；下次 A* 和 ETA 立即使用该经验，无需等待 DeepSeek。
- DeepSeek 速度/风险学习与本地驾驶学习分层保存；两者融合，不重复相乘；清除 AI 学习不会删除真实驾驶经验。
- **Phase 5 AI 视觉导航**：可框选游戏内地图区域，将当前游戏地图截图与程序生成的 Road Graph 参考图做双图对照；DeepSeek 只能对当前路线的真实 edge token 给出 blocked / danger / uncertain / clear 结构化判断。
- 视觉判断是**临时证据层**：默认 8 分钟过期；只有 blocked / danger 且达到置信门槛才影响寻路，uncertain 不改变路线；真实驾驶通过同一 edge 会自动清除矛盾视觉风险。
- 可选实验模式：实时导航时每 3 分钟最多自动视觉检查一次，默认关闭。
- **Phase 4 车辆级导航**：不同车辆拥有不同 Primary / Secondary / Track / Bridge 通过效率；ETA 也按车型和道路类型修正。
- 路线偏好：Fastest / Shortest / Safe。
- **临时危险区**：可在当前位置快速标记 300m / 15min 危险圆；Safe/Fastest 会按车型风险容忍度调整路线，地图直接显示危险范围。
- 实时导航：位置持续投影到当前路线，按路口生成“左转 / 右转 / 左前方 / 右前方 / 掉头 / 到达”指令；持续偏航后自动从当前位置重规划；Windows TTS 分级播报。
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
4. 回到导航页，先点“从屏幕读取当前位置”和“读取游戏标记并导航”验证。
5. 可开启“自动跟随游戏目标标记”。在游戏内改变目标后，程序会在连续两次识别一致后锁定新目的地并重新规划。
6. 点“规划路线”或直接开启“实时导航”。HUD 会按路口显示下一动作、距离、剩余里程、动态 ETA 和路线进度。
7. “经济”页点“经济自主选择”，双击一个方案即可把该方案的目的地交给独立导航模块。
8. 在“导航”页选择实际车辆。不同车辆会对土路、支路、桥梁产生不同路径成本和 ETA。
9. 遇到临时危险时，可点击“当前位置危险 300m/15min”，路线会立即重算；可随时清除当前地图临时危险。
10. 进入“道路校准”页：
   - **自动分析当前地图**：分析地图道路概率，生成初始自动 Road Graph；重复执行只替换旧 auto 数据。
   - **手工点路**：选择道路类型后开启，在左侧地图沿真实道路依次点击；“断开下一段”用于切换到另一条不相连道路。
   - **实车学习**：开启后正常驾驶，程序继续只读已校准的屏幕坐标；停止后会自动过滤抖动/异常跳点、简化轨迹并合并到 Road Graph。
   - 即使不专门开启“实车学习”，正常实时导航也会记录路段级通过证据；样本足够强时自动道路会升级为 `trace/verified`。
10. 在“校准”页额外框选“游戏地图视觉区域”，尽量只包含地图本体。
11. DeepSeek 页可执行“AI视觉检查当前路线”。手动模式先展示结果，点“应用视觉风险并重算”后才影响路线；视觉证据默认 8 分钟过期。
12. 可选开启“实时导航时每 3 分钟自动视觉检查（实验）”。自动模式默认关闭。
13. DeepSeek 页仍可执行“AI分析导航经验”查看结构化建议；确认后点“应用高置信建议”。实验性自动学习默认关闭。

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


## Phase 5 AI 视觉导航

Phase 5 不让模型直接猜游戏坐标，而使用**双图对照 + 路段 token**：

1. 程序只截取用户在“校准”页框选的游戏地图区域。
2. 程序基于本地真实地图、当前 Road Graph 和当前路线生成第二张参考图。
3. 当前路线最多选取 24 条 Road Graph edge，并在参考图上标记为 `R01`、`R02`…。
4. DeepSeek 只能返回这些 token 的 `blocked` / `danger` / `uncertain` / `clear` 判断。
5. 程序把 token 映射回真实 edge ID，并再次验证 edge 是否存在。
6. 只有 `blocked` / `danger` 且置信度达到门槛的结果才进入临时视觉风险层；`uncertain` 永远不会直接改变路线。

视觉模型固定使用 `deepseek-flash`，因为当前 DeepSeek Vision API 支持通过 Chat Completions 发送 PNG/JPEG/WebP 图片。请求使用 Base64 内联图片和 JSON Output。

### 视觉证据与永久学习分离

视觉证据保存在：

```text
%LOCALAPPDATA%\WardogsNavigator\vision-edge-evidence.json
```

默认 TTL 为 8 分钟，最长只允许 30 分钟。它不会写入 Road Graph 的永久 `Risk`、`AiRiskAdjustment` 或道路几何。

如果实时 OCR 轨迹随后证明车辆确实沿某条被视觉标记的 edge 正常通过（足够采样、足够距离、低偏差），程序会自动删除该 edge 的临时视觉证据。真实驾驶事实优先于视觉推断。

### 上传内容边界

进行 AI 视觉检查时只发送：

- 用户校准的**游戏地图区域截图**
- 程序本地生成的**当前地图 + 当前路线参考图**
- 当前地图 ID
- 当前路线候选 edge token 与真实 edge ID 映射

不会因为视觉导航而上传整个桌面、游戏内存、进程数据、聊天历史、经济历史或 OCR 原始连续帧。

实验自动视觉模式默认关闭；开启后实时导航期间每 3 分钟最多调用一次视觉 API。


## Phase 6 高德 / Google Maps 式导航

Phase 6 的实时导航不再用“当前位置到最近路线点”的简单逻辑，而是持续进行 route matching：

```text
游戏当前位置 OCR
→ 投影到当前路线 polyline
→ 计算路线进度 / 偏差 / 剩余距离
→ 找到下一有效路口 maneuver
→ HUD + TTS
→ 连续偏航确认
→ 从当前真实位置重新 A*
→ 新路线继续匹配
```

转向角由相邻路线段的真实方位计算。小于约 22° 视为继续行驶，22–50° 为左/右前方，50–135° 为左/右转，大于约 135° 为掉头。HUD 会显示大号方向箭头，并按约 260m、80m、30m 三个距离层级进行语音提示。

### 偏航与重规划

为了避免 OCR 抖动导致路线频繁闪烁，偏航采用状态机而不是单点阈值：

- 路线偏差超过约 45m：HUD 标记偏航，但先继续确认。
- 路线偏差达到约 75m：累计偏航样本。
- 连续 2 个有效样本仍超过阈值：从当前坐标重新规划到原目标。
- 极端偏离约 180m：可直接进入重规划。
- 回到路线附近后偏航计数立即恢复。

每次重规划都会计入 `NavigationExperience.Replans`，后续可供本地学习和 DeepSeek 分析。

### 真实驾驶即时学习最快路线

每次导航对实际经过的 edge 统计真实距离和时间。程序根据：

```text
observedSpeed
/
(vehicleBaseSpeed × roadClassFactor)
```

得到该车型在该 edge 上的实际速度修正，并使用 EWMA 平滑更新 `LocalVehicleSpeedMultipliers`。下一次 Fastest A* 和 ETA 会直接使用这个结果。

本地真实驾驶层与 DeepSeek 层分离：

```text
LocalVehicleSpeedMultipliers   <- 实际驾驶事实
VehicleSpeedMultipliers        <- DeepSeek 结构化建议
AiRiskAdjustment               <- DeepSeek 风险建议
```

如果本地和 AI 都有速度建议，路由器按本地 72% / AI 28% 融合，而不是将两个倍率连续相乘。真实驾驶数据因此始终占主要权重。


## Phase 7 导航性能与模型学习优化

### 实时链路

实时导航刷新从约 1.4 秒调整为约 1 秒，但不再把 OCR 原始坐标直接送进导航。

当前位置处理链：

```text
OCR raw XY
→ 地图边界检查
→ 物理速度 / 大跳变门槛
→ 最近 3 个有效点的中值
→ 0.82 低延迟平滑
→ heading
→ route matching
→ HUD / reroute / learning
```

一次明显 OCR 数字跳变会被拒绝；如果真的发生大范围位置变化，则需要 3 个连续新样本后重新锚定，避免滤波器永久锁死在旧位置。

目标标记 OCR 不再每个实时 Tick 执行，而是约每 4 秒检查一次。当前位置仍保持约 1 秒刷新，因此导航响应更快，同时显著降低目标 OCR 的 CPU 占用。

偏航重规划增加约 4 秒冷却，避免车辆在路线边缘来回摆动时连续触发 A*。

### 路线搜索性能与自然度

Road Graph 的邻接表现在每次规划只构建一次，起点/终点吸附的 4 种组合复用同一份结构。

Fastest / Safe 搜索状态从：

```text
currentNode
```

升级为：

```text
previousNode + currentNode
```

因此路径成本可以计算真实转角。Shortest 仍然保持纯距离语义；Fastest / Safe 会对连续大角度转弯、掉头增加额外成本，避免为了少几十米选择大量小路拐弯。

路线匹配也加入当前 heading。交叉路口、回头路、相邻平行路段不再只比较几何距离，而是综合“距离 + 行驶方向一致性”。

### 本地速度学习置信度

每个 road edge / vehicle 现在维护：

```text
ObservationCount
EffectiveWeight
MeanMultiplier
Variance
Confidence
LastUpdatedUtc
```

单趟数据的权重由采样点数量、实际行驶距离和路线偏差共同决定。

单次新观测最多只能在现有均值附近有限幅度更新，避免停车、碰撞、战斗减速或 OCR 异常把整条道路永久学成“慢路”。

置信度会随着多次稳定观测上升；路由器使用：

```text
base road model
+ confidence-weighted real driving model
+ confidence-weighted DeepSeek model
```

而不是简单固定比例。真实驾驶最高权重高于 AI；低置信本地样本和低置信 AI 都会自动向基础道路模型收缩。

### DeepSeek 残差学习

DeepSeek 输入现在同时包含 `LocalVehicleSpeedLearning` 和车型级 AI confidence。

如果本地真实驾驶模型 confidence 已经很高，而最近多次行程没有出现持续残差异常，DeepSeek 的速度建议应接近 1.0，不再重复学习同一个速度差。

程序还会进行确定性后处理：

- 样本太少时限制 AI 最大 confidence。
- 高置信本地模型与单次 AI 大幅速度修正冲突时，AI confidence 被压低。
- 没有持续重规划 / 高偏航证据时，AI riskDelta 会被大幅衰减。
