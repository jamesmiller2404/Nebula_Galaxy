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
        private int _uPalette;
        private int _paletteTexture;

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
            _uPalette = GL.GetUniformLocation(_shaderProgram, "uPalette");

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _paletteTexture = CreatePaletteTexture(BuildPalette());

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, IntPtr.Zero, IntPtr.Zero, BufferUsageHint.DynamicDraw);

            int stride = sizeof(float) * 5;
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, stride, sizeof(float) * 3);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, sizeof(float) * 4);

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
            float[] buffer = new float[_starCount * 5];
            for (int i = 0; i < _starCount; i++)
            {
                buffer[i * 5 + 0] = stars[i].Position.X;
                buffer[i * 5 + 1] = stars[i].Position.Y;
                buffer[i * 5 + 2] = stars[i].Position.Z;
                buffer[i * 5 + 3] = stars[i].Intensity;
                buffer[i * 5 + 4] = stars[i].ColorIndex / 255f;
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
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture1D, _paletteTexture);
            GL.Uniform1(_uPalette, 0);

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
            if (_paletteTexture != 0)
            {
                GL.DeleteTexture(_paletteTexture);
            }
        }

        private static int CreatePaletteTexture(Vector3[] palette)
        {
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture1D, tex);
            GL.TexParameter(TextureTarget.Texture1D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture1D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture1D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            float[] data = new float[palette.Length * 3];
            for (int i = 0; i < palette.Length; i++)
            {
                data[i * 3 + 0] = palette[i].X;
                data[i * 3 + 1] = palette[i].Y;
                data[i * 3 + 2] = palette[i].Z;
            }

            GL.TexImage1D(TextureTarget.Texture1D, 0, PixelInternalFormat.Rgb16f, palette.Length, 0, PixelFormat.Rgb, PixelType.Float, data);
            GL.BindTexture(TextureTarget.Texture1D, 0);
            return tex;
        }

        private static Vector3[] BuildPalette()
        {
            var palette = new Vector3[256];
            var core = new Vector3(1.0f, 0.95f, 0.90f);
            var mid = new Vector3(0.85f, 0.90f, 1.0f);
            var outer = new Vector3(0.45f, 0.60f, 1.0f);

            for (int i = 0; i < palette.Length; i++)
            {
                float t = i / 255f;
                float midT = Math.Clamp((t - 0.2f) / 0.3f, 0f, 1f);
                float outerT = Math.Clamp((t - 0.5f) / 0.5f, 0f, 1f);
                Vector3 warmToMid = Vector3.Lerp(core, mid, midT);
                palette[i] = Vector3.Lerp(warmToMid, outer, outerT);
            }

            return palette;
        }

        private const string VertexSource = @"#version 330 core
layout(location = 0) in vec3 in_position;
layout(location = 1) in float in_intensity;
layout(location = 2) in float in_colorIndex;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

out float vIntensity;
out float vColorIndex;

void main()
{
    vec4 world = uModel * vec4(in_position, 1.0);
    vec4 viewPos = uView * world;
    float dist = max(length(viewPos.xyz), 0.01);
    gl_Position = uProjection * viewPos;
    float size = 8.0 / dist;
    gl_PointSize = clamp(size, 1.5, 12.0);
    vIntensity = in_intensity;
    vColorIndex = clamp(in_colorIndex, 0.0, 1.0);
}";

        private const string FragmentSource = @"#version 330 core
in float vIntensity;
in float vColorIndex;
uniform sampler1D uPalette;
out vec4 fragColor;

void main()
{
    vec2 centered = gl_PointCoord * 2.0 - 1.0;
    float d = dot(centered, centered);
    float falloff = clamp(1.0 - smoothstep(0.0, 1.0, d), 0.0, 1.0);
    float alpha = falloff;                 // keep disc thickness stable
    vec3 color = texture(uPalette, vColorIndex).rgb * vIntensity;
    fragColor = vec4(color, alpha);
}";
    }
}
