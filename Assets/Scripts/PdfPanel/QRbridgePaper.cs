using UnityEngine;
using Meta.XR.MRUtilityKitSamples.QRCodeDetection;
public class QRbridgePaper : MonoBehaviour
{
    public QRCode qRCode;
    public RenderPDFPageWithCVSegmentation renderPDFPageWithCVSegmentation;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

        qRCode = this.GetComponent<QRCode>();
        


        
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void loadPdf()
    {


        var url = qRCode.PayloadText;


        renderPDFPageWithCVSegmentation.LoadPDF(url);


        


        




        
        


    }
}
