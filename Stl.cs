using System;
using System.Globalization;
using System.IO;

internal static class Stl
{
    internal const int HeaderSize = 84;
    internal const int TriangleSize = 50;

    internal static string Prepare(byte[] input, double[] offset, out byte[] output)
    {
        if (input.Length < HeaderSize ||
            input.LongLength != HeaderSize + TriangleSize * (long)BitConverter.ToUInt32(input, 80))
        {
            throw new InvalidDataException("The STL export contains invalid triangle data.");
        }

        int count = 0;
        var min = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var max = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        var vertices = new double[3, 3];
        using (var stream = new MemoryStream(input.Length))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(input, 0, HeaderSize);
            for (int start = HeaderSize; start < input.Length; start += TriangleSize)
            {
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    int point = start + 12 + vertex * 12;
                    // Assimp produces a right-handed Y-up scene. VENUS models use mm.
                    // Rotate into an XY build plane, then apply VENUS offsets in X/Y/Z.
                    vertices[vertex, 0] = ToStlFloat(BitConverter.ToSingle(input, point) + offset[0]);
                    vertices[vertex, 1] = ToStlFloat(-BitConverter.ToSingle(input, point + 8) + offset[1]);
                    vertices[vertex, 2] = ToStlFloat(BitConverter.ToSingle(input, point + 4) + offset[2]);
                }
                double ax = vertices[1, 0] - vertices[0, 0];
                double ay = vertices[1, 1] - vertices[0, 1];
                double az = vertices[1, 2] - vertices[0, 2];
                double bx = vertices[2, 0] - vertices[0, 0];
                double by = vertices[2, 1] - vertices[0, 1];
                double bz = vertices[2, 2] - vertices[0, 2];
                double nx = ay * bz - az * by;
                double ny = az * bx - ax * bz;
                double nz = ax * by - ay * bx;
                double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (length == 0)
                {
                    continue;
                }
                writer.Write((float)(nx / length));
                writer.Write((float)(ny / length));
                writer.Write((float)(nz / length));
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    for (int axis = 0; axis < 3; axis++)
                    {
                        double value = vertices[vertex, axis];
                        writer.Write((float)value);
                        min[axis] = Math.Min(min[axis], value);
                        max[axis] = Math.Max(max[axis], value);
                    }
                }
                writer.Write((ushort)0);
                count++;
            }
            if (count == 0)
            {
                throw new InvalidDataException("The 3D model contains no valid triangles.");
            }
            stream.Position = 80;
            writer.Write(count);
            output = stream.ToArray();
        }
        return string.Format(CultureInfo.InvariantCulture,
            "{0} {1}. X × Y × Z: {2:0.###} × {3:0.###} × {4:0.###} mm.",
            count, count == 1 ? "triangle" : "triangles",
            max[0] - min[0], max[1] - min[1], max[2] - min[2]);
    }

    private static float ToStlFloat(double value)
    {
        float converted = (float)value;
        if (float.IsNaN(converted) || float.IsInfinity(converted))
        {
            throw new InvalidDataException("The 3D model contains invalid coordinates.");
        }
        return converted;
    }
}
