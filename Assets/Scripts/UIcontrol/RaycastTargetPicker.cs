// Assets/Scripts/RaycastTargetPicker.cs
using UnityEngine;

public class RaycastTargetPicker : MonoBehaviour
{
    [Header("Pick Settings")]
    [SerializeField] private float maxDistance = 100f;
    [SerializeField] private LayerMask pickMask = ~0; // everything by default
    [SerializeField] private KeyCode cancelKey = KeyCode.Escape;

    private Camera _cam;
    private bool _picking;
    private LuaPromptUI _ui;

    private void Awake()
    {
        _cam = Camera.main;
        if (!_cam) _cam = GetComponent<Camera>();
    }

    public void BeginPick(LuaPromptUI ui)
    {
        _ui = ui;
        _picking = true;
    }

    private void Update()
    {
        if (!_picking) return;

        if (Input.GetKeyDown(cancelKey))
        {
            _picking = false;
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (!_cam) return;

            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, maxDistance, pickMask))
            {
                _ui?.OnPickedTarget(hit.collider.gameObject);
                _picking = false;
            }
        }
    }
}
