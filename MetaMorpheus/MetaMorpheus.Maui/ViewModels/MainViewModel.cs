using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using MetaMorpheus.Maui.Commands;
using MetaMorpheus.Maui.Services;
using Nett;
using TaskLayer;
using EngineLayer;

namespace MetaMorpheus.Maui.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly TaskOrchestrator _taskOrchestrator;
    private readonly IFilePickerService _filePickerService;
    private readonly AsyncCommand _addSpectraCommand;
    private readonly AsyncCommand _addProteinDatabaseCommand;
    private readonly AsyncCommand _addTaskCommand;
    private readonly AsyncCommand _runCommand;
    private readonly Command<string> _removeSpectraCommand;
    private readonly Command<ProteinDatabaseViewModel> _removeProteinDatabaseCommand;
    private readonly Command<TaskItemViewModel> _removeTaskCommand;
    private readonly Command _clearLogCommand;

    private string _title = "MetaMorpheus";
    private string _subtitle = "Cross-platform preview";
    private string _outputFolder = string.Empty;
    private string _outputFolderDescription = string.Empty;
    private string _statusMessage = string.Empty;
    private double _progress;
    private bool _isBusy;

    public MainViewModel(TaskOrchestrator taskOrchestrator, IFilePickerService filePickerService)
    {
        _taskOrchestrator = taskOrchestrator;
        _filePickerService = filePickerService;

        SpectraFiles.CollectionChanged += OnCollectionChanged;
        ProteinDatabases.CollectionChanged += OnCollectionChanged;
        Tasks.CollectionChanged += OnCollectionChanged;

        InitializeVersionInformation();

        OutputFolder = GetDefaultOutputFolder();
        OutputFolderDescription = "Edit the path to choose where MetaMorpheus writes results.";

        _addSpectraCommand = new AsyncCommand(AddSpectraAsync, () => !IsBusy);
        AddSpectraCommand = _addSpectraCommand;

        _addProteinDatabaseCommand = new AsyncCommand(AddProteinDatabasesAsync, () => !IsBusy);
        AddProteinDatabaseCommand = _addProteinDatabaseCommand;

        _addTaskCommand = new AsyncCommand(AddTaskAsync, () => !IsBusy);
        AddTaskCommand = _addTaskCommand;

        _runCommand = new AsyncCommand(RunAsync, () => CanRun);
        RunCommand = _runCommand;

        _removeSpectraCommand = new Command<string>(RemoveSpectra, _ => !IsBusy);
        RemoveSpectraCommand = _removeSpectraCommand;

        _removeProteinDatabaseCommand = new Command<ProteinDatabaseViewModel>(RemoveProteinDatabase, _ => !IsBusy);
        RemoveProteinDatabaseCommand = _removeProteinDatabaseCommand;

        _removeTaskCommand = new Command<TaskItemViewModel>(RemoveTask, _ => !IsBusy);
        RemoveTaskCommand = _removeTaskCommand;

        _clearLogCommand = new Command(ClearLog);
        ClearLogCommand = _clearLogCommand;

        _taskOrchestrator.LogReceived += (_, message) => AddLog(message);
        _taskOrchestrator.StatusUpdated += (_, message) => StatusMessage = message;
        _taskOrchestrator.ProgressUpdated += (_, value) => Progress = value;
        _taskOrchestrator.RunStarted += (_, __) => OnRunStarted();
        _taskOrchestrator.RunCompleted += (_, result) => OnRunCompleted(result);
    }

    public ObservableCollection<string> SpectraFiles { get; } = new();

    public ObservableCollection<ProteinDatabaseViewModel> ProteinDatabases { get; } = new();

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = new();

    public ObservableCollection<string> LogEntries { get; } = new();

    public ICommand AddSpectraCommand { get; }

    public ICommand AddProteinDatabaseCommand { get; }

    public ICommand AddTaskCommand { get; }

    public ICommand RunCommand { get; }

    public ICommand RemoveSpectraCommand { get; }

    public ICommand RemoveProteinDatabaseCommand { get; }

    public ICommand RemoveTaskCommand { get; }

    public ICommand ClearLogCommand { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetProperty(ref _outputFolder, value))
            {
                EnsureOutputFolderExists(value);
            }
        }
    }

    public string OutputFolderDescription
    {
        get => _outputFolderDescription;
        private set => SetProperty(ref _outputFolderDescription, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                UpdateCommandStates();
                OnPropertyChanged(nameof(CanRun));
            }
        }
    }

    public bool CanRun => !IsBusy && Tasks.Any() && SpectraFiles.Any();

    private void InitializeVersionInformation()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(GlobalVariables.MetaMorpheusVersion))
            {
                GlobalVariables.SetUpGlobalVariables();
            }

            Title = $"MetaMorpheus {GlobalVariables.MetaMorpheusVersion}";
            Subtitle = "Cross-platform preview build";
        }
        catch (Exception ex)
        {
            AddLog($"Unable to load MetaMorpheus version: {ex.Message}");
        }
    }

    private static string GetDefaultOutputFolder()
    {
        var baseFolder = Path.Combine(FileSystem.AppDataDirectory, "MetaMorpheus", "Results");
        return baseFolder;
    }

    private void EnsureOutputFolderExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
            AddLog($"Unable to create output directory at {path}.");
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanRun));
        _runCommand.RaiseCanExecuteChanged();
    }

    private async Task AddSpectraAsync()
    {
        var files = await _filePickerService.PickSpectraAsync().ConfigureAwait(false);
        var selections = files?.ToList();
        if (selections == null || selections.Count == 0)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            foreach (var path in selections)
            {
                if (!SpectraFiles.Contains(path))
                {
                    SpectraFiles.Add(path);
                }
            }
        });

        AddLog($"Added {selections.Count} spectra file(s).");
    }

    private async Task AddProteinDatabasesAsync()
    {
        var files = await _filePickerService.PickProteinDatabasesAsync().ConfigureAwait(false);
        var selections = files?.ToList();
        if (selections == null || selections.Count == 0)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            foreach (var path in selections)
            {
                if (ProteinDatabases.Any(db => db.Path == path))
                {
                    continue;
                }

                ProteinDatabases.Add(new ProteinDatabaseViewModel(path, IsContaminant(path)));
            }
        });

        AddLog($"Added {selections.Count} protein database file(s).");
    }

    private async Task AddTaskAsync()
    {
        var taskPath = await _filePickerService.PickTaskConfigurationAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(taskPath))
        {
            return;
        }

        if (Tasks.Any(t => t.SourcePath == taskPath))
        {
            AddLog($"Task already loaded: {taskPath}");
            return;
        }

        try
        {
            var taskItem = await LoadTaskFromFileAsync(taskPath).ConfigureAwait(false);
            if (taskItem == null)
            {
                AddLog($"Unsupported task configuration: {taskPath}");
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() => Tasks.Add(taskItem));
            AddLog($"Loaded task '{taskItem.DisplayName}'.");
        }
        catch (Exception ex)
        {
            AddLog($"Failed to read task configuration: {ex.Message}");
        }
    }

    private async Task<TaskItemViewModel?> LoadTaskFromFileAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            var toml = Toml.ReadFile(filePath, MetaMorpheusTask.tomlConfig);
            var taskType = toml.Get<string>("TaskType");

            MetaMorpheusTask? task = taskType switch
            {
                "Search" => Toml.ReadFile<SearchTask>(filePath, MetaMorpheusTask.tomlConfig),
                "Calibrate" => Toml.ReadFile<CalibrationTask>(filePath, MetaMorpheusTask.tomlConfig),
                "Gptmd" => Toml.ReadFile<GptmdTask>(filePath, MetaMorpheusTask.tomlConfig),
                "XLSearch" => Toml.ReadFile<XLSearchTask>(filePath, MetaMorpheusTask.tomlConfig),
                "GlycoSearch" => Toml.ReadFile<GlycoSearchTask>(filePath, MetaMorpheusTask.tomlConfig),
                "Average" => Toml.ReadFile<SpectralAveragingTask>(filePath, MetaMorpheusTask.tomlConfig),
                _ => null
            };

            if (task == null)
            {
                return null;
            }

            var displayName = Path.GetFileNameWithoutExtension(filePath);
            return new TaskItemViewModel(displayName, filePath, task);
        }).ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!SpectraFiles.Any())
        {
            AddLog("Select at least one spectra file before running.");
            return;
        }

        if (!Tasks.Any())
        {
            AddLog("Add at least one MetaMorpheus task before running.");
            return;
        }

        var taskDefinitions = Tasks.Select((task, index) =>
            new TaskRunRequest($"Task{index + 1}_{task.TaskType}", task.Task)).ToList();

        var spectra = SpectraFiles.ToList();
        var databases = ProteinDatabases.Select(db => db.ToDbForTask()).ToList();

        try
        {
            await _taskOrchestrator.RunAsync(taskDefinitions, spectra, databases, OutputFolder).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AddLog($"Run failed: {ex.Message}");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsBusy = false;
                StatusMessage = "Run failed";
                Progress = 0;
            });
        }
    }

    private void RemoveSpectra(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SpectraFiles.Remove(path);
    }

    private void RemoveProteinDatabase(ProteinDatabaseViewModel? database)
    {
        if (database == null)
        {
            return;
        }

        ProteinDatabases.Remove(database);
    }

    private void RemoveTask(TaskItemViewModel? task)
    {
        if (task == null)
        {
            return;
        }

        Tasks.Remove(task);
    }

    private void ClearLog()
    {
        LogEntries.Clear();
    }

    private void OnRunStarted()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsBusy = true;
            StatusMessage = "Running MetaMorpheus tasks...";
            Progress = 0;
        });
    }

    private void OnRunCompleted(string? outputFolder)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsBusy = false;
            Progress = 1;
            StatusMessage = string.IsNullOrWhiteSpace(outputFolder)
                ? "Run complete"
                : $"Run complete. Results: {outputFolder}";
        });
    }

    private void UpdateCommandStates()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _addSpectraCommand.RaiseCanExecuteChanged();
            _addProteinDatabaseCommand.RaiseCanExecuteChanged();
            _addTaskCommand.RaiseCanExecuteChanged();
            _runCommand.RaiseCanExecuteChanged();
            _removeSpectraCommand.ChangeCanExecute();
            _removeProteinDatabaseCommand.ChangeCanExecute();
            _removeTaskCommand.ChangeCanExecute();
        });
    }

    private void AddLog(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = $"{DateTime.Now:HH:mm:ss} {message}";
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LogEntries.Add(entry);
            while (LogEntries.Count > 500)
            {
                LogEntries.RemoveAt(0);
            }
        });
    }

    private static bool IsContaminant(string path)
    {
        var upper = Path.GetFileName(path).ToUpperInvariant();
        return upper.Contains("CONTAMINANT") || upper.Contains("CRAP");
    }
}
