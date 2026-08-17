using System.Windows;
using System.Windows.Input;

namespace BricsAI.Overlay
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            try
            {
                System.IO.File.AppendAllText("debug_log.txt", "MainWindow constructor called\n");
                InitializeComponent();
                System.IO.File.AppendAllText("debug_log.txt", "MainWindow InitializeComponent finished\n");

                if (DataContext is ViewModels.MainViewModel vm)
                {
                    vm.Messages.CollectionChanged += (s, e) => 
                    {
                        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                        {
                            ChatScrollViewer.ScrollToBottom();
                        }
                    };
                }
            }
            catch (System.Exception ex)
            {
                System.IO.File.WriteAllText("constructor_error.txt", ex.ToString());
                throw;
            }
        }

        protected override void OnContentRendered(System.EventArgs e)
        {
            base.OnContentRendered(e);
            System.IO.File.AppendAllText("debug_log.txt", "MainWindow ContentRendered\n");
        }

        protected override void OnClosed(System.EventArgs e)
        {
            System.IO.File.AppendAllText("debug_log.txt", "MainWindow Closed\n");
            base.OnClosed(e);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (WindowState == WindowState.Normal)
                    WindowState = WindowState.Maximized;
                else
                    WindowState = WindowState.Normal;
            }
            else
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
        {
             if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // Allow the TextBox to handle the newline naturally
                    return;
                }

                var vm = (ViewModels.MainViewModel)DataContext;
                if (vm.SendCommand.CanExecute(null))
                {
                    vm.SendCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
