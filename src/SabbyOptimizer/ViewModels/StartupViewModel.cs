namespace PCTweaker.ViewModels;

public sealed class StartupViewModel : ViewModelBase
{
    private string _statusText = "Opening Sabby Optimizer…";
    private string _detailText = "Preparing the workspace.";
    private bool _hasError;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public void SetStatus(string status, string detail)
    {
        HasError = false;
        StatusText = status;
        DetailText = detail;
    }

    public void SetError(string status, string detail)
    {
        HasError = true;
        StatusText = status;
        DetailText = detail;
    }
}
