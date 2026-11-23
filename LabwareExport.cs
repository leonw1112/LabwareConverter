using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class LabwareExport
{
    internal static readonly string HamiltonRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Hamilton");
    internal static readonly string DefaultSource = Path.Combine(HamiltonRoot, "Labware");
    internal static readonly string DefaultOutput = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "STL");

    private static readonly string[] ModelExtensions = { ".hxx", ".x", ".gltf", ".glb", ".3ds" };
    private static readonly string[] DefinitionExtensions = { ".rck", ".tml", ".ctr", ".lid" };
    private static readonly string[] Keys = { "3DModelRel", "3DModel", "3DxOffset", "3DyOffset", "3DzOffset" };
    private static readonly Encoding HamiltonEncoding = Encoding.GetEncoding(1252);
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Regex TextFields = new Regex(
        @"^\s*(3DModelRel|3DModel|3D[xyz]Offset)\s*,\s*""([^""\r\n]*)""",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal sealed class Result
    {
        internal int Labware;
        internal int Models;
        internal int NoModel;
        internal int MissingModel;
        internal int Existing;
        internal int Errors;

        internal string Summary
        {
            get
            {
                return string.Format(CultureInfo.GetCultureInfo("en-US"),
                    "Exported {0:N0} labware STLs and {1:N0} standalone STLs.\n" +
                    "{2:N0} without a model, {3:N0} missing references, {4:N0} already exist, {5:N0} errors.",
                    Labware, Models, NoModel, MissingModel, Existing, Errors);
            }
        }
    }

    private readonly string source;
    private readonly string output;
    private readonly bool overwrite;
    private readonly HashSet<string> modelPaths;
    private readonly ILookup<string, string> modelsByName;

    private LabwareExport(string source, string output, bool overwrite, IEnumerable<string> models)
    {
        this.source = source;
        this.output = output;
        this.overwrite = overwrite;
        modelPaths = new HashSet<string>(models, PathComparer);
        modelsByName = models.ToLookup(Path.GetFileName, PathComparer);
    }

    internal static Result Run(string source, string output, bool overwrite, Action<int, int, string> progress)
    {
        source = Path.GetFullPath(source);
        output = Path.GetFullPath(output);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException("Source folder not found: " + source);
        }
        if (PathComparer.Equals(WithSeparator(source), WithSeparator(output)))
        {
            throw new IOException("Choose a separate output folder.");
        }

        string[] files = EnumerateFiles(source).OrderBy(path => path, PathComparer).ToArray();
        string[] definitions = files.Where(path => HasExtension(path, DefinitionExtensions)).ToArray();
        string[] models = files.Where(path => HasExtension(path, ModelExtensions)).ToArray();
        if (definitions.Length + models.Length == 0)
        {
            throw new InvalidDataException("The source folder contains no supported labware definitions or 3D models.");
        }

        var conversion = new LabwareExport(source, output, overwrite, models);
        var result = new Result();
        var report = new StringBuilder();
        AppendRow(report, "Source file", "3D model", "STL file", "Status", "Details");
        int total = definitions.Length + models.Length;
        int current = 0;
        string library = Path.Combine(HamiltonRoot, "Bin", "assimp-vc143-mt.dll");

        Directory.CreateDirectory(output);
        using (var assimp = new Assimp(library))
        {
            foreach (var target in conversion.GetTargets(definitions, "Labware"))
            {
                progress(current++, total, conversion.Relative(target.Key));
                conversion.Convert(assimp, target.Key, target.Value, true, report, result);
            }
            foreach (var target in conversion.GetTargets(models, "Models"))
            {
                progress(current++, total, conversion.Relative(target.Key));
                conversion.Convert(assimp, target.Key, target.Value, false, report, result);
            }
        }

        string reportPath = Path.Combine(output, "ExportReport.csv");
        var utf8 = new UTF8Encoding(true);
        byte[] reportBytes = utf8.GetPreamble().Concat(utf8.GetBytes(report.ToString())).ToArray();
        WriteAtomically(reportPath, reportBytes, true);
        progress(total, total, "");
        return result;
    }

    private void Convert(Assimp assimp, string input, string target, bool definition,
        StringBuilder report, Result result)
    {
        string model = definition ? null : input;
        string details = "";
        try
        {
            var offsets = new double[3];
            if (definition)
            {
                Dictionary<string, string> fields = ReadDefinition(input);
                string relative = Field(fields, "3DModelRel");
                string absolute = Field(fields, "3DModel");
                model = FindModel(input, relative, absolute);
                if (model == null)
                {
                    string reference = relative.Length > 0 ? relative : absolute;
                    if (reference.Length > 0)
                    {
                        result.MissingModel++;
                        AppendRow(report, Relative(input), reference, "", "Missing model reference",
                            "No unique match for the referenced file in the source folder.");
                    }
                    else
                    {
                        result.NoModel++;
                        AppendRow(report, Relative(input), "", "", "No 3D model",
                            "No 3D model could be associated with this definition.");
                    }
                    return;
                }
                for (int axis = 0; axis < offsets.Length; axis++)
                {
                    string offset = Field(fields, Keys[axis + 2]);
                    if (offset.Length > 0 && !double.TryParse(offset.Replace(',', '.'),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out offsets[axis]))
                    {
                        throw new InvalidDataException("Invalid labware offset: " + offset);
                    }
                }
                if (relative.Length == 0 && absolute.Length == 0)
                {
                    details = "Matched by file name. ";
                }
            }

            string relativeTarget = target.Substring(WithSeparator(output).Length);
            if (!overwrite && File.Exists(target))
            {
                result.Existing++;
                AppendRow(report, Relative(input), Relative(model), relativeTarget, "Already exists", details);
                return;
            }
            byte[] stl = assimp.ReadTriangles(model);
            details += Stl.Prepare(stl, offsets, out stl);
            if (!WriteAtomically(target, stl, overwrite))
            {
                result.Existing++;
                AppendRow(report, Relative(input), Relative(model), relativeTarget, "Already exists", details);
                return;
            }
            if (definition)
            {
                result.Labware++;
            }
            else
            {
                result.Models++;
            }
            AppendRow(report, Relative(input), Relative(model), relativeTarget, "Exported", details);
        }
        catch (Exception error)
        {
            if (!(error is IOException || error is InvalidDataException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is OverflowException))
            {
                throw;
            }
            result.Errors++;
            AppendRow(report, Relative(input), model == null ? "" : Relative(model), "", "Error", error.Message);
        }
    }

    private string FindModel(string definition, string relative, string absolute)
    {
        string folder = Path.GetDirectoryName(definition);
        foreach (string reference in new[] { relative, absolute }.Where(s => s.Length > 0))
        {
            string path = CanonicalReference(reference);
            foreach (string basePath in new[] { folder, source })
            {
                try
                {
                    string candidate = Path.GetFullPath(Path.Combine(basePath, path));
                    if (modelPaths.Contains(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // Older definitions can contain obsolete Windows paths.
                }
                catch (NotSupportedException)
                {
                }
            }
        }
        foreach (string reference in new[] { relative, absolute }.Where(s => s.Length > 0))
        {
            string path = CanonicalReference(reference);
            string name = path.Substring(path.LastIndexOf('\\') + 1);
            string local = Path.Combine(folder, name);
            if (modelPaths.Contains(local))
            {
                return local;
            }
            string[] matches = modelsByName[name].Take(2).ToArray();
            if (matches.Length == 1)
            {
                return matches[0];
            }
        }
        if (relative.Length == 0 && absolute.Length == 0)
        {
            foreach (string extension in ModelExtensions)
            {
                string candidate = Path.ChangeExtension(definition, extension);
                if (modelPaths.Contains(candidate))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    private static Dictionary<string, string> ReadDefinition(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string encoded = HamiltonEncoding.GetString(bytes);
        if (encoded.StartsWith("HxCfgFile", StringComparison.Ordinal) ||
            encoded.StartsWith("ï»¿HxCfgFile", StringComparison.Ordinal) ||
            (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe))
        {
            if (encoded[0] != 'H')
            {
                using (var reader = new StreamReader(new MemoryStream(bytes), HamiltonEncoding, true))
                {
                    encoded = reader.ReadToEnd();
                }
            }
            foreach (Match match in TextFields.Matches(encoded))
            {
                fields[match.Groups[1].Value] = match.Groups[2].Value.Trim();
            }
        }
        else if (bytes.Length >= 8 && (bytes[0] == 2 || bytes[0] == 3) && bytes[1] == 0)
        {
            // Binary HxCfg fields store the key and value as length-prefixed ANSI strings.
            foreach (string key in Keys)
            {
                string marker = ((char)key.Length) + key;
                int position = encoded.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (position < 0)
                {
                    continue;
                }
                int lengthOffset = position + marker.Length;
                if (lengthOffset >= bytes.Length || lengthOffset + 1 + bytes[lengthOffset] > bytes.Length)
                {
                    throw new InvalidDataException("Incomplete Hamilton configuration field: " + key);
                }
                fields[key] = HamiltonEncoding.GetString(bytes, lengthOffset + 1, bytes[lengthOffset]).Trim();
            }
        }
        else
        {
            throw new InvalidDataException("Unrecognized VENUS labware format.");
        }
        return fields;
    }

    private IEnumerable<KeyValuePair<string, string>> GetTargets(string[] inputs, string category)
    {
        var groups = inputs.ToLookup(path => Path.ChangeExtension(path, ".stl"), PathComparer);
        var used = new HashSet<string>(PathComparer);
        foreach (string input in inputs)
        {
            string stem = Path.ChangeExtension(input, ".stl");
            string name = groups[stem].Count() > 1 ? input + ".stl" : stem;
            string specific = name;
            for (int index = 2; !used.Add(name); index++)
            {
                name = Path.ChangeExtension(specific, "." + index + ".stl");
            }
            yield return new KeyValuePair<string, string>(input, Path.Combine(output, category, Relative(name)));
        }
    }

    internal static IEnumerable<string> EnumerateFiles(string root)
    {
        foreach (string path in Directory.EnumerateFiles(root))
        {
            yield return path;
        }
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }
            foreach (string path in EnumerateFiles(directory))
            {
                yield return path;
            }
        }
    }

    private static bool WriteAtomically(string target, byte[] data, bool overwrite)
    {
        string folder = Path.GetDirectoryName(target);
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, data);
            try
            {
                File.Move(temporary, target);
                return true;
            }
            catch (IOException)
            {
                if (!File.Exists(target))
                {
                    throw;
                }
                if (!overwrite)
                {
                    return false;
                }
                File.Replace(temporary, target, null);
                return true;
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void AppendRow(StringBuilder report, params string[] cells)
    {
        report.AppendLine(string.Join(";", cells.Select(cell => "\"" + cell.Replace("\"", "\"\"") + "\"")));
    }

    private static bool HasExtension(string path, string[] extensions)
    {
        return extensions.Contains(Path.GetExtension(path), PathComparer);
    }

    private static string Field(Dictionary<string, string> fields, string key)
    {
        string value;
        return fields.TryGetValue(key, out value) ? value : "";
    }

    private static string CanonicalReference(string value)
    {
        return Regex.Replace(value.Replace('/', '\\'), @"\\{2,}", @"\");
    }

    private static string WithSeparator(string path)
    {
        return path.TrimEnd('\\', '/') + "\\";
    }

    private string Relative(string path)
    {
        return path.Substring(WithSeparator(source).Length);
    }
}
