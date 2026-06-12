using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class MotionGrayscaleFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Range(0.0f, 1.0f)]
        public float motionThreshold = 0.02f;
        [Range(0.0f, 1.0f)]
        public float grayscaleStrength = 1.0f;
    }

    public Settings settings = new Settings();
    private MotionGrayscalePass pass;

    public override void Create()
    {
        pass = new MotionGrayscalePass(settings);
        pass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
                                         ref RenderingData renderingData)
    {
        renderer.EnqueuePass(pass);
    }
}