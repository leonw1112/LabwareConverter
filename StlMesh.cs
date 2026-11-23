using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Media.Media3D;

internal static class StlMesh
{
    internal static MeshGeometry3D Read(string path, CancellationToken cancellation)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        using (var reader = new BinaryReader(stream))
        {
            Point3DCollection points = null;
            if (stream.Length >= Stl.HeaderSize)
            {
                stream.Position = Stl.HeaderSize - sizeof(uint);
                uint count = reader.ReadUInt32();
                // Binary STL headers can also start with "solid".
                if (stream.Length == Stl.HeaderSize + Stl.TriangleSize * (long)count)
                {
                    points = ReadBinary(reader, checked((int)count), cancellation);
                }
            }
            if (points == null)
            {
                stream.Position = 0;
                using (var text = new StreamReader(stream))
                {
                    points = ReadAscii(text, cancellation);
                }
            }
            if (points.Count == 0)
            {
                throw new InvalidDataException("The STL file contains no triangles.");
            }
            var mesh = new MeshGeometry3D { Positions = points };
            mesh.Freeze();
            return mesh;
        }
    }

    private static Point3DCollection ReadBinary(BinaryReader reader, int count, CancellationToken cancellation)
    {
        var points = new Point3DCollection(checked(count * 3));
        for (int i = 0; i < count; i++)
        {
            if (i % 1024 == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }
            reader.ReadSingle(); // Stored normal.
            reader.ReadSingle();
            reader.ReadSingle();
            for (int vertex = 0; vertex < 3; vertex++)
            {
                points.Add(new Point3D(
                    Coordinate(reader.ReadSingle()), Coordinate(reader.ReadSingle()), Coordinate(reader.ReadSingle())));
            }
            reader.ReadUInt16(); // Attribute byte count.
        }
        return points;
    }

    private static Point3DCollection ReadAscii(StreamReader reader, CancellationToken cancellation)
    {
        var points = new Point3DCollection();
        string token = ReadToken(reader);
        do
        {
            Expect(token, "solid");
            reader.ReadLine(); // Optional solid name.
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                token = ReadToken(reader);
                if (IsToken(token, "endsolid"))
                {
                    reader.ReadLine();
                    break;
                }
                Expect(token, "facet");
                Expect(ReadToken(reader), "normal");
                ReadNumber(reader);
                ReadNumber(reader);
                ReadNumber(reader);
                Expect(ReadToken(reader), "outer");
                Expect(ReadToken(reader), "loop");
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    Expect(ReadToken(reader), "vertex");
                    points.Add(new Point3D(ReadNumber(reader), ReadNumber(reader), ReadNumber(reader)));
                }
                Expect(ReadToken(reader), "endloop");
                Expect(ReadToken(reader), "endfacet");
            }
            token = ReadToken(reader);
        }
        while (token.Length > 0);
        return points;
    }

    private static double ReadNumber(TextReader reader)
    {
        double value;
        if (!double.TryParse(ReadToken(reader), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            throw new InvalidDataException("The ASCII STL contains an invalid number.");
        }
        return Coordinate(value);
    }

    private static double Coordinate(double value)
    {
        if (!(value >= -float.MaxValue && value <= float.MaxValue))
        {
            throw new InvalidDataException("The STL file contains invalid coordinates.");
        }
        return value;
    }

    private static string ReadToken(TextReader reader)
    {
        int next;
        while ((next = reader.Peek()) >= 0 && char.IsWhiteSpace((char)next))
        {
            reader.Read();
        }
        var token = new StringBuilder();
        while ((next = reader.Peek()) >= 0 && !char.IsWhiteSpace((char)next))
        {
            token.Append((char)reader.Read());
        }
        return token.ToString();
    }

    private static void Expect(string token, string expected)
    {
        if (!IsToken(token, expected))
        {
            throw new InvalidDataException("Invalid or incomplete STL file. Expected '" + expected + "'.");
        }
    }

    private static bool IsToken(string token, string expected)
    {
        return string.Equals(token, expected, StringComparison.OrdinalIgnoreCase);
    }
}
