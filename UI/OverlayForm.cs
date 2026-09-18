using System.Drawing;
using WardogsNavigator.Services;

namespace WardogsNavigator.UI;

public sealed class OverlayForm : Form
{
    private readonly Label _icon = new();
    private readonly Label _main = new();
    private readonly Label _sub = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(18, 20, 24);
        Opacity = 0.93;
        Width = 520;
        Height = 168;
        Padding = new Padding(12);

        _icon.Location = new Point(14, 15);
        _icon.Size = new Size(72, 70);
        _icon.Font = new Font(
            "Segoe UI Symbol",
            38,
            FontStyle.Bold);
        _icon.ForeColor = Color.LimeGreen;
        _icon.TextAlign = ContentAlignment.MiddleCenter;
        _icon.Text = "↑";

        _main.Location = new Point(94, 14);
        _main.Size = new Size(408, 50);
        _main.Font = new Font(
            "Microsoft YaHei UI",
            18,
            FontStyle.Bold);
        _main.ForeColor = Color.White;
        _main.Text = "导航待命";
        _main.TextAlign = ContentAlignment.MiddleLeft;

        _sub.Location = new Point(96, 64);
        _sub.Size = new Size(406, 28);
        _sub.Font = new Font(
            "Microsoft YaHei UI",
            10.5f);
        _sub.ForeColor = Color.FromArgb(
            165,
            220,
            180);

        _progress.Location = new Point(16, 105);
        _progress.Size = new Size(486, 10);
        _progress.Minimum = 0;
        _progress.Maximum = 1000;
        _progress.Style =
            ProgressBarStyle.Continuous;

        _status.Location = new Point(16, 121);
        _status.Size = new Size(486, 26);
        _status.Font = new Font(
            "Microsoft YaHei UI",
            9.5f);
        _status.ForeColor = Color.Silver;

        Controls.Add(_icon);
        Controls.Add(_main);
        Controls.Add(_sub);
        Controls.Add(_progress);
        Controls.Add(_status);

        StartPosition = FormStartPosition.Manual;
        var wa =
            Screen.PrimaryScreen?.WorkingArea ??
            new Rectangle(0, 0, 1920, 1080);
        Location = new Point(
            wa.Right - Width - 24,
            wa.Top + 40);
    }

    public void UpdateCue(
        NavigationCue cue,
        RoutePlan? route)
    {
        _icon.Text = IconFor(
            cue.ManeuverKind);

        _icon.ForeColor = cue.OffRoute
            ? Color.OrangeRed
            : cue.Arrived
                ? Color.DeepSkyBlue
                : Color.LimeGreen;

        _main.Text = cue.Instruction;

        if (route == null)
        {
            _sub.Text =
                "剩余 " +
                cue.RemainingKm.ToString("F2") +
                " km";
        }
        else
        {
            _sub.Text =
                "剩余 " +
                cue.RemainingKm.ToString("F2") +
                " km   ETA " +
                cue.RemainingMinutes.ToString("F1") +
                " min   下一动作 " +
                NavigationGuidance.FormatDistance(
                    cue.NextDistanceMeters) +
                (route.UsedFallback
                    ? "   [直线回退]"
                    : "");
        }

        _progress.Value = Math.Clamp(
            (int)Math.Round(
                cue.Progress01 * 1000),
            0,
            1000);

        if (cue.Arrived)
        {
            _status.ForeColor =
                Color.DeepSkyBlue;
            _status.Text =
                "已完成导航";
        }
        else if (cue.OffRoute)
        {
            _status.ForeColor =
                Color.OrangeRed;
            _status.Text =
                "偏离路线 " +
                cue.DeviationMeters.ToString("F0") +
                " m" +
                (cue.ShouldReroute
                    ? " · 正在重新规划"
                    : " · 正在确认位置");
        }
        else
        {
            _status.ForeColor =
                Color.Silver;
            _status.Text =
                "路线进度 " +
                (cue.Progress01 * 100)
                    .ToString("F0") +
                "% · 路线偏差 " +
                cue.DeviationMeters
                    .ToString("F0") +
                " m";
        }
    }

    private static string IconFor(
        NavigationManeuverKind kind) =>
        kind switch
        {
            NavigationManeuverKind.SlightLeft =>
                "↖",
            NavigationManeuverKind.SlightRight =>
                "↗",
            NavigationManeuverKind.TurnLeft =>
                "←",
            NavigationManeuverKind.TurnRight =>
                "→",
            NavigationManeuverKind.UTurn =>
                "↶",
            NavigationManeuverKind.Arrive =>
                "●",
            _ =>
                "↑"
        };
}
