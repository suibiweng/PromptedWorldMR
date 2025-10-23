using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

using System;
using UnityEngine.UI;
using UnityEngine.Android;
using PassthroughCameraSamples;


public class Fast3dFunctions : MonoBehaviour
{

    public RawImage CapturePreview;

    public Camera MaskCamera;

    public Camera CaptureCamera;

    public Camera ObjCam;
    private int originalCullingMask;
    private bool isRenderingNothing = false; //

    public RawImage DepthTextureIMG;


    public static Texture2D streamingTexture;
    public RenderTexture Mask,Depth,ObjTexture;

    public WebCamTextureManager webCamTextureManager;

    //public DisplayCaptureManager displayCaptureManager;

     Texture2D  updatetexture2D;

    void Start() {
    //    InitCameraMask();
        //displayCaptureManager= FindAnyObjectByType<DisplayCaptureManager>();
       // StartCapture();

    // ToggleCullingMask();


    //updatetexture2D = new Texture2D(webCamTextureManager.WebCamTexture.width, webCamTextureManager.WebCamTexture.height, TextureFormat.RGBA32, false);
       

    



    }


    


    

    



    public void  CaptureDepth(string url,string filename,Vector2 objectPosition,string urlid){


        var texture2D = ConvertRenderTextureToTexture2D(Depth);
            StartCoroutine(UploadPNG(texture2D, url, filename, "", false, 0, objectPosition, false, "Depth", urlid));








    }



    Texture2D ConvertToTexture2D(Texture texture)
    {
        // Ensure the input texture is readable (or handle RenderTexture)
        RenderTexture renderTexture = RenderTexture.GetTemporary(
            texture.width,
            texture.height,
            0,
            RenderTextureFormat.Default,
            RenderTextureReadWrite.Linear
        );

        // Blit the texture into the RenderTexture
        Graphics.Blit(texture, renderTexture);

        // Create a new Texture2D
        Texture2D texture2D = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);

        // Read the RenderTexture contents into the Texture2D
        RenderTexture.active = renderTexture;
        texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture2D.Apply();

        // Clean up
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(renderTexture);

        return texture2D;
    }


