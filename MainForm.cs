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
    private readonly TraceLearningService _traceLearning = new();
    private readonly AutoRoadExtractor _autoRoadExtractor;
    private readonly RoutePlanner _routes;
    private readonly GameWindowCapture _capture = new();
    private readonly CoordinateRecognizer _ocr = new();
    private readonly DeepSeekClient _ai = new();
    private readonly SpeechSynthesizer _tts = new();
    private readonly OverlayForm _overlay = new();

    private readonly MapCanvas _mapCanvas = new();
    private readonly ComboBox _map = new();
    private readonly ComboBox _routeMode = new();
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
    private readonly TextBox _windowTitle = new();
    private readonly Label _calibrationStatus = new();
    private readonly Button _liveButton = new();
    private readonly ComboBox _roadClass = new();
    private readonly Label _roadGraphStatus = new();
    private readonly Button _roadEditButton = new();
    private readonly Button _roadLearnButton = new();
    private readonly Button _autoRoadButton = new();
    private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 1400 };

    private RoutePlan? _route;
    private EconomicPlan? _selectedEconomicPlan;
    private RoadGraph _currentRoadGraph = new();
    private bool _roadEditMode;
    private string? _roadEditPreviousNodeId;
    private MapPoint? _lastLivePoint;
    private double? _headingDeg;
    private bool _live;
    private DateTime _lastSpoken = DateTime.MinValue;
    private int _lastCueIndex = -1;
    private CancellationTokenSource? _planCts;

    public MainForm()
    {
        _routes = new RoutePlanner(_mapAssets, _roadGraphs);
        _autoRoadExtractor = new AutoRoadExtractor(_mapAssets);

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

        p.Controls.Add(Header("地图 / 路线"));
        p.Controls.Add(_map);
        p.Controls.Add(_routeMode);

        p.Controls.Add(Header("当前位置 X / Y"));
        p.Controls.Add(CoordRow(_selfX, _selfY));

        p.Controls.Add(Header("目标 X / Y"));
        p.Controls.Add(CoordRow(_targetX, _targetY));

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

        var readTarget = Btn("从屏幕读取目标");
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

        p.Controls.Add(self);
        p.Controls.Add(target);

        _calibrationStatus.Width = 400;
        _calibrationStatus.Height = 120;
        p.Controls.Add(_calibrationStatus);

        p.Controls.Add(new Label
        {
            AutoSize = false,
            Width = 400,
            Height = 170,
            ForeColor = Color.Silver,
            Text =
                "校准区域按游戏窗口客户区比例保存，因此 1080p / 1440p / 4K 切换后仍可复用。\r\n\r\n" +
                "程序只抓取屏幕像素；不读取进程内存、不注入、不安装驱动。"
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
            await SwitchMapAsync();
        };

        _routeMode.SelectedIndexChanged += (_, _) =>
        {
            if (Enum.TryParse<RoutePreference>(_routeMode.Text, out var pref))
            {
                _settings.RoutePreference = pref;
                _settings.Save();
            }
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

        UpdateCalibrationStatus();
    }

    private async Task SwitchMapAsync()
    {
        if (_traceLearning.IsRecording)
        {
            _traceLearning.Cancel();
            _roadLearnButton.Text = "开始实车学习";
            if (!_live)
                _liveTimer.Stop();
        }

        var id = _map.Text;
        var bitmap = await _mapAssets.GetBitmapAsync(id);
        _currentRoadGraph = _roadGraphs.Load(id);
        _roadEditPreviousNodeId = null;
        _mapCanvas.SetMap(bitmap, _maps.Get(id));
        _mapCanvas.SetRoadGraph(_currentRoadGraph);
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
        _planCts = new CancellationTokenSource();
        var token = _planCts.Token;

        try
        {
            _routeSummary.Text = "路线：正在分析真实地图道路…";

            var vehicle = _selectedEconomicPlan?.Vehicle;
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
                token);

            var cue = RoutePlanner.BuildCue(_route, self, _headingDeg);

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
            UpdateMapState();

            if (speak && _settings.SpeakNavigation)
                Speak(cue.Instruction + "。全程 " + _route.DistanceKm.ToString("F1") + " 公里。");
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
        var point = await ReadRegionAsync(_settings.TargetRegion);
        if (point is not MapPoint value) return;

        SetTarget(value);
        await PlanRouteAsync(false);
    }

    private async Task<MapPoint?> ReadRegionAsync(NormalizedRegion region)
    {
        try
        {
            using var bitmap = _capture.Capture(_settings.GameWindowTitleContains, region);
            var result = await _ocr.RecognizeAsync(bitmap);

            if (!result.Success)
            {
                _routeSummary.Text =
                    "识别失败：" + result.Error +
                    (string.IsNullOrWhiteSpace(result.RawText) ? "" : " [" + result.RawText + "]");
                return null;
            }

            return result.Point;
        }
        catch (Exception ex)
        {
            _routeSummary.Text = "截屏/OCR失败：" + ex.Message;
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
            _liveTimer.Start();
        }
        else
        {
            if (!_traceLearning.IsRecording)
                _liveTimer.Stop();
        }
    }

    private async Task LiveTickAsync()
    {
        if (!_live && !_traceLearning.IsRecording) return;

        var current = await ReadRegionAsync(_settings.PlayerRegion);
        if (current is not MapPoint now) return;

        if (_lastLivePoint is MapPoint old && old.DistanceMeters(now) >= 3)
            _headingDeg = old.BearingDegTo(now);

        _lastLivePoint = now;
        SetSelf(now);

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

        if (_settings.AutoReadTarget && _settings.TargetRegion.IsValid)
        {
            var target = await ReadRegionAsync(_settings.TargetRegion);
            if (target is MapPoint targetPoint)
            {
                if (!TryPoint(_targetX, _targetY, out var oldTarget) ||
                    oldTarget.DistanceMeters(targetPoint) > 12)
                {
                    SetTarget(targetPoint);
                    await PlanRouteAsync(false);
                }
            }
        }

        if (_route == null)
        {
            await PlanRouteAsync(false);
            return;
        }

        if (RoutePlanner.DistanceToRouteMeters(_route, now) > 150)
        {
            await PlanRouteAsync(false);
            return;
        }

        var cue = RoutePlanner.BuildCue(_route, now, _headingDeg);
        _overlay.UpdateCue(cue, _route);
        UpdateMapState();

        if (_settings.SpeakNavigation &&
            !cue.Arrived &&
            (cue.RouteIndex != _lastCueIndex ||
             DateTime.UtcNow - _lastSpoken > TimeSpan.FromSeconds(20)))
        {
            _lastCueIndex = cue.RouteIndex;
            _lastSpoken = DateTime.UtcNow;
            Speak(cue.Instruction);
        }
        else if (cue.Arrived && cue.RouteIndex != _lastCueIndex)
        {
            _lastCueIndex = cue.RouteIndex;
            Speak("已到达目的地");
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

            var routeText = _route == null
                ? "当前没有路线。"
                : "路线：" + _route.Preference +
                  "，" + _route.DistanceKm.ToString("F2") + "km" +
                  "，ETA " + _route.EstimatedMinutes.ToString("F1") + "min" +
                  "，来源 " + _route.Source + "。";

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

        if (!force &&
            _currentRoadGraph.Edges.Any(e =>
                e.Source.Equals("auto", StringComparison.OrdinalIgnoreCase)))
            return;

        _autoRoadButton.Enabled = false;

        try
        {
            UpdateRoadGraphStatus("正在分析真实地图道路概率…");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var result = await _autoRoadExtractor.ExtractAsync(
                _map.Text,
                cts.Token);

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
            UpdateRoadGraphStatus("自动道路识别超时/取消；保留现有 Road Graph。");
        }
        catch (Exception ex)
        {
            UpdateRoadGraphStatus("自动道路识别失败：" + ex.Message);
        }
        finally
        {
            _autoRoadButton.Enabled = true;
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
            "\r\n网络总长 " + stats.NetworkKm.ToString("F2") + " km";
    }

    private void UpdateCalibrationStatus()
    {
        _calibrationStatus.Text =
            "窗口：" + _settings.GameWindowTitleContains + "\r\n" +
            "当前位置区域：" + FormatRegion(_settings.PlayerRegion) + "\r\n" +
            "目标区域：" + FormatRegion(_settings.TargetRegion);
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
