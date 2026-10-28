using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NetPilot.App.Views.Dialogs;

namespace NetPilot.App.Services;

public sealed class DesktopDialogService(TopLevel owner) : IDialogService
{
    public async Task<string?> PickOpenFileAsync(string title, params FilePickerFileType[] types)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types.Length > 0 ? types : null
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, params FilePickerFileType[] types)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = types.FirstOrDefault()?.Patterns?.FirstOrDefault()?.Replace("*.", string.Empty),
            FileTypeChoices = types.Length > 0 ? types : null,
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }

    public async Task ShowMessageAsync(string title, string message, bool isError = false)
    {
        if (owner is Window window)
        {
            await MessageDialog.ShowAsync(window, title, message, false, false, isError);
        }
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (owner is not Window window)
        {
            return false;
        }

        return await MessageDialog.ShowAsync(window, title, message, true, false, false);
    }

    public async Task<string?> PromptAsync(string title, string label, string defaultValue = "")
    {
        if (owner is not Window window)
        {
            return null;
        }

        return await PromptDialog.ShowAsync(window, title, label, defaultValue);
    }

    public async Task CopyTextAsync(string text)
    {
        if (owner.Clipboard is not null)
        {
            await owner.Clipboard.SetTextAsync(text);
        }
    }
}
