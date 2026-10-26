using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace YeastSim
{
    /// <summary>
    /// Renders the field of view from straight above with the <c>YeastSim/CellCapture</c> shader
    /// and reads back, per pixel, which cell is visible and how directly its surface faces the camera.
    /// </summary>
    /// <remarks>
    /// Cell ids are rendered as exact integers (low byte in red, high byte in green) into an
    /// 8-bit, non-sRGB, non-multisampled target, so there is no blending at cell edges. Where
    /// cells pile up, the top one wins. Arrays are row-major with row 0 at the top of the image;
    /// image right = +x and image up = +z in world space.
    /// </remarks>
    public sealed class FrameCapture : IDisposable
    {
        private static readonly int CellIdProperty = Shader.PropertyToID("_CellId");

        private readonly int _resolution;
        private readonly Camera _camera;
        private readonly RenderTexture _target;
        private readonly Texture2D _readback;
        private readonly Material _material;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly HashSet<int> _expectedIds = new HashSet<int>();

        public int Resolution => _resolution;

        public FrameCapture(Shader shader, int cellLayer, MicroscopeSettings microscope, Transform parent)
        {
            _resolution = microscope.Resolution;
            _material = new Material(shader) { name = "Cell capture (runtime)" };

            var cameraObject = new GameObject("Capture Camera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(microscope.FieldCentre.x, 50f, microscope.FieldCentre.y),
                Quaternion.Euler(90f, 0f, 0f));   // looking down; image up = +z, image right = +x

            _camera = cameraObject.AddComponent<Camera>();
            _camera.enabled = false;              // only renders on request
            _camera.orthographic = true;
            _camera.orthographicSize = microscope.FieldSize * 0.5f;
            _camera.aspect = 1f;
            _camera.nearClipPlane = 1f;
            _camera.farClipPlane = 100f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.cullingMask = 1 << cellLayer;
            _camera.allowMSAA = false;
            _camera.allowHDR = false;
            _camera.useOcclusionCulling = false;

            var descriptor = new RenderTextureDescriptor(_resolution, _resolution, GraphicsFormat.R8G8B8A8_UNorm, 24)
            {
                msaaSamples = 1,
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false,
            };
            _target = new RenderTexture(descriptor) { name = "Cell capture", filterMode = FilterMode.Point };
            _target.Create();
            _camera.targetTexture = _target;

            _readback = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false, true);
        }

        /// <summary>
        /// Captures the current frame. <paramref name="labelIdOf"/> gives the id each cell is drawn
        /// with (normally its own id). Fills <paramref name="ids"/> and <paramref name="facing"/>
        /// (length resolution²) and returns the fraction of cell pixels whose id didn't decode to a
        /// drawn cell (should be 0; anything else means the colours were altered on the way).
        /// </summary>
        public float Capture(IReadOnlyList<YeastCell> cells, Func<YeastCell, int> labelIdOf, ushort[] ids, byte[] facing)
        {
            var savedMaterials = new Material[cells.Count][];
            _expectedIds.Clear();

            for (int i = 0; i < cells.Count; i++)
            {
                SkinnedMeshRenderer renderer = cells[i].Renderer;
                savedMaterials[i] = renderer.sharedMaterials;
                renderer.sharedMaterial = _material;

                int id = labelIdOf(cells[i]);
                _expectedIds.Add(id);
                renderer.GetPropertyBlock(_block);
                _block.SetFloat(CellIdProperty, id);
                renderer.SetPropertyBlock(_block);
            }

            try
            {
                Render();
            }
            finally
            {
                for (int i = 0; i < cells.Count; i++) cells[i].Renderer.sharedMaterials = savedMaterials[i];
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            _readback.ReadPixels(new Rect(0, 0, _resolution, _resolution), 0, 0, false);
            RenderTexture.active = previous;

            var pixels = _readback.GetRawTextureData<Color32>();
            int n = _resolution;
            int cellPixels = 0, badPixels = 0;
            for (int texRow = 0; texRow < n; texRow++)
            {
                int imageRow = n - 1 - texRow;   // textures start at the bottom, images at the top
                for (int x = 0; x < n; x++)
                {
                    Color32 c = pixels[texRow * n + x];
                    int id = c.r | (c.g << 8);
                    int index = imageRow * n + x;
                    ids[index] = (ushort)id;
                    facing[index] = id != 0 ? c.b : (byte)0;
                    if (id == 0) continue;
                    cellPixels++;
                    if (!_expectedIds.Contains(id)) badPixels++;
                }
            }
            return cellPixels > 0 ? (float)badPixels / cellPixels : 0f;
        }

        private void Render()
        {
            // URP applies the pipeline asset's Render Scale to this camera too; a rescaled id image
            // would blend neighbouring ids. Force 1 for the capture and restore it afterwards.
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            PropertyInfo renderScale = pipeline != null ? pipeline.GetType().GetProperty("renderScale") : null;
            object savedScale = null;
            if (renderScale != null && renderScale.CanWrite && renderScale.PropertyType == typeof(float))
            {
                savedScale = renderScale.GetValue(pipeline);
                renderScale.SetValue(pipeline, 1f);
            }

            try
            {
                var request = new RenderPipeline.StandardRequest { destination = _target };
                if (RenderPipeline.SupportsRenderRequest(_camera, request))
                    RenderPipeline.SubmitRenderRequest(_camera, request);
                else
                    _camera.Render();
            }
            finally
            {
                if (savedScale != null) renderScale.SetValue(pipeline, savedScale);
            }
        }

        /// <summary>World position (on the slide plane) of the centre of pixel (x, y), y counted from the top.</summary>
        public static Vector2 PixelToWorld(MicroscopeSettings microscope, float x, float y)
        {
            float half = microscope.Resolution * 0.5f;
            return new Vector2(microscope.FieldCentre.x + (x + 0.5f - half) * microscope.PixelSize,
                               microscope.FieldCentre.y + (half - (y + 0.5f)) * microscope.PixelSize);
        }

        public void Dispose()
        {
            if (_camera != null) _camera.targetTexture = null;
            if (_target != null) { _target.Release(); UnityEngine.Object.Destroy(_target); }
            if (_readback != null) UnityEngine.Object.Destroy(_readback);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
