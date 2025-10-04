using UnityEngine;
using PromptedWorld;
public class PromptedWorldManager : MonoBehaviour
{


    public GameObject ProgramableObjectPrefab;


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

        GameObject obj = Instantiate(ProgramableObjectPrefab);
        obj.transform.SetParent(this.transform);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one * 0.2f;
        // Example shape creation
        GameObject shape = PrimitiveFactory.CreatePrimitive(shapeType, Vector3.zero, Quaternion.identity);

        obj.GetComponent<ProgramableObject>().setShape(shape);
        // selectedObject = shape;
    }
    

    public void setSelectedObject(GameObject obj)
    {
        selectedObject = obj;
    }   
}
