using System.Drawing;

namespace FinanceTracker;

internal sealed class AccountEditForm : Form
{
    private readonly Account _account;
    private readonly TextBox _name = UiKit.TextInput();
    private readonly NumericUpDown _balance = UiKit.MoneyInput();
    private readonly CheckBox _defaultIncome = new() { Text = "Основной счёт для новых поступлений", AutoSize = true, Font = new Font("Segoe UI", 9.5F) };
    private readonly CheckBox _defaultExpense = new() { Text = "Основной счёт для новых расходов", AutoSize = true, Font = new Font("Segoe UI", 9.5F) };

    public AccountEditForm(Account? account = null)
    {
        _account = account ?? new Account { IsActive = true };
        Text = account == null ? "Новый счёт" : "Изменить счёт";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 310);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = UiKit.Background;

        _balance.Minimum = -1_000_000_000m;
        _name.Text = _account.Name;
        _balance.Value = Math.Min(_balance.Maximum, Math.Max(_balance.Minimum, Money.ToThousands(_account.InitialBalanceCents)));
        _defaultIncome.Checked = _account.IsDefaultIncome;
        _defaultExpense.Checked = _account.IsDefaultExpense;

        var fields = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18), AutoScroll = true };
        fields.Controls.Add(UiKit.FieldLabel("Название счёта"));
        fields.Controls.Add(_name);
        fields.Controls.Add(UiKit.FieldLabel("Начальный остаток, тыс. ₽"));
        fields.Controls.Add(_balance);
        fields.Controls.Add(_defaultIncome);
        fields.Controls.Add(_defaultExpense);
        fields.Controls.Add(new Label { Text = "Текущий остаток рассчитывается автоматически по операциям. Значения вводятся в тысячах рублей: 10,5 = 10 500 ₽.", AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = UiKit.Muted, Margin = new Padding(3, 8, 3, 3) });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 72, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var save = UiKit.PrimaryButton("Сохранить"); save.Width = 120; save.Click += SaveClick;
        var cancel = UiKit.SecondaryButton("Отмена", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }); cancel.Width = 110;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        Controls.Add(fields); Controls.Add(buttons);
        AcceptButton = save; CancelButton = cancel;
    }

    public Account Result => _account;

    private void SaveClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) { MessageBox.Show(this, "Укажите название счёта."); return; }
        _account.Name = _name.Text.Trim();
        _account.InitialBalanceCents = Money.FromThousands(_balance.Value);
        _account.IsDefaultIncome = _defaultIncome.Checked;
        _account.IsDefaultExpense = _defaultExpense.Checked;
        DialogResult = DialogResult.OK;
        Close();
    }
}

internal sealed class CategoryEditForm : Form
{
    private readonly Category _category;
    private readonly TextBox _name = UiKit.TextInput(320);
    private readonly ComboBox _scope = UiKit.Combo(300);

    public CategoryEditForm(Category? category = null)
    {
        _category = category ?? new Category { IsActive = true, Scope = CategoryScope.Both };
        Text = category == null ? "Новая категория" : "Изменить категорию";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(470, 270);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = UiKit.Background;

        _name.Text = _category.Name;
        _scope.Items.AddRange(new object[]
        {
            new ScopeItem(CategoryScope.Both),
            new ScopeItem(CategoryScope.Income),
            new ScopeItem(CategoryScope.Expense)
        });
        _scope.SelectedIndex = _category.Scope switch { CategoryScope.Income => 1, CategoryScope.Expense => 2, _ => 0 };

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18) };
        flow.Controls.Add(UiKit.FieldLabel("Название"));
        flow.Controls.Add(_name);
        flow.Controls.Add(UiKit.FieldLabel("Где использовать"));
        flow.Controls.Add(_scope);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 72, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var save = UiKit.PrimaryButton("Сохранить"); save.Width = 120; save.Click += (_, _) => Save();
        var cancel = UiKit.SecondaryButton("Отмена", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }); cancel.Width = 110;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        Controls.Add(flow); Controls.Add(buttons);
        AcceptButton = save; CancelButton = cancel;
    }

    public Category Result => _category;

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) { MessageBox.Show(this, "Укажите название категории."); return; }
        _category.Name = _name.Text.Trim();
        _category.Scope = ((ScopeItem)_scope.SelectedItem!).Scope;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record ScopeItem(CategoryScope Scope)
    {
        public override string ToString() => UiText.CategoryScopeText(Scope);
    }
}

internal sealed class ClientEditForm : Form
{
    private readonly Client _client;
    private readonly TextBox _name = UiKit.TextInput(360);
    private readonly CheckBox _isLoan = new() { Text = "Займы", AutoSize = true, Font = new Font("Segoe UI", 9.5F) };
    private readonly NumericUpDown _percent = new() { DecimalPlaces = 2, Minimum = -99.99m, Maximum = 1000m, Increment = 0.25m, Width = 160, Font = new Font("Segoe UI", 10F) };
    private readonly TextBox _notes = new() { Width = 500, Height = 60, Multiline = true, Font = new Font("Segoe UI", 10F) };
    private readonly DataGridView _rules = UiKit.Grid();
    private readonly Panel _loanPanel = new() { Dock = DockStyle.Fill };

    public ClientEditForm(Client? client = null)
    {
        _client = client ?? new Client { IsActive = true, IsLoan = false };
        Text = client == null ? "Новый клиент" : "Изменить клиента";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 630);
        MinimumSize = new Size(650, 580);
        BackColor = UiKit.Background;

