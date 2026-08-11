using System.Configuration;
using System.Data;
using System.Windows;
using iRacing_Spotter_Generator.Services;

namespace iRacing_Spotter_Generator
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settings = AppSettingsService.Load();
            LocalizationManager.SetLanguage(settings.Language);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
    }

}
