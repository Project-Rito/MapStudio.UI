﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using ImGuiNET;
using MapStudio.UI;
using Toolbox.Core.ViewModels;
using GLFrameworkEngine;
using System.Reflection;
using OpenTK;
using Toolbox.Core;

namespace MapStudio.UI
{
    /// <summary>
    /// Represents a workspace instance of a workspace window.
    /// </summary>
    public class Workspace : IDisposable
    {
        public static Workspace ActiveWorkspace { get; set; }

        public bool UpdateDockLayout = true;

        /// <summary>
        /// The name of the workspace to display as the window title.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// System controller. Generally used for in tool animation playback and game calculations.
        /// </summary>
        public StudioSystem StudioSystem = new StudioSystem();

        /// <summary>
        /// The project resources to keep track of the project files.
        /// </summary>
        public ProjectResources Resources = new ProjectResources();

        /// <summary>
        /// Determines if the window is opened or not. If set to false, the window will close.
        /// </summary>
        public bool Opened = true;

        /// <summary>
        /// The dock ID used on the workspace window. Child docks link to this.
        /// </summary>
        public uint DockID;

        /// <summary>
        /// The creation ID for assigning a dock layout.
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// The window class reference for handling dock spaces.
        /// </summary>
        public unsafe ImGuiWindowClass* window_class;

        /// <summary>
        /// The parent OpenTK GameWindow
        /// </summary>
        public GameWindow ParentWindow;

        //Editors
        public List<IEditor> Editors = new List<IEditor>();

        public IEditor ActiveEditor { get; set; }

        Outliner Outliner { get; set; }
        Viewport ViewportWindow { get; set; }
        PropertyWindow PropertyWindow { get; set; }
        ConsoleWindow ConsoleWindow { get; set; }
        AssetViewWindow AssetViewWindow { get; set; }
        ToolWindow ToolWindow { get; set; }

        public List<Window> Windows = new List<Window>();
        public List<DockWindow> DockedWindows = new List<DockWindow>();

        public EventHandler ApplicationUserEntered;

        public Workspace(GlobalSettings settings, int id, GameWindow parentWindow)
        {
            ID = id;
            //Window docks
            PropertyWindow = new PropertyWindow();
            Outliner = new Outliner();
            ToolWindow = new ToolWindow();
            ConsoleWindow = new ConsoleWindow();
            AssetViewWindow = new AssetViewWindow();
            ViewportWindow = new Viewport(this, settings);
            ParentWindow = parentWindow;
            ViewportWindow.Pipeline._context.Scene.SelectionChanged += delegate
            {
                if (ViewportWindow.IsFocused)
                    ScrollToSelectedNode(GetSelected().FirstOrDefault());
            };

            //Confiure the layout placements
            Outliner.DockDirection = ImGuiDir.Left;
            Outliner.SplitRatio = 0.2f;

            ToolWindow.DockDirection = ImGuiDir.Down;
            ToolWindow.SplitRatio = 0.3f;
            ToolWindow.ParentDock = Outliner;

            PropertyWindow.DockDirection = ImGuiDir.Right;
            PropertyWindow.SplitRatio = 0.3f;

            ConsoleWindow.DockDirection = ImGuiDir.Down;
            ConsoleWindow.SplitRatio = 0.3f;

            AssetViewWindow.DockDirection = ImGuiDir.Down;
            AssetViewWindow.SplitRatio = 0.3f;

            this.DockedWindows.Add(Outliner);
            this.DockedWindows.Add(PropertyWindow);
            this.DockedWindows.Add(ConsoleWindow);
            this.DockedWindows.Add(AssetViewWindow);
            this.DockedWindows.Add(ToolWindow);
            this.DockedWindows.Add(ViewportWindow);

            foreach (var window in DockedWindows)
                window.Opened = true;

            this.Windows.Add(new StatisticsWindow());

            //Ignore the workspace setting as the editor handles files differently
            Outliner.ShowWorkspaceFileSetting = false;
            Workspace.ActiveWorkspace = this;
            StudioSystem.Instance = this.StudioSystem;
        }

        public void ReloadViewportMenu() { ViewportWindow.ReloadMenus(); }

