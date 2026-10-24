using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TabSwitch;

/// <summary>Draws the tray icons: coloured badge with the active driver's letter, plus a status pip.</summary>
public static class IconFactory
{
    static readonly Color Green = Color.FromArgb(0x1F, 0xA8, 0x5C);   // OpenTabletDriver
    static readonly Color Orange = Color.FromArgb(0xE8, 0x7A, 0x1E);  // XP-PEN
    static readonly Color Blue = Color.FromArgb(0x3A, 0x7B, 0xE0);    // switching
    static readonly Color Red = Color.FromArgb(0xD6, 0x3B, 0x3B);     // error
    static readonly Color Gray = Color.FromArgb(0x8A, 0x8A, 0x8A);    // auto off

    public static Bitmap Draw(TrayState state, bool autoOn, int size = 32)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        var (color, text) = state switch
        {
            TrayState.Otd => (Green, "O"),
            TrayState.XpPen => (Orange, "X"),
            TrayState.Switching => (Blue, "⇄"),
            _ => (Red, "!"),
        };

        float s = size / 32f;
        using (var path = RoundedRect(new RectangleF(1 * s, 1 * s, 30 * s, 30 * s), 8 * s))
        using (var brush = new SolidBrush(color))
            g.FillPath(brush, path);

        using (var font = new Font("Segoe UI", 19 * s, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 1 * s, size, size), sf);

        // Bottom-right pip: grey with a bar when automatic switching is off.
        if (!autoOn && state != TrayState.Error)
        {
            var r = new RectangleF(19 * s, 19 * s, 12 * s, 12 * s);
            using var b = new SolidBrush(Gray);
            using var pen = new Pen(Color.White, 2 * s);
            g.FillEllipse(b, r);
            g.DrawEllipse(pen, r);
            g.DrawLine(pen, 22 * s, 25 * s, 28 * s, 25 * s);
        }
        return bmp;
    }

    public static IntPtr CreateHIcon(TrayState state, bool autoOn)
    {
        using var bmp = Draw(state, autoOn);
        return bmp.GetHicon();
    }

    /// <summary>Writes a multi-size .ico (PNG-compressed entries) for the exe/app icon.</summary>
    public static void SaveIco(string path)
    {
        int[] sizes = { 16, 32, 48, 256 };
        var pngs = sizes.Select(sz =>
        {
            using var bmp = Draw(TrayState.Otd, true, sz);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
