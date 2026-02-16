using UnityEngine;
using UnityEngine.Rendering;

namespace FakeLightSystem
{
    public class EnableDepth : MonoBehaviour
    {
        public Camera TargetCamera;

        private void Start()
        {
            RenderPipelineAsset CurrentRenderPipelineAsset = GraphicsSettings.currentRenderPipeline;

            if (CurrentRenderPipelineAsset == null)
            {
                TargetCamera.depthTextureMode = DepthTextureMode.Depth;
            }
            else
            {
                Debug.LogWarning("only add this script if in the built-in render pipeline. in urp, enable the depth texture on the urp asset. depth texture is always enabled in hdrp!");
            }
        }
    }
}