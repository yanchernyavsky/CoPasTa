using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CoPasTa;

internal static class IconFactory
{
    /// <summary>
    /// Документ с двумя строками и указатель мыши, частично его перекрывающий.
    /// Белая заливка + тёмная обводка: значок виден и на светлой, и на тёмной теме.
    /// Bitmap нужно хранить всё время жизни иконки (GetHicon ссылается на него).
    /// </summary>
    internal static (Bitmap Bitmap, Icon Icon) Create()
    {
        var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color dark = Color.FromArgb(31, 41, 55);
            using var outline = new Pen(dark, 1.4f);
            using var linePen = new Pen(dark, 1.2f);
            using var white = new SolidBrush(Color.White);

            // документ
            g.FillRectangle(white, 5, 3, 18, 23);
            g.DrawRectangle(outline, 5, 3, 18, 23);
            g.DrawLine(linePen, 8, 10, 18, 10);
            g.DrawLine(linePen, 8, 15, 15, 15);

            // указатель мыши поверх документа
            var arrow = new[]
            {
                new PointF(13, 13),
                new PointF(13, 28),
                new PointF(17, 24),
                new PointF(20, 30),
                new PointF(23, 29),
                new PointF(20, 23),
                new PointF(26, 23)
            };

            using var path = new GraphicsPath();
            path.AddPolygon(arrow);
            g.FillPath(white, path);
            g.DrawPath(outline, path);
        }

        return (bitmap, Icon.FromHandle(bitmap.GetHicon()));
    }
}
