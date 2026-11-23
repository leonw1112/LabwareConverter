using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

internal static class VenusTemplate
{
    private const string Instance = "default";
    private const string ProgramId = "HXCFGFILLib.HxCfgFile";

    internal static void CheckExtension(string extension)
    {
        DefinitionTypes(extension);
    }

    internal static bool Copy(string original, string destination, string modelPath, string name)
    {
        Type editor = Type.GetTypeFromProgID(ProgramId);
        if (editor == null)
        {
            throw new InvalidOperationException("Creating a labware definition requires the installed Hamilton VENUS configuration component.");
        }

        object instance = Activator.CreateInstance(editor);
        try
        {
            dynamic config = instance;
            short validity = config.LoadFile(original);
            if (validity != 0 && validity != 1)
            {
                throw new InvalidDataException("VENUS could not load the selected labware template.");
            }
            string kind = FindDefinition(config, Path.GetExtension(original));
            short version = config.GetDataDefVerNum(kind, Instance);
            string startFolder = Path.GetDirectoryName(original);
            string finalFolder = Path.GetDirectoryName(modelPath);
            RebaseReferences(config, kind, version, startFolder, finalFolder);

            string modelRelative = @".\" + Path.GetFileName(modelPath);
            config.PutStringValueInDataDef(kind, version, Instance, "3DModel", modelPath);
            config.PutStringValueInDataDef(kind, version, Instance, "3DModelRel", modelRelative);
            config.PutDoubleValueInDataDef(kind, version, Instance, "3DxOffset", 0.0);
            config.PutDoubleValueInDataDef(kind, version, Instance, "3DyOffset", 0.0);
            config.PutDoubleValueInDataDef(kind, version, Instance, "3DzOffset", 0.0);
            config.PutShortValueInDataDef(kind, version, Instance, "ReadOnly", (short)0);
            config.PutStringValueInDataDef(kind, version, Instance, "ViewName", name);
            config.StoreFile(destination, validity);

            if ((short)config.LoadFile(destination) != validity ||
                (string)config.GetDataDefValueAsString(kind, Instance, "3DModel") != modelPath ||
                (string)config.GetDataDefValueAsString(kind, Instance, "3DModelRel") != modelRelative)
            {
                throw new InvalidDataException("VENUS could not read back the saved model reference.");
            }
            return validity == 1;
        }
        finally
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }

    private static string[] DefinitionTypes(string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".rck": return new[] { "RECTRACK", "CIRCRACK" };
            case ".tml": return new[] { "TEMPLATE", "COMPOUNDTEMPLATE" };
            case ".ctr": return new[] { "CONTAINER" };
            case ".lid": return new[] { "COVER" };
            default: throw new ArgumentException("Choose a VENUS rack, carrier, container, or lid template (.rck, .tml, .ctr, or .lid).");
        }
    }

    private static string FindDefinition(dynamic config, string extension)
    {
        object types = null;
        object names = null;
        config.GetDataDefInstanceNames(ref types, ref names);
        var kinds = (object[])types;
        var instances = (object[])names;
        foreach (string expected in DefinitionTypes(extension))
        {
            for (int i = 0; i < kinds.Length; i++)
            {
                if (string.Equals((string)kinds[i], expected, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string)instances[i], Instance, StringComparison.OrdinalIgnoreCase))
                {
                    return (string)kinds[i];
                }
            }
        }
        throw new InvalidDataException("The selected file does not contain a VENUS definition of the expected type.");
    }

    private static void RebaseReferences(dynamic config, string kind, short version, string source, string target)
    {
        RebaseReference(config, kind, version, "Bitmap", "BitmapRel", source, target);
        RebaseReference(config, kind, version, "Image3D", "ImageRel", source, target);
        if (kind == "TEMPLATE" || kind == "COMPOUNDTEMPLATE")
        {
            int sites;
            int.TryParse(Field(config, kind, "Site.Cnt"), NumberStyles.Integer, CultureInfo.InvariantCulture, out sites);
            for (int i = 1; i <= sites; i++)
            {
                string field = "Site." + i.ToString(CultureInfo.InvariantCulture) + ".LabwareFile";
                RebaseReference(config, kind, version, field, field + "Rel", source, target);
            }
        }
        else if (kind == "RECTRACK" || kind == "CIRCRACK")
        {
            // Container types are indexed consecutively in the VENUS rack definition.
            for (int i = 1; ; i++)
            {
                string field = "Cntr." + i.ToString(CultureInfo.InvariantCulture) + ".file";
                if (!RebaseReference(config, kind, version, field, field + "Rel", source, target))
                {
                    break;
                }
            }
        }
    }

    private static bool RebaseReference(dynamic config, string kind, short version, string field, string relativeField,
        string source, string target)
    {
        string relative = Field(config, kind, relativeField);
        string fallback = Field(config, kind, field);
        if (relative.Length == 0 && fallback.Length == 0)
        {
            return false;
        }

        string existing = ResolveReference(source, relative) ??
            ResolveReference(LabwareExport.DefaultSource, fallback) ?? ResolveReference(source, fallback);
        if (existing != null)
        {
            var destination = new Uri(target.TrimEnd('\\', '/') + Path.DirectorySeparatorChar);
            var asset = new Uri(existing);
            Uri rebased = destination.MakeRelativeUri(asset);
            string reference = rebased.IsAbsoluteUri ? "" : Uri.UnescapeDataString(rebased.ToString()).Replace('/', '\\');
            if (reference.Length > 0 && !reference.StartsWith(@"..\", StringComparison.Ordinal))
            {
                reference = @".\" + reference;
            }
            config.PutStringValueInDataDef(kind, version, Instance, field, existing);
            config.PutStringValueInDataDef(kind, version, Instance, relativeField, reference);
        }
        return true;
    }

    private static string Field(dynamic config, string kind, string key)
    {
        string value = "";
        config.LookupDataDefValueAsString(kind, Instance, key, ref value);
        return value ?? "";
    }

    private static string ResolveReference(string parent, string reference)
    {
        if (reference.Length == 0)
        {
            return null;
        }
        string path;
        try
        {
            path = Path.GetFullPath(Path.Combine(parent, reference));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        return File.Exists(path) ? path : null;
    }
}
