using System.Windows.Input;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Commands;

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    private readonly IFeatureAccessGuard? _featureAccessGuard;
    private readonly ApplicationFeature _feature;
    private readonly FeatureAccessRequirement _accessRequirement;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(
        Action execute,
        IFeatureAccessGuard featureAccessGuard,
        ApplicationFeature feature,
        FeatureAccessRequirement accessRequirement,
        Func<bool>? canExecute = null)
        : this(execute, canExecute)
    {
        _featureAccessGuard = featureAccessGuard ??
            throw new ArgumentNullException(nameof(featureAccessGuard));
        _feature = feature;
        _accessRequirement = accessRequirement;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        HasFeatureAccess() && (_canExecute?.Invoke() ?? true);

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _execute();
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private bool HasFeatureAccess() =>
        _featureAccessGuard?.CanAccess(_feature, _accessRequirement) ?? true;
}
