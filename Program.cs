using System.Globalization;

namespace FinanceTracker;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ShowFatal(ex);
        };

        try
        {
            var db = new FinanceDatabase();
            db.Initialize();
            if (db.BackupOnStart)
            {
                try { db.CreateZipBackupBesideExecutable(); }
                catch (Exception backupEx)
                {
                    MessageBox.Show($"Не удалось создать автоматическую резервную копию базы данных.\n\n{backupEx.Message}",
                        "Резервная копия", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            Application.Run(new MainForm(db));
        }
        catch (Exception ex)
        {
            ShowFatal(ex);
        }
    }

    private static void ShowFatal(Exception ex)
    {
        MessageBox.Show(
            $"Произошла ошибка:\n\n{ex.Message}\n\nПодробности:\n{ex}",
            "Финансовый учёт",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
