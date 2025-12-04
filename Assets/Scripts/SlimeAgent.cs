using System.Collections.Generic;
using UnityEngine;

public class SlimeAgent : MonoBehaviour
{
    public enum Mode { Grow, Shrink, Idle }
    public enum Team : byte { Player = 2, Enemy = 3 }

    [Header("Field & Rendering")]
    public MultiBlobField field;
    public SlimeMaskRenderer maskRenderer;
    [Tooltip("(Optional) The opposing slime agent. If assigned, captured cells will be removed from its mask for correct visuals.")]
    public SlimeAgent opponent;

    [Header("Team & Seed")]
    public Team team = Team.Player;
    public Vector2Int seed = new Vector2Int(10, 10);

    [Header("Sim")]
    public float ticksPerSecond = 20f;
    public bool use8Neighbors = false;
    [Min(1)] public int growCellsPerTick = 32;
    [Min(1)] public int shrinkCellsPerTick = 64;

    [Header("Adaptive Growth")]
    [Range(0.05f, 0.6f)] public float perimeterFraction = 0.25f;
    [Min(1)] public int minGrowPerTick = 8;
    [Min(1)] public int maxGrowPerTick = 128;

    [Header("Pushing (when encountering other slime)")]
    [Tooltip("How much more local support (neighbors) is needed to capture an enemy cell.")]
    public int pushBias = 0;
    [Range(0, 4)] public int captureBoost = 1; // extra friendly neighbors counted when growing
    [Range(0f, 1f)] public float captureRandomness = 0.2f; // chance to capture when nearly tied
    public bool captureOnlyWhileGrowing = true; // apply boost only while in Grow

    [Header("Enemy AI")]
    public bool autoRun = false;
    public int idleCycleTicks = 40;
    public int maxGrowthRows = 20;

    [Header("Runtime (readonly)")]
    public Mode mode = Mode.Shrink;

    // internal
    byte Me => (byte)team;
    byte Other => team == Team.Player ? (byte)Team.Enemy : (byte)Team.Player;

    float acc;
    System.Random rng;

    Queue<Vector2Int> frontier;
    HashSet<int> frontierSet;
    List<List<Vector2Int>> waves;
    List<Vector2Int> currentWave;

    int idleTick;
    int lastMaxY;

    void Awake()
    {
        rng = new System.Random(1234 + (int)team);
        EnsureRefs();
        Init();
    }

    void EnsureRefs()
    {
        if (!field) field = FindObjectOfType<MultiBlobField>();
        if (!maskRenderer) maskRenderer = GetComponentInChildren<SlimeMaskRenderer>();
    }

    [ContextMenu("Init Slime")]
    public void Init()
    {
        if (field == null || field.cells == null)
        {
            field = FindObjectOfType<MultiBlobField>();
            field.Build();
        }

        seed.x = Mathf.Clamp(seed.x, 0, field.width - 1);
        seed.y = Mathf.Clamp(seed.y, 0, field.height - 1);

        // clear previous ownership
        for (int x = 0; x < field.width; x++)
            for (int y = 0; y < field.height; y++)
                if (field.cells[x, y] == Me)
                    field.cells[x, y] = 0;

        // place seed if not blocked
        if (field.cells[seed.x, seed.y] == 1) seed = FindNearestFree(seed);
        field.cells[seed.x, seed.y] = Me;

        frontier = new Queue<Vector2Int>(field.width * field.height / 4);
        frontierSet = new HashSet<int>();
        waves = new List<List<Vector2Int>>(1024);
        currentWave = null;
        lastMaxY = seed.y;

        RebuildFrontierFromPerimeter();

        // one-time initial draw
        maskRenderer?.EnsureInit(field.width, field.height);
        maskRenderer?.ApplyDelta(new List<Vector2Int> { seed }, null);

        if (autoRun) SetMode(Mode.Grow);
    }

    void Update()
    {
        if (autoRun) EnemyAIUpdate();

        acc += Time.deltaTime * ticksPerSecond;
        while (acc >= 1f)
        {
            Tick();
            acc -= 1f;
        }
    }

