using SystemPerformanceAccelerator.Core.Models;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class BulkSelectionTests
{
    [Fact]
    public void SetAll_SelectsAndDeselectsEveryItem()
    {
        var items = new[]
        {
            new Item(false, "A"),
            new Item(true, "A"),
            new Item(false, "B")
        };

        BulkSelection.SetAll(
            items,
            true,
            static (item, selected) => item.IsSelected = selected);
        Assert.All(items, item => Assert.True(item.IsSelected));

        BulkSelection.SetAll(
            items,
            false,
            static (item, selected) => item.IsSelected = selected);
        Assert.All(items, item => Assert.False(item.IsSelected));
    }

    [Fact]
    public void GetState_ReportsCheckedPartialUncheckedAndEmptyReset()
    {
        var items = new List<Item>
        {
            new(false, "A"),
            new(false, "A")
        };

        Assert.Equal(false, BulkSelection.GetState(items, item => item.IsSelected));

        items[0].IsSelected = true;
        items[1].IsSelected = true;
        Assert.Equal(true, BulkSelection.GetState(items, item => item.IsSelected));

        items[1].IsSelected = false;
        Assert.Null(BulkSelection.GetState(items, item => item.IsSelected));

        items.Clear();
        Assert.Equal(false, BulkSelection.GetState(items, item => item.IsSelected));

        Assert.Equal(false, BulkSelection.ResolveTarget(null, true));
        Assert.Null(BulkSelection.ResolveTarget(null, null));
    }

    [Fact]
    public void SetAllButOnePerGroup_PreservesOneKeeperInEveryGroup()
    {
        var items = new[]
        {
            new Item(false, "A"),
            new Item(false, "A"),
            new Item(false, "A"),
            new Item(false, "B"),
            new Item(false, "B")
        };

        BulkSelection.SetAllButOnePerGroup(
            items,
            true,
            item => item.Group,
            static (item, selected) => item.IsSelected = selected);

        Assert.Equal(2, items.Count(item => item.Group == "A" && item.IsSelected));
        Assert.Equal(1, items.Count(item => item.Group == "B" && item.IsSelected));
        Assert.Equal(1, items.Count(item => item.Group == "A" && !item.IsSelected));
        Assert.Equal(1, items.Count(item => item.Group == "B" && !item.IsSelected));
    }

    [Fact]
    public void GetAllButOnePerGroupState_ReportsCheckedPartialAndUnchecked()
    {
        var items = new[]
        {
            new Item(false, "A"),
            new Item(true, "A"),
            new Item(false, "B"),
            new Item(true, "B")
        };

        Assert.Equal(true, BulkSelection.GetAllButOnePerGroupState(
            items,
            item => item.Group,
            item => item.IsSelected));

        items[3].IsSelected = false;
        Assert.Null(BulkSelection.GetAllButOnePerGroupState(
            items,
            item => item.Group,
            item => item.IsSelected));

        items[1].IsSelected = false;
        Assert.Equal(false, BulkSelection.GetAllButOnePerGroupState(
            items,
            item => item.Group,
            item => item.IsSelected));
    }

    private sealed class Item(bool isSelected, string group)
    {
        public bool IsSelected { get; set; } = isSelected;
        public string Group { get; } = group;
    }
}
