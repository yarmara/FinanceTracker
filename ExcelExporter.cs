using System.IO.Compression;
using System.Text;
using System.Xml;

namespace FinanceTracker;

internal sealed class ExcelExporter
{
    private readonly FinanceDatabase _db;

    public ExcelExporter(FinanceDatabase db) => _db = db;

    public void Export(string filePath)
    {
        var dashboard = _db.GetDashboard();
        var transactions = _db.GetTransactions(int.MaxValue);
        var clients = _db.GetClients(false);
        var clientInflows = _db.GetClientInflowHistory();
        var expenses = _db.GetPlannedExpenses(false);
        var incomes = _db.GetPlannedIncomes(false);
        var accounts = _db.GetAccounts(false);
        var categories = _db.GetCategories(false);
        var holidays = _db.GetHolidays();

        var sheets = new List<SheetSpec>
        {
            BuildSummary(dashboard),
            BuildTransactions(transactions),
            BuildClientInflows(clientInflows),
            BuildClients(clients),
            BuildExpenses(expenses),
            BuildIncomes(incomes),
            BuildAccounts(accounts),
            BuildCategories(categories),
            BuildHolidays(holidays)
        };

        if (File.Exists(filePath)) File.Delete(filePath);
        using var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

        WriteContentTypes(zip, sheets.Count);
        WriteRootRels(zip);
        WriteWorkbook(zip, sheets);
        WriteWorkbookRels(zip, sheets.Count);
        WriteStyles(zip);
        WriteCoreProperties(zip);
        WriteAppProperties(zip, sheets);
        for (var i = 0; i < sheets.Count; i++) WriteSheet(zip, sheets[i], i + 1);
    }

    private static SheetSpec BuildSummary(DashboardData d)
    {
        var s = new SheetSpec("Сводка", new[] { "Показатель", "Сумма" });
        s.Add("Общий баланс", Cell.Money(d.TotalBalanceCents));
        s.Add("Свободно по итогам", Cell.Money(d.FreeCents));
        s.Add("К возврату клиентам", Cell.Money(d.ClientReturnCents));
        s.Add("Остаток обязательных платежей на текущий год", Cell.Money(d.PaymentsCents));
        s.Add("Остаток обязательных платежей на текущий месяц", Cell.Money(d.MonthMandatoryPaymentsCents));
        s.Add("Необязательные запланированные платежи на текущий год", Cell.Money(d.OptionalPaymentsCents));
        s.Add("Ожидаемый доход текущего месяца", Cell.Money(d.ExpectedIncomeCents));
        s.Add("Плановые расходы текущего месяца", Cell.Money(d.MonthExpenseCents));
        s.Add("Прогноз на конец месяца", Cell.Money(d.ForecastEndOfMonthCents));
        return s;
    }

    private static SheetSpec BuildTransactions(IEnumerable<TransactionListRow> rows)
    {
        var s = new SheetSpec("Операции", new[] { "ID", "Дата", "Тип", "Счёт", "Счёт назначения", "Клиент", "Сумма", "Категория", "План", "Комментарий" });
        foreach (var r in rows)
            s.Add(r.Id, r.Date, r.Type, r.Account, r.ToAccount, r.Client, Cell.Money(r.AmountCents), r.Category, r.Plan, r.Comment);
        return s;
    }

    private static SheetSpec BuildClientInflows(IEnumerable<ClientInflowHistoryRow> rows)
    {
        var s = new SheetSpec("Поступления клиентов", new[] { "ID", "Дата", "Клиент", "Получено", "% к возврату", "Нужно вернуть", "Уже возвращено", "Осталось", "Зафиксированный график" });
        foreach (var r in rows)
            s.Add(r.Id, r.Date, r.Client, Cell.Money(r.PrincipalCents), r.ReturnPercent, Cell.Money(r.ReturnTotalCents), Cell.Money(r.ReturnedCents), Cell.Money(r.RemainingCents), r.Schedule);
        return s;
    }

    private static SheetSpec BuildClients(IEnumerable<Client> rows)
    {
        var s = new SheetSpec("Клиенты", new[] { "ID", "Клиент", "Займы", "% возврата", "График возврата", "Активен", "Примечание" });
        foreach (var c in rows)
        {
            var schedule = c.IsLoan ? string.Join("; ", c.Rules.OrderBy(x => x.PartOrder).Select(x => $"{x.Percentage:N2}% через {x.Workdays} раб. дн.")) : "";
            s.Add(c.Id, c.Name, c.IsLoan ? "Да" : "Нет", c.IsLoan ? c.ReturnPercent : 0m, schedule, c.IsActive ? "Да" : "Нет", c.Notes);
        }
        return s;
    }

