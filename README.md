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
- **v0.10.0 UI / AI / 性能 / 兼容性升级**：统一中文深色 UI、顶部运行状态栏、低占用/平衡/实时三档性能模式；窗口采集支持自动 / 屏幕拷贝 / PrintWindow 兼容后端，可将 OBS Projector/预览窗口作为独立采集源。
- DeepSeek 客户端复用 HTTP 连接，增加超时与 429/5xx/JSON 空响应重试；AI 视觉增加近似画面指纹缓存，重复路线/近似地图画面不再重复调用 API。
- 目标图标检测从逐像素 GetPixel 改为批量像素读取，降低高分辨率地图扫描的 CPU/GDI 开销。
- 导航偏航阈值会根据当前实际速度自适应，高速行驶时减少 OCR/位置延迟造成的误重规划；低占用模式关闭预测路线预热。
- HUD 改为不抢焦点、鼠标穿透，减少和游戏/OBS 前台窗口冲突。
- **Phase 9 旋转地图与预测重规划**：视觉配准升级为平移 + 缩放 + 任意角度旋转；旋转视口、目标像素→世界坐标、视觉记忆和地图 UI 全部使用同一变换矩阵。
- 视觉目标扫描与 1 秒导航 Tick 完全解耦；第一次全图旋转搜索不会暂停当前位置、HUD、偏航判断。
- Road Graph 缓存拓扑和节点到节点核心 A* 路径；缓存键包含道路版本、车型、路线策略、临时危险和视觉风险，环境变化自动失效。
- 主路线生成后会预热约 28% / 52% / 74% 位置到同一目的地的核心路径，为后续偏航重规划提前准备。
- **Phase 8 游戏地图视觉目标导航**：本地多尺度地图配准 + 目标图标视觉校准/检测 + 像素→世界 X/Y 转换；视觉成功时不依赖目标坐标文字 OCR，失败时自动回退到现有 OCR。
- 每张地图保存最后一次成功视口、配准成功/失败次数、EWMA 置信度和上一次视觉目标，作为下一次局部搜索提示，形成长期视觉导航记忆。
- 程序地图会用青色虚线显示当前游戏地图视觉视口，并用紫色十字显示视觉目标与置信度，便于人工验证配准是否正确。
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


## Phase 8 游戏地图自动视觉配准与目标图标导航

Phase 8 把目标获取链路从“主要依赖坐标文字 OCR”升级为“本地视觉优先，OCR 兜底”。

实时目标链：

```text
游戏地图截图
→ 本地地图视觉配准
→ 目标图标检测
→ 屏幕像素
→ 当前地图视口变换
→ 世界 X/Y
→ 目标连续样本确认
→ A* / HUD / TTS
```

如果视觉配准或目标图标检测置信度不足，则自动回退：

```text
目标坐标文字 OCR
→ 世界 X/Y
→ 导航
```

因此 Phase 8 不会让旧的目标坐标读取能力失效。

### 第一次目标图标校准

1. 在游戏中打开当前地图并放一个目标标记。
2. 在“校准”页先框选“游戏地图视觉区域”，尽量只包含地图本体。
3. 点击“校准目标标记图标”。
4. 程序显示当前游戏地图截图；直接点击目标标记图标中心。
5. 程序在本地学习该标记周围高饱和度像素的 Hue / Saturation / Value 特征和 RGB 样本。
6. 校准完成后自动开启“优先使用地图视觉识别目标标记”。

目标图标特征保存在本地 settings，不需要上传到 DeepSeek。

### 本地地图视觉配准

程序使用当前本地真实地图作为基准，针对游戏地图截图执行多尺度视口搜索。

未知量包括：

```text
viewport left
viewport top
viewport width
viewport height
```

也就是允许游戏地图处于不同平移/缩放状态，只要地图保持相同方向即可。

算法会先把基准图和游戏截图缩小生成边缘特征图，然后使用归一化相关性搜索最匹配的地图裁剪区域。

如果该地图过去已有成功配准记忆，下一次优先在上次视口附近进行局部搜索；局部结果不足才退回全图多尺度搜索。

这让长期使用时大多数识别不需要每次重新扫描整张地图。

### 视觉导航记忆

视觉记忆保存在：

```text
%LOCALAPPDATA%\WardogsNavigator\visual-map-memory.json
```

每张地图独立保存：

