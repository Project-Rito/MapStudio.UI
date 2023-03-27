﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Toolbox.Core;
using Toolbox.Core.ViewModels;

namespace MapStudio.UI
{
    internal class ArchiveEditor
    {
        public static void Load(IArchiveFile file, NodeBase root)
        {
            CreateObjectHiearchy(root, file);
        }

        static NodeBase CreateObjectHiearchy(NodeBase parent, IArchiveFile archiveFile)
        {
            // build a TreeNode collection from the file list
            foreach (var file in archiveFile.Files)
            {
                string[] paths = file.FileName.Split('/');
                ProcessTree(parent, file, paths, 0);
            }
            return parent;
        }

        static void ProcessTree(NodeBase parent, ArchiveFileInfo file, string[] paths, int index)
        {
            string currentPath = paths[index];
            if (paths.Length - 1 == index)
            {
                var fileNode = new FileNode(currentPath, file);
                string ext = Utils.GetExtension(currentPath);
                if (string.IsNullOrEmpty(Path.GetExtension(file.FileName)))
                    fileNode.Header += ".bin";

                parent.AddChild(fileNode);
                return;
            }

            var node = FindFolderNode(parent, currentPath);
            if (node == null)
            {
                node = new FolderNode(currentPath);
                node.Icon = "Folder";
                parent.AddChild(node);
            }

            ProcessTree(node, file, paths, index + 1);
        }

        private static NodeBase FindFolderNode(NodeBase parent, string path)
        {
            NodeBase node = null;
            foreach (var child in parent.Children.ToArray())
            {
                if (child.Header.Equals(path))
                {
                    node = child;
                    break;
                }
            }

            return node;
        }

        class FolderNode : NodeBase
        {
            public FolderNode(string name) : base(name)
            {
                Icon = IconManager.FOLDER_ICON.ToString();
            }
        }

        class FileNode : NodeBase
        {
            MemoryEditor MemoryEditor = new MemoryEditor();

            /// <summary>
            /// The attached file information from an archive file.
            /// </summary>
            private ArchiveFileInfo FileInfo;

            public FileNode(string name, ArchiveFileInfo fileInfo) : base(name)
            {
                CanRename = fileInfo.ParentArchiveFile.CanRenameFiles;
                FileInfo = fileInfo;
                Tag = fileInfo;
                Icon = IconManager.FILE_ICON.ToString();
                if (!string.IsNullOrEmpty(FileInfo.Icon))
                    Icon = FileInfo.Icon;

                IconColor = FileInfo.IconColor;

                if (name.EndsWith(".byaml") || name.EndsWith(".byml"))
                    IconColor = new System.Numerics.Vector4(0.564f, 0.792f, 0.97f, 1);

                if (name.EndsWith(".bcmdl") || name.EndsWith(".bch"))
                {
                    IconColor = new System.Numerics.Vector4(1, 0.5f, 0, 1);
                    Icon = '\uf1b2'.ToString();
                }


                TagUI.UIDrawer += delegate
                {
                    RenderHexView();
                };
                ContextMenus.Add(new MenuItemModel("Rename", () => {
                    ActivateRename = true;
                })
                { IsEnabled = fileInfo.ParentArchiveFile.CanRenameFiles });

                ContextMenus.Add(new MenuItemModel("Export Raw Data", Export));
                ContextMenus.Add(new MenuItemModel("Export Raw Data to File Location", ExportToFileLocation));

                ContextMenus.Add(new MenuItemModel("Replace Raw Data", Replace) { IsEnabled = fileInfo.ParentArchiveFile.CanReplaceFiles });
                ContextMenus.Add(new MenuItemModel(""));
                ContextMenus.Add(new MenuItemModel("Delete", Delete) { IsEnabled = fileInfo.ParentArchiveFile.CanDeleteFiles });

                OnSelected += delegate  
                {
                    //TODO this will work better when the file editor gets switched better for opened files
                   // if (IsSelected)
                        //Workspace.ActiveWorkspace.ActiveEditor = FileInfo.ParentArchiveFile as FileEditor;
                };
                OnHeaderRenamed += delegate
                {
                    FileInfo.FileName = GetFullPath(this);
                };
            }

