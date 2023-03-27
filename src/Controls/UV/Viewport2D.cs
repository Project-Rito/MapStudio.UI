﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL;
using OpenTK;
using OpenTK.Input;
using System.Drawing;
using GLFrameworkEngine;
using ImGuiNET;

namespace MapStudio.UI
{
    public class Viewport2D
    {
        public virtual float PreviewScale => 1f;

        public virtual bool UseOrtho { get; set; } = true;
        public virtual bool UseGrid { get; set; } = true;

        public int Width { get; set; }
        public int Height { get; set; }

        private System.Drawing.Point originMouse { get; set; }

        public Camera2D Camera = new Camera2D();

        Framebuffer FrameBuffer;

        public class Camera2D
        {
            public Matrix4 ViewMatrix = Matrix4.Identity;
            public Matrix4 ProjectionMatrix = Matrix4.Identity;

            public Matrix4 ModelViewMatrix
            {
                get
                {
                    return ViewMatrix * ProjectionMatrix;
                }
            }

            public float Zoom = 1;
            public Vector2 Position;
        }

        public void OnLoad()
        {
            FrameBuffer = new Framebuffer(FramebufferTarget.Framebuffer, Width, Height, 
                PixelInternalFormat.Rgb, 1);
        }

        public System.Drawing.Color BackgroundColor = System.Drawing.Color.FromArgb(40, 40, 40);

        public int GetViewportTexture() => ((GLTexture2D)FrameBuffer.Attachments[0]).ID;

        bool onEnter = false;

        public void Render(int width, int height)
        {
            if (Width != (int)width || Height != (int)height)
            {
                Width = (int)width;
                Height = (int)height;
                OnResize();
            }

            if (ImGui.IsWindowFocused() && ImGui.IsWindowHovered() || _mouseDown) {
                if (!onEnter) {
                    onEnter = true;
                    OnEnter();
                }

                UpdateCamera();
            }
            else
                onEnter = false;

            RenderEditor();
            var id = GetViewportTexture();

            ImGui.Image((IntPtr)id, new System.Numerics.Vector2(width, height));
        }

        private bool _mouseDown;

        private void OnEnter()
        {
            ImGuiHelper.UpdateMouseState();
            originMouse = new System.Drawing.Point(MouseEventInfo.X, MouseEventInfo.Y);
            mouseWheelPrevious = MouseEventInfo.WheelPrecise;
        }

        private void UpdateCamera()
        {
            ImGuiHelper.UpdateMouseState();

            if (ImGui.IsAnyMouseDown() && !_mouseDown)
            {
                OnMouseDown();
                _mouseDown = true;
            }

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) ||
               ImGui.IsMouseReleased(ImGuiMouseButton.Right) ||
               ImGui.IsMouseReleased(ImGuiMouseButton.Middle))
            {
                OnMouseUp();
                _mouseDown = false;
            }

            if (_mouseDown)
                OnMouseMove();

            OnMouseWheel();
        }

        private void RenderEditor()
        {
            if (FrameBuffer == null)
                OnLoad();

            FrameBuffer.Bind();

            GL.Viewport(0, 0, Width, Height);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            GL.MatrixMode(MatrixMode.Modelview);

            if (UseOrtho)
            {
                float halfW = Width / 2.0f, halfH = Height / 2.0f;
                var orthoMatrix = Matrix4.CreateOrthographic(halfW, halfH, -10000, 10000);

                Matrix4 scaleMat = Matrix4.CreateScale(Camera.Zoom * PreviewScale, Camera.Zoom * PreviewScale, 1);
                Matrix4 transMat = Matrix4.CreateTranslation(Camera.Position.X, Camera.Position.Y, 0);

                Camera.ViewMatrix = scaleMat * transMat;
                Camera.ProjectionMatrix = orthoMatrix;
            }
            else
            {
                var cameraPosition = new Vector3(Camera.Position.X, Camera.Position.Y, -(Camera.Zoom * 500));
                var perspectiveMatrix = Matrix4.CreatePerspectiveFieldOfView(1.3f, Width / Height, 0.01f, 100000);

                Camera.ViewMatrix = Matrix4.CreateTranslation(cameraPosition);
                Camera.ProjectionMatrix = perspectiveMatrix;
            }

            GL.ClearColor(0,0,0,0);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            SetupScene();

            FrameBuffer.Unbind();
        }

        private void SetupScene()
        {
            RenderScene();
        }

        public virtual void RenderScene()
        {

        }

        public void OnMouseDown() {
            originMouse = MouseEventInfo.Position;
        }

        public void OnMouseUp()
        {
        }

        public void OnMouseMove()
        {
            if (MouseEventInfo.LeftButton == ButtonState.Pressed)
            {
                var pos = new Vector2(MouseEventInfo.X - originMouse.X, MouseEventInfo.Y - originMouse.Y);
                Camera.Position.X += pos.X;
                Camera.Position.Y += pos.Y;

                originMouse = MouseEventInfo.Position;
            }
        }

        public void OnResize() {
            FrameBuffer?.Resize(Width, Height);
        }

        private float mouseWheelPrevious = 0;
        public void OnMouseWheel()
        {
            float delta = -(MouseEventInfo.WheelPrecise - mouseWheelPrevious);
            Camera.Zoom = Math.Max(2, Camera.Zoom - delta * 3.5f);
            mouseWheelPrevious = MouseEventInfo.WheelPrecise;
        }
    }
}
