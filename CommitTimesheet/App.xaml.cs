using System.Windows;
using System.Windows.Threading;
using CommitTimesheet.Services;
using CommitTimesheet.ViewModels;
using CommitTimesheet.Views;

namespace CommitTimesheet;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var runner = new ProcessRunner();
        var dialogs = new DialogService();
        var viewModel = new MainViewModel(
            new SettingsStore(),
            new GitLogReader(runner),
            new WorkingStateReader(runner),
            new ClaudeSummarizer(runner),
            new TimesheetAssembler(),
            new TsvFormatter(),
            dialogs);

        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        viewModel.RefreshCommand.Execute(null);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        new DialogService().ShowError("Something went wrong", e.Exception.Message, e.Exception.ToString());
        e.Handled = true;
    }
}
