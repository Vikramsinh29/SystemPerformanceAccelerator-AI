namespace SystemPerformanceAccelerator.Core.Models;

public static class BulkSelection
{
    public static bool? GetState<T>(
        IEnumerable<T> items,
        Func<T, bool> isSelected)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(isSelected);

        var anyItem = false;
        var anySelected = false;
        var anyUnselected = false;

        foreach (var item in items)
        {
            anyItem = true;
            if (isSelected(item))
            {
                anySelected = true;
            }
            else
            {
                anyUnselected = true;
            }

            if (anySelected && anyUnselected)
            {
                return null;
            }
        }

        return anyItem && anySelected;
    }

    public static bool? ResolveTarget(
        bool? requestedState,
        bool? currentState) =>
        requestedState ?? (currentState == true ? false : null);

    public static void SetAll<T>(
        IEnumerable<T> items,
        bool isSelected,
        Action<T, bool> setSelected)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(setSelected);

        foreach (var item in items)
        {
            setSelected(item, isSelected);
        }
    }

    public static bool? GetAllButOnePerGroupState<T, TKey>(
        IEnumerable<T> items,
        Func<T, TKey> groupKey,
        Func<T, bool> isSelected,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(isSelected);

        var groups = items.GroupBy(groupKey, comparer).ToArray();
        if (groups.Length == 0)
        {
            return false;
        }

        var anySelected = false;
        var everyGroupAtMaximum = true;

        foreach (var group in groups)
        {
            var groupItems = group.ToArray();
            var selectedCount = groupItems.Count(isSelected);
            anySelected |= selectedCount > 0;
            everyGroupAtMaximum &=
                selectedCount == Math.Max(0, groupItems.Length - 1);
        }

        if (!anySelected)
        {
            return false;
        }

        return everyGroupAtMaximum ? true : null;
    }

    public static void SetAllButOnePerGroup<T, TKey>(
        IEnumerable<T> items,
        bool selectRemovableItems,
        Func<T, TKey> groupKey,
        Action<T, bool> setSelected,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(setSelected);

        if (!selectRemovableItems)
        {
            SetAll(items, false, setSelected);
            return;
        }

        foreach (var group in items.GroupBy(groupKey, comparer))
        {
            var keepFirst = true;
            foreach (var item in group)
            {
                setSelected(item, !keepFirst);
                keepFirst = false;
            }
        }
    }
}
