# WARDOGS Tactical Navigator

一个从零重做的 Windows 10/11 x64 WARDOGS 综合运输 / 导航 / 火控助手。

**安全边界：只读取屏幕像素，不读取游戏内存，不注入，不 Hook，不安装驱动。**

## 当前功能

- 游戏窗口区域校准：当前位置坐标区域 / 目标坐标区域按窗口比例保存，兼容 1080p / 1440p / 4K。
- 本地 OCR：Tesseract 只识别坐标数字；首次使用会下载公开的 `eng.traineddata`。
- 真实地图：缓存 `wardogs-fdc-web` 中的 Bakurani / Ozeti / Zestafona WebP。
- 地面导航：从真实地图像素生成道路偏好代价栅格，并用 A* 规划；飞机使用直线。
- 路线偏好：Fastest / Shortest / Safe。
- 实时导航：当前位置变化后更新 HUD；偏离路线约 150m 自动重新规划；Windows TTS 播报。
- 经济系统：与路线系统完全分离。经济层自己选择车辆、目的地、载荷、单程/往返；路线层只负责“怎么去”。
- 火控基础：距离、方位角、0–6400 方向密位。
- DeepSeek：程序内提供 Key 输入、模型选择、测试连接、AI 解释当前经济与路线方案。Key 用 Windows DPAPI 按当前用户加密保存。
- DeepSeek 只是辅助层：断网/无 Key 时 OCR、经济、路线和火控仍然工作。

## 架构

```text
Screen Capture -> Local OCR -> Current/Target XY
                         |
                         +-> EconomyEngine ---------> selected vehicle/destination/load
                         |        (deterministic)
                         |
                         +-> RoutePlanner ----------> real-map raster A* -> Navigation HUD/TTS
                         |        (independent)
                         |
                         +-> FireControl -----------> range / azimuth / direction mils
                         |
                         +-> DeepSeekClient --------> explanation / advice only
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
7. 可选：在 DeepSeek 页填 Key，测试连接后让 AI 解释当前方案。

## 路线精度说明

道路识别目前是**真实地图像素驱动的道路偏好栅格**，不是官方 navmesh。地图版本或画面风格变化后，需要继续调道路像素权重或导入人工校准道路图；程序在地图加载失败时会明确标记“直线回退”，不会把回退路线伪装成道路导航。
