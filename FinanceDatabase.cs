using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;

namespace FinanceTracker;

internal sealed class FinanceDatabase
{
    public string DatabasePath { get; }

    public FinanceDatabase()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FinanceTracker");
        DatabasePath = Path.Combine(dir, "finance.db");
    }

    private WinSqliteConnection Open() => new(DatabasePath);

    public void Initialize()
    {
        using var db = Open();
        db.ExecuteScript(@"
CREATE TABLE IF NOT EXISTS accounts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    initial_balance_cents INTEGER NOT NULL DEFAULT 0,
    is_active INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS clients (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    return_percent REAL NOT NULL DEFAULT 0,
    notes TEXT NOT NULL DEFAULT '',
    is_active INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS client_return_rules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
    part_order INTEGER NOT NULL,
    percentage REAL NOT NULL,
    workdays INTEGER NOT NULL,
    UNIQUE(client_id, part_order)
);
CREATE TABLE IF NOT EXISTS planned_expenses (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    amount_cents INTEGER NOT NULL,
    recurrence TEXT NOT NULL,
    due_date TEXT NULL,
    due_month INTEGER NULL,
    due_day INTEGER NULL,
    category TEXT NOT NULL DEFAULT '',
    mandatory INTEGER NOT NULL DEFAULT 1,
    active INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS planned_incomes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    amount_cents INTEGER NOT NULL,
    due_date TEXT NOT NULL,
    client_id INTEGER NULL REFERENCES clients(id),
    comment TEXT NOT NULL DEFAULT '',
    active INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS transactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    occurred_at TEXT NOT NULL,
    type TEXT NOT NULL,
    account_id INTEGER NULL REFERENCES accounts(id),
    to_account_id INTEGER NULL REFERENCES accounts(id),
    amount_cents INTEGER NOT NULL,
    client_id INTEGER NULL REFERENCES clients(id),
    client_inflow_id INTEGER NULL,
    planned_expense_id INTEGER NULL REFERENCES planned_expenses(id),
    planned_income_id INTEGER NULL REFERENCES planned_incomes(id),
    plan_period TEXT NULL,
    category TEXT NOT NULL DEFAULT '',
    comment TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS client_inflows (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    transaction_id INTEGER NOT NULL UNIQUE REFERENCES transactions(id) ON DELETE CASCADE,
    client_id INTEGER NOT NULL REFERENCES clients(id),
    principal_cents INTEGER NOT NULL,
    return_percent REAL NOT NULL,
    return_total_cents INTEGER NOT NULL,
    created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS client_inflow_schedule (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    client_inflow_id INTEGER NOT NULL REFERENCES client_inflows(id) ON DELETE CASCADE,
    part_order INTEGER NOT NULL,
    percentage REAL NOT NULL,
    due_date TEXT NOT NULL,
    amount_cents INTEGER NOT NULL,
    UNIQUE(client_inflow_id, part_order)
);
CREATE TABLE IF NOT EXISTS holidays (
    date TEXT PRIMARY KEY,
    title TEXT NOT NULL DEFAULT ''
);
CREATE TABLE IF NOT EXISTS categories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE COLLATE NOCASE,
    scope TEXT NOT NULL DEFAULT 'Both',
    is_active INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS app_settings (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS idx_tx_date ON transactions(occurred_at);
CREATE INDEX IF NOT EXISTS idx_tx_client_inflow ON transactions(client_inflow_id);
CREATE INDEX IF NOT EXISTS idx_tx_plan_expense ON transactions(planned_expense_id, plan_period);
CREATE INDEX IF NOT EXISTS idx_tx_plan_income ON transactions(planned_income_id);
CREATE INDEX IF NOT EXISTS idx_schedule_due ON client_inflow_schedule(due_date);
");

        // Additive migrations: the existing finance.db is kept in place and no user rows are deleted.
        EnsureColumn(db, "accounts", "is_default_income", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "accounts", "is_default_expense", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "planned_incomes", "category", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db, "planned_incomes", "creates_return_obligation", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "client_inflows", "override_due_date", "TEXT NULL");
        EnsureColumn(db, "clients", "is_loan", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "planned_expenses", "interval_months", "INTEGER NOT NULL DEFAULT 1");
        // One-time additive migration: infer the new loan flag for existing clients, but never
        // override a user's later manual choice on subsequent application starts.
        if (db.ExecuteScalar("SELECT value FROM app_settings WHERE key='client_is_loan_migrated_v18';") == null)
        {
            db.ExecuteNonQuery(@"UPDATE clients SET is_loan=1 WHERE is_loan=0 AND (ABS(return_percent)>0.000001 OR EXISTS(SELECT 1 FROM client_inflows i WHERE i.client_id=clients.id) OR EXISTS(SELECT 1 FROM planned_incomes p WHERE p.client_id=clients.id AND COALESCE(p.creates_return_obligation,0)=1) OR EXISTS(SELECT 1 FROM client_return_rules r WHERE r.client_id=clients.id AND r.workdays>0) OR (SELECT COUNT(*) FROM client_return_rules r2 WHERE r2.client_id=clients.id)>1);");
            db.ExecuteNonQuery("INSERT OR REPLACE INTO app_settings(key,value) VALUES('client_is_loan_migrated_v18','1');");
        }

        var count = Convert.ToInt64(db.ExecuteScalar("SELECT COUNT(*) FROM accounts;") ?? 0L);
        if (count == 0)
            db.ExecuteNonQuery("INSERT INTO accounts(name, initial_balance_cents, is_active, is_default_income, is_default_expense) VALUES(?, 0, 1, 1, 1);", "Основной счёт");

        // Existing installations get a sensible default account without changing balances.
        db.ExecuteNonQuery("UPDATE accounts SET is_default_income=0,is_default_expense=0 WHERE is_active=0;");
        if (Convert.ToInt64(db.ExecuteScalar("SELECT COUNT(*) FROM accounts WHERE is_active=1 AND is_default_income=1;") ?? 0L) == 0)
            db.ExecuteNonQuery("UPDATE accounts SET is_default_income=1 WHERE id=(SELECT id FROM accounts WHERE is_active=1 ORDER BY id LIMIT 1);");
        if (Convert.ToInt64(db.ExecuteScalar("SELECT COUNT(*) FROM accounts WHERE is_active=1 AND is_default_expense=1;") ?? 0L) == 0)
            db.ExecuteNonQuery("UPDATE accounts SET is_default_expense=1 WHERE id=(SELECT id FROM accounts WHERE is_active=1 ORDER BY id LIMIT 1);");

        // Import category names already present in the old database into the new directory.
        db.ExecuteNonQuery("INSERT OR IGNORE INTO categories(name,scope,is_active) SELECT DISTINCT TRIM(category),'Both',1 FROM transactions WHERE TRIM(category)<>'';");
        db.ExecuteNonQuery("INSERT OR IGNORE INTO categories(name,scope,is_active) SELECT DISTINCT TRIM(category),'Both',1 FROM planned_expenses WHERE TRIM(category)<>'';");
        db.ExecuteNonQuery("INSERT OR IGNORE INTO categories(name,scope,is_active) SELECT DISTINCT TRIM(category),'Both',1 FROM planned_incomes WHERE TRIM(category)<>'';");
        if (Convert.ToInt64(db.ExecuteScalar("SELECT COUNT(*) FROM categories;") ?? 0L) == 0)
            db.ExecuteNonQuery("INSERT INTO categories(name,scope,is_active) VALUES('Прочее','Both',1);");
        db.ExecuteNonQuery("INSERT OR IGNORE INTO app_settings(key,value) VALUES('backup_on_start','0');");
        db.ExecuteNonQuery("INSERT OR IGNORE INTO app_settings(key,value) VALUES('backup_password_protected','');");
    }

    #region Accounts
    public List<Account> GetAccounts(bool activeOnly = false)
    {
        using var db = Open();
        var rows = db.Query(activeOnly
            ? "SELECT id, name, initial_balance_cents, is_active, is_default_income, is_default_expense FROM accounts WHERE is_active=1 ORDER BY name;"
            : "SELECT id, name, initial_balance_cents, is_active, is_default_income, is_default_expense FROM accounts ORDER BY is_active DESC, name;");
        var list = new List<Account>();
        foreach (var row in rows)
        {
            var a = new Account
            {
                Id = L(row, "id"),
                Name = S(row, "name"),
                InitialBalanceCents = L(row, "initial_balance_cents"),
                IsActive = L(row, "is_active") != 0,
                IsDefaultIncome = L(row, "is_default_income") != 0,
                IsDefaultExpense = L(row, "is_default_expense") != 0
            };
            a.CurrentBalanceCents = GetAccountBalance(db, a.Id, a.InitialBalanceCents);
            list.Add(a);
        }
        return list;
    }

    public void SaveAccount(Account account)
    {
        if (string.IsNullOrWhiteSpace(account.Name)) throw new ArgumentException("Укажите название счёта.");
        if (!account.IsActive)
        {
            account.IsDefaultIncome = false;
            account.IsDefaultExpense = false;
        }
        using var db = Open();
        db.BeginTransaction();
        try
        {
            long id;
            if (account.Id == 0)
            {
                db.ExecuteNonQuery("INSERT INTO accounts(name, initial_balance_cents, is_active, is_default_income, is_default_expense) VALUES(?, ?, 1, ?, ?);",
                    account.Name.Trim(), account.InitialBalanceCents, account.IsDefaultIncome, account.IsDefaultExpense);
                id = db.LastInsertRowId;
            }
            else
            {
                id = account.Id;
                db.ExecuteNonQuery("UPDATE accounts SET name=?, initial_balance_cents=?, is_active=?, is_default_income=?, is_default_expense=? WHERE id=?;",
                    account.Name.Trim(), account.InitialBalanceCents, account.IsActive, account.IsDefaultIncome, account.IsDefaultExpense, id);
            }
            if (account.IsDefaultIncome) db.ExecuteNonQuery("UPDATE accounts SET is_default_income=0 WHERE id<>?;", id);
            if (account.IsDefaultExpense) db.ExecuteNonQuery("UPDATE accounts SET is_default_expense=0 WHERE id<>?;", id);
            if (Convert.ToInt64(db.ExecuteScalar("SELECT COUNT(*) FROM accounts WHERE is_active=1 AND is_default_income=1;") ?? 0L) == 0)
                db.ExecuteNonQuery("UPDATE accounts SET is_default_income=1 WHERE id=(SELECT id FROM accounts WHERE is_active=1 ORDER BY id LIMIT 1);");
            if (Convert.ToInt64(db.ExecuteScalar("SELECT COUNT(*) FROM accounts WHERE is_active=1 AND is_default_expense=1;") ?? 0L) == 0)
                db.ExecuteNonQuery("UPDATE accounts SET is_default_expense=1 WHERE id=(SELECT id FROM accounts WHERE is_active=1 ORDER BY id LIMIT 1);");
            db.Commit();
        }
        catch
        {
            db.Rollback();
            throw;
        }
    }

    public void SetAccountActive(long id, bool active)
    {
        using var db = Open();
        db.ExecuteNonQuery("UPDATE accounts SET is_active=?, is_default_income=CASE WHEN ?=0 THEN 0 ELSE is_default_income END, is_default_expense=CASE WHEN ?=0 THEN 0 ELSE is_default_expense END WHERE id=?;", active, active, active, id);
        if (active) return;
        if (Convert.ToInt64(db.ExecuteScalar("SELECT COUNT(*) FROM accounts WHERE is_active=1 AND is_default_income=1;") ?? 0L) == 0)
            db.ExecuteNonQuery("UPDATE accounts SET is_default_income=1 WHERE id=(SELECT id FROM accounts WHERE is_active=1 ORDER BY id LIMIT 1);");
        if (Convert.ToInt64(db.ExecuteScalar("SELECT COUNT(*) FROM accounts WHERE is_active=1 AND is_default_expense=1;") ?? 0L) == 0)
            db.ExecuteNonQuery("UPDATE accounts SET is_default_expense=1 WHERE id=(SELECT id FROM accounts WHERE is_active=1 ORDER BY id LIMIT 1);");
    }

    private static long GetAccountBalance(WinSqliteConnection db, long accountId, long initial)
    {
        var incoming = Convert.ToInt64(db.ExecuteScalar(@"
SELECT COALESCE(SUM(CASE
    WHEN account_id=? AND type IN ('Income','ClientIncome','Adjustment') THEN amount_cents
    WHEN to_account_id=? AND type='Transfer' THEN amount_cents
    ELSE 0 END),0)
FROM transactions;", accountId, accountId) ?? 0L);
        var outgoing = Convert.ToInt64(db.ExecuteScalar(@"
SELECT COALESCE(SUM(CASE
    WHEN account_id=? AND type IN ('Expense','ClientReturn','Transfer') THEN amount_cents
    ELSE 0 END),0)
FROM transactions;", accountId) ?? 0L);
        return initial + incoming - outgoing;
    }
    #endregion

    #region Categories
    public List<Category> GetCategories(bool activeOnly = false, CategoryScope? forScope = null)
    {
        using var db = Open();
        var rows = db.Query(activeOnly
            ? "SELECT id,name,scope,is_active FROM categories WHERE is_active=1 ORDER BY name;"
            : "SELECT id,name,scope,is_active FROM categories ORDER BY is_active DESC,name;");
        var result = rows.Select(ReadCategory).ToList();
        if (forScope.HasValue)
            result = result.Where(x => x.Scope == CategoryScope.Both || x.Scope == forScope.Value).ToList();
        return result;
    }

    public Category? GetCategory(long id)
    {
        using var db = Open();
        var row = db.Query("SELECT id,name,scope,is_active FROM categories WHERE id=?;", id).FirstOrDefault();
        return row == null ? null : ReadCategory(row);
    }

    public void SaveCategory(Category category)
    {
        if (string.IsNullOrWhiteSpace(category.Name)) throw new ArgumentException("Укажите название категории.");
        using var db = Open();
        db.BeginTransaction();
        try
        {
            if (category.Id == 0)
            {
                db.ExecuteNonQuery("INSERT INTO categories(name,scope,is_active) VALUES(?,?,1);", category.Name.Trim(), category.Scope.ToString());
            }
            else
            {
                var oldName = Convert.ToString(db.ExecuteScalar("SELECT name FROM categories WHERE id=?;", category.Id)) ?? "";
                db.ExecuteNonQuery("UPDATE categories SET name=?,scope=?,is_active=? WHERE id=?;", category.Name.Trim(), category.Scope.ToString(), category.IsActive, category.Id);
                if (!string.Equals(oldName, category.Name.Trim(), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(oldName))
                {
                    db.ExecuteNonQuery("UPDATE transactions SET category=? WHERE category=?;", category.Name.Trim(), oldName);
                    db.ExecuteNonQuery("UPDATE planned_expenses SET category=? WHERE category=?;", category.Name.Trim(), oldName);
                    db.ExecuteNonQuery("UPDATE planned_incomes SET category=? WHERE category=?;", category.Name.Trim(), oldName);
                }
            }
            db.Commit();
        }
        catch
        {
            db.Rollback();
            throw;
        }
    }

    public void SetCategoryActive(long id, bool active)
    {
        using var db = Open();
        db.ExecuteNonQuery("UPDATE categories SET is_active=? WHERE id=?;", active, id);
    }

    private static Category ReadCategory(Dictionary<string, object?> row)
    {
        Enum.TryParse<CategoryScope>(S(row, "scope"), out var scope);
        return new Category
        {
            Id = L(row, "id"),
            Name = S(row, "name"),
            Scope = scope,
            IsActive = L(row, "is_active") != 0
        };
    }
    #endregion

    #region Clients
    public List<Client> GetClients(bool activeOnly = false)
    {
        using var db = Open();
        var sql = activeOnly
            ? "SELECT id,name,return_percent,notes,is_active,is_loan FROM clients WHERE is_active=1 ORDER BY name;"
            : "SELECT id,name,return_percent,notes,is_active,is_loan FROM clients ORDER BY is_active DESC,name;";
        return db.Query(sql).Select(row => ReadClient(db, row)).ToList();
    }

    public Client? GetClient(long id)
    {
        using var db = Open();
        var row = db.Query("SELECT id,name,return_percent,notes,is_active,is_loan FROM clients WHERE id=?;", id).FirstOrDefault();
        return row == null ? null : ReadClient(db, row);
    }

    private static Client ReadClient(WinSqliteConnection db, Dictionary<string, object?> row)
    {
        var c = new Client
        {
            Id = L(row, "id"),
            Name = S(row, "name"),
            ReturnPercent = D(row, "return_percent"),
            Notes = S(row, "notes"),
            IsActive = L(row, "is_active") != 0,
            IsLoan = L(row, "is_loan") != 0
        };
        c.Rules = db.Query("SELECT part_order,percentage,workdays FROM client_return_rules WHERE client_id=? ORDER BY part_order;", c.Id)
            .Select(r => new ClientReturnRule
            {
                PartOrder = (int)L(r, "part_order"),
                Percentage = D(r, "percentage"),
                Workdays = (int)L(r, "workdays")
            }).ToList();
        return c;
    }

    public void SaveClient(Client client)
    {
        if (string.IsNullOrWhiteSpace(client.Name)) throw new ArgumentException("Укажите имя клиента.");
        if (client.IsLoan)
        {
            if (client.ReturnPercent <= -100m) throw new ArgumentException("Процент должен быть больше -100%. Например, -5% означает возврат 95% суммы.");
            if (client.Rules.Count == 0) throw new ArgumentException("Для клиента с займом задайте хотя бы одну часть графика возврата.");
            var totalPct = client.Rules.Sum(x => x.Percentage);
            if (Math.Abs(totalPct - 100m) > 0.0001m) throw new ArgumentException($"Сумма частей графика должна быть 100%. Сейчас: {totalPct:N2}%.");
            if (client.Rules.Any(x => x.Percentage <= 0 || x.Workdays < 0)) throw new ArgumentException("Доля должна быть больше 0%, а число рабочих дней — неотрицательным.");
        }

        using var db = Open();
        db.BeginTransaction();
        try
        {
            long id;
            if (client.Id == 0)
            {
                db.ExecuteNonQuery("INSERT INTO clients(name,return_percent,notes,is_active,is_loan) VALUES(?,?,?,1,?);",
                    client.Name.Trim(), client.ReturnPercent, client.Notes.Trim(), client.IsLoan);
                id = db.LastInsertRowId;
            }
            else
            {
                id = client.Id;
                db.ExecuteNonQuery("UPDATE clients SET name=?,return_percent=?,notes=?,is_active=?,is_loan=? WHERE id=?;",
                    client.Name.Trim(), client.ReturnPercent, client.Notes.Trim(), client.IsActive, client.IsLoan, id);
                db.ExecuteNonQuery("DELETE FROM client_return_rules WHERE client_id=?;", id);
            }

            var order = 1;
            foreach (var rule in (client.IsLoan ? client.Rules : new List<ClientReturnRule>()).OrderBy(x => x.PartOrder))
            {
                db.ExecuteNonQuery("INSERT INTO client_return_rules(client_id,part_order,percentage,workdays) VALUES(?,?,?,?);",
                    id, order++, rule.Percentage, rule.Workdays);
            }
            db.Commit();
        }
        catch
        {
            db.Rollback();
            throw;
        }
    }

    public void SetClientActive(long id, bool active)
    {
        using var db = Open();
        db.ExecuteNonQuery("UPDATE clients SET is_active=? WHERE id=?;", active, id);
    }
    #endregion

    #region Planned expenses
    public List<PlannedExpense> GetPlannedExpenses(bool activeOnly = false)
    {
        using var db = Open();
        var sql = activeOnly
            ? "SELECT * FROM planned_expenses WHERE active=1 ORDER BY title;"
            : "SELECT * FROM planned_expenses ORDER BY active DESC,title;";
        return db.Query(sql).Select(ReadPlannedExpense).ToList();
    }

    public void SavePlannedExpense(PlannedExpense p)
    {
        if (string.IsNullOrWhiteSpace(p.Title)) throw new ArgumentException("Укажите название расхода.");
        if (p.AmountCents <= 0) throw new ArgumentException("Сумма расхода должна быть больше нуля.");
        if (p.Recurrence == ExpenseRecurrence.Monthly && (p.DueDay is null or < 1 or > 31)) throw new ArgumentException("Для ежемесячного расхода укажите день месяца от 1 до 31.");
        if ((p.Recurrence is ExpenseRecurrence.Quarterly or ExpenseRecurrence.EveryNMonths) && p.DueDate == null) throw new ArgumentException("Для периодического расхода укажите дату первого платежа.");
        if (p.Recurrence == ExpenseRecurrence.EveryNMonths && (p.IntervalMonths < 1 || p.IntervalMonths > 120)) throw new ArgumentException("Интервал повторения должен быть от 1 до 120 месяцев.");
        if (p.Recurrence == ExpenseRecurrence.Annual && ((p.DueMonth is null or < 1 or > 12) || (p.DueDay is null or < 1 or > 31))) throw new ArgumentException("Для ежегодного расхода укажите месяц и день.");
        if (p.Recurrence == ExpenseRecurrence.OneTime && p.DueDate == null) throw new ArgumentException("Для разового расхода укажите дату.");

        using var db = Open();
        var dueDate = p.DueDate?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (p.Id == 0)
        {
            db.ExecuteNonQuery(@"INSERT INTO planned_expenses(title,amount_cents,recurrence,due_date,due_month,due_day,interval_months,category,mandatory,active)
VALUES(?,?,?,?,?,?,?,?,?,1);", p.Title.Trim(), p.AmountCents, p.Recurrence.ToString(), dueDate, p.DueMonth, p.DueDay, p.IntervalMonths, p.Category.Trim(), p.Mandatory);
        }
        else
        {
            db.ExecuteNonQuery(@"UPDATE planned_expenses SET title=?,amount_cents=?,recurrence=?,due_date=?,due_month=?,due_day=?,interval_months=?,category=?,mandatory=?,active=? WHERE id=?;",
                p.Title.Trim(), p.AmountCents, p.Recurrence.ToString(), dueDate, p.DueMonth, p.DueDay, p.IntervalMonths, p.Category.Trim(), p.Mandatory, p.Active, p.Id);
        }
    }

    public void SetPlannedExpenseActive(long id, bool active)
    {
        using var db = Open();
        db.ExecuteNonQuery("UPDATE planned_expenses SET active=? WHERE id=?;", active, id);
    }

    public List<ExpenseOccurrenceOption> GetExpenseOptionsForDate(DateTime date)
    {
        using var db = Open();
        var plans = db.Query("SELECT * FROM planned_expenses WHERE active=1 ORDER BY title;").Select(ReadPlannedExpense).ToList();
        return plans.Select(p => MakeOccurrence(db, p, date)).Where(x => x.RemainingCents > 0).ToList();
    }

    private static PlannedExpense ReadPlannedExpense(Dictionary<string, object?> row)
    {
        Enum.TryParse<ExpenseRecurrence>(S(row, "recurrence"), out var recurrence);
        return new PlannedExpense
        {
            Id = L(row, "id"),
            Title = S(row, "title"),
            AmountCents = L(row, "amount_cents"),
            Recurrence = recurrence,
            DueDate = ParseDateNullable(row.TryGetValue("due_date", out var dd) ? dd : null),
            DueMonth = INullable(row.TryGetValue("due_month", out var dm) ? dm : null),
            DueDay = INullable(row.TryGetValue("due_day", out var dy) ? dy : null),
            IntervalMonths = Math.Max(1, row.TryGetValue("interval_months", out var im) && im != null ? Convert.ToInt32(im) : 1),
            Category = S(row, "category"),
            Mandatory = L(row, "mandatory") != 0,
            Active = L(row, "active") != 0
        };
    }

    private static ExpenseOccurrenceOption MakeOccurrence(WinSqliteConnection db, PlannedExpense p, DateTime basis, long? excludeTransactionId = null)
    {
        string key;
        DateTime due;
        switch (p.Recurrence)
        {
            case ExpenseRecurrence.Monthly:
                key = basis.ToString("yyyy-MM");
                due = SafeDate(basis.Year, basis.Month, p.DueDay ?? 1);
                break;
            case ExpenseRecurrence.Quarterly:
                due = RecurringDueDate(p.DueDate ?? basis.Date, basis.Date, 3);
                key = due.ToString("yyyy-MM");
                break;
            case ExpenseRecurrence.EveryNMonths:
                due = RecurringDueDate(p.DueDate ?? basis.Date, basis.Date, Math.Max(1, p.IntervalMonths));
                key = due.ToString("yyyy-MM");
                break;
            case ExpenseRecurrence.Annual:
                key = basis.Year.ToString();
                due = SafeDate(basis.Year, p.DueMonth ?? 1, p.DueDay ?? 1);
                break;
            default:
                key = "once";
                due = p.DueDate?.Date ?? basis.Date;
                break;
        }
        var paid = Convert.ToInt64(db.ExecuteScalar(@"SELECT COALESCE(SUM(amount_cents),0) FROM transactions
WHERE type='Expense' AND planned_expense_id=? AND COALESCE(plan_period,'')=? AND (? IS NULL OR id<>?);", p.Id, key, excludeTransactionId, excludeTransactionId) ?? 0L);
        return new ExpenseOccurrenceOption
        {
            PlanId = p.Id,
            Title = p.Title,
            PeriodKey = key,
            DueDate = due,
            AmountCents = p.AmountCents,
            PaidCents = paid
        };
    }
    #endregion

    #region Planned incomes
    public List<PlannedIncome> GetPlannedIncomes(bool activeOnly = false)
    {
        using var db = Open();
        var sql = @"SELECT p.*, COALESCE(c.name,'') client_name,
COALESCE((SELECT SUM(t.amount_cents) FROM transactions t WHERE t.planned_income_id=p.id AND t.type IN ('Income','ClientIncome')),0) paid_cents
FROM planned_incomes p LEFT JOIN clients c ON c.id=p.client_id" + (activeOnly ? " WHERE p.active=1" : "") + " ORDER BY p.active DESC,p.due_date,p.title;";
        return db.Query(sql).Select(ReadPlannedIncome).ToList();
    }

    public void SavePlannedIncome(PlannedIncome p)
    {
        if (string.IsNullOrWhiteSpace(p.Title)) throw new ArgumentException("Укажите название ожидаемого поступления.");
        if (p.AmountCents <= 0) throw new ArgumentException("Сумма должна быть больше нуля.");
        using var db = Open();
        if (p.CreatesReturnObligation && !p.ClientId.HasValue) throw new ArgumentException("Для поступления с обязательством возврата выберите клиента.");
        if (p.CreatesReturnObligation && p.ClientId.HasValue && !IsLoanClient(db, p.ClientId.Value)) throw new ArgumentException("Обязательство возврата можно включить только для клиента с установленной галкой «Займы».");
        if (p.Id == 0)
            db.ExecuteNonQuery("INSERT INTO planned_incomes(title,amount_cents,due_date,client_id,category,creates_return_obligation,comment,active) VALUES(?,?,?,?,?,?,?,1);",
                p.Title.Trim(), p.AmountCents, p.DueDate.ToString("yyyy-MM-dd"), p.ClientId, p.Category.Trim(), p.CreatesReturnObligation, p.Comment.Trim());
        else
            db.ExecuteNonQuery("UPDATE planned_incomes SET title=?,amount_cents=?,due_date=?,client_id=?,category=?,creates_return_obligation=?,comment=?,active=? WHERE id=?;",
                p.Title.Trim(), p.AmountCents, p.DueDate.ToString("yyyy-MM-dd"), p.ClientId, p.Category.Trim(), p.CreatesReturnObligation, p.Comment.Trim(), p.Active, p.Id);
    }

    public void SetPlannedIncomeActive(long id, bool active)
    {
        using var db = Open();
        db.ExecuteNonQuery("UPDATE planned_incomes SET active=? WHERE id=?;", active, id);
    }

    private static PlannedIncome ReadPlannedIncome(Dictionary<string, object?> row) => new()
    {
        Id = L(row, "id"),
        Title = S(row, "title"),
        AmountCents = L(row, "amount_cents"),
        DueDate = ParseDate(S(row, "due_date")),
        ClientId = LNullable(row.TryGetValue("client_id", out var ci) ? ci : null),
        ClientName = S(row, "client_name"),
        Category = S(row, "category"),
        CreatesReturnObligation = L(row, "creates_return_obligation") != 0,
        Active = L(row, "active") != 0,
        Comment = S(row, "comment"),
        PaidCents = L(row, "paid_cents")
    };
    #endregion

    #region Transactions
    public void AddIncome(DateTime date, long accountId, long amountCents, string category, string comment, long? clientId, long? plannedIncomeId, bool createsReturnObligation, DateTime? overrideReturnDueDate)
    {
        if (amountCents <= 0) throw new ArgumentException("Сумма должна быть больше нуля.");
        if (createsReturnObligation && !clientId.HasValue) throw new ArgumentException("Для поступления с обязательством возврата выберите клиента.");
        if (overrideReturnDueDate.HasValue && overrideReturnDueDate.Value.Date < date.Date) throw new ArgumentException("Срок возврата не может быть раньше даты поступления.");

        using var db = Open();
        if (createsReturnObligation && (!clientId.HasValue || !IsLoanClient(db, clientId.Value))) throw new ArgumentException("Возврат можно создать только для клиента с установленной галкой «Займы».");
        db.BeginTransaction();
        try
        {
            ValidatePlannedIncomeAmount(db, plannedIncomeId, amountCents, null);
            var kind = createsReturnObligation ? TransactionKind.ClientIncome : TransactionKind.Income;
            db.ExecuteNonQuery(@"INSERT INTO transactions(occurred_at,type,account_id,to_account_id,amount_cents,client_id,planned_income_id,category,comment,created_at)
VALUES(?,?,?,?,?,?,?,?,?,?);", DateText(date), kind.ToString(), accountId, null, amountCents, clientId, plannedIncomeId, category.Trim(), comment.Trim(), DateText(DateTime.Now));
            var txId = db.LastInsertRowId;

            if (createsReturnObligation)
                CreateOrReplaceClientInflow(db, txId, clientId!.Value, date, amountCents, overrideReturnDueDate, null);

            db.Commit();
        }
        catch
        {
            db.Rollback();
            throw;
        }
    }

    public void AddExpense(DateTime date, long accountId, long amountCents, string category, string comment, long? plannedExpenseId)
    {
        if (amountCents <= 0) throw new ArgumentException("Сумма должна быть больше нуля.");
        using var db = Open();
        string? period = null;
        if (plannedExpenseId.HasValue)
        {
            var row = db.Query("SELECT * FROM planned_expenses WHERE id=? AND active=1;", plannedExpenseId.Value).FirstOrDefault()
                ?? throw new ArgumentException("Плановый расход не найден или архивирован.");
            var p = ReadPlannedExpense(row);
            var occurrence = MakeOccurrence(db, p, date);
            period = occurrence.PeriodKey;
            if (amountCents > occurrence.RemainingCents)
                throw new ArgumentException($"Сумма превышает остаток планового расхода ({Money.Format(occurrence.RemainingCents)}).");
        }
        db.ExecuteNonQuery(@"INSERT INTO transactions(occurred_at,type,account_id,amount_cents,planned_expense_id,plan_period,category,comment,created_at)
VALUES(?,?,?,?,?,?,?,?,?);", DateText(date), TransactionKind.Expense.ToString(), accountId, amountCents, plannedExpenseId, period, category.Trim(), comment.Trim(), DateText(DateTime.Now));
    }

    public void AddClientReturn(DateTime date, long accountId, long clientId, long clientInflowId, long amountCents, string comment)
    {
        if (amountCents <= 0) throw new ArgumentException("Сумма должна быть больше нуля.");
        using var db = Open();
        var inflow = GetClientInflowOptions(db, clientId).FirstOrDefault(x => x.Id == clientInflowId)
            ?? throw new ArgumentException("Выбранное обязательство клиента не найдено.");
        if (amountCents > inflow.RemainingCents) throw new ArgumentException($"Сумма возврата превышает остаток ({Money.Format(inflow.RemainingCents)}).");
        db.ExecuteNonQuery(@"INSERT INTO transactions(occurred_at,type,account_id,amount_cents,client_id,client_inflow_id,comment,created_at)
VALUES(?,?,?,?,?,?,?,?);", DateText(date), TransactionKind.ClientReturn.ToString(), accountId, amountCents, clientId, clientInflowId, comment.Trim(), DateText(DateTime.Now));
    }

    public void AddTransfer(DateTime date, long fromAccountId, long toAccountId, long amountCents, string comment)
    {
        if (fromAccountId == toAccountId) throw new ArgumentException("Счёт отправления и счёт назначения должны отличаться.");
        if (amountCents <= 0) throw new ArgumentException("Сумма должна быть больше нуля.");
        using var db = Open();
        db.ExecuteNonQuery(@"INSERT INTO transactions(occurred_at,type,account_id,to_account_id,amount_cents,comment,created_at)
VALUES(?,?,?,?,?,?,?);", DateText(date), TransactionKind.Transfer.ToString(), fromAccountId, toAccountId, amountCents, comment.Trim(), DateText(DateTime.Now));
    }

    public void AddAdjustment(DateTime date, long accountId, long signedAmountCents, string comment)
    {
        if (signedAmountCents == 0) throw new ArgumentException("Сумма корректировки не может быть нулевой.");
        using var db = Open();
        db.ExecuteNonQuery(@"INSERT INTO transactions(occurred_at,type,account_id,amount_cents,comment,created_at)
VALUES(?,?,?,?,?,?);", DateText(date), TransactionKind.Adjustment.ToString(), accountId, signedAmountCents, comment.Trim(), DateText(DateTime.Now));
    }

    public TransactionEditData? GetTransactionForEdit(long id)
    {
        using var db = Open();
        var row = db.Query(@"SELECT t.*, ci.override_due_date
FROM transactions t LEFT JOIN client_inflows ci ON ci.transaction_id=t.id WHERE t.id=?;", id).FirstOrDefault();
        if (row == null) return null;
        Enum.TryParse<TransactionKind>(S(row, "type"), out var kind);
        return new TransactionEditData
        {
            Id = L(row, "id"),
            Kind = kind,
            Date = ParseDate(S(row, "occurred_at")),
            AccountId = LNullable(row.TryGetValue("account_id", out var a) ? a : null),
            ToAccountId = LNullable(row.TryGetValue("to_account_id", out var ta) ? ta : null),
            AmountCents = L(row, "amount_cents"),
            ClientId = LNullable(row.TryGetValue("client_id", out var c) ? c : null),
            ClientInflowId = LNullable(row.TryGetValue("client_inflow_id", out var ci) ? ci : null),
            PlannedExpenseId = LNullable(row.TryGetValue("planned_expense_id", out var pe) ? pe : null),
            PlannedIncomeId = LNullable(row.TryGetValue("planned_income_id", out var pi) ? pi : null),
            PlanPeriod = S(row, "plan_period"),
            Category = S(row, "category"),
            Comment = S(row, "comment"),
            OverrideReturnDueDate = ParseDateNullable(row.TryGetValue("override_due_date", out var od) ? od : null)
        };
    }

    public void UpdateIncomeTransaction(long id, DateTime date, long accountId, long amountCents, string category, string comment,
        long? clientId, long? plannedIncomeId, bool createsReturnObligation, DateTime? overrideReturnDueDate)
    {
        if (amountCents <= 0) throw new ArgumentException("Сумма должна быть больше нуля.");
        if (createsReturnObligation && !clientId.HasValue) throw new ArgumentException("Для поступления с обязательством возврата выберите клиента.");
        if (overrideReturnDueDate.HasValue && overrideReturnDueDate.Value.Date < date.Date) throw new ArgumentException("Срок возврата не может быть раньше даты поступления.");

        using var db = Open();
        db.BeginTransaction();
        try
        {
            var old = db.Query("SELECT type FROM transactions WHERE id=?;", id).FirstOrDefault() ?? throw new ArgumentException("Операция не найдена.");
            var oldIsObligation = S(old, "type") == TransactionKind.ClientIncome.ToString();
            var inflowIdObj = db.ExecuteScalar("SELECT id FROM client_inflows WHERE transaction_id=?;", id);
            var inflowId = inflowIdObj == null ? (long?)null : Convert.ToInt64(inflowIdObj);
            var returned = inflowId.HasValue
                ? Convert.ToInt64(db.ExecuteScalar("SELECT COALESCE(SUM(amount_cents),0) FROM transactions WHERE type='ClientReturn' AND client_inflow_id=?;", inflowId.Value) ?? 0L)
                : 0L;

            if (oldIsObligation && !createsReturnObligation && returned > 0)
                throw new InvalidOperationException("Нельзя убрать обязательство возврата у поступления, по которому уже есть возвраты. Сначала исправьте или удалите связанные возвраты.");

            if (createsReturnObligation && (!clientId.HasValue || !IsLoanClient(db, clientId.Value))) throw new ArgumentException("Возврат можно создать только для клиента с установленной галкой «Займы».");
            ValidatePlannedIncomeAmount(db, plannedIncomeId, amountCents, id);
            var kind = createsReturnObligation ? TransactionKind.ClientIncome : TransactionKind.Income;
            db.ExecuteNonQuery(@"UPDATE transactions SET occurred_at=?,type=?,account_id=?,to_account_id=NULL,amount_cents=?,client_id=?,client_inflow_id=NULL,
planned_expense_id=NULL,planned_income_id=?,plan_period=NULL,category=?,comment=? WHERE id=?;",
                DateText(date), kind.ToString(), accountId, amountCents, clientId, plannedIncomeId, category.Trim(), comment.Trim(), id);

            if (!createsReturnObligation)
            {
                if (inflowId.HasValue) db.ExecuteNonQuery("DELETE FROM client_inflows WHERE id=?;", inflowId.Value);
            }
            else
            {
                var newInflowId = CreateOrReplaceClientInflow(db, id, clientId!.Value, date, amountCents, overrideReturnDueDate, inflowId);
                var newReturnTotal = Convert.ToInt64(db.ExecuteScalar("SELECT return_total_cents FROM client_inflows WHERE id=?;", newInflowId) ?? 0L);
                if (returned > newReturnTotal)
                    throw new InvalidOperationException($"Уже возвращено {Money.Format(returned)}, что больше нового обязательства {Money.Format(newReturnTotal)}.");
                if (returned > 0)
                    db.ExecuteNonQuery("UPDATE transactions SET client_id=? WHERE type='ClientReturn' AND client_inflow_id=?;", clientId.Value, newInflowId);
            }
            db.Commit();
        }
        catch
        {
            db.Rollback();
            throw;
        }
    }

    public void UpdateExpenseTransaction(long id, DateTime date, long accountId, long amountCents, string category, string comment, long? plannedExpenseId)
    {
        if (amountCents <= 0) throw new ArgumentException("Сумма должна быть больше нуля.");
        using var db = Open();
        string? period = null;
        if (plannedExpenseId.HasValue)
        {
            var row = db.Query("SELECT * FROM planned_expenses WHERE id=?;", plannedExpenseId.Value).FirstOrDefault()
                ?? throw new ArgumentException("Плановый расход не найден.");
            var p = ReadPlannedExpense(row);
            var occurrence = MakeOccurrence(db, p, date, id);
            period = occurrence.PeriodKey;
            if (amountCents > occurrence.RemainingCents)
                throw new ArgumentException($"Сумма превышает доступный остаток планового расхода ({Money.Format(occurrence.RemainingCents)}).");
        }
        db.ExecuteNonQuery(@"UPDATE transactions SET occurred_at=?,account_id=?,to_account_id=NULL,amount_cents=?,client_id=NULL,client_inflow_id=NULL,
planned_expense_id=?,planned_income_id=NULL,plan_period=?,category=?,comment=? WHERE id=? AND type='Expense';",
            DateText(date), accountId, amountCents, plannedExpenseId, period, category.Trim(), comment.Trim(), id);
    }

    public void UpdateClientReturnTransaction(long id, DateTime date, long accountId, long amountCents, string comment)
    {
        if (amountCents <= 0) throw new ArgumentException("Сумма должна быть больше нуля.");
        using var db = Open();
        var row = db.Query("SELECT client_inflow_id FROM transactions WHERE id=? AND type='ClientReturn';", id).FirstOrDefault()
            ?? throw new ArgumentException("Операция возврата не найдена.");
        var inflowId = LNullable(row.TryGetValue("client_inflow_id", out var ci) ? ci : null) ?? throw new InvalidOperationException("У возврата нет связанного обязательства.");
        var total = Convert.ToInt64(db.ExecuteScalar("SELECT return_total_cents FROM client_inflows WHERE id=?;", inflowId) ?? 0L);
        var otherReturned = Convert.ToInt64(db.ExecuteScalar("SELECT COALESCE(SUM(amount_cents),0) FROM transactions WHERE type='ClientReturn' AND client_inflow_id=? AND id<>?;", inflowId, id) ?? 0L);
        var available = Math.Max(0, total - otherReturned);
        if (amountCents > available) throw new ArgumentException($"Сумма возврата превышает доступный остаток ({Money.Format(available)}).");
        db.ExecuteNonQuery("UPDATE transactions SET occurred_at=?,account_id=?,amount_cents=?,comment=? WHERE id=?;", DateText(date), accountId, amountCents, comment.Trim(), id);
    }

    public void UpdateTransferTransaction(long id, DateTime date, long fromAccountId, long toAccountId, long amountCents, string comment)
    {
        if (fromAccountId == toAccountId) throw new ArgumentException("Счёт отправления и счёт назначения должны отличаться.");
        if (amountCents <= 0) throw new ArgumentException("Сумма должна быть больше нуля.");
        using var db = Open();
        db.ExecuteNonQuery("UPDATE transactions SET occurred_at=?,account_id=?,to_account_id=?,amount_cents=?,comment=? WHERE id=? AND type='Transfer';",
            DateText(date), fromAccountId, toAccountId, amountCents, comment.Trim(), id);
    }

    public void UpdateAdjustmentTransaction(long id, DateTime date, long accountId, long signedAmountCents, string comment)
    {
        if (signedAmountCents == 0) throw new ArgumentException("Сумма корректировки не может быть нулевой.");
        using var db = Open();
        db.ExecuteNonQuery("UPDATE transactions SET occurred_at=?,account_id=?,amount_cents=?,comment=? WHERE id=? AND type='Adjustment';",
            DateText(date), accountId, signedAmountCents, comment.Trim(), id);
    }

    private static long CreateOrReplaceClientInflow(WinSqliteConnection db, long transactionId, long clientId, DateTime date, long amountCents,
        DateTime? overrideReturnDueDate, long? existingInflowId)
    {
        var clientRow = db.Query("SELECT id,name,return_percent FROM clients WHERE id=?;", clientId).FirstOrDefault()
            ?? throw new ArgumentException("Клиент не найден.");
        var percent = D(clientRow, "return_percent");
        if (percent <= -100m) throw new ArgumentException("Процент клиента должен быть больше -100%.");
        var principal = Money.FromCents(amountCents);
        var returnTotalCents = Money.ToCents(principal * (1m + percent / 100m));
        if (returnTotalCents < 0) throw new ArgumentException("Рассчитанная сумма возврата не может быть отрицательной.");

        long inflowId;
        if (existingInflowId.HasValue)
        {
            inflowId = existingInflowId.Value;
            db.ExecuteNonQuery(@"UPDATE client_inflows SET client_id=?,principal_cents=?,return_percent=?,return_total_cents=?,override_due_date=? WHERE id=?;",
                clientId, amountCents, percent, returnTotalCents, overrideReturnDueDate?.ToString("yyyy-MM-dd"), inflowId);
            db.ExecuteNonQuery("DELETE FROM client_inflow_schedule WHERE client_inflow_id=?;", inflowId);
        }
        else
        {
            db.ExecuteNonQuery(@"INSERT INTO client_inflows(transaction_id,client_id,principal_cents,return_percent,return_total_cents,override_due_date,created_at)
VALUES(?,?,?,?,?,?,?);", transactionId, clientId, amountCents, percent, returnTotalCents, overrideReturnDueDate?.ToString("yyyy-MM-dd"), DateText(DateTime.Now));
            inflowId = db.LastInsertRowId;
        }

        if (overrideReturnDueDate.HasValue)
        {
            db.ExecuteNonQuery(@"INSERT INTO client_inflow_schedule(client_inflow_id,part_order,percentage,due_date,amount_cents)
VALUES(?,?,?,?,?);", inflowId, 1, 100m, overrideReturnDueDate.Value.Date.ToString("yyyy-MM-dd"), returnTotalCents);
            return inflowId;
        }

        var rules = db.Query("SELECT part_order,percentage,workdays FROM client_return_rules WHERE client_id=? ORDER BY part_order;", clientId);
        if (rules.Count == 0) throw new ArgumentException("У клиента не задан график возврата. Либо задайте его в карточке клиента, либо укажите индивидуальный срок возврата в операции.");
        var rulesTotal = rules.Sum(x => D(x, "percentage"));
        if (Math.Abs(rulesTotal - 100m) > 0.0001m) throw new ArgumentException("График возврата клиента должен составлять 100%.");

        var holidays = LoadHolidaySet(db);
        long allocated = 0;
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            var pct = D(r, "percentage");
            var workdays = (int)L(r, "workdays");
            var part = i == rules.Count - 1
                ? returnTotalCents - allocated
                : Money.ToCents(Money.FromCents(returnTotalCents) * pct / 100m);
            allocated += part;
            var due = AddWorkingDays(date.Date, workdays, holidays);
            db.ExecuteNonQuery(@"INSERT INTO client_inflow_schedule(client_inflow_id,part_order,percentage,due_date,amount_cents)
VALUES(?,?,?,?,?);", inflowId, i + 1, pct, due.ToString("yyyy-MM-dd"), part);
        }
        return inflowId;
    }

    private static void ValidatePlannedIncomeAmount(WinSqliteConnection db, long? plannedIncomeId, long amountCents, long? excludeTransactionId)
    {
        if (!plannedIncomeId.HasValue) return;
        var rem = PlannedIncomeRemaining(db, plannedIncomeId.Value, excludeTransactionId);
        if (amountCents > rem) throw new ArgumentException($"Сумма превышает доступный остаток ожидаемого поступления ({Money.Format(rem)}).");
    }

    public List<ClientInflowOption> GetClientInflowOptions(long clientId)
    {
        using var db = Open();
        return GetClientInflowOptions(db, clientId);
    }

    public ClientInflowOption? GetClientInflowById(long inflowId)
    {
        using var db = Open();
        var row = db.Query(@"SELECT ci.id,c.name client_name,t.occurred_at,ci.principal_cents,ci.return_total_cents,
COALESCE((SELECT SUM(r.amount_cents) FROM transactions r WHERE r.type='ClientReturn' AND r.client_inflow_id=ci.id),0) returned_cents
FROM client_inflows ci JOIN clients c ON c.id=ci.client_id JOIN transactions t ON t.id=ci.transaction_id WHERE ci.id=?;", inflowId).FirstOrDefault();
        if (row == null) return null;
        return new ClientInflowOption
        {
            Id = L(row, "id"),
            ClientName = S(row, "client_name"),
            Date = ParseDate(S(row, "occurred_at")),
            PrincipalCents = L(row, "principal_cents"),
            ReturnTotalCents = L(row, "return_total_cents"),
            ReturnedCents = L(row, "returned_cents")
        };
    }

    private static List<ClientInflowOption> GetClientInflowOptions(WinSqliteConnection db, long clientId)
    {
        var rows = db.Query(@"SELECT ci.id, c.name client_name, t.occurred_at, ci.principal_cents, ci.return_total_cents,
COALESCE((SELECT SUM(r.amount_cents) FROM transactions r WHERE r.type='ClientReturn' AND r.client_inflow_id=ci.id),0) returned_cents
FROM client_inflows ci
JOIN clients c ON c.id=ci.client_id
JOIN transactions t ON t.id=ci.transaction_id
WHERE ci.client_id=? ORDER BY t.occurred_at,ci.id;", clientId);
        return rows.Select(r => new ClientInflowOption
        {
            Id = L(r, "id"),
            ClientName = S(r, "client_name"),
            Date = ParseDate(S(r, "occurred_at")),
            PrincipalCents = L(r, "principal_cents"),
            ReturnTotalCents = L(r, "return_total_cents"),
            ReturnedCents = L(r, "returned_cents")
        }).Where(x => x.RemainingCents > 0).ToList();
    }

    public List<TransactionListRow> GetTransactions(int limit = 5000)
    {
        using var db = Open();
        var rows = db.Query(@"SELECT t.id,t.occurred_at,t.type,t.amount_cents,t.category,t.comment,
COALESCE(a.name,'') account_name,COALESCE(a2.name,'') to_account_name,COALESCE(c.name,'') client_name,
COALESCE(pe.title,'') expense_plan,COALESCE(pi.title,'') income_plan
FROM transactions t
LEFT JOIN accounts a ON a.id=t.account_id
LEFT JOIN accounts a2 ON a2.id=t.to_account_id
LEFT JOIN clients c ON c.id=t.client_id
LEFT JOIN planned_expenses pe ON pe.id=t.planned_expense_id
LEFT JOIN planned_incomes pi ON pi.id=t.planned_income_id
ORDER BY t.occurred_at DESC,t.id DESC LIMIT ?;", limit);
        return rows.Select(r =>
        {
            Enum.TryParse<TransactionKind>(S(r, "type"), out var kind);
            return new TransactionListRow
            {
                Id = L(r, "id"),
                Date = ParseDate(S(r, "occurred_at")),
                Type = UiText.TransactionType(kind),
                Account = S(r, "account_name"),
                ToAccount = S(r, "to_account_name"),
                Client = S(r, "client_name"),
                AmountCents = L(r, "amount_cents"),
                Category = S(r, "category"),
                Plan = !string.IsNullOrWhiteSpace(S(r, "expense_plan")) ? S(r, "expense_plan") : S(r, "income_plan"),
                Comment = S(r, "comment")
            };
        }).ToList();
    }

    public List<TransactionListRow> GetTransactionsFiltered(DateTime fromDate, DateTime toDate, string? category, int limit = 5000)
    {
        if (toDate.Date < fromDate.Date) (fromDate, toDate) = (toDate, fromDate);
        using var db = Open();
        var categoryFilter = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        var rows = db.Query(@"SELECT t.id,t.occurred_at,t.type,t.amount_cents,t.category,t.comment,
COALESCE(a.name,'') account_name,COALESCE(a2.name,'') to_account_name,COALESCE(c.name,'') client_name,
COALESCE(pe.title,'') expense_plan,COALESCE(pi.title,'') income_plan
FROM transactions t
LEFT JOIN accounts a ON a.id=t.account_id
LEFT JOIN accounts a2 ON a2.id=t.to_account_id
LEFT JOIN clients c ON c.id=t.client_id
LEFT JOIN planned_expenses pe ON pe.id=t.planned_expense_id
LEFT JOIN planned_incomes pi ON pi.id=t.planned_income_id
WHERE date(t.occurred_at)>=date(?) AND date(t.occurred_at)<=date(?)
  AND (? IS NULL OR t.category=?)
ORDER BY t.occurred_at DESC,t.id DESC LIMIT ?;",
            fromDate.ToString("yyyy-MM-dd"), toDate.ToString("yyyy-MM-dd"), categoryFilter, categoryFilter, limit);
        return rows.Select(r =>
        {
            Enum.TryParse<TransactionKind>(S(r, "type"), out var kind);
            return new TransactionListRow
            {
                Id = L(r, "id"),
                Date = ParseDate(S(r, "occurred_at")),
                Type = UiText.TransactionType(kind),
                Account = S(r, "account_name"),
                ToAccount = S(r, "to_account_name"),
                Client = S(r, "client_name"),
                AmountCents = L(r, "amount_cents"),
                Category = S(r, "category"),
                Plan = !string.IsNullOrWhiteSpace(S(r, "expense_plan")) ? S(r, "expense_plan") : S(r, "income_plan"),
                Comment = S(r, "comment")
            };
        }).ToList();
    }

    public List<string> GetTransactionCategories()
    {
        using var db = Open();
        return db.Query("SELECT DISTINCT category FROM transactions WHERE TRIM(COALESCE(category,''))<>'' ORDER BY category;")
            .Select(r => S(r, "category")).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    public List<HistoryOperationRow> GetOperationHistory(DateTime fromDate, DateTime toDate, bool openOnly = false)
    {
        if (toDate.Date < fromDate.Date) (fromDate, toDate) = (toDate, fromDate);
        using var db = Open();
        // History is focused on loans. In open-only mode one row per source loan is
        // returned regardless of the selected date interval; the return operations
        // column still lists every linked return.
        var selectSql = @"SELECT t.id,t.occurred_at,t.type,t.amount_cents,t.category,t.comment,t.client_inflow_id,
COALESCE(a.name,'') account_name,COALESCE(c.name,'') client_name,
COALESCE(ci.id,0) own_inflow_id,COALESCE(ci.return_total_cents,0) own_return_total,
COALESCE((SELECT SUM(r.amount_cents) FROM transactions r WHERE r.type='ClientReturn' AND r.client_inflow_id=ci.id),0) own_returned,
COALESCE(src.occurred_at,'') source_date,COALESCE(src.amount_cents,0) source_amount,
COALESCE(linkci.return_total_cents,0) source_return_total,
COALESCE((SELECT SUM(r.amount_cents) FROM transactions r WHERE r.type='ClientReturn' AND r.client_inflow_id=linkci.id),0) source_returned
FROM transactions t
LEFT JOIN accounts a ON a.id=t.account_id
LEFT JOIN clients c ON c.id=t.client_id
LEFT JOIN client_inflows ci ON ci.transaction_id=t.id
LEFT JOIN client_inflows linkci ON linkci.id=t.client_inflow_id
LEFT JOIN transactions src ON src.id=linkci.transaction_id
";

        List<Dictionary<string, object?>> rows;
        if (openOnly)
        {
            rows = db.Query(selectSql + @"WHERE t.type='ClientIncome' AND ci.id IS NOT NULL
  AND COALESCE((SELECT SUM(r.amount_cents) FROM transactions r WHERE r.type='ClientReturn' AND r.client_inflow_id=ci.id),0) < ci.return_total_cents
ORDER BY t.occurred_at,t.id;");
        }
        else
        {
            rows = db.Query(selectSql + @"WHERE date(t.occurred_at)>=date(?) AND date(t.occurred_at)<=date(?)
  AND t.type IN ('ClientIncome','ClientReturn')
ORDER BY t.occurred_at,t.id;", fromDate.ToString("yyyy-MM-dd"), toDate.ToString("yyyy-MM-dd"));
        }

        var result = new List<HistoryOperationRow>();
        foreach (var r in rows)
        {
            Enum.TryParse<TransactionKind>(S(r, "type"), out var kind);
            var amount = L(r, "amount_cents");
            var info = "";
            var returnOperations = "";
            var loanStatus = LoanSettlementStatus.None;

            if (kind == TransactionKind.ClientIncome && L(r, "own_inflow_id") > 0)
            {
                var inflowId = L(r, "own_inflow_id");
                var returnTotal = L(r, "own_return_total");
                var returned = L(r, "own_returned");
                var remaining = Math.Max(0, returnTotal - returned);
                var returns = db.Query("SELECT occurred_at,amount_cents FROM transactions WHERE type='ClientReturn' AND client_inflow_id=? ORDER BY occurred_at,id;", inflowId);
                returnOperations = returns.Count == 0
                    ? "—"
                    : string.Join("; ", returns.Select(x => $"{ParseDate(S(x, "occurred_at")):dd.MM.yyyy}: {Money.Format(L(x, "amount_cents"))}"));
                info = $"Поступило {Money.Format(amount)}; к возврату {Money.Format(returnTotal)}; возвращено {Money.Format(returned)}; осталось {Money.Format(remaining)}";
                loanStatus = SettlementStatus(returnTotal, returned);
            }
            else if (kind == TransactionKind.ClientReturn && !string.IsNullOrWhiteSpace(S(r, "source_date")))
            {
                var returnTotal = L(r, "source_return_total");
                var returned = L(r, "source_returned");
                var remaining = Math.Max(0, returnTotal - returned);
                var inflowId = L(r, "client_inflow_id");
                var returns = db.Query("SELECT occurred_at,amount_cents FROM transactions WHERE type='ClientReturn' AND client_inflow_id=? ORDER BY occurred_at,id;", inflowId);
                returnOperations = returns.Count == 0
                    ? "—"
                    : string.Join("; ", returns.Select(x => $"{ParseDate(S(x, "occurred_at")):dd.MM.yyyy}: {Money.Format(L(x, "amount_cents"))}"));
                info = $"Поступление {ParseDate(S(r, "source_date")):dd.MM.yyyy}: {Money.Format(L(r, "source_amount"))}; к возврату {Money.Format(returnTotal)}; возвращено {Money.Format(returned)}; осталось {Money.Format(remaining)}";
                loanStatus = SettlementStatus(returnTotal, returned);
            }

            result.Add(new HistoryOperationRow
            {
                Id = L(r, "id"),
                Date = ParseDate(S(r, "occurred_at")),
                Type = UiText.TransactionType(kind),
                Client = S(r, "client_name"),
                AmountCents = amount,
                IncomeCents = kind == TransactionKind.ClientIncome ? amount : 0,
                OutflowCents = kind == TransactionKind.ClientReturn ? amount : 0,
                Account = S(r, "account_name"),
                Category = S(r, "category"),
                ClientReturnInfo = info,
                ReturnOperations = returnOperations,
                LoanStatus = loanStatus,
                Comment = S(r, "comment")
            });
        }
        return result;
    }

    private static LoanSettlementStatus SettlementStatus(long returnTotal, long returned)
    {
        if (returnTotal <= 0) return LoanSettlementStatus.None;
        if (returned >= returnTotal) return LoanSettlementStatus.Closed;
        if (returned > 0) return LoanSettlementStatus.Partial;
        return LoanSettlementStatus.Open;
    }

    public void DeleteTransaction(long id)
    {
        using var db = Open();
        var row = db.Query("SELECT id,type FROM transactions WHERE id=?;", id).FirstOrDefault()
            ?? throw new ArgumentException("Операция не найдена.");
        var type = S(row, "type");
        if (type == TransactionKind.ClientIncome.ToString())
        {
            var inflowId = db.ExecuteScalar("SELECT id FROM client_inflows WHERE transaction_id=?;", id);
            if (inflowId != null)
            {
                var returns = Convert.ToInt64(db.ExecuteScalar("SELECT COUNT(*) FROM transactions WHERE type='ClientReturn' AND client_inflow_id=?;", Convert.ToInt64(inflowId)) ?? 0L);
                if (returns > 0) throw new InvalidOperationException("Нельзя удалить поступление клиента, пока существуют связанные возвраты. Сначала удалите возвраты.");
            }
        }
        db.ExecuteNonQuery("DELETE FROM transactions WHERE id=?;", id);
    }

    private static long PlannedIncomeRemaining(WinSqliteConnection db, long id, long? excludeTransactionId = null)
    {
        var row = db.Query(@"SELECT p.amount_cents,
COALESCE((SELECT SUM(t.amount_cents) FROM transactions t WHERE t.planned_income_id=p.id AND t.type IN ('Income','ClientIncome') AND (? IS NULL OR t.id<>?)),0) paid
FROM planned_incomes p WHERE p.id=?;", excludeTransactionId, excludeTransactionId, id).FirstOrDefault()
            ?? throw new ArgumentException("Ожидаемое поступление не найдено.");
        return Math.Max(0, L(row, "amount_cents") - L(row, "paid"));
    }
    #endregion

    #region Dashboard
    public DashboardData GetDashboard()
    {
        using var db = Open();
        var today = DateTime.Today;
        var startMonth = new DateTime(today.Year, today.Month, 1);
        var endMonth = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var accounts = db.Query("SELECT id,name,initial_balance_cents FROM accounts ORDER BY name;");
        var accountDetails = accounts.Select(a => new DashboardAccountDetail
        {
            Name = S(a, "name"),
            BalanceCents = GetAccountBalance(db, L(a, "id"), L(a, "initial_balance_cents"))
        }).ToList();
        var totalBalance = accountDetails.Sum(a => a.BalanceCents);

        var openLoanDetails = GetOpenLoanDetails(db);
        var toReturn = openLoanDetails.Sum(x => x.RemainingCents);

        var expenseOccurrences = GetReservedExpenseOccurrences(db, today);
        var payments = expenseOccurrences.Where(x => x.Plan.Mandatory).Sum(x => x.Option.RemainingCents);
        var monthMandatoryOccurrences = expenseOccurrences
            .Where(x => x.Plan.Mandatory && x.Option.DueDate.Year == today.Year && x.Option.DueDate.Month == today.Month)
            .OrderBy(x => x.Option.DueDate)
            .ThenBy(x => x.Plan.Title)
            .ToList();
        var monthMandatoryPayments = monthMandatoryOccurrences.Sum(x => x.Option.RemainingCents);
        var optionalPaymentOccurrences = expenseOccurrences
            .Where(x => !x.Plan.Mandatory && x.Option.DueDate.Year == today.Year)
            .OrderBy(x => x.Option.DueDate)
            .ThenBy(x => x.Plan.Title)
            .ToList();
        var optionalPayments = optionalPaymentOccurrences.Sum(x => x.Option.RemainingCents);
        var monthExpense = expenseOccurrences.Where(x => x.Option.DueDate.Year == today.Year && x.Option.DueDate.Month == today.Month)
            .Sum(x => x.Option.RemainingCents);

        var expected = GetExpectedIncomeRows(db);
        var expectedTotal = expected
            .Where(x => x.DueDate.Date >= startMonth && x.DueDate.Date <= endMonth)
            .Sum(x => x.NetRemainingCents);
        var clientObligations = GetClientObligations(db);

        var obligations = new List<ObligationRow>();
        obligations.AddRange(clientObligations);
        foreach (var e in expenseOccurrences.Where(x => x.Option.RemainingCents > 0))
        {
            var inMonth = e.Option.DueDate.Year == today.Year && e.Option.DueDate.Month == today.Month;
            var section = e.Option.DueDate.Year < today.Year ? "Просрочено" : (inMonth ? "Текущий месяц" : "Текущий год");
            obligations.Add(new ObligationRow
            {
                Kind = section,
                Name = e.Plan.Title,
                DueDate = e.Option.DueDate,
                RemainingCents = e.Option.RemainingCents,
                Mandatory = e.Plan.Mandatory
            });
        }
        // The dashboard is an action list: completed obligations stay in history and are hidden here.
        obligations = obligations.Where(x => x.RemainingCents > 0).OrderBy(x => x.DueDate).ThenBy(x => x.Name).Take(30).ToList();

        var dueClientByMonthEnd = clientObligations.Where(x => x.DueDate <= endMonth).Sum(x => x.RemainingCents);
        var dueMandatoryExpensesByMonthEnd = expenseOccurrences
            .Where(x => x.Plan.Mandatory && x.Option.DueDate <= endMonth)
            .Sum(x => x.Option.RemainingCents);
        var dueOptionalExpensesByMonthEnd = expenseOccurrences
            .Where(x => !x.Plan.Mandatory && x.Option.DueDate <= endMonth)
            .Sum(x => x.Option.RemainingCents);
        var incomeByMonthEnd = expected.Where(x => x.DueDate <= endMonth).Sum(x => x.NetRemainingCents);
        var forecast = totalBalance + incomeByMonthEnd - dueClientByMonthEnd - dueMandatoryExpensesByMonthEnd - dueOptionalExpensesByMonthEnd;
        var previousMonthIncome = GetPreviousMonthIncome(db, today);

        return new DashboardData
        {
            TotalBalanceCents = totalBalance,
            ClientReturnCents = toReturn,
            PaymentsCents = payments,
            MonthMandatoryPaymentsCents = monthMandatoryPayments,
            OptionalPaymentsCents = optionalPayments,
            FreeCents = totalBalance - toReturn - payments,
            ExpectedIncomeCents = expectedTotal,
            MonthExpenseCents = monthExpense,
            ForecastExpectedIncomeCents = incomeByMonthEnd,
            ForecastClientReturnsCents = dueClientByMonthEnd,
            ForecastMandatoryExpensesCents = dueMandatoryExpensesByMonthEnd,
            ForecastOptionalExpensesCents = dueOptionalExpensesByMonthEnd,
            ForecastEndOfMonthCents = forecast,
            PreviousMonthIncomeCents = previousMonthIncome,
            AccountDetails = accountDetails,
            MandatoryPaymentDetails = expenseOccurrences.Where(x => x.Plan.Mandatory).OrderBy(x => x.Option.DueDate).ThenBy(x => x.Plan.Title).Select(x => new DashboardPaymentDetail
            {
                Name = x.Plan.Title,
                DueDate = x.Option.DueDate,
                RemainingCents = x.Option.RemainingCents
            }).ToList(),
            MonthMandatoryPaymentDetails = monthMandatoryOccurrences.Select(x => new DashboardPaymentDetail
            {
                Name = x.Plan.Title,
                DueDate = x.Option.DueDate,
                RemainingCents = x.Option.RemainingCents
            }).ToList(),
            OptionalPaymentDetails = optionalPaymentOccurrences.Select(x => new DashboardPaymentDetail
            {
                Name = x.Plan.Title,
                DueDate = x.Option.DueDate,
                RemainingCents = x.Option.RemainingCents
            }).ToList(),
            CurrentMonthExpectedIncomeDetails = expected
                .Where(x => x.DueDate.Date >= startMonth && x.DueDate.Date <= endMonth)
                .OrderBy(x => x.DueDate).ThenBy(x => x.Name).ToList(),
            OpenLoanDetails = openLoanDetails,
            Obligations = obligations,
            ExpectedIncomes = expected.Take(30).ToList()
        };
    }

    private static long GetPreviousMonthIncome(WinSqliteConnection db, DateTime today)
    {
        var currentMonthStart = new DateTime(today.Year, today.Month, 1);
        var start = currentMonthStart.AddMonths(-1);
        var end = currentMonthStart.AddDays(-1);

        // Service/ordinary income: all actual income operations that do not create
        // a client return obligation.
        var serviceIncome = Convert.ToInt64(db.ExecuteScalar(@"SELECT COALESCE(SUM(amount_cents),0)
FROM transactions
WHERE type='Income' AND date(occurred_at)>=date(?) AND date(occurred_at)<=date(?);",
            start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd")) ?? 0L);

        long closedLoanNet = 0;
        var loans = db.Query(@"SELECT ci.id,ci.principal_cents,ci.return_total_cents
FROM client_inflows ci
JOIN transactions t ON t.id=ci.transaction_id
ORDER BY t.occurred_at,ci.id;");
        foreach (var loan in loans)
        {
            var inflowId = L(loan, "id");
            var returnTotal = L(loan, "return_total_cents");
            if (returnTotal <= 0) continue;

            long running = 0;
            DateTime? closedAt = null;
            var returns = db.Query(@"SELECT occurred_at,amount_cents
FROM transactions
WHERE type='ClientReturn' AND client_inflow_id=?
ORDER BY occurred_at,id;", inflowId);
            foreach (var ret in returns)
            {
                running += L(ret, "amount_cents");
                if (running >= returnTotal)
                {
                    closedAt = ParseDate(S(ret, "occurred_at")).Date;
                    break;
                }
            }

            if (closedAt.HasValue && closedAt.Value >= start && closedAt.Value <= end)
                closedLoanNet += L(loan, "principal_cents") - returnTotal;
        }

        return serviceIncome + closedLoanNet;
    }

    private sealed class ExpenseOccurrenceWithPlan
    {
        public PlannedExpense Plan { get; init; } = null!;
        public ExpenseOccurrenceOption Option { get; init; } = null!;
    }

    private static List<ExpenseOccurrenceWithPlan> GetReservedExpenseOccurrences(WinSqliteConnection db, DateTime today)
    {
        var plans = db.Query("SELECT * FROM planned_expenses WHERE active=1 ORDER BY title;").Select(ReadPlannedExpense).ToList();
        var list = new List<ExpenseOccurrenceWithPlan>();
        foreach (var p in plans)
        {
            if (p.Recurrence == ExpenseRecurrence.Monthly)
            {
                var o = MakeOccurrence(db, p, today);
                if (o.RemainingCents > 0) list.Add(new ExpenseOccurrenceWithPlan { Plan = p, Option = o });
            }
            else if (p.Recurrence is ExpenseRecurrence.Quarterly or ExpenseRecurrence.EveryNMonths)
            {
                var o = MakeOccurrence(db, p, today);
                // Do not reserve occurrences whose first/current due date is in a future year.
                if (o.DueDate.Year <= today.Year && o.RemainingCents > 0) list.Add(new ExpenseOccurrenceWithPlan { Plan = p, Option = o });
            }
            else if (p.Recurrence == ExpenseRecurrence.Annual)
            {
                var o = MakeOccurrence(db, p, today);
                if (o.RemainingCents > 0) list.Add(new ExpenseOccurrenceWithPlan { Plan = p, Option = o });
            }
            else if (p.DueDate.HasValue && p.DueDate.Value.Year <= today.Year)
            {
                var o = MakeOccurrence(db, p, today);
                if (o.RemainingCents > 0) list.Add(new ExpenseOccurrenceWithPlan { Plan = p, Option = o });
            }
        }
        return list;
    }

    private static List<ExpectedIncomeRow> GetExpectedIncomeRows(WinSqliteConnection db)
    {
        var rows = db.Query(@"SELECT p.id,p.title,p.amount_cents,p.due_date,p.creates_return_obligation,COALESCE(c.name,'') client_name,COALESCE(c.return_percent,0) return_percent,
COALESCE((SELECT SUM(t.amount_cents) FROM transactions t WHERE t.planned_income_id=p.id AND t.type IN ('Income','ClientIncome')),0) paid
FROM planned_incomes p LEFT JOIN clients c ON c.id=p.client_id WHERE p.active=1 ORDER BY p.due_date,p.title;");
        var result = new List<ExpectedIncomeRow>();
        foreach (var r in rows)
        {
            var gross = Math.Max(0, L(r, "amount_cents") - L(r, "paid"));
            if (gross <= 0) continue;
            var createsReturn = L(r, "creates_return_obligation") != 0;
            var net = gross;
            if (createsReturn)
            {
                var percent = D(r, "return_percent");
                var returnAmount = Money.ToCents(Money.FromCents(gross) * (1m + percent / 100m));
                net = gross - returnAmount;
            }
            result.Add(new ExpectedIncomeRow
            {
                PlanId = L(r, "id"),
                Name = S(r, "title"),
                Client = S(r, "client_name"),
                DueDate = ParseDate(S(r, "due_date")),
                GrossRemainingCents = gross,
                NetRemainingCents = net,
                CreatesReturnObligation = createsReturn
            });
        }
        return result.OrderBy(x => x.DueDate).ToList();
    }

    private static List<ObligationRow> GetClientObligations(WinSqliteConnection db)
    {
        var inflows = db.Query(@"SELECT ci.id, c.name client_name,
COALESCE((SELECT SUM(t.amount_cents) FROM transactions t WHERE t.type='ClientReturn' AND t.client_inflow_id=ci.id),0) paid
FROM client_inflows ci JOIN clients c ON c.id=ci.client_id ORDER BY ci.id;");
        var list = new List<ObligationRow>();
        foreach (var inflow in inflows)
        {
            var id = L(inflow, "id");
            var paidLeft = L(inflow, "paid");
            var schedules = db.Query("SELECT part_order,due_date,amount_cents FROM client_inflow_schedule WHERE client_inflow_id=? ORDER BY part_order;", id);
            foreach (var s in schedules)
            {
                var part = L(s, "amount_cents");
                var applied = Math.Min(part, Math.Max(0, paidLeft));
                paidLeft -= applied;
                var rem = part - applied;
                if (rem <= 0) continue;
                list.Add(new ObligationRow
                {
                    Kind = "Возврат клиенту",
                    Name = S(inflow, "client_name"),
                    DueDate = ParseDate(S(s, "due_date")),
                    RemainingCents = rem,
                    Mandatory = true
                });
            }
        }
        return list.OrderBy(x => x.DueDate).ToList();
    }

    private static List<DashboardLoanDetail> GetOpenLoanDetails(WinSqliteConnection db)
    {
        var rows = db.Query(@"SELECT ci.id,c.name client_name,t.occurred_at,ci.principal_cents,ci.return_total_cents,
COALESCE((SELECT SUM(r.amount_cents) FROM transactions r WHERE r.type='ClientReturn' AND r.client_inflow_id=ci.id),0) returned_cents
FROM client_inflows ci
JOIN clients c ON c.id=ci.client_id
JOIN transactions t ON t.id=ci.transaction_id
ORDER BY t.occurred_at,ci.id;");
        var result = new List<DashboardLoanDetail>();
        foreach (var row in rows)
        {
            var item = new DashboardLoanDetail
            {
                Client = S(row, "client_name"),
                InflowDate = ParseDate(S(row, "occurred_at")),
                PrincipalCents = L(row, "principal_cents"),
                ReturnTotalCents = L(row, "return_total_cents"),
                ReturnedCents = L(row, "returned_cents")
            };
            if (item.RemainingCents > 0)
                result.Add(item);
        }
        return result;
    }
    #endregion

    #region Holidays / export helpers
    public List<(DateTime Date, string Title)> GetHolidays()
    {
        using var db = Open();
        return db.Query("SELECT date,title FROM holidays ORDER BY date;")
            .Select(r => (ParseDate(S(r, "date")), S(r, "title"))).ToList();
    }

    public void AddHoliday(DateTime date, string title)
    {
        using var db = Open();
        db.ExecuteNonQuery("INSERT OR REPLACE INTO holidays(date,title) VALUES(?,?);", date.ToString("yyyy-MM-dd"), title.Trim());
    }

    public void DeleteHoliday(DateTime date)
    {
        using var db = Open();
        db.ExecuteNonQuery("DELETE FROM holidays WHERE date=?;", date.ToString("yyyy-MM-dd"));
    }

    public List<ClientInflowOption> GetAllOpenClientInflows()
    {
        using var db = Open();
        var clientIds = db.Query("SELECT id FROM clients;").Select(r => L(r, "id"));
        return clientIds.SelectMany(id => GetClientInflowOptions(db, id)).OrderBy(x => x.Date).ToList();
    }

    public List<ClientInflowHistoryRow> GetClientInflowHistory()
    {
        using var db = Open();
        var rows = db.Query(@"SELECT ci.id,t.occurred_at,c.name client_name,ci.principal_cents,ci.return_percent,ci.return_total_cents,
COALESCE((SELECT SUM(r.amount_cents) FROM transactions r WHERE r.type='ClientReturn' AND r.client_inflow_id=ci.id),0) returned_cents
FROM client_inflows ci JOIN clients c ON c.id=ci.client_id JOIN transactions t ON t.id=ci.transaction_id
ORDER BY t.occurred_at,ci.id;");
        var result = new List<ClientInflowHistoryRow>();
        foreach (var r in rows)
        {
            var id = L(r, "id");
            var schedule = db.Query("SELECT part_order,percentage,due_date,amount_cents FROM client_inflow_schedule WHERE client_inflow_id=? ORDER BY part_order;", id);
            var text = string.Join("; ", schedule.Select(x => $"{D(x, "percentage"):N2}% — до {ParseDate(S(x, "due_date")):dd.MM.yyyy} — {Money.Format(L(x, "amount_cents"))}"));
            result.Add(new ClientInflowHistoryRow
            {
                Id = id,
                Date = ParseDate(S(r, "occurred_at")),
                Client = S(r, "client_name"),
                PrincipalCents = L(r, "principal_cents"),
                ReturnPercent = D(r, "return_percent"),
                ReturnTotalCents = L(r, "return_total_cents"),
                ReturnedCents = L(r, "returned_cents"),
                Schedule = text
            });
        }
        return result;
    }

    public string CreateBackup(string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using (var db = Open()) db.ExecuteNonQuery("PRAGMA wal_checkpoint(FULL);");
        File.Copy(DatabasePath, destinationPath, true);
        return destinationPath;
    }

    public bool BackupOnStart
    {
        get
        {
            using var db = Open();
            return string.Equals(Convert.ToString(db.ExecuteScalar("SELECT value FROM app_settings WHERE key='backup_on_start';")), "1", StringComparison.Ordinal);
        }
    }

    public void SetBackupOnStart(bool enabled)
    {
        using var db = Open();
        db.ExecuteNonQuery("INSERT OR REPLACE INTO app_settings(key,value) VALUES('backup_on_start',?);", enabled ? "1" : "0");
    }

    private static readonly byte[] BackupPasswordEntropy = Encoding.UTF8.GetBytes("FinanceTracker.BackupPassword.v1");

    public bool HasBackupPassword => !string.IsNullOrWhiteSpace(GetBackupPasswordForSettings());

    public string GetBackupPasswordForSettings()
    {
        try { return ReadBackupPassword(strict: false); }
        catch { return ""; }
    }

    public void SetBackupPassword(string password)
    {
        var normalized = password ?? "";
        string protectedValue = "";
        if (!string.IsNullOrEmpty(normalized))
        {
            var plain = Encoding.UTF8.GetBytes(normalized);
            var protectedBytes = ProtectedData.Protect(plain, BackupPasswordEntropy, DataProtectionScope.CurrentUser);
            protectedValue = Convert.ToBase64String(protectedBytes);
        }
        using var db = Open();
        db.ExecuteNonQuery("INSERT OR REPLACE INTO app_settings(key,value) VALUES('backup_password_protected',?);", protectedValue);
    }

    private string ReadBackupPassword(bool strict)
    {
        using var db = Open();
        var value = Convert.ToString(db.ExecuteScalar("SELECT value FROM app_settings WHERE key='backup_password_protected';")) ?? "";
        if (string.IsNullOrWhiteSpace(value)) return "";
        try
        {
            var protectedBytes = Convert.FromBase64String(value);
            var plain = ProtectedData.Unprotect(protectedBytes, BackupPasswordEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (!strict && (ex is CryptographicException || ex is FormatException))
        {
            return "";
        }
        catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
        {
            throw new InvalidOperationException("Не удалось прочитать сохранённый пароль архива. Откройте Настройки и задайте пароль заново.", ex);
        }
    }

    public string CreateZipBackupBesideExecutable()
    {
        using (var db = Open()) db.ExecuteNonQuery("PRAGMA wal_checkpoint(FULL);");

        var folder = AppContext.BaseDirectory;
        Directory.CreateDirectory(folder);
        var finalName = $"FinanceTracker_backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";
        var finalPath = Path.Combine(folder, finalName);
        var tempPath = Path.Combine(folder, $".FinanceTracker_backup_{Guid.NewGuid():N}.tmp");
        var password = ReadBackupPassword(strict: true);
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipOutputStream(fs))
            {
                zip.IsStreamOwner = false;
                zip.SetLevel(9);
                if (!string.IsNullOrEmpty(password)) zip.Password = password;

                var fileInfo = new FileInfo(DatabasePath);
                var entry = new ZipEntry("finance.db")
                {
                    DateTime = DateTime.Now,
                    Size = fileInfo.Length
                };
                if (!string.IsNullOrEmpty(password)) entry.AESKeySize = 256;

                zip.PutNextEntry(entry);
                using (var input = new FileStream(DatabasePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    input.CopyTo(zip);
                zip.CloseEntry();
                zip.Finish();
            }

            foreach (var old in Directory.GetFiles(folder, "FinanceTracker_backup_*.zip"))
            {
                if (string.Equals(old, finalPath, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(old); } catch { /* The new backup is more important than cleanup. */ }
            }
            File.Move(tempPath, finalPath, true);
            return finalPath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private static HashSet<DateTime> LoadHolidaySet(WinSqliteConnection db) => db.Query("SELECT date FROM holidays;")
        .Select(r => ParseDate(S(r, "date")).Date).ToHashSet();

    private static DateTime AddWorkingDays(DateTime date, int workdays, HashSet<DateTime> holidays)
    {
        var d = date.Date;
        var left = workdays;
        while (left > 0)
        {
            d = d.AddDays(1);
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (holidays.Contains(d)) continue;
            left--;
        }
        return d;
    }
    #endregion

    #region Helpers
    private static void EnsureColumn(WinSqliteConnection db, string table, string column, string definition)
    {
        var exists = db.Query($"PRAGMA table_info({table});").Any(r => string.Equals(S(r, "name"), column, StringComparison.OrdinalIgnoreCase));
        if (!exists) db.ExecuteNonQuery($"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    private static string DateText(DateTime date) => date.ToString("yyyy-MM-dd HH:mm:ss");
    private static DateTime ParseDate(string s)
    {
        var formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy" };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)) return exact;
        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var current)) return current;
        return DateTime.MinValue;
    }
    private static DateTime? ParseDateNullable(object? o) => o == null || string.IsNullOrWhiteSpace(Convert.ToString(o)) ? null : ParseDate(Convert.ToString(o)!);
    private static int? INullable(object? o) => o == null ? null : Convert.ToInt32(o);
    private static long? LNullable(object? o) => o == null ? null : Convert.ToInt64(o);
    private static string S(Dictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null ? Convert.ToString(v) ?? "" : "";
    private static long L(Dictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null ? Convert.ToInt64(v) : 0L;
    private static decimal D(Dictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null ? Convert.ToDecimal(v) : 0m;
    private static bool IsLoanClient(WinSqliteConnection db, long clientId)
        => Convert.ToInt64(db.ExecuteScalar("SELECT COALESCE(is_loan,0) FROM clients WHERE id=?;", clientId) ?? 0L) != 0;

    private static DateTime RecurringDueDate(DateTime anchor, DateTime basis, int intervalMonths)
    {
        anchor = anchor.Date; basis = basis.Date; intervalMonths = Math.Max(1, intervalMonths);
        if (basis < anchor) return anchor;
        var months = (basis.Year - anchor.Year) * 12 + basis.Month - anchor.Month;
        var cycle = Math.Max(0, months / intervalMonths);
        var targetMonth = anchor.AddMonths(cycle * intervalMonths);
        return SafeDate(targetMonth.Year, targetMonth.Month, anchor.Day);
    }

    private static DateTime SafeDate(int year, int month, int day)
    {
        var safeDay = Math.Max(1, Math.Min(day, DateTime.DaysInMonth(year, month)));
        return new DateTime(year, month, safeDay);
    }
    #endregion
}
