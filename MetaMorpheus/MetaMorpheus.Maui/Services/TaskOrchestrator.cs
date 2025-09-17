using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EngineLayer;
using TaskLayer;

namespace MetaMorpheus.Maui.Services;

public record TaskRunRequest(string Name, MetaMorpheusTask Task);

public class TaskOrchestrator
{
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private bool _isRunning;
    private string? _lastOutputFolder;

    public TaskOrchestrator()
    {
        try
        {
            GlobalVariables.SetUpGlobalVariables();
        }
        catch
        {
            // The view-model will surface initialization failures through log messages.
        }
    }

    public event EventHandler? RunStarted;

    public event EventHandler<string?>? RunCompleted;

    public event EventHandler<string>? LogReceived;

    public event EventHandler<string>? StatusUpdated;

    public event EventHandler<double>? ProgressUpdated;

    public async Task RunAsync(IReadOnlyList<TaskRunRequest> tasks, IReadOnlyList<string> spectraFiles, IReadOnlyList<DbForTask> proteinDatabases, string outputFolder, CancellationToken cancellationToken = default)
    {
        if (tasks == null)
        {
            throw new ArgumentNullException(nameof(tasks));
        }

        if (spectraFiles == null)
        {
            throw new ArgumentNullException(nameof(spectraFiles));
        }

        if (proteinDatabases == null)
        {
            throw new ArgumentNullException(nameof(proteinDatabases));
        }

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            throw new ArgumentException("Output folder must be provided.", nameof(outputFolder));
        }

        if (tasks.Count == 0)
        {
            throw new InvalidOperationException("At least one MetaMorpheus task must be provided.");
        }

        if (spectraFiles.Count == 0)
        {
            throw new InvalidOperationException("At least one spectra file must be provided.");
        }

        await _runLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("A MetaMorpheus run is already in progress.");
            }

            _isRunning = true;
            _lastOutputFolder = null;
            OnProgressUpdated(0);
            OnRunStarted();
            OnLog($"Launching {tasks.Count} task(s) with {spectraFiles.Count} spectra file(s).");

            SubscribeHandlers();

            try
            {
                var runner = new EverythingRunnerEngine(tasks.Select(t => (t.Name, t.Task)).ToList(), spectraFiles.ToList(), proteinDatabases.ToList(), outputFolder);
                await Task.Run(() => runner.Run(), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                UnsubscribeHandlers();
                _isRunning = false;
                OnRunCompleted(_lastOutputFolder ?? outputFolder);
            }
        }
        finally
        {
            _runLock.Release();
        }
    }

    private void SubscribeHandlers()
    {
        EverythingRunnerEngine.StartingAllTasksEngineHandler += OnStartingAllTasks;
        EverythingRunnerEngine.FinishedAllTasksEngineHandler += OnFinishedAllTasks;
        EverythingRunnerEngine.WarnHandler += OnWarn;
        EverythingRunnerEngine.FinishedWritingAllResultsFileHandler += OnFinishedWritingAllResults;

        MetaMorpheusTask.WarnHandler += OnWarn;
        MetaMorpheusTask.LogHandler += OnLog;
        MetaMorpheusTask.OutLabelStatusHandler += OnStatus;
        MetaMorpheusTask.OutProgressHandler += OnProgress;
        MetaMorpheusTask.StartingSingleTaskHander += OnStartingTask;
        MetaMorpheusTask.FinishedSingleTaskHandler += OnFinishedTask;
        MetaMorpheusTask.FinishedWritingFileHandler += OnFinishedWritingFile;

        MetaMorpheusEngine.WarnHandler += OnWarn;
        MetaMorpheusEngine.OutLabelStatusHandler += OnStatus;
        MetaMorpheusEngine.OutProgressHandler += OnProgress;
    }

    private void UnsubscribeHandlers()
    {
        EverythingRunnerEngine.StartingAllTasksEngineHandler -= OnStartingAllTasks;
        EverythingRunnerEngine.FinishedAllTasksEngineHandler -= OnFinishedAllTasks;
        EverythingRunnerEngine.WarnHandler -= OnWarn;
        EverythingRunnerEngine.FinishedWritingAllResultsFileHandler -= OnFinishedWritingAllResults;

        MetaMorpheusTask.WarnHandler -= OnWarn;
        MetaMorpheusTask.LogHandler -= OnLog;
        MetaMorpheusTask.OutLabelStatusHandler -= OnStatus;
        MetaMorpheusTask.OutProgressHandler -= OnProgress;
        MetaMorpheusTask.StartingSingleTaskHander -= OnStartingTask;
        MetaMorpheusTask.FinishedSingleTaskHandler -= OnFinishedTask;
        MetaMorpheusTask.FinishedWritingFileHandler -= OnFinishedWritingFile;

        MetaMorpheusEngine.WarnHandler -= OnWarn;
        MetaMorpheusEngine.OutLabelStatusHandler -= OnStatus;
        MetaMorpheusEngine.OutProgressHandler -= OnProgress;
    }

    private void OnStartingAllTasks(object? sender, EventArgs e) => OnStatus("Preparing tasks...");

    private void OnFinishedAllTasks(object? sender, StringEventArgs e)
    {
        _lastOutputFolder = e.S;
        OnStatus("Finished running MetaMorpheus tasks.");
    }

    private void OnFinishedWritingAllResults(object? sender, StringEventArgs e)
    {
        OnLog($"All task results written to {e.S}");
    }

    private void OnWarn(object? sender, StringEventArgs e) => OnLog($"⚠️ {e.S}");

    private void OnLog(object? sender, StringEventArgs e) => OnLog(e.S);

    private void OnLog(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            LogReceived?.Invoke(this, message);
        }
    }

    private void OnStatus(object? sender, StringEventArgs e) => OnStatus(e.S);

    private void OnProgress(object? sender, ProgressEventArgs e)
    {
        var normalized = Math.Clamp(e.NewProgress / 100d, 0d, 1d);
        OnProgressUpdated(normalized);
        if (!string.IsNullOrWhiteSpace(e.V))
        {
            OnStatus(e.V);
        }
    }

    private void OnFinishedWritingFile(object? sender, SingleFileEventArgs e)
    {
        OnLog($"Finished writing file: {e.WrittenFile}");
    }

    private void OnStartingTask(object? sender, SingleTaskEventArgs e)
    {
        OnLog($"Starting task: {e.DisplayName}");
    }

    private void OnFinishedTask(object? sender, SingleTaskEventArgs e)
    {
        OnLog($"Finished task: {e.DisplayName}");
    }

    private void OnRunStarted() => RunStarted?.Invoke(this, EventArgs.Empty);

    private void OnRunCompleted(string? outputFolder) => RunCompleted?.Invoke(this, outputFolder);

    private void OnStatus(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusUpdated?.Invoke(this, message);
        }
    }

    private void OnProgressUpdated(double progress) => ProgressUpdated?.Invoke(this, progress);
}
