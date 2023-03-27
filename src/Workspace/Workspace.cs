﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Drawing;
using System.Threading.Tasks;
using ImGuiNET;
using MapStudio.UI;
using Toolbox.Core.ViewModels;
using GLFrameworkEngine;
using System.Reflection;
using OpenTK;
using Toolbox.Core;
using Toolbox.Core.Animations;
using UIFramework;

namespace MapStudio.UI
{
    /// <summary>
    /// Represents a workspace instance of a workspace window.
    /// </summary>
    public class Workspace : DockWindow, IDisposable
    {
        public EventHandler OnProjectLoad;
        public EventHandler OnProjectSave;

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
        /// Determines if the window can close or not during the dialog confirmation.
        /// </summary>
        public bool CanClose = false;

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

        private FileEditor _activeEditor;

        /// <summary>
        /// Represents the active file for editing.
        /// </summary>
        public FileEditor ActiveEditor
        {
            get { return _activeEditor; }
            set
            {
                if (_activeEditor != value)
                {
                    _activeEditor = value;
                    ReloadEditors();
                }
            }
        }

        /// <summary>
        /// The outliner tree for storing node data in a hiearchy.
        /// </summary>
        public Outliner Outliner { get; set; }

        /// <summary>
        /// The viewport for displaying 3D rendering.
        /// </summary>
        public Viewport ViewportWindow { get; set; }

        /// <summary>
        /// Property window for displaying current properties from a selected outliner node.
        /// </summary>
        public PropertyWindow PropertyWindow { get; set; }

        /// <summary>
        /// The console for printing out information, warnings and errors.
        /// </summary>
        public ConsoleWindow ConsoleWindow { get; set; }

        /// <summary>
        /// A window for storing assets such as models, textures, map objects, and materials.
        /// </summary>
        public AssetViewWindow AssetViewWindow { get; set; }

        /// <summary>
        /// A window for tool utilities.
        /// </summary>
        public ToolWindow ToolWindow { get; set; }

        /// <summary>
        /// A help window for explaining how to use the current editor.
        /// </summary>
        public HelpWindow HelpWindow { get; set; }

        public AnimationGraphWindow GraphWindow { get; set; }

        public TimelineWindow TimelineWindow { get; set; }

        public UVWindow UVWindow { get; set; }

        /// <summary>
        /// Tool menus for selecting different editor tools to use in the workspace.
        /// </summary>
        public List<MenuItemModel> WorkspaceTools = new List<MenuItemModel>();

        private MenuItemModel _activeWorkspaceTool;
        public MenuItemModel ActiveWorkspaceTool
        {
            get
            {
                if (_activeWorkspaceTool == null)
                    _activeWorkspaceTool = WorkspaceTools.FirstOrDefault();
                return _activeWorkspaceTool;
            }
            set {
                _activeWorkspaceTool = value;
            }
        }

        /// <summary>
        /// A window list of popup windows in the workspace.
        /// </summary>
        public List<Window> Windows = new List<Window>();
        public List<DockWindow> DockWindows = new List<DockWindow>();

        private static ColorCycle WorkspaceColorCycle = new ColorCycle();
        public STAnimation GetActiveAnimation() => GraphWindow.ActiveAnimation;

        public string ID = "Space";

        public override string GetWindowID() => ID;

