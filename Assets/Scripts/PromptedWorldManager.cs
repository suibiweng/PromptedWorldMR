using UnityEngine;
using PromtedWorld;
public class PromptedWorldManager : MonoBehaviour
{


    public GameObject selectedObject;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    public void CreateShape(int shapeType)
    {

        // Example shape creation
        GameObject shape = PrimitiveFactory.CreatePrimitive(shapeType, Vector3.zero, Quaternion.identity);
        selectedObject = shape;
    }
    

    public void setSelectedObject(GameObject obj)
    {
        selectedObject = obj;
    }   
}
