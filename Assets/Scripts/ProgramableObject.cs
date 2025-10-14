using UnityEngine;
using PromptedWorld;
using UnityEngine.UI;
using TMPro;


public class ProgramableObject : MonoBehaviour
{
    public PromptedWorldManager promptedWorldManager;
    public string id;

    

    public string promptlog;

    public bool isRealObject = false;
    public TMP_Text TextBox;
    public RawImage Objimage;
    public Renderer ShapeRenderer;
    public GameObject shape;
    public Transform shapeRoot;
    
    public LuaBehaviour luaBehaviour;

    public Outline selectOutline;


            void Awake()
    {
            promptedWorldManager=FindAnyObjectByType<PromptedWorldManager>();
            // Assign a new ID only if not already set
            if (string.IsNullOrEmpty(id))
                id = IDGenerator.GenerateID();
        }



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

    public bool isToching;
    float Touchingdistance = 0.1f;

    void TochingDetection()
    {
       isToching=
        Vector3.Distance(promptedWorldManager.userLeftHand.position, this.gameObject.transform.position) < Touchingdistance ||
        Vector3.Distance(promptedWorldManager.userRightHand.position, this.gameObject.transform.position) < Touchingdistance;
    
    }



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

        TochingDetection();

    }
}