            private static string GetFullPath(NodeBase node)
            {
                return String.Join(Path.AltDirectorySeparatorChar, GetPaths(node));
            }

            private static List<string> GetPaths(NodeBase node)
            {
                List<string> paths = new List<string>();
                if (node.Parent != null && node.Parent is FolderNode)
                    GetParentPaths(ref paths, node.Parent);
                paths.Add(node.Header);
                return paths;
            }

            private static void GetParentPaths(ref List<string> paths , NodeBase node)
            {
                if (node.Parent != null && node.Parent is FolderNode)
                    GetParentPaths(ref paths, node.Parent);
                paths.Add(node.Header);
            }

            private void Export()
            {
                ImguiFileDialog dlg = new ImguiFileDialog();
                dlg.SaveDialog = true;
                dlg.FilterAll = true;
                dlg.FileName = this.Header;
                if (dlg.ShowDialog())
                    ExportFile(dlg.FilePath);
            }

            private void ExportToFileLocation()
            {
                string folderPath = ((IFileFormat)FileInfo.ParentArchiveFile).FileInfo.FolderPath;
                if (!Directory.Exists(folderPath))
                    return;

                string filePath = Path.Combine(folderPath, Path.GetFileName(this.Header));
                ExportFile(filePath);
            }

            private void ExportFile(string filePath)
            {
                if (FileInfo.FileFormat != null && FileInfo.FileFormat.CanSave)
                {
                    var mem = new MemoryStream();
                    FileInfo.FileFormat.Save(mem);
                    File.WriteAllBytes(filePath, mem.ToArray());
                }
                else
                {
                    File.WriteAllBytes(filePath, FileInfo.AsBytes());
                }
            }

            private void Replace()
            {
                ImguiFileDialog dlg = new ImguiFileDialog();
                dlg.FilterAll = true;
                if (dlg.ShowDialog())
                {
                    FileInfo.SetData(File.ReadAllBytes(dlg.FilePath));
                    if (FileInfo.FileFormat != null)
                        OpenFile();
                }
            }

            private void Delete()
            {
                int result = TinyFileDialog.MessageBoxInfoYesNo($"Are you sure you want to remove file {Header}? This operation cannot be undone.");
                if (result != 1)
                    return;

                FileInfo.ParentArchiveFile.DeleteFile(FileInfo);
                this.Parent.Children.Remove(this);
            }

            public override void OnDoubleClicked()
            {
                if (FileInfo.FileFormat == null)
                    OpenFile();
            }

            private void OpenFile()
            {
                FileInfo.FileFormat = FileInfo.OpenFile();
                if (FileInfo.FileFormat == null)
                    return;

                var editor = FileInfo.FileFormat as FileEditor;
                editor.Scene.Init();

                this.Tag = FileInfo.FileFormat;
                this.TagUI.Tag = editor.Root.TagUI;

                this.Children.Clear();
                foreach (var child in editor.Root.Children)
                    this.Children.Add(child);

                Workspace.ActiveWorkspace.ViewportWindow.Pipeline.AddFile(editor, this.Header);
                Workspace.ActiveWorkspace.ActiveEditor = editor;
            }

            public void SaveFile()
            {
                if (FileInfo.FileFormat != null && FileInfo.FileFormat.CanSave)
                {
                    var mem = new MemoryStream();
                    FileInfo.FileFormat.Save(mem);
                    //Compress if necessary
                    FileInfo.FileData = FileInfo.CompressData(mem);
                }
            }

            private void RenderHexView()
            {
                byte[] data = FileInfo.AsBytes();
                MemoryEditor.Draw(data, data.Length);
            }
        }
    }
}
