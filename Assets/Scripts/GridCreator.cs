using UnityEngine;

/// <summary>
/// Builds a rectangular map divided into square cells. Towers (and anything else)
/// are placed on cells; each cell can hold one occupant.
/// The grid's bottom-left corner sits at this GameObject's position.
/// </summary>
public class GridCreator : MonoBehaviour
{
    public class GridCell
    {
        public Vector2Int Coordinates { get; }
        public Vector3 WorldCenter { get; }
        public GameObject Occupant { get; set; }
        public bool IsOccupied => Occupant != null;

        public GridCell(Vector2Int coordinates, Vector3 worldCenter)
        {
            Coordinates = coordinates;
            WorldCenter = worldCenter;
        }
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Header("Grid Size")]
    [SerializeField, Min(1)] private int width = 20;
    [SerializeField, Min(1)] private int height = 20;
    [SerializeField, Min(0.1f)] private float cellSize = 2f;

    [Header("Visuals")]
    [SerializeField] private Material groundMaterial;
    [SerializeField] private Color groundColor = new Color(0.25f, 0.35f, 0.25f);
    [SerializeField] private Color tileColor = new Color(0.45f, 0.6f, 0.4f);
    [SerializeField, Range(0.5f, 1f)] private float tileFill = 0.92f;

    private GridCell[,] cells;
    private MeshRenderer[,] tileRenderers;
    private MaterialPropertyBlock propertyBlock;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        BuildGrid();
    }

    private void BuildGrid()
    {
        cells = new GridCell[width, height];
        tileRenderers = new MeshRenderer[width, height];

        CreateGround();

        Transform tilesParent = new GameObject("Tiles").transform;
        tilesParent.SetParent(transform, false);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int coordinates = new Vector2Int(x, y);
                Vector3 center = GetCellCenter(coordinates);
                cells[x, y] = new GridCell(coordinates, center);
                tileRenderers[x, y] = CreateTile(coordinates, center, tilesParent);
            }
        }
    }

    // The ground keeps its collider so mouse raycasts can find which cell was clicked.
    private void CreateGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(transform, false);

        // Unity's built-in plane is 10x10 units.
        float worldWidth = width * cellSize;
        float worldHeight = height * cellSize;
        ground.transform.localPosition = new Vector3(worldWidth * 0.5f, 0f, worldHeight * 0.5f);
        ground.transform.localScale = new Vector3(worldWidth / 10f, 1f, worldHeight / 10f);

        MeshRenderer groundRenderer = ground.GetComponent<MeshRenderer>();
        if (groundMaterial != null)
        {
            groundRenderer.sharedMaterial = groundMaterial;
        }
        SetRendererColor(groundRenderer, groundColor);
    }

    private MeshRenderer CreateTile(Vector2Int coordinates, Vector3 center, Transform parent)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
        tile.name = $"Tile_{coordinates.x}_{coordinates.y}";
        Destroy(tile.GetComponent<Collider>());

        tile.transform.SetParent(parent, false);
        // Lift slightly above the ground to avoid z-fighting.
        tile.transform.position = center + Vector3.up * 0.01f;
        tile.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        tile.transform.localScale = Vector3.one * (cellSize * tileFill);

        MeshRenderer tileRenderer = tile.GetComponent<MeshRenderer>();
        SetRendererColor(tileRenderer, tileColor);
        return tileRenderer;
    }

    // Uses a property block so every tile shares one material (no per-tile material copies).
    private void SetRendererColor(Renderer targetRenderer, Color color)
    {
        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, color);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }

    public bool IsInsideGrid(Vector2Int coordinates)
    {
        return coordinates.x >= 0 && coordinates.x < width
            && coordinates.y >= 0 && coordinates.y < height;
    }

    public Vector3 GetCellCenter(Vector2Int coordinates)
    {
        Vector3 localCenter = new Vector3(
            (coordinates.x + 0.5f) * cellSize,
            0f,
            (coordinates.y + 0.5f) * cellSize);
        return transform.position + localCenter;
    }

    public Vector2Int WorldToCoordinates(Vector3 worldPosition)
    {
        Vector3 local = worldPosition - transform.position;
        return new Vector2Int(
            Mathf.FloorToInt(local.x / cellSize),
            Mathf.FloorToInt(local.z / cellSize));
    }

    public bool TryGetCell(Vector2Int coordinates, out GridCell cell)
    {
        if (cells == null || !IsInsideGrid(coordinates))
        {
            cell = null;
            return false;
        }

        cell = cells[coordinates.x, coordinates.y];
        return true;
    }

    public bool TryGetCellFromWorld(Vector3 worldPosition, out GridCell cell)
    {
        return TryGetCell(WorldToCoordinates(worldPosition), out cell);
    }

    /// <summary>Places an object on a free cell, snapping it to the cell center.</summary>
    public bool TryPlace(Vector2Int coordinates, GameObject occupant)
    {
        if (!TryGetCell(coordinates, out GridCell cell) || cell.IsOccupied)
        {
            return false;
        }

        cell.Occupant = occupant;
        occupant.transform.position = cell.WorldCenter;
        return true;
    }

    public void ClearCell(Vector2Int coordinates)
    {
        if (TryGetCell(coordinates, out GridCell cell))
        {
            cell.Occupant = null;
        }
    }

    public void SetTileColor(Vector2Int coordinates, Color color)
    {
        if (tileRenderers != null && IsInsideGrid(coordinates))
        {
            SetRendererColor(tileRenderers[coordinates.x, coordinates.y], color);
        }
    }

    public void ResetTileColor(Vector2Int coordinates)
    {
        SetTileColor(coordinates, tileColor);
    }

    // Previews the grid layout in the Scene view without entering Play mode.
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Vector3 origin = transform.position;
        float worldWidth = width * cellSize;
        float worldHeight = height * cellSize;

        for (int x = 0; x <= width; x++)
        {
            Vector3 start = origin + new Vector3(x * cellSize, 0f, 0f);
            Gizmos.DrawLine(start, start + new Vector3(0f, 0f, worldHeight));
        }

        for (int y = 0; y <= height; y++)
        {
            Vector3 start = origin + new Vector3(0f, 0f, y * cellSize);
            Gizmos.DrawLine(start, start + new Vector3(worldWidth, 0f, 0f));
        }
    }
}
