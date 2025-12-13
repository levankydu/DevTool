using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathwayDevTool.Models;
using PathwayDevTool.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;

namespace PathwayDevTool.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        #region Properties
        [ObservableProperty]
        private string? solutionPath;

        [ObservableProperty]
        private int port;

        [ObservableProperty]
        private bool isLoaded;

        [ObservableProperty]
        private string loadMenuText = "_Load Solution";

        [ObservableProperty]
        private bool saveRecentPaths = true;

        [ObservableProperty]
        private string? lastPath;

        [ObservableProperty]
        private int selectedTabIndex = 0;

        public ObservableCollection<Microservice> Services { get; } = [];

        public ICommand? RunCommand { get; }

        private readonly CollectionViewSource _runningServicesSource;
        private readonly CollectionViewSource _stoppedServicesSource;

        public ICollectionView RunningServicesView => _runningServicesSource.View;

        public ICollectionView StoppedServicesView => _stoppedServicesSource.View;

        #endregion

        #region Constructor
        public MainViewModel()
        {
            _runningServicesSource = new CollectionViewSource { Source = Services };
            _runningServicesSource.Filter += (s, e) =>
            {
                e.Accepted = e.Item is Microservice svc && svc.IsRunning;
            };

            _stoppedServicesSource = new CollectionViewSource { Source = Services };
            _stoppedServicesSource.Filter += (s, e) =>
            {
                e.Accepted = e.Item is Microservice svc && !svc.IsRunning;
            };

            Services.CollectionChanged += Services_CollectionChanged;
            LoadSettings();
            LoadLastPath();

            if (SaveRecentPaths && !string.IsNullOrEmpty(LastPath) && Directory.Exists(LastPath))
            {
                SolutionPath = LastPath;
                LoadServices();
                IsLoaded = true;
                LoadMenuText = "_Unload Solution";
            }
        }
        #endregion

        #region Service Property Changed
        private void Service_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Microservice.IsRunning))
            {
                _runningServicesSource.View.Refresh();
                _stoppedServicesSource.View.Refresh();
                StartAllCommand.NotifyCanExecuteChanged();
                StopAllCommand.NotifyCanExecuteChanged();
            }
        }
        partial void OnIsLoadedChanged(bool value)
        {
            StartAllCommand.NotifyCanExecuteChanged();
        }


        private void Services_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (Microservice svc in e.NewItems)
                {
                    svc.PropertyChanged += Service_PropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (Microservice svc in e.OldItems)
                {
                    svc.PropertyChanged -= Service_PropertyChanged;
                }
            }
        }
        #endregion

        #region Load/Unload Services
        [RelayCommand]
        void Load()
        {
            if (IsLoaded)
            {
                UnloadServices();
            }
            else
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
            StartAllCommand.NotifyCanExecuteChanged();
            StopAllCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        void UnloadServices()
        {
            var runningServices = Services.Where(s => s.IsRunning).ToList();

            if (runningServices.Any())
            {
                var result = System.Windows.MessageBox.Show(
                    $"There are {runningServices.Count} service(s) still running.\n\n" +
                    "Do you want to stop them and unload?",
                    "Confirm Unload",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Cancel)
                {
                    return;
                }

                foreach (var service in runningServices)
                {
                    Stop(service);
                }
            }

            Services.Clear();
            SolutionPath = null;
            IsLoaded = false;
            LoadMenuText = "_Load Solution";
        }

        void LoadServices()
        {
            try
            {
                Services.Clear();
                var detector = new MicroserviceDetector();
                var detected = detector.Detect(SolutionPath);

                foreach (var svc in detected)
                {
                    if (svc.Projects == null) continue;

                    foreach (var service in svc.Projects)
                    {
                        service.IsRunning = false;
                        service.ProcessId = 0;
                        service.Process = null;
                        Services.Add(service);
                    }
                }
                IsLoaded = true;
                LoadMenuText = "_Unload Solution";
                if (SaveRecentPaths)
                {
                    LastPath = SolutionPath;
                    SaveLastPath();
                }
            }
            catch (DirectoryNotFoundException ex)
            {
                System.Windows.MessageBox.Show(
                    $"Directory not found!\n\n" +
                    $"The solution does not exist at:\n{SolutionPath}\n\n" +
                    $"Please select a valid solution folder.",
                    "Error Loading Services",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                IsLoaded = false;
                LoadMenuText = "_Load Solution";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"An error occurred while loading services:\n\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                IsLoaded = false;
                LoadMenuText = "_Load Solution";
            }

        }

        #endregion

        #region Run/Stop Services
        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task ToggleAsync(Microservice svc)
        {
            if (svc.IsRunning)
                Stop(svc);
            else
                await RunServiceAsync(svc);

            this.RunningServicesView.Refresh();
            this.StoppedServicesView.Refresh();
        }

        public async Task RunServiceAsync(Microservice svc)
        {
            if (svc is null || svc.IsRunning || svc.IsStarting)
                return;

            svc.IsStarting = true;

            Process? process = null;

            try
            {
                var port = GetPortFromLaunchSettings(svc.ProjectPath);

                process = StartDotnetProcess(svc.ProjectPath);
                if (process == null)
                    throw new InvalidOperationException("Failed to start dotnet process.");

                AttachProcessExitHandler(process, svc);

                var isReady = await WaitForPort(process, svc.Name, port);

                if (!isReady)
                    throw new TimeoutException($"Service '{svc.Name}' failed to start.");

                svc.IsRunning = true;
                svc.Process = process;
                svc.ProcessId = process.Id;

                Debug.WriteLine($"✓ [{svc.Name}] Service started successfully!");

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    SelectedTabIndex = 1;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"✗ [{svc.Name}] Start failed: {ex.Message}");

                SafeStopProcess(process);

                System.Windows.MessageBox.Show(
                    $"Service '{svc.Name}' failed to start.\n\n{ex.Message}",
                    "Service Start Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
            finally
            {
                svc.IsStarting = false;
            }
        }

        private static Process? StartDotnetProcess(string? projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return null;

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{projectPath}\"",
                WorkingDirectory = Path.GetDirectoryName(projectPath),
                UseShellExecute = false,
                CreateNoWindow = true
            };

            return Process.Start(psi);
        }

        private static void AttachProcessExitHandler(Process process, Microservice svc)
        {
            process.EnableRaisingEvents = true;

            process.Exited += (_, _) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Debug.WriteLine($"[{svc.Name}] Process exited");

                    svc.IsRunning = false;
                    svc.IsStarting = false;
                    svc.ProcessId = 0;
                    svc.Process = null;
                });
            };
        }

        private static void SafeStopProcess(Process? process)
        {
            try
            {
                if (process is { HasExited: false })
                    process.Kill(true);
            }
            catch
            {
                // ignore
            }
        }

        private static int GetPortFromLaunchSettings(string? projectPath)
        {
            try
            {
                if (string.IsNullOrEmpty(projectPath))
                    return 5000;

                var projectDir = Path.GetDirectoryName(projectPath);
                var launchSettingsPath = Path.Combine(projectDir, "Properties", "launchSettings.json");

                if (!File.Exists(launchSettingsPath))
                    return 5000;

                var json = File.ReadAllText(launchSettingsPath);

                var match = System.Text.RegularExpressions.Regex.Match(json, @"""sslPort""\s*:\s*(\d+)");

                if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                {
                    return port;
                }

                return 5000;
            }
            catch (Exception ex)
            {
                return 5000;
            }
        }

        private static async Task<bool> WaitForPort(Process process, string serviceName, int port, int timeoutSeconds = 120)
        {
            var startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
            {
                if (process.HasExited)
                {
                    Debug.WriteLine($"[{serviceName}] Process crashed (Exit code: {process.ExitCode})");
                    return false;
                }

                if (IsPortOpen("localhost", port))
                {
                    Debug.WriteLine($"[{serviceName}] Port {port} is now open!");
                    return true;
                }

                await Task.Delay(500);
            }

            Debug.WriteLine($"[{serviceName}] Timeout waiting for port {port}");
            return false;
        }

        private static bool IsPortOpen(string host, int port)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var result = client.BeginConnect(host, port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100));

                if (success)
                {
                    client.EndConnect(result);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
        [RelayCommand]
        static void Stop(Microservice svc)
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

        public void Cleanup()
        {
            var runningServices = Services.Where(s => s.IsRunning).ToList();

            foreach (var service in runningServices)
            {
                Stop(service);
            }
        }

        #endregion

        #region Menu Commands
        [RelayCommand]
        private static void Exit()
        {
            System.Windows.Application.Current.MainWindow?.Close();
        }
        [RelayCommand]
        private static void About()
        {
            System.Windows.MessageBox.Show(
                "Pathway Dev Tool v1.0\n\n" +
                "A tool for managing microservices during development.\n\n" +
                "© 2025",
                "About Pathway Dev Tool",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        [RelayCommand(CanExecute = nameof(CanStartAll))]
        private async Task StartAllAsync()
        {
            var stoppedServices = Services.Where(s => !s.IsRunning).ToList();
            foreach (var service in stoppedServices)
            {
                await RunServiceAsync(service);
            }
        }
        private bool CanStartAll()
        {
            return IsLoaded && Services.Any(s => !s.IsRunning);
        }
        [RelayCommand(CanExecute = nameof(CanStopAll))]
        private void StopAll()
        {
            var runningServices = Services.Where(s => s.IsRunning).ToList();
            foreach (var service in runningServices)
            {
                Stop(service);
            }
        }
        private bool CanStopAll()
        {
            return Services.Any(s => s.IsRunning);
        }
        private void SaveLastPath()
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PathwayDevTool");

                Directory.CreateDirectory(appDataPath);

                var filePath = Path.Combine(appDataPath, "last-path.txt");

                if (!string.IsNullOrEmpty(lastPath))
                {
                    File.WriteAllText(filePath, LastPath);
                }
                else
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving last path: {ex.Message}");
            }
        }
        private void LoadLastPath()
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PathwayDevTool");

                var filePath = Path.Combine(appDataPath, "last-path.txt");

                if (File.Exists(filePath))
                {
                    var path = File.ReadAllText(filePath);
                    if (Directory.Exists(path))
                    {
                        LastPath = path;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading last path: {ex.Message}");
            }
        }
        private void SaveSettings()
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PathwayDevTool");

                Directory.CreateDirectory(appDataPath);

                var settingsPath = Path.Combine(appDataPath, "settings.txt");
                File.WriteAllText(settingsPath, SaveRecentPaths.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
        private void LoadSettings()
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PathwayDevTool");

                var settingsPath = Path.Combine(appDataPath, "settings.txt");

                if (File.Exists(settingsPath))
                {
                    var value = File.ReadAllText(settingsPath);
                    SaveRecentPaths = bool.Parse(value);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings: {ex.Message}");
                SaveRecentPaths = true;
            }
        }
        partial void OnSaveRecentPathsChanged(bool value)
        {
            SaveSettings();

            if (!value)
            {
                LastPath = null;
                SaveLastPath();
            }
        }
        #endregion
    }
}
