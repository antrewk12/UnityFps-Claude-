using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Highlights the grid tile under the mouse cursor so the player can see
/// where a tower would be placed. Occupied tiles use a different color.
/// </summary>
[RequireComponent(typeof(GridCreator))]
public class GridHighlighter : MonoBehaviour
{
    private static readonly Vector2Int NoCell = new Vector2Int(-1, -1);

    [SerializeField] private Camera targetCamera;
    [SerializeField] private Color freeHighlightColor = new Color(1f, 0.9f, 0.3f);
    [SerializeField] private Color occupiedHighlightColor = new Color(0.9f, 0.25f, 0.2f);

    private GridCreator grid;
    private Vector2Int hoveredCoordinates = NoCell;

    public bool HasHoveredCell => hoveredCoordinates != NoCell;
    public Vector2Int HoveredCoordinates => hoveredCoordinates;

    private void Awake()
    {
        grid = GetComponent<GridCreator>();
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Update()
    {
        Vector2Int newCoordinates = GetCoordinatesUnderMouse();
        if (newCoordinates == hoveredCoordinates)
        {
            return;
        }

        if (HasHoveredCell)
        {
            grid.ResetTileColor(hoveredCoordinates);
        }

        hoveredCoordinates = newCoordinates;

        if (HasHoveredCell)
        {
            RefreshHighlight();
        }
    }

    private void OnDisable()
    {
        if (HasHoveredCell)
        {
            grid.ResetTileColor(hoveredCoordinates);
            hoveredCoordinates = NoCell;
        }
    }

    /// <summary>Re-applies the hover color, e.g. after a tower is placed on the hovered cell.</summary>
    public void RefreshHighlight()
    {
        if (!grid.TryGetCell(hoveredCoordinates, out GridCreator.GridCell cell))
        {
            return;
        }

        grid.SetTileColor(hoveredCoordinates, cell.IsOccupied ? occupiedHighlightColor : freeHighlightColor);
    }

    // Intersects the mouse ray with the grid's flat plane: no colliders needed.
    private Vector2Int GetCoordinatesUnderMouse()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || targetCamera == null)
        {
            return NoCell;
        }

        Ray ray = targetCamera.ScreenPointToRay(mouse.position.ReadValue());
        Plane gridPlane = new Plane(Vector3.up, transform.position);
        if (!gridPlane.Raycast(ray, out float distance))
        {
            return NoCell;
        }

        Vector2Int coordinates = grid.WorldToCoordinates(ray.GetPoint(distance));
        return grid.IsInsideGrid(coordinates) ? coordinates : NoCell;
    }
}
