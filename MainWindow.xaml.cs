using PathwayDevTool.ViewModels;
using PathwayDevTool.Views;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace PathwayDevTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            var menuView = new MenuView { DataContext = _viewModel };
            var mainView = new MainView { DataContext = _viewModel };
            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

            System.Windows.Controls.Grid.SetRow(menuView, 0);
            System.Windows.Controls.Grid.SetRow(mainView, 1);

            grid.Children.Add(menuView);
            grid.Children.Add(mainView);

            Content = grid;

            Closing += MainWindow_Closing;
            KeyDown += MainWindow_KeyDown;
        }

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.O && e.KeyboardDevice.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                _viewModel.LoadCommand.Execute(null);
            }
        }
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var runningCount = _viewModel.Services.Count(s => s.IsRunning);

            if (runningCount > 0)
            {
                var result = MessageBox.Show(
                    $"There are {runningCount} service(s) still running.\n\n" +
                    "Do you want to stop them before closing?",
                    "Confirm Exit",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (result == MessageBoxResult.OK)
                {
                    _viewModel.Cleanup();
                }
            }
            else
            {
                _viewModel.Cleanup();
            }
        }
    }
}