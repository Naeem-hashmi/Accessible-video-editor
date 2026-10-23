using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.App;

/// <summary>
/// Every layer drawn along time, for mouse users and sighted helpers: click an item to select it, click
/// empty space to move the playhead, drag an item to move it, drag its right end to change its length,
/// double-click for its options, right-click for a menu, and Control with the wheel to zoom. It draws in
/// system colours, so it follows High Contrast. Keyboard and screen reader users have the same through the
/// Layers and Timeline lists, so this view takes no keyboard focus.
/// </summary>
public sealed class TimelineTracks : FrameworkElement
{
    public const double RulerHeight = 22;
    public const double RowHeight = 38;
    private const double EdgeGrip = 6;

    private Project? _project;
    private Guid? _focusedLayer;
    private Guid? _focusedItem;
    private double _playhead;
    private double _pixelsPerSecond = 40;

    private (Guid Layer, Guid Item, bool Trim, Point Start, double ItemStart, double ItemEnd)? _drag;
    private double _dragOffset;

    public TimelineTracks()
    {
        Focusable = false;
        ClipToBounds = true;
        Cursor = Cursors.Arrow;
    }

    /// <summary>Clicked on an item (itemId set) or empty space (null) at a time.</summary>
    public event Action<Guid, Guid?, double>? Clicked;

    /// <summary>An item was dragged to start at a new time.</summary>
    public event Action<Guid, Guid, double>? ItemMoved;

    /// <summary>An item's right end was dragged to a new end time.</summary>
    public event Action<Guid, Guid, double>? ItemResized;

    public event Action<Guid, Guid>? ItemDoubleClicked;

    /// <summary>Right-click: layer, item (or null) and time.</summary>
    public event Action<Guid, Guid?, double>? MenuRequested;