        public List<MenuItemModel> GetDockToggles()
        {
            List<MenuItemModel> menus = new List<MenuItemModel>();
            foreach (var dock in DockedWindows)
            {
                var item = new MenuItemModel(dock.Name, () => {
                    dock.Opened = !dock.Opened;
                });
                item.IsChecked = dock.Opened;
            }
            return menus;
        }

        public List<MenuItemModel> GetViewportMenuIcons()
        {
            List<MenuItemModel> menus = new List<MenuItemModel>();
            menus.AddRange(ActiveEditor.GetViewportMenuIcons());
            return menus;
        }

        public List<MenuItemModel> GetEditMenus()
        {
            List<MenuItemModel> menus = new List<MenuItemModel>();
            menus.AddRange(ViewportWindow.GetEditMenuItems());
            menus.AddRange(ActiveEditor.GetEditMenuItems());
            return menus;
        }

        public void AddAssetCategory(IAssetCategory category)
        {
            this.AssetViewWindow.AddCategory(category);
        }

        public void ReloadOutliner()
        {

        }

        public bool LoadFileFormat(string filePath, bool isProject = false)
        {
            if (!File.Exists(filePath))
                return false;

            if (!isProject)
                Resources.ProjectFile.WorkingDirectory = Path.GetDirectoryName(filePath);

            IFileFormat fileFormat = Toolbox.Core.IO.STFileLoader.OpenFileFormat(filePath);
            if (fileFormat == null)
            {
                StudioLogger.WriteError(string.Format(TranslationSource.GetText("ERROR_FILE_UNSUPPORTED"), filePath));
                return false;
            }

            //Add the file to the project resources
            Resources.AddFile(fileFormat);

            //Make sure the file format path is at the working directory instead of the project path
            //So when the user saves the files directly, they will save to the original place.
            fileFormat.FileInfo.FilePath = $"{Resources.ProjectFile.WorkingDirectory}\\{fileFormat.FileInfo.FileName}";

            StudioLogger.WriteLine(string.Format(TranslationSource.GetText("LOADING_FILE"), filePath));

            if (fileFormat is ICustomFileEditor)
            {
                var editor = ((ICustomFileEditor)fileFormat).Editor;
                Editors.Add(editor);
                ActiveEditor = editor;
            } //Default file type to the model editor
            else if (fileFormat is IModelFormat)
            {
                var modelEditor = new ModelEditor();
                Editors.Add(modelEditor);
                ActiveEditor = modelEditor;

                modelEditor.ImportAsset(filePath);
            }

            InitEditors(filePath);
            LoadProjectResources();

            return true;
        }

        public void DrawMenu()
        {
          
        }

        public void CreateNewProject()
        {
            Name = "New Project";
            Resources = new ProjectResources();
            LoadProjectResources();
        }

        /// <summary>
        /// Scrolls the outliner to the selected node.
        /// </summary>
        public void ScrollToSelectedNode(NodeBase node)
        {
            Outliner.ScrollToSelected(node);
        }

        /// <summary>
        /// Updates the active workspace instance.
        /// This should be applied when a workspace window is focused.
        /// </summary>
        public static void UpdateActive(Workspace workspace)
        {
            ActiveWorkspace = workspace;
            //Update error logger on switch
            workspace.PrintErrors();
            //Update the system instance.
            StudioSystem.Instance = workspace.StudioSystem;
        }

        public void OnWindowLoaded()
        {
            ViewportWindow.SetActive();
        }

        /// <summary>
        /// Loads the file data into the current workspace
        /// </summary>
        public bool LoadProjectFile(string filePath)
        {
            InitEditors(filePath);
            LoadProjectResources();
            return true;
        }

        private void LoadProjectResources()
        {
            ReloadEditors();
            LoadEditorNodes();

            Outliner.UpdateScroll(0.0f, Resources.ProjectFile.OutlierScroll);
            OnWindowLoaded();

            ProcessLoading.Instance.Update(100, 100, "Finished loading!");
        }

