using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace VeldridSurfaceTest;

/// <summary>
/// Self-contained Veldrid renderer that draws a solid colour-per-face cube.
///
/// The cube geometry and shaders are backend-agnostic (GLSL compiled to SPIR-V
/// by Veldrid.SPIRV, then cross-compiled to HLSL / MSL / GLSL ES as needed).
///
/// Usage:
/// <code>
///   // Create after GraphicsDevice is ready:
///   var renderer = new VeldridRenderer(gd, surfaceWidth, surfaceHeight);
///
///   // Each frame (e.g. from VulkanSurface.Render or a UITimer):
///   renderer.Resize(newWidth, newHeight); // no-op if size hasn't changed
///   renderer.RenderFrame(yaw, pitch, zoom);
///
///   // Teardown (call before GraphicsDevice.Dispose):
///   renderer.Dispose();
/// </code>
/// </summary>
public sealed class VeldridRenderer : IDisposable
{
    // ── Veldrid objects ───────────────────────────────────────────────────────
    readonly GraphicsDevice  _gd;
    readonly CommandList     _cl;
    readonly DeviceBuffer    _vertexBuffer;
    readonly DeviceBuffer    _mvpBuffer;
    readonly ResourceLayout  _resourceLayout;
    readonly ResourceSet     _resourceSet;
    readonly Pipeline        _pipeline;
    readonly Shader[]        _shaders;

    // ── Surface dimensions ────────────────────────────────────────────────────
    uint _surfaceW;
    uint _surfaceH;

    // ── Static geometry (created once for the process lifetime) ───────────────
    static readonly VertexPC[] s_cubeVerts = BuildCubeVertices();

    // ── Vertex layout ─────────────────────────────────────────────────────────
    /// <summary>
    /// Tightly packed: xyz (12 bytes) followed by rgba (16 bytes) = 28 bytes/vertex.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct VertexPC
    {
        public float X, Y, Z;   // position
        public float R, G, B, A; // colour
    }

    // ── GLSL shaders ──────────────────────────────────────────────────────────
    // Veldrid.SPIRV compiles these to SPIR-V and then cross-compiles to whatever
    // language the active backend requires (HLSL for D3D11, MSL for Metal, etc.).

    const string VertGlsl = @"
#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;

layout(set = 0, binding = 0) uniform MvpBuf {
    mat4 MVP;
};

layout(location = 0) out vec4 fColor;

void main() {
    gl_Position = MVP * vec4(Position, 1.0);
    fColor = Color;
}";

