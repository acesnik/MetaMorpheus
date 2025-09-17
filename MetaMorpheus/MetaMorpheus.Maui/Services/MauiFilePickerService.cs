using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace MetaMorpheus.Maui.Services;

public class MauiFilePickerService : IFilePickerService
{
    private static readonly FilePickerFileType TaskConfigurationFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.WinUI, new[] { ".toml" } },
        { DevicePlatform.macOS, new[] { ".toml" } },
        { DevicePlatform.iOS, new[] { ".toml" } },
        { DevicePlatform.Android, new[] { ".toml" } }
    });

    public async Task<IEnumerable<string>> PickSpectraAsync()
    {
        try
        {
            var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Select spectra files"
            }).ConfigureAwait(false);

            if (results == null)
            {
                return Enumerable.Empty<string>();
            }

            var resolved = await Task.WhenAll(results.Select(ResolveFileAsync)).ConfigureAwait(false);
            return resolved.Where(path => !string.IsNullOrWhiteSpace(path))!;
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    public async Task<IEnumerable<string>> PickProteinDatabasesAsync()
    {
        try
        {
            var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Select protein databases"
            }).ConfigureAwait(false);

            if (results == null)
            {
                return Enumerable.Empty<string>();
            }

            var resolved = await Task.WhenAll(results.Select(ResolveFileAsync)).ConfigureAwait(false);
            return resolved.Where(path => !string.IsNullOrWhiteSpace(path))!;
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    public async Task<string?> PickTaskConfigurationAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a MetaMorpheus task configuration",
                FileTypes = TaskConfigurationFileType
            }).ConfigureAwait(false);

            return result == null ? null : await ResolveFileAsync(result).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> ResolveFileAsync(FileResult file)
    {
        if (!string.IsNullOrWhiteSpace(file.FullPath))
        {
            return file.FullPath;
        }

        try
        {
            var target = Path.Combine(FileSystem.CacheDirectory, file.FileName);
            await using var destination = File.OpenWrite(target);
            await using var source = await file.OpenReadAsync();
            await source.CopyToAsync(destination).ConfigureAwait(false);
            return target;
        }
        catch
        {
            return null;
        }
    }
}