        private void InitEditors(string filePath)
        {
            bool isProject = filePath.EndsWith(".json");

            ProcessLoading.Instance.Update(0, 100, "Loading Files");

            //Init the current file data
            string folder = System.IO.Path.GetDirectoryName(filePath);

            //Set current folder as project name
            if (isProject)
                Name = new DirectoryInfo(folder).Name;
            else
                Name = Path.GetFileName(filePath);

            //Load file resources
            if (isProject)
                Resources.LoadProject(filePath, ViewportWindow.Pipeline._context, this);
            else
                Resources.LoadFolder(folder, filePath);

            //Load the current workspace layout from the project file
            ActiveEditor.SubEditor = Resources.ProjectFile.SelectedWorkspace;
            //Current tool window to display
            ToolWindow.ToolDrawer = this.ActiveEditor.ToolWindowDrawer;

            ProcessLoading.Instance.Update(70, 100, "Loading Editors");

            ViewportWindow.ReloadMenus();
        }

        /// <summary>
        /// Checks and prints out any errors related to the current file data.
        /// </summary>
        public void PrintErrors()
        {
            StudioLogger.ResetErrors();
        }

        /// <summary>
        /// Reloads the current editor data.
        /// </summary>
        public void ReloadEditors()
        {
            if (ActiveEditor == null)
                return;

            Outliner.Nodes.Clear();
            Outliner.Nodes.AddRange(ActiveEditor.Nodes);

            Outliner.FilterMenuItems = ActiveEditor.GetFilterMenuItems();
            ToolWindow.ToolDrawer = ActiveEditor.ToolWindowDrawer;
        }

        /// <summary>
        /// Saves the file data of the active editor.
        /// </summary>
        public void SaveFileData()
        {
            //Apply editor data
            SaveEditorData(false);
            //Save each file format 
            foreach (var file in Resources.Files)
            {
                if (!file.CanSave)
                    continue;

                //Path doesn't exist so use a file dialog
                if (!File.Exists(file.FileInfo.FilePath))
                    SaveFileWithDialog();
                else
                    SaveFileData(file, file.FileInfo.FilePath);
            }
        }

        public void SaveFileWithDialog()
        {
            //Apply editor data
            SaveEditorData(false);
            //Save each file format 
            foreach (var file in Resources.Files)
            {
                if (!file.CanSave)
                    continue;

                SaveFileWithDialog(file);
            }
        }

        private void SaveFileWithDialog(IFileFormat fileFormat)
        {
            ImguiFileDialog sfd = new ImguiFileDialog() { SaveDialog = true };
            sfd.FileName = fileFormat.FileInfo.FileName;

            for (int i = 0; i < fileFormat.Extension.Length; i++)
                sfd.AddFilter(fileFormat.Extension[i], fileFormat.Description?.Length > i ? fileFormat.Description[i] : "");
            if (sfd.ShowDialog("SAVE_FILE"))
                SaveFileData(fileFormat, sfd.FilePath);
        }

        private void SaveFileData(IFileFormat fileFormat, string filePath)
        {
            //Save current file
            Toolbox.Core.IO.STFileSaver.SaveFileFormat(fileFormat, filePath);
            StudioLogger.WriteLine(string.Format(TranslationSource.GetText("SAVED_FILE"), filePath));

            TinyFileDialog.MessageBoxInfoOk($"File {filePath} has been saved!");

            PrintErrors();
        }

        public void SaveProject(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            Name = new DirectoryInfo(folderPath).Name;

            //Apply editor data
            SaveEditorData(true);
            //Save as project
            Resources.SaveProject($"{folderPath}\\Project.json", ViewportWindow.Pipeline._context, this);
            //Save the thumbnail in the current view
            var thumb = ViewportWindow.Pipeline.SaveAsScreenshot(720, 512);
            thumb.Save($"{folderPath}\\Thumbnail.png");
            //Update icon cache for thumbnails used
            IconManager.LoadTextureFile($"{folderPath}\\Thumbnail.png", 64, 64, true);

            PrintErrors();
        }

        private void SaveEditorData(bool isProject)
        {
            //Apply editor data
            ActiveEditor.OnSave(Resources);
            if (isProject)
            {
                Resources.ProjectFile.OutlierScroll = Outliner.ScrollY;

                //Reset node list
                Resources.ProjectFile.Nodes.Clear();
                //Save each node ID
                int nIndex = 0;
                foreach (var node in Outliner.Nodes)
                    SaveEditorNode(node, ref nIndex);
            }
        }

