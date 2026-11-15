<#
.SYNOPSIS
    Compiles PlasmaOrbEffect.fx with fxc.exe, generates Assets\elektra.ico, and publishes
    a self-contained single-file Windows x64 release binary for Elektra.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = ".\dist\Elektra-win-x64",
    [switch]$SkipShaderCompile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$CsprojPath  = Join-Path $ProjectRoot "Elektra.csproj"
$FxSource    = Join-Path $ProjectRoot "UI\Effects\PlasmaOrbEffect.fx"
$PsOutput    = Join-Path $ProjectRoot "UI\Effects\PlasmaOrbEffect.ps"
$IcoOutput   = Join-Path $ProjectRoot "Assets\elektra.ico"
$ModelsDir   = Join-Path $ProjectRoot "Models"

function Write-Step([string]$Msg) { Write-Host "`n==> $Msg" -ForegroundColor Cyan }

if (-not $SkipShaderCompile) {
    Write-Step "Compiling HLSL Pixel Shader (ps_3_0)..."
    $sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    $fxcPath = Get-ChildItem -Path $sdkRoot -Filter "fxc.exe" -Recurse -ErrorAction SilentlyContinue |
               Where-Object { $_.FullName -match "\\x64\\fxc\.exe$" } |
               Sort-Object FullName -Descending |
               Select-Object -First 1 -ExpandProperty FullName

    if (-not $fxcPath) { throw "x64 fxc.exe not found under '$sdkRoot'. Install Windows 10/11 SDK." }
    & $fxcPath /T ps_3_0 /E main /O3 /nologo /Fo $PsOutput $FxSource
    if ($LASTEXITCODE -ne 0) { throw "fxc.exe failed with exit code $LASTEXITCODE." }
    Write-Host "    Compiled -> $PsOutput" -ForegroundColor Green
}

Write-Step "Generating Multi-Resolution Assets\elektra.ico..."
$icoDir = Split-Path -Parent $IcoOutput
if (-not (Test-Path $icoDir)) { New-Item -ItemType Directory -Path $icoDir -Force | Out-Null }

$iconGeneratorCSharp = @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

public static class BuildTimeIconPacker
{
    public static void Generate(string outputPath)
    {
        int[] sizes = new[] { 16, 24, 32, 48, 64, 256 };
        var frames = new List<Tuple<int, byte[]>>();
        Color coreColor = Color.FromArgb(233, 213, 255);
        Color midColor  = Color.FromArgb(124, 58, 237);
        Color arcColor  = Color.FromArgb(196, 181, 253);

        foreach (int size in sizes)
        {
            using (var bmp = RenderOrb(size, coreColor, midColor, arcColor))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                frames.Add(Tuple.Create(size, ms.ToArray()));
            }
        }

        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(fs, Encoding.UTF8))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)frames.Count);
            int offset = 6 + (16 * frames.Count);
            foreach (var f in frames)
            {
                byte dim = f.Item1 >= 256 ? (byte)0 : (byte)f.Item1;
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write((uint)f.Item2.Length); w.Write((uint)offset);
                offset += f.Item2.Length;
            }
            foreach (var f in frames) w.Write(f.Item2);
        }
    }

    private static Bitmap RenderOrb(int size, Color coreColor, Color midColor, Color arcColor)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float pad = size * 0.06f, d = size - pad * 2f, c = size / 2f;
            var rect = new RectangleF(pad, pad, d, d);

            using (var path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterPoint = new PointF(c * 0.88f, c * 0.85f);
                    var blend = new ColorBlend(3);
                    blend.Colors = new[] { Color.FromArgb(245, 10, 6, 24), midColor, coreColor };
                    blend.Positions = new[] { 0f, 0.58f, 1f };
                    pgb.InterpolationColors = blend;
                    g.FillEllipse(pgb, rect);
                }
            }

            using (var clip = new GraphicsPath())
            {
                clip.AddEllipse(rect);
                g.SetClip(clip);
                var rng = new Random(1337);
                int tendrils = size <= 24 ? 6 : 8;
                using (var arcPen = new Pen(Color.FromArgb(235, arcColor), Math.Max(0.9f, size * 0.028f)))
                {
                    for (int t = 0; t < tendrils; t++)
                    {
                        double baseAng = (Math.PI * 2.0 * t) / tendrils + 0.35;
                        var pts = new PointF[7]; pts[0] = new PointF(c, c);
                        for (int s = 1; s <= 6; s++)
                        {
                            float p = s / 6f, r = p * d * 0.48f;
                            double ang = baseAng + (rng.NextDouble() - 0.5) * 0.45 * p;
                            pts[s] = new PointF(c + (float)(Math.Cos(ang) * r), c + (float)(Math.Sin(ang) * r));
                        }
                        g.DrawLines(arcPen, pts);
                    }
                }
                float cr = d * 0.16f;
                using (var cb = new SolidBrush(Color.FromArgb(220, Color.White))) g.FillEllipse(cb, c - cr, c - cr, cr * 2f, cr * 2f);
                var hiRect = new RectangleF(pad + d * 0.18f, pad + d * 0.10f, d * 0.64f, d * 0.38f);
                using (var sb = new LinearGradientBrush(hiRect, Color.FromArgb(115, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical)) g.FillEllipse(sb, hiRect);
                g.ResetClip();
            }
            using (var rim = new Pen(Color.FromArgb(190, arcColor), Math.Max(1f, size * 0.032f))) g.DrawEllipse(rim, rect);
        }
        return bmp;
    }
}
"@

Add-Type -TypeDefinition $iconGeneratorCSharp -ReferencedAssemblies @("System.Drawing")
[BuildTimeIconPacker]::Generate($IcoOutput)
Write-Host "    Generated -> $IcoOutput" -ForegroundColor Green

Write-Step "Publishing Self-Contained Single-File Release..."
if (Test-Path $OutputDir) { Remove-Item -Path $OutputDir -Recurse -Force }

dotnet publish $CsprojPath -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $OutputDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

if (Test-Path $ModelsDir) {
    Copy-Item -Path $ModelsDir -Destination (Join-Path $OutputDir "Models") -Recurse -Force
    Write-Host "    Bundled Models directory." -ForegroundColor Green
} else {
    Write-Warning "Models directory not found at '$ModelsDir'. Place your Vosk model folder next to Elektra.exe before running."
}

Write-Host "`nBUILD COMPLETE: $(Join-Path $OutputDir 'Elektra.exe')" -ForegroundColor Green