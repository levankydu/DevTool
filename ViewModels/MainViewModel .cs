using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathwayDevTool.Models;
using PathwayDevTool.Services;
using PathwayDevTool.Views;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Data.SqlClient;

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

        public ICommand ShowConsoleCommand { get; }

        private readonly CollectionViewSource _runningServicesSource;
        private readonly CollectionViewSource _stoppedServicesSource;
        private ConsoleWindow? _consoleWindow;

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

            ShowConsoleCommand = new RelayCommand<Microservice>(ShowConsole);

        }
        #endregion

        #region Show Console
        private void ShowConsole(Microservice? service)
        {
            if (service == null) return;

            service.IsConsoleVisible = !service.IsConsoleVisible;

            if (service.IsConsoleVisible && service.ProcessId > 0)
            {
                _consoleWindow?.Close();

                _consoleWindow = new ConsoleWindow(service);
                _consoleWindow.Show();
            }
            else if (!service.IsConsoleVisible)
            {
                if (_consoleWindow != null)
                {
                    _consoleWindow.Hide();
                    _consoleWindow = null;
                }
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
                        service.PropertyChanged += (s, e) =>
                        {
                            if (s is Microservice svc && e.PropertyName == nameof(Microservice.IsConsoleVisible))
                            {
                                if (!svc.IsConsoleVisible && _consoleWindow != null)
                                {
                                    _consoleWindow.Hide();
                                    _consoleWindow = null;
                                }
                            }

                        };
                        Services.Add(service);
                    }
                }

                //await CheckRunningServicesAsync();

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

        private async Task CheckRunningServicesAsync()
        {
            foreach (var service in Services)
            {
                var port = GetPortFromLaunchSettings(service.ProjectPath);

                if (await IsPortOpen("localhost", port))
                {
                    Debug.WriteLine($"[{service.Name}] Detected running on port {port}");

                    var process = FindProcessByPort(port, service.Name);
                    if (process != null)
                    {
                        ReadEventLog(service);

                        AttachProcessExitHandler(process, service);
                        service.IsRunning = true;
                        service.ProcessId = process.Id;
                        service.Process = process;

                        Debug.WriteLine($"[{service.Name}] Set as running - PID: {process.Id}");
                    }
                }
            }
        }
        private static void ReadEventLog(Microservice service)
        {
            try
            {
                var eventLog = new System.Diagnostics.EventLog("Application");
                var entries = eventLog.Entries
                    .Cast<System.Diagnostics.EventLogEntry>()
                    .Where(e => e.Source.Contains(service.Name) || e.Message.Contains(service.Name))
                    .OrderByDescending(e => e.TimeGenerated)
                    .Take(20)
                    .ToList();

                var logText = new StringBuilder();
                logText.AppendLine($"[Event Log - {DateTime.Now:HH:mm:ss}]");

                foreach (var entry in entries)
                {
                    logText.AppendLine($"[{entry.TimeGenerated:HH:mm:ss}] {entry.Message}");
                }

                service.ConsoleOutput = logText.ToString();
            }
            catch
            {
                service.ConsoleOutput = "[Unable to read event log]";
            }
        }

        private static Process? FindProcessByPort(int port, string serviceName)
        {
            try
            {
                // Get all processes listening on port
                var processes = Process.GetProcesses();

                foreach (var process in processes)
                {
                    try
                    {
                        // Simple check - có thể improve bằng netstat command
                        if (process.ProcessName.Contains("dotnet") ||
                            process.ProcessName.Contains(serviceName))
                        {
                            // Verify port is actually listening
                            var connections = System.Net.NetworkInformation.IPGlobalProperties
                                .GetIPGlobalProperties()
                                .GetActiveTcpListeners();

                            if (connections.Any(c => c.Port == port))
                            {
                                return process;
                            }
                        }
                    }
                    catch { }
                }

                return null;
            }
            catch
            {
                return null;
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
                await RunServiceAsync(svc, true);

            this.RunningServicesView.Refresh();
            this.StoppedServicesView.Refresh();
        }

        public async Task RunServiceAsync(Microservice svc, bool runInSingleThread = false)
        {
            if (runInSingleThread)
            {
                try
                {
                    await CheckSqlServerHealth(svc);
                }
                catch (Exception)
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to connect to SQL services for '{svc.Name}'.",
                        "Service Start Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }
            }

            if (svc is null || svc.IsRunning)
                return;

            svc.IsStarting = true;

            Process? process = null;

            try
            {
                var port = GetPortFromLaunchSettings(svc.ProjectPath);
                if (await IsPortOpen("localhost", port))
                {
                    throw new Exception($"Service '{svc.Name}' cannot start. Port {port} is already in use.");
                }
                process = StartDotnetProcess(svc.ProjectPath, port);
                if (process == null)
                    throw new Exception("Failed to start dotnet process.");

                // Start reading output async
                var outputSb = new StringBuilder();
                var outputTask = ReadProcessOutputAsync(process, outputSb, svc);

                AttachProcessExitHandler(process, svc);

                var isReady = await WaitForPort(process, svc.Name, port);

                if (!isReady)
                    throw new Exception($"Service '{svc.Name}' failed to start.");

                svc.IsRunning = true;
                svc.Process = process;
                svc.ProcessId = process.Id;
                _ = outputTask;
            }
            catch (Exception ex)
            {
                SafeStopProcess(process);
                System.Windows.MessageBox.Show(
                    $"{ex.Message}",
                    "Service Start Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
            finally
            {
                svc.IsStarting = false;
            }
        }

        private static async Task ReadProcessOutputAsync(Process process, StringBuilder outputSb, Microservice svc)
        {
            var stdOutTask = Task.Run(async () =>
            {
                try
                {
                    using var reader = process.StandardOutput;
                    var buffer = new char[1024];
                    int charsRead;

                    while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        var output = new string(buffer, 0, charsRead);
                        outputSb.Append($"[{DateTime.Now:HH:mm:ss}] {output}");
                        svc.ConsoleOutput = outputSb.ToString();

                        await Task.Delay(50);
                    }
                }
                catch { }
            });

            var stdErrTask = Task.Run(async () =>
            {
                try
                {
                    using var reader = process.StandardError;
                    var buffer = new char[1024];
                    int charsRead;

                    while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        var output = new string(buffer, 0, charsRead);
                        outputSb.Append($"[{DateTime.Now:HH:mm:ss}] [ERROR] {output}");
                        svc.ConsoleOutput = outputSb.ToString();

                        await Task.Delay(50);
                    }
                }
                catch { }
            });

            await Task.WhenAll(stdOutTask, stdErrTask);
        }

        private static Process? StartDotnetProcess(string? projectPath, int port)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return null;

            var projectDir = Path.GetDirectoryName(projectPath)!;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var buildDir = Path.Combine(projectDir, "bin", "Debug");

            if (!BuildProject(projectPath, buildDir))
                return null;

            return StartProcessWithBinary(projectDir, buildDir, projectName, port);
        }

        private static bool BuildProject(string projectPath, string buildPath)
        {
            var buildInfo = new ProcessStartInfo("dotnet", $"build \"{projectPath}\" -c Debug")
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var buildProcess = Process.Start(buildInfo);
            if (buildProcess is null)
                return false;

            buildProcess.WaitForExit();
            UpdateConnectionStrings(buildPath);
            return buildProcess.ExitCode == 0;
        }

        private static Process? StartProcessWithBinary(string projectDir, string buildDir, string projectName, int port)
        {
            var exePath = Directory.EnumerateFiles(buildDir, $"{projectName}.exe", SearchOption.AllDirectories).FirstOrDefault();
            var dllPath = Directory.EnumerateFiles(buildDir, $"{projectName}.dll", SearchOption.AllDirectories).FirstOrDefault();

            var psi = new ProcessStartInfo
            {
                WorkingDirectory = projectDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                EnvironmentVariables =
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["ASPNETCORE_URLS"] = $"https://localhost:{port}"
                }
            };

            if (!string.IsNullOrEmpty(exePath))
            {
                psi.FileName = exePath;
            }
            else if (!string.IsNullOrEmpty(dllPath))
            {
                psi.FileName = "dotnet";
                psi.Arguments = $"\"{dllPath}\"";
            }
            else
            {
                return null;
            }

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
                var launchSettingsPath = Path.Combine(projectDir!, "Properties", "launchSettings.json");

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

        private static async Task<bool> WaitForPort(Process process, string serviceName, int port, int timeoutSeconds = 60)
        {
            var startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
            {
                if (process.HasExited)
                {
                    Debug.WriteLine($"[{serviceName}] Process crashed (Exit code: {process.ExitCode})");
                    return false;
                }

                if (await IsPortOpen("localhost", port))
                {
                    Debug.WriteLine($"[{serviceName}] Port {port} is now open!");
                    return true;
                }

                await Task.Delay(500);
            }

            Debug.WriteLine($"[{serviceName}] Timeout waiting for port {port}");
            return false;
        }

        private static async Task<bool> IsPortOpen(string host, int port, int timeoutMs = 2000)
        {
            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(timeoutMs);

                await client.ConnectAsync(host, port, cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Timeout connecting to {host}:{port}");
                return false;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Connection refused: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
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
            svc.IsConsoleVisible = false;
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
            try
            {
                await CheckSqlServerHealth(null);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to connect to SQL services.",
                    "Service Start Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            ;

            var necessaryServices = new List<Microservice>();
            var otherServices = new List<Microservice>();
            var webServices = new List<Microservice>();
            var communicationServices = new List<Microservice>();
            foreach (var service in Services)
            {
                if (service.IsRunning) continue;
                if (service.Name == null) continue;
                service.IsStarting = true;

                if (service.Name.Contains("OcelotApi", StringComparison.OrdinalIgnoreCase) ||
                    service.Name.Contains("Location", StringComparison.OrdinalIgnoreCase) ||
                    service.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase) ||
                    service.Name.Contains("Notification", StringComparison.OrdinalIgnoreCase))
                {
                    necessaryServices.Add(service);
                }
                else if (service.Name.Contains("Web", StringComparison.OrdinalIgnoreCase))
                {
                    webServices.Add(service);
                }
                else if (service.Name.Contains("Chat", StringComparison.OrdinalIgnoreCase) ||
                    service.Name.Contains("Messaging", StringComparison.OrdinalIgnoreCase))
                {
                    communicationServices.Add(service);
                }
                else
                {
                    otherServices.Add(service);
                }
            }
            if (necessaryServices.Count < 4)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to start all services.",
                    "Service Start Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                SetAllServiceStatus([.. necessaryServices.Union(otherServices)], false);
                return;
            };
            foreach (var service in necessaryServices.Union(webServices))
            {
                await RunServiceAsync(service);
                await Task.Delay(2000);
            }
            foreach (var service in otherServices)
            {
                await RunServiceAsync(service);
                await Task.Delay(2000);
            }
            var communicationTasks = communicationServices.Select(service => RunServiceAsync(service)).ToList();
            await Task.WhenAll(communicationTasks);
        }
        private static void SetAllServiceStatus(List<Microservice> services, bool IsStarting)
        {
            foreach (var service in services)
            {
                service.IsStarting = IsStarting;
            }
        }

        private static void UpdateConnectionStrings(string buildBasePath)
        {
            try
            {
                var appsettingsFiles = Directory.GetFiles(
                buildBasePath,
                "appsettings.Development.json",
                SearchOption.AllDirectories);

                foreach (var filePath in appsettingsFiles)
                {

                    var content = File.ReadAllText(filePath);
                    var jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(content);

                    // Check DefaultConnection exist
                    if (jsonObj["ConnectionStrings"]?["DefaultConnection"] == null)
                    {
                        Console.WriteLine($"Warning: DefaultConnection not found in {filePath}");
                        return;
                    }
                    content = Regex.Replace(content, @";?User ID=.*?;", ";");
                    content = Regex.Replace(content, @";?Password=.*?;", ";");
                    if (!content.Contains("Integrated Security"))
                    {
                        content = Regex.Replace(
                            content,
                            @"""(Server=.*?)""",
                            "\"$1;Integrated Security=true\""
                        );
                    }
                    else
                    {
                        content = Regex.Replace(content, @"Integrated Security=\w+", "Integrated Security=true");
                    }
                    if (content.Contains("Max Pool Size"))
                    {
                        content = Regex.Replace(content, @"Max Pool Size=\d+", "Max Pool Size=5");
                    }
                    else
                    {
                        content = Regex.Replace(
                            content,
                            @"""(Server=.*?)""",
                            "\"$1;Max Pool Size=5\""
                        );
                    }

                    File.WriteAllText(filePath, content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Error: {ex.Message}");
            }

        }
        private async Task<bool> CheckSqlServerHealth(Microservice? svc)
        {
            if (svc == default)
            {
                var identityService = Services.FirstOrDefault(s => s.Name != null && s.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase));
                if (identityService == default)
                {
                    throw new Exception("Identity service not found.");
                }
                svc = identityService;
                await CheckSqlServerHealth(svc);
            }
            ;

            var projectDir = Path.GetDirectoryName(svc.ProjectPath)!;
            var projectName = Path.GetFileNameWithoutExtension(svc.ProjectPath);
            var buildDir = Path.Combine(projectDir, "bin", "Debug");
            UpdateConnectionStrings(buildDir);

            var appsettingsFiles = Directory.GetFiles(
            buildDir,
            "appsettings.Development.json",
            SearchOption.AllDirectories);
            if (appsettingsFiles.Length == 0)
            {
                Debug.WriteLine("appsettings.Development.json not found");
                throw new Exception("appsettings.Development.json not found");
            }

            var appsettingsPath = appsettingsFiles[0];
            var json = File.ReadAllText(appsettingsPath);

            var jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);

            string connectionString = jsonObj["ConnectionStrings"]["DefaultConnection"];

            if (string.IsNullOrEmpty(connectionString))
            {
                Debug.WriteLine("Connection string not found");
                return false;
            }

            if (string.IsNullOrEmpty(connectionString))
            {
                Debug.WriteLine("Connection string not found in appsettings");
                throw new Exception("Connection string not found in appsettings");
            }

            using var connection = new System.Data.SqlClient.SqlConnection(connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1";
            var result = await cmd.ExecuteScalarAsync();
            if (result == null)
            {
                Debug.WriteLine("SQL connection test failed");
                throw new Exception("SQL connection test failed");
            }
            return result != null;
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
