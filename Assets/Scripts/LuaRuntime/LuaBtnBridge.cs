using UnityEngine;

public class LuaBtnBridge : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    public void OnClick()
    {
        Debug.Log("LuaBtnBridge: OnClick");
    }


    public void OnPressed()
    {
        Debug.Log("LuaBtnBridge: OnPressed");
    }

    public void OnReleased()
    {
        Debug.Log("LuaBtnBridge: OnReleased");
    }
}
