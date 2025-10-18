
using UnityEngine;

using OVR.Input;
public class SettheDot : MonoBehaviour
{
    public PlaneFromFourClicks_External planeFromFourClicks;

    public CropPassthroughExample cropPassthroughWithPreview;
    public Transform refdot;
    public GameObject dot;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

        if (OVRInput.GetUp(OVRInput.Button.SecondaryIndexTrigger))
        {
            //Instantiate(dot, refdot.position, Quaternion.identity);
            planeFromFourClicks.AddPoint(refdot.position);
        }

        if (OVRInput.GetUp(OVRInput.Button.SecondaryHandTrigger))
        {
            //Instantiate(dot, refdot.position, Quaternion.identity);
            cropPassthroughWithPreview.CropNow();
        }


    }
}
