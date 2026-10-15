namespace NetworkDriveRemapper;

/// <summary>A double-buffered drive grid that paints complete frames without flashing its background.</summary>
internal sealed class BufferedDriveGrid : DataGridView
{
    /// <summary>Enables the native control's protected buffering option for selection, scrolling, and inventory updates.</summary>
    internal BufferedDriveGrid() => DoubleBuffered = true;
}

/// <summary>A borderless button whose icon scales with its current size, leaving a two-pixel margin.</summary>
internal sealed class StretchedIconButton : Button
{
    /// <summary>Keeps normal button keyboard/accessibility behavior while painting only the background and image.</summary>
    internal StretchedIconButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Gets or sets the window-owned source bitmap; the button does not dispose this shared image.</summary>
    internal Image? ActionImage { get; set; }

    /// <summary>Draws only the scaled icon with two-pixel margins and optional keyboard focus; no native button frame is painted.</summary>
    protected override void OnPaint(PaintEventArgs pevent)
    {
        // ButtonBase can suppress OnPaintBackground; fill explicitly so transparent PNG margins stay clean.
        // Calling Button.OnPaint would add the unwanted native frame, including through transparent image pixels.
        using var background = new SolidBrush(Parent?.BackColor ?? SystemColors.Control);
        pevent.Graphics.FillRectangle(background, ClientRectangle);
        if (ActionImage is not null)
            IconAssets.Draw(pevent.Graphics, ActionImage, IconAssets.ImageBounds(ClientRectangle), Enabled);
        if (Focused && ShowFocusCues && ClientSize.Width > 6 && ClientSize.Height > 6)
            ControlPaint.DrawFocusRectangle(pevent.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
    }
}

/// <summary>A clickable grid button with an icon scaled to the current row/button bounds, or no action when blank.</summary>
internal sealed class DriveActionCell : DataGridViewButtonCell
{
    /// <summary>Gets or sets the window-owned source image; null hides the button and its clickable content.</summary>
    internal Image? ActionImage { get; set; }

    /// <summary>Copies the source-image reference when WinForms clones the column's cell template.</summary>
    public override object Clone()
    {
        var clone = (DriveActionCell)base.Clone();
        clone.ActionImage = ActionImage;
        return clone;
    }

    /// <summary>Uses the entire button rectangle for hit testing; blank action cells have no clickable content.</summary>
    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) =>
        ActionImage is null || rowIndex < 0 ? Rectangle.Empty : IconAssets.ImageBounds(new Rectangle(Point.Empty, GetSize(rowIndex)));

    /// <summary>Paints the selected cell background and a borderless icon with exactly two pixels of surrounding space.</summary>
    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex,
        DataGridViewElementStates cellState, object? value, object? formattedValue, string? errorText,
        DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
        // Excluding content flags prevents an empty action from leaving a blank native button behind.
        base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, string.Empty, string.Empty,
            errorText, cellStyle, advancedBorderStyle,
            paintParts & (DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground | DataGridViewPaintParts.Border));
        if (ActionImage is null || (paintParts & DataGridViewPaintParts.ContentForeground) == 0)
            return;
        Rectangle button = IconAssets.ImageBounds(cellBounds);
        if (button.Width <= 0 || button.Height <= 0)
            return;
        bool enabled = DataGridView?.Enabled == true;
        IconAssets.Draw(graphics, ActionImage, button, enabled);
        if (DataGridView?.Focused == true && DataGridView.CurrentCell == this && button.Width > 6 && button.Height > 6)
            ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(button, -3, -3));
    }
}
