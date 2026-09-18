using System.Drawing;

namespace WardogsNavigator.UI;

public sealed class OverlayForm : Form
{
    private readonly Label _main = new();
    private readonly Label _sub = new();

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(18, 20, 24);
        Opacity = 0.90;
        Width = 430;
        Height = 118;
        Padding = new Padding(14);

        _main.Dock = DockStyle.Top;
        _main.Height = 52;
        _main.Font = new Font("Microsoft YaHei UI", 19, FontStyle.Bold);
        _main.ForeColor = Color.White;
        _main.Text = "导航待命";

        _sub.Dock = DockStyle.Fill;
        _sub.Font = new Font("Microsoft YaHei UI", 10.5f);
        _sub.ForeColor = Color.FromArgb(165, 220, 180);

        Controls.Add(_sub);
        Controls.Add(_main);

        StartPosition = FormStartPosition.Manual;
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(wa.Right - Width - 24, wa.Top + 40);
    }

    public void UpdateCue(NavigationCue cue, RoutePlan? route)
    {
        _main.Text = cue.Instruction;
        if (route == null)
        {
            _sub.Text = "剩余 " + cue.RemainingKm.ToString("F2") + " km";
            return;
        }

        _sub.Text =
            "剩余 " + cue.RemainingKm.ToString("F2") + " km   " +
            "路线 " + route.DistanceKm.ToString("F2") + " km   " +
            "ETA " + route.EstimatedMinutes.ToString("F1") + " min" +
            (route.UsedFallback ? "   [直线回退]" : "");
    }
}
