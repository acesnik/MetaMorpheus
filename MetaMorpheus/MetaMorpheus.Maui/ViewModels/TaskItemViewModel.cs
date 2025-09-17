using System.IO;
using TaskLayer;

namespace MetaMorpheus.Maui.ViewModels;

public class TaskItemViewModel
{
    public TaskItemViewModel(string displayName, string sourcePath, MetaMorpheusTask task)
    {
        DisplayName = displayName;
        SourcePath = sourcePath;
        Task = task;
        TaskType = task.TaskType.ToString();
    }

    public string DisplayName { get; }

    public string SourcePath { get; }

    public string TaskType { get; }

    public MetaMorpheusTask Task { get; }

    public string ShortSourceName => Path.GetFileName(SourcePath);
}
