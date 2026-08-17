using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Pointer input for Preymet: press the Start cell (or any cell already on the
/// path) and drag through orthogonally-adjacent cells to trace a path; drag back
/// over the previous cell to undo. The board enforces all path rules, so this
/// just translates the pointer position into begin/step/end calls.
/// </summary>
[DisallowMultipleComponent]
public class PreymetController : MonoBehaviour
{
    [SerializeField] private PreymetBoard board;
    [SerializeField] private Camera cam;

    private bool _dragging;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    private void Update()
    {
        if (board == null || cam == null) return;

        // Pointer.current is the mouse on desktop and the touchscreen on device,
        // so one path covers both. It's null when neither is present.
        Pointer pointer = Pointer.current;
        if (pointer == null) return;

        if (pointer.press.wasPressedThisFrame)
        {
            if (board.TryWorldToCell(PointerWorld(), out Vector2Int cell))
                _dragging = board.BeginPath(cell);
        }
        else if (_dragging && pointer.press.isPressed)
        {
            if (board.TryWorldToCell(PointerWorld(), out Vector2Int cell))
                board.StepTo(cell);
        }
        else if (_dragging && pointer.press.wasReleasedThisFrame)
        {
            board.EndPath();
            _dragging = false;
        }
    }

    private Vector3 PointerWorld()
    {
        Vector3 screen = Pointer.current.position.ReadValue(); // Update checked it exists
        screen.z = -cam.transform.position.z; // distance to the z = 0 play plane
        return cam.ScreenToWorldPoint(screen);
    }
}
