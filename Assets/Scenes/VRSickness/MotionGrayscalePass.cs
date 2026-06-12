using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class MotionGrayscalePass : ScriptableRenderPass
{
    private MotionGrayscaleFeature.Settings settings;
    private Material material;

    private class PassData
    {
        public TextureHandle src;
        public Material material;
        public float motionThreshold;
        public float grayscaleStrength;
    }

    public MotionGrayscalePass(MotionGrayscaleFeature.Settings settings)
    {
        this.settings = settings;
        material = new Material(Shader.Find("Custom/MotionGrayscale"));
        requiresIntermediateTexture = true;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph,
                                           ContextContainer frameData)
    {
        if (material == null) return;

        UniversalResourceData resourceData =
            frameData.Get<UniversalResourceData>();

        TextureHandle src = resourceData.activeColorTexture;

        using (var builder = renderGraph.AddUnsafePass<PassData>(
            "MotionGrayscale", out var passData))
        {
            passData.src = src;
            passData.material = material;
            passData.motionThreshold = settings.motionThreshold;
            passData.grayscaleStrength = settings.grayscaleStrength;

            builder.UseTexture(src, AccessFlags.ReadWrite);

            builder.SetRenderFunc((PassData data,
                                   UnsafeGraphContext context) =>
            {
                data.material.SetFloat("_MotionThreshold",
                                       data.motionThreshold);
                data.material.SetFloat("_GrayscaleStrength",
                                       data.grayscaleStrength);

                CommandBuffer cmd = CommandBufferHelpers
                    .GetNativeCommandBuffer(context.cmd);
                Blitter.BlitTexture(cmd, data.src,
                                    new Vector4(1, 1, 0, 0),
                                    data.material, 0);
            });
        }
    }
    [System.Obsolete]
    public override void Execute(ScriptableRenderContext context,
                                 ref RenderingData renderingData)
    { }
}