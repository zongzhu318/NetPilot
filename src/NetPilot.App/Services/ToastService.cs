namespace NetPilot.App.Services;

public sealed class ToastService
{
    public event EventHandler<string>? Shown;

    public void Show(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Shown?.Invoke(this, message);
        }
    }
}
