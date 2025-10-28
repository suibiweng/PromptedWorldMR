// RenderSinglePDFPageFromUrl.cs
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Paroxe.PdfRenderer;

public class RenderSinglePDFPageFromUrl : MonoBehaviour
{
    [Header("PDF Source")]
    [Tooltip("Direct URL to a .pdf (must allow cross-origin if on WebGL).")]
    public string pdfUrl;

    [Tooltip("Optional: password if the PDF is protected.")]
    public string password = null;

    [Header("Page & Size")]
    [Tooltip("0-based page index to render.")]
    public int pageIndex = 0;

    [Tooltip("Target width in pixels; height is computed to preserve aspect.")]
    public int targetWidth = 1024;

    [Tooltip("Optional absolute max height (0 = ignore).")]
    public int maxHeight = 0;

    [Header("Targets (assign ONE)")]
    public RawImage uiTarget;         // World-Space or Screen-Space UI
    public Renderer meshTarget;       // e.g., Quad's MeshRenderer

    [Header("Advanced")]
    [Tooltip("If true, destroys any previous texture when re-rendering.")]
    public bool destroyPreviousTexture = true;

    private Texture2D _currentTex;

    void Start()
    {
        StartCoroutine(LoadAndRender());
    }

    public IEnumerator LoadAndRender()
    {
        if (string.IsNullOrEmpty(pdfUrl))
        {
            Debug.LogError("[PDF] No URL set.");
            yield break;
        }

        using (var req = UnityWebRequest.Get(pdfUrl))
        {
            // If you need custom headers (auth token, etc.), set them here:
            // req.SetRequestHeader("Authorization", "Bearer ...");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"[PDF] Download failed: {req.error}\nURL: {pdfUrl}");
                yield break;
            }

            byte[] bytes = req.downloadHandler.data;
            if (bytes == null || bytes.Length == 0)
            {
                Debug.LogError("[PDF] Empty download.");
                yield break;
            }

            // Open the PDF from memory
            var doc = new PDFDocument(bytes, password);
            if (!doc.IsValid)
            {
                Debug.LogError("[PDF] Invalid or password-protected PDF (wrong password?).");
                yield break;
            }

            int clamped = Mathf.Clamp(pageIndex, 0, doc.GetPageCount() - 1);

            using (var page = doc.GetPage(clamped))
            {
                // Keep aspect ratio of the page
                Vector2 pageSizePt = page.GetPageSize(1.0f); // points, ratio only
                int w = Mathf.Max(16, targetWidth);
                int h = Mathf.RoundToInt(w * (pageSizePt.y / pageSizePt.x));
                if (maxHeight > 0 && h > maxHeight)
                {
                    h = maxHeight;
                    w = Mathf.RoundToInt(h * (pageSizePt.x / pageSizePt.y));
                }

                using (var renderer = new PDFRenderer())
                {
                    Texture2D tex = renderer.RenderPageToTexture(
                        page, w, h, null, new PDFRenderer.RenderSettings()
                    );

                    if (tex == null)
                    {
                        Debug.LogError("[PDF] Render returned null texture.");
                        yield break;
                    }

                    // Clean old texture if requested
                    if (destroyPreviousTexture && _currentTex != null)
                        Destroy(_currentTex);

                    _currentTex = tex;

                    // Assign to whichever target is set
                    if (uiTarget != null)
                    {
                        uiTarget.texture = tex;
                        // optional: auto-size the RectTransform to the page
                        var rt = uiTarget.rectTransform;
                        rt.sizeDelta = new Vector2(w, h);
                        uiTarget.SetNativeSize(); // if you prefer 1:1 pixels
                    }
                    else if (meshTarget != null)
                    {
                        meshTarget.material.mainTexture = tex;
                        // Optional: adjust mesh scale to match aspect in world units
                        float aspect = (float)w / h;
                        meshTarget.transform.localScale = new Vector3(aspect, 1f, 1f);
                    }
                    else
                    {
                        Debug.LogWarning("[PDF] No target assigned. Texture rendered but not displayed.");
                    }
                }
            }
        }
    }

    void OnDestroy()
    {
        if (_currentTex != null) Destroy(_currentTex);
    }
}
