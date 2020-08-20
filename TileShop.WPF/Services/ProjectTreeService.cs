﻿using System;
using System.Collections.Generic;
using System.Linq;
using ImageMagitek;
using ImageMagitek.Project;
using ImageMagitek.Colors;
using ImageMagitek.Services;
using TileShop.WPF.ViewModels;
using TileShop.WPF.Models;
using TileShop.WPF.ViewModels.Dialogs;
using Monaco.PathTree;

namespace TileShop.WPF.Services
{
    public interface IProjectTreeService
    {
        IPathTree<IProjectResource> Tree { get; }

        MagitekResult ApplySchemaDefinition(string schemaFileName);

        MagitekResult<ImageProjectNodeViewModel> NewProject(string projectName);
        MagitekResults<ImageProjectNodeViewModel> OpenProject(string projectFileName);
        MagitekResult SaveProject(ProjectTree projectTree, string projectFileName);
        void CloseProject(ProjectTree projectTree);
        void CloseProjects();

        FolderNodeViewModel CreateNewFolder(TreeNodeViewModel parentNodeModel);
        TreeNodeViewModel AddResource(TreeNodeViewModel parentModel, IProjectResource resource);

        MagitekResult CanMoveNode(TreeNodeViewModel node, TreeNodeViewModel parentNode);
        void MoveNode(TreeNodeViewModel node, TreeNodeViewModel parentNode);

        ResourceRemovalChangesViewModel GetResourceRemovalChanges(TreeNodeViewModel rootNodeModel, TreeNodeViewModel removeNodeModel);
    }

    public class ProjectTreeService : IProjectTreeService
    {
        public IPathTree<IProjectResource> Tree { get; private set; }
        private ICodecService _codecService;
        private ISolutionService _solutionService;
        private readonly string _schemaFileName;

        public ProjectTreeService(string schemaFileName, ICodecService codecService)
        {
            _schemaFileName = schemaFileName;
            _codecService = codecService;

            _solutionService = new SolutionService(codecService, Enumerable.Empty<IProjectResource>());
        }

        //public ImageProjectNodeViewModel NewProject(string projectName)
        //{
        //    var project = new ImageProject(projectName);
        //    Tree = new PathTree<IProjectResource>(projectName, project);
        //    return new ImageProjectNodeViewModel(Tree.Root);
        //}

        public MagitekResult<ImageProjectNodeViewModel> NewProject(string projectName)
        {
            var newResult = _solutionService.NewProject(projectName);
            return newResult.Match<MagitekResult<ImageProjectNodeViewModel>>(
                success =>
                {
                    var imageVm = new ImageProjectNodeViewModel(success.Result.Tree.Root);
                    return new MagitekResult<ImageProjectNodeViewModel>.Success(imageVm);
                },
                fail => new MagitekResult<ImageProjectNodeViewModel>.Failed(fail.Reason));
        }

        public MagitekResult ApplySchemaDefinition(string schemaFileName) => 
            _solutionService.LoadSchemaDefinition(schemaFileName);

        public MagitekResults<ImageProjectNodeViewModel> OpenProject(string projectFileName)
        {
            var newResult = _solutionService.OpenProject(projectFileName);
            return newResult.Match<MagitekResults<ImageProjectNodeViewModel>>(
                success =>
                {
                    var imageVm = new ImageProjectNodeViewModel(success.Result.Tree.Root);
                    return new MagitekResults<ImageProjectNodeViewModel>.Success(imageVm);
                },
                fail => new MagitekResults<ImageProjectNodeViewModel>.Failed(fail.Reasons));
        }
        //{


        //    if (string.IsNullOrWhiteSpace(projectFileName))
        //        throw new ArgumentException($"{nameof(OpenProject)} cannot have a null or empty value for '{nameof(projectFileName)}'");

        //    CloseResources();
        //    var deserializer = new XmlGameDescriptorReader(_schemaFileName, _codecService.CodecFactory);
        //    var result = deserializer.ReadProject(projectFileName);

        //    if (result.Value is MagitekResults<IPathTree<IProjectResource>>.Success success)
        //        Tree = success.Result;

        //    return result;
        //}

        public MagitekResult SaveProject(ProjectTree projectTree, string projectFileName) => 
            _solutionService.SaveProject(projectTree, projectFileName);
        //{
        //    if (Tree is null)
        //        throw new InvalidOperationException($"{nameof(SaveProject)} does not have a tree");

        //    if (string.IsNullOrWhiteSpace(projectFileName))
        //        throw new ArgumentException($"{nameof(SaveProject)} cannot have a null or empty value for '{nameof(projectFileName)}'");

        //    var serializer = new XmlGameDescriptorWriter();
        //    return serializer.WriteProject(Tree, projectFileName);
        //}

        public void CloseProject(ProjectTree projectTree) => 
            _solutionService.CloseProject(projectTree);

        public void CloseProjects()
        {
            foreach (var project in _solutionService.ProjectTrees.Values)
                _solutionService.CloseProject(project);
        }

        /// <summary>
        /// Creates a new folder with a default name under the given parentNodeModel
        /// </summary>
        /// <param name="parentNodeModel">Parent of the new folder</param>
        /// <returns>The new folder or null if it cannot be created</returns>
        public FolderNodeViewModel CreateNewFolder(TreeNodeViewModel parentNodeModel)
        {
            var parentNode = parentNodeModel.Node;

            if (FindNewChildResourceName(parentNode, "New Folder") is string name)
            {
                var folder = new ResourceFolder(name);
                var node = AddResource(parentNodeModel, folder) as FolderNodeViewModel;

                return node;
            }
            else
                return null;
        }

