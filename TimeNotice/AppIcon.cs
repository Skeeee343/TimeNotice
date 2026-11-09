using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace TimeNotice;

// Single source of truth for the stopwatch logo: the tray icon
// (WinForms) and every window's taskbar/title-bar icon (WPF) all
// draw from DrawBitmap(), so they can never drift apart.
public static class AppIcon
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static System.Drawing.Icon CreateTrayIcon(bool dimmed = false)
    {
        using var bmp = DrawBitmap(dimmed);
        var hIcon = bmp.GetHicon();
        try
        {
            using var tmp = System.Drawing.Icon.FromHandle(hIcon);
            return (System.Drawing.Icon)tmp.Clone();
        }
        finally { DestroyIcon(hIcon); }
    }

    public static System.Windows.Media.ImageSource CreateWindowIcon()
    {
        using var bmp = DrawBitmap();
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var img = new BitmapImage();
        img.BeginInit();
        img.StreamSource = ms;
        img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }

    // Simple stopwatch: safety-yellow face, black rim + hands, light outer halo for dark mode.
    // Drawn at 32px so it stays legible scaled down to a 16px tray. Dimmed = gray face for suppressed state.
    private static System.Drawing.Bitmap DrawBitmap(bool dimmed = false)
    {
        const int size = 32;
        var bmp = new System.Drawing.Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            var yellow = dimmed
                ? System.Drawing.Color.FromArgb(255, 140, 140, 148) // dimmed gray: suppressed, not broken
                : System.Drawing.Color.FromArgb(255, 255, 212, 0); // safety yellow
            var black = System.Drawing.Color.FromArgb(255, 17, 17, 22);
            var outline = System.Drawing.Color.FromArgb(255, 245, 245, 247);
            using var faceBrush = new System.Drawing.SolidBrush(yellow);
            using var blackBrush = new System.Drawing.SolidBrush(black);
            using var halo = new System.Drawing.Pen(outline, 2f);
            using var rim = new System.Drawing.Pen(black, 2.5f);
            using var hand = new System.Drawing.Pen(black, 2f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };

            // Crown: top button + stem.
            g.FillRectangle(blackBrush, 12, 1, 8, 3);
            g.FillRectangle(blackBrush, 14, 4, 4, 3);
            // Side button (right, angled).
            g.DrawLine(hand, 23.5f, 9.5f, 27, 6);
            g.FillEllipse(blackBrush, 25, 3, 5, 5);

            // Face: light halo first so the black rim stays defined on dark taskbars.
            g.DrawEllipse(halo, 4, 6, 24, 24);
            g.FillEllipse(faceBrush, 5, 7, 22, 22);
            g.DrawEllipse(rim, 5, 7, 22, 22);

            // Hands: 12 o'clock + ~4 o'clock, center dot.
            var center = new System.Drawing.PointF(16, 18);
            g.DrawLine(hand, center, new System.Drawing.PointF(16, 11));
            g.DrawLine(hand, center, new System.Drawing.PointF(20.5f, 20.5f));
            g.FillEllipse(blackBrush, 14.5f, 16.5f, 3, 3);
        }
        return bmp;
    }
}