        _name.Text = _client.Name;
        _isLoan.Checked = _client.IsLoan;
        _percent.Value = Math.Min(_percent.Maximum, Math.Max(_percent.Minimum, _client.ReturnPercent));
        _notes.Text = _client.Notes;

        _rules.ReadOnly = false;
        _rules.AllowUserToAddRows = false;
        _rules.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _rules.Columns.Add(new DataGridViewTextBoxColumn { Name = "Order", HeaderText = "№", ReadOnly = true, FillWeight = 20 });
        _rules.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pct", HeaderText = "Доля, %", FillWeight = 40 });
        _rules.Columns.Add(new DataGridViewTextBoxColumn { Name = "Days", HeaderText = "Через рабочих дней", FillWeight = 60 });
        IEnumerable<ClientReturnRule> existing = _client.Rules.Count > 0
            ? _client.Rules.OrderBy(x => x.PartOrder)
            : new[] { new ClientReturnRule { PartOrder = 1, Percentage = 100m, Workdays = 0 } };
        foreach (var r in existing) _rules.Rows.Add(r.PartOrder, r.Percentage, r.Workdays);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(18), BackColor = UiKit.Background };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        top.Controls.Add(UiKit.FieldLabel("Клиент")); top.Controls.Add(_name); top.Controls.Add(_isLoan);
        root.Controls.Add(top, 0, 0);

        var loanLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        loanLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); loanLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); loanLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var percentFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        percentFlow.Controls.Add(UiKit.FieldLabel("Процент к сумме возврата (например, -5 = вернуть 95% от поступления)")); percentFlow.Controls.Add(_percent);
        loanLayout.Controls.Add(percentFlow, 0, 0);
        var scheduleHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 52 };
        scheduleHeader.Controls.Add(new Label { Text = "График возврата — сумма долей должна быть 100%", AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = UiKit.Text, Margin = new Padding(3, 16, 12, 3) });
        var addRule = UiKit.SecondaryButton("+ Часть", (_, _) => { if (_rules.Rows.Count < 8) { _rules.Rows.Add(_rules.Rows.Count + 1, 0m, 0); Renumber(); } }); addRule.Width = 100; addRule.Height = 34;
        var delRule = UiKit.SecondaryButton("Удалить", (_, _) => { if (_rules.SelectedRows.Count > 0 && _rules.Rows.Count > 1) { _rules.Rows.RemoveAt(_rules.SelectedRows[0].Index); Renumber(); } }); delRule.Width = 100; delRule.Height = 34;
        scheduleHeader.Controls.Add(addRule); scheduleHeader.Controls.Add(delRule);
        loanLayout.Controls.Add(scheduleHeader, 0, 1); loanLayout.Controls.Add(_rules, 0, 2);
        _loanPanel.Controls.Add(loanLayout);
        root.Controls.Add(_loanPanel, 0, 1);

        var notesFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 100, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        notesFlow.Controls.Add(UiKit.FieldLabel("Примечание")); notesFlow.Controls.Add(_notes);
        root.Controls.Add(notesFlow, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 72, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 8, 10, 14) };
        var save = UiKit.PrimaryButton("Сохранить"); save.Width = 120; save.Click += SaveClick;
        var cancel = UiKit.SecondaryButton("Отмена", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }); cancel.Width = 110;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        AcceptButton = save; CancelButton = cancel;
        _isLoan.CheckedChanged += (_, _) => UpdateLoanVisibility();
        UpdateLoanVisibility();
    }

    public Client Result => _client;

    private void UpdateLoanVisibility()
    {
        _loanPanel.Visible = _isLoan.Checked;
    }

    private void Renumber()
    {
        for (var i = 0; i < _rules.Rows.Count; i++) _rules.Rows[i].Cells[0].Value = i + 1;
    }

    private void SaveClick(object? sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_name.Text)) throw new ArgumentException("Укажите имя клиента.");
            var rules = new List<ClientReturnRule>();
            if (_isLoan.Checked)
            {
                for (var i = 0; i < _rules.Rows.Count; i++)
                {
                    var row = _rules.Rows[i];
                    if (!decimal.TryParse(Convert.ToString(row.Cells[1].Value), out var pct)) throw new ArgumentException($"Некорректная доля в строке {i + 1}.");
                    if (!int.TryParse(Convert.ToString(row.Cells[2].Value), out var days)) throw new ArgumentException($"Некорректное число рабочих дней в строке {i + 1}.");
                    rules.Add(new ClientReturnRule { PartOrder = i + 1, Percentage = pct, Workdays = days });
                }
            }
            _client.Name = _name.Text.Trim();
            _client.IsLoan = _isLoan.Checked;
            _client.ReturnPercent = _isLoan.Checked ? _percent.Value : 0m;
            _client.Notes = _notes.Text.Trim();
            _client.Rules = rules;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { UiKit.Error(this, ex); }
    }
}

