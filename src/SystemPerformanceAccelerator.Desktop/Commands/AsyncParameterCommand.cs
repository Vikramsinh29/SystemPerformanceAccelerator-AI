using System.Windows.Input;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Commands;

public sealed class AsyncParameterCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private readonly IFeatureAccessGuard? _featureAccessGuard;
    private readonly ApplicationFeature _feature;
    private readonly FeatureAccessRequirement _accessRequirement;
    private bool _isExecuting;

    public AsyncParameterCommand(
        Func<object?, Task> execute,
        Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public AsyncParameterCommand(
        Func<object?, Task> execute,
        IFeatureAccessGuard featureAccessGuard,
        ApplicationFeature feature,
        FeatureAccessRequirement accessRequirement,
        Predicate<object?>? canExecute = null)
        : this(execute, canExecute)
    {
        _featureAccessGuard = featureAccessGuard ??
            throw new ArgumentNullException(nameof(featureAccessGuard));
        _feature = feature;
        _accessRequirement = accessRequirement;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isExecuting &&
        HasFeatureAccess() &&
        (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter) =>
        await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private bool HasFeatureAccess() =>
        _featureAccessGuard?.CanAccess(_feature, _accessRequirement) ?? true;
}