void InitCameraMask(){

           if (CaptureCamera == null)
        {
           // CaptureCamera = GetComponent<Camera>(); // Attempt to auto-assign
        }

        if (CaptureCamera != null)
        {
            // Store the original culling mask of the camera
            originalCullingMask = CaptureCamera.cullingMask;
        }
        else
        {
            Debug.LogError("No camera assigned to CameraCullingSwitcher!");
        }




}


    public void ToggleCullingMask()
    {
        if (CaptureCamera == null) return;

        if (isRenderingNothing)
        {
            // Restore the original culling mask
            CaptureCamera.cullingMask = originalCullingMask;
        }
        else
        {
            // Set the culling mask to Nothing
            CaptureCamera.cullingMask = 0;
        }

        // Toggle the state
        isRenderingNothing = !isRenderingNothing;
    }



    void Update() {
      
        // Graphics.CopyTexture(webCamTextureManager.WebCamTexture, updatetexture2D);
        // UpdateTexture(updatetexture2D);

        if(CapturePreview!=null)

        CapturePreview.texture=webCamTextureManager.WebCamTexture;


    }


    public Texture2D Convert_WebCamTexture_To_Texture2d(WebCamTexture _webCamTexture)
        {
        Texture2D _texture2D = new Texture2D(_webCamTexture.width, _webCamTexture.height);
        _texture2D.SetPixels32(_webCamTexture.GetPixels32());

        return _texture2D;
        }




    public void StartCapture(){

    //    displayCaptureManager.StartScreenCapture();


    }

    public void StopCapture()
    {

     //   displayCaptureManager.StopScreenCapture();


    }

    public void sendCommand(string url,string command ,string urlid,string Prompt)
    {

        StartCoroutine(SendtheCommand(url,command,urlid,Prompt));




    }

    







    // Update the texture to be uploaded
    public static void UpdateTexture(Texture2D texture)
    {
        streamingTexture = texture;
    }



    public void CaptureIpCam(string url, string filename,Vector2 objPosition,string urlid)
    {
        streamingTexture=Convert_WebCamTexture_To_Texture2d(webCamTextureManager.WebCamTexture);
        if (streamingTexture == null)
        {
            Debug.LogError("No texture set for streaming. Use UpdateTexture to set a texture first.");
        
        }
         

        StartCoroutine(UploadPNG(streamingTexture, url, filename,"",false,0,objPosition,false,"IP_RGB",urlid));
    }


    public void ModifyCapture(string url, string filename,Vector2 objPosition,string urlid)
    {
           Texture2D texture = ConvertRenderTextureToTexture2D(ObjTexture);


        StartCoroutine(UploadPNG(texture, url, filename,"",false,0,objPosition,false,"RGB_modify",urlid));
    }


    public void DreamMesh(string url,string urlid,string Prompt){


        StartCoroutine(SendtheCommand( url,"ShapeE",urlid,Prompt));



    }






        public void ChangeMaterial(string url,string urlid,string Prompt)
        {



            StartCoroutine(SendtheCommand( url,"ChangeTexture",urlid,Prompt));








        }





        public void CaptureOBJ(string url, string filename,Vector2 objPosition,string urlid,bool drawingMsk=false)

    
    {

          Texture2D texture2D = ConvertRenderTextureToTexture2D(ObjTexture);
        if (texture2D == null)
        {
            Debug.LogError("No texture set for streaming. Use UpdateTexture to set a texture first.");
            return;
        }

        StartCoroutine(UploadPNG(texture2D, url, filename,"",false,0,objPosition,false,"RGB",urlid, drawingMsk));
    }



    // Capture and upload the current streaming texture with a custom filename
    public void Capture(string url, string filename,Vector2 objPosition,string urlid,bool drawingMsk=false)

    
    {

        streamingTexture=Convert_WebCamTexture_To_Texture2d(webCamTextureManager.WebCamTexture);
        if (streamingTexture == null)
        {
            Debug.LogError("No texture set for streaming. Use UpdateTexture to set a texture first.");
            return;
        }

        StartCoroutine(UploadPNG(streamingTexture, url, filename,"",false,0,objPosition,false,"RGB",urlid, drawingMsk));
    }

    // Overloaded Capture function to handle RenderTexture input
    public void UploadMask(string url, string filename, string prompt,Vector2 objPosition,string urlid)
    {
        Texture2D texture2D = ConvertRenderTextureToTexture2D(Mask);
        StartCoroutine(UploadPNG(texture2D, url, filename, prompt,false,0,objPosition,false,"Mask",urlid));
        Destroy(texture2D); // Clean up after upload
    }



        public void UploadErase(string url, string filename, Vector2 objPosition,string urlid)
    {
        streamingTexture=Convert_WebCamTexture_To_Texture2d(webCamTextureManager.WebCamTexture);
        StartCoroutine(EraseMaskUPload(streamingTexture,url, filename,objPosition,urlid));
        // Destroy(texture2D); // Clean up after upload
    }








    public void UploadDepthMap(string url, string filename,Vector2 objPosition,string urlid)
    {
        Texture2D texture2D = ConvertRenderTextureToTexture2D(Depth);

        // Texture2D texture2D = CaptureRawImageShaderEffect(Depth);
        StartCoroutine(UploadPNG(texture2D, url, filename, "",false,0,objPosition,false,"Depth",urlid));
        Destroy(texture2D); // Clean up after upload
    }   


    // Converts RenderTexture to Texture2D
    private Texture2D ConvertRenderTextureToTexture2D(RenderTexture renderTexture)
    {
        Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        return texture;
    }

    public IEnumerator EraseMaskUPload(Texture2D RGBtext,string url, string filename,Vector2 objectPosition,string urlid){

        byte[] pngData = RGBtext.EncodeToPNG();
    if (pngData != null)
    {
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", pngData, filename, "image/png");
        form.AddField("objectPosition", $"({(int)objectPosition.x},{(int)objectPosition.y})"); // Send as (x,y)
        form.AddField("URLID",urlid);

        UnityWebRequest request = UnityWebRequest.Post(url, form);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Upload complete with filename: " + filename);
        }
        else
        {
            Debug.LogError("Error: " + request.error);
        }
    }



    }



    public void ObjwithDrawing(string url, string filename, string prompt,Vector2 objPosition,string urlid)
    {
        Texture2D texture2D = ConvertRenderTextureToTexture2D(ObjTexture);
        StartCoroutine(Drawinto3D(texture2D,url,filename,objPosition,prompt,urlid));
        Destroy(texture2D); // Clean up after upload
    }




    public void UploadDrawing(string url, string filename, string prompt,Vector2 objPosition,string urlid)
    {
        Texture2D texture2D = ConvertRenderTextureToTexture2D(Mask);
        StartCoroutine(Drawinto3D(texture2D,url,filename,objPosition,prompt,urlid));
        Destroy(texture2D); // Clean up after upload
    }




    public IEnumerator Drawinto3D(Texture2D RGBtext,string url, string filename,Vector2 objectPosition,string Prompt,string urlid){

        byte[] pngData = RGBtext.EncodeToPNG();
    if (pngData != null)
    {
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", pngData, filename, "image/png");
        form.AddField("objectPosition", $"({(int)objectPosition.x},{(int)objectPosition.y})"); // Send as (x,y)
        form.AddField("URLID",urlid);
        form.AddField("prompt",Prompt);

        UnityWebRequest request = UnityWebRequest.Post(url, form);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Upload complete with filename: " + filename);
        }
        else
        {
            Debug.LogError("Error: " + request.error);
        }
    }




    }



    // Upload the texture as PNG to the specified URL with a custom filename