internal sealed class PlannedExpenseEditForm : Form
{
    private readonly PlannedExpense _item;
    private readonly bool _isEdit;
    private readonly TextBox _title = UiKit.TextInput(360);
    private readonly NumericUpDown _amount = UiKit.MoneyInput();
    private readonly ComboBox _recurrence = UiKit.Combo(220);
    private readonly NumericUpDown _day = new() { Minimum = 1, Maximum = 31, Value = 1, Width = 100, Font = new Font("Segoe UI", 10F) };
    private readonly NumericUpDown _intervalMonths = new() { Minimum = 1, Maximum = 120, Value = 2, Width = 100, Font = new Font("Segoe UI", 10F) };
    private readonly Label _intervalLabel = UiKit.FieldLabel("Повторять каждые, месяцев");
    private readonly DateTimePicker _date = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "dd.MM.yyyy",
        Width = 150,
        Font = new Font("Segoe UI", 10F),
        MinDate = new DateTime(2000, 1, 1),
        MaxDate = new DateTime(DateTime.Today.Year + 50, 12, 31)
    };
    private readonly ComboBox _category = UiKit.Combo(320);
    private readonly CheckBox _mandatory = new() { Text = "Учитывать как обязательный расход при расчёте свободных средств", AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9.5F) };
    private readonly Label _dateLabel = UiKit.FieldLabel("Срок");
    private readonly Label _futureHint = new()
    {
        Text = "Разовый расход можно запланировать на будущий год. До наступления этого года он хранится в плане, но не влияет на показатели Главной.",
        AutoSize = false,
        Width = 520,
        Height = 42,
        ForeColor = UiKit.Muted,
        Font = new Font("Segoe UI", 9F),
        Margin = new Padding(3, 4, 3, 4)
    };

    public PlannedExpenseEditForm(FinanceDatabase db, PlannedExpense? item = null)
    {
        _isEdit = item != null;
        _item = item ?? new PlannedExpense { Active = true, Mandatory = true, Recurrence = ExpenseRecurrence.Monthly, DueDay = 1 };
        Text = item == null ? "Новый плановый расход" : "Изменить плановый расход";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(590, 500);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; BackColor = UiKit.Background;

        _recurrence.Items.AddRange(new object[] { new RecurrenceItem(ExpenseRecurrence.Monthly), new RecurrenceItem(ExpenseRecurrence.Quarterly), new RecurrenceItem(ExpenseRecurrence.EveryNMonths), new RecurrenceItem(ExpenseRecurrence.Annual), new RecurrenceItem(ExpenseRecurrence.OneTime) });
        _recurrence.SelectedIndexChanged += (_, _) => UpdateDueControls();
        var categories = db.GetCategories(true, CategoryScope.Expense).Select(x => x.Name).ToList();
        if (!string.IsNullOrWhiteSpace(_item.Category) && !categories.Contains(_item.Category, StringComparer.OrdinalIgnoreCase)) categories.Insert(0, _item.Category);
        _category.DataSource = categories;
        ApplyItemToControls();

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18), AutoScroll = true };
        flow.Controls.Add(UiKit.FieldLabel("Название")); flow.Controls.Add(_title);
        flow.Controls.Add(UiKit.FieldLabel("Сумма, тыс. ₽")); flow.Controls.Add(_amount);
        flow.Controls.Add(UiKit.FieldLabel("Периодичность")); flow.Controls.Add(_recurrence);
        flow.Controls.Add(_dateLabel);
        flow.Controls.Add(_day); flow.Controls.Add(_date);
        flow.Controls.Add(_intervalLabel); flow.Controls.Add(_intervalMonths);
        flow.Controls.Add(_futureHint);
        flow.Controls.Add(UiKit.FieldLabel("Категория")); flow.Controls.Add(_category);
        flow.Controls.Add(_mandatory);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 72, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var save = UiKit.PrimaryButton("Сохранить"); save.Width = 120; save.Click += SaveClick;
        var cancel = UiKit.SecondaryButton("Отмена", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }); cancel.Width = 110;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        Controls.Add(flow); Controls.Add(buttons);
        AcceptButton = save; CancelButton = cancel;
        UpdateDueControls();
        if (_isEdit)
            Shown += (_, _) => BeginInvoke(new Action(() => SelectCategory(_item.Category)));
    }

    public PlannedExpense Result => _item;

    private void ApplyItemToControls()
    {
        _recurrence.SelectedIndex = _item.Recurrence switch { ExpenseRecurrence.Monthly => 0, ExpenseRecurrence.Quarterly => 1, ExpenseRecurrence.EveryNMonths => 2, ExpenseRecurrence.Annual => 3, _ => 4 };
        _title.Text = _item.Title;
        _amount.Value = Math.Min(_amount.Maximum, Math.Max(_amount.Minimum, Money.ToThousands(_item.AmountCents)));
        _day.Value = Math.Min(31, Math.Max(1, _item.DueDay ?? 1));
        _intervalMonths.Value = Math.Min(_intervalMonths.Maximum, Math.Max(_intervalMonths.Minimum, _item.IntervalMonths <= 0 ? 2 : _item.IntervalMonths));
        _date.Value = _item.Recurrence == ExpenseRecurrence.Annual
            ? SafeDate(DateTime.Today.Year, _item.DueMonth ?? DateTime.Today.Month, _item.DueDay ?? 1)
            : _item.DueDate ?? DateTime.Today;
        _mandatory.Checked = _item.Mandatory;
        SelectCategory(_item.Category);
        UpdateDueControls();
    }

    private void SelectCategory(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _category.SelectedIndex = -1;
            return;
        }
        for (var i = 0; i < _category.Items.Count; i++)
        {
            if (string.Equals(Convert.ToString(_category.Items[i]), name, StringComparison.OrdinalIgnoreCase))
            {
                _category.SelectedIndex = i;
                return;
            }
        }
        _category.SelectedIndex = -1;
    }

    private void UpdateDueControls()
    {
        var rec = ((RecurrenceItem)_recurrence.SelectedItem!).Value;
        _day.Visible = rec == ExpenseRecurrence.Monthly;
        _date.Visible = rec != ExpenseRecurrence.Monthly;
        _intervalLabel.Visible = rec == ExpenseRecurrence.EveryNMonths;
        _intervalMonths.Visible = rec == ExpenseRecurrence.EveryNMonths;
        _futureHint.Visible = rec == ExpenseRecurrence.OneTime;
        _dateLabel.Text = rec switch
        {
            ExpenseRecurrence.Monthly => "День оплаты каждого месяца",
            ExpenseRecurrence.Quarterly => "Дата первого квартального платежа",
            ExpenseRecurrence.EveryNMonths => "Дата первого платежа",
            ExpenseRecurrence.Annual => "День и месяц ежегодной оплаты (год не используется)",
            _ => "Дата оплаты"
        };
    }

    private void SaveClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_title.Text)) { MessageBox.Show(this, "Укажите название расхода."); return; }
        if (_amount.Value <= 0) { MessageBox.Show(this, "Укажите сумму больше нуля."); return; }
        var rec = ((RecurrenceItem)_recurrence.SelectedItem!).Value;
        // Read the calendar value once. In particular, keep the selected year for
        // one-time expenses instead of deriving it from the current year anywhere.
        var selectedDate = _date.Value.Date;
        _item.Title = _title.Text.Trim(); _item.AmountCents = Money.FromThousands(_amount.Value); _item.Recurrence = rec; _item.Category = Convert.ToString(_category.SelectedItem) ?? ""; _item.Mandatory = _mandatory.Checked;
        _item.IntervalMonths = rec == ExpenseRecurrence.Quarterly ? 3 : (rec == ExpenseRecurrence.EveryNMonths ? (int)_intervalMonths.Value : 1);
        if (rec == ExpenseRecurrence.Monthly) { _item.DueDay = (int)_day.Value; _item.DueMonth = null; _item.DueDate = null; }
        else if (rec == ExpenseRecurrence.Annual) { _item.DueMonth = selectedDate.Month; _item.DueDay = selectedDate.Day; _item.DueDate = null; }
        else { _item.DueDate = selectedDate; _item.DueMonth = null; _item.DueDay = null; }
        DialogResult = DialogResult.OK; Close();
    }

    private static DateTime SafeDate(int year, int month, int day) => new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    private sealed record RecurrenceItem(ExpenseRecurrence Value) { public override string ToString() => UiText.Recurrence(Value); }
}

