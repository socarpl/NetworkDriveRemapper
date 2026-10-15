# NetworkDriveRemapper application icon

The application uses this icon for the executable in Explorer and for the main window's title bar,
taskbar, and task switching. The project embeds the ICO for window use and sets ApplicationIcon for
the executable. The solution's Assets folder includes the ICO, source PNG, these notes, and row/toolbar images.

- `NetworkDriveRemapper.ico`: 32-bit Windows icon with PNG frames at 16, 24, 32, 48, 64, 128, and 256 pixels.
- `NetworkDriveRemapper.png`: original generated artwork, preserving its alpha channel.

Generated with the built-in imagegen tool and its transparent-background option. The complete image
canvas was resized with bicubic interpolation and packaged into ICO without changing the artwork.
Windows LoadImageW successfully loaded each of the seven sizes. The artwork files are tracked in Git.

## Generation prompt

Use case: logo-brand. Asset type: Windows application icon for NetworkDriveRemapper, a utility to map, browse, replace, and remove network drive letters. Primary request: one polished, clean flat icon of a silver network drive with a small network connector and two bold blue curved arrows forming a partial circle to symbolize reconnecting/remapping. Simple strong silhouette, dark charcoal contours, silver gray and saturated blue palette, minimal subtle shading, thick readable shapes suitable for downscaling to 16 and 32 pixels. Single centered icon filling about 85% of a square canvas, balanced negative space around edges. Genuine transparent background. No text, no drive letters, no red X, no watermark, no app mockup, no tile background, no cast shadow outside icon, no multiple variants, no size chart.
