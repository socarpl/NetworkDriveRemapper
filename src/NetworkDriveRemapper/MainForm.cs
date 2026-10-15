using System.ComponentModel;
using NetworkDriveRemapper.Core;

namespace NetworkDriveRemapper;

/// <summary>Main window displaying A–Z, their network targets, and readable reservation labels.</summary>
public sealed class MainForm : Form
{
    // The service owns policy; controls only display snapshots and dispatch user actions.
    private readonly DriveMappingService service;
    private readonly Func<IWin32Window, string, bool> confirmForceRemoval;
    private readonly Action<IWin32Window, string> showRemovalError;
    private readonly BufferedDriveGrid grid = new();
    private readonly CheckBox onlyAvailable = new();
    private readonly StretchedIconButton refresh = new();
    private readonly Label footer = new();
    private readonly Icon applicationIcon = IconAssets.LoadApplicationIcon();
    private readonly Bitmap editIcon = IconAssets.Load("icon_edit2.png");
    private readonly Bitmap openIcon = IconAssets.Load("icon_open.png");
    private readonly Bitmap removeIcon = IconAssets.Load("icon_remove.png");
    private readonly Bitmap refreshIcon = IconAssets.Load("icon_refresh.png");
    private readonly ToolTip tooltips = new();
    private IReadOnlyList<DriveEntry> drives = [];
    private bool busy;

    /// <summary>Builds the main window and connects refresh, filter, mouse, and keyboard behavior.</summary>
    /// <param name="service">Mapping service using either Windows or a deterministic test adapter.</param>
    public MainForm(DriveMappingService service) : this(service, ConfirmForceRemoval, ShowRemovalError)
    {
    }

