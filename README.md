# Hamilton Labware Converter

A Windows desktop tool for converting between STL meshes and Hamilton VENUS 3D models. It can create a new labware definition from a VENUS template and includes an optional STL viewer.

## Requirements

- Windows with .NET Framework 4.5 or later.
- For Hamilton-to-STL exports, Hamilton VENUS with the 32-bit Assimp library at `%ProgramFiles(x86)%\Hamilton\Bin\assimp-vc143-mt.dll`. The STL viewer also runs independently of VENUS.
- Creating a definition from a template uses the registered VENUS configuration component. Creating a standalone DirectX model from STL runs independently of VENUS.

## Build and run

Run the build script from the repository root in PowerShell.

    powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1

The script uses the Windows .NET Framework compiler and creates `LabwareToSTL.exe` in the repository root. Pass `-OutputDirectory` to build into a different folder.

1. Open `LabwareToSTL.exe`. The default source is `C:\Program Files (x86)\Hamilton\Labware`.
2. Choose an output folder and click **Export STLs**. The default is the `STL` folder next to the executable. Existing STLs are kept unless you enable **Overwrite existing STL files**.
3. Open **STL Viewer** to inspect the output, or **Open output folder** to use it in another application.

## Export output

| Path | Contents |
| --- | --- |
| `STL/Labware/` | Models associated with labware definitions |
| `STL/Models/` | Standalone exports of the source 3D model files |
| `STL/ExportReport.csv` | Per-file status, source model, output path, and dimensions for newly exported geometry |

Labware definitions can be RCK, TML, CTR, or LID files. Supported model formats are DirectX X, Hamilton HXX, glTF, GLB, and 3DS. The exporter preserves source subfolders, uses Z as the vertical axis, and writes Hamilton model dimensions in millimetres. It applies definition offsets to labware exports. Missing 3D models and unresolved model references are listed in the report.

## STL to Hamilton

1. Click **STL to Hamilton** and choose a binary or ASCII STL.
2. Set **STL units** to match the source model. The tool writes the DirectX model in millimetres and preserves the STL origin.
3. To create a complete labware definition, select an existing `.rck`, `.tml`, `.ctr`, or `.lid` as a template. Choose a template with the well layout, site layout, and dimensions you need. Leave the template field blank to create a standalone `.x` model that you can attach in the VENUS Labware Editor.
4. Click **Convert STL**. The default output is `HamiltonOutput\<name>` next to the executable. The generated model and copied definition sit together in that folder.

The STL supplies the 3D surface. The template supplies the working dimensions, well geometry, carrier sites, and other labware settings. The copy uses the new output name and zero model offsets. Review the origin, dimensions, and functional settings in the VENUS Labware Editor before using the definition in a method. Keep the definition and `.x` file together. Template dependencies such as container definitions and site labware still refer to their existing files.

## STL viewer

The viewer opens in a separate window. Select an exported file in the list or use **Open STL** to load any binary or ASCII STL. The filter searches filenames and relative folder names.

| Control | Action |
| --- | --- |
| Left drag | Orbit the model |
| Right drag | Pan |
| Mouse wheel | Zoom |
| Fit view | Recenter the model and reset the camera |

The viewer shows the triangle count and bounding dimensions in the STL's coordinate units. The viewer displays Hamilton exports in millimetres.

## Batch mode

    $source = 'C:\Program Files (x86)\Hamilton\Labware'
    $output = Join-Path $PWD 'STL'
    $process = Start-Process .\LabwareToSTL.exe -ArgumentList ('--batch "{0}" "{1}"' -f $source, $output) -Wait -PassThru
    $process.ExitCode

Append `--overwrite` to the argument list to replace existing STL files. Exit codes are 0 for a completed run without conversion errors, 1 for one or more per-file errors, and 2 for invalid arguments or a failure that stops the run. The report records skipped files.

## Source layout

| File | Responsibility |
| --- | --- |
| `Program.cs` | Export window and batch entry point |
| `LabwareExport.cs` | Labware discovery, model matching, and reporting |
| `Assimp.cs` | Model import through the installed VENUS library |
| `Stl.cs` | Binary export and coordinate conversion |
| `ModelViewer.cs` | File browser and viewer window |
| `StlMesh.cs` | Binary and ASCII STL reading |
| `PreviewViewport.cs` | 3D rendering and camera controls |
| `DirectXModel.cs` | STL geometry conversion and DirectX writing |
| `LabwareImport.cs` | Output package creation |
| `VenusTemplate.cs` | VENUS template copying and model references |
| `ImportWindow.cs` | STL-to-Hamilton window |
