namespace AccessibleVideoEditor.Core.Model;

/// <summary>A rectangle on the frame, in pixels of the project canvas.</summary>
public readonly record struct FrameRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double Area => Math.Max(0, Width) * Math.Max(0, Height);

    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;

    public FrameRect Intersect(FrameRect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var r = Math.Min(Right, other.Right);
        var b = Math.Min(Bottom, other.Bottom);
        return r <= x || b <= y ? new FrameRect(x, y, 0, 0) : new FrameRect(x, y, r - x, b - y);
    }
}

/// <summary>
/// Where an item's picture lands on the frame. This is the same arithmetic the renderer uses (crop, fit
/// inside the size box, place by preset with a margin, keep inside the frame), so what is spoken is what
/// is seen. Text size is estimated from the number of letters.
/// </summary>
public static class Geometry
{
    /// <summary>Gap between a corner-placed picture and the frame edge, as a share of the shorter side.</summary>
    public const double MarginShare = 0.04;

    public static double Margin(int width, int height) => Math.Round(Math.Min(width, height) * MarginShare / 2) * 2;

    public static FrameRect RectOf(Item item, int width, int height)
    {
        var placement = item.Placement;
        double w, h;
        if (item.Kind == ItemKind.Text)
        {
            (w, h) = TextSize(item.Text, width, height);
        }
        else
        {
            double sourceW = item.SourceWidth > 0 ? item.SourceWidth : width;
            double sourceH = item.SourceHeight > 0 ? item.SourceHeight : height;
            if (item.Kind == ItemKind.Color)
            {
                sourceW = width;
                sourceH = height;
            }

            sourceW *= Math.Max(0.05, 1 - (placement.CropLeft + placement.CropRight) / 100);
            sourceH *= Math.Max(0.05, 1 - (placement.CropTop + placement.CropBottom) / 100);
            var (boxW, boxH) = BoxSize(placement, width, height);
            var scale = placement.Position == PositionPreset.FullFrame && placement.Fill
                ? Math.Max(boxW / sourceW, boxH / sourceH)
                : Math.Min(boxW / sourceW, boxH / sourceH);
            w = Math.Min(width, sourceW * scale);
            h = Math.Min(height, sourceH * scale);
        }

        var (x, y) = Position(placement, width, height, w, h, item.Kind == ItemKind.Text);
        return new FrameRect(x, y, w, h);
    }

    public static (double Width, double Height) BoxSize(Placement placement, int width, int height)
    {
        if (placement.Position == PositionPreset.FullFrame)
        {
            return (width, height);
        }

        var scale = Math.Clamp(placement.SizePercent, 5, 100) / 100;
        return (width * scale, height * scale);
    }

    public static double TextPixelSize(TextStyle text, int height) =>
        Math.Max(8, Math.Round(height * Math.Clamp(text.SizePercent, 1, 50) / 100));

    /// <summary>Approximate size of a text block: about 0.55 of the letter height per letter.</summary>
    public static (double Width, double Height) TextSize(TextStyle text, int width, int height)
    {
        var size = TextPixelSize(text, height);
        var lines = (string.IsNullOrEmpty(text.Content) ? " " : text.Content).Replace("\r", "").Split('\n');
        var longest = lines.Max(l => l.Length);
        var padding = text.Background == TextBackground.None ? 0 : size * 0.6;
        var w = Math.Min(width, Math.Max(size, longest * size * 0.55) + padding);
        var h = Math.Min(height, lines.Length * size * 1.2 + padding);
        return (w, h);
    }

    private static (double X, double Y) Position(Placement placement, int width, int height, double w, double h, bool isText)
    {
        var m = Margin(width, height);
        var center = (width - w) / 2;
        var middle = (height - h) / 2;
        var preset = placement.Position == PositionPreset.FullFrame && isText ? PositionPreset.Center : placement.Position;
        var (x, y) = preset switch
        {
            PositionPreset.TopLeft => (m, m),
            PositionPreset.TopCenter => (center, m),
            PositionPreset.TopRight => (width - w - m, m),
            PositionPreset.MiddleLeft => (m, middle),
            PositionPreset.MiddleRight => (width - w - m, middle),
            PositionPreset.BottomLeft => (m, height - h - m),
            PositionPreset.BottomCenter => (center, height - h - m),
            PositionPreset.BottomRight => (width - w - m, height - h - m),
            PositionPreset.Custom => (width * placement.CustomXPercent / 100, height * placement.CustomYPercent / 100),
            _ => (center, middle),
        };

        return (Math.Clamp(x, 0, Math.Max(0, width - w)), Math.Clamp(y, 0, Math.Max(0, height - h)));
    }

    /// <summary>The 3 by 3 area of the frame a rectangle's centre falls in, e.g. "top right".</summary>
    public static string Area(FrameRect rect, int width, int height)
    {
        var column = rect.CenterX < width / 3.0 ? "left" : rect.CenterX > width * 2 / 3.0 ? "right" : "centre";
        var row = rect.CenterY < height / 3.0 ? "top" : rect.CenterY > height * 2 / 3.0 ? "bottom" : "middle";
        return (row, column) switch
        {
            ("middle", "centre") => "centre",
            ("middle", _) => $"middle {column}",
            (_, "centre") => $"{row} centre",
            _ => $"{row} {column}",
        };
    }
}
