using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        if (args.Length != 0)
        {
            if ((args.Length != 3 && args.Length != 4) || args[0] != "--batch" ||
                (args.Length == 4 && args[3] != "--overwrite"))
            {
                return 2;
            }
            try
            {
                var result = LabwareExport.Run(args[1], args[2], args.Length == 4,
                    delegate { });
                return result.Errors == 0 ? 0 : 1;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 2;
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ExportWindow());
        return 0;
    }
}

internal sealed class ExportWindow : Form
{
    private readonly TextBox source = new TextBox { Dock = DockStyle.Fill, Text = LabwareExport.DefaultSource };
    private readonly TextBox target = new TextBox { Dock = DockStyle.Fill, Text = LabwareExport.DefaultOutput };
    private readonly Button browseSource = CreateButton("Browse...");
    private readonly Button browseTarget = CreateButton("Browse...");
    private readonly Button export = CreateButton("Export STLs");
    private readonly Button openOutput = CreateButton("Open output folder");
    private readonly Button openViewer = CreateButton("STL Viewer...");
    private readonly CheckBox overwrite = new CheckBox
    {
        Text = "Overwrite existing STL files", AutoSize = true, Dock = DockStyle.Fill,
        Margin = new Padding(0, 8, 0, 8)
    };
    private readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Height = 16 };
    private ModelViewer viewer;
    private bool busy;

    internal ExportWindow()
    {
        Text = "Hamilton Labware to STL";
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(660, 286);
        MinimumSize = new Size(560, 302);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 8, Dock = DockStyle.Fill, Padding = new Padding(18),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int row = 0; row < layout.RowCount; row++)
        {
            layout.RowStyles.Add(row == 6
                ? new RowStyle(SizeType.Percent, 100)
                : new RowStyle(SizeType.AutoSize));
        }

        AddWide(layout, CreateLabel("Hamilton labware folder"), 0);
        layout.Controls.Add(source, 0, 1);
        layout.Controls.Add(browseSource, 1, 1);
        AddWide(layout, CreateLabel("Output folder"), 2);
        layout.Controls.Add(target, 0, 3);
        layout.Controls.Add(browseTarget, 1, 3);
        AddWide(layout, overwrite, 4);
        AddWide(layout, progress, 5);
        status.Text = "Ready.";
        status.Padding = new Padding(0, 7, 0, 2);
        AddWide(layout, status, 6);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        actions.Controls.Add(export);
        actions.Controls.Add(openOutput);
        actions.Controls.Add(openViewer);
        AddWide(layout, actions, 7);
        Controls.Add(layout);
        AcceptButton = export;

        source.AccessibleName = "Hamilton labware folder";
        target.AccessibleName = "Output folder";
        browseSource.AccessibleName = "Browse for the Hamilton labware folder";
        browseTarget.AccessibleName = "Browse for the output folder";
        browseSource.Click += delegate { ChooseFolder(source, "Choose the Hamilton labware folder."); };
        browseTarget.Click += delegate { ChooseFolder(target, "Choose the output folder for the STL files."); };
        openOutput.Click += delegate { OpenOutput(); };
        openViewer.Click += async delegate { await OpenViewer(); };
        export.Click += async delegate { await Export(); };
        target.TextChanged += delegate { openOutput.Enabled = !busy && Directory.Exists(target.Text.Trim()); };
        openOutput.Enabled = Directory.Exists(target.Text);
        FormClosing += delegate(object sender, FormClosingEventArgs eventArgs) { eventArgs.Cancel = busy; };
    }

    private async Task Export()
    {
        string input = source.Text.Trim();
        string output = target.Text.Trim();
        if (input.Length == 0 || output.Length == 0)
        {
            status.Text = "Enter a source folder and an output folder.";
            return;
        }
        bool replace = overwrite.Checked;
        SetBusy(true);
        status.AutoEllipsis = true;
        status.Text = "Scanning the source folder...";
        progress.Value = 0;
        try
        {
            var result = await Task.Run(() => LabwareExport.Run(input, output, replace, (done, total, name) =>
            {
                if (done % 12 == 0)
                {
                    BeginInvoke(new Action(() =>
                    {
                        progress.Value = 100 * done / total;
                        status.Text = string.Format("{0:N0} / {1:N0}   {2}", done, total, name);
                    }));
                }
            }));
            progress.Value = 100;
            status.AutoEllipsis = false;
            status.Text = result.Summary;
            if (viewer != null && !viewer.IsDisposed)
            {
                await viewer.RefreshLibrary(output);
            }
        }
        catch (Exception error)
        {
            status.Text = "Export failed.";
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        source.Enabled = target.Enabled = browseSource.Enabled = browseTarget.Enabled =
            export.Enabled = overwrite.Enabled = !value;
        openOutput.Enabled = !value && Directory.Exists(target.Text.Trim());
    }

    private void OpenOutput()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.GetFullPath(target.Text.Trim())) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OpenViewer()
    {
        if (viewer == null || viewer.IsDisposed)
        {
            viewer = new ModelViewer();
            viewer.Show(this);
        }
        else
        {
            if (viewer.WindowState == FormWindowState.Minimized)
            {
                viewer.WindowState = FormWindowState.Normal;
            }
            viewer.Activate();
        }
        await viewer.RefreshLibrary(target.Text.Trim());
    }

    private static void ChooseFolder(TextBox field, string description)
    {
        using (var picker = new FolderBrowserDialog { Description = description, SelectedPath = field.Text })
        {
            if (picker.ShowDialog(field.FindForm()) == DialogResult.OK)
            {
                field.Text = picker.SelectedPath;
            }
        }
    }

    private static Button CreateButton(string text)
    {
        return new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
    }

    private static Label CreateLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 3, 0, 0) };
    }

    private static void AddWide(TableLayoutPanel layout, Control control, int row)
    {
        layout.Controls.Add(control, 0, row);
        layout.SetColumnSpan(control, 2);
    }
}
