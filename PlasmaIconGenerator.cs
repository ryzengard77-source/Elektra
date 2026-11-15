/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using Elektra.Audio;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;
using LinearGradientBrush = System.Drawing.Drawing2D.LinearGradientBrush;
using Pen = System.Drawing.Pen;

namespace Elektra.UI.Assets;

public static class PlasmaIconGenerator
{
    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 256];

    public static MemoryStream BuildMultiResolutionIcoStream(PlasmaState state = PlasmaState.Idle)
    {
        (Color core, Color mid, Color arc) = GetPalette(state);
        var frames = new List<(int Size, byte[] Png)>(IconSizes.Length);

        foreach (int size in IconSizes)
        {
            using var bmp = RenderPlasmaOrbBitmap(size, core, mid, arc);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            frames.Add((size, ms.ToArray()));
        }

        var ico = new MemoryStream();
        using var w = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)frames.Count);

        int offset = 6 + (16 * frames.Count);
        foreach (var (size, bytes) in frames)
        {
            byte dim = size >= 256 ? (byte)0 : (byte)size;
            w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write((uint)bytes.Length); w.Write((uint)offset);
            offset += bytes.Length;
        }
        foreach (var (_, bytes) in frames) w.Write(bytes);

        w.Flush();
        ico.Position = 0;
        return ico;
    }

    public static Icon CreateTrayIcon(PlasmaState state = PlasmaState.Idle)
    {
        using var stream = BuildMultiResolutionIcoStream(state);
        return new Icon(stream);
    }

    public static ImageSource CreateWpfWindowIcon(PlasmaState state = PlasmaState.Idle)
    {
        using var stream = BuildMultiResolutionIcoStream(state);
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var best = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        best.Freeze();
        return best;
    }

    private static Bitmap RenderPlasmaOrbBitmap(int size, Color coreColor, Color midColor, Color arcColor)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float pad = size * 0.06f, d = size - pad * 2f, c = size / 2f;
        var rect = new RectangleF(pad, pad, d, d);

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(rect);
            using var pgb = new PathGradientBrush(path) { CenterPoint = new PointF(c * 0.88f, c * 0.85f) };
            pgb.InterpolationColors = new ColorBlend(3)
            {
                Colors = [Color.FromArgb(245, 10, 6, 24), midColor, coreColor],
                Positions = [0f, 0.58f, 1f]
            };
            g.FillEllipse(pgb, rect);
        }

        using var clip = new GraphicsPath();
        clip.AddEllipse(rect);
        g.SetClip(clip);

        var rng = new Random(1337);
        int tendrils = size <= 24 ? 6 : 8;
        using var arcPen = new Pen(Color.FromArgb(235, arcColor), Math.Max(0.9f, size * 0.028f));
        for (int t = 0; t < tendrils; t++)
        {
            double baseAng = (Math.PI * 2 * t) / tendrils + 0.35;
            var pts = new PointF[7];
            pts[0] = new PointF(c, c);
            for (int s = 1; s <= 6; s++)
            {
                float p = s / 6f, r = p * d * 0.48f;
                double ang = baseAng + (rng.NextDouble() - 0.5) * 0.45 * p;
                pts[s] = new PointF(c + (float)(Math.Cos(ang) * r), c + (float)(Math.Sin(ang) * r));
            }
            g.DrawLines(arcPen, pts);
        }

        float cr = d * 0.16f;
        using (var cb = new SolidBrush(Color.FromArgb(220, Color.White)))
            g.FillEllipse(cb, c - cr, c - cr, cr * 2, cr * 2);

        var hiRect = new RectangleF(pad + d * 0.18f, pad + d * 0.10f, d * 0.64f, d * 0.38f);
        using (var sb = new LinearGradientBrush(hiRect, Color.FromArgb(115, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
            g.FillEllipse(sb, hiRect);

        g.ResetClip();
        using var rim = new Pen(Color.FromArgb(190, arcColor), Math.Max(1f, size * 0.032f));
        g.DrawEllipse(rim, rect);
        return bmp;
    }

    private static (Color Core, Color Mid, Color Arc) GetPalette(PlasmaState state) => state switch
    {
        PlasmaState.Listening => (Color.FromArgb(207, 250, 254), Color.FromArgb(6, 182, 212), Color.FromArgb(103, 232, 249)),
        PlasmaState.Thinking => (Color.FromArgb(254, 240, 138), Color.FromArgb(236, 72, 153), Color.FromArgb(253, 186, 116)),
        PlasmaState.Speaking => (Color.FromArgb(209, 250, 229), Color.FromArgb(16, 185, 129), Color.FromArgb(110, 231, 183)),
        PlasmaState.Warning => (Color.FromArgb(254, 202, 202), Color.FromArgb(220, 38, 38), Color.FromArgb(252, 129, 129)),
        _ => (Color.FromArgb(233, 213, 255), Color.FromArgb(124, 58, 237), Color.FromArgb(196, 181, 253))
    };
}