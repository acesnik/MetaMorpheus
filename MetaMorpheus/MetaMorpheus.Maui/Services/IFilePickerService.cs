using System.Collections.Generic;
using System.Threading.Tasks;

namespace MetaMorpheus.Maui.Services;

public interface IFilePickerService
{
    Task<IEnumerable<string>> PickSpectraAsync();
    Task<IEnumerable<string>> PickProteinDatabasesAsync();
    Task<string?> PickTaskConfigurationAsync();
}