    private static SheetSpec BuildExpenses(IEnumerable<PlannedExpense> rows)
    {
        var s = new SheetSpec("План расходов", new[] { "ID", "Название", "Сумма", "Периодичность", "Срок", "Категория", "Обязательный", "Активен" });
        foreach (var p in rows)
        {
            var due = p.Recurrence switch
            {
                ExpenseRecurrence.Monthly => $"{p.DueDay}-е число каждого месяца",
                ExpenseRecurrence.Quarterly => $"с {p.DueDate:dd.MM.yyyy}, каждые 3 месяца",
                ExpenseRecurrence.EveryNMonths => $"с {p.DueDate:dd.MM.yyyy}, каждые {p.IntervalMonths} месяцев",
                ExpenseRecurrence.Annual => $"{p.DueDay.GetValueOrDefault():00}.{p.DueMonth.GetValueOrDefault():00} ежегодно",
                _ => p.DueDate?.ToString("dd.MM.yyyy") ?? ""
            };
            s.Add(p.Id, p.Title, Cell.Money(p.AmountCents), UiText.Recurrence(p.Recurrence), due, p.Category, p.Mandatory ? "Да" : "Нет", p.Active ? "Да" : "Нет");
        }
        return s;
    }

    private static SheetSpec BuildIncomes(IEnumerable<PlannedIncome> rows)
    {
        var s = new SheetSpec("План поступлений", new[] { "ID", "Название", "Сумма", "Получено", "Осталось", "Срок", "Клиент", "Возврат клиенту", "Категория", "Активен", "Комментарий" });
        foreach (var p in rows)
            s.Add(p.Id, p.Title, Cell.Money(p.AmountCents), Cell.Money(p.PaidCents), Cell.Money(p.RemainingCents), p.DueDate, p.ClientName, p.CreatesReturnObligation ? "Да" : "Нет", p.Category, p.Active ? "Да" : "Нет", p.Comment);
        return s;
    }

    private static SheetSpec BuildAccounts(IEnumerable<Account> rows)
    {
        var s = new SheetSpec("Счета", new[] { "ID", "Счёт", "Начальный остаток", "Текущий остаток", "Основной для поступлений", "Основной для расходов", "Активен" });
        foreach (var a in rows)
            s.Add(a.Id, a.Name, Cell.Money(a.InitialBalanceCents), Cell.Money(a.CurrentBalanceCents), a.IsDefaultIncome ? "Да" : "Нет", a.IsDefaultExpense ? "Да" : "Нет", a.IsActive ? "Да" : "Нет");
        return s;
    }

    private static SheetSpec BuildCategories(IEnumerable<Category> rows)
    {
        var s = new SheetSpec("Категории", new[] { "ID", "Категория", "Использование", "Активна" });
        foreach (var c in rows) s.Add(c.Id, c.Name, UiText.CategoryScopeText(c.Scope), c.IsActive ? "Да" : "Нет");
        return s;
    }

    private static SheetSpec BuildHolidays(IEnumerable<(DateTime Date, string Title)> rows)
    {
        var s = new SheetSpec("Праздники", new[] { "Дата", "Название" });
        foreach (var h in rows) s.Add(h.Date, h.Title);
        return s;
    }