        public Workspace(GlobalSettings settings, string name) : base(name)
        {

            //Window docks

            ParentWindow = parentWindow;
            ViewportWindow.Pipeline._context.Scene.SelectionUIChanged += (o, e) =>
            {
                //Assign the active file format if outliner has it selected
                if (Outliner.SelectedNode != null)
                {
                    //Select the active file to edit if one is selected
                    if (Outliner.SelectedNode.Tag is FileEditor && Outliner.SelectedNode.Tag != ActiveEditor)
                        ActiveEditor = (FileEditor)Outliner.SelectedNode.Tag;

                    //Select an animation for playback in the timeline window
                    if (Outliner.SelectedNode.Tag is STAnimation) {
                        TimelineWindow.AddAnimation((STAnimation)Outliner.SelectedNode.Tag);
                        GraphWindow.AddAnimation((STAnimation)Outliner.SelectedNode.Tag);

                    }
                    //Load a material to the UV window if one is selected
                    if (Outliner.SelectedNode.Tag is STGenericMaterial) {
                        UVWindow.Load((STGenericMaterial)Outliner.SelectedNode.Tag);
                    }
                    //Load a mesh to the UV window if one is selected
                    if (Outliner.SelectedNode.Tag is STGenericMesh) {
                        UVWindow.Load((STGenericMesh)Outliner.SelectedNode.Tag);
                    }
                }
                PropertyWindow.SelectedObject = Outliner.SelectedNode;
            };

            ToolWindow.UIDrawer += delegate {
                this.ActiveEditor?.DrawToolWindow();
            };

            ViewportWindow.DrawViewportMenuBar += delegate {
                this.ActiveEditor?.DrawViewportMenuBar();
            };

            HelpWindow.UIDrawer += delegate
            {
                this.ActiveEditor?.DrawHelpWindow();
            };

            this.DockedWindows.Add(Outliner);
            this.DockedWindows.Add(PropertyWindow);
            this.DockedWindows.Add(ConsoleWindow);
            this.DockedWindows.Add(AssetViewWindow);
            this.DockedWindows.Add(ToolWindow);
            this.DockedWindows.Add(ViewportWindow);
            this.DockedWindows.Add(HelpWindow);

            ReloadDefaultDockSettings();

            this.Windows.Add(new StatisticsWindow());

            //Ignore the workspace setting as the editor handles files differently
            Outliner.ShowWorkspaceFileSetting = false;
            Workspace.ActiveWorkspace = this;
            StudioSystem.Instance = this.StudioSystem;
        }

        public override void Render()
        {
            //Draw opened windows (non dockable)
            foreach (var window in Windows)
            {
                if (!window.Opened)
                    continue;

                if (ImGui.Begin(window.Name, ref window.Opened, window.Flags))
                {
                    window.Render();
                }
                ImGui.End();
            }

            if (ImGui.IsWindowFocused())
                Workspace.UpdateActive(this);

            if (ViewportWindow.IsFocused)
                PropertyWindow.SelectedObject = GetSelectedNode();

            base.Render();
        }

        public void ReloadDefaultDockSettings()
        {
            //Confiure the layout placements
            UVWindow.DockDirection = ImGuiDir.Down;
            UVWindow.ParentDock = Outliner;
            UVWindow.SplitRatio = 0.3f;

            Outliner.DockDirection = ImGuiDir.Left;
            Outliner.SplitRatio = 0.2f;

            ToolWindow.DockDirection = ImGuiDir.Up;
            ToolWindow.SplitRatio = 0.3f;
            ToolWindow.ParentDock = PropertyWindow;

            PropertyWindow.DockDirection = ImGuiDir.Right;
            PropertyWindow.SplitRatio = 0.3f;

            ConsoleWindow.DockDirection = ImGuiDir.Down;
            ConsoleWindow.SplitRatio = 0.3f;

            AssetViewWindow.DockDirection = ImGuiDir.Down;
            AssetViewWindow.SplitRatio = 0.3f;

            this.DockWindows.Add(Outliner);
            this.DockWindows.Add(PropertyWindow);
            this.DockWindows.Add(ConsoleWindow);
            this.DockWindows.Add(AssetViewWindow);
            this.DockWindows.Add(ToolWindow);
            this.DockWindows.Add(ViewportWindow);
            HelpWindow.DockDirection = ImGuiDir.Down;
            HelpWindow.ParentDock = Outliner;
            HelpWindow.SplitRatio = 0.3f;

            foreach (var window in DockWindows)
                window.Opened = true;

            this.Windows.Add(new StatisticsWindow());

            //Ignore the workspace setting as the editor handles files differently
            Outliner.ShowWorkspaceFileSetting = false;
            Workspace.ActiveWorkspace = this;
            StudioSystem.Instance = this.StudioSystem;
            this.UpdateDockLayout = true;
        }

        public void ReloadViewportMenu() { ViewportWindow.ReloadMenus(); }

        /// <summary>
        /// Gets a list of menu items for toggling dock windows.
        /// </summary>
        public List<MenuItemModel> GetDockToggles()
        {
            List<MenuItemModel> menus = new List<MenuItemModel>();
            foreach (var dock in DockWindows)
            {
                var item = new MenuItemModel(dock.Name, () => {
                    dock.Opened = !dock.Opened;
                });
                item.IsChecked = dock.Opened;
            }
            return menus;
        }

