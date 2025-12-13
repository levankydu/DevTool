using PathwayDevTool.Models;
using PathwayDevTool.ViewModels;
using System.Windows.Data;

namespace PathwayDevTool.Views
{
    /// <summary>
    /// Interaction logic for MainView.xaml
    /// </summary>
    public partial class MainView : System.Windows.Controls.UserControl
    {
        public MainView()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
        private void RunningServices_Filter(object sender, FilterEventArgs e)
        {
            if (e.Item is Microservice svc)
            {
                e.Accepted = svc.IsRunning;
            }
        }

        private void StoppedServices_Filter(object sender, FilterEventArgs e)
        {
            if (e.Item is Microservice svc)
            {
                e.Accepted = !svc.IsRunning;
            }
        }
    }
}
