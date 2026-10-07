using System.Diagnostics;
using System.Drawing;

namespace FinanceTracker;

internal sealed class MainForm : Form
{
    private readonly FinanceDatabase _db;
    private readonly TabControl _tabs = new();

    private Label _balanceValue = null!;
    private Label _freeValue = null!;
    private Label _returnValue = null!;
    private Label _paymentsValue = null!;
    private Label _monthPaymentsValue = null!;
    private Label _optionalPaymentsValue = null!;
    private Label _expectedValue = null!;
    private Label _forecastValue = null!;
    private Label _forecastDetails = null!;
    private Control _balanceCard = null!;
    private Control _returnCard = null!;
    private Control _paymentsCard = null!;
    private Control _monthPaymentsCard = null!;
    private Control _optionalPaymentsCard = null!;
    private Control _freeCard = null!;
    private Control _expectedCard = null!;
    private Control _forecastPanel = null!;
    private Panel _metricLegendHost = null!;
    private Panel _metricLegendPanel = null!;
    private RichTextBox _metricLegendText = null!;
    private readonly Control[] _metricCards = new Control[7];
    private readonly string[] _metricLegendTexts = new string[7];
    private int _activeMetricLegendIndex = -1;
    private readonly DataGridView _obligations = UiKit.Grid();
    private readonly DataGridView _expected = UiKit.Grid();
    private readonly DataGridView _transactions = UiKit.Grid();
    private readonly DateTimePicker _transactionsFrom = CreateIntervalDatePicker();
    private readonly DateTimePicker _transactionsTo = CreateIntervalDatePicker();
    private readonly ComboBox _transactionsCategory = CreateFilterCombo();
    private readonly DataGridView _history = UiKit.Grid();
    private readonly DateTimePicker _historyFrom = CreateIntervalDatePicker();
    private readonly DateTimePicker _historyTo = CreateIntervalDatePicker();
    private bool _historyOpenOnly = true;
    private readonly DataGridView _clients = UiKit.Grid();
    private readonly DataGridView _expenses = UiKit.Grid();
    private readonly DataGridView _incomes = UiKit.Grid();
    private readonly DataGridView _accounts = UiKit.Grid();
    private readonly DataGridView _categories = UiKit.Grid();
    private readonly DataGridView _holidays = UiKit.Grid();
    private readonly Label _status = new();
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000, InitialDelay = 350, ReshowDelay = 100, ShowAlways = true };

    private static DateTimePicker CreateIntervalDatePicker() => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "dd.MM.yyyy",
        Width = 165,
        Height = 38,
        MinimumSize = new Size(165, 38),
        Font = new Font("Segoe UI", 11F),
        CalendarFont = new Font("Segoe UI", 11F)
    };

    private static ComboBox CreateFilterCombo()
    {
        var combo = new ComboBox
        {
            Width = 250,
            Height = 42,
            MinimumSize = new Size(250, 42),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 12F),
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 34,
            IntegralHeight = true
        };
        combo.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            if (e.Index >= 0)
            {
                var text = combo.GetItemText(combo.Items[e.Index]);
                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    combo.Font,
                    e.Bounds,
                    e.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            e.DrawFocusRectangle();
        };
        return combo;
    }

    public MainForm(FinanceDatabase db)
    {
        _db = db;
        Text = "Финансовый учёт";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1150, 760);
        Size = new Size(1420, 900);
        BackColor = UiKit.Background;
        Font = new Font("Segoe UI", 9.5F);

        var prevMonth = DateTime.Today.AddMonths(-1);
        _historyFrom.Value = new DateTime(prevMonth.Year, prevMonth.Month, 1);
        _historyTo.Value = new DateTime(prevMonth.Year, prevMonth.Month, DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month));
        var currentMonth = DateTime.Today;
        _transactionsFrom.Value = new DateTime(currentMonth.Year, currentMonth.Month, 1);
        _transactionsTo.Value = new DateTime(currentMonth.Year, currentMonth.Month, DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month));

        var top = BuildTopBar();
        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = new Font("Segoe UI", 10F);
        _tabs.Padding = new Point(18, 8);
        _tabs.Controls.Add(BuildDashboardTab());
        _tabs.Controls.Add(BuildTransactionsTab());
        _tabs.Controls.Add(BuildHistoryTab());
        _tabs.Controls.Add(BuildClientsTab());
        _tabs.Controls.Add(BuildExpensesTab());
        _tabs.Controls.Add(BuildIncomesTab());
        _tabs.Controls.Add(BuildAccountsTab());
        _tabs.Controls.Add(BuildSettingsTab());
        _tabs.SelectedIndexChanged += (_, _) => RefreshAll();

        _status.Dock = DockStyle.Bottom;
        _status.Height = 26;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = UiKit.Muted;
        _status.Padding = new Padding(10, 0, 0, 0);

        Controls.Add(_tabs);
        Controls.Add(_status);
        Controls.Add(top);

        Shown += (_, _) => RefreshAll();
    }

    private Control BuildTopBar()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Color.White, Padding = new Padding(16, 10, 16, 8) };
        panel.Paint += (_, e) => { using var pen = new Pen(UiKit.Border); e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1); };
        var title = new Label { Text = "Finance Tracker", Dock = DockStyle.Left, Width = 340, Font = new Font("Segoe UI", 19F, FontStyle.Bold), ForeColor = UiKit.Muted, TextAlign = ContentAlignment.MiddleLeft };
        panel.Controls.Add(title);
        return panel;
    }

    private TabPage BuildDashboardTab()
    {
        var tab = NewTab("Главная");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), BackColor = UiKit.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 278));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var metricArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = UiKit.Background };
        metricArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
        metricArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, BackColor = UiKit.Background };
        for (var i = 0; i < 7; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 7F));

        _balanceCard = UiKit.MetricCard("Общий баланс", out _balanceValue);
        _returnCard = UiKit.MetricCard("К возврату клиентам", out _returnValue);
        _paymentsCard = UiKit.MetricCard("Остаток обязательных платежей на текущий год", out _paymentsValue);
        _monthPaymentsCard = UiKit.MetricCard("Остаток обязательных платежей на текущий месяц", out _monthPaymentsValue);
        _optionalPaymentsCard = UiKit.MetricCard("Необязательные запланированные платежи на текущий год", out _optionalPaymentsValue);
        _freeCard = UiKit.MetricCard("Свободно по итогам", out _freeValue);
        _expectedCard = UiKit.MetricCard("Ожидаемое поступление в текущем месяце", out _expectedValue);
        _metricCards[0] = _balanceCard;
        _metricCards[1] = _returnCard;
        _metricCards[2] = _paymentsCard;
        _metricCards[3] = _monthPaymentsCard;
        _metricCards[4] = _optionalPaymentsCard;
        _metricCards[5] = _freeCard;
        _metricCards[6] = _expectedCard;
        for (var i = 0; i < _metricCards.Length; i++)
        {
            cards.Controls.Add(_metricCards[i], i, 0);
            var captured = i;
            WireMetricCardClick(_metricCards[i], () => ShowMetricLegend(captured));
        }
        metricArea.Controls.Add(cards, 0, 0);

        _metricLegendHost = new Panel { Dock = DockStyle.Fill, BackColor = UiKit.Background, Padding = new Padding(6, 4, 6, 4) };
        _metricLegendPanel = new Panel
        {
            BackColor = Color.FromArgb(238, 242, 246),
            Visible = false,
            Height = 124,
            Width = 430,
            Padding = new Padding(12, 9, 12, 8)
        };
        _metricLegendPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(UiKit.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, _metricLegendPanel.Width - 1, _metricLegendPanel.Height - 1);
        };
        _metricLegendText = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ForeColor = UiKit.Muted,
            BackColor = Color.FromArgb(238, 242, 246),
            Font = new Font("Segoe UI", 8.7F),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            TabStop = false,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        _metricLegendPanel.Controls.Add(_metricLegendText);
        _metricLegendHost.Controls.Add(_metricLegendPanel);
        _metricLegendHost.SizeChanged += (_, _) => PositionMetricLegend();
        metricArea.Controls.Add(_metricLegendHost, 0, 1);
        root.Controls.Add(metricArea, 0, 0);

        var quick = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4, 10, 4, 6),
            WrapContents = false,
            BackColor = UiKit.Background
        };
        var income = UiKit.PrimaryButton("+ Поступление", (_, _) => OpenTransaction(QuickAction.Income)); income.Height = 50;
        var expense = UiKit.PrimaryButton("− Расход", (_, _) => OpenTransaction(QuickAction.Expense)); expense.Height = 50;
        var ret = UiKit.PrimaryButton("↩ Возврат", (_, _) => OpenTransaction(QuickAction.ClientReturn)); ret.Height = 50;
        var transfer = UiKit.PrimaryButton("⇄ Перевод", (_, _) => OpenTransaction(QuickAction.Transfer)); transfer.Height = 50;
        var quickButtons = new[] { income, expense, ret, transfer };
        foreach (var button in quickButtons) quick.Controls.Add(button);
        quick.SizeChanged += (_, _) =>
        {
            var available = quick.ClientSize.Width - quick.Padding.Horizontal - quickButtons.Sum(b => b.Margin.Horizontal);
            var width = Math.Clamp(available / quickButtons.Length, 135, 190);
            foreach (var button in quickButtons) button.Width = width;
        };
        root.Controls.Add(quick, 0, 1);

        SetupObligationGrid();
        SetupExpectedGrid();
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = UiKit.Background };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        var obligationsSection = UiKit.Section("Ближайшие обязательства", _obligations);
        obligationsSection.Margin = new Padding(4);
        var expectedSection = UiKit.Section("Ожидаемый доход", _expected);
        expectedSection.Margin = new Padding(4);

        _forecastPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiKit.Surface,
            Margin = new Padding(4),
            Padding = new Padding(14, 12, 14, 10)
        };
        _forecastPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(UiKit.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, _forecastPanel.Width - 1, _forecastPanel.Height - 1);
        };
        var forecastHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        var forecastTitle = new Label
        {
            Text = "Прогноз на конец текущего месяца:",
            AutoSize = true,
            ForeColor = UiKit.Text,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Margin = new Padding(0, 3, 4, 0)
        };
        _forecastValue = new Label
        {
            Text = "0 тыс. ₽",
            AutoSize = true,
            ForeColor = UiKit.Text,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Margin = new Padding(0, 3, 0, 0)
        };
        forecastHeader.Controls.Add(forecastTitle);
        forecastHeader.Controls.Add(_forecastValue);
        _forecastDetails = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = UiKit.Muted,
            Font = new Font("Segoe UI", 8.6F),
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = false,
            Padding = new Padding(0, 4, 0, 0)
        };
        _forecastPanel.Controls.Add(_forecastDetails);
        _forecastPanel.Controls.Add(forecastHeader);

        bottom.Controls.Add(obligationsSection, 0, 0);
        bottom.Controls.Add(expectedSection, 1, 0);
        bottom.Controls.Add(_forecastPanel, 2, 0);
        root.Controls.Add(bottom, 0, 2);

        tab.Controls.Add(root);
        return tab;
    }

    private TabPage BuildTransactionsTab()
    {
        SetupTransactionsGrid();
        var tab = NewTab("Операции");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), BackColor = UiKit.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = NewToolbar();
        toolbar.Dock = DockStyle.Fill;
        toolbar.Height = 64;
        toolbar.Controls.Add(UiKit.PrimaryButton("+ Поступление", (_, _) => OpenTransaction(QuickAction.Income)));
        toolbar.Controls.Add(UiKit.PrimaryButton("− Расход", (_, _) => OpenTransaction(QuickAction.Expense)));
        toolbar.Controls.Add(UiKit.PrimaryButton("↩ Возврат", (_, _) => OpenTransaction(QuickAction.ClientReturn)));
        toolbar.Controls.Add(UiKit.PrimaryButton("⇄ Перевод", (_, _) => OpenTransaction(QuickAction.Transfer)));
        toolbar.Controls.Add(UiKit.SecondaryButton("Корректировка", (_, _) => OpenTransaction(QuickAction.Adjustment)));
        toolbar.Controls.Add(UiKit.SecondaryButton("Изменить", EditTransaction));
        toolbar.Controls.Add(UiKit.SecondaryButton("Удалить запись", DeleteTransaction));
        root.Controls.Add(toolbar, 0, 0);

        var filter = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(4, 7, 4, 7),
            BackColor = UiKit.Background
        };
        var prev = UiKit.SecondaryButton("Прошлый месяц", (_, _) => SetPreviousMonthTransactions()); prev.Width = 145; prev.Height = 42; prev.Margin = new Padding(0, 1, 0, 0);
        var current = UiKit.SecondaryButton("Текущий месяц", (_, _) => SetCurrentMonthTransactions()); current.Width = 140; current.Height = 42; current.Margin = new Padding(8, 1, 12, 0);
        filter.Controls.Add(prev); filter.Controls.Add(current);
        filter.Controls.Add(new Label { Text = "Период с", AutoSize = true, Margin = new Padding(0, 9, 6, 0), ForeColor = UiKit.Text });
        _transactionsFrom.Margin = new Padding(0, 2, 0, 0);
        filter.Controls.Add(_transactionsFrom);
        filter.Controls.Add(new Label { Text = "по", AutoSize = true, Margin = new Padding(12, 9, 6, 0), ForeColor = UiKit.Text });
        _transactionsTo.Margin = new Padding(0, 2, 0, 0);
        filter.Controls.Add(_transactionsTo);
        filter.Controls.Add(new Label { Text = "Категория", AutoSize = true, Margin = new Padding(14, 9, 6, 0), ForeColor = UiKit.Text });
        _transactionsCategory.Margin = new Padding(0, 1, 0, 0);
        _transactionsCategory.Height = 42;
        filter.Controls.Add(_transactionsCategory);
        var show = UiKit.PrimaryButton("Показать", (_, _) => RefreshTransactions()); show.Width = 115; show.Height = 42; show.Margin = new Padding(12, 1, 0, 0);
        filter.Controls.Add(show);
        root.Controls.Add(filter, 0, 1);

        var gridSection = UiKit.Section("Операции", _transactions);
        gridSection.Dock = DockStyle.Fill;
        root.Controls.Add(gridSection, 0, 2);
        _transactions.CellDoubleClick += (_, e) => { var id = IdAtRow(_transactions, e.RowIndex); if (id.HasValue) EditTransactionById(id.Value); };
        tab.Controls.Add(root);
        return tab;
    }

    private void SetPreviousMonthTransactions()
    {
        var d = DateTime.Today.AddMonths(-1);
        _transactionsFrom.Value = new DateTime(d.Year, d.Month, 1);
        _transactionsTo.Value = new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        RefreshTransactions();
    }

    private void SetCurrentMonthTransactions()
    {
        var d = DateTime.Today;
        _transactionsFrom.Value = new DateTime(d.Year, d.Month, 1);
        _transactionsTo.Value = new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        RefreshTransactions();
    }

    private TabPage BuildHistoryTab()
    {
        SetupHistoryGrid();
        var tab = NewTab("История займов");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12), BackColor = UiKit.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var filter = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(4, 8, 4, 8) };
        var prev = UiKit.SecondaryButton("Прошлый месяц", (_, _) => SetPreviousMonthHistory()); prev.Width = 150; prev.Height = 42; prev.Margin = new Padding(0, 1, 0, 0);
        var current = UiKit.SecondaryButton("Текущий месяц", (_, _) => SetCurrentMonthHistory()); current.Width = 145; current.Height = 42; current.Margin = new Padding(8, 1, 12, 0);
        filter.Controls.Add(prev); filter.Controls.Add(current);
        filter.Controls.Add(new Label { Text = "Период с", AutoSize = true, Margin = new Padding(0, 10, 6, 0), ForeColor = UiKit.Text });
        filter.Controls.Add(_historyFrom);
        filter.Controls.Add(new Label { Text = "по", AutoSize = true, Margin = new Padding(12, 10, 6, 0), ForeColor = UiKit.Text });
        filter.Controls.Add(_historyTo);
        _historyFrom.Margin = new Padding(0, 2, 0, 0);
        _historyTo.Margin = new Padding(0, 2, 0, 0);
        var show = UiKit.PrimaryButton("Показать", (_, _) => ShowHistoryPeriod()); show.Width = 120; show.Height = 42; show.Margin = new Padding(14, 1, 0, 0);
        var openOnly = UiKit.SecondaryButton("Не закрытые", (_, _) => ShowOpenLoansHistory()); openOnly.Width = 130; openOnly.Height = 42; openOnly.Margin = new Padding(8, 1, 0, 0);
        filter.Controls.Add(show); filter.Controls.Add(openOnly);
        root.Controls.Add(filter, 0, 0);
        root.Controls.Add(BuildHistorySection(), 0, 1);
        tab.Controls.Add(root);
        return tab;
    }


    private Control BuildHistorySection()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiKit.Surface, Padding = new Padding(12) };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(UiKit.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        };

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiKit.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var title = new Label
        {
            Text = "История займов",
            AutoSize = false,
            Width = 155,
            Height = 34,
            Margin = new Padding(0, 0, 4, 0),
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = UiKit.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };

        header.Controls.Add(title);
        header.Controls.Add(LoanStatusLegend("Закрыт", Color.FromArgb(226, 244, 231), compact: true));
        header.Controls.Add(LoanStatusLegend("Частично", Color.FromArgb(255, 247, 218), compact: true));
        header.Controls.Add(LoanStatusLegend("Не закрыт", Color.FromArgb(252, 231, 231), compact: true));

        panel.Controls.Add(_history);
        panel.Controls.Add(header);
        header.BringToFront();
        return panel;
    }

    private void SetPreviousMonthHistory()
    {
        _historyOpenOnly = false;
        var d = DateTime.Today.AddMonths(-1);
        _historyFrom.Value = new DateTime(d.Year, d.Month, 1);
        _historyTo.Value = new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        RefreshHistory();
    }

    private void SetCurrentMonthHistory()
    {
        _historyOpenOnly = false;
        var d = DateTime.Today;
        _historyFrom.Value = new DateTime(d.Year, d.Month, 1);
        _historyTo.Value = new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        RefreshHistory();
    }

    private void ShowHistoryPeriod()
    {
        _historyOpenOnly = false;
        RefreshHistory();
    }

    private void ShowOpenLoansHistory()
    {
        _historyOpenOnly = true;
        RefreshHistory();
    }

    private TabPage BuildClientsTab()
    {
        SetupClientsGrid();
        var toolbar = NewToolbar();
        toolbar.Controls.Add(UiKit.PrimaryButton("+ Клиент", AddClient));
        toolbar.Controls.Add(UiKit.SecondaryButton("Изменить", EditClient));
        toolbar.Controls.Add(UiKit.SecondaryButton("Архив / вернуть", ToggleClient));
        _clients.CellDoubleClick += (_, _) => EditClient(null, EventArgs.Empty);
        return BuildGridTab("Клиенты", toolbar, _clients);
    }

    private TabPage BuildExpensesTab()
    {
        SetupExpensesGrid();
        var toolbar = NewToolbar();
        toolbar.Controls.Add(UiKit.PrimaryButton("+ Расход", AddExpensePlan));
        toolbar.Controls.Add(UiKit.SecondaryButton("Изменить", EditExpensePlan));
        toolbar.Controls.Add(UiKit.SecondaryButton("Архив / вернуть", ToggleExpensePlan));
        _expenses.CellDoubleClick += (_, _) => EditExpensePlan(null, EventArgs.Empty);
        return BuildGridTab("План расходов", toolbar, _expenses);
    }

    private TabPage BuildIncomesTab()
    {
        SetupIncomesGrid();
        var toolbar = NewToolbar();
        toolbar.Controls.Add(UiKit.PrimaryButton("+ Поступление", AddIncomePlan));
        toolbar.Controls.Add(UiKit.SecondaryButton("Изменить", EditIncomePlan));
        toolbar.Controls.Add(UiKit.SecondaryButton("Архив / вернуть", ToggleIncomePlan));
        _incomes.CellDoubleClick += (_, e) => { var id = IdAtRow(_incomes, e.RowIndex); if (id.HasValue) EditIncomePlanById(id.Value); };
        return BuildGridTab("План поступлений", toolbar, _incomes);
    }

    private TabPage BuildAccountsTab()
    {
        SetupAccountsGrid();
        var toolbar = NewToolbar();
        toolbar.Controls.Add(UiKit.PrimaryButton("+ Счёт", AddAccount));
        toolbar.Controls.Add(UiKit.SecondaryButton("Изменить", EditAccount));
        toolbar.Controls.Add(UiKit.SecondaryButton("Архив / вернуть", ToggleAccount));
        _accounts.CellDoubleClick += (_, _) => EditAccount(null, EventArgs.Empty);
        return BuildGridTab("Счета", toolbar, _accounts);
    }

    private TabPage BuildSettingsTab()
    {
        SetupCategoriesGrid();
        SetupHolidaysGrid();
        var tab = NewTab("Настройки");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(18), BackColor = UiKit.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 285));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var info = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14) };
        info.Paint += (_, e) => { using var p = new Pen(UiKit.Border); e.Graphics.DrawRectangle(p, 0, 0, info.Width - 1, info.Height - 1); };

        var infoLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = Color.White };
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        infoLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        infoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        infoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var dbLabel = new Label
        {
            Text = $"База данных:\n{_db.DatabasePath}\n\nРезервная копия хранится рядом с FinanceTracker.exe. При новом архивировании предыдущий ZIP удаляется.",
            Dock = DockStyle.Fill,
            ForeColor = UiKit.Text,
            Font = new Font("Segoe UI", 9.5F),
            AutoEllipsis = true
        };
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(6, 0, 0, 0)
        };
        var openFolder = UiKit.SecondaryButton("Открыть папку данных", (_, _) => OpenDataFolder()); openFolder.Width = 200; openFolder.Height = 38;
        var backupNow = UiKit.PrimaryButton("Сделать бекап сейчас", (_, _) => BackupNow()); backupNow.Width = 200; backupNow.Height = 38;
        var exportExcel = UiKit.SecondaryButton("Экспорт в Excel", (_, _) => ExportExcel()); exportExcel.Width = 200; exportExcel.Height = 38;
        right.Controls.Add(openFolder); right.Controls.Add(backupNow); right.Controls.Add(exportExcel);
        var backupCheck = new CheckBox
        {
            Text = "Создавать ZIP-бекап базы при каждом запуске программы",
            AutoSize = true,
            Checked = _db.BackupOnStart,
            Anchor = AnchorStyles.Left,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = UiKit.Text,
            Padding = new Padding(0, 4, 0, 0)
        };
        backupCheck.CheckedChanged += (_, _) =>
        {
            try { _db.SetBackupOnStart(backupCheck.Checked); }
            catch (Exception ex) { UiKit.Error(this, ex); }
        };

        var passwordPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0),
            BackColor = Color.White
        };
        passwordPanel.Controls.Add(new Label
        {
            Text = "Пароль на архив",
            AutoSize = true,
            Margin = new Padding(0, 8, 10, 0),
            ForeColor = UiKit.Text,
            Font = new Font("Segoe UI", 9.5F)
        });
        var backupPassword = new TextBox
        {
            Width = 280,
            Font = new Font("Segoe UI", 10.5F),
            UseSystemPasswordChar = true,
            Text = _db.GetBackupPasswordForSettings(),
            Margin = new Padding(0, 3, 10, 0)
        };
        var passwordHint = new Label
        {
            Text = "Пусто — обычный ZIP; задан — ZIP с AES-256.",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            ForeColor = UiKit.Muted,
            Font = new Font("Segoe UI", 8.8F)
        };
        void SaveBackupPassword()
        {
            try { _db.SetBackupPassword(backupPassword.Text); }
            catch (Exception ex) { UiKit.Error(this, ex); }
        }
        backupPassword.Leave += (_, _) => SaveBackupPassword();
        backupPassword.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                SaveBackupPassword();
                e.SuppressKeyPress = true;
            }
        };
        passwordPanel.Controls.Add(backupPassword);
        passwordPanel.Controls.Add(passwordHint);

        infoLayout.Controls.Add(dbLabel, 0, 0);
        infoLayout.Controls.Add(backupCheck, 0, 1);
        infoLayout.Controls.Add(passwordPanel, 0, 2);
        infoLayout.Controls.Add(right, 1, 0);
        infoLayout.SetRowSpan(right, 3);
        info.Controls.Add(infoLayout);
        root.Controls.Add(info, 0, 0);

        var lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 10, 0, 0), BackColor = UiKit.Background };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var categoriesPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 6, 0), BackColor = UiKit.Background };
        var categoriesToolbar = NewToolbar();
        categoriesToolbar.Controls.Add(UiKit.PrimaryButton("+ Категория", AddCategory));
        categoriesToolbar.Controls.Add(UiKit.SecondaryButton("Изменить", EditCategory));
        categoriesToolbar.Controls.Add(UiKit.SecondaryButton("Архив / вернуть", ToggleCategory));
        _categories.CellDoubleClick += (_, _) => EditCategory(null, EventArgs.Empty);
        var categoriesSection = UiKit.Section("Категории", _categories); categoriesSection.Dock = DockStyle.Fill;
        categoriesPanel.Controls.Add(categoriesSection);
        categoriesPanel.Controls.Add(categoriesToolbar);

        var holidaysPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 0, 0), BackColor = UiKit.Background };
        var holidaysToolbar = NewToolbar();
        holidaysToolbar.Controls.Add(UiKit.PrimaryButton("+ Нерабочий день", AddHoliday));
        holidaysToolbar.Controls.Add(UiKit.SecondaryButton("Удалить", DeleteHoliday));
        var holidaysSection = UiKit.Section("Дополнительные нерабочие дни для расчёта сроков возврата", _holidays); holidaysSection.Dock = DockStyle.Fill;
        holidaysPanel.Controls.Add(holidaysSection);
        holidaysPanel.Controls.Add(holidaysToolbar);

        lists.Controls.Add(categoriesPanel, 0, 0);
        lists.Controls.Add(holidaysPanel, 1, 0);
        root.Controls.Add(lists, 0, 1);
        tab.Controls.Add(root);
        return tab;
    }

    private static TabPage NewTab(string title) => new(title) { BackColor = UiKit.Background, Padding = new Padding(0) };

    private static FlowLayoutPanel NewToolbar() => new()
    {
        Dock = DockStyle.Top,
        Height = 68,
        BackColor = UiKit.Background,
        FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(4, 4, 4, 8),
        WrapContents = false,
        AutoScroll = false
    };

    private static TabPage BuildGridTab(string title, Control toolbar, Control grid)
    {
        var tab = NewTab(title);
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UiKit.Background };
        var gridSection = UiKit.Section(title, grid); gridSection.Dock = DockStyle.Fill;
        panel.Controls.Add(gridSection); panel.Controls.Add(toolbar);
        tab.Controls.Add(panel);
        return tab;
    }

    private void SetupObligationGrid()
    {
        _obligations.Columns.Add("Kind", "Раздел");
        _obligations.Columns.Add("Name", "Кому / что");
        _obligations.Columns.Add("Due", "Срок");
        _obligations.Columns.Add("Amount", "Осталось");
        _obligations.Columns[0].FillWeight = 32; _obligations.Columns[1].FillWeight = 55; _obligations.Columns[2].FillWeight = 28; _obligations.Columns[3].FillWeight = 35;
    }

    private void SetupExpectedGrid()
    {
        _expected.Columns.Add("Name", "Поступление");
        _expected.Columns.Add("Client", "Клиент");
        _expected.Columns.Add("Due", "Дата");
        _expected.Columns.Add("Gross", "Поступило");
        _expected.Columns.Add("Amount", "Останется");
        _expected.Columns[0].FillWeight = 46; _expected.Columns[1].FillWeight = 30; _expected.Columns[2].FillWeight = 24; _expected.Columns[3].FillWeight = 28; _expected.Columns[4].FillWeight = 28;
    }

    private void SetupTransactionsGrid()
    {
        AddHiddenId(_transactions);
        _transactions.Columns.Add("Date", "Дата"); _transactions.Columns.Add("Type", "Тип"); _transactions.Columns.Add("Account", "Счёт"); _transactions.Columns.Add("ToAccount", "Куда");
        _transactions.Columns.Add("Client", "Клиент"); _transactions.Columns.Add("Amount", "Сумма"); _transactions.Columns.Add("Category", "Категория"); _transactions.Columns.Add("Plan", "План"); _transactions.Columns.Add("Comment", "Комментарий");
        _transactions.Columns[1].FillWeight = 25; _transactions.Columns[2].FillWeight = 36; _transactions.Columns[3].FillWeight = 35; _transactions.Columns[4].FillWeight = 30; _transactions.Columns[5].FillWeight = 30; _transactions.Columns[6].FillWeight = 30; _transactions.Columns[7].FillWeight = 30; _transactions.Columns[8].FillWeight = 35; _transactions.Columns[9].FillWeight = 50;
    }

    private void SetupHistoryGrid()
    {
        AddHiddenId(_history);
        _history.Columns.Add("Date", "Дата");
        _history.Columns.Add("Type", "Операция");
        _history.Columns.Add("Client", "Клиент");
        _history.Columns.Add("Amount", "Сумма");
        _history.Columns.Add("Income", "Поступило");
        _history.Columns.Add("Outflow", "Отдано / расход");
        _history.Columns.Add("Account", "Счёт");
        _history.Columns.Add("Category", "Категория");
        _history.Columns.Add("ReturnInfo", "Поступление / расчёт с клиентом");
        _history.Columns.Add("ReturnOperations", "Операции возврата");
        _history.Columns.Add("Comment", "Комментарий");
        _history.Columns[1].FillWeight = 24; _history.Columns[2].FillWeight = 34; _history.Columns[3].FillWeight = 28; _history.Columns[4].FillWeight = 26;
        _history.Columns[5].FillWeight = 26; _history.Columns[6].FillWeight = 30; _history.Columns[7].FillWeight = 26;
        _history.Columns[8].FillWeight = 28; _history.Columns[9].FillWeight = 88; _history.Columns[10].FillWeight = 72; _history.Columns[11].FillWeight = 50;
        _history.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _history.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
    }

    private void SetupClientsGrid()
    {
        AddHiddenId(_clients); _clients.Columns.Add("Name", "Клиент"); _clients.Columns.Add("Loan", "Займы"); _clients.Columns.Add("Percent", "% к возврату"); _clients.Columns.Add("Schedule", "График"); _clients.Columns.Add("Active", "Статус"); _clients.Columns.Add("Notes", "Примечание");
        _clients.Columns[1].FillWeight = 35; _clients.Columns[2].FillWeight = 18; _clients.Columns[3].FillWeight = 25; _clients.Columns[4].FillWeight = 75; _clients.Columns[5].FillWeight = 25; _clients.Columns[6].FillWeight = 50;
    }

    private void SetupExpensesGrid()
    {
        AddHiddenId(_expenses); _expenses.Columns.Add("Title", "Расход"); _expenses.Columns.Add("Amount", "Сумма"); _expenses.Columns.Add("Recurrence", "Периодичность"); _expenses.Columns.Add("Due", "Срок"); _expenses.Columns.Add("Category", "Категория"); _expenses.Columns.Add("Mandatory", "Обязательный"); _expenses.Columns.Add("Active", "Статус");
    }

    private void SetupIncomesGrid()
    {
        AddHiddenId(_incomes); _incomes.Columns.Add("Title", "Поступление"); _incomes.Columns.Add("Amount", "План"); _incomes.Columns.Add("Paid", "Получено"); _incomes.Columns.Add("Remain", "Осталось"); _incomes.Columns.Add("Due", "Срок"); _incomes.Columns.Add("Client", "Клиент"); _incomes.Columns.Add("Return", "Возврат"); _incomes.Columns.Add("Category", "Категория"); _incomes.Columns.Add("Active", "Статус"); _incomes.Columns.Add("Comment", "Комментарий");
    }

    private void SetupAccountsGrid()
    {
        AddHiddenId(_accounts); _accounts.Columns.Add("Name", "Счёт"); _accounts.Columns.Add("Initial", "Начальный остаток"); _accounts.Columns.Add("Current", "Текущий остаток"); _accounts.Columns.Add("DefaultIncome", "Основной для поступлений"); _accounts.Columns.Add("DefaultExpense", "Основной для расходов"); _accounts.Columns.Add("Active", "Статус");
    }

    private void SetupCategoriesGrid()
    {
        AddHiddenId(_categories);
        _categories.Columns.Add("Name", "Категория");
        _categories.Columns.Add("Scope", "Использование");
        _categories.Columns.Add("Active", "Статус");
    }

    private void SetupHolidaysGrid()
    {
        _holidays.Columns.Add("Date", "Дата"); _holidays.Columns.Add("Title", "Название");
    }

    private static void AddHiddenId(DataGridView grid)
    {
        var id = new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", Visible = false };
        grid.Columns.Add(id);
    }

    private void RefreshAll()
    {
        try
        {
            RefreshDashboard(); RefreshTransactions(); RefreshHistory(); RefreshClients(); RefreshExpenses(); RefreshIncomes(); RefreshAccounts(); RefreshCategories(); RefreshHolidays();
            _status.Text = $"Обновлено: {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _status.Text = "Ошибка обновления";
            UiKit.Error(this, ex);
        }
    }

    private void RefreshDashboard()
    {
        var d = _db.GetDashboard();
        SetMetric(_balanceValue, d.TotalBalanceCents, UiKit.Text);
        SetMetric(_freeValue, d.FreeCents, d.FreeCents < 0 ? UiKit.Bad : UiKit.Good);
        SetMetric(_returnValue, d.ClientReturnCents, UiKit.Text);
        SetMetric(_paymentsValue, d.PaymentsCents, UiKit.Text);
        SetMetric(_monthPaymentsValue, d.MonthMandatoryPaymentsCents, UiKit.Text);
        SetMetric(_optionalPaymentsValue, d.OptionalPaymentsCents, UiKit.Muted);
        SetMetric(_expectedValue, d.ExpectedIncomeCents, UiKit.Text);
        SetMetric(_forecastValue, d.ForecastEndOfMonthCents, d.ForecastEndOfMonthCents < 0 ? UiKit.Bad : UiKit.Text);
        _forecastDetails.Text = BuildForecastDetails(d);

        // Hover = short explanation of the calculation rule. Click = actual rows that form the amount.
        SetToolTipRecursive(_balanceCard, BuildBalanceExplanation());
        SetToolTipRecursive(_returnCard, BuildLoanReturnExplanation());
        SetToolTipRecursive(_paymentsCard, BuildYearMandatoryExplanation());
        SetToolTipRecursive(_monthPaymentsCard, BuildMonthMandatoryExplanation());
        SetToolTipRecursive(_optionalPaymentsCard, BuildOptionalPaymentsExplanation());
        SetToolTipRecursive(_freeCard, BuildFreeExplanation());
        SetToolTipRecursive(_expectedCard, BuildExpectedIncomeExplanation());

        _metricLegendTexts[0] = BuildAccountLegend(d.AccountDetails);
        _metricLegendTexts[1] = BuildLoanReturnToolTip(d.OpenLoanDetails);
        _metricLegendTexts[2] = BuildPaymentToolTip(d.MandatoryPaymentDetails);
        _metricLegendTexts[3] = BuildPaymentToolTip(d.MonthMandatoryPaymentDetails);
        _metricLegendTexts[4] = BuildPaymentToolTip(d.OptionalPaymentDetails);
        _metricLegendTexts[5] = BuildFreeBreakdown(d);
        _metricLegendTexts[6] = BuildExpectedIncomeToolTip(d.CurrentMonthExpectedIncomeDetails);
        if (_activeMetricLegendIndex >= 0) UpdateMetricLegendText();

        _obligations.Rows.Clear();
        foreach (var o in d.Obligations)
        {
            var idx = _obligations.Rows.Add(o.Kind, o.Name, o.DueDate.ToString("dd.MM.yyyy"), Money.Format(o.RemainingCents));
            if (o.IsCurrentMonth)
            {
                _obligations.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(231, 246, 235);
                _obligations.Rows[idx].DefaultCellStyle.SelectionBackColor = Color.FromArgb(205, 232, 212);
            }
            if (o.IsOverdue) { _obligations.Rows[idx].DefaultCellStyle.ForeColor = UiKit.Bad; _obligations.Rows[idx].Cells[2].Value = $"{o.DueDate:dd.MM.yyyy} · просрочено"; }
            else if (!o.Mandatory) _obligations.Rows[idx].DefaultCellStyle.ForeColor = UiKit.Muted;
        }

        _expected.Rows.Clear();
        foreach (var x in d.ExpectedIncomes)
        {
            var idx = _expected.Rows.Add(x.Name, x.Client, x.DueDate.ToString("dd.MM.yyyy"), Money.Format(x.GrossRemainingCents), Money.Format(x.NetRemainingCents));
            if (x.IsOverdue) { _expected.Rows[idx].DefaultCellStyle.ForeColor = UiKit.Warn; _expected.Rows[idx].Cells[2].Value = $"{x.DueDate:dd.MM.yyyy} · срок прошёл"; }
        }
    }

    private void SetToolTipRecursive(Control control, string text)
    {
        _toolTip.SetToolTip(control, text);
        foreach (Control child in control.Controls) SetToolTipRecursive(child, text);
    }

    private static string BuildBalanceExplanation() =>
        "Сумма текущих остатков по всем счетам с учётом всех сохранённых операций.";

    private static string BuildLoanReturnExplanation() =>
        "Сумма непогашенных обязательств по всем открытым займам клиентов.";

    private static string BuildYearMandatoryExplanation() =>
        "Ежемесячные расходы — только текущий месяц." + Environment.NewLine +
        "Раз в квартал и раз в несколько месяцев — очередные платежи текущего года." + Environment.NewLine +
        "Ежегодные расходы — обязательство текущего года." + Environment.NewLine +
        "Разовые расходы — если срок в текущем году." + Environment.NewLine +
        "Просроченные разовые расходы прошлых лет — пока не оплачены.";

    private static string BuildMonthMandatoryExplanation() =>
        "Непогашенные обязательные плановые расходы, срок которых приходится на текущий календарный месяц.";

    private static string BuildOptionalPaymentsExplanation() =>
        "Непогашенные необязательные плановые расходы со сроком в текущем году. Они показаны для контроля и не уменьшают «Свободно по итогам».";

    private static string BuildFreeExplanation() =>
        "Общий баланс минус остаток возвратов клиентам минус обязательные платежи, зарезервированные на текущий год по правилам программы.";

    private static string BuildExpectedIncomeExplanation() =>
        "Чистая ожидаемая сумма по планам поступлений текущего месяца. Для займа учитывается сумма, которая останется после рассчитанного возврата клиенту.";

    private static string BuildPaymentToolTip(IReadOnlyList<DashboardPaymentDetail> items)
    {
        if (items.Count == 0) return "Нет непогашенных платежей.";
        return string.Join(Environment.NewLine, items.Select(x =>
            $"{x.DueDate:dd.MM.yyyy} · {x.Name} · {Money.Format(x.RemainingCents)}"));
    }

    private static string BuildLoanReturnToolTip(IReadOnlyList<DashboardLoanDetail> items)
    {
        if (items.Count == 0) return "Нет сумм к возврату.";
        return string.Join(Environment.NewLine, items.Select(x =>
            $"{x.Client} · {x.InflowDate:dd.MM.yyyy} · поступило {Money.Format(x.PrincipalCents)} · осталось {Money.Format(x.RemainingCents)}"));
    }

    private static string BuildExpectedIncomeToolTip(IReadOnlyList<ExpectedIncomeRow> items)
    {
        if (items.Count == 0) return "Нет ожидаемых поступлений.";
        return string.Join(Environment.NewLine, items.Select(x =>
        {
            var who = string.IsNullOrWhiteSpace(x.Client) ? x.Name : $"{x.Name} · {x.Client}";
            return $"{x.DueDate:dd.MM.yyyy} · {who} · {Money.Format(x.NetRemainingCents)}";
        }));
    }

    private static string BuildAccountLegend(IReadOnlyList<DashboardAccountDetail> items)
    {
        if (items.Count == 0) return "Нет счетов.";
        return string.Join(Environment.NewLine, items.Select(x => $"{x.Name} · {Money.Format(x.BalanceCents)}"));
    }

    private static string BuildFreeBreakdown(DashboardData d)
    {
        var lines = new List<string>();
        lines.AddRange(d.AccountDetails.Select(x => $"+ Счёт {x.Name} · {Money.Format(x.BalanceCents)}"));
        lines.AddRange(d.OpenLoanDetails.Where(x => x.RemainingCents > 0).Select(x =>
            $"− Займ {x.Client} · {x.InflowDate:dd.MM.yyyy} · {Money.Format(x.RemainingCents)}"));
        lines.AddRange(d.MandatoryPaymentDetails.Where(x => x.RemainingCents > 0).Select(x =>
            $"− {x.DueDate:dd.MM.yyyy} · {x.Name} · {Money.Format(x.RemainingCents)}"));
        return lines.Count == 0 ? "Нет составляющих." : string.Join(Environment.NewLine, lines);
    }

    private void WireMetricCardClick(Control control, Action click)
    {
        control.Cursor = Cursors.Hand;
        control.Click += (_, _) => click();
        foreach (Control child in control.Controls) WireMetricCardClick(child, click);
    }

    private void ShowMetricLegend(int index)
    {
        if (index < 0 || index >= _metricCards.Length) return;
        if (_activeMetricLegendIndex == index && _metricLegendPanel.Visible)
        {
            _metricLegendPanel.Visible = false;
            _activeMetricLegendIndex = -1;
            return;
        }
        _activeMetricLegendIndex = index;
        UpdateMetricLegendText();
        _metricLegendPanel.Visible = true;
        PositionMetricLegend();
        _metricLegendPanel.BringToFront();
    }

    private void UpdateMetricLegendText()
    {
        if (_activeMetricLegendIndex < 0 || _activeMetricLegendIndex >= _metricLegendTexts.Length) return;
        var body = _metricLegendTexts[_activeMetricLegendIndex];
        _metricLegendText.Text = "Состоит из:" + Environment.NewLine + body;
    }

    private void PositionMetricLegend()
    {
        if (_activeMetricLegendIndex < 0 || _metricLegendHost == null || _metricLegendPanel == null) return;
        var card = _metricCards[_activeMetricLegendIndex];
        if (card == null || _metricLegendHost.ClientSize.Width <= 0) return;

        var width = Math.Min(500, Math.Max(320, _metricLegendHost.ClientSize.Width / 3));
        var centerScreen = card.PointToScreen(new Point(card.Width / 2, card.Height));
        var center = _metricLegendHost.PointToClient(centerScreen).X;
        var x = Math.Clamp(center - width / 2, 6, Math.Max(6, _metricLegendHost.ClientSize.Width - width - 6));
        _metricLegendPanel.Width = width;
        _metricLegendPanel.Height = Math.Max(90, _metricLegendHost.ClientSize.Height - 8);
        _metricLegendPanel.Location = new Point(x, 4);
    }

    private static string BuildForecastDetails(DashboardData d)
    {
        return $"+ Ожидаемый чистый доход до конца месяца: {Money.Format(d.ForecastExpectedIncomeCents)}" + Environment.NewLine +
               $"− Возвраты клиентам до конца месяца: {Money.Format(d.ForecastClientReturnsCents)}" + Environment.NewLine +
               $"− Обязательные плановые расходы до конца месяца: {Money.Format(d.ForecastMandatoryExpensesCents)}" + Environment.NewLine +
               $"− Необязательные плановые расходы до конца месяца: {Money.Format(d.ForecastOptionalExpensesCents)}" + Environment.NewLine +
               $"= Прогноз: {Money.Format(d.ForecastEndOfMonthCents)}" + Environment.NewLine + Environment.NewLine +
               "Просроченные открытые суммы также учитываются, если их срок уже наступил." + Environment.NewLine +
               "Для запланированного займа ожидается чистая сумма после полного возврата клиенту." +
               Environment.NewLine + Environment.NewLine + Environment.NewLine +
               $"Доход прошлого месяца: {Money.Format(d.PreviousMonthIncomeCents)}";
    }

    private static Label LoanStatusLegend(string text, Color backColor, bool compact = false) => new()
    {
        Text = text,
        AutoSize = true,
        BackColor = backColor,
        ForeColor = UiKit.Text,
        Padding = compact ? new Padding(8, 4, 8, 4) : new Padding(8, 6, 8, 6),
        Margin = compact ? new Padding(8, 3, 0, 0) : new Padding(10, 5, 0, 0),
        Font = new Font("Segoe UI", 9F)
    };

    private static void ApplyLoanStatusColor(DataGridViewRow row, LoanSettlementStatus status)
    {
        switch (status)
        {
            case LoanSettlementStatus.Closed:
                row.DefaultCellStyle.BackColor = Color.FromArgb(226, 244, 231);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(199, 229, 207);
                break;
            case LoanSettlementStatus.Partial:
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 247, 218);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(244, 231, 180);
                break;
            case LoanSettlementStatus.Open:
                row.DefaultCellStyle.BackColor = Color.FromArgb(252, 231, 231);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(240, 207, 207);
                break;
        }
    }

    private void RefreshAfterTransaction(QuickAction action)
    {
        // A client return changes several aggregates at once (remaining client debt,
        // nearest obligations, balances and loan history). Refresh them immediately
        // after the modal save returns, then refresh the remaining reference views.
        if (action == QuickAction.ClientReturn)
        {
            RefreshDashboard();
            RefreshTransactions();
            RefreshHistory();
            RefreshClients();
            RefreshIncomes();
            RefreshAccounts();
            _status.Text = $"Обновлено после возврата: {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
            return;
        }
        RefreshAll();
    }

    private static void SetMetric(Label label, long cents, Color normal)
    {
        label.Text = Money.FormatNoKopecks(cents);
        label.ForeColor = cents < 0 ? UiKit.Bad : normal;
    }

    private void RefreshTransactions()
    {
        var selectedCategory = _transactionsCategory.SelectedIndex > 0 ? _transactionsCategory.SelectedItem?.ToString() : null;
        var categories = _db.GetCategories(false).Select(c => c.Name)
            .Concat(_db.GetTransactionCategories())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        var keep = selectedCategory;
        _transactionsCategory.BeginUpdate();
        try
        {
            _transactionsCategory.Items.Clear();
            _transactionsCategory.Items.Add("Все категории");
            foreach (var category in categories) _transactionsCategory.Items.Add(category);
            if (!string.IsNullOrWhiteSpace(keep) && _transactionsCategory.Items.Contains(keep))
                _transactionsCategory.SelectedItem = keep;
            else
                _transactionsCategory.SelectedIndex = 0;
        }
        finally { _transactionsCategory.EndUpdate(); }

        selectedCategory = _transactionsCategory.SelectedIndex > 0 ? _transactionsCategory.SelectedItem?.ToString() : null;
        _transactions.Rows.Clear();
        foreach (var t in _db.GetTransactionsFiltered(_transactionsFrom.Value.Date, _transactionsTo.Value.Date, selectedCategory))
        {
            var idx = _transactions.Rows.Add(t.Id, t.Date.ToString("dd.MM.yyyy"), t.Type, t.Account, t.ToAccount, t.Client, Money.Format(t.AmountCents), t.Category, t.Plan, t.Comment);
            if (t.Type is "Расход" or "Возврат клиенту") _transactions.Rows[idx].Cells[6].Style.ForeColor = UiKit.Bad;
            else if (t.Type is "Поступление" or "Поступление клиента с возвратом") _transactions.Rows[idx].Cells[6].Style.ForeColor = UiKit.Good;
        }
    }

    private void RefreshHistory()
    {
        if (_history.Columns.Count == 0) return;
        _history.Rows.Clear();
        var historyRows = _db.GetOperationHistory(_historyFrom.Value.Date, _historyTo.Value.Date, _historyOpenOnly);
        foreach (var h in historyRows)
        {
            var idx = _history.Rows.Add(h.Id, h.Date.ToString("dd.MM.yyyy"), h.Type, h.Client, Money.Format(h.AmountCents),
                h.IncomeCents == 0 ? "" : Money.Format(h.IncomeCents),
                h.OutflowCents == 0 ? "" : Money.Format(h.OutflowCents),
                h.Account, h.Category, h.ClientReturnInfo, h.ReturnOperations, h.Comment);
            var row = _history.Rows[idx];
            if (h.IncomeCents > 0) row.Cells[5].Style.ForeColor = UiKit.Good;
            if (h.OutflowCents > 0) row.Cells[6].Style.ForeColor = UiKit.Bad;
            ApplyLoanStatusColor(row, h.LoanStatus);
        }
    }

    private void RefreshClients()
    {
        _clients.Rows.Clear();
        foreach (var c in _db.GetClients(false))
        {
            var schedule = c.IsLoan ? string.Join("; ", c.Rules.OrderBy(r => r.PartOrder).Select(r => $"{r.Percentage:N2}% / {r.Workdays} раб.дн.")) : "";
            var pct = c.IsLoan ? $"{c.ReturnPercent:+0.##;-0.##;0}%" : "";
            var idx = _clients.Rows.Add(c.Id, c.Name, c.IsLoan ? "Да" : "Нет", pct, schedule, c.IsActive ? "Активен" : "Архив", c.Notes);
            if (!c.IsActive) _clients.Rows[idx].DefaultCellStyle.ForeColor = UiKit.Muted;
        }
    }

    private void RefreshExpenses()
    {
        _expenses.Rows.Clear();
        foreach (var p in _db.GetPlannedExpenses(false))
        {
            var due = p.Recurrence switch
            {
                ExpenseRecurrence.Monthly => $"{p.DueDay}-е число",
                ExpenseRecurrence.Quarterly => $"с {p.DueDate:dd.MM.yyyy}, каждые 3 мес.",
                ExpenseRecurrence.EveryNMonths => $"с {p.DueDate:dd.MM.yyyy}, каждые {p.IntervalMonths} мес.",
                ExpenseRecurrence.Annual => $"{p.DueDay.GetValueOrDefault():00}.{p.DueMonth.GetValueOrDefault():00}",
                _ => p.DueDate?.ToString("dd.MM.yyyy") ?? ""
            };
            var idx = _expenses.Rows.Add(p.Id, p.Title, Money.Format(p.AmountCents), UiText.Recurrence(p.Recurrence), due, p.Category, p.Mandatory ? "Да" : "Нет", p.Active ? "Активен" : "Архив");
            if (!p.Active) _expenses.Rows[idx].DefaultCellStyle.ForeColor = UiKit.Muted;
        }
    }

    private void RefreshIncomes()
    {
        _incomes.Rows.Clear();
        foreach (var p in _db.GetPlannedIncomes(false))
        {
            var idx = _incomes.Rows.Add(p.Id, p.Title, Money.Format(p.AmountCents), Money.Format(p.PaidCents), Money.Format(p.RemainingCents), p.DueDate.ToString("dd.MM.yyyy"), p.ClientName, p.CreatesReturnObligation ? "Да" : "Нет", p.Category, p.Active ? "Активен" : "Архив", p.Comment);
            if (!p.Active || p.RemainingCents == 0) _incomes.Rows[idx].DefaultCellStyle.ForeColor = UiKit.Muted;
        }
    }

    private void RefreshAccounts()
    {
        _accounts.Rows.Clear();
        foreach (var a in _db.GetAccounts(false))
        {
            var idx = _accounts.Rows.Add(a.Id, a.Name, Money.Format(a.InitialBalanceCents), Money.Format(a.CurrentBalanceCents), a.IsDefaultIncome ? "Да" : "", a.IsDefaultExpense ? "Да" : "", a.IsActive ? "Активен" : "Архив");
            if (!a.IsActive) _accounts.Rows[idx].DefaultCellStyle.ForeColor = UiKit.Muted;
            if (a.CurrentBalanceCents < 0) _accounts.Rows[idx].Cells[3].Style.ForeColor = UiKit.Bad;
        }
    }

    private void RefreshCategories()
    {
        _categories.Rows.Clear();
        foreach (var c in _db.GetCategories(false))
        {
            var idx = _categories.Rows.Add(c.Id, c.Name, UiText.CategoryScopeText(c.Scope), c.IsActive ? "Активна" : "Архив");
            if (!c.IsActive) _categories.Rows[idx].DefaultCellStyle.ForeColor = UiKit.Muted;
        }
    }

    private void RefreshHolidays()
    {
        _holidays.Rows.Clear();
        foreach (var h in _db.GetHolidays()) _holidays.Rows.Add(h.Date.ToString("dd.MM.yyyy"), h.Title);
    }

    private void OpenTransaction(QuickAction action)
    {
        try
        {
            using var f = new TransactionForm(_db, action);
            if (f.ShowDialog(this) == DialogResult.OK) RefreshAfterTransaction(action);
        }
        catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void EditTransaction(object? sender, EventArgs e)
    {
        var id = SelectedId(_transactions); if (!id.HasValue) return;
        EditTransactionById(id.Value);
    }

    private void EditTransactionById(long id)
    {
        try
        {
            var tx = _db.GetTransactionForEdit(id); if (tx == null) return;
            var action = tx.Kind switch
            {
                TransactionKind.Income or TransactionKind.ClientIncome => QuickAction.Income,
                TransactionKind.Expense => QuickAction.Expense,
                TransactionKind.ClientReturn => QuickAction.ClientReturn,
                TransactionKind.Transfer => QuickAction.Transfer,
                _ => QuickAction.Adjustment
            };
            using var f = new TransactionForm(_db, action, tx);
            if (f.ShowDialog(this) == DialogResult.OK) RefreshAfterTransaction(action);
        }
        catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private long? SelectedId(DataGridView grid)
    {
        if (grid.SelectedRows.Count == 0) return null;
        return Convert.ToInt64(grid.SelectedRows[0].Cells["Id"].Value);
    }

    private static long? IdAtRow(DataGridView grid, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return null;
        var value = grid.Rows[rowIndex].Cells["Id"].Value;
        return value == null ? null : Convert.ToInt64(value);
    }

    private void AddClient(object? sender, EventArgs e)
    {
        using var f = new ClientEditForm(); if (f.ShowDialog(this) != DialogResult.OK) return;
        try { _db.SaveClient(f.Result); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void EditClient(object? sender, EventArgs e)
    {
        var id = SelectedId(_clients); if (!id.HasValue) return;
        var c = _db.GetClient(id.Value); if (c == null) return;
        using var f = new ClientEditForm(c); if (f.ShowDialog(this) != DialogResult.OK) return;
        try { _db.SaveClient(f.Result); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void ToggleClient(object? sender, EventArgs e)
    {
        var id = SelectedId(_clients); if (!id.HasValue) return;
        var c = _db.GetClient(id.Value); if (c == null) return;
        try { _db.SetClientActive(id.Value, !c.IsActive); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void AddExpensePlan(object? sender, EventArgs e)
    {
        using var f = new PlannedExpenseEditForm(_db); if (f.ShowDialog(this) != DialogResult.OK) return;
        try { _db.SavePlannedExpense(f.Result); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void EditExpensePlan(object? sender, EventArgs e)
    {
        var id = SelectedId(_expenses); if (!id.HasValue) return;
        var p = _db.GetPlannedExpenses(false).FirstOrDefault(x => x.Id == id.Value); if (p == null) return;
        using var f = new PlannedExpenseEditForm(_db, p); if (f.ShowDialog(this) != DialogResult.OK) return;
        try { _db.SavePlannedExpense(f.Result); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void ToggleExpensePlan(object? sender, EventArgs e)
    {
        var id = SelectedId(_expenses); if (!id.HasValue) return;
        var p = _db.GetPlannedExpenses(false).FirstOrDefault(x => x.Id == id.Value); if (p == null) return;
        try { _db.SetPlannedExpenseActive(id.Value, !p.Active); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void AddIncomePlan(object? sender, EventArgs e)
    {
        using var f = new PlannedIncomeEditForm(_db); if (f.ShowDialog(this) != DialogResult.OK) return;
        try { _db.SavePlannedIncome(f.Result); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void EditIncomePlan(object? sender, EventArgs e)
    {
        var id = SelectedId(_incomes); if (!id.HasValue) return;
        EditIncomePlanById(id.Value);
    }

    private void EditIncomePlanById(long id)
    {
        var p = _db.GetPlannedIncomes(false).FirstOrDefault(x => x.Id == id); if (p == null) return;
        using var f = new PlannedIncomeEditForm(_db, p); if (f.ShowDialog(this) != DialogResult.OK) return;
        try { _db.SavePlannedIncome(f.Result); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void ToggleIncomePlan(object? sender, EventArgs e)
    {
        var id = SelectedId(_incomes); if (!id.HasValue) return;
        var p = _db.GetPlannedIncomes(false).FirstOrDefault(x => x.Id == id.Value); if (p == null) return;
        try { _db.SetPlannedIncomeActive(id.Value, !p.Active); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void AddAccount(object? sender, EventArgs e)
    {
        using var f = new AccountEditForm(); if (f.ShowDialog(this) != DialogResult.OK) return;
        try { _db.SaveAccount(f.Result); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void EditAccount(object? sender, EventArgs e)
    {
        var id = SelectedId(_accounts); if (!id.HasValue) return;
        var a = _db.GetAccounts(false).FirstOrDefault(x => x.Id == id.Value); if (a == null) return;
        using var f = new AccountEditForm(a); if (f.ShowDialog(this) != DialogResult.OK) return;
        try { _db.SaveAccount(f.Result); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void ToggleAccount(object? sender, EventArgs e)
    {
        var id = SelectedId(_accounts); if (!id.HasValue) return;
        var a = _db.GetAccounts(false).FirstOrDefault(x => x.Id == id.Value); if (a == null) return;
        if (a.IsActive && _db.GetAccounts(true).Count <= 1) { MessageBox.Show(this, "Должен остаться хотя бы один активный счёт."); return; }
        try { _db.SetAccountActive(id.Value, !a.IsActive); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void DeleteTransaction(object? sender, EventArgs e)
    {
        var id = SelectedId(_transactions); if (!id.HasValue) return;
        if (MessageBox.Show(this, "Удалить выбранную операцию? Балансы и связанные планы будут пересчитаны автоматически.", "Удаление операции", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { _db.DeleteTransaction(id.Value); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void AddCategory(object? sender, EventArgs e)
    {
        using var f = new CategoryEditForm(); if (f.ShowDialog(this) != DialogResult.OK) return;
        try { _db.SaveCategory(f.Result); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void EditCategory(object? sender, EventArgs e)
    {
        var id = SelectedId(_categories); if (!id.HasValue) return;
        var c = _db.GetCategory(id.Value); if (c == null) return;
        using var f = new CategoryEditForm(c); if (f.ShowDialog(this) != DialogResult.OK) return;
        try { _db.SaveCategory(f.Result); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void ToggleCategory(object? sender, EventArgs e)
    {
        var id = SelectedId(_categories); if (!id.HasValue) return;
        var c = _db.GetCategory(id.Value); if (c == null) return;
        try { _db.SetCategoryActive(id.Value, !c.IsActive); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void AddHoliday(object? sender, EventArgs e)
    {
        using var f = new HolidayEditForm(); if (f.ShowDialog(this) != DialogResult.OK) return;
        try { _db.AddHoliday(f.Date, f.HolidayTitle); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void DeleteHoliday(object? sender, EventArgs e)
    {
        if (_holidays.SelectedRows.Count == 0) return;
        if (!DateTime.TryParse(Convert.ToString(_holidays.SelectedRows[0].Cells[0].Value), out var date)) return;
        try { _db.DeleteHoliday(date); RefreshAll(); } catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void ExportExcel()
    {
        try
        {
            using var dialog = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"Финансовый_учёт_{DateTime.Now:yyyy-MM-dd_HH-mm}.xlsx", AddExtension = true, DefaultExt = "xlsx" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            new ExcelExporter(_db).Export(dialog.FileName);
            MessageBox.Show(this, $"Экспорт завершён:\n{dialog.FileName}", "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void BackupNow()
    {
        try
        {
            var path = _db.CreateZipBackupBesideExecutable();
            var protection = _db.HasBackupPassword ? "Архив защищён паролем AES-256." : "Архив создан без пароля.";
            MessageBox.Show(this, $"Резервная копия создана:\n{path}\n\n{protection}\nПредыдущий архив FinanceTracker удалён.", "Резервная копия", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private void OpenDataFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(_db.DatabasePath)!;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex) { UiKit.Error(this, ex); }
    }
}
