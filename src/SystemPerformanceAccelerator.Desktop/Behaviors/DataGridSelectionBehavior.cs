using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SystemPerformanceAccelerator.Desktop.ViewModels;

namespace SystemPerformanceAccelerator.Desktop.Behaviors;

public static class DataGridSelectionBehavior
{
    private static readonly MouseButtonEventHandler RowPreviewMouseLeftButtonDownHandler =
        OnRowPreviewMouseLeftButtonDown;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DataGridSelectionBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not DataGrid dataGrid)
        {
            return;
        }

        dataGrid.LoadingRow -= OnLoadingRow;
        dataGrid.UnloadingRow -= OnUnloadingRow;
        DetachRealizedRows(dataGrid);

        if (eventArgs.NewValue is true)
        {
            dataGrid.LoadingRow += OnLoadingRow;
            dataGrid.UnloadingRow += OnUnloadingRow;
            AttachRealizedRows(dataGrid);
        }
    }

    private static void OnLoadingRow(object? sender, DataGridRowEventArgs eventArgs) =>
        AttachRow(eventArgs.Row);

    private static void OnUnloadingRow(object? sender, DataGridRowEventArgs eventArgs) =>
        DetachRow(eventArgs.Row);

    private static void AttachRealizedRows(DataGrid dataGrid)
    {
        foreach (var item in dataGrid.Items)
        {
            if (dataGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
            {
                AttachRow(row);
            }
        }
    }

    private static void DetachRealizedRows(DataGrid dataGrid)
    {
        foreach (var item in dataGrid.Items)
        {
            if (dataGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
            {
                DetachRow(row);
            }
        }
    }

    private static void AttachRow(DataGridRow row)
    {
        row.RemoveHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            RowPreviewMouseLeftButtonDownHandler);

        row.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            RowPreviewMouseLeftButtonDownHandler,
            handledEventsToo: true);
    }

    private static void DetachRow(DataGridRow row)
    {
        row.RemoveHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            RowPreviewMouseLeftButtonDownHandler);
    }

    private static void OnRowPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (sender is not DataGridRow row ||
            row.Item is not ISelectableItem selectableItem ||
            eventArgs.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var checkBox = FindParent<CheckBox>(source);
        if (checkBox is not null)
        {
            if (!checkBox.IsEnabled)
            {
                return;
            }

            // Toggle exactly once on the first physical click. The second
            // click of a double-click is consumed without toggling again.
            if (eventArgs.ClickCount == 1)
            {
                SelectRow(row);
                selectableItem.IsSelected = !selectableItem.IsSelected;
            }

            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.ClickCount == 2)
        {
            SelectRow(row);
            selectableItem.IsSelected = !selectableItem.IsSelected;
            eventArgs.Handled = true;
        }
    }

    private static void SelectRow(DataGridRow row)
    {
        row.IsSelected = true;

        if (ItemsControl.ItemsControlFromItemContainer(row) is DataGrid dataGrid)
        {
            dataGrid.CurrentItem = row.Item;
            dataGrid.ScrollIntoView(row.Item);
        }
    }

    private static T? FindParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        DependencyObject? current = child;

        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        return null;
    }
}