- 上一次成功 `MapViewportRegistration`
- 成功配准次数
- 失败配准次数
- 配准 confidence 的 EWMA
- 上一次成功视觉目标坐标
- 上一次目标置信度
- 更新时间

重新框选“游戏地图视觉区域”时，当前地图旧视觉记忆会自动清除，避免旧视口污染新校准。

### 目标图标检测

目标图标检测完全本地执行。

程序根据第一次点击样本建立 HSV / RGB profile，然后对游戏地图截图进行颜色候选分割和连通域分析。

候选评分同时考虑：

- 色相相似度
- 饱和度
- 亮度
- 连通区域大小
- 图标紧凑度
- 与上一次目标位置的空间距离

已有目的地时，上一个世界目标会通过当前视觉配准投影回屏幕，作为候选优先级，而不是直接强制锁定，因此仍可以切换新目标。

### 可视化验证

主程序地图会显示：

- 青色虚线矩形：当前识别到的游戏地图视口
- `VIS xx%`：地图配准 confidence
- 紫色十字：视觉识别目标
- “视觉目标 xx%”：目标识别 confidence

如果青色视口明显对不上游戏当前地图，应重新框选视觉区域或在游戏中减少菜单/文字对地图主体的遮挡。

### 与 DeepSeek 的关系

Phase 8 的实时目标定位不依赖 DeepSeek API。

DeepSeek 继续负责：

- Phase 5 当前路线视觉异常判断
- Phase 7 导航经验残差分析
- 高层风险/速度学习建议

地图配准、目标图标识别、像素坐标转换和实时目标更新均在本机执行，因此不会因为 API 延迟阻塞目标导航。


## Phase 9 旋转视觉配准与预测重规划

### 任意角度地图旋转

视觉视口现在包含：

```text
left
top
width
height
rotationDeg
```

其中 `rotationDeg` 表示游戏截图坐标轴相对真实地图坐标轴的旋转角。

所有坐标链统一使用同一个仿射变换：

```text
screen pixel
→ normalized screen coordinate
→ scale by viewport width/height
→ rotate around viewport center
→ real map normalized coordinate
→ WARDOGS world X/Y
```

反向投影同样支持旋转，因此“上一次目标位置 → 当前游戏截图像素”也能在旋转地图下继续作为目标候选空间先验。

程序地图上的青色 `VIS` 框不再是轴对齐矩形，而会按照真实识别角度旋转，并显示：

```text
VIS 87% · +31°
```

### 旋转搜索策略

为控制 CPU，程序不会为每个角度生成新的旋转 Bitmap。

它直接在缩小后的边缘特征图上改变采样矩阵：

1. 有历史视觉记忆时，优先搜索上次视口附近的平移、缩放和约 ±12° 角度范围。
2. 局部结果不足时进入全局搜索。
3. 全局角度以约 30° 为种子扫描，并保留多个高分候选，而不是只相信第一个局部最优。
4. 多候选分别经过多级位置 / 缩放 / 旋转细化，再用更高采样密度进行最终复核。
5. 地图采样使用双线性插值，减少旋转后的锯齿误差。

### 实时导航与视觉线程解耦

Phase 8 的视觉目标扫描原本发生在实时导航 Tick 内。第一次全局视觉配准耗时较长时，会拖慢 1 秒位置更新。

Phase 9 改为：

```text
1s player OCR / HUD / deviation loop
          │
          ├── navigation continues immediately
          │
          └── independent single-flight target visual scan
                    ↓
              confirmed new target
                    ↓
                replan route
```

自动视觉目标扫描有独立取消令牌和超时。切换地图、关闭程序或启动新视觉任务时会取消旧任务。

一次视觉失败仍然保持上一次已确认目的地，不会中断当前导航。

### Road Graph 核心路径缓存

RoadGraphRouter 现在缓存两层数据：

- 拓扑缓存：nodes / usable edges / adjacency
- 核心 A* 路径缓存：road-node → road-node

核心路径缓存键包含：

- RoadGraph `UpdatedUtc`
- 节点/边数量
- Fastest / Safe / Shortest
- vehicle profile ID
- 当前有效临时危险签名
- 当前视觉 edge risk 签名

因此道路学习、道路编辑、危险变化或视觉风险变化后都会自动进入不同缓存空间，不会复用旧风险环境中的路径。

### 预测重规划预热

主路线完成后，如果它是真实 Road Graph 路线，程序会在后台尝试预热路线约：

```text
28%
52%
74%
```

