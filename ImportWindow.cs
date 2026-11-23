using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class ImportWindow : Form
{
    private static readonly double[] Scales = { 1, 10, 1000, 25.4 };
    private readonly TextBox stl = CreateField("STL file");
    private readonly TextBox template = CreateField("VENUS template (optional)");
    private readonly TextBox name = CreateField("Output name");
    private readonly TextBox output = CreateField("Output folder");
    private readonly ComboBox units = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button convert = CreateButton("Convert STL");
    private readonly Button openOutput = CreateButton("Open created folder");
    private readonly Label status = new Label { Dock = DockStyle.Fill };
    private string suggestedName = "";
    private string createdFolder;
    private bool busy;

    internal ImportWindow()
    {
        Text = "STL to Hamilton";
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 415);
        MinimumSize = new Size(555, 435);
        ShowInTaskbar = false;

        units.AccessibleName = "STL units";
        units.Items.AddRange(new object[] { "Millimetres", "Centimetres", "Metres", "Inches" });
        units.SelectedIndex = 0;
        output.Text = LabwareImport.DefaultOutput;
        openOutput.Enabled = false;

        var browseStl = CreateButton("Browse...");
        var browseTemplate = CreateButton("Browse...");
        var browseOutput = CreateButton("Browse...");
        browseStl.AccessibleName = "Browse for an STL file";
        browseTemplate.AccessibleName = "Browse for a VENUS template";
        browseOutput.AccessibleName = "Browse for the output folder";

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 13, Dock = DockStyle.Fill, Padding = new Padding(18)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int row = 0; row < layout.RowCount; row++)
        {
            layout.RowStyles.Add(row == 11 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        }

        AddField(layout, "STL file", stl, browseStl, 0);
        AddField(layout, "VENUS template (optional)", template, browseTemplate, 2);
        AddWide(layout, CreateLabel("Leave the template blank to create a standalone DirectX model."), 4);
        AddField(layout, "Output name", name, null, 5);
        AddField(layout, "STL units", units, null, 7);
        AddField(layout, "Output folder", output, browseOutput, 9);
        status.Padding = new Padding(3, 8, 3, 0);
        AddWide(layout, status, 11);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft
        };
        actions.Controls.Add(convert);
        actions.Controls.Add(openOutput);
        AddWide(layout, actions, 12);
        Controls.Add(layout);
        AcceptButton = convert;

        stl.TextChanged += delegate { SuggestName(); };
        browseStl.Click += delegate { ChooseFile(stl, "Open an STL model", "STL files (*.stl)|*.stl", ""); };
        browseTemplate.Click += delegate
        {
            ChooseFile(template, "Choose a VENUS labware template",
                "VENUS labware (*.rck;*.tml;*.ctr;*.lid)|*.rck;*.tml;*.ctr;*.lid", LabwareExport.DefaultSource);
        };
        browseOutput.Click += delegate
        {
            using (var picker = new FolderBrowserDialog { Description = "Choose the output folder.", SelectedPath = output.Text })
            {
                if (picker.ShowDialog(this) == DialogResult.OK)
                {
                    output.Text = picker.SelectedPath;
                }
            }
        };
        convert.Click += async delegate { await ConvertStl(); };
        openOutput.Click += delegate
        {
            try
            {
                Process.Start(new ProcessStartInfo(createdFolder) { UseShellExecute = true });
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        FormClosing += delegate(object sender, FormClosingEventArgs args) { args.Cancel = busy; };
    }

    private async Task ConvertStl()
    {
        string input = stl.Text.Trim();
        string selectedTemplate = template.Text.Trim();
        string packageName = name.Text.Trim();
        string target = output.Text.Trim();
        if (input.Length == 0 || packageName.Length == 0 || target.Length == 0)
        {
            status.Text = "Enter an STL file, an output name, and an output folder.";
            return;
        }

        double scale = Scales[units.SelectedIndex];
        busy = true;
        convert.Enabled = false;
        openOutput.Enabled = false;
        createdFolder = null;
        status.Text = "Converting the STL...";
        try
        {
            var result = await Task.Run(() => LabwareImport.Create(input, selectedTemplate, target, packageName, scale));
            createdFolder = result.Folder;
            status.Text = string.Format(CultureInfo.GetCultureInfo("en-US"), "Created {0} ({1:N0} {2}).",
                result.Definition == null ? Path.GetFileName(result.Model) :
                    Path.GetFileName(result.Definition) + " and " + Path.GetFileName(result.Model),
                result.Triangles, result.Triangles == 1 ? "triangle" : "triangles");
            if (result.TemplateMarkedInvalid)
            {
                status.Text += "\nThe template was marked invalid in VENUS. Review the copy in the Labware Editor.";
            }
        }
        catch (Exception error)
        {
            status.Text = "Conversion failed.";
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            busy = false;
            convert.Enabled = true;
            openOutput.Enabled = createdFolder != null;
        }
    }

    private void SuggestName()
    {
        string current = name.Text;
        if (current.Length == 0 || current == suggestedName)
        {
            try
            {
                suggestedName = Path.GetFileNameWithoutExtension(stl.Text.Trim());
                name.Text = suggestedName;
            }
            catch (ArgumentException)
            {
                // An incomplete path can contain invalid characters while it is being entered.
            }
        }
    }

    private void ChooseFile(TextBox field, string title, string filter, string initialDirectory)
    {
        using (var picker = new OpenFileDialog { Title = title, Filter = filter, InitialDirectory = initialDirectory })
        {
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                field.Text = picker.FileName;
            }
        }
    }

    private static TextBox CreateField(string accessibleName)
    {
        return new TextBox { Dock = DockStyle.Fill, AccessibleName = accessibleName };
    }

    private static Button CreateButton(string text)
    {
        return new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
    }

    private static Label CreateLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 1) };
    }

    private static void AddField(TableLayoutPanel layout, string caption, Control field, Button browse, int row)
    {
        AddWide(layout, CreateLabel(caption), row);
        if (browse == null)
        {
            AddWide(layout, field, row + 1);
        }
        else
        {
            layout.Controls.Add(field, 0, row + 1);
            layout.Controls.Add(browse, 1, row + 1);
        }
    }

    private static void AddWide(TableLayoutPanel layout, Control control, int row)
    {
        layout.Controls.Add(control, 0, row);
        layout.SetColumnSpan(control, 2);
    }
}