    /// <summary>Builds the real main window with injectable removal messages for non-destructive event-handler tests.</summary>
    /// <param name="service">Production or fake mapping service.</param>
    /// <param name="confirmForceRemoval">Owned Yes/No warning; true means the user explicitly selected Yes.</param>
    /// <param name="showRemovalError">Owned error message shown for ordinary errors or a failed forced retry.</param>
    internal MainForm(DriveMappingService service, Func<IWin32Window, string, bool> confirmForceRemoval,
        Action<IWin32Window, string> showRemovalError)
    {
        this.service = service;
        this.confirmForceRemoval = confirmForceRemoval;
        this.showRemovalError = showRemovalError;
        Text = "NetworkDriveRemapper";
        Icon = applicationIcon;
        Name = nameof(MainForm);
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        // Show all 26 rows on a tall desktop; keep smaller displays within their working area.
        int desktopHeight = Screen.PrimaryScreen?.WorkingArea.Height ?? 1080;
        ClientSize = new Size(820, Math.Min(950, desktopHeight - 100));
        MinimumSize = new Size(640, 530);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        KeyPreview = true;
        BuildLayout();

        Shown += async (_, _) => await RefreshDrivesAsync();
        refresh.Click += async (_, _) => await RefreshDrivesAsync();
        onlyAvailable.CheckedChanged += (_, _) => PopulateGrid();
        grid.CellDoubleClick += async (_, args) =>
        {
            if (args.RowIndex >= 0 && args.ColumnIndex < grid.Columns["OpenAction"].Index)
                await EditSelectedDriveAsync();
        };
        grid.CellContentClick += async (_, args) => await HandleRowActionAsync(args.RowIndex, args.ColumnIndex);
        grid.RowHeightChanged += (_, _) => UpdateActionColumnWidths();
        grid.RowsAdded += (_, _) => UpdateActionColumnWidths();
        grid.RowsRemoved += (_, _) => UpdateActionColumnWidths();
        grid.ColumnWidthChanged += (_, args) =>
        {
            if (args.Column.Name is "OpenAction" or "RemoveAction" or "EditAction")
                UpdateActionColumnWidths();
        };
        DpiChanged += (_, _) => UpdateActionColumnWidths();
        grid.KeyDown += async (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                args.Handled = true;
                args.SuppressKeyPress = true;
                if (grid.CurrentCell is { } cell && cell.ColumnIndex >= grid.Columns["OpenAction"].Index)
                    await HandleRowActionAsync(cell.RowIndex, cell.ColumnIndex);
                else
                    await EditSelectedDriveAsync();
            }
        };
        KeyDown += async (_, args) =>
        {
            if (args.KeyCode == Keys.F5)
            {
                args.Handled = true;
                await RefreshDrivesAsync();
            }
        };
        FormClosing += (_, args) => args.Cancel = busy;
    }

    /// <summary>Creates the drive/status grid with rightmost OPEN/REMOVE/EDIT icon actions and an icon-only Refresh button.</summary>
    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(new Label
        {
            Text = "NetworkDriveRemapper", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 20, FontStyle.Bold), ForeColor = Color.FromArgb(26, 47, 73)
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "Double-click an available letter or network drive to set its mapping.", Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(80, 90, 105), TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        onlyAvailable.Name = "OnlyAvailableCheckBox";
        onlyAvailable.Text = "Show only available";
        onlyAvailable.Checked = false;
        onlyAvailable.Dock = DockStyle.Fill;
        refresh.Name = "RefreshButton";
        refresh.Text = string.Empty;
        refresh.ActionImage = refreshIcon;
        refresh.AccessibleName = "Refresh drive letters (F5)";
        refresh.AccessibleDescription = "Reload the drive list. Shortcut: F5.";
        tooltips.SetToolTip(refresh, "Refresh drive letters (F5)");
        refresh.Dock = DockStyle.Fill;
        refresh.Margin = new Padding(0, 3, 0, 5);
        toolbar.Controls.Add(onlyAvailable, 0, 0);
        toolbar.Controls.Add(refresh, 1, 0);
        toolbar.SizeChanged += (_, _) => SizeRefreshButton(toolbar);
        layout.Controls.Add(toolbar, 0, 2);

        grid.Name = "DrivesGrid";
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.RowTemplate.Height = 27;
        grid.ColumnHeadersHeight = 34;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 240, 246);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(26, 47, 73);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(218, 232, 249);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(20, 40, 65);
        grid.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Drive", HeaderText = "Drive", Width = 90, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Location", HeaderText = "Network location / status", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(CreateActionColumn("OpenAction", "Open mapped drive in Explorer"));
        grid.Columns.Add(CreateActionColumn("RemoveAction", "Remove drive mapping"));
        grid.Columns.Add(CreateActionColumn("EditAction", "Edit drive mapping"));
        layout.Controls.Add(grid, 0, 3);
        footer.Name = "StatusLabel";
        footer.Dock = DockStyle.Fill;
        footer.TextAlign = ContentAlignment.MiddleLeft;
        footer.ForeColor = Color.FromArgb(80, 90, 105);
        layout.Controls.Add(footer, 0, 4);
        Controls.Add(layout);
    }

    /// <summary>Reads Windows on a worker thread so disconnected servers cannot freeze the window.</summary>
    private async Task RefreshDrivesAsync()
    {
        if (busy)
            return;
        SetBusy(true);
        footer.Text = "Reading drive letters…";
        try
        {
            drives = await Task.Run(() => service.GetDrives());
            PopulateGrid();
        }
        catch (Exception error)
        {
            footer.Text = "Could not refresh drive letters. Press Refresh to try again.";
            MessageBox.Show(this, error.Message, "Unable to read drives", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Reconciles the filtered inventory in place, preserving unchanged rows, selection, and scroll position.</summary>
    private void PopulateGrid()
    {
        char? selected = (grid.CurrentRow?.Tag as DriveEntry)?.Letter;
        int selectedColumn = grid.CurrentCell?.ColumnIndex ?? 0;
        int firstDisplayed = grid.FirstDisplayedScrollingRowIndex;
        DriveEntry[] visible = drives.Where(entry => !onlyAvailable.Checked || entry.Kind == DriveKind.Free).ToArray();
        var letters = visible.Select(entry => entry.Letter).ToHashSet();
        // Reuse existing native rows rather than clearing the grid when a modal closes or Refresh is clicked.
        for (int index = grid.Rows.Count - 1; index >= 0; index--)
            if (grid.Rows[index].Tag is not DriveEntry old || !letters.Contains(old.Letter))
                grid.Rows.RemoveAt(index);
        for (int index = 0; index < visible.Length; index++)
        {
            DriveEntry entry = visible[index];
            if (index >= grid.Rows.Count || (grid.Rows[index].Tag as DriveEntry)?.Letter != entry.Letter)
                grid.Rows.Insert(index, entry.DeviceName, entry.Description);
            DataGridViewRow row = grid.Rows[index];
            if (row.Tag is DriveEntry previous && previous == entry)
                continue;
            row.Tag = entry;
            row.Cells[0].Value = entry.DeviceName;
            row.Cells[1].Value = entry.Description;
            row.DefaultCellStyle.ForeColor = entry.Kind switch
            {
                DriveKind.Free => Color.FromArgb(24, 88, 166),
                DriveKind.Network => Color.FromArgb(26, 112, 72),
                _ => Color.FromArgb(110, 115, 122)
            };
            row.Cells[1].ToolTipText = entry.Description;
            var open = (DriveActionCell)row.Cells["OpenAction"];
            open.ActionImage = entry.Kind == DriveKind.Network ? openIcon : null;
            open.Value = open.ActionImage is null ? string.Empty : "OPEN";
            open.ToolTipText = open.ActionImage is null ? string.Empty : $"Open {entry.DeviceName} in Explorer";
            var remove = (DriveActionCell)row.Cells["RemoveAction"];
            remove.ActionImage = entry.Kind == DriveKind.Network && entry.CanMap ? removeIcon : null;
            remove.Value = remove.ActionImage is null ? string.Empty : "REMOVE";
            remove.ToolTipText = remove.ActionImage is null ? string.Empty : $"Remove mapping for {entry.DeviceName}";
            var edit = (DriveActionCell)row.Cells["EditAction"];
            edit.ActionImage = entry.CanMap ? editIcon : null;
            edit.Value = edit.ActionImage is null ? string.Empty : "EDIT";
            edit.ToolTipText = edit.ActionImage is null ? string.Empty : $"Edit mapping for {entry.DeviceName}";
            grid.InvalidateRow(index);
        }
        DataGridViewRow? selectedRow = grid.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(row => (row.Tag as DriveEntry)?.Letter == selected);
        if (selectedRow is not null && grid.CurrentCell != selectedRow.Cells[selectedColumn])
            grid.CurrentCell = selectedRow.Cells[selectedColumn];
        if (firstDisplayed >= 0 && grid.Rows.Count > 0)
        {
            int restored = Math.Min(firstDisplayed, grid.Rows.Count - 1);
            if (grid.FirstDisplayedScrollingRowIndex != restored)
                grid.FirstDisplayedScrollingRowIndex = restored;
        }
        footer.Text = $"{grid.Rows.Count} shown   |   {drives.Count(entry => entry.Kind == DriveKind.Free)} available"
            + $"   |   {drives.Count(entry => entry.Kind == DriveKind.Network)} network mappings";
    }

    /// <summary>Opens the editor only for free or known mapped letters, then reloads their state.</summary>
    private async Task EditSelectedDriveAsync()
    {
        if (busy || grid.CurrentRow?.Tag is not DriveEntry entry || !entry.CanMap)
            return;
        await EditDriveAsync(entry);
    }

    /// <summary>Opens the mapping editor for the clicked entry, independently of any previously selected row.</summary>
    private async Task EditDriveAsync(DriveEntry entry)
    {
        if (busy || !entry.CanMap)
            return;
        using var dialog = new MappingDialog(service, entry);
        dialog.ShowDialog(this);
        await RefreshDrivesAsync();
    }

    /// <summary>Routes single-click/keyboard icon actions, ignoring header, unavailable, or busy cells.</summary>
    private async Task HandleRowActionAsync(int rowIndex, int columnIndex)
    {
        if (busy || rowIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex < 0
            || grid.Rows[rowIndex].Tag is not DriveEntry entry)
            return;
        if (columnIndex == grid.Columns["EditAction"].Index && entry.CanMap)
            await EditDriveAsync(entry);
        else if (columnIndex == grid.Columns["RemoveAction"].Index && entry.Kind == DriveKind.Network && entry.CanMap)
            await RemoveDriveAsync(entry);
        else if (columnIndex == grid.Columns["OpenAction"].Index && entry.Kind == DriveKind.Network)
        {
            SetBusy(true);
            try
            {
                await Task.Run(() => service.OpenDrive(entry.Letter));
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "Unable to open drive", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }
    }

    /// <summary>Tries normal removal, offers a single confirmed forced retry only for error 2401, and refreshes in place.</summary>
    private async Task RemoveDriveAsync(DriveEntry entry)
    {
        SetBusy(true);
        try
        {
            try
            {
                await Task.Run(() => service.Remove(entry));
            }
            catch (Exception error) when (IsOpenFilesError(error))
            {
                if (confirmForceRemoval(this, BuildForceRemovalPrompt(entry, error)))
                    // Remove rereads ownership before this retry so a changed drive is never deliberately forced closed.
                    await Task.Run(() => service.Remove(entry, force: true));
            }
        }
        catch (Exception error)
        {
            showRemovalError(this, error.Message);
        }
        finally
        {
            SetBusy(false);
        }
        // Refresh on failure too: another application may have changed ownership since this row was displayed.
        await RefreshDrivesAsync();
    }

    /// <summary>Recognizes Windows ERROR_OPEN_FILES by native code through exception wrappers, never by message text.</summary>
    internal static bool IsOpenFilesError(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is Win32Exception { NativeErrorCode: 2401 })
                return true;
        return false;
    }

    /// <summary>Preserves the original error and identifies the drive/target, data-loss risks, and explicit force question.</summary>
    internal static string BuildForceRemovalPrompt(DriveEntry entry, Exception error) =>
        $"{error.Message}\n\n"
        + $"Force disconnect {entry.DeviceName} mapped to {entry.NetworkPath}?\n\n"
        + "Forcing disconnection can interrupt applications, file transfers, and pending requests. "
        + "Unsaved changes or pending writes may be lost. Save your work and close files first if possible.\n\n"
        + "Do you want to force disconnect this drive?";

    /// <summary>Shows an owned warning with Yes/No and No selected by default; only an explicit Yes authorizes force.</summary>
    private static bool ConfirmForceRemoval(IWin32Window owner, string message) =>
        MessageBox.Show(owner, message, "Unable to remove mapping", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>Reports an ordinary removal error or a failed forced retry without prompting again.</summary>
    private static void ShowRemovalError(IWin32Window owner, string message) =>
        MessageBox.Show(owner, message, "Unable to remove mapping", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>Creates an unsortable, non-resizable icon-button column with no inherited text padding.</summary>
    private DataGridViewButtonColumn CreateActionColumn(string name, string tooltip) => new()
    {
        Name = name, HeaderText = string.Empty, ToolTipText = tooltip, CellTemplate = new DriveActionCell(),
        Width = grid.RowTemplate.Height, MinimumWidth = 5, Resizable = DataGridViewTriState.False,
        SortMode = DataGridViewColumnSortMode.NotSortable, DefaultCellStyle = new DataGridViewCellStyle { Padding = Padding.Empty }
    };

    /// <summary>Keeps action columns sized to current row heights after resizing/scaling, without fixed-size icon caches.</summary>
    private void UpdateActionColumnWidths()
    {
        if (grid.IsDisposed || grid.Disposing || !grid.Columns.Contains("OpenAction")
            || !grid.Columns.Contains("RemoveAction") || !grid.Columns.Contains("EditAction"))
            return;
        int side = Math.Max(5, grid.Rows.Count == 0 ? grid.RowTemplate.Height : grid.Rows.Cast<DataGridViewRow>().Max(row => row.Height));
        if (grid.Columns["OpenAction"].Width != side)
            grid.Columns["OpenAction"].Width = side;
        if (grid.Columns["RemoveAction"].Width != side)
            grid.Columns["RemoveAction"].Width = side;
        if (grid.Columns["EditAction"].Width != side)
            grid.Columns["EditAction"].Width = side;
    }

    /// <summary>Makes Refresh a square based on its actual toolbar height, including current DPI-scaled margins.</summary>
    private void SizeRefreshButton(TableLayoutPanel toolbar)
    {
        int side = Math.Max(1, toolbar.ClientSize.Height - refresh.Margin.Vertical);
        if (toolbar.ColumnStyles[1].Width != side)
            toolbar.ColumnStyles[1].Width = side;
    }

    /// <summary>Guards overlapping actions without disabling/recoloring the entire grid for a short Explorer launch.</summary>
    private void SetBusy(bool value)
    {
        busy = value;
        refresh.Enabled = !value;
        onlyAvailable.Enabled = !value;
        // Every action checks busy. Keep selection/scrolling available and avoid an all-row disabled-state repaint.
        UseWaitCursor = value;
    }

    /// <summary>Disposes controls first, then the application icon, PNG source bitmaps, and tooltip owned by this window.</summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            tooltips.Dispose();
            applicationIcon.Dispose();
            editIcon.Dispose();
            openIcon.Dispose();
            removeIcon.Dispose();
            refreshIcon.Dispose();
        }
    }
}
