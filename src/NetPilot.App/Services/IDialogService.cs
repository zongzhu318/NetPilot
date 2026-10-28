using Avalonia.Platform.Storage;

namespace NetPilot.App.Services;

public interface IDialogService
{
    Task<string?> PickOpenFileAsync(string title, params FilePickerFileType[] types);
    Task<string?> PickSaveFileAsync(string title, string suggestedName, params FilePickerFileType[] types);
    Task ShowMessageAsync(string title, string message, bool isError = false);
    Task<bool> ConfirmAsync(string title, string message);
    Task<string?> PromptAsync(string title, string label, string defaultValue = "");
    Task CopyTextAsync(string text);
}
