namespace FinanceTracker;

internal enum TransactionKind
{
    Income,
    ClientIncome,
    Expense,
    ClientReturn,
    Transfer,
    Adjustment
}

internal enum ExpenseRecurrence
{
    Monthly,
    Quarterly,
    EveryNMonths,
    Annual,
    OneTime
}

internal enum CategoryScope
{
    Both,
    Income,
    Expense
}

internal sealed class Account
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long InitialBalanceCents { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefaultIncome { get; set; }
    public bool IsDefaultExpense { get; set; }
    public long CurrentBalanceCents { get; set; }
    public override string ToString() => Name;
}

internal sealed class Category
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public CategoryScope Scope { get; set; } = CategoryScope.Both;
    public bool IsActive { get; set; } = true;
    public override string ToString() => Name;
}

internal sealed class Client
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public decimal ReturnPercent { get; set; }
    public bool IsActive { get; set; }
    public bool IsLoan { get; set; }
    public string Notes { get; set; } = "";
    public List<ClientReturnRule> Rules { get; set; } = new();
    public override string ToString() => Name;
}

internal sealed class ClientReturnRule
{
    public int PartOrder { get; set; }
    public decimal Percentage { get; set; }
    public int Workdays { get; set; }
}

internal sealed class PlannedExpense
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public long AmountCents { get; set; }
    public ExpenseRecurrence Recurrence { get; set; }
    public DateTime? DueDate { get; set; }
    public int? DueMonth { get; set; }
    public int? DueDay { get; set; }
    public int IntervalMonths { get; set; } = 1;
    public string Category { get; set; } = "";
    public bool Mandatory { get; set; }
    public bool Active { get; set; }
    public override string ToString() => Title;
}

internal sealed class PlannedIncome
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public long AmountCents { get; set; }
    public DateTime DueDate { get; set; }
    public long? ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public string Category { get; set; } = "";
    public bool CreatesReturnObligation { get; set; }
    public bool Active { get; set; }
    public string Comment { get; set; } = "";
    public long PaidCents { get; set; }
    public long RemainingCents => Math.Max(0, AmountCents - PaidCents);
    public override string ToString() => $"{Title} — {Money.Format(RemainingCents)}";
}

internal sealed class ClientInflowOption
{
    public long Id { get; set; }
    public string ClientName { get; set; } = "";
    public DateTime Date { get; set; }
    public long PrincipalCents { get; set; }
    public long ReturnTotalCents { get; set; }
    public long ReturnedCents { get; set; }
    public long RemainingCents => Math.Max(0, ReturnTotalCents - ReturnedCents);
    public override string ToString() => $"{Date:dd.MM.yyyy} · {Money.Format(PrincipalCents)} · осталось {Money.Format(RemainingCents)}";
}

internal sealed class ExpenseOccurrenceOption
{
    public long PlanId { get; set; }
    public string Title { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public DateTime DueDate { get; set; }
    public long AmountCents { get; set; }
    public long PaidCents { get; set; }
    public long RemainingCents => Math.Max(0, AmountCents - PaidCents);
    public override string ToString() => $"{Title} · до {DueDate:dd.MM.yyyy} · осталось {Money.Format(RemainingCents)}";
}

internal sealed class ObligationRow
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime DueDate { get; set; }
    public long RemainingCents { get; set; }
    public bool Mandatory { get; set; }
    public bool IsOverdue => DueDate.Date < DateTime.Today;
    public bool IsCurrentMonth => DueDate.Year == DateTime.Today.Year && DueDate.Month == DateTime.Today.Month;
}

internal sealed class ExpectedIncomeRow
{
    public long PlanId { get; set; }
    public string Name { get; set; } = "";
    public string Client { get; set; } = "";
    public DateTime DueDate { get; set; }
    public long GrossRemainingCents { get; set; }
    public long NetRemainingCents { get; set; }
    public bool CreatesReturnObligation { get; set; }
    public bool IsOverdue => DueDate.Date < DateTime.Today;
}


internal sealed class DashboardAccountDetail
{
    public string Name { get; set; } = "";
    public long BalanceCents { get; set; }
}

internal sealed class DashboardPaymentDetail
{
    public string Name { get; set; } = "";
    public DateTime DueDate { get; set; }
    public long RemainingCents { get; set; }
}

internal sealed class DashboardLoanDetail
{
    public string Client { get; set; } = "";
    public DateTime InflowDate { get; set; }
    public long PrincipalCents { get; set; }
    public long ReturnTotalCents { get; set; }
    public long ReturnedCents { get; set; }
    public long RemainingCents => Math.Max(0, ReturnTotalCents - ReturnedCents);
}