        /// <summary>
        /// Gets a list of menu items for viewport icons.
        /// </summary>
        public List<MenuItemModel> GetViewportMenuIcons()
        {
            List<MenuItemModel> menus = new List<MenuItemModel>();
            if (ActiveEditor != null)
            menus.AddRange(ActiveEditor.GetViewportMenuIcons());
            return menus;
        }

        public List<MenuItemModel> GetEditMenus()
        {
            List<MenuItemModel> menus = new List<MenuItemModel>();
            if (ViewportWindow != null)
            menus.AddRange(ViewportWindow.GetEditMenuItems());
            if (ActiveEditor != null)
            menus.AddRange(ActiveEditor.GetEditMenuItems());
            return menus;
        }

        /// <summary>
        /// Gets a list of menu items for general view adjusting.
        /// </summary>
        public List<MenuItemModel> GetViewMenuItems()
        {
            List<MenuItemModel> menus = new List<MenuItemModel>();
            menus.AddRange(ActiveEditor.GetViewMenuItems());
            return menus;
        }
        
        /// <summary>
        /// Adds an asset category to the asset window.
        /// </summary>
        public void AddAssetCategory(IAssetLoader category) {
            this.AssetViewWindow.AddCategory(category);
        }

        /// <summary>
        /// Loads a file format into the current workspace.
        /// </summary>
        public FileEditor LoadFileFormat(string filePath, bool isProject = false)
        {
            if (!File.Exists(filePath))
                return null;

            if (!isProject)
                Resources.ProjectFile.WorkingDirectory = Path.GetDirectoryName(filePath);

            IFileFormat fileFormat = Toolbox.Core.IO.STFileLoader.OpenFileFormat(filePath);
            if (fileFormat == null)
            {
                StudioLogger.WriteError(string.Format(TranslationSource.GetText("ERROR_FILE_UNSUPPORTED"), filePath));
                return null;
            }
            //File must be an editor. Todo I need to find a more intutive way for this to work.
            var editor = fileFormat as FileEditor;
            if (editor == null)
                return null;

            ActiveEditor = editor;

            //Node per file editor
            editor.Root.Header = fileFormat.FileInfo.FileName;
            editor.Root.Tag = fileFormat;

            if (fileFormat is IArchiveFile)
                ArchiveEditor.Load((IArchiveFile)fileFormat, editor.Root);

            //Add the file to the project resources
            Resources.AddFile(fileFormat);

            //Make sure the file format path is at the working directory instead of the project path
            //So when the user saves the files directly, they will save to the original place.
            if (!isProject)
                fileFormat.FileInfo.FilePath = $"{Resources.ProjectFile.WorkingDirectory}/{fileFormat.FileInfo.FileName}";

            StudioLogger.WriteLine(string.Format(TranslationSource.GetText("LOADING_FILE"), filePath));

            SetupActiveEditor(editor);

            //Add to viewport filter list
            if (!(fileFormat is IArchiveFile))
                ViewportWindow.Pipeline.AddFile(editor, Path.GetFileNameWithoutExtension(filePath));

            StudioLogger.WriteLine(string.Format(TranslationSource.GetText("LOADING_FILE"), filePath));

            InitEditors(filePath);
            LoadProjectResources();

            return editor;
        }

        public void SetupActiveEditor(FileEditor editor)
        {
            //Add nodes to outliner
            Outliner.Nodes.Add(editor.Root);

            //Init the gl scene
            editor.Scene.Init();

            //Viewport on selection changed
            editor.Scene.SelectionUIChanged = null;
            editor.Scene.SelectionUIChanged += (o, e) =>
            {
                if (o == null)
                {
                    return;
                }

                if (ViewportWindow.IsFocused)
                {
                    if (!KeyInfo.EventInfo.KeyCtrl && !!KeyInfo.EventInfo.KeyShift)
                        Outliner.DeselectAll();
                    ScrollToSelectedNode((NodeBase)o);
                }

                if (!Outliner.SelectedNodes.Contains((NodeBase)o))
                {
                    Outliner.AddSelection((NodeBase)o);
                }

                //Update the property window.
                //This also updated in the outliner but the outliner doesn't have to be present this way
                if (o != null)
                    PropertyWindow.SelectedObject = (NodeBase)o;
            };
            GLContext.ActiveContext.Scene = editor.Scene;

            //Set active file format
            editor.SetActive();
            //Update editor viewport menus
            ReloadViewportMenu();
        }

