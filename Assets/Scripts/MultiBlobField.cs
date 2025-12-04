using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;



public class MultiBlobField : MonoBehaviour
{
    public enum ObstacleSource { Tilemap, Image }

    [Header("Size")]
    public int width = 128;
    public int height = 72;

    [Header("Obstacle Source")]
    public ObstacleSource obstacleSource = ObstacleSource.Image;
    public Tilemap obstaclesTM;

    [Header("Image Level (black=outside, red=obstacle, cyan/blue=play)")]
    public Texture2D levelImage;
    public bool matchGridToImage = true;
    [Range(0f, 0.3f)] public float colorTolerance = 0.08f;
    public Color outsideColor = Color.black;
    public Color obstacleColor = new Color(0.8f, 0, 0);
    public Color playColor = new Color(0, 0.9f, 0.9f);

    // Cell types
    // 0 = empty, 1 = obstacle, 2 = player slime, 3 = enemy slime
    public byte[,] cells;

    void Awake() { Build(); }

    [ContextMenu("Rebuild Field")]
    public void Build()
    {
        if (obstacleSource == ObstacleSource.Image && levelImage && matchGridToImage)
        {
            width = levelImage.width;
            height = levelImage.height;
        }

        cells = new byte[width, height];

        if (obstacleSource == ObstacleSource.Tilemap) ReadObstaclesFromTilemap();
        else ReadObstaclesFromImage();
    }

    void ReadObstaclesFromTilemap()
    {
        if (!obstaclesTM) return;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (obstaclesTM.HasTile(new Vector3Int(x, y, 0)))
                    cells[x, y] = 1;
    }

    void ReadObstaclesFromImage()
    {
        if (!levelImage) return;
        var px = levelImage.GetPixels32();
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                var c32 = px[row + x];
                var c = new Color(c32.r / 255f, c32.g / 255f, c32.b / 255f, 1);
                if (CloseTo(c, outsideColor, colorTolerance)) cells[x, y] = 1;
                else if (CloseTo(c, obstacleColor, colorTolerance)) cells[x, y] = 1;
                else if (CloseTo(c, playColor, colorTolerance)) cells[x, y] = 0;
                else cells[x, y] = 1;
            }
        }
    }

    bool CloseTo(Color a, Color b, float tol) =>
        Mathf.Abs(a.r - b.r) <= tol &&
        Mathf.Abs(a.g - b.g) <= tol &&
        Mathf.Abs(a.b - b.b) <= tol;

    public bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

    public int CountNeighbors(int x, int y, byte target, bool use8)
    {
        int c = 0;
        // 4
        if (InBounds(x+1,y) && cells[x+1,y]==target) c++;
        if (InBounds(x-1,y) && cells[x-1,y]==target) c++;
        if (InBounds(x,y+1) && cells[x,y+1]==target) c++;
        if (InBounds(x,y-1) && cells[x,y-1]==target) c++;
        if (use8)
        {
            if (InBounds(x+1,y+1) && cells[x+1,y+1]==target) c++;
            if (InBounds(x+1,y-1) && cells[x+1,y-1]==target) c++;
            if (InBounds(x-1,y+1) && cells[x-1,y+1]==target) c++;
            if (InBounds(x-1,y-1) && cells[x-1,y-1]==target) c++;
        }
        return c;
    }
    
   #if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (width <= 0 || height <= 0) return;
        Gizmos.color = new Color(0, 1, 1, 0.6f);
        var center = new Vector3(width * 0.5f, height * 0.5f, 0);
        var size   = new Vector3(width, height, 0.01f);
        Gizmos.DrawWireCube(center, size);
    }
#endif 
}