    private static void WriteContentTypes(ZipArchive zip, int sheetCount)
    {
        using var xw = CreateWriter(zip, "[Content_Types].xml");
        xw.WriteStartDocument(true);
        xw.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
        xw.WriteStartElement("Default"); xw.WriteAttributeString("Extension", "rels"); xw.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml"); xw.WriteEndElement();
        xw.WriteStartElement("Default"); xw.WriteAttributeString("Extension", "xml"); xw.WriteAttributeString("ContentType", "application/xml"); xw.WriteEndElement();
        Override(xw, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        Override(xw, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        for (var i = 1; i <= sheetCount; i++) Override(xw, $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        Override(xw, "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml");
        Override(xw, "/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml");
        xw.WriteEndElement();
    }

    private static void Override(XmlWriter xw, string part, string contentType)
    {
        xw.WriteStartElement("Override");
        xw.WriteAttributeString("PartName", part);
        xw.WriteAttributeString("ContentType", contentType);
        xw.WriteEndElement();
    }

    private static void WriteRootRels(ZipArchive zip)
    {
        using var xw = CreateWriter(zip, "_rels/.rels");
        xw.WriteStartDocument(true);
        xw.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
        Rel(xw, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
        Rel(xw, "rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml");
        Rel(xw, "rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties", "docProps/app.xml");
        xw.WriteEndElement();
    }

    private static void Rel(XmlWriter xw, string id, string type, string target)
    {
        xw.WriteStartElement("Relationship");
        xw.WriteAttributeString("Id", id); xw.WriteAttributeString("Type", type); xw.WriteAttributeString("Target", target);
        xw.WriteEndElement();
    }

    private static void WriteWorkbook(ZipArchive zip, List<SheetSpec> sheets)
    {
        using var xw = CreateWriter(zip, "xl/workbook.xml");
        xw.WriteStartDocument(true);
        xw.WriteStartElement("workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        xw.WriteAttributeString("xmlns", "r", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        xw.WriteStartElement("sheets");
        for (var i = 0; i < sheets.Count; i++)
        {
            xw.WriteStartElement("sheet");
            xw.WriteAttributeString("name", sheets[i].Name);
            xw.WriteAttributeString("sheetId", (i + 1).ToString());
            xw.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", $"rId{i + 1}");
            xw.WriteEndElement();
        }
        xw.WriteEndElement();
        xw.WriteStartElement("calcPr"); xw.WriteAttributeString("calcId", "191029"); xw.WriteAttributeString("fullCalcOnLoad", "1"); xw.WriteEndElement();
        xw.WriteEndElement();
    }

    private static void WriteWorkbookRels(ZipArchive zip, int count)
    {
        using var xw = CreateWriter(zip, "xl/_rels/workbook.xml.rels");
        xw.WriteStartDocument(true);
        xw.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
        for (var i = 1; i <= count; i++) Rel(xw, $"rId{i}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", $"worksheets/sheet{i}.xml");
        Rel(xw, $"rId{count + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
        xw.WriteEndElement();
    }

    private static void WriteStyles(ZipArchive zip)
    {
        using var xw = CreateWriter(zip, "xl/styles.xml");
        xw.WriteStartDocument(true);
        xw.WriteStartElement("styleSheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        xw.WriteStartElement("numFmts"); xw.WriteAttributeString("count", "1");
        xw.WriteStartElement("numFmt"); xw.WriteAttributeString("numFmtId", "164"); xw.WriteAttributeString("formatCode", "#,##0.### \"тыс. ₽\";[Red](#,##0.### \"тыс. ₽\");-"); xw.WriteEndElement();
        xw.WriteEndElement();
        xw.WriteStartElement("fonts"); xw.WriteAttributeString("count", "2");
        WriteFont(xw, false); WriteFont(xw, true); xw.WriteEndElement();
        xw.WriteStartElement("fills"); xw.WriteAttributeString("count", "3");
        WriteFill(xw, "none", null); WriteFill(xw, "gray125", null); WriteFill(xw, "solid", "1F4E78"); xw.WriteEndElement();
        xw.WriteStartElement("borders"); xw.WriteAttributeString("count", "1");
        xw.WriteStartElement("border"); xw.WriteElementString("left", ""); xw.WriteElementString("right", ""); xw.WriteElementString("top", ""); xw.WriteElementString("bottom", ""); xw.WriteElementString("diagonal", ""); xw.WriteEndElement(); xw.WriteEndElement();
        xw.WriteStartElement("cellStyleXfs"); xw.WriteAttributeString("count", "1"); WriteXf(xw, 0, 0, 0, 0, false); xw.WriteEndElement();
        xw.WriteStartElement("cellXfs"); xw.WriteAttributeString("count", "3");
        WriteXf(xw, 0, 0, 0, 0, false);
        WriteXf(xw, 1, 2, 0, 0, true);
        WriteXf(xw, 0, 0, 0, 164, false, true);
        xw.WriteEndElement();
        xw.WriteStartElement("cellStyles"); xw.WriteAttributeString("count", "1");
        xw.WriteStartElement("cellStyle"); xw.WriteAttributeString("name", "Normal"); xw.WriteAttributeString("xfId", "0"); xw.WriteAttributeString("builtinId", "0"); xw.WriteEndElement(); xw.WriteEndElement();
        xw.WriteEndElement();
    }

    private static void WriteFont(XmlWriter xw, bool bold)
    {
        xw.WriteStartElement("font");
        if (bold) xw.WriteElementString("b", "");
        xw.WriteStartElement("sz"); xw.WriteAttributeString("val", "11"); xw.WriteEndElement();
        xw.WriteStartElement("name"); xw.WriteAttributeString("val", "Calibri"); xw.WriteEndElement();
        xw.WriteStartElement("family"); xw.WriteAttributeString("val", "2"); xw.WriteEndElement();
        xw.WriteEndElement();
    }

    private static void WriteFill(XmlWriter xw, string pattern, string? rgb)
    {
        xw.WriteStartElement("fill"); xw.WriteStartElement("patternFill"); xw.WriteAttributeString("patternType", pattern);
        if (rgb != null) { xw.WriteStartElement("fgColor"); xw.WriteAttributeString("rgb", "FF" + rgb); xw.WriteEndElement(); xw.WriteStartElement("bgColor"); xw.WriteAttributeString("indexed", "64"); xw.WriteEndElement(); }
        xw.WriteEndElement(); xw.WriteEndElement();
    }

    private static void WriteXf(XmlWriter xw, int fontId, int fillId, int borderId, int numFmtId, bool alignment, bool number = false)
    {
        xw.WriteStartElement("xf");
        xw.WriteAttributeString("numFmtId", numFmtId.ToString()); xw.WriteAttributeString("fontId", fontId.ToString()); xw.WriteAttributeString("fillId", fillId.ToString()); xw.WriteAttributeString("borderId", borderId.ToString()); xw.WriteAttributeString("xfId", "0");
        if (fontId != 0) xw.WriteAttributeString("applyFont", "1");
        if (fillId != 0) xw.WriteAttributeString("applyFill", "1");
        if (number) xw.WriteAttributeString("applyNumberFormat", "1");
        if (alignment)
        {
            xw.WriteAttributeString("applyAlignment", "1");
            xw.WriteStartElement("alignment"); xw.WriteAttributeString("horizontal", "center"); xw.WriteAttributeString("vertical", "center"); xw.WriteEndElement();
        }
        xw.WriteEndElement();
    }

    private static void WriteCoreProperties(ZipArchive zip)
    {
        using var xw = CreateWriter(zip, "docProps/core.xml");
        xw.WriteStartDocument(true);
        xw.WriteStartElement("cp", "coreProperties", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
        xw.WriteAttributeString("xmlns", "dc", null, "http://purl.org/dc/elements/1.1/");
        xw.WriteAttributeString("xmlns", "dcterms", null, "http://purl.org/dc/terms/");
        xw.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
        xw.WriteElementString("dc", "creator", "http://purl.org/dc/elements/1.1/", "Финансовый учёт");
        xw.WriteStartElement("dcterms", "created", "http://purl.org/dc/terms/"); xw.WriteAttributeString("xsi", "type", "http://www.w3.org/2001/XMLSchema-instance", "dcterms:W3CDTF"); xw.WriteString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")); xw.WriteEndElement();
        xw.WriteEndElement();
    }

    private static void WriteAppProperties(ZipArchive zip, List<SheetSpec> sheets)
    {
        using var xw = CreateWriter(zip, "docProps/app.xml");
        xw.WriteStartDocument(true);
        xw.WriteStartElement("Properties", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties");
        xw.WriteAttributeString("xmlns", "vt", null, "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes");
        xw.WriteElementString("Application", "FinanceTracker");
        xw.WriteStartElement("TitlesOfParts"); xw.WriteStartElement("vt", "vector", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"); xw.WriteAttributeString("size", sheets.Count.ToString()); xw.WriteAttributeString("baseType", "lpstr");
        foreach (var s in sheets) xw.WriteElementString("vt", "lpstr", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes", s.Name);
        xw.WriteEndElement(); xw.WriteEndElement();
        xw.WriteEndElement();
    }

    private static void WriteSheet(ZipArchive zip, SheetSpec sheet, int index)
    {
        using var xw = CreateWriter(zip, $"xl/worksheets/sheet{index}.xml");
        xw.WriteStartDocument(true);
        xw.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        xw.WriteStartElement("sheetViews"); xw.WriteStartElement("sheetView"); xw.WriteAttributeString("workbookViewId", "0");
        xw.WriteStartElement("pane"); xw.WriteAttributeString("ySplit", "1"); xw.WriteAttributeString("topLeftCell", "A2"); xw.WriteAttributeString("activePane", "bottomLeft"); xw.WriteAttributeString("state", "frozen"); xw.WriteEndElement();
        xw.WriteEndElement(); xw.WriteEndElement();

        var widths = sheet.GetWidths();
        xw.WriteStartElement("cols");
        for (var i = 0; i < widths.Length; i++)
        {
            xw.WriteStartElement("col"); xw.WriteAttributeString("min", (i + 1).ToString()); xw.WriteAttributeString("max", (i + 1).ToString()); xw.WriteAttributeString("width", widths[i].ToString(System.Globalization.CultureInfo.InvariantCulture)); xw.WriteAttributeString("customWidth", "1"); xw.WriteEndElement();
        }
        xw.WriteEndElement();

        xw.WriteStartElement("sheetData");
        WriteRow(xw, 1, sheet.Headers.Select(h => new Cell(h) { Header = true }).ToArray());
        for (var i = 0; i < sheet.Rows.Count; i++) WriteRow(xw, i + 2, sheet.Rows[i]);
        xw.WriteEndElement();
        if (sheet.Headers.Length > 0)
        {
            xw.WriteStartElement("autoFilter"); xw.WriteAttributeString("ref", $"A1:{ColumnName(sheet.Headers.Length)}{Math.Max(1, sheet.Rows.Count + 1)}"); xw.WriteEndElement();
        }
        xw.WriteEndElement();
    }

    private static void WriteRow(XmlWriter xw, int rowIndex, Cell[] cells)
    {
        xw.WriteStartElement("row"); xw.WriteAttributeString("r", rowIndex.ToString());
        for (var col = 0; col < cells.Length; col++) WriteCell(xw, $"{ColumnName(col + 1)}{rowIndex}", cells[col]);
        xw.WriteEndElement();
    }

    private static void WriteCell(XmlWriter xw, string reference, Cell c)
    {
        xw.WriteStartElement("c"); xw.WriteAttributeString("r", reference);
        if (c.Header) xw.WriteAttributeString("s", "1");
        else if (c.IsMoney) xw.WriteAttributeString("s", "2");

        if (c.IsMoney)
        {
            xw.WriteElementString("v", Money.ToThousands(Convert.ToInt64(c.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (c.Value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            xw.WriteElementString("v", Convert.ToString(c.Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            xw.WriteAttributeString("t", "inlineStr");
            xw.WriteStartElement("is"); xw.WriteStartElement("t");
            var text = c.Value switch { null => "", DateTime d => d.ToString("dd.MM.yyyy"), _ => Convert.ToString(c.Value) ?? "" };
            if (text.StartsWith(' ') || text.EndsWith(' ')) xw.WriteAttributeString("xml", "space", null, "preserve");
            xw.WriteString(text); xw.WriteEndElement(); xw.WriteEndElement();
        }
        xw.WriteEndElement();
    }

    private static string ColumnName(int col)
    {
        var name = "";
        while (col > 0) { col--; name = (char)('A' + col % 26) + name; col /= 26; }
        return name;
    }

    private static XmlWriter CreateWriter(ZipArchive zip, string path)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        return XmlWriter.Create(entry.Open(), new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, CloseOutput = true });
    }

    private sealed class SheetSpec
    {
        public string Name { get; }
        public string[] Headers { get; }
        public List<Cell[]> Rows { get; } = new();
        public SheetSpec(string name, string[] headers) { Name = name; Headers = headers; }
        public void Add(params object?[] values) => Rows.Add(values.Select(v => v as Cell ?? new Cell(v)).ToArray());
        public double[] GetWidths()
        {
            var widths = Headers.Select(h => Math.Min(45, Math.Max(10, h.Length + 2))).Select(x => (double)x).ToArray();
            foreach (var row in Rows.Take(500))
                for (var i = 0; i < Math.Min(row.Length, widths.Length); i++)
                {
                    var len = row[i].DisplayLength;
                    widths[i] = Math.Min(45, Math.Max(widths[i], len + 2));
                }
            return widths;
        }
    }

    private sealed class Cell
    {
        public object? Value { get; }
        public bool IsMoney { get; init; }
        public bool Header { get; init; }
        public int DisplayLength => IsMoney ? global::FinanceTracker.Money.Format(Convert.ToInt64(Value)).Length : (Value switch { DateTime => 10, null => 0, _ => Convert.ToString(Value)?.Length ?? 0 });
        public Cell(object? value) => Value = value;
        public static Cell Money(long cents) => new(cents) { IsMoney = true };
    }
}
