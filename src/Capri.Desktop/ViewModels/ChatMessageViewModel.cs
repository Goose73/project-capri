using CommunityToolkit.Mvvm.ComponentModel;

namespace Capri.Desktop.ViewModels;

public sealed partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _role = string.Empty;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isError;

    public ChatMessageViewModel(string role, string text, bool isError = false)
    {
        _role = role;
        _text = text;
        _isError = isError;
    }

    public void Append(string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        Text += delta;
    }
}
