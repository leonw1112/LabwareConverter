using System;
using System.IO;

internal static class LabwareImport
{
    internal static readonly string DefaultOutput =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HamiltonOutput");

    internal sealed class Result
    {
        internal string Folder;
        internal string Model;
        internal string Definition;
        internal int Triangles;
        internal bool TemplateMarkedInvalid;
    }

    internal static Result Create(string stlPath, string templatePath, string outputFolder, string name, double scale)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal))
        {
            throw new ArgumentException("Enter a valid Windows file name for the labware.");
        }

        stlPath = Path.GetFullPath(stlPath);
        outputFolder = Path.GetFullPath(outputFolder);
        bool hasTemplate = !string.IsNullOrWhiteSpace(templatePath);
        string extension = "";
        if (hasTemplate)
        {
            templatePath = Path.GetFullPath(templatePath);
            extension = Path.GetExtension(templatePath).ToLowerInvariant();
            VenusTemplate.CheckExtension(extension);
        }

        string finalFolder = Path.Combine(outputFolder, name);
        if (Directory.Exists(finalFolder) || File.Exists(finalFolder))
        {
            throw new IOException("An output item already exists at " + finalFolder + ". Choose a different name or folder.");
        }

        string staging = Path.Combine(outputFolder, "." + Guid.NewGuid().ToString("N"));
        string modelFile = name + ".x";
        string stagedModel = Path.Combine(staging, modelFile);
        string stagedDefinition = hasTemplate ? Path.Combine(staging, name + extension) : null;
        var result = new Result
        {
            Folder = finalFolder,
            Model = Path.Combine(finalFolder, modelFile),
            Definition = hasTemplate ? Path.Combine(finalFolder, name + extension) : null
        };

        Directory.CreateDirectory(staging);
        try
        {
            result.Triangles = DirectXModel.Write(stlPath, stagedModel, scale);
            if (hasTemplate)
            {
                result.TemplateMarkedInvalid = !VenusTemplate.Copy(templatePath, stagedDefinition, result.Model, name);
            }
            Directory.Move(staging, finalFolder);
            return result;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                File.Delete(stagedModel);
                if (stagedDefinition != null)
                {
                    File.Delete(stagedDefinition);
                }
                Directory.Delete(staging);
            }
        }
    }
}
