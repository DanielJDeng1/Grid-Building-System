using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Unity 6 / URP Render Graph version.
// Add this Renderer Feature to your URP Renderer asset (Forward+).
//
// Because the effect reads the depth/normal buffers rather than scene color, edges
// remain visible whether the room is fully lit, fully dark, or lit only by emergency
// lighting - this is the piece that satisfies the "works during blackouts" requirement.
public class EdgeDetectionRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        public Material edgeDetectionMaterial;
    }

    public Settings settings = new Settings();

    class EdgeDetectionPass : ScriptableRenderPass
    {
        private Material material;
        private const string PassName = "Clinical Facility Edge Detection";

        private class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public EdgeDetectionPass(Material mat)
        {
            material = mat;
            // Requests the DepthNormals prepass so cameraDepthTexture / cameraNormalsTexture
            // are populated this frame, same as the old ConfigureInput call.
            ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (cameraData.cameraType != CameraType.Game) return;

            // Camera color can't be both read and written in the same raster pass,
            // so blit into a temp texture, then hand that back as the new camera color.
            TextureHandle source = resourceData.activeColorTexture;

            TextureDesc destDesc = renderGraph.GetTextureDesc(source);
            destDesc.name = "_ClinicalEdgeTemp";
            destDesc.clearBuffer = false;
            destDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(PassName, out var passData))
            {
                passData.source = source;
                passData.material = material;

                builder.UseTexture(source, AccessFlags.Read);

                // Explicitly register the depth/normal textures as read dependencies so the
                // Render Graph scheduler keeps the DepthNormals prepass alive and orders this
                // pass after it. The shader itself samples these via the global textures
                // (DeclareDepthTexture.hlsl / DeclareNormalsTexture.hlsl).
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                if (resourceData.cameraNormalsTexture.IsValid())
                    builder.UseTexture(resourceData.cameraNormalsTexture, AccessFlags.Read);

                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            // Feed the result back in as the camera's color texture for subsequent passes.
            resourceData.cameraColor = destination;
        }
    }

    private EdgeDetectionPass pass;

    public override void Create()
    {
        pass = new EdgeDetectionPass(settings.edgeDetectionMaterial)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.edgeDetectionMaterial == null)
        {
            Debug.LogWarning("EdgeDetectionRendererFeature: assign the EdgeDetectionPost material in the renderer feature settings.");
            return;
        }
        renderer.EnqueuePass(pass);
    }
}