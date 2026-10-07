using System.Drawing;

namespace FinanceTracker;

internal static class UiKit
{
    public static readonly Color Accent = Color.FromArgb(31, 78, 121);
    public static readonly Color Accent2 = Color.FromArgb(46, 116, 181);
    public static readonly Color Good = Color.FromArgb(32, 122, 72);
    public static readonly Color Warn = Color.FromArgb(196, 111, 0);
    public static readonly Color Bad = Color.FromArgb(176, 35, 35);
    public static readonly Color Surface = Color.White;
    public static readonly Color Background = Color.FromArgb(244, 247, 250);
    public static readonly Color Border = Color.FromArgb(218, 225, 232);
    public static readonly Color Text = Color.FromArgb(32, 39, 46);
    public static readonly Color Muted = Color.FromArgb(100, 112, 122);

    public static Button PrimaryButton(string text, EventHandler? click = null)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = false,
            Height = 42,
            Width = 150,
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(6)
        };
        b.FlatAppearance.BorderSize = 0;
        if (click != null) b.Click += click;
        return b;
    }

    public static Button SecondaryButton(string text, EventHandler? click = null)
    {
        var b = PrimaryButton(text, click);
        b.BackColor = Color.White;
        b.ForeColor = Accent;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Accent;
        return b;
    }

    public static DataGridView Grid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.None,
            GridColor = Border,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeight = 38,
            RowTemplate = { Height = 34 },
            Font = new Font("Segoe UI", 9.5F),
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(235, 241, 247),
                ForeColor = Text,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                SelectionBackColor = Color.FromArgb(235, 241, 247),
                SelectionForeColor = Text
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Text,
                SelectionBackColor = Color.FromArgb(220, 234, 247),
                SelectionForeColor = Text,
                Padding = new Padding(4, 2, 4, 2)
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(249, 251, 253)
            }
        };
    }

    public static Panel MetricCard(string title, out Label valueLabel)
    {
        var p = new Panel
        {
            BackColor = Surface,
            Margin = new Padding(6),
            Padding = new Padding(16, 12, 16, 12),
            MinimumSize = new Size(150, 104)
        };
        p.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
        };
        var titleLabel = new Label
        {
            Text = title,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5F),
            Dock = DockStyle.Top,
            Height = 56,
            AutoEllipsis = false
        };
        valueLabel = new Label
        {
            Text = "0 тыс. ₽",
            ForeColor = Text,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        p.Controls.Add(valueLabel);
        p.Controls.Add(titleLabel);
        return p;
    }

    public static Panel Section(string title, Control content)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(12) };
        panel.Paint += (_, e) => { using var pen = new Pen(Border); e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1); };
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(content);
        panel.Controls.Add(label);
        return panel;
    }

    public static NumericUpDown MoneyInput(decimal max = 1_000_000_000m)
    {
        return new NumericUpDown
        {
            DecimalPlaces = 3,
            ThousandsSeparator = true,
            Maximum = max,
            Minimum = 0,
            Width = 240,
            Font = new Font("Segoe UI", 10F)
        };
    }

    public static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5F),
        ForeColor = Text,
        Margin = new Padding(3, 8, 3, 2)
    };

    public static TextBox TextInput(int width = 300) => new()
    {
        Width = width,
        Font = new Font("Segoe UI", 10F)
    };

    public static ComboBox Combo(int width = 300) => new()
    {
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Font = new Font("Segoe UI", 10F)
    };

    public static void Error(IWin32Window owner, Exception ex) => MessageBox.Show(owner, ex.Message, "Финансовый учёт", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
