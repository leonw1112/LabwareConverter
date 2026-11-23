using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class Assimp : IDisposable
{
    private const uint Triangulate = 0x8;
    private const uint PreTransformVertices = 0x100;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ImportFile(byte[] path, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ImportMemory(byte[] data, uint length, uint flags, string extension);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ExportBlob(IntPtr scene, string format, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetError();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Release(IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public UIntPtr Size;
        public IntPtr Data;
    }

    private IntPtr module;
    private readonly ImportFile importFile;
    private readonly ImportMemory importMemory;
    private readonly ExportBlob exportBlob;
    private readonly GetError getError;
    private readonly Release releaseImport;
    private readonly Release releaseBlob;
    private static readonly Regex MatrixTerminator = new Regex(
        @"(\bFrameTransformMatrix\s*\{\s*(?:[-+\d.eE]+\s*,\s*){15}[-+\d.eE]+\s*;)(\s*\})",
        RegexOptions.CultureInvariant);

    internal Assimp(string libraryPath)
    {
        // Resolve the DLL's own dependencies from the Hamilton Bin directory.
        module = LoadLibraryEx(Path.GetFullPath(libraryPath), IntPtr.Zero, 0x8);
        if (module == IntPtr.Zero)
        {
            throw new IOException("Could not load the Assimp library from the Hamilton installation: " +
                libraryPath, new Win32Exception(Marshal.GetLastWin32Error()));
        }

        try
        {
            importFile = Bind<ImportFile>("aiImportFile");
            importMemory = Bind<ImportMemory>("aiImportFileFromMemory");
            exportBlob = Bind<ExportBlob>("aiExportSceneToBlob");
            getError = Bind<GetError>("aiGetErrorString");
            releaseImport = Bind<Release>("aiReleaseImport");
            releaseBlob = Bind<Release>("aiReleaseExportBlob");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal byte[] ReadTriangles(string path)
    {
        const uint flags = Triangulate | PreTransformVertices;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        IntPtr scene;
        if (extension == ".hxx" || extension == ".x" || extension == ".3ds" || extension == ".glb")
        {
            byte[] model = extension == ".hxx" ? ReadHxx(path) : File.ReadAllBytes(path);
            string hint = extension == ".hxx" ? "x" : extension.Substring(1);
            scene = importMemory(model, (uint)model.Length, flags, hint);
            if (scene == IntPtr.Zero && hint == "x" && model.Length >= 16 &&
                Encoding.ASCII.GetString(model, 8, 4) == "txt " &&
                (Marshal.PtrToStringAnsi(getError()) ?? "").Contains("Semicolon expected"))
            {
                // Some legacy exporters omit the outer separator after the matrix array.
                Encoding encoding = Encoding.GetEncoding(28591);
                string original = encoding.GetString(model);
                string corrected = MatrixTerminator.Replace(original, "$1;$2");
                if (corrected != original)
                {
                    byte[] repaired = encoding.GetBytes(corrected);
                    scene = importMemory(repaired, (uint)repaired.Length, flags, "x");
                }
            }
        }
        else
        {
            // glTF can refer to an external .bin file relative to its own path.
            scene = importFile(Encoding.UTF8.GetBytes(path + "\0"), flags);
        }

        if (scene == IntPtr.Zero)
        {
            throw Failure("3D import");
        }

        try
        {
            IntPtr blob = exportBlob(scene, "stlb", 0);
            if (blob == IntPtr.Zero)
            {
                throw Failure("STL export");
            }
            try
            {
                var entry = (Blob)Marshal.PtrToStructure(blob, typeof(Blob));
                var data = new byte[checked((int)entry.Size.ToUInt64())];
                Marshal.Copy(entry.Data, data, 0, data.Length);
                return data;
            }
            finally
            {
                releaseBlob(blob);
            }
        }
        finally
        {
            releaseImport(scene);
        }
    }

    private static byte[] ReadHxx(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        const string signature = "Hamilton3dData";
        const string name = "__Main3dData__";
        if (data.Length < 32 || Encoding.ASCII.GetString(data, 0, signature.Length) != signature)
        {
            throw new InvalidDataException("Unrecognized Hamilton 3D container.");
        }
        int nameOffset = BitConverter.ToInt32(data, 23);
        int nameLength = data[27];
        int modelLength = (data[28] << 24) | (data[29] << 16) | (data[30] << 8) | data[31];
        const int footerLength = 16;
        if (nameOffset < 32 || nameLength != name.Length ||
            (long)nameOffset + nameLength + footerLength >= data.Length ||
            Encoding.ASCII.GetString(data, nameOffset, nameLength) != name)
        {
            throw new InvalidDataException("The Hamilton container is missing 3D model data.");
        }
        int start = nameOffset + nameLength;
        using (var source = new MemoryStream(data, start, data.Length - start - footerLength))
        using (var gzip = new GZipStream(source, CompressionMode.Decompress))
        using (var model = new MemoryStream())
        {
            gzip.CopyTo(model);
            byte[] bytes = model.ToArray();
            if (bytes.Length != modelLength || bytes.Length < 16 ||
                Encoding.ASCII.GetString(bytes, 0, 4) != "xof ")
            {
                throw new InvalidDataException("The Hamilton container does not contain a valid DirectX model.");
            }
            return bytes;
        }
    }

    private T Bind<T>(string name) where T : class
    {
        IntPtr address = GetProcAddress(module, name);
        if (address == IntPtr.Zero)
        {
            throw new EntryPointNotFoundException(name);
        }
        return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
    }

    private InvalidDataException Failure(string operation)
    {
        return new InvalidDataException(operation + ": " + Marshal.PtrToStringAnsi(getError()));
    }

    public void Dispose()
    {
        if (module != IntPtr.Zero)
        {
            FreeLibrary(module);
            module = IntPtr.Zero;
        }
    }
}
