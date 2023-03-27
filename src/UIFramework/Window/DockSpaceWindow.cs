﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ImGuiNET;

namespace UIFramework
{
    /// <summary>
    /// Represents a window to dock multiple docking windows.
    /// </summary>
    public class DockSpaceWindow : Window, IDisposable
    {
        /// <summary>
        /// A list of dockable windows that can dock to this dockspace.
        /// </summary>
        public List<DockWindow> DockedWindows = new List<DockWindow>();

        /// <summary>
        /// Determines to reload the dock layout or not.
        /// </summary>
        public bool UpdateDockLayout = false;

        public bool IsFullScreen = false;

        public unsafe ImGuiWindowClass* window_class;

        public DockSpaceWindow(string name)  {
            Name = name;
        }

        public override void Render()
        {
            uint dockspaceId = ImGui.GetID($"{Name}dock_layout");            
            unsafe
            {
                //Check if the dock has been created or needs to be updated
                if (ImGui.DockBuilderGetNode(dockspaceId).NativePtr == null || this.UpdateDockLayout)
                {
                    ReloadDockLayout(dockspaceId);
                }
                ImGui.DockSpace(dockspaceId, new System.Numerics.Vector2(0, 0),
                    ImGuiDockNodeFlags.CentralNode, window_class);
            }

            if (IsFullScreen)
            {
                var fullDock = DockedWindows.FirstOrDefault(x => x.IsFullScreen);
                fullDock.Show();
                return;
            }
            else
            {
                foreach (var window in DockedWindows)
                    window.Show();
            }
        }

        public unsafe void SetupParentDock(uint parentDockID, IEnumerable<DockSpaceWindow> children)
        {
            //Check if the dock has been created or needs to be updated
            if (ImGui.DockBuilderGetNode(parentDockID).NativePtr == null || UpdateDockLayout)
            {
                ImGuiDockNodeFlags dockspace_flags = ImGuiDockNodeFlags.None;
                uint main_id = parentDockID;

                ImGui.DockBuilderRemoveNode(parentDockID); // Clear out existing layout
                ImGui.DockBuilderAddNode(parentDockID, dockspace_flags); // Add empty node

                foreach (var workspace in children)
                    ImGui.DockBuilderDockWindow(workspace.GetWindowName(), main_id);

                ImGui.DockBuilderFinish(parentDockID);
                UpdateDockLayout = false;
            }

            unsafe
            {
                //Create an inital dock space for docking workspaces.
                ImGui.DockSpace(parentDockID, new System.Numerics.Vector2(0.0f, 0.0f), 0, window_class);
            }
        }

        public void ReloadDockLayout(uint dockspaceId)
        {
            ImGuiDockNodeFlags dockspace_flags = ImGuiDockNodeFlags.None;

            ImGui.DockBuilderRemoveNode(dockspaceId); // Clear out existing layout
            ImGui.DockBuilderAddNode(dockspaceId, dockspace_flags); // Add empty node

            //This variable will track the document node
            uint dock_main_id = dockspaceId;
            //Reset IDs
            foreach (var dock in DockedWindows)
                dock.DockID = 0;

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
                ImGui.DockBuilderDockWindow(dock.GetWindowName(), dock.DockID);
            }
            ImGui.DockBuilderFinish(dockspaceId);

            UpdateDockLayout = false;
        }

        public override void OnLoad()
        {
            if (loaded) 
                return;

            loaded = true;

            unsafe
            {
                uint windowId = ImGui.GetID($"###window_{this.Name}");

                this.window_class = (ImGuiWindowClass*)Marshal.AllocHGlobal(sizeof(ImGuiWindowClass));
                ImGuiWindowClass windowClass = new ImGuiWindowClass();
                windowClass.ClassId = windowId;
                windowClass.DockingAllowUnclassed = 0;

                Marshal.StructureToPtr(windowClass,(IntPtr)this.window_class,false);
            }
        }

        public void Dispose() {
            unsafe {
                Marshal.FreeHGlobal((IntPtr)this.window_class);
                this.window_class = (ImGuiWindowClass*)IntPtr.Zero;
            }
        }
    }
}
