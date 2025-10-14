using UnityEngine;
using PromptedWorld;
using UnityEngine.UI;
using TMPro;


public class ProgramableObject : MonoBehaviour
{

    public bool isRealObject = false;
    public TMP_Text TextBox;
    public RawImage Objimage;
    public Renderer ShapeRenderer;
    public GameObject shape;
    public Transform shapeRoot;
    
    public LuaBehaviour luaBehaviour;

    public Outline selectOutline;



    public bool hasLuaScript()
    {
        return GetComponent<LuaBehaviour>() != null;
    }







    public void setLabel(string label)
    {
        TextBox.text = label;
    }   

    public void setImage(Texture texture)
    {   Objimage.gameObject.SetActive(true);
    
        Objimage.texture = texture;
        Objimage.color = Color.white;
    }

    public void setShape(GameObject obj)
    {
        
        shape = obj;
        selectOutline = shape.AddComponent<Outline>();
        shape.transform.SetParent(shapeRoot);
        shape.transform.localPosition = Vector3.zero;
        shape.transform.localRotation = Quaternion.identity;
        ShapeRenderer = shape.GetComponent<Renderer>();
    }



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }
}
