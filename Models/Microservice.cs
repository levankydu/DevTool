using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;

namespace PathwayDevTool.Models
{
    public class Microservice: ObservableObject
    {
        public string? Name { get; set; }
        public string? ProjectPath { get; set; }

        private int port;
        public int ProcessId
        {
            get => port;
            set => SetProperty(ref port, value);
        }

        private bool isRunning;
        public bool IsRunning
        {
            get => isRunning;
            set => SetProperty(ref isRunning, value);
        }

        public Process? Process { get; set; }
    }

}