    const string FragGlsl = @"
#version 450

layout(location = 0) in vec4 fColor;
layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = fColor;
}";

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises all Veldrid GPU objects.
    /// Must be called on the same thread that created <paramref name="gd"/>.
    /// </summary>
    public VeldridRenderer(GraphicsDevice gd, uint width, uint height)
    {
        _gd       = gd;
        _surfaceW = Math.Max(1, width);
        _surfaceH = Math.Max(1, height);

        var factory = gd.ResourceFactory;

        // ── Vertex buffer (write-once, read many) ─────────────────────────────
        var vbSize = (uint)(s_cubeVerts.Length * Marshal.SizeOf<VertexPC>());
        _vertexBuffer = factory.CreateBuffer(
            new BufferDescription(vbSize, BufferUsage.VertexBuffer));
        gd.UpdateBuffer(_vertexBuffer, 0, s_cubeVerts);

        // ── MVP uniform buffer (64 bytes = one mat4, updated each frame) ──────
        _mvpBuffer = factory.CreateBuffer(
            new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        // ── Shaders ───────────────────────────────────────────────────────────
        // factory.CreateFromSpirv compiles GLSL → SPIR-V and then
        // cross-compiles to the backend's native shader language automatically.
        _shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex,   Encoding.UTF8.GetBytes(VertGlsl), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragGlsl), "main"));

        // ── Resource layout (MVP uniform bound at set=0, binding=0) ──────────
        _resourceLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription(
                    "MvpBuf", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        // ── Graphics pipeline ─────────────────────────────────────────────────
        var pipelineDesc = new GraphicsPipelineDescription
        {
            BlendState        = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState   = RasterizerStateDescription.Default,
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ShaderSet = new ShaderSetDescription(
                new[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription(
                            "Position",
                            VertexElementSemantic.TextureCoordinate,
                            VertexElementFormat.Float3),
                        new VertexElementDescription(
                            "Color",
                            VertexElementSemantic.TextureCoordinate,
                            VertexElementFormat.Float4))
                },
                _shaders),
            ResourceLayouts = new[] { _resourceLayout },
            // Outputs must match the framebuffer we will draw into.
            Outputs = gd.MainSwapchain.Framebuffer.OutputDescription,
        };
        _pipeline = factory.CreateGraphicsPipeline(pipelineDesc);

        // ── Resource set ──────────────────────────────────────────────────────
        _resourceSet = factory.CreateResourceSet(
            new ResourceSetDescription(_resourceLayout, _mvpBuffer));

        // ── Command list ──────────────────────────────────────────────────────
        _cl = factory.CreateCommandList();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Informs the renderer that the swapchain surface has been resized.
    /// A no-op if the dimensions have not changed.
    /// Must be called before <see cref="RenderFrame"/> when the surface size changes.
    /// </summary>
    public void Resize(uint width, uint height)
    {
        var w = Math.Max(1u, width);
        var h = Math.Max(1u, height);
        if (w == _surfaceW && h == _surfaceH) return;

        _surfaceW = w;
        _surfaceH = h;
        _gd.ResizeMainWindow(w, h);
    }

    /// <summary>
    /// Renders one frame.
    /// </summary>
    /// <param name="yaw">  Camera yaw in radians (Y-axis rotation of the model).</param>
    /// <param name="pitch">Camera pitch in radians (X-axis rotation of the model).</param>
    /// <param name="zoom"> Camera distance from the cube centre (positive = pull back).</param>
    public void RenderFrame(float yaw, float pitch, float zoom)
    {
        // ── Build MVP matrix ──────────────────────────────────────────────────
        //
        // Model: rotate the cube by the caller-supplied angles.
        var model = Matrix4x4.CreateRotationY(yaw)
                  * Matrix4x4.CreateRotationX(pitch);

        // View: camera sits on the Z axis at distance 'zoom', looking at the origin.
        var view = Matrix4x4.CreateLookAt(
            new Vector3(0f, 0f, zoom),
            Vector3.Zero,
            Vector3.UnitY);

        // Projection: standard perspective.
        //   GraphicsDeviceOptions.PreferStandardClipSpaceYDirection = true means
        //   Veldrid has already arranged for clip-space Y to match OpenGL convention
        //   across all backends; we don't need to flip the matrix here.
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4f,
            (float)_surfaceW / (float)_surfaceH,
            0.1f,
            100f);

        // System.Numerics.Matrix4x4 is row-major; GLSL uniform blocks expect
        // column-major storage, so transpose before uploading.
        var mvp = Matrix4x4.Transpose(proj * view * model);
        _gd.UpdateBuffer(_mvpBuffer, 0, ref mvp);

        // ── Record and submit command list ────────────────────────────────────
        _cl.Begin();

        _cl.SetFramebuffer(_gd.MainSwapchain.Framebuffer);
        _cl.SetViewport(0, new Viewport(0, 0, _surfaceW, _surfaceH, 0, 1));
        _cl.SetScissorRect(0, 0, 0, _surfaceW, _surfaceH);

        // Dark navy background so the coloured faces stand out.
        _cl.ClearColorTarget(0, new RgbaFloat(0.04f, 0.04f, 0.08f, 1f));
        _cl.ClearDepthStencil(1f);

        _cl.SetPipeline(_pipeline);
        _cl.SetVertexBuffer(0, _vertexBuffer);
        _cl.SetGraphicsResourceSet(0, _resourceSet);

        _cl.Draw((uint)s_cubeVerts.Length);   // 36 vertices — 12 triangles

        _cl.End();

        _gd.SubmitCommands(_cl);
        _gd.SwapBuffers(_gd.MainSwapchain);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _gd.WaitForIdle();     // ensure GPU has finished before releasing resources
        _cl.Dispose();
        _resourceSet.Dispose();
        _pipeline.Dispose();
        _resourceLayout.Dispose();
        foreach (var s in _shaders) s.Dispose();
        _mvpBuffer.Dispose();
        _vertexBuffer.Dispose();
    }

    // ── Geometry ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds 36 un-indexed vertices (6 faces × 2 triangles × 3 vertices).
    /// Each face has a distinct solid colour.
    /// </summary>
    static VertexPC[] BuildCubeVertices()
    {
        // Face colours: Blue(+Z front), Yellow(−Z back),
        //               Red(+X right),  Cyan(−X left),
        //               Green(+Y top),  Magenta(−Y bottom)
        (float r, float g, float b)[] fc =
        {
            (0f, 0f, 1f),   // +Z  blue
            (1f, 1f, 0f),   // −Z  yellow
            (1f, 0f, 0f),   // +X  red
            (0f, 1f, 1f),   // −X  cyan
            (0f, 1f, 0f),   // +Y  green
            (1f, 0f, 1f),   // −Y  magenta
        };

        // Each face is a quad expressed as 4 corners (winding: CCW from outside),
        // split into 2 triangles with indices 0,1,2 and 0,2,3.
        (float x, float y, float z)[][] faces =
        {
            new[] { (-1f,+1f,+1f), (+1f,+1f,+1f), (+1f,-1f,+1f), (-1f,-1f,+1f) }, // +Z
            new[] { (+1f,+1f,-1f), (-1f,+1f,-1f), (-1f,-1f,-1f), (+1f,-1f,-1f) }, // -Z
            new[] { (+1f,+1f,+1f), (+1f,+1f,-1f), (+1f,-1f,-1f), (+1f,-1f,+1f) }, // +X
            new[] { (-1f,+1f,-1f), (-1f,+1f,+1f), (-1f,-1f,+1f), (-1f,-1f,-1f) }, // -X
            new[] { (-1f,+1f,-1f), (+1f,+1f,-1f), (+1f,+1f,+1f), (-1f,+1f,+1f) }, // +Y
            new[] { (-1f,-1f,+1f), (+1f,-1f,+1f), (+1f,-1f,-1f), (-1f,-1f,-1f) }, // -Y
        };

        var verts = new VertexPC[36];
        int vi = 0;
        for (int f = 0; f < 6; f++)
        {
            var col  = fc[f];
            var quad = faces[f];
            foreach (int i in new[] { 0, 1, 2,  0, 2, 3 })
            {
                verts[vi++] = new VertexPC
                {
                    X = quad[i].x, Y = quad[i].y, Z = quad[i].z,
                    R = col.r,     G = col.g,     B = col.b,  A = 1f,
                };
            }
        }
        return verts;
    }
}