public IEnumerator UploadPNG(Texture2D texture, string url, string filename, string prompt, bool flipY, int xOffset, Vector2 objectPosition, bool debugDraw, string type,string urlid,bool drawingMsk=false)
{

    //   Texture2D msk = ConvertRenderTextureToTexture2D(Mask);
    // byte[] mskData =msk.EncodeToPNG();
    byte[] pngData = texture.EncodeToPNG();
    if (pngData != null)
    {
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", pngData, filename, "image/png");

        // if(drawingMsk) form.AddBinaryData("Mask", mskData, filename+"Msk", "image/png");
        // else form.AddBinaryData("Mask", null, null, "image/png");



        form.AddField("prompt", prompt);
        form.AddField("flipY", flipY ? "true" : "false"); 
        form.AddField("xOffset", xOffset.ToString()); 
        form.AddField("objectPosition", $"({(int)objectPosition.x},{(int)objectPosition.y})"); // Send as (x,y)
        form.AddField("debugDraw", debugDraw ? "true" : "false"); 
        form.AddField("type", type);
        form.AddField("URLID",urlid);

        UnityWebRequest request = UnityWebRequest.Post(url, form);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Upload complete with filename: " + filename);
        }
        else
        {
            Debug.LogError("Error: " + request.error);
        }
    }
}


public IEnumerator SendtheCommand( string url,string command ,string urlid,string prompt)
{
    
    if (command != "")
    {
        WWWForm form = new WWWForm();

        form.AddField("Command", command);
        form.AddField("URLID",urlid);
        form.AddField("Prompt",prompt);

        UnityWebRequest request = UnityWebRequest.Post(url, form);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Command sent: " + command);
        }
        else
        {
            Debug.LogError("Error: " + request.error);
        }
    }
}



    public Texture2D ConvertRawImageToTexture2D(RawImage rawImage)
    {
        Texture sourceTexture = rawImage.texture;

        // If already a Texture2D, just cast
        if (sourceTexture is Texture2D tex2D)
        {
            return tex2D;
        }

        // If it's a RenderTexture, convert to Texture2D
        if (sourceTexture is RenderTexture renderTex)
        {
            // Backup the currently active RenderTexture
            RenderTexture currentRT = RenderTexture.active;

            // Set the RenderTexture as the active one
            RenderTexture.active = renderTex;

            // Create a new Texture2D with the same dimensions
            Texture2D tex = new Texture2D(renderTex.width, renderTex.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
            tex.Apply();

            // Restore the previously active RenderTexture
            RenderTexture.active = currentRT;

            return tex;
        }

        Debug.LogWarning("Unsupported texture type.");
        return null;
    }

    public Texture2D CaptureRawImageShaderEffect(RawImage rawImage)
{
    Material mat = rawImage.material;
    Texture sourceTex = rawImage.texture;

    int width = (int)rawImage.rectTransform.rect.width;
    int height = (int)rawImage.rectTransform.rect.height;

    RenderTexture rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
    Graphics.Blit(sourceTex, rt, mat); // Apply material/shader to texture

    RenderTexture.active = rt;
    Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
    tex.Apply();
    RenderTexture.active = null;

    rt.Release();
    return tex;
}


}