internal sealed class PlannedIncomeEditForm : Form
{
    private readonly PlannedIncome _item;
    private readonly bool _isEdit;
    private readonly TextBox _title = UiKit.TextInput(360);
    private readonly NumericUpDown _amount = UiKit.MoneyInput();
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short, Width = 160, Font = new Font("Segoe UI", 10F) };
    private readonly ComboBox _client = UiKit.Combo(320);
    private readonly ComboBox _category = UiKit.Combo(320);
    private readonly CheckBox _returnObligation = new() { Text = "После поступления возникает обязательство возврата клиенту", AutoSize = true, Font = new Font("Segoe UI", 9.5F) };
    private readonly TextBox _comment = new() { Width = 460, Height = 70, Multiline = true, Font = new Font("Segoe UI", 10F) };

    public PlannedIncomeEditForm(FinanceDatabase db, PlannedIncome? item = null)
    {
        _isEdit = item != null;
        _item = item ?? new PlannedIncome { Active = true, DueDate = DateTime.Today };
        Text = item == null ? "Новое ожидаемое поступление" : "Изменить ожидаемое поступление";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(610, 610);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = UiKit.Background;

        var clients = new List<Choice> { new(null, "— без клиента —", false) };
        clients.AddRange(db.GetClients(true).Select(c => new Choice(c.Id, c.Name, c.IsLoan)));
        if (_item.ClientId.HasValue && clients.All(x => x.Id != _item.ClientId))
        {
            var old = db.GetClient(_item.ClientId.Value);
            if (old != null) clients.Add(new Choice(old.Id, old.Name + " (архив)", old.IsLoan));
        }
        _client.DataSource = clients;
        _client.SelectedIndexChanged += (_, _) => UpdateReturnAvailability();

        var categories = db.GetCategories(true, CategoryScope.Income).Select(x => x.Name).ToList();
        if (!string.IsNullOrWhiteSpace(_item.Category) && !categories.Contains(_item.Category, StringComparer.OrdinalIgnoreCase)) categories.Insert(0, _item.Category);
        _category.DataSource = categories;
        ApplyItemToControls();

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18), AutoScroll = true };
        flow.Controls.Add(UiKit.FieldLabel("Название")); flow.Controls.Add(_title);
        flow.Controls.Add(UiKit.FieldLabel("Сумма, тыс. ₽")); flow.Controls.Add(_amount);
        flow.Controls.Add(UiKit.FieldLabel("Ожидаемая дата")); flow.Controls.Add(_date);
        flow.Controls.Add(UiKit.FieldLabel("Клиент (необязательно)")); flow.Controls.Add(_client);
        flow.Controls.Add(_returnObligation);
        flow.Controls.Add(new Label { Text = "Если флажок включён, на Главной ожидаемая сумма будет показана как сумма, которая останется после рассчитанного возврата клиенту.", AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = UiKit.Muted, Margin = new Padding(3, 2, 3, 8) });
        flow.Controls.Add(UiKit.FieldLabel("Категория")); flow.Controls.Add(_category);
        flow.Controls.Add(UiKit.FieldLabel("Комментарий")); flow.Controls.Add(_comment);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 72, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var save = UiKit.PrimaryButton("Сохранить"); save.Width = 120; save.Click += SaveClick;
        var cancel = UiKit.SecondaryButton("Отмена", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }); cancel.Width = 110;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        Controls.Add(flow); Controls.Add(buttons); AcceptButton = save; CancelButton = cancel;
        UpdateReturnAvailability();

        // Re-apply selections after the WinForms binding context is fully active.
        // This is especially important when editing a row that points to a client or
        // category loaded into a data-bound ComboBox.
        if (_isEdit)
            Shown += (_, _) => BeginInvoke(new Action(() =>
            {
                SelectClient(_item.ClientId);
                SelectCategory(_item.Category);
            }));
    }

    public PlannedIncome Result => _item;

    private void ApplyItemToControls()
    {
        _title.Text = _item.Title;
        _amount.Value = Math.Min(_amount.Maximum, Math.Max(_amount.Minimum, Money.ToThousands(_item.AmountCents)));
        _date.Value = _item.DueDate == default ? DateTime.Today : _item.DueDate;
        _comment.Text = _item.Comment;
        _returnObligation.Checked = _item.CreatesReturnObligation;
        SelectClient(_item.ClientId);
        SelectCategory(_item.Category);
    }

    private void SelectClient(long? id)
    {
        for (var i = 0; i < _client.Items.Count; i++)
        {
            if (_client.Items[i] is Choice choice && choice.Id == id)
            {
                _client.SelectedIndex = i;
                return;
            }
        }
        if (_client.Items.Count > 0) _client.SelectedIndex = 0;
    }

    private void SelectCategory(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _category.SelectedIndex = -1;
            return;
        }
        for (var i = 0; i < _category.Items.Count; i++)
        {
            if (string.Equals(Convert.ToString(_category.Items[i]), name, StringComparison.OrdinalIgnoreCase))
            {
                _category.SelectedIndex = i;
                return;
            }
        }
        _category.SelectedIndex = -1;
    }

    private void UpdateReturnAvailability()
    {
        var choice = _client.SelectedItem as Choice;
        var canReturn = choice?.IsLoan == true;
        _returnObligation.Enabled = canReturn;
        if (!canReturn) _returnObligation.Checked = false;
    }

    private void SaveClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_title.Text)) { MessageBox.Show(this, "Укажите название."); return; }
        if (_amount.Value <= 0) { MessageBox.Show(this, "Укажите сумму больше нуля."); return; }
        var clientId = (_client.SelectedItem as Choice)?.Id;
        if (_returnObligation.Checked && !clientId.HasValue) { MessageBox.Show(this, "Для поступления с возвратом выберите клиента."); return; }
        _item.Title = _title.Text.Trim();
        _item.AmountCents = Money.FromThousands(_amount.Value);
        _item.DueDate = _date.Value.Date;
        _item.ClientId = clientId;
        _item.Category = Convert.ToString(_category.SelectedItem) ?? "";
        _item.CreatesReturnObligation = _returnObligation.Checked;
        _item.Comment = _comment.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record Choice(long? Id, string Text, bool IsLoan) { public override string ToString() => Text; }
}