internal sealed class DashboardData
{
    public long TotalBalanceCents { get; set; }
    public long FreeCents { get; set; }
    public long ClientReturnCents { get; set; }
    public long PaymentsCents { get; set; }
    public long MonthMandatoryPaymentsCents { get; set; }
    public long OptionalPaymentsCents { get; set; }
    public long ExpectedIncomeCents { get; set; }
    public long MonthExpenseCents { get; set; }
    public long ForecastExpectedIncomeCents { get; set; }
    public long ForecastClientReturnsCents { get; set; }
    public long ForecastMandatoryExpensesCents { get; set; }
    public long ForecastOptionalExpensesCents { get; set; }
    public long ForecastEndOfMonthCents { get; set; }
    public long PreviousMonthIncomeCents { get; set; }
    public List<DashboardAccountDetail> AccountDetails { get; set; } = new();
    public List<DashboardPaymentDetail> MandatoryPaymentDetails { get; set; } = new();
    public List<DashboardPaymentDetail> MonthMandatoryPaymentDetails { get; set; } = new();
    public List<DashboardPaymentDetail> OptionalPaymentDetails { get; set; } = new();
    public List<ExpectedIncomeRow> CurrentMonthExpectedIncomeDetails { get; set; } = new();
    public List<DashboardLoanDetail> OpenLoanDetails { get; set; } = new();
    public List<ObligationRow> Obligations { get; set; } = new();
    public List<ExpectedIncomeRow> ExpectedIncomes { get; set; } = new();
}

internal sealed class ClientInflowHistoryRow
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public string Client { get; set; } = "";
    public long PrincipalCents { get; set; }
    public decimal ReturnPercent { get; set; }
    public long ReturnTotalCents { get; set; }
    public long ReturnedCents { get; set; }
    public long RemainingCents => Math.Max(0, ReturnTotalCents - ReturnedCents);
    public string Schedule { get; set; } = "";
}

internal enum LoanSettlementStatus
{
    None,
    Open,
    Partial,
    Closed
}

internal sealed class HistoryOperationRow
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public string Type { get; set; } = "";
    public string Client { get; set; } = "";
    public long AmountCents { get; set; }
    public long IncomeCents { get; set; }
    public long OutflowCents { get; set; }
    public string Account { get; set; } = "";
    public string Category { get; set; } = "";
    public string ClientReturnInfo { get; set; } = "";
    public string ReturnOperations { get; set; } = "";
    public LoanSettlementStatus LoanStatus { get; set; } = LoanSettlementStatus.None;
    public string Comment { get; set; } = "";
}

internal sealed class TransactionListRow
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public string Type { get; set; } = "";
    public string Account { get; set; } = "";
    public string ToAccount { get; set; } = "";
    public string Client { get; set; } = "";
    public long AmountCents { get; set; }
    public string Category { get; set; } = "";
    public string Plan { get; set; } = "";
    public string Comment { get; set; } = "";
}

internal sealed class TransactionEditData
{
    public long Id { get; set; }
    public TransactionKind Kind { get; set; }
    public DateTime Date { get; set; }
    public long? AccountId { get; set; }
    public long? ToAccountId { get; set; }
    public long AmountCents { get; set; }
    public long? ClientId { get; set; }
    public long? ClientInflowId { get; set; }
    public long? PlannedExpenseId { get; set; }
    public long? PlannedIncomeId { get; set; }
    public string PlanPeriod { get; set; } = "";
    public string Category { get; set; } = "";
    public string Comment { get; set; } = "";
    public DateTime? OverrideReturnDueDate { get; set; }
    public bool CreatesReturnObligation => Kind == TransactionKind.ClientIncome;
}

internal static class Money
{
    // Storage remains in kopecks for backward compatibility with the existing database.
    public static long ToCents(decimal rubles) => decimal.ToInt64(decimal.Round(rubles * 100m, 0, MidpointRounding.AwayFromZero));
    public static decimal FromCents(long cents) => cents / 100m;

    // The user interface works in thousands of rubles: 10.5 means 10,500 rubles.
    public static long FromThousands(decimal thousands) => ToCents(thousands * 1000m);
    public static decimal ToThousands(long cents) => FromCents(cents) / 1000m;
    public static string Format(long cents) => $"{ToThousands(cents):#,##0.###} тыс. ₽";
    public static string FormatNoKopecks(long cents) => Format(cents);
}

internal static class UiText
{
    public static string TransactionType(TransactionKind kind) => kind switch
    {
        TransactionKind.Income => "Поступление",
        TransactionKind.ClientIncome => "Поступление клиента с возвратом",
        TransactionKind.Expense => "Расход",
        TransactionKind.ClientReturn => "Возврат клиенту",
        TransactionKind.Transfer => "Перевод",
        TransactionKind.Adjustment => "Корректировка",
        _ => kind.ToString()
    };

    public static string Recurrence(ExpenseRecurrence recurrence) => recurrence switch
    {
        ExpenseRecurrence.Monthly => "Ежемесячно",
        ExpenseRecurrence.Quarterly => "Раз в квартал",
        ExpenseRecurrence.EveryNMonths => "Раз в несколько месяцев",
        ExpenseRecurrence.Annual => "Ежегодно",
        ExpenseRecurrence.OneTime => "Разово",
        _ => recurrence.ToString()
    };

    public static string CategoryScopeText(CategoryScope scope) => scope switch
    {
        CategoryScope.Income => "Поступления",
        CategoryScope.Expense => "Расходы",
        _ => "Поступления и расходы"
    };
}
