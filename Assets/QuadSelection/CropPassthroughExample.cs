// Somewhere in your manager or test MonoBehaviour:

using System.Collections.Generic;
using UnityEngine;
public class CropPassthroughExample : MonoBehaviour
{
    public PlaneFromFourClicks_External planeClicker; // drag the component
    public Camera passthroughProjectionCamera;        // camera that matches passthrough pose/FOV (often Camera.main)
    public Texture passthroughTexture;                // your live passthrough RenderTexture/Texture

    PassthroughCropper_External _cropper;

    void Awake()
    {
        _cropper = new PassthroughCropper_External(512, 512); // choose output size
    }
    void OnDestroy() => _cropper?.Dispose();

    // Call this after you’ve picked 4 points (e.g., via a button or key)
    [ContextMenu("Crop Current Quad From Passthrough")]
    public void CropNow()
    {
        var pts = planeClicker.CurrentQuadWorldPoints;
        if (pts == null || pts.Count != 4) { Debug.LogWarning("Need 4 points first."); return; }

        var cropped = _cropper.Crop(passthroughProjectionCamera, pts, passthroughTexture, flipY: true);
        if (!cropped) return;

        // Example: apply the crop back onto the generated plane
        planeClicker.ApplyTexture(cropped, createMaterialIfMissing: true);

        // (Optional) save it:
        // var path = ImageIO.SavePNG(cropped);
        // Debug.Log("Saved to: " + path);
    }
}
