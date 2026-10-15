# Windows, layout and visual references

All measurements below are baseline logical WinForms values at the initial design DPI unless explicitly
stated physical pixels. Use PerMonitorV2 and AutoScaleMode.Dpi. Native borders/fonts can vary by Windows
theme. Preserve layout relationships and supplied images rather than drawing screenshots as UI.

## MainForm

- Title and heading: NetworkDriveRemapper. Segoe UI 10 body, heading 20 bold.
- Client width 820; height min(950, primary working-area height - 100). Minimum window size 640 x 530.
- White background, centered, resizable. Outer table padding 20; rows 44, 36, 44, remaining space, 36.
- Instruction: Double-click an available letter or network drive to set its mapping.
- Toolbar: Show only available at left, square icon-only Refresh at right. Its side follows actual
  toolbar height minus vertical margins; Refresh margins are left/right 0, top 3, bottom 5.
- Grid columns: Drive (width 90), Network location / status (fill), OPEN, REMOVE, EDIT. Action headers
  are blank; action columns are unsortable and not user-resizable. Action column widths follow the
  largest current row height (row-template height when empty). No row headers.
- Default row height 27; header height 34. Read-only, single full-row selection, no row insertion,
  deletion, user row resizing, or sorting. Data text padding 8 left/right; action padding zero.
- BorderStyle.None, CellBorderStyle.None, ColumnHeadersBorderStyle.None. Double-buffered grid.
- Header RGB 235,240,246; heading/header text 26,47,73; selection background 218,232,249;
  selection text 20,40,65. Free text 24,88,166; network text 26,112,72; occupied text 110,115,122.
- Footer format: {shown} shown   |   {free} available   |   {network} network mappings.
  Counts for free/network use the full cached inventory, even when filtering.
- Main window uses the generated application icon. The executable embeds all seven ICO sizes.

## Exact icon geometry

Current images are in [assets](assets/icon_edit2.png). OPEN -> REMOVE -> EDIT on mapped rows, EDIT alone
at far right on free rows. An absent action has no painted icon and empty hit-test content.

For a cell rectangle (x,y,w,h), image bounds are (x+2,y+2,w-4,h-4). The toolbar icon uses the same inset
inside its current client rectangle. Return empty if width/height <=4. The two-pixel inset remains
**two physical pixels at every DPI**, not an additional DPI-scaled margin.

Stretch the entire source canvas to those bounds using high-quality bicubic interpolation, mirrored
edges and transparency. Grow as well as shrink; do not clamp to PNG dimensions. Do not draw a native
button rectangle behind either row icons or Refresh. Refresh explicitly fills the parent background.
Keyboard focus indicators inside the image area are permitted and implemented in the baseline.
The row hit-test rectangle equals the image bounds. Keep Enter and native Space behavior.

## MappingDialog

- Title: Map network drive — {letter}: ; heading: Network mapping for {letter}:.
- Segoe UI 10; heading 17 bold. Client 720 x 560; minimum window 680 x 560; center parent.
- Owned modal, white, no taskbar entry, no maximize/minimize.
- Outer padding 20; rows 48,24,36,24,64,38,38,36,remaining,44,48.
- Target path label/input, then This drive letter is available. or OK replaces the current network mapping.
- Type of login group: **Credentials first, Anonymous second**, auto-sized; tab indices 0 and 1.
- Free letters: Credentials checked; current qualified Windows username prefilled; blank masked password.
- Existing mappings: Anonymous checked; path prefilled; username/password blank and disabled.
- Username/password input rows have a 100-wide label column; textboxes fill the rest.
- Target placeholder: \\server\share or \\server\share\folder.
- Username placeholder: DOMAIN\user or user@domain.
- Checkbox: Open directory after mapping, checked initially.
- Status area: wrapping/ellipsized feedback with hidden AutoSize Read more... beneath it; marquee strip.
- Buttons: Test connection (155x34), Open in Explorer (170x34), Cancel (110x34), OK (110x34),
  with margins 0,4,8,4. Initial target focus; OK is AcceptButton and Cancel is CancelButton.
- Bottom two-line note is specified in STRINGS.md. Retain the direct-Explorer explanation.

## CredentialsHelpDialog

Owned modal titled Anonymous access and credentials, client 790x730, minimum 640x520.
Segoe UI 11; heading 19 bold; outer padding 24, heading row 52 and close row 50.
Scrollable paragraphs use selectable borderless read-only RichTextBox controls with inline bold
and Consolas 11 code spans. Separate LinkLabels follow the first three paragraphs; no embedded browser.
Paragraph height follows wrapped content. Close is 110x34; Enter/Escape both close only help.
Preserve exact wording and emphasis from [STRINGS.md](STRINGS.md).

## CredentialConflictDialog

Owned modal titled Network login conflict — Windows error 1219, client 1000x660, minimum 950x620,
Segoe UI 10, white background, no taskbar/maximize/minimize.

Show original error in a scrolling read-only area, server/login explanation, then a read-only connection
grid: Drive (78), Server / share (fill, minimum 200), Login in use (210), Status (130),
Open references (140). Unknown device/account/reference values are displayed explicitly.
Below it: discovery notes, bold continuation prompt, then the three exact buttons from STRINGS.md.
Button widths 225,425,170, height 36, margins 0,5,8,5. Keep all three visible at minimum size.
Cancel is the default resolution. No in the destructive warning returns to this same review.

## Screenshots and provenance

| File | Scenario |
| --- | --- |
| [main.png](screenshots/main.png) | A-Z inventory with a fake local C: and fake remembered mapped Z:, current icons and application title/icon. |
| [mapping-new-drive.png](screenshots/mapping-new-drive.png) | Available A: with current default Credentials and blank password. EXAMPLE-PC\SampleUser is substituted AFTER verifying the actual WindowsIdentity default, to avoid recording a real account. |
| [mapping-anonymous.png](screenshots/mapping-anonymous.png) | Existing Z: mapping, initial Anonymous mode, but Credentials still appears first. |
| [mapping-credentials.png](screenshots/mapping-credentials.png) | Explicit sample DOMAIN\alice and masked sample password; these are illustrative inputs, not defaults/secrets. |
| [mapping-access-denied.png](screenshots/mapping-access-denied.png) | Error-5 feedback and contextual link. |
| [credentials-help.png](screenshots/credentials-help.png) | Formatted exact explanation; scrolling may hide later paragraphs. |
| [credential-conflict.png](screenshots/credential-conflict.png) | Fake 1219 review with mapped/deviceless connections. |
| [credential-conflict-minimum.png](screenshots/credential-conflict-minimum.png) | Same review at minimum supported size. |
| [main-icons-row-16.png](screenshots/main-icons-row-16.png) | Smaller-row regression rendering. |
| [main-icons-row-48.png](screenshots/main-icons-row-48.png) | Larger-row rendering showing upscaling and 2px margins. |
| [main-icons-scaled.png](screenshots/main-icons-scaled.png) | 1.5x control-scale regression rendering. |

Screenshots are offscreen WinForms DrawToBitmap renders using fake mapping data. The ten existing
renders were produced by the baseline regression runner; the new-drive image was captured from that
same baseline using a temporary fake-backed capture harness. Native message boxes for forced removal
and server-drop confirmation are specified by text/buttons/defaults instead of a staged screenshot.
Do not infer their behavior from unrelated modal images.
