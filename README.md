# Hamilton Labware to STL

A Windows desktop tool for exporting 3D models from a Hamilton VENUS labware library. It converts model geometry to binary STL and includes an optional viewer for inspecting the results.

## Requirements

- Windows with .NET Framework 4.5 or later.
- For conversion, Hamilton VENUS with the 32-bit Assimp library at `%ProgramFiles(x86)%\Hamilton\Bin\assimp-vc143-mt.dll`. The STL viewer also runs independently of VENUS.

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
