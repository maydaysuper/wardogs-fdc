using System.Diagnostics;
using System.Drawing;
using System.Speech.Synthesis;
using WardogsNavigator.Services;
using WardogsNavigator.UI;

namespace WardogsNavigator;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly MapAssetService _mapAssets = new();
    private readonly MapCatalog _maps = new();
    private readonly EconomyEngine _economy = new();
    private readonly RoadGraphStore _roadGraphs = new();
    private readonly NavigationHazardStore _hazards = new();
    private readonly NavigationVisionEvidenceStore _visionEvidence = new();
    private readonly NavigationExperienceStore _experiences = new();
    private readonly NavigationMapMemoryStore _visualMapMemory = new();
    private readonly NavigationLearningSession _navigationLearning = new();
    private readonly NavigationGuidanceTracker _guidance = new();
    private readonly TraceLearningService _traceLearning = new();
    private readonly AutoRoadExtractor _autoRoadExtractor;
    private readonly AiNavigationLearningService _aiNavigationLearning;
    private readonly AiVisionNavigationService _aiVision;
    private readonly MapVisualRegistrationService _mapVisualRegistration;
    private readonly TargetMarkerDetector _targetMarkerDetector = new();
    private readonly RoutePlanner _routes;
    private readonly GameWindowCapture _capture = new();
    private readonly CoordinateRecognizer _ocr = new();
    private readonly DeepSeekClient _ai = new();
    private readonly SpeechSynthesizer _tts = new();
    private readonly OverlayForm _overlay = new();

    private readonly MapCanvas _mapCanvas = new();
    private readonly ComboBox _map = new();
    private readonly ComboBox _routeMode = new();
    private readonly ComboBox _navVehicle = new();
    private readonly CheckBox _autoTargetCheck = new();
    private readonly CheckBox _visualTargetCheck = new();
    private readonly TextBox _selfX = new();
    private readonly TextBox _selfY = new();
    private readonly TextBox _targetX = new();
    private readonly TextBox _targetY = new();
    private readonly Label _routeSummary = new();
    private readonly Label _fireSummary = new();
    private readonly DataGridView _economyGrid = new();
    private readonly TextBox _apiKey = new();
    private readonly ComboBox _model = new();
    private readonly TextBox _aiOutput = new();
    private readonly CheckBox _aiAutoApplyLearning = new();
    private readonly CheckBox _aiAutoVisionScan = new();
    private readonly TextBox _windowTitle = new();
    private readonly Label _calibrationStatus = new();
    private readonly Button _liveButton = new();
    private readonly ComboBox _roadClass = new();
    private readonly Label _roadGraphStatus = new();
    private readonly Button _roadEditButton = new();
    private readonly Button _roadLearnButton = new();
    private readonly Button _autoRoadButton = new();
    private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 1000 };
    private readonly NavigationPositionFilter _positionFilter = new();

    private RoutePlan? _route;
    private EconomicPlan? _selectedEconomicPlan;
    private RoadGraph _currentRoadGraph = new();
    private bool _roadEditMode;
    private string? _roadEditPreviousNodeId;
    private MapPoint? _lastLivePoint;
    private MapPoint? _pendingTargetPoint;
    private int _pendingTargetSamples;
    private double? _headingDeg;
    private bool _live;
    private DateTime _lastSpoken = DateTime.MinValue;
    private int _lastCueIndex = -1;
    private string _lastSpokenCueKey = "";
    private CancellationTokenSource? _planCts;
    private CancellationTokenSource? _predictionCts;
    private CancellationTokenSource? _autoRoadCts;
    private string? _autoExtractMapId;
    private AiNavigationLearningReport? _lastAiLearningReport;
    private AiVisionNavigationReport? _lastVisionReport;
    private bool _aiLearningBusy;
    private bool _visionBusy;
    private bool _targetScanBusy;
    private bool _liveTickBusy;
    private DateTime _lastAutoVisionScan = DateTime.MinValue;
    private DateTime _lastTargetOcrUtc = DateTime.MinValue;
    private DateTime _lastRerouteUtc = DateTime.MinValue;
    private MapViewportRegistration? _lastMapRegistration;
    private VisualTargetDetection? _lastVisualTargetDetection;
    private CancellationTokenSource? _visionCts;
    private CancellationTokenSource? _targetScanCts;

    public MainForm()
    {
        _routes = new RoutePlanner(
            _mapAssets,
            _roadGraphs,
            _hazards,
            _visionEvidence);
        _autoRoadExtractor = new AutoRoadExtractor(_mapAssets);
        _aiNavigationLearning = new AiNavigationLearningService(_ai);
        _aiVision = new AiVisionNavigationService(_ai, _mapAssets);
        _mapVisualRegistration =
            new MapVisualRegistrationService(
                _mapAssets);

        Text = "WARDOGS Tactical Navigator";
        Width = 1420;
        Height = 860;
        MinimumSize = new Size(1100, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 20, 24);
        ForeColor = Color.Gainsboro;

        BuildUi();
        LoadSettingsIntoUi();
        WireEvents();

        Shown += async (_, _) => await SwitchMapAsync();
        FormClosed += (_, _) =>
        {
            _liveTimer.Stop();
            _ocr.Dispose();
            _mapAssets.Dispose();
            _tts.Dispose();
            _overlay.Close();
            _planCts?.Cancel();
            _predictionCts?.Cancel();
            _autoRoadCts?.Cancel();
            _visionCts?.Cancel();
            _targetScanCts?.Cancel();
        };
    }

    private void BuildUi()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = BackColor,
            Panel1MinSize = 500,
            Panel2MinSize = 360
        };
        Controls.Add(split);
        split.SplitterDistance = Math.Max(500, Math.Min(ClientSize.Width - 380, 860));

        _mapCanvas.Dock = DockStyle.Fill;
        split.Panel1.Controls.Add(_mapCanvas);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        split.Panel2.Controls.Add(tabs);

        tabs.TabPages.Add(MakeNavigationTab());
        tabs.TabPages.Add(MakeEconomyTab());
        tabs.TabPages.Add(MakeRoadGraphTab());
        tabs.TabPages.Add(MakeAiTab());
        tabs.TabPages.Add(MakeCalibrationTab());
    }

    private TabPage MakeNavigationTab()
    {
        var tab = NewTab("导航");
        var p = Flow();

        _map.DropDownStyle = ComboBoxStyle.DropDownList;
        _map.Items.AddRange(new object[] { "bakurani", "ozeti", "zestafona" });
        _map.Width = 360;

        _routeMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _routeMode.Items.AddRange(Enum.GetNames<RoutePreference>());
        _routeMode.Width = 360;

        _navVehicle.DropDownStyle = ComboBoxStyle.DropDownList;
        _navVehicle.Width = 360;
        _navVehicle.Items.Clear();
        _navVehicle.Items.Add(new NavVehicleItem(null, "通用地面车辆"));
        foreach (var vehicle in _economy.Vehicles)
            _navVehicle.Items.Add(new NavVehicleItem(vehicle, vehicle.NameZh));
        _navVehicle.SelectedIndex = 0;

        p.Controls.Add(Header("地图 / 路线"));
        p.Controls.Add(_map);
        p.Controls.Add(_routeMode);
        p.Controls.Add(Header("导航车辆"));
        p.Controls.Add(_navVehicle);

        p.Controls.Add(Header("当前位置 X / Y"));
        p.Controls.Add(CoordRow(_selfX, _selfY));

        p.Controls.Add(Header("目标 X / Y"));
        p.Controls.Add(CoordRow(_targetX, _targetY));

        _autoTargetCheck.Text = "自动跟随游戏目标标记";
        _autoTargetCheck.AutoSize = true;
        _autoTargetCheck.ForeColor = Color.Gainsboro;
        _autoTargetCheck.CheckedChanged += (_, _) =>
        {
            _settings.AutoReadTarget =
                _autoTargetCheck.Checked;
            _settings.Save();
        };
        p.Controls.Add(_autoTargetCheck);

        _visualTargetCheck.Text =
            "优先使用地图视觉识别目标标记";
        _visualTargetCheck.AutoSize = true;
        _visualTargetCheck.ForeColor = Color.Gainsboro;
        _visualTargetCheck.CheckedChanged += (_, _) =>
        {
            _settings.VisualTargetNavigationEnabled =
                _visualTargetCheck.Checked;
            _settings.Save();
        };
        p.Controls.Add(_visualTargetCheck);

        var buttons = new FlowLayoutPanel
        {
            Width = 410,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight
        };

        var plan = Btn("规划路线");
        plan.Click += async (_, _) => await PlanRouteAsync(true);

        _liveButton.Text = "开始实时导航";
        _liveButton.AutoSize = true;
        _liveButton.BackColor = Color.FromArgb(42, 48, 58);
        _liveButton.ForeColor = Color.White;
        _liveButton.FlatStyle = FlatStyle.Flat;
        _liveButton.Click += (_, _) => ToggleLive();

        var hud = Btn("HUD");
        hud.Click += (_, _) =>
        {
            if (_overlay.Visible) _overlay.Hide();
            else _overlay.Show();
        };

        buttons.Controls.Add(plan);
        buttons.Controls.Add(_liveButton);
        buttons.Controls.Add(hud);
        p.Controls.Add(buttons);

        var hazardButtons = new FlowLayoutPanel
        {
            Width = 410,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight
        };

        var addHazard = Btn("当前位置危险 300m/15min");
        addHazard.Click += async (_, _) =>
        {
            if (!TryPoint(_selfX, _selfY, out var current))
            {
                MessageBox.Show("请先读取当前位置。");
                return;
            }

            _hazards.Add(
                _map.Text,
                current,
                300,
                0.80,
                TimeSpan.FromMinutes(15),
                "临时危险区",
                "manual");

            UpdateHazards();
            await PlanRouteAsync(false);
        };

        var clearHazards = Btn("清除临时危险");
        clearHazards.Click += async (_, _) =>
        {
            _hazards.ClearMap(_map.Text);
            UpdateHazards();
            await PlanRouteAsync(false);
        };

        hazardButtons.Controls.Add(addHazard);
        hazardButtons.Controls.Add(clearHazards);
        p.Controls.Add(hazardButtons);

        _routeSummary.Width = 400;
        _routeSummary.Height = 78;
        _routeSummary.ForeColor = Color.FromArgb(160, 235, 180);
        _routeSummary.Text = "路线：—";
        p.Controls.Add(_routeSummary);

        _fireSummary.Width = 400;
        _fireSummary.Height = 64;
        _fireSummary.ForeColor = Color.FromArgb(240, 210, 120);
        _fireSummary.Text = "火控：—";
        p.Controls.Add(_fireSummary);

        var readSelf = Btn("从屏幕读取当前位置");
        readSelf.Click += async (_, _) => await ReadPlayerOnceAsync();

        var readTarget = Btn("读取游戏标记并导航");
        readTarget.Click += async (_, _) => await ReadTargetOnceAsync();

        p.Controls.Add(readSelf);
        p.Controls.Add(readTarget);

        tab.Controls.Add(p);
        return tab;
    }

    private TabPage MakeEconomyTab()
    {
        var tab = NewTab("经济");
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            BackColor = BackColor
        };
        tab.Controls.Add(host);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = BackColor
        };

        var run = Btn("经济自主选择");
        run.Click += (_, _) => RunEconomy();

        var use = Btn("使用选中方案并导航");
        use.Click += async (_, _) => await UseSelectedEconomicPlanAsync();

        top.Controls.Add(run);
        top.Controls.Add(use);
        host.Controls.Add(top);

        _economyGrid.Dock = DockStyle.Fill;
        _economyGrid.BackgroundColor = Color.FromArgb(26, 29, 35);
        _economyGrid.ForeColor = Color.Black;
        _economyGrid.ReadOnly = true;
        _economyGrid.AutoGenerateColumns = false;
        _economyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _economyGrid.MultiSelect = false;
        _economyGrid.RowHeadersVisible = false;

        _economyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "车辆", DataPropertyName = "Vehicle", Width = 90 });
        _economyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "目的地", DataPropertyName = "Destination", Width = 90 });
        _economyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "模式", DataPropertyName = "Mode", Width = 88 });
        _economyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "净利", DataPropertyName = "Net", Width = 78 });
        _economyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "$/min", DataPropertyName = "Rate", Width = 66 });
        _economyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "距离", DataPropertyName = "Distance", Width = 72 });

        _economyGrid.CellDoubleClick += async (_, _) => await UseSelectedEconomicPlanAsync();

        host.Controls.Add(_economyGrid);
        top.BringToFront();

        return tab;
    }


    private TabPage MakeRoadGraphTab()
    {
        var tab = NewTab("道路校准");
        var p = Flow();

        p.Controls.Add(Header("自动地图道路识别"));

        _autoRoadButton.Text = "自动分析当前地图";
        _autoRoadButton.AutoSize = true;
        _autoRoadButton.BackColor = Color.FromArgb(42, 48, 58);
        _autoRoadButton.ForeColor = Color.White;
        _autoRoadButton.FlatStyle = FlatStyle.Flat;
        _autoRoadButton.Click += async (_, _) => await AutoExtractRoadsAsync(force: true);
        p.Controls.Add(_autoRoadButton);

        var removeAuto = Btn("删除自动道路");
        removeAuto.Click += (_, _) =>
        {
            var removed = _roadGraphs.RemoveAutoGraph(_currentRoadGraph);
            SaveRoadGraph();
            UpdateRoadGraphStatus("已删除 " + removed + " 条自动道路；手工/实车道路保留。");
        };
        p.Controls.Add(removeAuto);

        p.Controls.Add(new Label
        {
            AutoSize = false,
            Width = 400,
            Height = 78,
            ForeColor = Color.Silver,
            Text =
                "自动识别会分析当前真实地图的道路概率并生成初始 Road Graph。自动边为虚线且未验证；不会覆盖手工或实车学习道路。"
        });

        p.Controls.Add(Header("Road Graph 精确道路层"));

        _roadClass.DropDownStyle = ComboBoxStyle.DropDownList;
        _roadClass.Items.AddRange(Enum.GetNames<RoadClass>());
        _roadClass.SelectedItem = RoadClass.Secondary.ToString();
        _roadClass.Width = 240;
        p.Controls.Add(_roadClass);

        _roadEditButton.Text = "开始手工点路";
        _roadEditButton.AutoSize = true;
        _roadEditButton.BackColor = Color.FromArgb(42, 48, 58);
        _roadEditButton.ForeColor = Color.White;
        _roadEditButton.FlatStyle = FlatStyle.Flat;
        _roadEditButton.Click += (_, _) => ToggleRoadEdit();
        p.Controls.Add(_roadEditButton);

        var breakRoad = Btn("断开下一段");
        breakRoad.Click += (_, _) =>
        {
            _roadEditPreviousNodeId = null;
            UpdateRoadGraphStatus("已断开；下一次点击从新道路开始。");
        };
        p.Controls.Add(breakRoad);

        var undo = Btn("撤销上一手工路段");
        undo.Click += (_, _) =>
        {
            if (_roadGraphs.RemoveLastManualSegment(
                    _currentRoadGraph,
                    _roadEditPreviousNodeId,
                    out var previous))
            {
                _roadEditPreviousNodeId = previous;
                SaveRoadGraph();
                UpdateRoadGraphStatus("已撤销上一手工路段。");
            }
        };
        p.Controls.Add(undo);

        p.Controls.Add(Header("实车道路学习"));

        _roadLearnButton.Text = "开始实车学习";
        _roadLearnButton.AutoSize = true;
        _roadLearnButton.BackColor = Color.FromArgb(42, 48, 58);
        _roadLearnButton.ForeColor = Color.White;
        _roadLearnButton.FlatStyle = FlatStyle.Flat;
        _roadLearnButton.Click += (_, _) => ToggleRoadLearning();
        p.Controls.Add(_roadLearnButton);

        var open = Btn("打开道路数据目录");
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(_roadGraphs.RootDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = _roadGraphs.RootDirectory,
                UseShellExecute = true
            });
        };
        p.Controls.Add(open);

        var clear = Btn("清空当前地图 Road Graph");
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    "确认清空当前地图的全部手工/学习道路数据？",
                    "Road Graph",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            _currentRoadGraph = _roadGraphs.Reset(_map.Text);
            _roadEditPreviousNodeId = null;
            _mapCanvas.SetRoadGraph(_currentRoadGraph);
            UpdateRoadGraphStatus("已清空当前地图 Road Graph。");
        };
        p.Controls.Add(clear);

        _roadGraphStatus.Width = 400;
        _roadGraphStatus.Height = 125;
        _roadGraphStatus.ForeColor = Color.FromArgb(130, 220, 255);
        p.Controls.Add(_roadGraphStatus);

        p.Controls.Add(new Label
        {
            AutoSize = false,
            Width = 400,
            Height = 230,
            ForeColor = Color.Silver,
            Text =
                "手工点路：开启后，在左侧真实地图上沿道路依次点击；每个点击都会生成/吸附道路节点并立即保存。\r\n\r\n" +
                "实车学习：只读取已校准的屏幕坐标。开车时每次有效位置变化都会记录；停止后自动去抖、简化并合并到 Road Graph。\r\n\r\n" +
                "导航优先级：已验证/实车 Road Graph → 自动 Road Graph → 地图像素 A* → 直线回退。"
        });

        tab.Controls.Add(p);
        return tab;
    }

    private TabPage MakeAiTab()
    {
        var tab = NewTab("DeepSeek AI");
        var p = Flow();

        p.Controls.Add(Header("DeepSeek API Key（DPAPI 本机加密）"));

        _apiKey.Width = 380;
        _apiKey.UseSystemPasswordChar = true;
        p.Controls.Add(_apiKey);

        _model.DropDownStyle = ComboBoxStyle.DropDownList;
        _model.Items.AddRange(new object[] { "deepseek-flash", "deepseek-v4-pro" });
        _model.Width = 240;
        p.Controls.Add(_model);

        var row = new FlowLayoutPanel { Width = 420, Height = 42 };

        var save = Btn("保存 Key");
        save.Click += (_, _) =>
        {
            SecretStore.SaveDeepSeekKey(_apiKey.Text);
            _settings.DeepSeekModel = _model.Text;
            _settings.Save();
            MessageBox.Show("已保存到当前 Windows 用户的加密存储。");
        };

        var test = Btn("测试连接");
        test.Click += async (_, _) => await TestAiAsync();

        var analyze = Btn("分析当前方案");
        analyze.Click += async (_, _) => await AnalyzeCurrentAsync();

        row.Controls.Add(save);
        row.Controls.Add(test);
        row.Controls.Add(analyze);
        p.Controls.Add(row);

        p.Controls.Add(Header("AI 导航学习"));

        _aiAutoApplyLearning.Text = "到达后自动分析并应用高置信建议（实验）";
        _aiAutoApplyLearning.AutoSize = true;
        _aiAutoApplyLearning.ForeColor = Color.Gainsboro;
        _aiAutoApplyLearning.CheckedChanged += (_, _) =>
        {
            _settings.AiAutoApplyNavigationLearning = _aiAutoApplyLearning.Checked;
            _settings.Save();
        };
        p.Controls.Add(_aiAutoApplyLearning);

        var learnRow = new FlowLayoutPanel
        {
            Width = 420,
            Height = 76,
            FlowDirection = FlowDirection.LeftToRight
        };

        var analyzeLearning = Btn("AI分析导航经验");
        analyzeLearning.Click += async (_, _) =>
            await AnalyzeNavigationLearningAsync(autoApply: false, silent: false);

        var applyLearning = Btn("应用高置信建议");
        applyLearning.Click += async (_, _) =>
        {
            ApplyLastAiLearning(0.72);
            await PlanRouteAsync(false);
        };

        var clearLearning = Btn("清除AI路段学习");
        clearLearning.Click += async (_, _) =>
        {
            var changed = _roadGraphs.RemoveAiLearning(_currentRoadGraph);
            if (changed > 0)
                SaveRoadGraph();

            _lastAiLearningReport = null;
            _aiOutput.Text = "已清除当前地图 " + changed + " 条路段的 AI 学习参数。";
            await PlanRouteAsync(false);
        };

        learnRow.Controls.Add(analyzeLearning);
        learnRow.Controls.Add(applyLearning);
        learnRow.Controls.Add(clearLearning);
        p.Controls.Add(learnRow);

        p.Controls.Add(Header("AI 视觉导航"));

        _aiAutoVisionScan.Text = "实时导航时每 3 分钟自动视觉检查（实验）";
        _aiAutoVisionScan.AutoSize = true;
        _aiAutoVisionScan.ForeColor = Color.Gainsboro;
        _aiAutoVisionScan.CheckedChanged += (_, _) =>
        {
            _settings.AiAutoVisionScan = _aiAutoVisionScan.Checked;
            _settings.Save();
        };
        p.Controls.Add(_aiAutoVisionScan);

        var visionRow = new FlowLayoutPanel
        {
            Width = 420,
            Height = 76,
            FlowDirection = FlowDirection.LeftToRight
        };

        var analyzeVision = Btn("AI视觉检查当前路线");
        analyzeVision.Click += async (_, _) =>
            await AnalyzeVisionRouteAsync(
                autoApply: false,
                silent: false);

        var applyVision = Btn("应用视觉风险并重算");
        applyVision.Click += async (_, _) =>
        {
            ApplyLastVisionEvidence(0.80);
            await PlanRouteAsync(false);
        };

        var clearVision = Btn("清除视觉风险");
        clearVision.Click += async (_, _) =>
        {
            var removed = _visionEvidence.ClearMap(_map.Text);
            _lastVisionReport = null;
            UpdateVisionEvidence();
            _aiOutput.Text =
                "已清除当前地图 " +
                removed +
                " 条临时视觉证据。";
            await PlanRouteAsync(false);
        };

        visionRow.Controls.Add(analyzeVision);
        visionRow.Controls.Add(applyVision);
        visionRow.Controls.Add(clearVision);
        p.Controls.Add(visionRow);

        p.Controls.Add(new Label
        {
            AutoSize = false,
            Width = 400,
            Height = 72,
            ForeColor = Color.Silver,
            Text =
                "视觉检查固定使用 deepseek-flash。只上传你在“校准”页框选的游戏地图区域；程序参考图只包含当前地图和当前路线。视觉风险默认 8 分钟后过期。"
        });

        _aiOutput.Multiline = true;
        _aiOutput.ScrollBars = ScrollBars.Vertical;
        _aiOutput.Width = 400;
        _aiOutput.Height = 430;
        p.Controls.Add(_aiOutput);

        tab.Controls.Add(p);
        return tab;
    }

    private TabPage MakeCalibrationTab()
    {
        var tab = NewTab("校准");
        var p = Flow();

        p.Controls.Add(Header("游戏窗口标题包含"));

        _windowTitle.Width = 360;
        p.Controls.Add(_windowTitle);

        var saveTitle = Btn("保存窗口设置");
        saveTitle.Click += (_, _) =>
        {
            _settings.GameWindowTitleContains = _windowTitle.Text.Trim();
            _settings.Save();
            UpdateCalibrationStatus();
        };
        p.Controls.Add(saveTitle);

        var self = Btn("框选当前位置坐标区域");
        self.Click += (_, _) => CalibrateRegion(true);

        var target = Btn("框选目标坐标区域");
        target.Click += (_, _) => CalibrateRegion(false);

        var visionMap = Btn("框选游戏地图视觉区域");
        visionMap.Click += (_, _) => CalibrateVisionMapRegion();

        var marker = Btn("校准目标标记图标");
        marker.Click += async (_, _) =>
            await CalibrateTargetMarkerAsync();

        var testRegistration = Btn("测试视觉配准");
        testRegistration.Click += async (_, _) =>
            await TestVisualRegistrationAsync();

        var clearVisualMemory = Btn("清除当前地图视觉记忆");
        clearVisualMemory.Click += (_, _) =>
        {
            _visualMapMemory.Clear(_map.Text);
            _lastMapRegistration = null;
            _lastVisualTargetDetection = null;
            _mapCanvas.SetVisualMapState(
                null,
                null,
                0);
            UpdateCalibrationStatus();
        };

        p.Controls.Add(self);
        p.Controls.Add(target);
        p.Controls.Add(visionMap);
        p.Controls.Add(marker);
        p.Controls.Add(testRegistration);
        p.Controls.Add(clearVisualMemory);

        _calibrationStatus.Width = 400;
        _calibrationStatus.Height = 205;
        p.Controls.Add(_calibrationStatus);

        p.Controls.Add(new Label
        {
            AutoSize = false,
            Width = 400,
            Height = 220,
            ForeColor = Color.Silver,
            Text =
                "校准区域按游戏窗口客户区比例保存，因此 1080p / 1440p / 4K 切换后仍可复用。\r\n\r\n" +
                "“游戏地图视觉区域”同时用于 AI 路线视觉检查和本地地图配准；尽量框住地图本体并减少聊天框/菜单遮挡。\r\n\r\n" +
                "目标标记图标只需校准一次：打开游戏地图、放一个目标标记，然后点“校准目标标记图标”并点击标记中心。\r\n\r\n" +
                "程序只抓取屏幕像素；本地目标识别不读取进程内存、不注入、不安装驱动，也不需要调用 AI API。"
        });

        tab.Controls.Add(p);
        return tab;
    }

    private void WireEvents()
    {
        _map.SelectedIndexChanged += async (_, _) =>
        {
            _settings.CurrentMap = _map.Text;
            _settings.Save();
            _route = null;
            _guidance.Reset(null);
            await SwitchMapAsync();
        };

        _routeMode.SelectedIndexChanged += async (_, _) =>
        {
            if (Enum.TryParse<RoutePreference>(_routeMode.Text, out var pref))
            {
                _settings.RoutePreference = pref;
                _settings.Save();
            }

            if (_route != null)
                await PlanRouteAsync(false);
        };

        _navVehicle.SelectedIndexChanged += async (_, _) =>
        {
            if (_route != null)
                await PlanRouteAsync(false);
        };

        _mapCanvas.MapClicked += point =>
        {
            if (_roadEditMode)
            {
                _roadGraphs.AddManualPoint(
                    _currentRoadGraph,
                    point,
                    ref _roadEditPreviousNodeId,
                    SelectedRoadClass());

                SaveRoadGraph();
                UpdateRoadGraphStatus("手工道路点已保存：" + point);
                return;
            }

            SetTarget(point);
            _ = PlanRouteAsync(false);
        };

        _liveTimer.Tick += async (_, _) => await LiveTickAsync();
    }

    private void LoadSettingsIntoUi()
    {
        _map.SelectedItem = _settings.CurrentMap;
        if (_map.SelectedIndex < 0) _map.SelectedIndex = 0;

        _routeMode.SelectedItem = _settings.RoutePreference.ToString();
        if (_routeMode.SelectedIndex < 0) _routeMode.SelectedIndex = 0;

        _windowTitle.Text = _settings.GameWindowTitleContains;
        _apiKey.Text = SecretStore.LoadDeepSeekKey();

        _model.SelectedItem = _settings.DeepSeekModel;
        if (_model.SelectedIndex < 0) _model.SelectedIndex = 0;

        _autoTargetCheck.Checked =
            _settings.AutoReadTarget;
        _visualTargetCheck.Checked =
            _settings.VisualTargetNavigationEnabled;
        _aiAutoApplyLearning.Checked = _settings.AiAutoApplyNavigationLearning;
        _aiAutoVisionScan.Checked = _settings.AiAutoVisionScan;

        UpdateCalibrationStatus();
    }

    private async Task SwitchMapAsync()
    {
        StoreNavigationExperience(completed: false);

        var requestedMapId = _map.Text;

        if (_autoExtractMapId != null &&
            !_autoExtractMapId.Equals(requestedMapId, StringComparison.OrdinalIgnoreCase))
            _autoRoadCts?.Cancel();

        _visionCts?.Cancel();
        _targetScanCts?.Cancel();
        _predictionCts?.Cancel();
        _lastVisionReport = null;
        _lastAutoVisionScan = DateTime.MinValue;
        _lastTargetOcrUtc = DateTime.MinValue;
        _lastRerouteUtc = DateTime.MinValue;
        _positionFilter.Reset();
        _lastLivePoint = null;
        _headingDeg = null;

        if (_traceLearning.IsRecording)
        {
            _traceLearning.Cancel();
            _roadLearnButton.Text = "开始实车学习";
            if (!_live)
                _liveTimer.Stop();
        }

        var id = _map.Text;
        var bitmap = await _mapAssets.GetBitmapAsync(id);
        var visualMemory =
            _visualMapMemory.Get(id);
        _lastMapRegistration =
            visualMemory.LastRegistration;
        _lastVisualTargetDetection = null;
        _currentRoadGraph = _roadGraphs.Load(id);
        _roadEditPreviousNodeId = null;
        _mapCanvas.SetMap(bitmap, _maps.Get(id));
        _mapCanvas.SetRoadGraph(_currentRoadGraph);
        _mapCanvas.SetGuidance(null);
        _mapCanvas.SetVisualMapState(
            _lastMapRegistration,
            visualMemory.LastVisualTarget,
            visualMemory.LastTargetConfidence);
        _guidance.Reset(null);
        UpdateHazards();
        UpdateVisionEvidence();
        UpdateRoadGraphStatus();
        UpdateMapState();

        if (!_currentRoadGraph.Edges.Any(e =>
                e.Source.Equals("auto", StringComparison.OrdinalIgnoreCase)))
        {
            await AutoExtractRoadsAsync(force: false);
        }
    }

    private void RunEconomy()
    {
        if (!TryPoint(_selfX, _selfY, out var self))
        {
            MessageBox.Show("请先获得当前位置。");
            return;
        }

        var destinations = _maps.GetEconomicDestinations(_map.Text);
        var plans = _economy.Optimize(self, destinations, 3);
        _selectedEconomicPlan = _economy.PickBest(plans);

        var rows = plans.Take(80).Select(p => new EconomyRow(p)).ToList();
        _economyGrid.DataSource = rows;

        if (rows.Count > 0)
            _economyGrid.Rows[0].Selected = true;
    }

    private async Task UseSelectedEconomicPlanAsync()
    {
        if (_economyGrid.CurrentRow?.DataBoundItem is not EconomyRow row) return;

        _selectedEconomicPlan = row.Plan;
        SelectNavigationVehicle(row.Plan.Vehicle.Id);
        SetTarget(row.Plan.Destination.Position);
        await PlanRouteAsync(true);
    }

    private async Task PlanRouteAsync(bool speak)
    {
        if (!TryPoint(_selfX, _selfY, out var self) ||
            !TryPoint(_targetX, _targetY, out var target))
        {
            _routeSummary.Text = "路线：缺少有效的当前位置或目标坐标。";
            return;
        }

        _planCts?.Cancel();
        _predictionCts?.Cancel();

        _planCts =
            new CancellationTokenSource();

        _predictionCts =
            new CancellationTokenSource();

        var token =
            _planCts.Token;

        try
        {
            UpdateHazards();
            _routeSummary.Text = "路线：正在分析真实地图道路…";

            var vehicle = SelectedNavigationVehicle();
            var profile = VehicleRoutingProfileService.For(vehicle);
            var speed = vehicle?.SpeedKmh ?? 80;
            var air = vehicle?.Air ?? false;
            var preference = Enum.TryParse<RoutePreference>(_routeMode.Text, out var parsed)
                ? parsed
                : RoutePreference.Fastest;

            _route = await _routes.PlanAsync(
                _map.Text,
                self,
                target,
                preference,
                speed,
                air,
                profile,
                token);

            _guidance.Reset(_route);
            _lastSpokenCueKey = "";
            _lastRerouteUtc = DateTime.UtcNow;

            if (_navigationLearning.IsActive)
            {
                var sameLearningContext =
                    _navigationLearning.VehicleId.Equals(
                        profile.VehicleId,
                        StringComparison.OrdinalIgnoreCase) &&
                    _navigationLearning.Preference ==
                        preference;

                if (sameLearningContext)
                {
                    _navigationLearning.NoteReplan(
                        _route);
                }
                else
                {
                    StoreNavigationExperience(
                        completed: false);

                    _navigationLearning.Start(
                        _map.Text,
                        profile.VehicleId,
                        preference,
                        _route,
                        _currentRoadGraph,
                        self,
                        speed);
                }
            }
            else if (_live)
            {
                _navigationLearning.Start(
                    _map.Text,
                    profile.VehicleId,
                    preference,
                    _route,
                    _currentRoadGraph,
                    self,
                    speed);
            }

            var cue = _guidance.BuildCue(self, _headingDeg);

            _routeSummary.Text =
                "路线：" + _route.DistanceKm.ToString("F2") + " km · ETA " +
                _route.EstimatedMinutes.ToString("F1") + " min\r\n" +
                cue.Instruction + " · " +
                (_route.UsedFallback ? "直线回退" : _route.Source);

            var fire = FireControl.Calculate(self, target);
            _fireSummary.Text =
                "火控：" + fire.DistanceMeters.ToString("F0") + " m · " +
                fire.AzimuthDeg.ToString("000.0") + "° · DIR " +
                fire.DirectionMils.ToString("F0") + " mil";

            _overlay.UpdateCue(cue, _route);
            _mapCanvas.SetGuidance(cue);
            UpdateMapState();

            var predictionToken =
                _predictionCts.Token;

            _ =
                _routes.WarmPredictedReroutesAsync(
                    _map.Text,
                    _route,
                    preference,
                    speed,
                    profile,
                    predictionToken);

            if (speak && _settings.SpeakNavigation)
            {
                _lastSpokenCueKey = CueSpeechKey(cue);
                _lastSpoken = DateTime.UtcNow;
                Speak(
                    cue.Instruction +
                    "。全程 " +
                    _route.DistanceKm.ToString("F1") +
                    " 公里。");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _routeSummary.Text = "路线失败：" + ex.Message;
        }
    }

    private async Task ReadPlayerOnceAsync()
    {
        var point = await ReadRegionAsync(_settings.PlayerRegion);
        if (point is not MapPoint value) return;

        SetSelf(value);
        UpdateMapState();
    }

    private async Task ReadTargetOnceAsync()
    {
        var point =
            await ReadBestTargetAsync(
                silent: false);

        if (point is not MapPoint value)
            return;

        _pendingTargetPoint = null;
        _pendingTargetSamples = 0;
        SetTarget(value);
        await PlanRouteAsync(false);
    }

    private async Task RefreshLiveTargetAsync()
    {
        if (_targetScanBusy)
            return;

        _targetScanBusy = true;

        _targetScanCts?.Cancel();
        _targetScanCts?.Dispose();

        var localCts =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(18));

        _targetScanCts =
            localCts;

        try
        {
            var mapId =
                _map.Text;

            var target =
                await ReadBestTargetAsync(
                    silent: true,
                    localCts.Token);

            if (localCts.IsCancellationRequested ||
                !_map.Text.Equals(
                    mapId,
                    StringComparison.OrdinalIgnoreCase) ||
                target is not MapPoint targetPoint)
                return;

            var changed =
                !TryPoint(
                    _targetX,
                    _targetY,
                    out var oldTarget) ||
                oldTarget.DistanceMeters(
                    targetPoint) > 12;

            if (!changed)
            {
                _pendingTargetPoint = null;
                _pendingTargetSamples = 0;
                return;
            }

            if (_pendingTargetPoint is MapPoint pending &&
                pending.DistanceMeters(
                    targetPoint) <= 20)
            {
                _pendingTargetSamples++;
            }
            else
            {
                _pendingTargetPoint =
                    targetPoint;
                _pendingTargetSamples = 1;
            }

            if (_pendingTargetSamples < 2)
                return;

            SetTarget(targetPoint);
            _pendingTargetPoint = null;
            _pendingTargetSamples = 0;

            await PlanRouteAsync(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Automatic target scanning is opportunistic. Keep navigating
            // toward the last confirmed destination if one scan fails.
        }
        finally
        {
            if (ReferenceEquals(
                    _targetScanCts,
                    localCts))
                _targetScanCts = null;

            localCts.Dispose();
            _targetScanBusy = false;
        }
    }

    private async Task<MapPoint?> ReadBestTargetAsync(
        bool silent,
        CancellationToken cancellationToken = default)
    {
        string visualError = "";

        if (_settings.VisualTargetNavigationEnabled &&
            _settings.VisionMapRegion.IsValid &&
            _settings.TargetMarkerProfile?.IsValid == true)
        {
            var visual =
                await ReadVisualTargetAsync(
                    silent: true,
                    cancellationToken);

            if (visual.Success &&
                visual.Confidence >=
                    _settings.VisualTargetMinConfidence)
                return visual.Point;

            visualError =
                visual.Error;
        }

        if (_settings.TargetRegion.IsValid)
        {
            var ocr =
                await ReadRegionAsync(
                    _settings.TargetRegion,
                    silent: true,
                    cancellationToken);

            if (ocr is MapPoint point)
                return point;
        }

        if (!silent)
        {
            _routeSummary.Text =
                string.IsNullOrWhiteSpace(
                    visualError)
                    ? "目标识别失败：视觉目标和坐标 OCR 都没有获得有效目标。"
                    : "目标识别失败：" +
                      visualError +
                      "；坐标 OCR 也未获得有效目标。";
        }

        return null;
    }

    private async Task<VisualTargetDetection> ReadVisualTargetAsync(
        bool silent,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.VisionMapRegion.IsValid)
        {
            return new VisualTargetDetection
            {
                Error =
                    "尚未框选游戏地图视觉区域。"
            };
        }

        if (_settings.TargetMarkerProfile?.IsValid != true)
        {
            return new VisualTargetDetection
            {
                Error =
                    "尚未校准目标标记图标。"
            };
        }

        try
        {
            using var screenshot =
                _capture.Capture(
                    _settings.GameWindowTitleContains,
                    _settings.VisionMapRegion);

            var mapId =
                _map.Text;

            var memory =
                _visualMapMemory.Get(
                    mapId);

            var hint =
                _lastMapRegistration?.IsValid == true
                    ? _lastMapRegistration
                    : memory.LastRegistration;

            var registration =
                await _mapVisualRegistration.RegisterAsync(
                    mapId,
                    screenshot,
                    hint,
                    cancellationToken);

            if (registration?.IsValid != true ||
                registration.Confidence <
                    _settings.VisualMapMinRegistrationConfidence)
            {
                _visualMapMemory.RecordRegistrationFailure(
                    mapId);

                var error =
                    registration == null
                        ? "本地地图配准失败。"
                        : "地图配准置信度不足：" +
                          Math.Round(
                              registration.Confidence *
                              100)
                              .ToString("F0") +
                          "%";

                if (!silent)
                    _routeSummary.Text =
                        error;

                return new VisualTargetDetection
                {
                    Error = error,
                    RegistrationConfidence =
                        registration?.Confidence ?? 0
                };
            }

            _lastMapRegistration =
                registration;

            _visualMapMemory.RecordRegistration(
                mapId,
                registration);

            MapPoint? expected =
                TryPoint(
                    _targetX,
                    _targetY,
                    out var currentTarget)
                    ? currentTarget
                    : memory.LastVisualTarget;

            var detection =
                await Task.Run(
                    () =>
                        _targetMarkerDetector.Detect(
                            screenshot,
                            registration,
                            _settings.TargetMarkerProfile,
                            expected),
                    cancellationToken);

            if (detection.Success &&
                detection.Confidence >=
                    _settings.VisualTargetMinConfidence)
            {
                _lastVisualTargetDetection =
                    detection;

                _visualMapMemory.RecordTarget(
                    mapId,
                    detection.Point,
                    detection.Confidence);

                _mapCanvas.SetVisualMapState(
                    registration,
                    detection.Point,
                    detection.Confidence);

                if (!silent)
                {
                    _routeSummary.Text =
                        "视觉目标：" +
                        detection.Point +
                        " · 目标置信 " +
                        Math.Round(
                            detection.Confidence *
                            100)
                            .ToString("F0") +
                        "% · 配准 " +
                        Math.Round(
                            registration.Confidence *
                            100)
                            .ToString("F0") +
                        "% · 旋转 " +
                        registration.RotationDeg
                            .ToString("+0;-0;0") +
                        "°";
                }

                return detection;
            }

            detection.Success = false;

            if (string.IsNullOrWhiteSpace(
                    detection.Error))
            {
                detection.Error =
                    "目标图标候选置信度不足：" +
                    Math.Round(
                        detection.Confidence *
                        100)
                        .ToString("F0") +
                    "%";
            }

            _mapCanvas.SetVisualMapState(
                registration,
                null,
                0);

            if (!silent)
                _routeSummary.Text =
                    detection.Error;

            return detection;
        }
        catch (OperationCanceledException)
        {
            return new VisualTargetDetection
            {
                Error =
                    "视觉目标识别已取消。"
            };
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                _routeSummary.Text =
                    "视觉目标识别失败：" +
                    ex.Message;
            }

            return new VisualTargetDetection
            {
                Error =
                    "视觉目标识别失败：" +
                    ex.Message
            };
        }
    }

    private async Task<MapPoint?> ReadRegionAsync(
        NormalizedRegion region,
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var bitmap = _capture.Capture(_settings.GameWindowTitleContains, region);
            var result =
                await _ocr.RecognizeAsync(
                    bitmap,
                    cancellationToken);

            if (!result.Success)
            {
                if (!silent)
                {
                    _routeSummary.Text =
                        "识别失败：" + result.Error +
                        (string.IsNullOrWhiteSpace(result.RawText)
                            ? ""
                            : " [" + result.RawText + "]");
                }

                return null;
            }

            return result.Point;
        }
        catch (Exception ex)
        {
            if (!silent)
                _routeSummary.Text =
                    "截屏/OCR失败：" + ex.Message;

            return null;
        }
    }

    private void ToggleLive()
    {
        _live = !_live;
        _liveButton.Text = _live ? "停止实时导航" : "开始实时导航";

        if (_live)
        {
            _overlay.Show();
            _positionFilter.Reset();
            _lastLivePoint = null;
            _headingDeg = null;
            _lastTargetOcrUtc = DateTime.MinValue;
            _liveTimer.Start();

            if (_route != null && TryPoint(_selfX, _selfY, out var current))
            {
                var vehicle = SelectedNavigationVehicle();
                var profile = VehicleRoutingProfileService.For(vehicle);
                var preference = Enum.TryParse<RoutePreference>(
                        _routeMode.Text,
                        out var parsed)
                    ? parsed
                    : RoutePreference.Fastest;

                var speed =
                    vehicle?.SpeedKmh ?? 80;

                _navigationLearning.Start(
                    _map.Text,
                    profile.VehicleId,
                    preference,
                    _route,
                    _currentRoadGraph,
                    current,
                    speed);
            }
        }
        else
        {
            StoreNavigationExperience(completed: false);

            if (!_traceLearning.IsRecording)
                _liveTimer.Stop();
        }
    }

    private async Task LiveTickAsync()
    {
        if (_liveTickBusy)
            return;

        _liveTickBusy = true;

        try
        {
        if (!_live && !_traceLearning.IsRecording) return;
        
                var rawCurrent = await ReadRegionAsync(
                    _settings.PlayerRegion,
                    silent: true);

                if (rawCurrent is not MapPoint rawNow)
                    return;

                var sampleUtc = DateTime.UtcNow;

                if (!_positionFilter.TryAccept(
                        rawNow,
                        sampleUtc,
                        out var now))
                    return;

                if (_lastLivePoint is MapPoint old &&
                    old.DistanceMeters(now) >= 3)
                    _headingDeg =
                        old.BearingDegTo(now);

                _lastLivePoint = now;
                SetSelf(now);
                _navigationLearning.NotePoint(now);
        
                if (_traceLearning.IsRecording)
                {
                    _traceLearning.Accept(now);
                    UpdateRoadGraphStatus(
                        "实车学习中：已采样 " +
                        _traceLearning.RawPoints.Count +
                        " 个有效位置点。");
                }
        
                if (!_live)
                {
                    UpdateMapState();
                    return;
                }
        
                var targetReadAvailable =
                    (
                        _settings.VisualTargetNavigationEnabled &&
                        _settings.VisionMapRegion.IsValid &&
                        _settings.TargetMarkerProfile?.IsValid == true
                    ) ||
                    _settings.TargetRegion.IsValid;

                var targetScanSeconds =
                    _settings.VisualTargetNavigationEnabled
                        ? Math.Clamp(
                            _settings.VisualTargetScanSeconds,
                            2,
                            15)
                        : 4;

                if (_settings.AutoReadTarget &&
                    targetReadAvailable &&
                    !_targetScanBusy &&
                    DateTime.UtcNow - _lastTargetOcrUtc >=
                        TimeSpan.FromSeconds(
                            targetScanSeconds))
                {
                    _lastTargetOcrUtc =
                        DateTime.UtcNow;

                    _ =
                        RefreshLiveTargetAsync();
                }

                if (_route == null)
                {
                    await PlanRouteAsync(false);
                    return;
                }

                var cue = _guidance.BuildCue(now, _headingDeg);

                if (cue.ShouldReroute &&
                    DateTime.UtcNow - _lastRerouteUtc >=
                        TimeSpan.FromSeconds(4))
                {
                    _lastRerouteUtc =
                        DateTime.UtcNow;

                    _routeSummary.Text =
                        "路线：检测到持续偏航 " +
                        cue.DeviationMeters.ToString("F0") +
                        " m，正在从当前位置重新规划…";

                    if (_settings.SpeakNavigation &&
                        DateTime.UtcNow - _lastSpoken >
                            TimeSpan.FromSeconds(6))
                    {
                        _lastSpoken = DateTime.UtcNow;
                        Speak("已偏离路线，正在重新规划");
                    }

                    await PlanRouteAsync(false);
                    return;
                }

                _overlay.UpdateCue(cue, _route);
                _mapCanvas.SetGuidance(cue);
                UpdateMapState();

                _routeSummary.Text =
                    "导航：剩余 " +
                    cue.RemainingKm.ToString("F2") +
                    " km · ETA " +
                    cue.RemainingMinutes.ToString("F1") +
                    " min\r\n" +
                    cue.Instruction +
                    " · 偏差 " +
                    cue.DeviationMeters.ToString("F0") +
                    " m";

                var cueKey = CueSpeechKey(cue);
                var promptWindow =
                    cue.NextDistanceMeters <= 260 ||
                    DateTime.UtcNow - _lastSpoken >
                        TimeSpan.FromSeconds(35);

                if (_settings.SpeakNavigation &&
                    !cue.Arrived &&
                    !cue.OffRoute &&
                    cueKey != _lastSpokenCueKey &&
                    promptWindow)
                {
                    _lastSpokenCueKey = cueKey;
                    _lastCueIndex = cue.RouteIndex;
                    _lastSpoken = DateTime.UtcNow;
                    Speak(cue.Instruction);
                }
                else if (cue.Arrived &&
                         _lastSpokenCueKey != "arrive")
                {
                    _lastSpokenCueKey = "arrive";
                    _lastCueIndex = cue.RouteIndex;
                    _lastSpoken = DateTime.UtcNow;
                    Speak("已到达目的地");
                }
        
                if (cue.Arrived && _navigationLearning.IsActive)
                {
                    StoreNavigationExperience(completed: true);
        
                    if (_settings.AiAutoApplyNavigationLearning &&
                        !string.IsNullOrWhiteSpace(_apiKey.Text))
                    {
                        var snapshot = _experiences.Snapshot(_map.Text);
                        if (snapshot.CompletedCount >= 3 &&
                            snapshot.CompletedCount % 3 == 0)
                        {
                            await AnalyzeNavigationLearningAsync(
                                autoApply: true,
                                silent: true);
                        }
                    }
                }
        
                if (!cue.Arrived &&
                    _settings.AiAutoVisionScan &&
                    _settings.VisionMapRegion.IsValid &&
                    !string.IsNullOrWhiteSpace(_apiKey.Text) &&
                    _route?.EdgeIds.Count > 0 &&
                    !_visionBusy &&
                    DateTime.UtcNow - _lastAutoVisionScan >
                        TimeSpan.FromMinutes(3))
                {
                    _lastAutoVisionScan = DateTime.UtcNow;
        
                    await AnalyzeVisionRouteAsync(
                        autoApply: true,
                        silent: true);
                }
        }
        finally
        {
            _liveTickBusy = false;
        }
    }

    private async Task TestAiAsync()
    {
        try
        {
            _aiOutput.Text = "连接中…";
            _aiOutput.Text = await _ai.TestAsync(_apiKey.Text, _model.Text);
        }
        catch (Exception ex)
        {
            _aiOutput.Text = ex.Message;
        }
    }

    private async Task AnalyzeCurrentAsync()
    {
        try
        {
            var economyText = _selectedEconomicPlan == null
                ? "当前没有选中的经济方案。"
                : "经济方案：" +
                  _selectedEconomicPlan.Vehicle.NameZh +
                  "，目的地 " + _selectedEconomicPlan.Destination.Label +
                  "，" + _selectedEconomicPlan.LoadMode +
                  "，" + (_selectedEconomicPlan.RoundTrip ? "往返" : "单程") +
                  "，3趟预计净利 $" + _selectedEconomicPlan.SessionNet.ToString("F0") +
                  "，$" + _selectedEconomicPlan.SessionPerMinute.ToString("F1") + "/min。";

            var navVehicle = SelectedNavigationVehicle();
            var hazards = _hazards.GetActive(_map.Text);
            var learning = _experiences.Snapshot(_map.Text, 10);

            var routeText = _route == null
                ? "当前没有路线。"
                : "导航车辆：" + (navVehicle?.NameZh ?? "通用地面车辆") +
                  "；路线：" + _route.Preference +
                  "，" + _route.DistanceKm.ToString("F2") + "km" +
                  "，ETA " + _route.EstimatedMinutes.ToString("F1") + "min" +
                  "，来源 " + _route.Source +
                  "，当前临时危险区 " + hazards.Count +
                  "，已有导航学习样本 " + learning.ExperienceCount + "。";

            _aiOutput.Text = "DeepSeek 分析中…";
            _aiOutput.Text = await _ai.AskAsync(
                _apiKey.Text,
                _model.Text,
                "你是 WARDOGS 运输与导航辅助分析员。确定性经济公式与 A* 路线已经由本地程序算好。你只能解释结果、指出风险和给操作建议；不要编造价格、坐标、道路或替换本地计算结果。",
                economyText + "\n" + routeText +
                "\n请用简洁中文解释这个方案为什么值得选，以及驾驶时应该注意什么。");
        }
        catch (Exception ex)
        {
            _aiOutput.Text = ex.Message;
        }
    }



    private async Task AnalyzeVisionRouteAsync(
        bool autoApply,
        bool silent)
    {
        if (_visionBusy)
            return;

        if (_route == null || _route.EdgeIds.Count == 0)
        {
            if (!silent)
                _aiOutput.Text =
                    "当前路线没有 Road Graph 路段，无法进行 edge 级视觉检查。";
            return;
        }

        if (!_settings.VisionMapRegion.IsValid)
        {
            if (!silent)
                _aiOutput.Text =
                    "请先在“校准”页框选游戏地图视觉区域。";
            return;
        }

        if (string.IsNullOrWhiteSpace(_apiKey.Text))
        {
            if (!silent)
                _aiOutput.Text =
                    "请先保存 DeepSeek API Key。";
            return;
        }

        var mapId = _map.Text;
        var routeSnapshot = _route;
        _visionBusy = true;

        _visionCts?.Cancel();
        _visionCts?.Dispose();

        var localCts =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(55));
        _visionCts = localCts;

        try
        {
            if (!silent)
                _aiOutput.Text =
                    "正在截取游戏地图并进行 DeepSeek 视觉对照…";

            using var gameMap =
                _capture.Capture(
                    _settings.GameWindowTitleContains,
                    _settings.VisionMapRegion);

            MapPoint? current =
                TryPoint(
                    _selfX,
                    _selfY,
                    out var self)
                    ? self
                    : null;

            MapPoint? target =
                TryPoint(
                    _targetX,
                    _targetY,
                    out var tgt)
                    ? tgt
                    : null;

            var report =
                await _aiVision.AnalyzeRouteAsync(
                    _apiKey.Text,
                    mapId,
                    _currentRoadGraph,
                    routeSnapshot,
                    gameMap,
                    current,
                    target,
                    localCts.Token);

            if (!_map.Text.Equals(
                    mapId,
                    StringComparison.OrdinalIgnoreCase))
                return;

            _lastVisionReport = report;

            var applied = 0;

            if (autoApply)
            {
                applied = ApplyVisionReport(
                    report,
                    _settings.AiVisionMinConfidence);
            }

            if (!silent)
            {
                var lines = report.Findings
                    .OrderByDescending(x => x.Confidence)
                    .Take(24)
                    .Select(x =>
                        x.EdgeId +
                        " · " +
                        x.Kind +
                        " · 风险 " +
                        Math.Round(
                            x.Severity * 100)
                            .ToString("F0") +
                        "% · 置信 " +
                        Math.Round(
                            x.Confidence * 100)
                            .ToString("F0") +
                        "% · " +
                        x.Reason)
                    .ToList();

                _aiOutput.Text =
                    report.Summary +
                    "\r\n\r\n" +
                    (lines.Count == 0
                        ? "视觉检查未发现需要标记的当前路线异常。"
                        : string.Join(
                            "\r\n",
                            lines)) +
                    (autoApply
                        ? "\r\n\r\n已应用 " +
                          applied +
                          " 条高置信临时视觉证据。"
                        : "");
            }

            if (autoApply && applied > 0)
                await PlanRouteAsync(false);
        }
        catch (OperationCanceledException)
        {
            if (!silent)
                _aiOutput.Text =
                    "AI 视觉检查已取消/超时。";
        }
        catch (Exception ex)
        {
            if (!silent)
                _aiOutput.Text =
                    "AI 视觉检查失败：" +
                    ex.Message;
        }
        finally
        {
            if (ReferenceEquals(
                    _visionCts,
                    localCts))
            {
                _visionCts = null;
            }

            localCts.Dispose();
            _visionBusy = false;
        }
    }

    private int ApplyVisionReport(
        AiVisionNavigationReport report,
        double minimumConfidence)
    {
        var validEdgeIds =
            _currentRoadGraph.Edges
                .Select(x => x.Id)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var ttl = TimeSpan.FromMinutes(
            Math.Clamp(
                _settings.AiVisionEvidenceMinutes,
                1,
                30));

        var applied =
            _visionEvidence.ApplyReport(
                _map.Text,
                report,
                validEdgeIds,
                minimumConfidence,
                ttl);

        UpdateVisionEvidence();
        return applied;
    }

    private void ApplyLastVisionEvidence(
        double minimumConfidence)
    {
        if (_lastVisionReport == null)
        {
            _aiOutput.Text =
                "请先执行“AI视觉检查当前路线”。";
            return;
        }

        var applied =
            ApplyVisionReport(
                _lastVisionReport,
                minimumConfidence);

        _aiOutput.Text =
            _lastVisionReport.Summary +
            "\r\n\r\n已应用 " +
            applied +
            " 条置信度 ≥ " +
            Math.Round(
                minimumConfidence * 100)
                .ToString("F0") +
            "% 的临时视觉证据。";
    }

    private async Task AnalyzeNavigationLearningAsync(bool autoApply, bool silent)
    {
        if (_aiLearningBusy)
            return;

        _aiLearningBusy = true;

        try
        {
            var snapshot = _experiences.Snapshot(_map.Text, 30);

            if (snapshot.ExperienceCount < 2)
            {
                if (!silent)
                    _aiOutput.Text = "导航学习样本不足：至少完成/记录 2 次导航后再分析。";
                return;
            }

            if (!silent)
                _aiOutput.Text =
                    "DeepSeek 正在分析 " +
                    snapshot.ExperienceCount +
                    " 条导航经验…";

            var report = await _aiNavigationLearning.AnalyzeAsync(
                _apiKey.Text,
                _model.Text,
                _map.Text,
                _currentRoadGraph,
                snapshot);

            _lastAiLearningReport = report;

            var applied = 0;
            if (autoApply)
            {
                applied = _roadGraphs.ApplyAiSuggestions(
                    _currentRoadGraph,
                    report.Suggestions,
                    _settings.AiLearningMinConfidence);

                if (applied > 0)
                    SaveRoadGraph();
            }

            if (!silent)
            {
                var lines = report.Suggestions
                    .OrderByDescending(s => s.Confidence)
                    .Take(20)
                    .Select(s =>
                        s.EdgeId +
                        " · " +
                        s.VehicleId +
                        " · 速度×" +
                        s.SpeedMultiplier.ToString("F2") +
                        " · 风险Δ" +
                        s.RiskDelta.ToString("+0.00;-0.00;0.00") +
                        " · 置信 " +
                        Math.Round(s.Confidence * 100).ToString("F0") +
                        "% · " +
                        s.Reason)
                    .ToList();

                _aiOutput.Text =
                    report.Summary +
                    "\r\n\r\n" +
                    (lines.Count == 0
                        ? "没有足够可信的路段建议。"
                        : string.Join("\r\n", lines)) +
                    (autoApply
                        ? "\r\n\r\n自动应用 " + applied + " 条高置信建议。"
                        : "");
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                _aiOutput.Text = "AI 导航学习失败：" + ex.Message;
        }
        finally
        {
            _aiLearningBusy = false;
        }
    }

    private void ApplyLastAiLearning(double minimumConfidence)
    {
        if (_lastAiLearningReport == null)
        {
            _aiOutput.Text = "请先执行“AI分析导航经验”。";
            return;
        }

        var applied = _roadGraphs.ApplyAiSuggestions(
            _currentRoadGraph,
            _lastAiLearningReport.Suggestions,
            minimumConfidence);

        if (applied > 0)
            SaveRoadGraph();

        _aiOutput.Text =
            _lastAiLearningReport.Summary +
            "\r\n\r\n已应用 " +
            applied +
            " 条置信度 ≥ " +
            Math.Round(minimumConfidence * 100).ToString("F0") +
            "% 的建议。";
    }

    private void StoreNavigationExperience(bool completed)
    {
        var experience = _navigationLearning.Stop(completed);
        if (experience == null)
            return;

        if (!completed &&
            experience.ActualDistanceKm < 0.03 &&
            experience.DurationMinutes < 0.20)
            return;

        _experiences.Append(experience);

        var provenEdges = experience.EdgeObservations
            .Where(x =>
                x.Samples >= 3 &&
                x.DistanceKm >= 0.03 &&
                x.MaxDeviationMeters <= 90)
            .Select(x => x.EdgeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (provenEdges.Count > 0)
        {
            _visionEvidence.ClearEdges(
                experience.MapId,
                provenEdges);
            UpdateVisionEvidence();
        }

        var changed = _roadGraphs.RecordNavigationExperience(
            _currentRoadGraph,
            experience);

        if (changed > 0)
        {
            _roadGraphs.Save(_currentRoadGraph);

            if (_currentRoadGraph.MapId.Equals(
                    _map.Text,
                    StringComparison.OrdinalIgnoreCase))
            {
                _currentRoadGraph = _roadGraphs.Load(_map.Text);
                _mapCanvas.SetRoadGraph(_currentRoadGraph);
            }
        }
    }

    private VehicleSpec? SelectedNavigationVehicle()
    {
        return _navVehicle.SelectedItem is NavVehicleItem item
            ? item.Vehicle
            : _selectedEconomicPlan?.Vehicle;
    }

    private void SelectNavigationVehicle(string vehicleId)
    {
        foreach (var item in _navVehicle.Items.OfType<NavVehicleItem>())
        {
            if (item.Vehicle?.Id.Equals(vehicleId, StringComparison.OrdinalIgnoreCase) == true)
            {
                _navVehicle.SelectedItem = item;
                return;
            }
        }
    }

    private void UpdateHazards()
    {
        _mapCanvas.SetHazards(
            _hazards.GetActive(_map.Text));
    }

    private void UpdateVisionEvidence()
    {
        _mapCanvas.SetVisionEvidence(
            _visionEvidence.GetActive(
                _map.Text));
    }


    private async Task CalibrateTargetMarkerAsync()
    {
        if (!_settings.VisionMapRegion.IsValid)
        {
            MessageBox.Show(
                "请先框选“游戏地图视觉区域”。");
            return;
        }

        try
        {
            using var screenshot =
                _capture.Capture(
                    _settings.GameWindowTitleContains,
                    _settings.VisionMapRegion);

            using var calibrator =
                new TargetMarkerCalibrationForm(
                    screenshot);

            if (calibrator.ShowDialog(this) !=
                DialogResult.OK)
                return;

            var profile =
                _targetMarkerDetector.CreateProfile(
                    screenshot,
                    calibrator.SelectedPixel);

            if (!profile.IsValid)
            {
                MessageBox.Show(
                    "目标标记样本颜色特征不足。请确保点击的是地图中的彩色目标标记中心。");
                return;
            }

            _settings.TargetMarkerProfile =
                profile;
            _settings.VisualTargetNavigationEnabled =
                true;
            _settings.Save();

            _visualTargetCheck.Checked = true;

            UpdateCalibrationStatus();

            _routeSummary.Text =
                "目标图标已校准 · Hue " +
                profile.HueDeg.ToString("F0") +
                "° · 容差 ±" +
                profile.HueToleranceDeg.ToString("F0") +
                "°";

            await TestVisualRegistrationAsync(
                silent: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "目标标记校准失败：" +
                ex.Message);
        }
    }

    private async Task TestVisualRegistrationAsync(
        bool silent = false)
    {
        if (!_settings.VisionMapRegion.IsValid)
        {
            if (!silent)
            {
                MessageBox.Show(
                    "请先框选“游戏地图视觉区域”。");
            }
            return;
        }

        try
        {
            using var screenshot =
                _capture.Capture(
                    _settings.GameWindowTitleContains,
                    _settings.VisionMapRegion);

            var mapId =
                _map.Text;

            var memory =
                _visualMapMemory.Get(
                    mapId);

            var hint =
                _lastMapRegistration?.IsValid == true
                    ? _lastMapRegistration
                    : memory.LastRegistration;

            var registration =
                await _mapVisualRegistration.RegisterAsync(
                    mapId,
                    screenshot,
                    hint);

            if (registration?.IsValid != true)
            {
                _visualMapMemory.RecordRegistrationFailure(
                    mapId);

                if (!silent)
                {
                    MessageBox.Show(
                        "视觉配准失败。请确认游戏中打开的是当前地图，并尽量只框选地图本体。");
                }

                UpdateCalibrationStatus();
                return;
            }

            var accepted =
                registration.Confidence >=
                _settings.VisualMapMinRegistrationConfidence;

            if (accepted)
            {
                _lastMapRegistration =
                    registration;

                _visualMapMemory.RecordRegistration(
                    mapId,
                    registration);

                _mapCanvas.SetVisualMapState(
                    registration,
                    _lastVisualTargetDetection?.Success == true
                        ? _lastVisualTargetDetection.Point
                        : memory.LastVisualTarget,
                    _lastVisualTargetDetection?.Success == true
                        ? _lastVisualTargetDetection.Confidence
                        : memory.LastTargetConfidence);
            }
            else
            {
                _visualMapMemory.RecordRegistrationFailure(
                    mapId);
            }

            UpdateCalibrationStatus();

            var centerX =
                (
                    registration.Left01 +
                    registration.Width01 * 0.5
                ) *
                MapPoint.MapSize;

            var centerY =
                (
                    1 -
                    registration.Top01 -
                    registration.Height01 * 0.5
                ) *
                MapPoint.MapSize;

            var widthUnits =
                registration.Width01 *
                MapPoint.MapSize;

            var heightUnits =
                registration.Height01 *
                MapPoint.MapSize;

            var message =
                "视觉配准 " +
                (
                    accepted
                        ? "通过"
                        : "置信度不足"
                ) +
                " · " +
                Math.Round(
                    registration.Confidence *
                    100)
                    .ToString("F0") +
                "% · 旋转 " +
                registration.RotationDeg
                    .ToString("+0;-0;0") +
                "°\r\n" +
                "视口中心 X " +
                centerX.ToString("F1") +
                " / Y " +
                centerY.ToString("F1") +
                " · 宽 " +
                widthUnits.ToString("F1") +
                " / 高 " +
                heightUnits.ToString("F1");

            _routeSummary.Text =
                message;

            if (!silent)
            {
                MessageBox.Show(
                    message);
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                MessageBox.Show(
                    "视觉配准测试失败：" +
                    ex.Message);
            }
        }
    }

    private void CalibrateVisionMapRegion()
    {
        var client =
            _capture.GetClientScreenRect(
                _settings.GameWindowTitleContains);

        if (client == null)
        {
            MessageBox.Show(
                "未找到游戏窗口，请先确认窗口标题并使用窗口化/无边框。");
            return;
        }

        using var selector =
            new RegionSelectForm();

        if (selector.ShowDialog(this) !=
            DialogResult.OK)
            return;

        var normalized =
            GameWindowCapture.ToNormalized(
                selector.SelectedScreenRectangle,
                client.Value);

        if (!normalized.IsValid)
        {
            MessageBox.Show(
                "地图视觉选区无效。");
            return;
        }

        _settings.VisionMapRegion =
            normalized;

        _visualMapMemory.Clear(
            _map.Text);
        _lastMapRegistration = null;
        _lastVisualTargetDetection = null;
        _mapCanvas.SetVisualMapState(
            null,
            null,
            0);

        _settings.Save();
        UpdateCalibrationStatus();
    }

    private void CalibrateRegion(bool player)
    {
        var client = _capture.GetClientScreenRect(_settings.GameWindowTitleContains);
        if (client == null)
        {
            MessageBox.Show("未找到游戏窗口，请先确认窗口标题并使用窗口化/无边框。");
            return;
        }

        using var selector = new RegionSelectForm();
        if (selector.ShowDialog(this) != DialogResult.OK) return;

        var normalized = GameWindowCapture.ToNormalized(
            selector.SelectedScreenRectangle,
            client.Value);

        if (!normalized.IsValid)
        {
            MessageBox.Show("选区无效。");
            return;
        }

        if (player) _settings.PlayerRegion = normalized;
        else _settings.TargetRegion = normalized;

        _settings.Save();
        UpdateCalibrationStatus();
    }



    private async Task AutoExtractRoadsAsync(bool force)
    {
        if (string.IsNullOrWhiteSpace(_map.Text))
            return;

        var mapId = _map.Text;

        if (_autoExtractMapId != null &&
            _autoExtractMapId.Equals(mapId, StringComparison.OrdinalIgnoreCase))
            return;

        if (!force &&
            _currentRoadGraph.Edges.Any(e =>
                e.Source.Equals("auto", StringComparison.OrdinalIgnoreCase)))
            return;

        _autoRoadCts?.Cancel();

        var localCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        _autoRoadCts = localCts;
        _autoExtractMapId = mapId;
        _autoRoadButton.Enabled = false;

        try
        {
            UpdateRoadGraphStatus("正在分析真实地图道路概率…");

            var result = await _autoRoadExtractor.ExtractAsync(
                mapId,
                localCts.Token);

            if (!_map.Text.Equals(mapId, StringComparison.OrdinalIgnoreCase))
                return;

            _roadGraphs.ReplaceAutoGraph(
                _currentRoadGraph,
                result.Graph);

            SaveRoadGraph();

            UpdateRoadGraphStatus(
                "自动识别完成：节点 " +
                result.AcceptedNodes +
                " · 边 " +
                result.AcceptedEdges +
                " · 平均置信 " +
                Math.Round(result.AverageScore * 100).ToString("F0") +
                "%");
        }
        catch (OperationCanceledException)
        {
            if (_map.Text.Equals(mapId, StringComparison.OrdinalIgnoreCase))
                UpdateRoadGraphStatus("自动道路识别超时/取消；保留现有 Road Graph。");
        }
        catch (Exception ex)
        {
            if (_map.Text.Equals(mapId, StringComparison.OrdinalIgnoreCase))
                UpdateRoadGraphStatus("自动道路识别失败：" + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_autoRoadCts, localCts))
            {
                _autoRoadCts = null;
                _autoExtractMapId = null;
                _autoRoadButton.Enabled = true;
            }

            localCts.Dispose();
        }
    }

    private RoadClass SelectedRoadClass()
    {
        return Enum.TryParse<RoadClass>(_roadClass.Text, out var roadClass)
            ? roadClass
            : RoadClass.Secondary;
    }

    private void ToggleRoadEdit()
    {
        _roadEditMode = !_roadEditMode;
        _roadEditPreviousNodeId = null;

        _roadEditButton.Text = _roadEditMode
            ? "停止手工点路"
            : "开始手工点路";

        UpdateRoadGraphStatus(
            _roadEditMode
                ? "手工点路已开启：请在左侧地图沿道路依次点击。"
                : "手工点路已停止。");
    }

    private void ToggleRoadLearning()
    {
        if (!_traceLearning.IsRecording)
        {
            if (!_settings.PlayerRegion.IsValid)
            {
                MessageBox.Show("请先在“校准”页框选当前位置坐标区域。");
                return;
            }

            MapPoint? current = TryPoint(_selfX, _selfY, out var p) ? p : null;
            _traceLearning.Start(current);
            _roadLearnButton.Text = "停止并保存实车学习";
            _liveTimer.Start();

            UpdateRoadGraphStatus(
                "实车学习已开启。按正常道路驾驶，程序只读取屏幕坐标。");
            return;
        }

        var learned = _traceLearning.StopAndSimplify();
        _roadLearnButton.Text = "开始实车学习";

        if (!_live)
            _liveTimer.Stop();

        if (learned.Count >= 2)
        {
            _roadGraphs.MergeTrace(
                _currentRoadGraph,
                learned,
                SelectedRoadClass());
            SaveRoadGraph();

            UpdateRoadGraphStatus(
                "实车学习完成：合并 " +
                learned.Count +
                " 个简化道路节点。");
        }
        else
        {
            UpdateRoadGraphStatus("实车学习结束：有效轨迹不足，未写入道路图。");
        }
    }

    private void SaveRoadGraph()
    {
        _roadGraphs.Save(_currentRoadGraph);
        _currentRoadGraph = _roadGraphs.Load(_map.Text);
        _mapCanvas.SetRoadGraph(_currentRoadGraph);
    }

    private void UpdateRoadGraphStatus(string? message = null)
    {
        var stats = _roadGraphs.GetStats(_currentRoadGraph);

        _roadGraphStatus.Text =
            (string.IsNullOrWhiteSpace(message) ? "" : message + "\r\n") +
            "地图：" + _map.Text + "\r\n" +
            "节点 " + stats.Nodes +
            " · 道路边 " + stats.Edges +
            " · 已验证 " + stats.VerifiedEdges +
            " · 实车学习 " + stats.LearnedEdges +
            " · 自动 " + stats.AutoEdges +
            " · 实车速度学习 " + stats.LocalSpeedLearnedEdges +
            " · AI学习 " + stats.AiLearnedEdges +
            "\r\n网络总长 " +
            stats.NetworkKm.ToString("F2") +
            " km · 临时危险 " +
            _hazards.GetActive(_map.Text).Count +
            " · 视觉证据 " +
            _visionEvidence.GetActive(_map.Text).Count;
    }

    private void UpdateCalibrationStatus()
    {
        var mapId =
            string.IsNullOrWhiteSpace(_map.Text)
                ? _settings.CurrentMap
                : _map.Text;

        var memory =
            _visualMapMemory.Get(mapId);

        var marker =
            _settings.TargetMarkerProfile?.IsValid == true
                ? "已校准 · Hue " +
                  _settings.TargetMarkerProfile.HueDeg.ToString("F0") +
                  "°"
                : "未校准";

        var registration =
            memory.LastRegistration?.IsValid == true
                ? "已记忆 · " +
                  Math.Round(
                      memory.LastRegistration.Confidence *
                      100)
                      .ToString("F0") +
                  "% · " +
                  memory.LastRegistration.RotationDeg
                      .ToString("+0;-0;0") +
                  "°"
                : "无";

        _calibrationStatus.Text =
            "窗口：" + _settings.GameWindowTitleContains + "\r\n" +
            "当前位置区域：" + FormatRegion(_settings.PlayerRegion) + "\r\n" +
            "目标坐标 OCR 区域：" + FormatRegion(_settings.TargetRegion) + "\r\n" +
            "游戏地图视觉区域：" + FormatRegion(_settings.VisionMapRegion) + "\r\n" +
            "目标图标：" + marker + "\r\n" +
            "当前地图视觉记忆：" + registration +
            " · 成功 " + memory.SuccessfulRegistrations +
            " / 失败 " + memory.FailedRegistrations;
    }

    private static string FormatRegion(NormalizedRegion region)
    {
        return region.IsValid
            ? region.X.ToString("P1") + ", " +
              region.Y.ToString("P1") + ", " +
              region.Width.ToString("P1") + " × " +
              region.Height.ToString("P1")
            : "未校准";
    }

    private void SetSelf(MapPoint p)
    {
        _selfX.Text = p.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        _selfY.Text = p.Y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void SetTarget(MapPoint p)
    {
        _targetX.Text = p.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        _targetY.Text = p.Y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        UpdateMapState();
    }

    private void UpdateMapState()
    {
        MapPoint? self = TryPoint(_selfX, _selfY, out var s) ? s : null;
        MapPoint? target = TryPoint(_targetX, _targetY, out var t) ? t : null;
        _mapCanvas.SetState(self, target, _route);
    }


    private static string CueSpeechKey(
        NavigationCue cue)
    {
        if (cue.Arrived)
            return "arrive";

        var bucket = cue.NextDistanceMeters switch
        {
            <= 30 => 0,
            <= 80 => 1,
            <= 260 => 2,
            _ => 3
        };

        return
            cue.ManeuverRoutePointIndex +
            ":" +
            cue.ManeuverKind +
            ":" +
            bucket;
    }

    private void Speak(string text)
    {
        try
        {
            _tts.SpeakAsyncCancelAll();
            _tts.SpeakAsync(text);
        }
        catch
        {
        }
    }

    private static bool TryPoint(TextBox xBox, TextBox yBox, out MapPoint point)
    {
        point = default;

        if (!double.TryParse(
                xBox.Text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var x) ||
            !double.TryParse(
                yBox.Text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var y))
            return false;

        point = new MapPoint(x, y);
        return point.IsInsideMap;
    }

    private TabPage NewTab(string text) => new(text)
    {
        BackColor = BackColor,
        ForeColor = ForeColor
    };

    private static FlowLayoutPanel Flow() => new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(12),
        BackColor = Color.FromArgb(18, 20, 24)
    };

    private static Label Header(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.White,
        Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold),
        Margin = new Padding(0, 12, 0, 6)
    };

    private static Button Btn(string text) => new()
    {
        Text = text,
        AutoSize = true,
        BackColor = Color.FromArgb(42, 48, 58),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(2, 4, 4, 4)
    };

    private static Control CoordRow(TextBox x, TextBox y)
    {
        var row = new FlowLayoutPanel
        {
            Width = 390,
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight
        };

        x.Width = 145;
        y.Width = 145;

        row.Controls.Add(new Label
        {
            Text = "X",
            AutoSize = true,
            ForeColor = Color.Silver,
            Padding = new Padding(0, 7, 0, 0)
        });
        row.Controls.Add(x);
        row.Controls.Add(new Label
        {
            Text = "Y",
            AutoSize = true,
            ForeColor = Color.Silver,
            Padding = new Padding(0, 7, 0, 0)
        });
        row.Controls.Add(y);

        return row;
    }

    private sealed class NavVehicleItem
    {
        public VehicleSpec? Vehicle { get; }
        private readonly string _label;

        public NavVehicleItem(VehicleSpec? vehicle, string label)
        {
            Vehicle = vehicle;
            _label = label;
        }

        public override string ToString() => _label;
    }

    private sealed class EconomyRow
    {
        public EconomicPlan Plan { get; }
        public string Vehicle => Plan.Vehicle.NameZh;
        public string Destination => Plan.Destination.Label;
        public string Mode => Plan.LoadMode + "/" + (Plan.RoundTrip ? "往返" : "单程");
        public string Net => "$" + Plan.SessionNet.ToString("F0");
        public string Rate => "$" + Plan.SessionPerMinute.ToString("F1");
        public string Distance => Plan.DirectDistanceKm.ToString("F1") + "km";

        public EconomyRow(EconomicPlan plan) => Plan = plan;
    }
}