        public bool ApplyResourceRemovalChanges(IList<ResourceRemovalChange> changes)
        {
            return true;
        }

        public ResourceRemovalChangesViewModel GetResourceRemovalChanges(TreeNodeViewModel rootNodeModel, TreeNodeViewModel removeNodeModel)
        {
            if (removeNodeModel.Node is ImageProjectNodeViewModel)
                return null;

            var rootRemovalChange = new ResourceRemovalChange(removeNodeModel, true, false, false);
            var changes = new ResourceRemovalChangesViewModel(rootRemovalChange);

            var removedDict = SelfAndDescendants(removeNodeModel)
                .Select(x => new ResourceRemovalChange(x, true, false, false))
                .ToDictionary(key => key.Resource, val => val);

            // Palettes with removed DataFiles must be checked early, so that Arrangers are effected in the main loop by removed Palettes
            var removedPaletteNodes = SelfAndDescendants(rootNodeModel)
                .Where(x => x.Node.Value is Palette)
                .Where(x => removedDict.ContainsKey((x.Node.Value as Palette).DataFile));

            foreach (var palNode in removedPaletteNodes)
                removedDict[palNode.Node.Value] = new ResourceRemovalChange(palNode, true, false, false);

            changes.RemovedResources.AddRange(removedDict.Values);

            foreach (var node in SelfAndDescendants(rootNodeModel).Where(x => !removedDict.ContainsKey(x.Node.Value)))
            {
                var removed = false;
                var lostElements = false;
                var lostPalette = false;
                var resource = node.Node.Value;

                foreach (var linkedResource in resource.LinkedResources)
                {
                    if (removedDict.ContainsKey(linkedResource))
                    {
                        if (linkedResource is Palette && resource is Arranger)
                            lostPalette = true;
                        else if (linkedResource is DataFile && resource is Arranger)
                            lostElements = true;
                    }
                }

                if (removed || lostPalette || lostElements)
                {
                    var change = new ResourceRemovalChange(node, removed, lostPalette, lostElements);
                    changes.ChangedResources.Add(change);
                }
            }

            changes.HasRemovedResources = changes.RemovedResources.Count > 0;
            changes.HasChangedResources = changes.ChangedResources.Count > 0;

            return changes;
        }

        private string FindNewChildResourceName(IPathTreeNode<IProjectResource> node, string baseName)
        {
            return new string[] { baseName }
                .Concat(Enumerable.Range(1, 999).Select(x => $"{baseName} ({x})"))
                .FirstOrDefault(x => !node.ContainsChild(x));
        }

        public MagitekResult CanMoveNode(TreeNodeViewModel node, TreeNodeViewModel parentNode)
        {
            if (node is null)
                throw new ArgumentNullException($"{nameof(CanMoveNode)} parameter '{node}' was null");

            if (parentNode is null)
                throw new ArgumentNullException($"{nameof(CanMoveNode)} parameter '{parentNode}' was null");

            return _solutionService.CanMoveNode(node.Node, parentNode.Node);
        }

        /// <summary>
        /// Moves the specified node to the parentNode
        /// </summary>
        /// <param name="node">Node to be moved</param>
        /// <param name="parentNode">Parent to contain node after moving</param>
        public void MoveNode(TreeNodeViewModel node, TreeNodeViewModel parentNode)
        {
            _solutionService.MoveNode(node.Node, parentNode.Node);

            var oldParent = node.ParentModel;
            oldParent.Children.Remove(node);
            node.ParentModel = parentNode;
            parentNode.Children.Add(node);
        }

        public bool CanAddResource(IProjectResource resource)
        {
            if (resource is null || Tree is null)
                return false;

            if (Tree.Root.Children().Any(x => string.Equals(x.Name, resource.Name, StringComparison.OrdinalIgnoreCase)))
                return false;

            return true;
        }

        public TreeNodeViewModel AddResource(TreeNodeViewModel parentModel, IProjectResource resource)
        {
            var parentNode = parentModel.Node;
            var childNode = new PathTreeNode<IProjectResource>(resource.Name, resource);
            parentNode.AttachChild(childNode);

            TreeNodeViewModel childModel = resource switch
            {
                DataFile _ => new DataFileNodeViewModel(childNode, parentModel),
                ScatteredArranger _ => new ArrangerNodeViewModel(childNode, parentModel),
                Palette _ => new PaletteNodeViewModel(childNode, parentModel),
                ResourceFolder _ => new FolderNodeViewModel(childNode, parentModel),
                _ => throw new ArgumentException($"{nameof(AddResource)}: Cannot add a resource of type '{resource.GetType()}'")
            };

            parentModel.Children.Add(childModel);

            return childModel;
        }

        public IEnumerable<IPathTreeNode<IProjectResource>> Nodes() => Tree.EnumerateBreadthFirst();

        /// <summary>
        /// Depth-first tree traversal, returning leaf nodes before nodes higher in the hierarchy
        /// </summary>
        /// <param name="treeNode"></param>
        /// <returns></returns>
        private IEnumerable<TreeNodeViewModel> SelfAndDescendants(TreeNodeViewModel treeNode)
        {
            var nodeStack = new Stack<TreeNodeViewModel>();

            nodeStack.Push(treeNode);

            while (nodeStack.Count > 0)
            {
                var node = nodeStack.Pop();
                yield return node;
                foreach (var child in node.Children)
                    nodeStack.Push(child);
            }
        }
    }
}