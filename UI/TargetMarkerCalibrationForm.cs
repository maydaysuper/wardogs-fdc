using System.Drawing;

namespace WardogsNavigator.UI;

public sealed class TargetMarkerCalibrationForm : Form
{
    private readonly Bitmap _source;
    private readonly PictureBox _picture =
        new();
    private readonly Button _ok =
        new();

    private Point? _selected;

    public Point SelectedPixel =>
        _selected ??
        Point.Empty;

    public TargetMarkerCalibrationForm(
        Bitmap screenshot)
    {
        _source =
            new Bitmap(
                screenshot);

        Text =
            "校准目标标记";
        StartPosition =
            FormStartPosition.CenterParent;
        Width = 980;
        Height = 820;
        MinimumSize =
            new Size(
                680,
                560);

        BackColor =
            Color.FromArgb(
                18,
                20,
                24);
        ForeColor =
            Color.White;

        var instruction =
            new Label
            {
                Dock = DockStyle.Top,
                Height = 54,
                Padding =
                    new Padding(
                        12,
                        9,
                        12,
                        4),
                ForeColor =
                    Color.Gainsboro,
                Text =
                    "请直接点击游戏地图中的“目标标记图标”中心。程序只学习该图标的视觉颜色特征，不上传图片。"
            };

        _picture.Dock =
            DockStyle.Fill;
        _picture.BackColor =
            Color.Black;
        _picture.SizeMode =
            PictureBoxSizeMode.Zoom;
        _picture.Image =
            new Bitmap(
                _source);
        _picture.MouseClick +=
            PictureOnMouseClick;

        var footer =
            new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection =
                    FlowDirection.RightToLeft,
                Padding =
                    new Padding(
                        8)
            };

        _ok.Text =
            "保存目标标记样本";
        _ok.AutoSize = true;
        _ok.Enabled = false;
        _ok.DialogResult =
            DialogResult.OK;

        var cancel =
            new Button
            {
                Text = "取消",
                AutoSize = true,
                DialogResult =
                    DialogResult.Cancel
            };

        footer.Controls.Add(_ok);
        footer.Controls.Add(cancel);

        Controls.Add(_picture);
        Controls.Add(instruction);
        Controls.Add(footer);

        AcceptButton = _ok;
        CancelButton = cancel;
    }

    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            var image =
                _picture.Image;
            _picture.Image = null;
            image?.Dispose();
            _source.Dispose();
        }

        base.Dispose(disposing);
    }

    private void PictureOnMouseClick(
        object? sender,
        MouseEventArgs e)
    {
        var imageRect =
            GetImageRectangle();

        if (!imageRect.Contains(
                e.Location))
            return;

        var u =
            (e.X -
             imageRect.Left) /
            (double)Math.Max(
                1,
                imageRect.Width);

        var v =
            (e.Y -
             imageRect.Top) /
            (double)Math.Max(
                1,
                imageRect.Height);

        var x =
            Math.Clamp(
                (int)Math.Round(
                    u *
                    (
                        _source.Width -
                        1
                    )),
                0,
                _source.Width - 1);

        var y =
            Math.Clamp(
                (int)Math.Round(
                    v *
                    (
                        _source.Height -
                        1
                    )),
                0,
                _source.Height - 1);

        _selected =
            new Point(
                x,
                y);

        _ok.Enabled = true;

        RenderSelection();
    }

    private void RenderSelection()
    {
        if (_selected is not Point p)
            return;

        var preview =
            new Bitmap(
                _source);

        using (var g =
               Graphics.FromImage(preview))
        {
            using var shadow =
                new Pen(
                    Color.Black,
                    5);

            using var pen =
                new Pen(
                    Color.Lime,
                    2);

            const int radius = 16;

            g.DrawEllipse(
                shadow,
                p.X - radius,
                p.Y - radius,
                radius * 2,
                radius * 2);

            g.DrawEllipse(
                pen,
                p.X - radius,
                p.Y - radius,
                radius * 2,
                radius * 2);

            g.DrawLine(
                shadow,
                p.X - 24,
                p.Y,
                p.X + 24,
                p.Y);

            g.DrawLine(
                shadow,
                p.X,
                p.Y - 24,
                p.X,
                p.Y + 24);

            g.DrawLine(
                pen,
                p.X - 24,
                p.Y,
                p.X + 24,
                p.Y);

            g.DrawLine(
                pen,
                p.X,
                p.Y - 24,
                p.X,
                p.Y + 24);
        }

        var old =
            _picture.Image;
        _picture.Image =
            preview;

        if (old != null &&
            !ReferenceEquals(
                old,
                _source))
            old.Dispose();
    }

    private Rectangle GetImageRectangle()
    {
        var client =
            _picture.ClientRectangle;

        if (_source.Width <= 0 ||
            _source.Height <= 0 ||
            client.Width <= 0 ||
            client.Height <= 0)
            return client;

        var imageAspect =
            _source.Width /
            (double)_source.Height;

        var clientAspect =
            client.Width /
            (double)client.Height;

        if (imageAspect >
            clientAspect)
        {
            var width =
                client.Width;

            var height =
                (int)Math.Round(
                    width /
                    imageAspect);

            return new Rectangle(
                0,
                (
                    client.Height -
                    height
                ) /
                2,
                width,
                height);
        }

        var h =
            client.Height;

        var w =
            (int)Math.Round(
                h *
                imageAspect);

        return new Rectangle(
            (
                client.Width -
                w
            ) /
            2,
            0,
            w,
            h);
    }
}
