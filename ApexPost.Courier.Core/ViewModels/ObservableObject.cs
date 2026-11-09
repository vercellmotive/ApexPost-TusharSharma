using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ApexPost.Courier.Core.ViewModels;

/// <summary>A minimal INotifyPropertyChanged base, so the view models need no MVVM library.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>An ICommand that runs an async method and cannot be started twice at once.</summary>
public sealed class AsyncCommand(Func<Task> execute) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running;

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute();
        }
        finally
        {
            _running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>Runs work on the UI thread. The app's implementation uses MAUI MainThread.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>Moves between the Login page and the main tabs.</summary>
public interface INavigator
{
    Task ShowLoginAsync();

    Task ShowMainAsync();
}
