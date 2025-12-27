using PathwayDevTool.Models;
using PathwayDevTool.ViewModels;
using System.Windows;

namespace PathwayDevTool.Views
{
    /// <summary>
    /// Interaction logic for ConsoleWindow.xaml
    /// </summary>
    public partial class ConsoleWindow : Window
    {
        public ConsoleWindow(Microservice service)
        {
            InitializeComponent();
            DataContext = service;
            this.Closing += (s, e) => e.Cancel = true; // Block close button

        }
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            ScrollViewer?.ScrollToEnd();
        }
    }
}
