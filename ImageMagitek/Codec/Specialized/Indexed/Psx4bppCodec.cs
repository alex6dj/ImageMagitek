﻿using System;

namespace ImageMagitek.Codec
{
    public sealed class Psx4bppCodec : IndexedCodec
    {
        public override string Name => "PSX 4bpp";
        public override int Width { get; }
        public override int Height { get; }

        public override ImageLayout Layout => ImageLayout.Single;
        public override int ColorDepth => 4;
        public override int StorageSize => Width * Height * 4;
        public override int DefaultWidth => 64;
        public override int DefaultHeight => 64;
        public override int RowStride => 0;
        public override int ElementStride => 0;
        public override int WidthResizeIncrement => 2;
        public override int HeightResizeIncrement => 1;
        public override bool CanResize => true;

        private BitStream _bitStream;

        public Psx4bppCodec()
        {
            Width = DefaultWidth;
            Height = DefaultHeight;
            Initialize();
        }

        public Psx4bppCodec(int width, int height)
        {
            Width = width;
            Height = height;
            Initialize();
        }

        private void Initialize()
        {
            _foreignBuffer = new byte[(StorageSize + 7) / 8];
            _nativeBuffer = new byte[Width, Height];
            _bitStream = BitStream.OpenRead(_foreignBuffer, StorageSize);
        }

        public override byte[,] DecodeElement(ArrangerElement el, ReadOnlySpan<byte> encodedBuffer)
        {
            if (encodedBuffer.Length * 8 < StorageSize) // Decoding would require data past the end of the buffer
                throw new ArgumentException(nameof(encodedBuffer));

            encodedBuffer.Slice(0, ForeignBuffer.Length).CopyTo(_foreignBuffer);

            _bitStream.SeekAbsolute(0);

            for (int y = 0; y < el.Height; y++)
            {
                for (int x = 0; x < el.Width; x += 2)
                {
                    var palIndex = (byte)_bitStream.ReadBits(4);
                    _nativeBuffer[x + 1, y] = palIndex;

                    palIndex = (byte)_bitStream.ReadBits(4);
                    _nativeBuffer[x, y] = palIndex;
                }
            }

            return _nativeBuffer;
        }

        public override ReadOnlySpan<byte> EncodeElement(ArrangerElement el, byte[,] imageBuffer)
        {
            if (imageBuffer.GetLength(0) != Width || imageBuffer.GetLength(1) != Height)
                throw new ArgumentException(nameof(imageBuffer));

            int dest = 0;
            for (int y = 0; y < el.Height; y++)
            {
                for (int x = 0; x < el.Width; x += 2, dest++)
                {
                    byte indexLow = imageBuffer[x, y];
                    byte indexHigh = imageBuffer[x + 1, y];

                    byte index = (byte)(indexLow | (indexHigh << 4));
                    _foreignBuffer[dest] = index;
                }
            }

            return ForeignBuffer;
        }
    }
}
