﻿using System;
using System.Linq;
using ImageMagitek.Codec;
using ImageMagitek.Colors;
using ImageMagitek.ExtensionMethods;

namespace ImageMagitek
{
    public class IndexedImage : ImageBase<byte>
    {
        private Palette _defaultPalette;

        public IndexedImage(Arranger arranger) : this(arranger, null) { }

        public IndexedImage(Arranger arranger, Palette defaultPalette)
        {
            if (arranger is null)
                throw new ArgumentNullException($"{nameof(IndexedImage)}.Ctor parameter '{nameof(arranger)}' was null");

            Arranger = arranger;
            Image = new byte[Width * Height];
            _defaultPalette = defaultPalette;
            Render();
        }

        public override void ExportImage(string imagePath, IImageFileAdapter adapter) =>
            adapter.SaveImage(Image, Arranger, _defaultPalette, imagePath);

        public override void ImportImage(string imagePath, IImageFileAdapter adapter)
        {
            var importImage = adapter.LoadImage(imagePath, Arranger, _defaultPalette);
            importImage.CopyTo(Image, 0);
        }

        public override void Render()
        {
            if (Width <= 0 || Height <= 0)
                throw new InvalidOperationException($"{nameof(Render)}: arranger dimensions for '{Arranger.Name}' are too small to render " +
                    $"({Width}, {Height})");

            if (Width * Height != Image.Length)
                Image = new byte[Width * Height];

            foreach (var el in Arranger.EnumerateElements())
            {
                if (el.Codec is IIndexedCodec codec)
                {
                    var encodedBuffer = codec.ReadElement(el);
                    var decodedImage = codec.DecodeElement(el, encodedBuffer);

                    for (int y = 0; y < Arranger.ElementPixelSize.Height; y++)
                    {
                        var destidx = (y + el.Y1) * Width + el.X1;
                        for (int x = 0; x < Arranger.ElementPixelSize.Width; x++)
                        {
                            Image[destidx] = decodedImage[x, y];
                            destidx++;
                        }
                    }
                }
            }
        }

        public override void SaveImage()
        {
            var buffer = new byte[Arranger.ElementPixelSize.Width, Arranger.ElementPixelSize.Height];
            
            foreach (var el in Arranger.EnumerateElements().Where(x => x.Codec is IIndexedCodec))
            {
                Image.CopyToArray(buffer, el.X1, el.Y1, Width, el.Width, el.Height);
                var codec = el.Codec as IIndexedCodec;
                var encodedImage = codec.EncodeElement(el, buffer);
                codec.WriteElement(el, encodedImage);
            }

            foreach (var fs in Arranger.EnumerateElements().Select(x => x.DataFile.Stream).Distinct())
                fs.Flush();
        }

        public override void SetPixel(int x, int y, byte color)
        {
            if (x >= Width || y >= Height || x < 0 || y < 0)
                throw new ArgumentOutOfRangeException($"{nameof(GetPixel)} ({nameof(x)}: {x}, {nameof(y)}: {y}) were outside the image bounds ({nameof(Width)}: {Width}, {nameof(Height)}: {Height}");

            var pal = Arranger.GetElement(x, y).Palette;
            if (color >= pal.Entries)
                throw new ArgumentOutOfRangeException($"{nameof(GetPixel)} ({nameof(color)} ({color}): exceeded the number of entries in palette '{pal.Name}' ({pal.Entries})");

            Image[x + Width * y] = color;
        }

        public bool TrySetPixel(int x, int y, ColorRgba32 color)
        {
            var elem = Arranger.GetElementAtPixel(x, y);
            if (!(elem.Codec is IIndexedCodec))
                return false;

            var pal = elem.Palette ?? _defaultPalette;

            if (pal is null)
                return false;
                //throw new NullReferenceException($"{nameof(TrySetPixel)} at ({x}, {y}) with arranger '{Arranger.Name}' has no palette specified and no default palette");

            if (!pal.ContainsNativeColor(color))
                return false;
                //throw new ArgumentException($"{nameof(TrySetPixel)} with arranger '{Arranger.Name}' and palette '{pal.Name}' does not contain the native color ({color.R}, {color.G}, {color.B}, {color.A})");

            var index = pal.GetIndexByNativeColor(color, true);
            Image[x + Width * y] = index;
            return true;
        }

        public ColorRgba32 GetPixel(int x, int y, Arranger arranger)
        {
            var pal = Arranger.GetElement(x, y).Palette ?? _defaultPalette;
            var imageIndex = y * arranger.ArrangerPixelSize.Width + x;
            var palIndex = Image[imageIndex];
            return pal[palIndex];
        }
    }
}