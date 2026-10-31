using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace CodexTray;

internal static class TrayIconFactory
{
    private const int CanvasSize = 64;
    private static readonly IReadOnlyDictionary<char, string[]> DigitPatterns = new Dictionary<char, string[]>
    {
        ['0'] = ["111", "101", "101", "101", "111"],
        ['1'] = ["010", "110", "010", "010", "111"],
        ['2'] = ["111", "001", "111", "100", "111"],
        ['3'] = ["111", "001", "111", "001", "111"],
        ['4'] = ["101", "101", "111", "001", "001"],
        ['5'] = ["111", "100", "111", "001", "111"],
        ['6'] = ["111", "100", "111", "101", "111"],
        ['7'] = ["111", "001", "001", "001", "001"],
        ['8'] = ["111", "101", "111", "101", "111"],
        ['9'] = ["111", "101", "111", "001", "111"],
        ['-'] = ["000", "000", "111", "000", "000"]
    };

    public static Icon Create(decimal? remainingPercent, Color color)
    {
        using var bitmap = new Bitmap(CanvasSize, CanvasSize);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var outer = new RectangleF(2, 1, 60, 61);
            using var shadowPath = RoundedRectangle(new RectangleF(3, 3, 60, 60), 13);
            using var shadowBrush = new SolidBrush(Color.FromArgb(90, Color.Black));
            graphics.FillPath(shadowBrush, shadowPath);

            using var outerPath = RoundedRectangle(outer, 13);
            using var bodyBrush = new SolidBrush(Color.FromArgb(43, 47, 53));
            graphics.FillPath(bodyBrush, outerPath);

            using var previousClip = graphics.Clip;
            graphics.SetClip(outerPath);
            if (remainingPercent is { } remaining)
            {
                var normalized = (float)Math.Clamp(remaining, 0m, 100m) / 100f;
                var fillHeight = outer.Height * normalized;
                var fillRectangle = new RectangleF(outer.X, outer.Bottom - fillHeight, outer.Width, fillHeight);
                using var fillBrush = new LinearGradientBrush(
                    fillRectangle.Height > 0 ? fillRectangle : new RectangleF(outer.X, outer.Bottom - 1, outer.Width, 1),
                    Lighten(color, 0.18f),
                    color,
                    LinearGradientMode.Vertical);
                graphics.FillRectangle(fillBrush, fillRectangle);

                using var shineBrush = new SolidBrush(Color.FromArgb(32, Color.White));
                graphics.FillRectangle(shineBrush, outer.X, outer.Y, outer.Width * 0.20f, outer.Height);
            }

            graphics.Clip = previousClip;
            using var borderPen = new Pen(Color.FromArgb(220, Color.Black), 3);
            graphics.DrawPath(borderPen, outerPath);

            var rounded = remainingPercent is null
                ? "--"
                : Math.Round(Math.Clamp(remainingPercent.Value, 0m, 100m), 0, MidpointRounding.AwayFromZero).ToString("0");
            DrawDigits(graphics, rounded);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawDigits(Graphics graphics, string digits)
    {
        var cellSize = digits.Length >= 3 ? 4f : 6f;
        var glyphWidth = 3 * cellSize;
        var gap = cellSize;
        var totalWidth = digits.Length * glyphWidth + Math.Max(0, digits.Length - 1) * gap;
        var totalHeight = 5 * cellSize;
        var startX = (CanvasSize - totalWidth) / 2f;
        var startY = (CanvasSize - totalHeight) / 2f;

        graphics.SmoothingMode = SmoothingMode.None;
        using var outlineBrush = new SolidBrush(Color.Black);
        using var digitBrush = new SolidBrush(Color.White);
        DrawPatternCells(graphics, digits, startX, startY, cellSize, glyphWidth, gap, outlineBrush, 1f);
        DrawPatternCells(graphics, digits, startX, startY, cellSize, glyphWidth, gap, digitBrush, 0f);
    }

    private static void DrawPatternCells(
        Graphics graphics,
        string digits,
        float startX,
        float startY,
        float cellSize,
        float glyphWidth,
        float gap,
        Brush brush,
        float expansion)
    {
        for (var digitIndex = 0; digitIndex < digits.Length; digitIndex++)
        {
            if (!DigitPatterns.TryGetValue(digits[digitIndex], out var pattern))
            {
                continue;
            }

            var digitX = startX + digitIndex * (glyphWidth + gap);
            for (var row = 0; row < pattern.Length; row++)
            {
                for (var column = 0; column < pattern[row].Length; column++)
                {
                    if (pattern[row][column] == '1')
                    {
                        graphics.FillRectangle(
                            brush,
                            digitX + column * cellSize - expansion,
                            startY + row * cellSize - expansion,
                            cellSize + expansion * 2,
                            cellSize + expansion * 2);
                    }
                }
            }
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Lighten(Color color, float amount)
    {
        var red = color.R + (255 - color.R) * amount;
        var green = color.G + (255 - color.G) * amount;
        var blue = color.B + (255 - color.B) * amount;
        return Color.FromArgb(color.A, (int)red, (int)green, (int)blue);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