        //Load saved outliner node information like selection and expanded data.
        private void LoadEditorNodes()
        {
            //Each node in the hierachy will be checked via index
            int nIndex = 0;
            foreach (NodeBase node in Outliner.Nodes)
                LoadEditorNode(node, ref nIndex);
        }

        private void LoadEditorNode(NodeBase node, ref int nIndex)
        {
            nIndex++;

            if (Resources.ProjectFile.Nodes.ContainsKey(nIndex))
            {
                var n = Resources.ProjectFile.Nodes[nIndex];
                node.IsSelected = n.IsSelected;
                node.IsExpanded = n.IsExpaned;
            }

            foreach (NodeBase n in node.Children)
                LoadEditorNode(n, ref nIndex);
        }

        private void SaveEditorNode(NodeBase node, ref int nIndex)
        {
            nIndex++;

            Resources.ProjectFile.Nodes.Add(nIndex, new ProjectFile.NodeSettings()
            {
                IsExpaned = node.IsExpanded,
                IsSelected = node.IsSelected,
            });
            foreach (NodeBase n in node.Children)
                SaveEditorNode(n, ref nIndex);
        }

        /// <summary>
        /// Displays the workspace and all the docked windows given the current dock ID.
        /// </summary>
        public void Render(int workspaceID)
        {
            //Draw opened windows (non dockable)
            foreach (var window in Windows)
            {
                if (!window.Opened)
                    continue;

                if (ImGui.Begin(window.Name, ref window.Opened))
                {
                    window.Render();
                    ImGui.End();
                }
            }

            if (AssetViewWindow.IsFocused && AssetViewWindow.SelectedAsset != null)
                PropertyWindow.SelectedObject = AssetViewWindow.SelectedAsset;
            else
                PropertyWindow.SelectedObject = GetSelectedNode();

            //Draw dockable windows
            foreach (var dock in DockedWindows)
                if (dock.Opened)
                    LoadWindow(GetWindowName(dock.Name, workspaceID), ref dock.Opened, dock.Flags, () => dock.Render());
        }

        public NodeBase GetSelectedNode()
        {
            return GetSelected().FirstOrDefault();
        }

        public List<NodeBase> GetSelected()
        {
            List<NodeBase> selected = new List<NodeBase>();
            foreach (var node in Outliner.Nodes)
            {
                GetSelectedNode(node, ref selected);
            }
            return selected;
        }

        private void GetSelectedNode(NodeBase parent, ref List<NodeBase> selected)
        {
            if (parent.IsSelected && parent.Tag != null)
                selected.Add(parent);

            foreach (var child in parent.Children)
                GetSelectedNode(child, ref selected);

            return;
        }

        public void OnAssetViewportDrop()
        {
            var state = InputState.CreateMouseState();
            Vector2 screenPosition = new Vector2(state.Position.X, state.Position.Y);
            var asset = AssetViewWindow.DraggedAsset;
            ActiveEditor.AssetViewportDrop(asset, screenPosition);
        }

        public void OnLanguageChanged()
        {
        }

        /// <summary>
        /// The key event for when a key has been pressed down.
        /// Used to perform editor shortcuts.
        /// </summary>
        public void OnKeyDown(KeyEventInfo keyInfo, bool isRepeat)
        {
            if (Outliner.IsFocused)
                ViewportWindow.Pipeline._context.OnKeyDown(keyInfo, isRepeat);
            else if (ViewportWindow.IsFocused)
            {
                if (!isRepeat)
                    ActiveEditor.OnKeyDown(keyInfo);

                ViewportWindow.Pipeline._context.OnKeyDown(keyInfo, isRepeat);
                if (keyInfo.IsKeyDown(InputSettings.INPUT.Scene.ShowAddContextMenu))
                    ViewportWindow.LoadAddContextMenu();
            }
        }

        /// <summary>
        /// The mouse down event for when the mouse has been pressed down.
        /// Used to perform editor shortcuts.
        /// </summary>
        public void OnMouseDown(MouseEventInfo mouseInfo)
        {
            ActiveEditor.OnMouseDown(mouseInfo);
        }

        /// <summary>
        /// The mouse move event for when the mouse has been moved around.
        /// </summary>
        public void OnMouseMove(MouseEventInfo mouseInfo)
        {
            ActiveEditor.OnMouseMove(mouseInfo);
        }

