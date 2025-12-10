using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathwayDevTool.Models;
using PathwayDevTool.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;



namespace PathwayDevTool.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string? solutionPath;
        [ObservableProperty]
        private int port;
        public ObservableCollection<Microservice> Services { get; } = [];
        public ICommand RunCommand { get; }



        public MainViewModel()
        {
        }


        [RelayCommand]
        void Load()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select solution root folder (contains src)",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;
            SolutionPath = dialog.SelectedPath;
            LoadServices();
        }

        void LoadServices()
        {
            Services.Clear();
            var detector = new MicroserviceDetector();
            var detected = detector.Detect(SolutionPath);

            foreach (var svc in detected)
            {
                if (svc.Projects == null) continue;
                foreach (var service in svc.Projects)
                {
                    Services.Add(service);
                }
            }

        }
        [RelayCommand]
        void Toggle(Microservice svc)
        {
            if (svc.IsRunning)
                Stop(svc);
            else
                RunService(svc);
        }

        void RunService(Microservice svc)
        {
            if (svc.IsRunning)
                return;

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{svc.ProjectPath}\"",
                WorkingDirectory = Path.GetDirectoryName(svc.ProjectPath),
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var process = Process.Start(psi);
            if (process != null)
            {
                svc.Process = process;
                svc.ProcessId = process.Id; 
                svc.IsRunning = true;
            }
        }

        [RelayCommand]
        void Stop(Microservice svc)
        {
            if (!svc.IsRunning) return;

            try
            {
                svc.Process?.Kill(true);
            }
            catch { }

            svc.IsRunning = false;
            svc.ProcessId = 0;
            svc.Process = null;
        }



    }
}