    void EnemyAIUpdate()
    {
        if (mode == Mode.Grow || mode == Mode.Shrink)
        {
            for (int y = field.height - 1; y >= 0; y--)
            {
                for (int x = 0; x < field.width; x++)
                    if (field.cells[x, y] == Me) { lastMaxY = Mathf.Max(lastMaxY, y); break; }
            }
            if (lastMaxY <= seed.y - maxGrowthRows || lastMaxY >= seed.y + maxGrowthRows)
            {
                mode = Mode.Idle;
                idleTick = 0;
            }
        }
        else if (mode == Mode.Idle)
        {
            idleTick++;
            if (idleTick % idleCycleTicks < idleCycleTicks / 2) mode = Mode.Grow;
            else mode = Mode.Shrink;
        }
    }

    public void SetMode(Mode m)
    {
        mode = m;
        if (mode == Mode.Grow) { RebuildFrontierFromPerimeter(); currentWave = null; }
    }

    void Tick()
    {
        if (mode == Mode.Grow) GrowTick();
        else if (mode == Mode.Shrink) ShrinkTick();

        field.cells[seed.x, seed.y] = Me;
    }

    void GrowTick()
    {
        if (frontier.Count == 0) RebuildFrontierFromPerimeter();

        if (currentWave == null) currentWave = new List<Vector2Int>(growCellsPerTick);

        List<Vector2Int> capturedThisTick = null;

        int perimeter = Mathf.Max(frontier.Count, 1);
        int budget = Mathf.Clamp((int)(perimeter * perimeterFraction), minGrowPerTick, maxGrowPerTick);

        int batchCount = Mathf.Min(frontier.Count, budget * 2);
        if (batchCount <= 0) { CommitWave(); return; }

        var batch = new List<Vector2Int>(batchCount);
        for (int i = 0; i < batchCount; i++)
        {
            var p = frontier.Dequeue();
            frontierSet.Remove(Hash(p));
            batch.Add(p);
        }

        for (int i = 0; i < batch.Count; i++)
        {
            int j = i + rng.Next(batch.Count - i);
            (batch[i], batch[j]) = (batch[j], batch[i]);
        }

        foreach (var p in batch)
        {
            if (budget <= 0) break;
            if (!field.InBounds(p.x, p.y)) continue;
            var cell = field.cells[p.x, p.y];
            if (cell == 1) continue;

            if (cell == 0)
            {
                field.cells[p.x, p.y] = Me;
                currentWave.Add(p);
                budget--;
                EnqueueNeighbors(p);
            }
            else if (cell == Other)
            {
                int myN = field.CountNeighbors(p.x, p.y, Me, use8Neighbors);
                int hisN = field.CountNeighbors(p.x, p.y, Other, use8Neighbors);

                int boost = (captureOnlyWhileGrowing && mode == Mode.Grow) ? captureBoost : 0;
                int effectiveMy = myN + boost;
                int target = hisN + pushBias;

                bool take = effectiveMy >= target;

                // If we are close (off by 1), allow a probabilistic capture to avoid stalemates
                if (!take && captureRandomness > 0f && effectiveMy + 1 >= target)
                {
                    if (rng.NextDouble() < captureRandomness)
                        take = true;
                }

                if (take)
                {
                    field.cells[p.x, p.y] = Me; // capture that border cell
                    currentWave.Add(p);
                    if (capturedThisTick == null) capturedThisTick = new List<Vector2Int>(32);
                    capturedThisTick.Add(p);
                    budget--;
                    EnqueueNeighbors(p);
                }
            }
        }

        if (maskRenderer != null && currentWave.Count > 0)
            maskRenderer.ApplyDelta(currentWave, null);

        // Remove captured pixels from opponent's mask so blobs don't visually overlap
        if (capturedThisTick != null && capturedThisTick.Count > 0 && opponent != null && opponent.maskRenderer != null)
            opponent.maskRenderer.ApplyDelta(null, capturedThisTick);

        CommitWave();
    }

