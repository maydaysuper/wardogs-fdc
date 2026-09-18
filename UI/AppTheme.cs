using System.Drawing;

namespace WardogsNavigator.UI;

public static class AppTheme
{
    public static readonly Color Background = Color.FromArgb(15, 18, 23);
    public static readonly Color Panel = Color.FromArgb(22, 26, 33);
    public static readonly Color Input = Color.FromArgb(31, 36, 45);
    public static readonly Color Border = Color.FromArgb(55, 64, 78);
    public static readonly Color Accent = Color.FromArgb(68, 190, 130);
    public static readonly Color AccentBlue = Color.FromArgb(92, 175, 255);
    public static readonly Color Text = Color.FromArgb(232, 236, 242);
    public static readonly Color Muted = Color.FromArgb(158, 168, 184);
    private static readonly Font UiFont =
        new("Microsoft YaHei UI", 9.5f);

    public static void Apply(Control root)
    {
        root.Font = UiFont;

        foreach (Control control in root.Controls)
        {
            ApplyOne(control);
            if (control.HasChildren)
                Apply(control);
        }
    }

    private static void ApplyOne(Control control)
    {
        switch (control)
        {
            case TabPage page:
                page.BackColor = Background;
                page.ForeColor = Text;
                break;

            case Panel panel:
                panel.BackColor = Background;
                panel.ForeColor = Text;
                break;

            case FlowLayoutPanel flow:
                flow.BackColor = Background;
                flow.ForeColor = Text;
                break;

            case TextBox text:
                text.BackColor = Input;
                text.ForeColor = Text;
                text.BorderStyle = BorderStyle.FixedSingle;
                break;

            case ComboBox combo:
                combo.BackColor = Input;
                combo.ForeColor = Text;
                combo.FlatStyle = FlatStyle.Flat;
                break;

            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor =
                    Color.FromArgb(49, 58, 70);
                button.BackColor = Color.FromArgb(36, 43, 53);
                button.ForeColor = Text;
                button.Padding = new Padding(6, 2, 6, 2);
                break;

            case CheckBox check:
                check.ForeColor = Text;
                break;

            case DataGridView grid:
                grid.BackgroundColor = Panel;
                grid.BorderStyle = BorderStyle.None;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Input;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.BackColor = Panel;
                grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor =
                    Color.FromArgb(43, 86, 70);
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                grid.GridColor = Border;
                break;
        }
    }
}