三个前方位置到同一目的地的核心 Road Graph 路径。

预测预热是机会性的：

- 新规划会取消旧预热。
- 切地图会取消旧预热。
- 预热失败不会影响前台导航。
- 主导航不等待预热完成。

偏航发生在已预热区域附近时，重新规划能复用大量 node-to-node A* 结果。


## v0.10.0 UI / AI / 性能 / 游戏兼容性

### 统一 UI 与运行状态

主窗口顶部新增常驻状态栏，集中显示：

```text
IDLE / LIVE / LEARN
性能模式
CPU %
工作集内存
当前采集后端 + 最近一次采集耗时
路线缓存命中
AI视觉缓存命中
```

界面继续保持中文，并统一输入框、按钮、表格、标签页的深色样式。

HUD 使用 `WS_EX_NOACTIVATE + WS_EX_TRANSPARENT`：显示导航信息时不抢游戏焦点，并允许鼠标事件穿过 HUD。

### 三档性能模式

校准页新增：

```text
低占用
平衡
实时
```

行为差异：

- 低占用：实时位置 Tick 约 1.5 秒；视觉目标扫描最慢约 5 秒；不进行预测路线预热；自动 AI 视觉检查间隔放宽。
- 平衡：默认约 1 秒 Tick，保留预测路线预热。
- 实时：约 0.7 秒 Tick，视觉目标扫描可缩短到约 2–3 秒。

模式只改变执行频率/后台机会任务，不改变经济公式、道路数据或安全边界。

### OBS / 游戏窗口采集兼容

采集源和游戏窗口标题现在分离。

```text
游戏窗口：WARDOGS
采集源：可留空，或填写 OBS Projector / 预览窗口标题
```

支持三种后端：

- 自动：优先低延迟屏幕拷贝，发现明显黑帧/失败时自动尝试 PrintWindow。
- 屏幕拷贝：使用 CopyFromScreen，适合普通窗口化/无边框游戏。
- PrintWindow兼容：请求 Windows 直接绘制窗口客户区，可用于部分 OBS Projector / 预览窗口和被遮挡窗口场景。

校准页提供“测试采集源”，直接显示捕获分辨率、实际后端和耗时。

不同后端受游戏渲染 API、独占全屏、硬件加速和 OBS 窗口类型影响；无法保证所有 DirectX/反作弊/独占全屏组合都能由 PrintWindow 正确返回画面，因此自动模式仍保留屏幕拷贝作为主路径。

### AI 模型与识别优化

当前官方推荐视觉/快速模型继续使用：

```text
deepseek-flash
```

程序会自动把旧的 `deepseek-v4-flash` / `deepseek-v4-flash-vision-exp` 模型名映射到 `deepseek-flash`。

DeepSeek 客户端改为单例 HTTP 连接池，避免每次 AI 分析重新建立连接；增加：

- 45 秒请求超时
- 429 / 5xx 一次退避重试
- JSON Mode 空内容一次重试
- 自动 AI 导航学习固定使用 Flash，手动分析仍尊重用户模型选择

AI 路线视觉新增 dHash 画面指纹缓存。地图画面变化非常小、候选路线 edge 集合不变且缓存仍有效时，直接复用最近一次结构化结果，不再次发送图片。

低占用模式自动采用低细节视觉请求；其它模式保持高细节。

AI 导航学习只发送更强的路段观测样本，削减低采样/极短路段噪声和冗余上下文。

### 本地图像识别占用优化

目标图标检测以前对缩放后的整张地图逐像素调用 `Bitmap.GetPixel`。

0.10.0 改为：

```text
Bitmap
→ Format24bppRgb
→ LockBits / Marshal.Copy 批量读取
→ HSV / RGB 候选筛选
→ 连通域分析
```

在大地图视觉区域下可显著减少 GDI 调用和 CPU 开销。

### 导航模型优化

偏航不再只使用固定距离阈值。

0.10.0 在原有连续样本状态机上加入速度自适应：

- 低速仍保持较严格的道路匹配和偏航阈值。
- 速度越高，警告/重规划阈值会有限增加，吸收 OCR 采样延迟和车辆一秒内更大的真实位移。
- 阈值设置有上限，不会因为高速而无限放宽。
- 实时路线仍保留持续偏航样本确认和极端偏离快速重规划。

Road Graph 的路径缓存与 Phase 9 预测预热继续保留；状态栏可直接观察路线缓存命中效果。
