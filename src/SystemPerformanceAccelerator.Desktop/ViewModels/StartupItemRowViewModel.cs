using System.ComponentModel;
using System.Runtime.CompilerServices;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class StartupItemRowViewModel : INotifyPropertyChanged
{
    private bool _isUpdating;

    public StartupItemRowViewModel(StartupItem model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public StartupItem Model { get; }

    public string Name => Model.Name;

    public string Command => Model.Command;

    public string Source => Model.Source;

    public string Location => Model.Location;

    public StartupItemState State => Model.State;

    public string DetailedStatus => Model.Status;

    public bool IsUpdating
    {
        get => _isUpdating;
        set
        {
            if (_isUpdating == value)
            {
                return;
            }

            _isUpdating = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateLabel));
            OnPropertyChanged(nameof(StateGlyph));
            OnPropertyChanged(nameof(StateActionToolTip));
            OnPropertyChanged(nameof(CanToggle));
        }
    }

    public bool CanToggle =>
        !IsUpdating &&
        (Model.CanEnable || Model.CanDisable);

    public StartupItemState RequestedState => Model.State switch
    {
        StartupItemState.Enabled => StartupItemState.Disabled,
        StartupItemState.Disabled => StartupItemState.Enabled,
        _ => StartupItemState.Unknown
    };

    public string StateLabel => IsUpdating
        ? "Updating..."
        : Model.State switch
        {
            StartupItemState.Enabled => "Enabled",
            StartupItemState.Disabled => "Disabled",
            _ => "Unknown"
        };

    public string StateGlyph => IsUpdating
        ? "\uE72C"
        : Model.State switch
        {
            StartupItemState.Enabled => "\uE73E",
            StartupItemState.Disabled => "\uE711",
            _ => "\uE897"
        };

    public string StateActionToolTip
    {
        get
        {
            if (IsUpdating)
            {
                return $"Updating '{Model.Name}' and verifying the Windows startup state.";
            }

            if (Model.CanDisable)
            {
                return $"{Model.Status}. Click to disable this startup item.";
            }

            if (Model.CanEnable)
            {
                return $"{Model.Status}. Click to enable this startup item.";
            }

            return string.IsNullOrWhiteSpace(Model.StateChangeUnavailableReason)
                ? Model.Status
                : $"{Model.Status}. {Model.StateChangeUnavailableReason}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(name));
}