    void ShrinkTick()
    {
        int left = shrinkCellsPerTick;
        List<Vector2Int> removed = new List<Vector2Int>(left);

        while (left > 0 && waves.Count > 0)
        {
            var wave = waves[waves.Count - 1];
            while (left > 0 && wave.Count > 0)
            {
                var p = wave[wave.Count - 1];
                wave.RemoveAt(wave.Count - 1);
                if (p == seed) continue;
                if (field.InBounds(p.x, p.y) && field.cells[p.x, p.y] == Me)
                {
                    field.cells[p.x, p.y] = 0;
                    removed.Add(p);
                    left--;
                }
            }
            if (wave.Count == 0) waves.RemoveAt(waves.Count - 1);
        }

        if (maskRenderer != null && removed.Count > 0)
            maskRenderer.ApplyDelta(null, removed);
    }

    void CommitWave()
    {
        if (currentWave != null && currentWave.Count > 0)
        {
            waves.Add(currentWave);
            currentWave = null;
        }
    }

    void RebuildFrontierFromPerimeter()
    {
        frontier ??= new Queue<Vector2Int>();
        frontier.Clear();
        frontierSet ??= new HashSet<int>();
        frontierSet.Clear();

        for (int x = 0; x < field.width; x++)
            for (int y = 0; y < field.height; y++)
            {
                byte c = field.cells[x, y];
                if ((c == 0 || c == Other) && HasNeighborOfMine(x, y))
                    Enqueue(new Vector2Int(x, y));
            }
    }

    bool HasNeighborOfMine(int x, int y) =>
        field.CountNeighbors(x, y, Me, use8Neighbors) > 0;

    void EnqueueNeighbors(Vector2Int p)
    {
        // Horizontal bias: enqueue left/right first and duplicate slightly for weight
        Enqueue(new Vector2Int(p.x + 1, p.y));
        Enqueue(new Vector2Int(p.x - 1, p.y));
        Enqueue(new Vector2Int(p.x + 1, p.y)); // extra horizontal weight
        Enqueue(new Vector2Int(p.x - 1, p.y)); // extra horizontal weight

        Enqueue(new Vector2Int(p.x, p.y + 1));
        Enqueue(new Vector2Int(p.x, p.y - 1));

        if (use8Neighbors)
        {
            Enqueue(new Vector2Int(p.x + 1, p.y + 1));
            Enqueue(new Vector2Int(p.x + 1, p.y - 1));
            Enqueue(new Vector2Int(p.x - 1, p.y + 1));
            Enqueue(new Vector2Int(p.x - 1, p.y - 1));
        }
    }

    void Enqueue(Vector2Int p)
    {
        if (!field.InBounds(p.x, p.y)) return;
        byte c = field.cells[p.x, p.y];
        // Skip obstacles and our own cells; accept empty or enemy cells
        if (c == 1 || c == Me) return;
        int h = Hash(p);
        if (frontierSet.Add(h)) frontier.Enqueue(p);
    }

    Vector2Int FindNearestFree(Vector2Int s)
    {
        var q = new Queue<Vector2Int>();
        var seen = new HashSet<int>();
        q.Enqueue(s); seen.Add(Hash(s));
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            if (field.InBounds(p.x, p.y) && field.cells[p.x, p.y] != 1) return p;
            EnqueueAllNeighbors(p, q, seen);
        }
        return new Vector2Int(0, 0);
    }

    void EnqueueAllNeighbors(Vector2Int p, Queue<Vector2Int> q, HashSet<int> seen)
    {
        var dirs = new Vector2Int[]
        {
            new Vector2Int(1,0), new Vector2Int(-1,0),
            new Vector2Int(0,1), new Vector2Int(0,-1),
            new Vector2Int(1,1), new Vector2Int(1,-1),
            new Vector2Int(-1,1), new Vector2Int(-1,-1),
        };
        foreach (var d in dirs)
        {
            var n = p + d;
            if (field.InBounds(n.x, n.y) && seen.Add(Hash(n))) q.Enqueue(n);
        }
    }

    int Hash(Vector2Int p) => (p.x << 16) ^ p.y;
}