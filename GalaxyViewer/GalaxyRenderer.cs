using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.GLControl;

namespace GalaxyViewer
{
    internal class GalaxyRenderer : IDisposable
    {
        private readonly GLControl _glControl;
        private int _shaderProgram;
        private int _vao;
        private int _vbo;
        private int _starCount;
        private bool _initialized;
        private int _uModel;
        private int _uView;
        private int _uProjection;

        public GalaxyRenderer(GLControl glControl)
        {
            _glControl = glControl;
        }

        public void Initialize()
        {
            _glControl.MakeCurrent();
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ProgramPointSize);

            _shaderProgram = CreateProgram();
            _uModel = GL.GetUniformLocation(_shaderProgram, "uModel");
            _uView = GL.GetUniformLocation(_shaderProgram, "uView");
            _uProjection = GL.GetUniformLocation(_shaderProgram, "uProjection");

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, IntPtr.Zero, IntPtr.Zero, BufferUsageHint.DynamicDraw);

            int stride = sizeof(float) * 4;
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, stride, sizeof(float) * 3);

            GL.BindVertexArray(0);
            _initialized = true;
        }

        public void UpdateStars(IReadOnlyList<Star> stars)
        {
            if (!_initialized)
            {
                return;
            }

            _glControl.MakeCurrent();
            _starCount = stars.Count;
            float[] buffer = new float[_starCount * 4];
            for (int i = 0; i < _starCount; i++)
            {
                buffer[i * 4 + 0] = stars[i].Position.X;
                buffer[i * 4 + 1] = stars[i].Position.Y;
                buffer[i * 4 + 2] = stars[i].Position.Z;
                buffer[i * 4 + 3] = stars[i].Intensity;
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, buffer.Length * sizeof(float), buffer, BufferUsageHint.DynamicDraw);
        }

        public void Render(Camera camera)
        {
            if (!_initialized)
            {
                return;
            }

            _glControl.MakeCurrent();
            GL.Viewport(0, 0, _glControl.ClientSize.Width, _glControl.ClientSize.Height);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            Matrix4 model = Matrix4.Identity;
            Matrix4 view = camera.GetViewMatrix();
            float aspect = Math.Max(0.1f, _glControl.ClientSize.Width / (float)Math.Max(1, _glControl.ClientSize.Height));
            Matrix4 projection = camera.GetProjectionMatrix(aspect);

            GL.UseProgram(_shaderProgram);
            GL.UniformMatrix4(_uModel, false, ref model);
            GL.UniformMatrix4(_uView, false, ref view);
            GL.UniformMatrix4(_uProjection, false, ref projection);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Points, 0, _starCount);
            GL.BindVertexArray(0);

            _glControl.SwapBuffers();
        }

        private int CreateProgram()
        {
            int vertex = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertex, VertexSource);
            GL.CompileShader(vertex);
            CheckShaderCompile(vertex, "vertex");

            int fragment = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragment, FragmentSource);
            GL.CompileShader(fragment);
            CheckShaderCompile(fragment, "fragment");

            int program = GL.CreateProgram();
            GL.AttachShader(program, vertex);
            GL.AttachShader(program, fragment);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
            {
                string info = GL.GetProgramInfoLog(program);
                throw new InvalidOperationException($"Failed to link shader program: {info}");
            }

            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);
            return program;
        }

        private static void CheckShaderCompile(int shader, string stage)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string info = GL.GetShaderInfoLog(shader);
                throw new InvalidOperationException($"Failed to compile {stage} shader: {info}");
            }
        }

        public void Dispose()
        {
            if (_shaderProgram != 0)
            {
                GL.DeleteProgram(_shaderProgram);
            }
            if (_vbo != 0)
            {
                GL.DeleteBuffer(_vbo);
            }
            if (_vao != 0)
            {
                GL.DeleteVertexArray(_vao);
            }
        }

        private const string VertexSource = @"#version 330 core
layout(location = 0) in vec3 in_position;
layout(location = 1) in float in_intensity;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

out float vIntensity;

void main()
{
    vec4 world = uModel * vec4(in_position, 1.0);
    vec4 viewPos = uView * world;
    float dist = max(length(viewPos.xyz), 0.01);
    gl_Position = uProjection * viewPos;
    float size = (6.0 + in_intensity * 7.0) / dist;
    gl_PointSize = clamp(size, 1.5, 14.0);
    vIntensity = clamp(in_intensity, 0.0, 1.0);
}";

        private const string FragmentSource = @"#version 330 core
in float vIntensity;
out vec4 fragColor;

void main()
{
    vec2 centered = gl_PointCoord * 2.0 - 1.0;
    float d = dot(centered, centered);
    float falloff = clamp(1.0 - smoothstep(0.0, 1.0, d), 0.0, 1.0);
    float i = vIntensity * falloff;
    fragColor = vec4(vec3(i), i);
}";
    }
}