        private bool init = false;
        public unsafe void InitWindowDocker(int index)
        {
            if (init)
                return;

            uint windowId = ImGui.GetID($"###window_{Name}{index}");

            ImGuiWindowClass windowClass = new ImGuiWindowClass();
            windowClass.ClassId = windowId;
            windowClass.DockingAllowUnclassed = 0;
            this.window_class = &windowClass;

            init = true;
        }

        private void LoadWindow(string name, ref bool opened, ImGuiWindowFlags windowFlags, Action action)
        {
            if (ImGui.Begin(name, ref opened, windowFlags)) {
                action.Invoke();
            }
            ImGui.End();
        }

        public void ReloadDockLayout(uint dockspaceId, int workspaceID)
        {
            ImGuiDockNodeFlags dockspace_flags = ImGuiDockNodeFlags.None;

            ImGui.DockBuilderRemoveNode(dockspaceId); // Clear out existing layout
            ImGui.DockBuilderAddNode(dockspaceId, dockspace_flags); // Add empty node

            // This variable will track the document node, however we are not using it here as we aren't docking anything into it.
            uint dock_main_id = dockspaceId;

            //Todo make these customizable from the dock list
        /*    var dock_right = ImGui.DockBuilderSplitNode(dock_main_id, ImGuiDir.Right, 0.2f, out uint nullL, out dock_main_id);
            var dock_left = ImGui.DockBuilderSplitNode(dock_main_id, ImGuiDir.Left, 0.3f, out uint nullR, out dock_main_id);
            var dock_down = ImGui.DockBuilderSplitNode(dock_main_id, ImGuiDir.Down, 0.3f, out uint nullU, out dock_main_id);
            var dock_down_left = ImGui.DockBuilderSplitNode(dock_left, ImGuiDir.Down, 0.3f, out uint nullUL, out dock_left);

            Outliner.DockID = dock_left;
            PropertyWindow.DockID = dock_right;
            ConsoleWindow.DockID = dock_down;
            AssetViewWindow.DockID = dock_down;
            ViewportWindow.DockID = dock_main_id;
            ToolWindow.DockID = dock_down_left;
            */
            foreach (var dock in DockedWindows)
            {
                if (dock.DockDirection == ImGuiDir.None)
                    dock.DockID = dock_main_id;
                else
                {
                    //Search for the same dock ID to reuse if possible
                    var dockedWindow = DockedWindows.FirstOrDefault(x => x != dock && x.DockDirection == dock.DockDirection && x.SplitRatio == dock.SplitRatio && x.ParentDock == dock.ParentDock);
                    if (dockedWindow != null && dockedWindow.DockID != 0)
                        dock.DockID = dockedWindow.DockID;
                    else if (dock.ParentDock != null)
                        dock.DockID = ImGui.DockBuilderSplitNode(dock.ParentDock.DockID, dock.DockDirection, dock.SplitRatio, out uint dockOut, out dock.ParentDock.DockID);
                    else
                        dock.DockID = ImGui.DockBuilderSplitNode(dock_main_id, dock.DockDirection, dock.SplitRatio, out uint dockOut, out dock_main_id);
                }
                ImGui.DockBuilderDockWindow(GetWindowName(dock.Name, workspaceID), dock.DockID);
            }

            ImGui.DockBuilderFinish(dockspaceId);

            UpdateDockLayout = false;
        }

        public void OnApplicationEnter() {
            ApplicationUserEntered?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            //Dispose files
            foreach (var file in Resources.Files)
                if (file is IDisposable)
                    ((IDisposable)file).Dispose();

            //Dispose renderables
            foreach (var render in DataCache.ModelCache.Values)
                render.Dispose();
            foreach (var tex in DataCache.TextureCache.Values)
                tex.Dispose();

            StudioSystem.Dispose();

            ViewportWindow.Dispose();
            Outliner.ActiveFileFormat = null;
            Outliner.Nodes.Clear();
            Outliner.SelectedNodes.Clear();
            DataCache.ModelCache.Clear();
            DataCache.TextureCache.Clear();
        }

        private string GetWindowName(string name, int id)
        {
            string text = TranslationSource.GetText(name);
            return $"{text}##{name}_{id}";
        }
    }
}