        public void CreateNewProject(Type editor, Action<bool> onProjectCreated)
        {
            Name = "New Project";
            Resources = new ProjectResources();
            Resources.ProjectFile.WorkingDirectory = Runtime.ExecutableDir;
            LoadProjectResources();

            ActiveEditor = (FileEditor)Activator.CreateInstance(editor);

            //Editors must be IFileFormat types for loading/saving
            if (ActiveEditor is IFileFormat)
            {
                ((IFileFormat)ActiveEditor).FileInfo = new File_Info();
                //Add to project files for saving project data.
                Resources.AddFile((IFileFormat)ActiveEditor);
            }
            ViewportWindow.Pipeline.AddFile(ActiveEditor, "NewProject");

            //Editors can have an optional dialog before creating new files.
            if (ActiveEditor.RenderNewFileDialog != null)
            {
                DialogHandler.Show(Name, 500, 105, () =>
                {
                    ActiveEditor.RenderNewFileDialog.Invoke();
                }, (ok) =>
                {
                    if (!ok)
                    {
                        onProjectCreated?.Invoke(false);
                        return;
                    }

                    ProcessLoading.Instance.IsLoading = true;

                    UIManager.ActionExecBeforeUIDraw += delegate
                    {
                        ActiveEditor.CreateNew();
                        SetupActiveEditor(ActiveEditor);
                        ActiveEditor.AfterLoaded();

                        onProjectCreated?.Invoke(true);
                    };
                });
            }
            else
            {
                ProcessLoading.Instance.IsLoading = true;

                ActiveEditor.CreateNew();
                SetupActiveEditor(ActiveEditor);
                onProjectCreated?.Invoke(true);
            }
        }

        /// <summary>
        /// Scrolls the outliner to the selected node.
        /// </summary>
        public void ScrollToSelectedNode(NodeBase node)
        {
            if (node.Parent == null)
                return;

            Outliner.ScrollToSelected(node);
        }

        /// <summary>
        /// Updates the active workspace instance.
        /// This should be applied when a workspace window is focused.
        /// </summary>
        public static void UpdateActive(Workspace workspace)
        {
            if (ActiveWorkspace == workspace)
                return;

            ActiveWorkspace = workspace;
            //Update error logger on switch
            workspace.PrintErrors();
            //Update the system instance.
            StudioSystem.Instance = workspace.StudioSystem;
            workspace.OnWindowLoaded();
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
            OnProjectLoad?.Invoke(this, EventArgs.Empty);

            Outliner.UpdateScroll(0.0f, Resources.ProjectFile.OutlierScroll);
            OnWindowLoaded();

            ProcessLoading.Instance.Update(100, 100, "Finished loading!");
        }

        private void InitEditors(string filePath)
        {
            bool isProject = filePath.EndsWith("Project.json");

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

            if (ActiveEditor != null)
            {
                //Load the current workspace layout from the project file
                ActiveEditor.SubEditor = Resources.ProjectFile.SelectedWorkspace;
                //Current tool window to display
                ToolWindow.ToolDrawer = this.ActiveEditor.ToolWindowDrawer;
            }

            ProcessLoading.Instance.Update(70, 100, "Loading Editors");

            ViewportWindow.ReloadMenus();
        }

        /// <summary>
        /// Checks and prints out any errors related to the current file data.
        /// </summary>
        public void PrintErrors()
        {
            StudioLogger.ResetErrors();
            ActiveEditor.PrintErrors();
        }

