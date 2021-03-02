﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using ImageMagitek.Codec;
using ImageMagitek.Colors;

namespace ImageMagitek.Project.Serialization
{
    /// <summary>
    /// Builds a ProjectTree from Serialization Models and handles resolving of resources
    /// </summary>
    class ProjectTreeBuilder
    {
        public ProjectTree Tree { get; private set; }

        private readonly List<IProjectResource> _globalResources;
        private readonly Palette _globalDefaultPalette;
        private readonly ICodecFactory _codecFactory;
        private readonly IColorFactory _colorFactory;

        public ProjectTreeBuilder(ICodecFactory codecFactory, IColorFactory colorFactory, IEnumerable<IProjectResource> globalResources)
        {
            _codecFactory = codecFactory;
            _colorFactory = colorFactory;
            _globalResources = globalResources.ToList();
            _globalDefaultPalette = _globalResources.OfType<Palette>().FirstOrDefault();
        }

        public MagitekResult AddProject(ImageProjectModel projectModel)
        {
            if (Tree?.Root is object)
                return new MagitekResult.Failed($"Attempted to add a new project '{projectModel?.Name}' to an existing project");

            var metadata = new ProjectMetadata(projectModel);
            var root = new ProjectNode(projectModel.Name, projectModel.ToImageProject(), metadata);
            Tree = new ProjectTree(root);

            return MagitekResult.SuccessResult;
        }

        public MagitekResult AddFolder(ResourceFolderModel folderModel, string parentNodePath)
        {
            var folder = new ResourceFolder(folderModel.Name);

            var folderNode = new FolderNode(folder.Name, folder);
            Tree.TryGetNode(parentNodePath, out var parentNode);
            parentNode.AttachChildNode(folderNode);

            return MagitekResult.SuccessResult;
        }

        public MagitekResult AddDataFile(DataFileModel dfModel, string parentNodePath)
        {
            var df = new DataFile(dfModel.Name, dfModel.Location);

            var dfNode = new DataFileNode(df.Name, df);
            Tree.TryGetNode(parentNodePath, out var parentNode);
            parentNode.AttachChildNode(dfNode);

            if (!File.Exists(df.Location))
                return new MagitekResult.Failed($"DataFile '{df.Name}' does not exist at location '{df.Location}'");

            return MagitekResult.SuccessResult;
        }

        public MagitekResult AddPalette(PaletteModel paletteModel, string parentNodePath)
        {
            var pal = new Palette(paletteModel.Name, _colorFactory, paletteModel.ColorModel, paletteModel.FileAddress, paletteModel.Entries, paletteModel.ZeroIndexTransparent, paletteModel.StorageSource);

            if (!Tree.TryGetItem<DataFile>(paletteModel.DataFileKey, out var df))
                return new MagitekResult.Failed($"Palette '{pal.Name}' could not locate DataFile with key '{paletteModel.DataFileKey}'");

            pal.DataFile = df;
            pal.LazyLoadPalette(pal.DataFile, pal.FileAddress, pal.ColorModel, pal.ZeroIndexTransparent, pal.Entries);

            var palNode = new PaletteNode(pal.Name, pal);
            Tree.TryGetNode(parentNodePath, out var parentNode);
            parentNode.AttachChildNode(palNode);

            return MagitekResult.SuccessResult;
        }

        public MagitekResult AddScatteredArranger(ScatteredArrangerModel arrangerModel, string parentNodePath)
        {
            var arranger = new ScatteredArranger(arrangerModel.Name, arrangerModel.ColorType, arrangerModel.Layout, 
                arrangerModel.ArrangerElementSize.Width, arrangerModel.ArrangerElementSize.Height, arrangerModel.ElementPixelSize.Width, arrangerModel.ElementPixelSize.Height);

            for (int x = 0; x < arrangerModel.ElementGrid.GetLength(0); x++)
            {
                for (int y = 0; y < arrangerModel.ElementGrid.GetLength(1); y++)
                {
                    var result = CreateElement(arrangerModel, x, y);

                    if (result.IsT0)
                    {
                        arranger.SetElement(result.AsT0.Result, x, y);
                    }
                    else if (result.IsT1)
                    {
                        return new MagitekResult.Failed(result.AsT1.Reason);
                    }
                }
            }

            var arrangerNode = new ArrangerNode(arranger.Name, arranger);
            Tree.TryGetNode(parentNodePath, out var parentNode);
            parentNode.AttachChildNode(arrangerNode);

            return MagitekResult.SuccessResult;
        }

        /// <summary>
        /// Resolves a palette resource using the supplied project tree and falls back to a default palette if available
        /// </summary>
        /// <param name="tree"></param>
        /// <param name="paletteKey"></param>
        /// <returns></returns>
        private Palette ResolvePalette(string paletteKey)
        {
            var pal = _globalDefaultPalette;

            if (string.IsNullOrEmpty(paletteKey))
            {
                return _globalDefaultPalette;
            }
            else if (!Tree.TryGetItem<Palette>(paletteKey, out pal))
            {
                var name = paletteKey.Split(Tree.PathSeparators).Last();
                pal = _globalResources.OfType<Palette>().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            return pal;
        }

        private MagitekResult<ArrangerElement?> CreateElement(ScatteredArrangerModel arrangerModel, int x, int y)
        {
            var elementModel = arrangerModel.ElementGrid[x, y];
            IGraphicsCodec codec = default;
            Palette palette = default;
            DataFile df = default;
            FileBitAddress address = 0;

            if (elementModel is null)
            {
                return new MagitekResult<ArrangerElement?>.Success(null);
            }
            else if (arrangerModel.ColorType == PixelColorType.Indexed)
            {
                if (!string.IsNullOrWhiteSpace(elementModel.DataFileKey))
                    Tree.TryGetItem<DataFile>(elementModel.DataFileKey, out df);
                
                address = elementModel.FileAddress;
                var paletteKey = elementModel.PaletteKey;
                palette = ResolvePalette(paletteKey);

                if (palette is null)
                {
                    return new MagitekResult<ArrangerElement?>.Failed($"Could not resolve palette '{paletteKey}' referenced by arranger '{arrangerModel.Name}'");
                }

                codec = _codecFactory.GetCodec(elementModel.CodecName, new Size(arrangerModel.ElementPixelSize.Width, arrangerModel.ElementPixelSize.Height));
            }
            else if (arrangerModel.ColorType == PixelColorType.Direct)
            {
                if (!string.IsNullOrWhiteSpace(elementModel.DataFileKey))
                    Tree.TryGetItem<DataFile>(elementModel.DataFileKey, out df);

                address = elementModel.FileAddress;
                codec = _codecFactory.GetCodec(elementModel.CodecName, new Size(arrangerModel.ElementPixelSize.Width, arrangerModel.ElementPixelSize.Height));
            }
            else
            {
                return new MagitekResult<ArrangerElement?>.Failed($"{nameof(CreateElement)}: Arranger '{arrangerModel.Name}' has invalid {nameof(PixelColorType)} '{arrangerModel.ColorType}'");
            }

            var pixelX = x * arrangerModel.ElementPixelSize.Width;
            var pixelY = y * arrangerModel.ElementPixelSize.Height;
            var el = new ArrangerElement(pixelX, pixelY, df, address, codec, palette);
            return new MagitekResult<ArrangerElement?>.Success(el);
        }
    }
}
