using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace NetworkDriveRemapper;

/// <summary>Loads the user's original PNG assets and draws them to exact current button dimensions.</summary>
internal static class IconAssets
{
    /// <summary>Loads and clones the embedded multi-size application icon for a window to own and dispose.</summary>
    /// <returns>An independent icon retaining its ICO frames after the resource stream closes.</returns>
    internal static Icon LoadApplicationIcon()
    {
        using Stream stream = typeof(IconAssets).Assembly.GetManifestResourceStream("NetworkDriveRemapper.Icons.Application.ico")
            ?? throw new InvalidOperationException("The embedded application icon is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    /// <summary>Reserves exactly two physical pixels on every side at any control size or display scale.</summary>
    /// <param name="bounds">Current row-action or toolbar-button rectangle.</param>
    /// <returns>The remaining image area, or empty when the control cannot contain the margins.</returns>
    internal static Rectangle ImageBounds(Rectangle bounds) => bounds.Width <= 4 || bounds.Height <= 4
        ? Rectangle.Empty : Rectangle.Inflate(bounds, -2, -2);

    /// <summary>Loads an embedded PNG into an independently owned bitmap, without depending on working-directory files.</summary>
    /// <param name="filename">Original PNG filename embedded by the application project.</param>
    /// <returns>A bitmap owned and disposed by the requesting window.</returns>
    internal static Bitmap Load(string filename)
    {
        using Stream stream = typeof(IconAssets).Assembly.GetManifestResourceStream("NetworkDriveRemapper.Icons." + filename)
            ?? throw new InvalidOperationException($"The embedded icon {filename} is missing.");
        using Image image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    /// <summary>Stretches the entire source canvas to exactly the destination bounds, both enlarging and shrinking.</summary>
    /// <param name="graphics">The current control/cell paint surface.</param>
    /// <param name="image">Unmodified original bitmap, including its transparency.</param>
    /// <param name="bounds">Actual current button bounds; no fixed icon size or source-size cap is applied.</param>
    /// <param name="enabled">False renders the same-sized image dimmed while the action is unavailable.</param>
    internal static void Draw(Graphics graphics, Image image, Rectangle bounds, bool enabled)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        GraphicsState state = graphics.Save();
        try
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            if (!enabled)
                attributes.SetColorMatrix(new ColorMatrix([
                    [0.3f, 0.3f, 0.3f, 0, 0], [0.59f, 0.59f, 0.59f, 0, 0],
                    [0.11f, 0.11f, 0.11f, 0, 0], [0, 0, 0, 0.45f, 0], [0, 0, 0, 0, 1]
                ]));
            graphics.DrawImage(image, bounds, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }
        finally
        {
            graphics.Restore(state);
        }
    }
}
