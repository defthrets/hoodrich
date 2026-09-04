# tools/

Self-contained build toolchain. **Not committed** — restore it with the script below.

`build.ps1` does not use `dotnet build`, because the .NET 8 SDK on this machine is broken:
`C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.28` is a partial install (3 files
where 8.0.19 has 184), so every `dotnet` invocation fails with a missing `hostpolicy.dll`.

Instead the build drives a Roslyn `csc.exe` that runs on .NET Framework directly. No SDK,
no Visual Studio, no admin rights.

## Restore

```powershell
$tools = $PSScriptRoot
$pkgs = @{
  "roslyn" = "https://api.nuget.org/v3-flatcontainer/microsoft.net.compilers.toolset/4.8.0/microsoft.net.compilers.toolset.4.8.0.nupkg"
  "refasm" = "https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net48/1.0.3/microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg"
}
foreach ($k in $pkgs.Keys) {
  $zip = Join-Path $tools "$k.zip"
  Invoke-WebRequest -Uri $pkgs[$k] -OutFile $zip -UseBasicParsing
  Expand-Archive $zip (Join-Path $tools $k) -Force
  Remove-Item $zip
}
```

Produces:

- `tools\roslyn\tasks\net472\csc.exe` — the C# compiler
- `tools\refasm\build\.NETFramework\v4.8\*.dll` — net48 reference assemblies

If the SDK ever gets repaired, `src\Trapline\Trapline.csproj` builds the same assembly with
`dotnet build -c Release`.

## The icons

Every PNG in `data\icons` is drawn by `make_icons.py`, from three files of drawings and one
kit:

- `iconkit.py` -- the rules. White on transparent, tinted at draw time by CustomSprite, in
  THREE TONES of alpha (body, plane, groove) plus holes; drawn at 512 and written at 128.
- `icons_a.py` -- the interface, money, places and people. `icons_b.py` -- the product and
  the tools. `icons_c.py` -- the sets' marks.
- `make_icons_old.py` -- the 64-pixel stencils this replaced, kept for the record.

`python tools\make_icons.py` redraws the lot; name icons on the command line to redraw just
those. `make_logo.py` makes the wordmark and prints the aspect to paste into `Draw.cs`.
