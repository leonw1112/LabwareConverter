using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Media.Media3D;

internal static class DirectXModel
{
    // Keep individual meshes compatible with readers using 16-bit vertex indices.
    private const int FacesPerMesh = ushort.MaxValue / 3;

    private struct Face
    {
        internal Point3D A;
        internal Point3D B;
        internal Point3D C;
        internal Vector3D Normal;
    }

    internal static int Write(string stlPath, string modelPath, double millimetresPerUnit)
    {
        if (double.IsNaN(millimetresPerUnit) || double.IsInfinity(millimetresPerUnit) || millimetresPerUnit <= 0)
        {
            throw new ArgumentOutOfRangeException("millimetresPerUnit", "The scale to millimetres must be positive.");
        }

        Point3DCollection vertices = StlMesh.Read(stlPath, CancellationToken.None).Positions;
        var faces = new List<Face>(vertices.Count / 3);
        for (int index = 0; index < vertices.Count; index += 3)
        {
            // Swap Y and Z for DirectX's left-handed Y-up space, then reverse the winding.
            Point3D a = ConvertPoint(vertices[index], millimetresPerUnit);
            Point3D b = ConvertPoint(vertices[index + 2], millimetresPerUnit);
            Point3D c = ConvertPoint(vertices[index + 1], millimetresPerUnit);
            Vector3D normal = Vector3D.CrossProduct(b - a, c - a);
            if (normal.LengthSquared != 0)
            {
                normal.Normalize();
                faces.Add(new Face { A = a, B = b, C = c, Normal = normal });
            }
        }
        if (faces.Count == 0)
        {
            throw new InvalidDataException("The STL contains no non-degenerate triangles at this scale.");
        }

        using (var stream = new FileStream(modelPath, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream, Encoding.ASCII))
        {
            writer.WriteLine("xof 0303txt 0032");
            writer.WriteLine("Frame Labware {");
            for (int start = 0; start < faces.Count; start += FacesPerMesh)
            {
                WriteMesh(writer, faces, start, Math.Min(FacesPerMesh, faces.Count - start));
            }
            writer.WriteLine("}");
        }
        return faces.Count;
    }

    private static void WriteMesh(TextWriter writer, List<Face> faces, int start, int count)
    {
        writer.WriteLine("  Mesh {");
        writer.WriteLine("    {0};", count * 3);
        for (int index = 0; index < count; index++)
        {
            Face face = faces[start + index];
            WriteVector(writer, face.A.X, face.A.Y, face.A.Z, ",");
            WriteVector(writer, face.B.X, face.B.Y, face.B.Z, ",");
            WriteVector(writer, face.C.X, face.C.Y, face.C.Z, index + 1 == count ? ";" : ",");
        }

        writer.WriteLine("    {0};", count);
        for (int index = 0; index < count; index++)
        {
            int vertex = index * 3;
            writer.WriteLine("    3;{0},{1},{2};{3}", vertex, vertex + 1, vertex + 2, index + 1 == count ? ";" : ",");
        }

        writer.WriteLine("    MeshNormals {");
        writer.WriteLine("      {0};", count);
        for (int index = 0; index < count; index++)
        {
            Vector3D normal = faces[start + index].Normal;
            WriteVector(writer, normal.X, normal.Y, normal.Z, index + 1 == count ? ";" : ",");
        }
        writer.WriteLine("      {0};", count);
        for (int index = 0; index < count; index++)
        {
            writer.WriteLine("      3;{0},{0},{0};{1}", index, index + 1 == count ? ";" : ",");
        }
        writer.WriteLine("    }");

        writer.WriteLine("    MeshMaterialList {");
        writer.WriteLine("      1;");
        writer.WriteLine("      {0};", count);
        for (int index = 0; index < count; index++)
        {
            writer.WriteLine(index + 1 == count ? "      0;;" : "      0,");
        }
        writer.WriteLine("      Material LabwareSurface {");
        writer.WriteLine("        0.70;0.74;0.80;1.0;;");
        writer.WriteLine("        8.0;");
        writer.WriteLine("        0.20;0.20;0.20;;");
        writer.WriteLine("        0.0;0.0;0.0;;");
        writer.WriteLine("      }");
        writer.WriteLine("    }");
        writer.WriteLine("  }");
    }

    private static void WriteVector(TextWriter writer, double x, double y, double z, string separator)
    {
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "    {0:R};{1:R};{2:R};{3}", (float)x, (float)y, (float)z, separator));
    }

    private static Point3D ConvertPoint(Point3D point, double scale)
    {
        float x = (float)(point.X * scale);
        float y = (float)(point.Z * scale);
        float z = (float)(point.Y * scale);
        if (float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z))
        {
            throw new InvalidDataException("The scaled STL coordinates exceed the DirectX coordinate range.");
        }
        return new Point3D(x, y, z);
    }
}
