using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// FOVMask.shader をカメラ描画の最後に合成するURP RendererFeature。
/// Unity 6 (URP 17+) の Render Graph 用 AddBlitPass ヘルパーを使用。
/// これにより、XR単一パスインスタンシング(Quest系)でも自動的に正しく処理される。
///
/// 導入手順:
///  1. 使用中の UniversalRendererData アセットの Renderer Features に
///     このFeatureを追加する
///  2. maskMaterial に Hidden/NonCircularFOV/FOVMask シェーダーのマテリアルを設定
/// </summary>
public class FOVMaskRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Material maskMaterial;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new Settings();
    FOVMaskPass pass;

    public override void Create()
    {
        pass = new FOVMaskPass(settings.maskMaterial, settings.renderPassEvent);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.maskMaterial == null) return;
        renderer.EnqueuePass(pass);
    }

    class FOVMaskPass : ScriptableRenderPass
    {
        Material material;

        public FOVMaskPass(Material mat, RenderPassEvent evt)
        {
            material = mat;
            renderPassEvent = evt;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle source = resourceData.activeColorTexture;

            TextureDesc desc = renderGraph.GetTextureDesc(source);
            desc.name = "_FOVMaskTemp";
            desc.clearBuffer = false;
            desc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            RenderGraphUtils.BlitMaterialParameters blitParams = new(source, destination, material, 0);
            renderGraph.AddBlitPass(blitParams, "NonCircularFOVMask");

            resourceData.cameraColor = destination;
        }
    }
}
