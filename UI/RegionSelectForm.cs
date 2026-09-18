using System.Drawing;

namespace WardogsNavigator.UI;

public sealed class RegionSelectForm : Form
{
    private Point _start;
    private Point _end;
    private bool _dragging;

    public Rectangle SelectedScreenRectangle { get; private set; }

    public RegionSelectForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0.28;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _start = e.Location;
        _end = e.Location;
        _dragging = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        _end = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        _end = e.Location;
        var local = Rect(_start, _end);
        SelectedScreenRectangle = new Rectangle(
            local.Left + Bounds.Left,
            local.Top + Bounds.Top,
            local.Width,
            local.Height);

        DialogResult = SelectedScreenRectangle.Width > 5 && SelectedScreenRectangle.Height > 5
            ? DialogResult.OK
            : DialogResult.Cancel;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Escape) return;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_dragging) return;

        var r = Rect(_start, _end);
        using var fill = new SolidBrush(Color.FromArgb(55, Color.DeepSkyBlue));
        using var pen = new Pen(Color.DeepSkyBlue, 3);
        e.Graphics.FillRectangle(fill, r);
        e.Graphics.DrawRectangle(pen, r);
    }

    private static Rectangle Rect(Point a, Point b) =>
        Rectangle.FromLTRB(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Max(a.X, b.X),
            Math.Max(a.Y, b.Y));
}
