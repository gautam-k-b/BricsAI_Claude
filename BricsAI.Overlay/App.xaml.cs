using System.Windows;

namespace BricsAI.Overlay
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                System.IO.File.AppendAllText("debug_log.txt", "App.OnStartup called\n");
                base.OnStartup(e);
            }
            catch (System.Exception ex)
            {
                System.IO.File.WriteAllText("error_log.txt", ex.ToString());
            }
        }
    }
}
