using TaskLayer;

namespace MetaMorpheus.Maui.ViewModels;

public class ProteinDatabaseViewModel
{
    public ProteinDatabaseViewModel(string path, bool isContaminant)
    {
        Path = path;
        IsContaminant = isContaminant;
    }

    public string Path { get; }

    public bool IsContaminant { get; }

    public string DisplayAnnotation => IsContaminant ? "  (contaminant)" : string.Empty;

    public DbForTask ToDbForTask() => new(Path, IsContaminant);
}
