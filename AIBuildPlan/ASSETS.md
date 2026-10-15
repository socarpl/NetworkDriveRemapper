# Asset inventory and integrity

Use these files unchanged. All are copied from the committed baseline; hashes and byte lengths are
recorded in MANIFEST.json. Another model does not need image generation to reconstruct this application.

| Path | Purpose | Integration |
| --- | --- | --- |
| assets/icon_open.png | OPEN for mapped drive letters | Embedded PNG, drawn into live cell bounds minus 2px per side. |
| assets/icon_remove.png | REMOVE between OPEN and EDIT | Embedded PNG, mapped known targets only. |
| assets/icon_edit2.png | Current EDIT artwork | Embedded PNG, free and known mapped letters. |
| assets/icon_refresh.png | Refresh above inventory | Embedded PNG, borderless square button with 2px image inset. |
| assets/app-icon/NetworkDriveRemapper.ico | Main executable/window icon | 32-bit PNG frames at 16,24,32,48,64,128,256 pixels; ApplicationIcon and embedded managed resource. |
| assets/app-icon/NetworkDriveRemapper.png | Original application artwork | Preserve as source asset; not used for runtime action buttons. |
| assets/app-icon/README.md | Original generation provenance/prompt | Preserve for attribution of process and maintainability. |

In a recreated solution, copy the four action images to its root and the app-icon files to
assets/app-icon, matching baseline project paths. The package's assets/ prefix is a packaging location,
not a request to change application path conventions.

The obsolete icon_edit.png is intentionally not included in this current-asset directory. It remains
in the historical source ZIP because it was a tracked source asset, but the current application must
not embed/use it. The generated source PNG has an alpha channel; preserve it when handling the ICO.
Do not turn the transparent background into a black/white tile.

Runtime resource names:
- NetworkDriveRemapper.Icons.icon_open.png
- NetworkDriveRemapper.Icons.icon_remove.png
- NetworkDriveRemapper.Icons.icon_edit2.png
- NetworkDriveRemapper.Icons.icon_refresh.png
- NetworkDriveRemapper.Icons.Application.ico

The source project links the managed ICO under Assets/NetworkDriveRemapper.ico and sets the native
ApplicationIcon property to the same source. MainForm clones the resource icon and owns its lifetime.
The Visual Studio solution has an Assets solution folder listing ICO, original PNG, provenance notes
and the four current action images. Shell resource verification at baseline integration confirmed
all seven executable frames exactly match the supplied ICO bytes.