        /// <summary>
        /// Reloads the current editor data.
        /// </summary>
        public void ReloadEditors()
        {
            if (ActiveEditor == null)
                return;

            PropertyWindow.SelectedObject = null;
            Outliner.FilterMenuItems = ActiveEditor.GetFilterMenuItems();

            ActiveEditor.SetActive();

            var docks = ActiveEditor.PrepareDocks();
            //A key to check if the existing layout changed
            string key = string.Join("", docks.Select(x => x.ToString()));
            string currentKey = string.Join("", DockWindows.Select(x => x.ToString()));
            if (key != currentKey)
            {
                DockWindows = docks;
                UpdateDockLayout = true;
            }
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
                if (Resources.ProjectFolder == null && !File.Exists(file.FileInfo.FilePath))
                SaveFileWithDialog(file);
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
            if (fileFormat.FileInfo.ParentArchive != null)
                fileFormat = fileFormat.FileInfo.ParentArchive as IFileFormat;

            ImguiFileDialog sfd = new ImguiFileDialog() { SaveDialog = true };
            sfd.FileName = fileFormat.FileInfo.FileName;

            for (int i = 0; i < fileFormat.Extension.Length; i++)
                sfd.AddFilter(fileFormat.Extension[i], fileFormat.Description?.Length > i ? fileFormat.Description[i] : "");
            if (sfd.ShowDialog("SAVE_FILE"))
                SaveFileData(fileFormat, sfd.FilePath);
        }

