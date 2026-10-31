using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MissionEditor.App.ViewModels;

/// <summary>Base class for view models with change notification.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
  /// <inheritdoc/>
  public event PropertyChangedEventHandler? PropertyChanged;

  /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
  protected void OnPropertyChanged ([CallerMemberName] string? propertyName = null) =>
      PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));

  /// <summary>Sets a field and raises <see cref="PropertyChanged"/> if the value changed.</summary>
  protected bool SetProperty<TValue> (ref TValue field, TValue value, [CallerMemberName] string? propertyName = null)
  {
    if (EqualityComparer<TValue>.Default.Equals (field, value))
      return false;
    field = value;
    OnPropertyChanged (propertyName);
    return true;
  }
}

/// <summary>A command that calls delegates.</summary>
/// <param name="execute">What the command does.</param>
/// <param name="canExecute">Whether it can run; null means always.</param>
public sealed class RelayCommand (Action execute, Func<bool>? canExecute = null) : ICommand
{
  /// <inheritdoc/>
  public event EventHandler? CanExecuteChanged
  {
    add => CommandManager.RequerySuggested += value;
    remove => CommandManager.RequerySuggested -= value;
  }

  /// <inheritdoc/>
  public bool CanExecute (object? parameter) => canExecute?.Invoke () ?? true;

  /// <inheritdoc/>
  public void Execute (object? parameter) => execute ();
}
