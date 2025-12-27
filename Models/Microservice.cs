using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;

namespace PathwayDevTool.Models
{
    public partial class Microservice : ObservableObject
    {

        [ObservableProperty]
        private string? name;

        [ObservableProperty]
        private string? projectPath;

        [ObservableProperty]
        private int processId = 0;

        [ObservableProperty]
        private bool isRunning = false;

        [ObservableProperty]
        public bool isStarting = false;

        public Process? Process { get; set; }

        [ObservableProperty]
        private string consoleOutput = "Waiting for output...";

        [ObservableProperty]
        private bool isConsoleVisible;
    }

}