    public double PixelsPerSecond
    {
        get => _pixelsPerSecond;
        set
        {
            _pixelsPerSecond = Math.Clamp(value, 4, 400);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public void Update(Project project, Guid? focusedLayer, Guid? focusedItem, double playhead)
    {
        _project = project;
        _focusedLayer = focusedLayer;
        _focusedItem = focusedItem;
        _playhead = playhead;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetPlayhead(double time)
    {
        _playhead = time;
        InvalidateVisual();
    }

    /// <summary>The x position of a time, for scrolling the playhead into view.</summary>
    public double XOf(double time) => time * _pixelsPerSecond;

    protected override Size MeasureOverride(Size availableSize)
    {
        var duration = _project is null ? 0 : Timeline.ProjectDuration(_project);
        var layers = _project?.Layers.Count ?? 0;
        var width = Math.Max(duration * _pixelsPerSecond + 80, double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width);
        return new Size(width, RulerHeight + Math.Max(1, layers) * RowHeight + 4);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    // Layers are drawn top to bottom from the highest (front) layer, like the picture they make.
    private int RowOf(int layerIndex) => (_project?.Layers.Count ?? 1) - 1 - layerIndex;

    protected override void OnRender(DrawingContext dc)
    {
        var back = SystemColors.WindowBrush;
        var text = SystemColors.WindowTextBrush;
        var grid = new Pen(SystemColors.GrayTextBrush, 0.5);
        dc.DrawRectangle(back, null, new Rect(RenderSize));
        if (_project is null)
        {
            return;
        }

        var typeface = new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Text(string s, double size, Brush brush) =>
            new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush, dpi);

        // Ruler: a tick every second, labels at a readable spacing.
        var step = _pixelsPerSecond >= 60 ? 1 : _pixelsPerSecond >= 20 ? 5 : _pixelsPerSecond >= 8 ? 10 : 30;
        var duration = Timeline.ProjectDuration(_project);
        for (var t = 0; t <= duration + step; t += step)
        {
            var x = t * _pixelsPerSecond;
            dc.DrawLine(grid, new Point(x, RulerHeight - 6), new Point(x, RenderSize.Height));
            dc.DrawText(Text(TimeSpan.FromSeconds(t).ToString(t >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture), 11, text), new Point(x + 2, 2));
        }

        for (var i = 0; i < _project.Layers.Count; i++)
        {
            var layer = _project.Layers[i];
            var top = RulerHeight + RowOf(i) * RowHeight;
            if (layer.Id == _focusedLayer)
            {
                dc.DrawRectangle(SystemColors.ControlLightBrush, null, new Rect(0, top, RenderSize.Width, RowHeight));
            }

            dc.DrawLine(grid, new Point(0, top + RowHeight), new Point(RenderSize.Width, top + RowHeight));
            var spans = Timeline.Spans(layer);
            for (var k = 0; k < layer.Items.Count; k++)
            {
                var item = layer.Items[k];
                var start = spans[k].Start;
                var end = spans[k].End;
                if (_drag is { } d && d.Item == item.Id)
                {
                    if (d.Trim)
                    {
                        end = Math.Max(start + Timeline.MinItemDuration, d.ItemEnd + _dragOffset);
                    }
                    else
                    {
                        start = Math.Max(0, d.ItemStart + _dragOffset);
                        end = start + spans[k].Duration;
                    }
                }

                var rect = new Rect(start * _pixelsPerSecond, top + 3, Math.Max(2, (end - start) * _pixelsPerSecond), RowHeight - 6);
                var focused = item.Id == _focusedItem;
                var fill = SystemParameters.HighContrast ? SystemColors.ControlBrush : KindBrush(item.Kind, layer.Muted);
                var border = new Pen(focused ? SystemColors.HighlightBrush : SystemColors.ControlDarkBrush, focused ? 3 : 1);
                dc.DrawRoundedRectangle(fill, border, rect, 4, 4);
                if (item.VolumeDips.Count > 0)
                {
                    // Ducked stretches as a darker band at the bottom.
                    foreach (var dip in item.VolumeDips)
                    {
                        var x0 = (start + dip.Start - item.SourceIn) * _pixelsPerSecond;
                        var x1 = (start + dip.End - item.SourceIn) * _pixelsPerSecond;
                        x0 = Math.Max(rect.Left, x0);
                        x1 = Math.Min(rect.Right, x1);
                        if (x1 > x0)
                        {
                            dc.DrawRectangle(SystemColors.ControlDarkBrush, null, new Rect(x0, rect.Bottom - 5, x1 - x0, 4));
                        }
                    }
                }

                var label = Text(item.Kind == ItemKind.Text ? item.Text.Content : item.Name, 12,
                    SystemParameters.HighContrast ? SystemColors.ControlTextBrush : Brushes.Black);
                label.MaxTextWidth = Math.Max(1, rect.Width - 8);
                label.MaxTextHeight = RowHeight - 10;
                label.Trimming = TextTrimming.CharacterEllipsis;
                dc.PushClip(new RectangleGeometry(rect));
                dc.DrawText(label, new Point(rect.Left + 4, rect.Top + 4));
                dc.Pop();
            }
        }

        // Beats as small ticks under the ruler, markers as lines.
        foreach (var beat in BeatEditing.BeatTimes(_project))
        {
            var x = beat * _pixelsPerSecond;
            dc.DrawLine(new Pen(SystemColors.HotTrackBrush, 1), new Point(x, RulerHeight - 5), new Point(x, RulerHeight));
        }

        foreach (var marker in _project.Markers)
        {
            var x = marker.Time * _pixelsPerSecond;
            dc.DrawLine(new Pen(SystemColors.HotTrackBrush, 1.5) { DashStyle = DashStyles.Dash }, new Point(x, RulerHeight), new Point(x, RenderSize.Height));
        }

        var playX = _playhead * _pixelsPerSecond;
        dc.DrawLine(new Pen(SystemParameters.HighContrast ? SystemColors.HighlightBrush : Brushes.Red, 2), new Point(playX, 0), new Point(playX, RenderSize.Height));
    }

    private static Brush KindBrush(ItemKind kind, bool muted)
    {
        var color = kind switch
        {
            ItemKind.Video => Color.FromRgb(0x9C, 0xC3, 0xF5),
            ItemKind.Image => Color.FromRgb(0xA8, 0xE0, 0xA8),
            ItemKind.Audio => Color.FromRgb(0xF7, 0xC9, 0x8B),
            ItemKind.Text => Color.FromRgb(0xD5, 0xB8, 0xF2),
            _ => Color.FromRgb(0xD0, 0xD0, 0xD0),
        };
        if (muted)
        {
            color = Color.FromRgb((byte)((color.R + 0xC0) / 2), (byte)((color.G + 0xC0) / 2), (byte)((color.B + 0xC0) / 2));
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private (Layer Layer, int Index, ItemSpan Span)? HitTest(Point p)
    {
        if (_project is null || p.Y < RulerHeight)
        {
            return null;
        }

        var row = (int)((p.Y - RulerHeight) / RowHeight);
        var layerIndex = _project.Layers.Count - 1 - row;
        if (layerIndex < 0 || layerIndex >= _project.Layers.Count)
        {
            return null;
        }

        var layer = _project.Layers[layerIndex];
        var time = p.X / _pixelsPerSecond;
        var spans = Timeline.Spans(layer);
        for (var k = spans.Count - 1; k >= 0; k--)
        {
            if (time >= spans[k].Start - EdgeGrip / _pixelsPerSecond && time <= spans[k].End + EdgeGrip / _pixelsPerSecond)
            {
                return (layer, k, spans[k]);
            }
        }

        return (layer, -1, default);
    }

    private Guid? LayerAt(Point p)
    {
        if (_project is null)
        {
            return null;
        }

        var row = (int)Math.Max(0, (p.Y - RulerHeight) / RowHeight);
        var index = _project.Layers.Count - 1 - row;
        return index >= 0 && index < _project.Layers.Count ? _project.Layers[index].Id : null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        var hit = HitTest(p);
        if (hit is { Index: >= 0 } h)
        {
            var item = h.Layer.Items[h.Index];
            if (e.ClickCount == 2)
            {
                ItemDoubleClicked?.Invoke(h.Layer.Id, item.Id);
                e.Handled = true;
                return;
            }

            var nearEnd = Math.Abs(p.X - h.Span.End * _pixelsPerSecond) <= EdgeGrip;
            _drag = (h.Layer.Id, item.Id, nearEnd, p, h.Span.Start, h.Span.End);
            _dragOffset = 0;
            CaptureMouse();
        }
        else if (LayerAt(p) is { } layerId)
        {
            Clicked?.Invoke(layerId, null, Math.Max(0, p.X / _pixelsPerSecond));
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_drag is { } d && e.LeftButton == MouseButtonState.Pressed)
        {
            _dragOffset = (p.X - d.Start.X) / _pixelsPerSecond;
            InvalidateVisual();
            return;
        }

        var hit = HitTest(p);
        Cursor = hit is { Index: >= 0 } h && Math.Abs(p.X - h.Span.End * _pixelsPerSecond) <= EdgeGrip ? Cursors.SizeWE
            : hit is { Index: >= 0 } ? Cursors.Hand : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag is not { } d)
        {
            return;
        }

        ReleaseMouseCapture();
        _drag = null;
        var moved = Math.Abs(e.GetPosition(this).X - d.Start.X) > 3;
        if (!moved)
        {
            Clicked?.Invoke(d.Layer, d.Item, Math.Max(0, d.Start.X / _pixelsPerSecond));
        }
        else if (d.Trim)
        {
            ItemResized?.Invoke(d.Layer, d.Item, d.ItemEnd + _dragOffset);
        }
        else
        {
            ItemMoved?.Invoke(d.Layer, d.Item, Math.Max(0, d.ItemStart + _dragOffset));
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        var hit = HitTest(p);
        if (hit is { } h)
        {
            MenuRequested?.Invoke(h.Layer.Id, h.Index >= 0 ? h.Layer.Items[h.Index].Id : null, Math.Max(0, p.X / _pixelsPerSecond));
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            PixelsPerSecond *= e.Delta > 0 ? 1.25 : 0.8;
            e.Handled = true;
        }
    }
}
