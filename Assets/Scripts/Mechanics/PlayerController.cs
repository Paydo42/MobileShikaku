using UnityEngine;

/// <summary>
/// Translates pointer input (mouse or single touch) into Shikaku rectangle
/// selections: press a cell, drag to the opposite corner to preview an
/// axis-aligned rectangle, release to commit it. Diagonal/freeform drags are
/// impossible by construction — the selection is always a rectangle defined by
/// its two corner cells.
/// </summary>
[DisallowMultipleComponent]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private GridManager grid;
    [Tooltip("Camera used for screen->world conversion. Defaults to Camera.main.")]
    [SerializeField] private Camera cam;
    [Tooltip("Hold a placed rectangle this long (without moving) to erase it.")]
    [SerializeField] private float erasePressSeconds = 0.6f;

    private bool _dragging;
    private Vector2Int _anchor;
    private float _pressTime;
    private bool _moved;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    private void Update()
    {
        if (grid == null || cam == null) return;

        if (Input.GetMouseButtonDown(0))
            BeginDrag();
        else if (_dragging && Input.GetMouseButton(0))
            UpdateDrag();
        else if (_dragging && Input.GetMouseButtonUp(0))
            EndDrag();
    }

    private void BeginDrag()
    {
        if (grid.TryWorldToCell(PointerWorld(), out Vector2Int cell))
        {
            _dragging = true;
            _anchor = cell;
            _pressTime = Time.time;
            _moved = false;
            grid.ShowPreview(_anchor, _anchor);
        }
    }

    private void UpdateDrag()
    {
        // ShowPreview clamps to the grid, so an out-of-range pointer is fine.
        grid.TryWorldToCell(PointerWorld(), out Vector2Int cell);
        if (cell != _anchor) _moved = true;
        grid.ShowPreview(_anchor, cell);

        // Long-press without moving erases the held rectangle.
        if (!_moved && Time.time - _pressTime >= erasePressSeconds && grid.EraseAt(_anchor))
        {
            grid.ClearPreview();
            _dragging = false;
        }
    }

    private void EndDrag()
    {
        grid.TryWorldToCell(PointerWorld(), out Vector2Int cell);
        grid.CommitSelection(_anchor, cell);
        _dragging = false;
    }

    private Vector3 PointerWorld()
    {
        Vector3 screen = Input.mousePosition;
        screen.z = -cam.transform.position.z;   // distance to the z = 0 play plane
        return cam.ScreenToWorldPoint(screen);
    }
}
