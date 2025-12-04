using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshRenderer))]
public class SlimeMaskRenderer : MonoBehaviour
{
    [Header("Grid dimensions (match simulation)")]
    public int width = 128;
    public int height = 72;

    [Header("Mask")]
    public Texture2D maskTex;
    public Material slimeMat;

    Color32[] buffer;
    MeshRenderer mr;

    void Awake()
    {
        mr = GetComponent<MeshRenderer>();
        if (slimeMat == null && mr) slimeMat = mr.material; // instance
        EnsureInit(width, height);
    }

    // --- NEW: safe lazy init you can call anytime ---
    public void EnsureInit(int w, int h)
    {
        if (w <= 0 || h <= 0) return;

        bool needRecreate = false;

        if (maskTex == null || maskTex.width != w || maskTex.height != h)
        {
            if (maskTex != null) DestroyImmediate(maskTex);
            maskTex = new Texture2D(w, h, TextureFormat.R8, false, true);
            maskTex.filterMode = FilterMode.Bilinear;
            maskTex.wrapMode = TextureWrapMode.Clamp;
            needRecreate = true;
        }

        if (buffer == null || buffer.Length != w * h)
        {
            buffer = new Color32[w * h];
            needRecreate = true;
        }

        width = w; height = h;

        if (slimeMat == null)
        {
            if (mr == null) mr = GetComponent<MeshRenderer>();
            if (mr != null) slimeMat = mr.material;
        }
        if (slimeMat != null && maskTex != null)
            slimeMat.SetTexture("_MaskTex", maskTex);

        // Fit the quad to the grid area
        transform.position = new Vector3(w * 0.5f, h * 0.5f, 0);
        transform.localScale = new Vector3(w, h, 1);

        if (needRecreate) ClearMask();
    }

    public void ClearMask()
    {
        if (buffer == null) return;
        for (int i = 0; i < buffer.Length; i++) buffer[i] = new Color32(0, 0, 0, 255);
        if (maskTex != null) { maskTex.SetPixels32(buffer); maskTex.Apply(false, false); }
    }

    // Call from agents every repaint
    public void UpdateFromGrid(byte[,] grid)
    {
        if (grid == null) return;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        EnsureInit(w, h); // <-- ensures texture exists/resized

        int idx = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++, idx++)
                buffer[idx] = (grid[x, y] == 1) ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 0, 255);

        maskTex.SetPixels32(buffer);
        maskTex.Apply(false, false);
    }

    // Incremental update: only touch changed cells and apply once per tick
    public void ApplyDelta(IList<Vector2Int> addCells, IList<Vector2Int> removeCells)
    {
        // Ensure texture and buffer are created and sized correctly
        EnsureInit(width, height);
        if (maskTex == null) return;

        // Set newly-filled cells to 1 (red channel = 255)
        if (addCells != null)
        {
            for (int i = 0; i < addCells.Count; i++)
            {
                var p = addCells[i];
                if ((uint)p.x < (uint)width && (uint)p.y < (uint)height)
                    maskTex.SetPixel(p.x, p.y, new Color32(255, 0, 0, 255));
            }
        }

        // Clear removed cells to 0
        if (removeCells != null)
        {
            for (int i = 0; i < removeCells.Count; i++)
            {
                var p = removeCells[i];
                if ((uint)p.x < (uint)width && (uint)p.y < (uint)height)
                    maskTex.SetPixel(p.x, p.y, new Color32(0, 0, 0, 255));
            }
        }

        // Upload once
        maskTex.Apply(false, false);
    }
}