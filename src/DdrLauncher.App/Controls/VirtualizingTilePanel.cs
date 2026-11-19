using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DdrLauncher.App.Controls;

/// <summary>
/// A grid of same-size cards that only builds the cards on screen (plus one row each side), so a list of 400 cards
/// opens as fast as a list of 20. As many columns as fit at <see cref="ItemMinWidth"/>; the row height is the first card's.
/// </summary>
public sealed class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemMinWidthProperty = DependencyProperty.Register(
        nameof(ItemMinWidth), typeof(double), typeof(VirtualizingTilePanel), new FrameworkPropertyMetadata(292.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private readonly TranslateTransform _scroll = new();
    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private double _rowHeight;

    public VirtualizingTilePanel() => RenderTransform = _scroll;

    /// <summary>The narrowest a card may get, margin included.</summary>
    public double ItemMinWidth
    {
        get => (double)GetValue(ItemMinWidthProperty);
        set => SetValue(ItemMinWidthProperty, value);
    }

    public ScrollViewer? ScrollOwner { get; set; }

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; }

    public double ExtentWidth => _extent.Width;

    public double ExtentHeight => _extent.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => 0;

    public double VerticalOffset => _offset.Y;

    private int Count => ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? ItemMinWidth : available.Width;
        var columns = Math.Max(1, (int)(width / ItemMinWidth));
        var itemWidth = width / columns;
        var count = Count;

        // _ = InternalChildren makes sure the generator is ready before it is used.
        _ = InternalChildren;
        var generator = ItemContainerGenerator;
        if (_rowHeight <= 0 && count > 0)
        {
            _rowHeight = Realize(generator, 0, new Size(itemWidth, double.PositiveInfinity)).DesiredSize.Height;
        }

        var rows = (count + columns - 1) / columns;
        UpdateScroll(new Size(width, rows * _rowHeight), available);

        var first = 0;
        var last = -1;
        if (count > 0 && _rowHeight > 0)
        {
            var firstRow = Math.Max(0, (int)(_offset.Y / _rowHeight) - 1);
            var lastRow = Math.Min(rows - 1, (int)((_offset.Y + _viewport.Height) / _rowHeight) + 1);
            first = firstRow * columns;
            last = Math.Min(count - 1, (lastRow + 1) * columns - 1);
            for (var i = first; i <= last; i++)
            {
                Realize(generator, i, new Size(itemWidth, _rowHeight));
            }
        }

        CleanUp(generator, first, last);
        return new Size(width, double.IsInfinity(available.Height) ? _extent.Height : available.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var columns = Math.Max(1, (int)(final.Width / ItemMinWidth));
        var itemWidth = final.Width / columns;
        var generator = ItemContainerGenerator;
        foreach (UIElement child in InternalChildren)
        {
            var index = generator.IndexFromGeneratorPosition(new GeneratorPosition(InternalChildren.IndexOf(child), 0));
            child.Arrange(new Rect(index % columns * itemWidth, index / columns * _rowHeight, itemWidth, _rowHeight));
        }

        _scroll.Y = -_offset.Y;
        return final;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Move)
        {
            RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
        }

        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            _rowHeight = 0;
            SetVerticalOffset(0);
        }

        base.OnItemsChanged(sender, args);
    }

    // Builds (or reuses) the card for item `index` and measures it.
    private UIElement Realize(IItemContainerGenerator generator, int index, Size size)
    {
        var position = generator.GeneratorPositionFromIndex(index);
        using (generator.StartAt(position, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
        {
            var child = (UIElement)generator.GenerateNext(out var isNew);
            if (isNew)
            {
                var insertAt = position.Offset == 0 ? position.Index : position.Index + 1;
                if (insertAt >= InternalChildren.Count)
                {
                    AddInternalChild(child);
                }
                else
                {
                    InsertInternalChild(insertAt, child);
                }

                generator.PrepareItemContainer(child);
            }

            child.Measure(size);
            return child;
        }
    }

    // Drops the cards that scrolled out of the realized range.
    private void CleanUp(IItemContainerGenerator generator, int first, int last)
    {
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var index = generator.IndexFromGeneratorPosition(position);
            if (index < first || index > last)
            {
                generator.Remove(position, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    private void UpdateScroll(Size extent, Size available)
    {
        var viewport = new Size(extent.Width, double.IsInfinity(available.Height) ? extent.Height : available.Height);
        if (extent != _extent || viewport != _viewport)
        {
            _extent = extent;
            _viewport = viewport;
            _offset.Y = Math.Max(0, Math.Min(_offset.Y, _extent.Height - _viewport.Height));
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, _extent.Height - _viewport.Height));
        if (offset != _offset.Y)
        {
            _offset.Y = offset;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }
    }

    public void SetHorizontalOffset(double offset)
    {
    }

    public void LineUp() => SetVerticalOffset(_offset.Y - 48);

    public void LineDown() => SetVerticalOffset(_offset.Y + 48);

    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);

    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);

    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - 120);

    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + 120);

    public void LineLeft()
    {
    }

    public void LineRight()
    {
    }

    public void PageLeft()
    {
    }

    public void PageRight()
    {
    }

    public void MouseWheelLeft()
    {
    }

    public void MouseWheelRight()
    {
    }

    /// <summary>Scrolls a card into view (keyboard moves, selection).</summary>
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is UIElement child && InternalChildren.Contains(child))
        {
            var columns = Math.Max(1, (int)(_extent.Width / ItemMinWidth));
            var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(InternalChildren.IndexOf(child), 0));
            var top = index / columns * _rowHeight;
            if (top < _offset.Y)
            {
                SetVerticalOffset(top);
            }
            else if (top + _rowHeight > _offset.Y + _viewport.Height)
            {
                SetVerticalOffset(top + _rowHeight - _viewport.Height);
            }
        }

        return rectangle;
    }

    protected override void BringIndexIntoView(int index)
    {
        var columns = Math.Max(1, (int)(_extent.Width / ItemMinWidth));
        SetVerticalOffset(index / columns * _rowHeight);
    }
}