internal enum QuickAction { Income, Expense, ClientReturn, Transfer, Adjustment }

internal sealed class TransactionForm : Form
{
    private readonly FinanceDatabase _db;
    private readonly QuickAction _action;
    private readonly TransactionEditData? _existing;
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short, Width = 150, Font = new Font("Segoe UI", 10F) };
    private readonly ComboBox _account = UiKit.Combo(340);
    private readonly ComboBox _toAccount = UiKit.Combo(340);
    private readonly NumericUpDown _amount = UiKit.MoneyInput();
    private readonly ComboBox _category = UiKit.Combo(360);
    private readonly TextBox _comment = new() { Width = 500, Height = 65, Multiline = true, Font = new Font("Segoe UI", 10F) };
    private readonly CheckBox _clientIncome = new() { Text = "Это поступление с обязательством возврата клиенту", AutoSize = true, Font = new Font("Segoe UI", 9.5F) };
    private readonly CheckBox _overrideDue = new() { Text = "Задать срок возврата именно для этой операции", AutoSize = true, Font = new Font("Segoe UI", 9.5F) };
    private readonly DateTimePicker _overrideDueDate = new() { Format = DateTimePickerFormat.Short, Width = 160, Font = new Font("Segoe UI", 10F) };
    private readonly ComboBox _client = UiKit.Combo(390);
    private readonly ComboBox _inflow = UiKit.Combo(530);
    private readonly ComboBox _expensePlan = UiKit.Combo(530);
    private readonly ComboBox _incomePlan = UiKit.Combo(530);
    private readonly ComboBox _adjustDirection = UiKit.Combo(240);
    private readonly Dictionary<Control, Label> _labels = new();
    private bool _restoringExisting;

    public TransactionForm(FinanceDatabase db, QuickAction action, TransactionEditData? existing = null)
    {
        _db = db;
        _action = action;
        _existing = existing;
        var baseTitle = action switch
        {
            QuickAction.Income => "Поступление",
            QuickAction.Expense => "Расход",
            QuickAction.ClientReturn => "Возврат клиенту",
            QuickAction.Transfer => "Перевод",
            _ => "Корректировка остатка"
        };
        Text = existing == null ? baseTitle : "Изменить операцию — " + baseTitle;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 760);
        MinimumSize = new Size(650, 650);
        BackColor = UiKit.Background;

        LoadAccounts();
        LoadClients();
        LoadCategories();
        LoadIncomePlans();
        RefreshExpensePlans();

        _client.SelectedIndexChanged += (_, _) => { RefreshInflows(); UpdateLoanClientAvailability(); };
        _clientIncome.CheckedChanged += (_, _) => UpdateVisibility();
        _overrideDue.CheckedChanged += (_, _) => UpdateVisibility();
        _date.ValueChanged += (_, _) => RefreshExpensePlans();
        _incomePlan.SelectedIndexChanged += (_, _) => { if (!_restoringExisting) ApplyIncomePlanDefaults(); };
        _expensePlan.SelectedIndexChanged += (_, _) => { if (!_restoringExisting) ApplyExpensePlanDefaults(); };

        _adjustDirection.Items.AddRange(new object[] { "Увеличить остаток", "Уменьшить остаток" });
        _adjustDirection.SelectedIndex = 0;

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(18), BackColor = UiKit.Background };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(fields, "Дата", _date);
        AddField(fields, _action == QuickAction.Transfer ? "Со счёта" : "Счёт", _account);
        AddField(fields, "На счёт", _toAccount);
        AddField(fields, "Сумма, тыс. ₽ (например 10,5 = 10 500 ₽)", _amount);
        AddField(fields, "Направление корректировки", _adjustDirection);
        fields.Controls.Add(_clientIncome);
        AddField(fields, "Клиент (можно выбрать и без обязательства возврата)", _client);
        fields.Controls.Add(_overrideDue);
        AddField(fields, "Индивидуальный срок возврата", _overrideDueDate);
        AddField(fields, "Обязательство / поступление клиента", _inflow);
        AddField(fields, "Связать с ожидаемым поступлением", _incomePlan);
        AddField(fields, "Связать с плановым расходом", _expensePlan);
        AddField(fields, "Категория", _category);
        AddField(fields, "Комментарий", _comment);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 76, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var save = UiKit.PrimaryButton(existing == null ? "Сохранить" : "Сохранить изменения"); save.Width = existing == null ? 130 : 180; save.Click += SaveClick;
        var cancel = UiKit.SecondaryButton("Отмена", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }); cancel.Width = 110;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        Controls.Add(fields); Controls.Add(buttons); AcceptButton = save; CancelButton = cancel;

        if (existing != null)
        {
            ApplyExisting(existing);
            // ComboBox DataSource binding can finish after the constructor. Restore the
            // exact persisted values one more time after the form has been shown so
            // client/category/account/plan fields always match the saved transaction.
            Shown += (_, _) => BeginInvoke(new Action(() => ApplyExisting(existing)));
        }
        else ApplyDefaultAccount();
        UpdateLoanClientAvailability();
        UpdateVisibility();
    }

    private void UpdateLoanClientAvailability()
    {
        if (_action != QuickAction.Income) return;
        var choice = _client.SelectedItem as ClientChoice;
        var canReturn = choice?.Client?.IsLoan == true;
        _clientIncome.Enabled = canReturn;
        if (!canReturn) _clientIncome.Checked = false;
    }

    private void LoadAccounts()
    {
        var accounts = _db.GetAccounts(false)
            .Where(a => a.IsActive || (_existing != null && (a.Id == _existing.AccountId || a.Id == _existing.ToAccountId)))
            .ToList();
        if (accounts.Count == 0) throw new InvalidOperationException("Сначала создайте хотя бы один активный счёт.");
        _account.DataSource = accounts.ToList();
        _toAccount.DataSource = accounts.ToList();
        if (_toAccount.Items.Count > 1) _toAccount.SelectedIndex = 1;
    }

    private void ApplyDefaultAccount()
    {
        var accounts = _account.DataSource as List<Account>;
        if (accounts == null || accounts.Count == 0) return;
        Account? preferred = _action switch
        {
            QuickAction.Income => accounts.FirstOrDefault(x => x.IsDefaultIncome),
            QuickAction.Expense or QuickAction.ClientReturn => accounts.FirstOrDefault(x => x.IsDefaultExpense),
            _ => null
        };
        if (preferred != null) SelectAccount(_account, preferred.Id);
    }

    private void LoadClients()
    {
        var clients = _db.GetClients(false)
            .Where(c => (c.IsActive || (_existing?.ClientId == c.Id) || (_action == QuickAction.ClientReturn && _db.GetClientInflowOptions(c.Id).Count > 0))
                && (_action != QuickAction.ClientReturn || c.IsLoan || _db.GetClientInflowOptions(c.Id).Count > 0))
            .ToList();
        var items = new List<ClientChoice>();
        if (_action == QuickAction.Income) items.Add(new ClientChoice(null, null, "— без клиента —"));
        items.AddRange(clients.Select(c => new ClientChoice(c.Id, c, c.Name + (c.IsActive ? "" : " (архив)"))));
        _client.DataSource = items;
    }

    private void LoadCategories()
    {
        if (_action is not (QuickAction.Income or QuickAction.Expense)) return;
        var scope = _action == QuickAction.Income ? CategoryScope.Income : CategoryScope.Expense;
        var names = _db.GetCategories(true, scope).Select(x => x.Name).ToList();
        if (_existing != null && !string.IsNullOrWhiteSpace(_existing.Category) && !names.Contains(_existing.Category, StringComparer.OrdinalIgnoreCase))
            names.Insert(0, _existing.Category);
        var items = new List<CategoryChoice> { new("", "— без категории —") };
        items.AddRange(names.Select(x => new CategoryChoice(x, x)));
        _category.DataSource = items;
    }

    private void LoadIncomePlans()
    {
        _incomePlan.Items.Clear();
        _incomePlan.Items.Add(new PlanChoice(null, "— не связывать с планом —"));
        if (_action != QuickAction.Income) { _incomePlan.SelectedIndex = 0; return; }
        foreach (var p in _db.GetPlannedIncomes(false).Where(x => (x.Active && x.RemainingCents > 0) || x.Id == _existing?.PlannedIncomeId))
            _incomePlan.Items.Add(new PlanChoice(p.Id, p.ToString(), p));
        _incomePlan.SelectedIndex = 0;
    }

    private void AddField(TableLayoutPanel root, string labelText, Control control)
    {
        var label = UiKit.FieldLabel(labelText); _labels[control] = label;
        root.Controls.Add(label); root.Controls.Add(control);
    }

    private void SetVisible(Control control, bool visible)
    {
        control.Visible = visible;
        if (_labels.TryGetValue(control, out var label)) label.Visible = visible;
    }

    private void UpdateVisibility()
    {
        SetVisible(_toAccount, _action == QuickAction.Transfer);
        SetVisible(_adjustDirection, _action == QuickAction.Adjustment);
        _clientIncome.Visible = _action == QuickAction.Income;
        SetVisible(_client, _action is QuickAction.Income or QuickAction.ClientReturn);
        _overrideDue.Visible = _action == QuickAction.Income && _clientIncome.Checked;
        SetVisible(_overrideDueDate, _action == QuickAction.Income && _clientIncome.Checked && _overrideDue.Checked);
        SetVisible(_inflow, _action == QuickAction.ClientReturn);
        SetVisible(_incomePlan, _action == QuickAction.Income);
        SetVisible(_expensePlan, _action == QuickAction.Expense);
        SetVisible(_category, _action is QuickAction.Income or QuickAction.Expense);
        if (_action == QuickAction.ClientReturn) RefreshInflows();
    }

    private void RefreshInflows()
    {
        if (_action != QuickAction.ClientReturn) return;
        var clientId = (_client.SelectedItem as ClientChoice)?.Id;
        if (!clientId.HasValue) { _inflow.DataSource = null; return; }

        if (_existing?.ClientInflowId is long existingInflowId)
        {
            var current = _db.GetClientInflowById(existingInflowId);
            _inflow.DataSource = current == null ? new List<ClientInflowOption>() : new List<ClientInflowOption> { current };
            _inflow.Enabled = false;
            _client.Enabled = false;
            return;
        }

        _inflow.Enabled = true;
        _client.Enabled = true;
        _inflow.DataSource = _db.GetClientInflowOptions(clientId.Value);
    }

    private void RefreshExpensePlans()
    {
        if (_action != QuickAction.Expense) return;
        var selectedId = (_expensePlan.SelectedItem as PlanChoice)?.Id ?? _existing?.PlannedExpenseId;
        _expensePlan.Items.Clear();
        _expensePlan.Items.Add(new PlanChoice(null, "— не связывать с планом —"));
        foreach (var o in _db.GetExpenseOptionsForDate(_date.Value.Date))
            _expensePlan.Items.Add(new PlanChoice(o.PlanId, o.ToString(), o));

        if (_existing?.PlannedExpenseId is long currentId && _expensePlan.Items.Cast<PlanChoice>().All(x => x.Id != currentId))
        {
            var p = _db.GetPlannedExpenses(false).FirstOrDefault(x => x.Id == currentId);
            if (p != null) _expensePlan.Items.Add(new PlanChoice(p.Id, p.Title + " (текущий план)", p));
        }
        SelectPlan(_expensePlan, selectedId);
    }

    private void ApplyIncomePlanDefaults()
    {
        if (_action != QuickAction.Income || _incomePlan.SelectedItem is not PlanChoice ch || ch.Value is not PlannedIncome plan) return;
        SelectClient(plan.ClientId);
        _clientIncome.Checked = plan.CreatesReturnObligation;
        SelectCategory(plan.Category);
    }

    private void ApplyExpensePlanDefaults()
    {
        if (_action != QuickAction.Expense || _expensePlan.SelectedItem is not PlanChoice ch) return;
        if (ch.Value is ExpenseOccurrenceOption occurrence)
        {
            var p = _db.GetPlannedExpenses(false).FirstOrDefault(x => x.Id == occurrence.PlanId);
            if (p != null) SelectCategory(p.Category);
        }
        else if (ch.Value is PlannedExpense plan) SelectCategory(plan.Category);
    }

    private void ApplyExisting(TransactionEditData x)
    {
        _restoringExisting = true;
        try
        {
            _date.Value = x.Date == default ? DateTime.Today : x.Date;
            SelectAccount(_account, x.AccountId);
            SelectAccount(_toAccount, x.ToAccountId);
            _amount.Value = Math.Min(_amount.Maximum, Math.Max(_amount.Minimum, Money.ToThousands(Math.Abs(x.AmountCents))));
            _comment.Text = x.Comment;

            // Select linked plans without applying their current defaults. When editing,
            // the transaction itself is the source of truth: its saved client/category
            // must not be replaced by values that the plan has today.
            if (x.PlannedIncomeId.HasValue) SelectPlan(_incomePlan, x.PlannedIncomeId);
            else SelectPlan(_incomePlan, null);
            if (x.PlannedExpenseId.HasValue) SelectPlan(_expensePlan, x.PlannedExpenseId);
            else SelectPlan(_expensePlan, null);

            SelectCategory(x.Category);
            SelectClient(x.ClientId);
            _clientIncome.Checked = x.Kind == TransactionKind.ClientIncome;
            _overrideDue.Checked = x.OverrideReturnDueDate.HasValue;
            if (x.OverrideReturnDueDate.HasValue) _overrideDueDate.Value = x.OverrideReturnDueDate.Value;
            if (_action == QuickAction.Adjustment) _adjustDirection.SelectedIndex = x.AmountCents < 0 ? 1 : 0;
            RefreshInflows();
            UpdateVisibility();
        }
        finally
        {
            _restoringExisting = false;
        }
    }

    private void SelectClient(long? id)
    {
        for (var i = 0; i < _client.Items.Count; i++)
            if (_client.Items[i] is ClientChoice c && c.Id == id) { _client.SelectedIndex = i; return; }
    }

    private static void SelectAccount(ComboBox combo, long? id)
    {
        if (!id.HasValue) return;
        for (var i = 0; i < combo.Items.Count; i++)
            if (combo.Items[i] is Account a && a.Id == id.Value) { combo.SelectedIndex = i; return; }
    }

    private static void SelectPlan(ComboBox combo, long? id)
    {
        if (!id.HasValue) { if (combo.Items.Count > 0) combo.SelectedIndex = 0; return; }
        for (var i = 0; i < combo.Items.Count; i++)
            if (combo.Items[i] is PlanChoice c && c.Id == id) { combo.SelectedIndex = i; return; }
    }

    private void SelectCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) { if (_category.Items.Count > 0) _category.SelectedIndex = 0; return; }
        for (var i = 0; i < _category.Items.Count; i++)
            if (_category.Items[i] is CategoryChoice c && string.Equals(c.Name, category, StringComparison.OrdinalIgnoreCase)) { _category.SelectedIndex = i; return; }
    }

    private void SaveClick(object? sender, EventArgs e)
    {
        try
        {
            var account = _account.SelectedItem as Account ?? throw new ArgumentException("Выберите счёт.");
            var amount = Money.FromThousands(_amount.Value);
            if (amount <= 0) throw new ArgumentException("Укажите сумму больше нуля.");
            var category = (_category.SelectedItem as CategoryChoice)?.Name ?? "";

            switch (_action)
            {
                case QuickAction.Income:
                {
                    var clientId = (_client.SelectedItem as ClientChoice)?.Id;
                    var createsReturn = _clientIncome.Checked;
                    var overrideDue = createsReturn && _overrideDue.Checked ? _overrideDueDate.Value.Date : (DateTime?)null;
                    var planId = (_incomePlan.SelectedItem as PlanChoice)?.Id;
                    if (_existing == null)
                        _db.AddIncome(_date.Value, account.Id, amount, category, _comment.Text, clientId, planId, createsReturn, overrideDue);
                    else
                        _db.UpdateIncomeTransaction(_existing.Id, _date.Value, account.Id, amount, category, _comment.Text, clientId, planId, createsReturn, overrideDue);
                    break;
                }
                case QuickAction.Expense:
                {
                    var planId = (_expensePlan.SelectedItem as PlanChoice)?.Id;
                    if (_existing == null)
                        _db.AddExpense(_date.Value, account.Id, amount, category, _comment.Text, planId);
                    else
                        _db.UpdateExpenseTransaction(_existing.Id, _date.Value, account.Id, amount, category, _comment.Text, planId);
                    break;
                }
                case QuickAction.ClientReturn:
                {
                    if (_existing == null)
                    {
                        var clientId = (_client.SelectedItem as ClientChoice)?.Id ?? throw new ArgumentException("Выберите клиента.");
                        var inflow = _inflow.SelectedItem as ClientInflowOption ?? throw new ArgumentException("У клиента нет открытого обязательства для возврата.");
                        _db.AddClientReturn(_date.Value, account.Id, clientId, inflow.Id, amount, _comment.Text);
                    }
                    else
                    {
                        _db.UpdateClientReturnTransaction(_existing.Id, _date.Value, account.Id, amount, _comment.Text);
                    }
                    break;
                }
                case QuickAction.Transfer:
                {
                    var to = _toAccount.SelectedItem as Account ?? throw new ArgumentException("Выберите счёт назначения.");
                    if (_existing == null) _db.AddTransfer(_date.Value, account.Id, to.Id, amount, _comment.Text);
                    else _db.UpdateTransferTransaction(_existing.Id, _date.Value, account.Id, to.Id, amount, _comment.Text);
                    break;
                }
                case QuickAction.Adjustment:
                {
                    var signed = _adjustDirection.SelectedIndex == 1 ? -amount : amount;
                    if (_existing == null) _db.AddAdjustment(_date.Value, account.Id, signed, _comment.Text);
                    else _db.UpdateAdjustmentTransaction(_existing.Id, _date.Value, account.Id, signed, _comment.Text);
                    break;
                }
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { UiKit.Error(this, ex); }
    }

    private sealed record ClientChoice(long? Id, Client? Client, string Text) { public override string ToString() => Text; }
    private sealed record CategoryChoice(string Name, string Text) { public override string ToString() => Text; }
    private sealed record PlanChoice(long? Id, string Text, object? Value = null) { public override string ToString() => Text; }
}

internal sealed class HolidayEditForm : Form
{
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short, Width = 160, Font = new Font("Segoe UI", 10F) };
    private readonly TextBox _title = UiKit.TextInput(300);
    public DateTime Date => _date.Value.Date;
    public string HolidayTitle => _title.Text.Trim();

    public HolidayEditForm()
    {
        Text = "Нерабочий день"; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(430, 250); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; BackColor = UiKit.Background;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18) };
        flow.Controls.Add(UiKit.FieldLabel("Дата")); flow.Controls.Add(_date); flow.Controls.Add(UiKit.FieldLabel("Название (необязательно)")); flow.Controls.Add(_title);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 72, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var ok = UiKit.PrimaryButton("Добавить"); ok.Width = 110; ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        var cancel = UiKit.SecondaryButton("Отмена", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }); cancel.Width = 110;
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel); Controls.Add(flow); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
    }
}