        private void SaveFileData(IFileFormat fileFormat, string filePath)
        {
            fileFormat.FileInfo.FilePath = filePath;

            //Apply editor data
            SaveEditorData(false);

            PrintErrors();

            string errorLog = StudioLogger.GetErrorLog();

            string name = Path.GetFileName(filePath);

            ProcessLoading.Instance.IsLoading = true;
            ProcessLoading.Instance.Update(0, 100, $"Saving {name}", "Saving");

            try
            {
                //Save current file
            Toolbox.Core.IO.STFileSaver.SaveFileFormat(fileFormat, filePath);
            StudioLogger.WriteLine(string.Format(TranslationSource.GetText("SAVED_FILE"), filePath));

            if (!string.IsNullOrEmpty(errorLog))
                TinyFileDialog.MessageBoxErrorOk($"{errorLog}");

            ProcessLoading.Instance.Update(100, 100, $"Saving {name}", "Saving");
            ProcessLoading.Instance.IsLoading = false;

            TinyFileDialog.MessageBoxInfoOk(string.Format(TranslationSource.GetText($"SAVED_FILE"), filePath));

            PrintErrors();
        }

        public void SaveProject(string folderPath)
        {
            Resources.ProjectFolder = folderPath;

            if (!Directory.Exists(folderPath))
            {
                try
                {
                Directory.CreateDirectory(folderPath);
                }
                catch
                {
                    TinyFileDialog.MessageBoxInfoOk($"{string.Format(TranslationSource.GetText("ERROR_CREATE_DIRECTORY"), folderPath)}\n{TranslationSource.GetText("NOTHING_SAVED")}");
                    return;
                }
            }

            Name = new DirectoryInfo(folderPath).Name;

            //Apply editor data
            SaveEditorData(true);
            //Save as project
            OnProjectSave?.Invoke(this, EventArgs.Empty);
            Resources.SaveProject(Path.Combine(folderPath,"Project.json"), ViewportWindow.Pipeline._context, this);
            //Save the thumbnail in the current view
            var thumb = ViewportWindow.SaveAsScreenshot(720, 512);
            thumb.Save(Path.Combine(folderPath,"Thumbnail.png"));

            //Update icon cache for thumbnails used
            IconManager.LoadTextureFile(Path.Combine(folderPath,"Thumbnail.png"), 64, 64, true);
            SaveFileData();

            PrintErrors();
        }

        /// <summary>
        /// Updates asset paths to point to a new project directory
        /// </summary>
        public void UpdateProjectAssetPaths(string oldDir, string newDir)
        {
            foreach (IFileFormat file in Resources.Files)
            {
                if (file.FileInfo.FilePath == null)
                    continue;
                string oldDirFullPath = Path.GetFullPath(oldDir); // FullPath to ensure everything is consistently named, for use with StartsWith()
                string newDirFullPath = Path.GetFullPath(newDir);
                string fileFullPath = Path.GetFullPath(file.FileInfo.FilePath);
                if (fileFullPath.StartsWith(oldDirFullPath))
                    file.FileInfo.FilePath = fileFullPath.Replace(oldDirFullPath, newDirFullPath);
            }
        }

        private void SaveEditorData(bool isProject)
        {
            //Apply editor data
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

        {
        }
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

            return; // This is just funny tbh
        }

        public void OnAssetViewportDrop()
        {
            InputState.UpdateMouseState();
            Vector2 screenPosition = new Vector2(MouseEventInfo.Position.X, MouseEventInfo.Position.Y);
            var asset = AssetViewWindow.DraggedAsset;

            //Handle the action before UI draw incase we want to update the UI (ie progress bar)
            UIManager.ActionExecBeforeUIDraw = () =>
            {
                ActiveEditor.AssetViewportDrop(asset, screenPosition);
            };
        }

        public void OnLanguageChanged()
        {
        }

        /// <summary>
        /// The key event for when a key has been pressed down.
        /// Used to perform editor shortcuts.
        /// </summary>
        public void OnKeyDown(KeyEventInfo e, bool isRepeat)
        {
            if (Outliner.IsFocused)
                ViewportWindow.Pipeline._context.OnKeyDown(e, isRepeat);
            else if (ViewportWindow.IsFocused)
            {
                if (!isRepeat)
                    ActiveEditor?.OnKeyDown(e);

                ViewportWindow.Pipeline._context.OnKeyDown(e, isRepeat);
                if (KeyInfo.EventInfo.IsKeyDown(InputSettings.INPUT.Scene.ShowAddContextMenu))
                    ViewportWindow.LoadAddContextMenu();
            }
        }

        {
        }

        /// <summary>
        /// The mouse down event for when the mouse has been pressed down.
        /// Used to perform editor shortcuts.
        /// </summary>
        public void OnMouseDown()
        {
            ActiveEditor?.OnMouseDown();
        }

        /// <summary>
        /// The mouse up event for when the mouse has been released.
        /// Used to perform editor shortcuts.
        /// </summary>
        public void OnMouseUp()
        {
            ActiveEditor?.OnMouseUp();
        }

        /// <summary>
        /// The mouse move event for when the mouse has been moved around.
        /// </summary>
        public void OnMouseMove()
        {
            ActiveEditor?.OnMouseMove();
        }

        public void OnMouseWheel()
        {
            ActiveEditor?.OnMouseWheel();
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
            foreach (var dock in DockWindows)
            {
                if (dock.DockDirection == ImGuiDir.None)
                    dock.DockID = dock_main_id;
                else
        {
                    //Search for the same dock ID to reuse if possible
                    var dockedWindow = DockWindows.FirstOrDefault(x => x != dock && x.DockDirection == dock.DockDirection && x.SplitRatio == dock.SplitRatio && x.ParentDock == dock.ParentDock);
                    if (dockedWindow != null && dockedWindow.DockID != 0)
                        dock.DockID = dockedWindow.DockID;
                    else if (dock.ParentDock != null)
                        dock.DockID = ImGui.DockBuilderSplitNode(dock.ParentDock.DockID, dock.DockDirection, dock.SplitRatio, out uint dockOut, out dock.ParentDock.DockID);
                    else
                        dock.DockID = ImGui.DockBuilderSplitNode(dock_main_id, dock.DockDirection, dock.SplitRatio, out uint dockOut, out dock_main_id);
                }
                ImGui.DockBuilderDockWindow(GetWindowName(dock.Name), dock.DockID);
            }

            ImGui.DockBuilderFinish(dockspaceId);

            UpdateDockLayout = false;
        }

        public void OnApplicationEnter() {
            ApplicationUserEntered?.Invoke(this, EventArgs.Empty);
        }

        public new void Dispose()
        {
            //Dispose files
            foreach (var file in Resources.Files)
                if (file is IDisposable)
                    ((IDisposable)file).Dispose();

            //Dispose renderables
            foreach (var render in DataCache.ModelCache.Values)
                render.Dispose();
            foreach (var texGroup in DataCache.TextureCache.Values)
                foreach (var tex in texGroup.Values)
                    tex.RenderTexture.Dispose();

            StudioSystem.Dispose();

            TimelineWindow.Dispose();
            GraphWindow.Dispose();
            ViewportWindow.Dispose();
            Outliner.ActiveFileFormat = null;
            Outliner.Nodes.Clear();
            Outliner.SelectedNodes.Clear();
            DataCache.ModelCache.Clear();
            DataCache.TextureCache.Clear();

            base.Dispose();
        }
            
        private string GetWindowName(string name)
        {
            string text = TranslationSource.GetText(name);
            return $"{text}##{name}_{DockID}";
        }
    }
}
