using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

internal sealed class ModelViewer : Form
{
    private sealed class ModelFile
    {
        internal readonly string Path;
        internal readonly string Name;

        internal ModelFile(string path, string name)
        {
            Path = path;
            Name = name;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    private readonly TextBox filter = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Filter by name or folder" };
    private readonly ListBox files = new ListBox
    {
        Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true,
        AccessibleName = "Exported STL files"
    };
    private readonly Label fileCount = CreateLabel("");
    private readonly Label modelName = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label metadata = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Button fit = CreateButton("Fit view");
    private readonly PreviewViewport preview = new PreviewViewport();
    private ModelFile[] library = new ModelFile[0];
    private string outputFolder;
    private string currentPath;
    private CancellationTokenSource meshRequest;
    private int scanVersion;

    internal ModelViewer()
    {
        Text = "STL Viewer";
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1020, 650);
        MinimumSize = new Size(730, 440);
        StartPosition = FormStartPosition.CenterParent;

        var open = CreateButton("Open STL...");
        var refresh = CreateButton("Refresh list");
        var toolbar = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 12)
        };
        toolbar.Controls.AddRange(new Control[] { open, refresh, fit });
        fit.Enabled = false;
        open.Click += async delegate { await OpenFile(); };
        refresh.Click += async delegate { await RefreshLibrary(outputFolder); };
        fit.Click += delegate { preview.Fit(); };

        var columns = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        columns.Controls.Add(CreateLibraryPanel(), 0, 0);
        columns.Controls.Add(CreatePreviewPanel(), 1, 0);

        var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(columns, 0, 1);
        Controls.Add(layout);

        filter.TextChanged += delegate { ApplyFilter(); };
        files.SelectedIndexChanged += async delegate
        {
            var selected = files.SelectedItem as ModelFile;
            if (selected != null && !string.Equals(selected.Path, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                await OpenMesh(selected.Path);
            }
        };
        FormClosed += delegate
        {
            scanVersion++;
            if (meshRequest != null)
            {
                meshRequest.Cancel();
            }
        };
    }

    internal async Task RefreshLibrary(string folder)
    {
        outputFolder = folder;
        int version = ++scanVersion;
        fileCount.Text = "Scanning output folder...";
        try
        {
            ModelFile[] models = await Task.Run(() => FindModels(folder));
            if (version == scanVersion)
            {
                library = models;
                ApplyFilter();
            }
        }
        catch (Exception)
        {
            if (version == scanVersion)
            {
                library = new ModelFile[0];
                ApplyFilter();
                fileCount.Text = "Could not read the output folder.";
            }
        }
    }

    private static ModelFile[] FindModels(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return new ModelFile[0];
        }
        string root = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return LabwareExport.EnumerateFiles(root)
            .Where(path => string.Equals(Path.GetExtension(path), ".stl", StringComparison.OrdinalIgnoreCase))
            .Select(path => new ModelFile(path, path.Substring(root.Length)))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void ApplyFilter()
    {
        string query = filter.Text.Trim();
        ModelFile[] matches = library.Where(file =>
            file.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        files.BeginUpdate();
        try
        {
            files.Items.Clear();
            files.Items.AddRange(matches);
            files.SelectedIndex = Array.FindIndex(matches,
                file => string.Equals(file.Path, currentPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            files.EndUpdate();
        }
        fileCount.Text = string.Format(CultureInfo.GetCultureInfo("en-US"),
            "{0:N0} of {1:N0} STL files", matches.Length, library.Length);
    }

    private async Task OpenFile()
    {
        using (var picker = new OpenFileDialog
        {
            Title = "Open an STL model",
            Filter = "STL files (*.stl)|*.stl|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(outputFolder) ? Path.GetFullPath(outputFolder) : ""
        })
        {
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                files.ClearSelected();
                await OpenMesh(picker.FileName);
            }
        }
    }

    private async Task OpenMesh(string path)
    {
        if (meshRequest != null)
        {
            meshRequest.Cancel();
        }
        var request = new CancellationTokenSource();
        meshRequest = request;
        currentPath = path;
        fit.Enabled = false;
        modelName.Text = Path.GetFileName(path);
        metadata.Text = "";
        preview.ShowMessage("Loading STL...");
        try
        {
            var mesh = await Task.Run(() => StlMesh.Read(path, request.Token), request.Token);
            if (request.IsCancellationRequested)
            {
                return;
            }
            preview.ShowMesh(mesh);
            fit.Enabled = true;
            var bounds = mesh.Bounds;
            int triangles = mesh.Positions.Count / 3;
            metadata.Text = string.Format(CultureInfo.GetCultureInfo("en-US"),
                "{0:N0} {1}   |   X × Y × Z: {2:0.###} × {3:0.###} × {4:0.###}",
                triangles, triangles == 1 ? "triangle" : "triangles", bounds.SizeX, bounds.SizeY, bounds.SizeZ);
        }
        catch (Exception error)
        {
            if (!request.IsCancellationRequested)
            {
                currentPath = null;
                files.ClearSelected();
                preview.ShowMessage("Could not open the STL file.\n" + error.Message);
            }
        }
        finally
        {
            if (meshRequest == request)
            {
                meshRequest = null;
            }
            request.Dispose();
        }
    }

    private Control CreateLibraryPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 5, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 12, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(CreateLabel("Exported files"), 0, 0);
        panel.Controls.Add(CreateLabel("Filter by name or folder"), 0, 1);
        panel.Controls.Add(filter, 0, 2);
        panel.Controls.Add(files, 0, 3);
        panel.Controls.Add(fileCount, 0, 4);
        return panel;
    }

    private Control CreatePreviewPanel()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, Dock = DockStyle.Fill };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(CreateLabel("Left drag to orbit  |  Right drag to pan  |  Scroll to zoom"), 0, 0);
        panel.Controls.Add(new ElementHost { Dock = DockStyle.Fill, Child = preview }, 0, 1);
        modelName.Height = Font.Height + 4;
        metadata.Height = Font.Height + 4;
        panel.Controls.Add(modelName, 0, 2);
        panel.Controls.Add(metadata, 0, 3);
        return panel;
    }

    private static Label CreateLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 3, 3, 5) };
    }

    private static Button CreateButton(string text)
    {
        return new Button { Text = text, AutoSize = true, Padding = new Padding(5, 0, 5, 0) };
    }
}
