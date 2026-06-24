using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A small, self-contained falling-pair puzzle game.  It creates its camera,
/// board, sprites, and score text at runtime, so no Unity UI or scene setup is required.
/// </summary>
public sealed class PuyoGameController : MonoBehaviour
{
    private const int Width = 6;
    private const int Height = 12;
    private const float FallInterval = 0.75f;
    private const float SoftFallInterval = 0.06f;

    private readonly int[,] board = new int[Width, Height];
    private readonly Color[] colors =
    {
        new Color(0.93f, 0.25f, 0.28f),
        new Color(0.18f, 0.55f, 0.95f),
        new Color(0.20f, 0.78f, 0.37f),
        new Color(0.96f, 0.79f, 0.18f)
    };

    private Sprite[] puyoSprites;
    private Sprite tileSprite;
    private SpriteRenderer[,] renderers = new SpriteRenderer[Width, Height];
    private TextMesh scoreLabel;
    private TextMesh messageLabel;
    private Vector2Int pivot;
    private int direction;
    private int pivotColor;
    private int satelliteColor;
    private float fallTimer;
    private int score;
    private bool resolving;
    private bool gameOver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateGame()
    {
        if (FindObjectOfType<PuyoGameController>() == null)
            new GameObject("Puyo Game").AddComponent<PuyoGameController>();
    }

    private void Awake()
    {
        Application.targetFrameRate = 60;
        CreateCamera();
        CreateVisuals();
        StartNewGame();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.rKey.wasPressedThisFrame)
        {
            StartNewGame();
            return;
        }

        if (gameOver || resolving)
            return;

        if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
            TryMove(Vector2Int.left);
        if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
            TryMove(Vector2Int.right);
        if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.xKey.wasPressedThisFrame)
            TryRotate(1);
        if (keyboard.zKey.wasPressedThisFrame)
            TryRotate(-1);
        if (keyboard.spaceKey.wasPressedThisFrame)
            HardDrop();

        fallTimer += Time.deltaTime;
        float interval = keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed
            ? SoftFallInterval : FallInterval;
        if (fallTimer >= interval)
        {
            fallTimer = 0f;
            if (!TryMove(Vector2Int.down))
                LockPair();
        }

        RenderBoard();
    }

    private void StartNewGame()
    {
        StopAllCoroutines();
        System.Array.Clear(board, 0, board.Length);
        score = 0;
        gameOver = false;
        resolving = false;
        messageLabel.text = "← → : move   ↑/X, Z : rotate   ↓ : soft drop   Space : hard drop";
        SpawnPair();
        RenderBoard();
    }

    private void SpawnPair()
    {
        pivot = new Vector2Int(Width / 2 - 1, Height - 2);
        direction = 0;
        pivotColor = Random.Range(1, colors.Length + 1);
        satelliteColor = Random.Range(1, colors.Length + 1);
        fallTimer = 0f;

        if (!CanPlace(pivot, direction))
        {
            gameOver = true;
            messageLabel.text = "GAME OVER — R to restart";
        }
    }

    private Vector2Int SatellitePosition(Vector2Int center, int rotation)
    {
        switch ((rotation + 4) % 4)
        {
            case 0: return center + Vector2Int.up;
            case 1: return center + Vector2Int.right;
            case 2: return center + Vector2Int.down;
            default: return center + Vector2Int.left;
        }
    }

    private bool CanPlace(Vector2Int candidatePivot, int candidateDirection)
    {
        Vector2Int satellite = SatellitePosition(candidatePivot, candidateDirection);
        return IsFree(candidatePivot) && IsFree(satellite);
    }

    private bool IsFree(Vector2Int p)
    {
        return p.x >= 0 && p.x < Width && p.y >= 0 && p.y < Height && board[p.x, p.y] == 0;
    }

    private bool TryMove(Vector2Int delta)
    {
        Vector2Int next = pivot + delta;
        if (!CanPlace(next, direction)) return false;
        pivot = next;
        return true;
    }

    private void TryRotate(int amount)
    {
        int nextDirection = (direction + amount + 4) % 4;
        if (CanPlace(pivot, nextDirection))
        {
            direction = nextDirection;
            return;
        }

        // A small wall kick makes rotations beside the edge practical.
        foreach (int xKick in new[] { -1, 1 })
        {
            Vector2Int kicked = pivot + new Vector2Int(xKick, 0);
            if (CanPlace(kicked, nextDirection))
            {
                pivot = kicked;
                direction = nextDirection;
                return;
            }
        }
    }

    private void HardDrop()
    {
        while (TryMove(Vector2Int.down)) { }
        LockPair();
    }

    private void LockPair()
    {
        board[pivot.x, pivot.y] = pivotColor;
        Vector2Int satellite = SatellitePosition(pivot, direction);
        board[satellite.x, satellite.y] = satelliteColor;

        // A horizontal pair may be supported on only one side.  Once the pair
        // separates into board cells, let the unsupported piece fall before
        // checking for clears, just like every other settled piece.
        resolving = true;
        ApplyGravity();
        RenderBoard();
        StartCoroutine(ResolveAndSpawn());
    }

    private IEnumerator ResolveAndSpawn()
    {
        resolving = true;
        int chain = 0;
        while (true)
        {
            yield return new WaitForSeconds(0.12f);
            bool[,] erase = FindGroups();
            int erased = CountMarked(erase);
            if (erased == 0) break;

            chain++;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (erase[x, y]) board[x, y] = 0;
            score += erased * 10 * chain;
            messageLabel.text = chain + " CHAIN!";
            RenderBoard();
            yield return new WaitForSeconds(0.18f);
            ApplyGravity();
            RenderBoard();
        }

        messageLabel.text = "← → : move   ↑/X, Z : rotate   ↓ : soft drop   Space : hard drop";
        resolving = false;
        SpawnPair();
        RenderBoard();
    }

    private bool[,] FindGroups()
    {
        bool[,] erase = new bool[Width, Height];
        bool[,] visited = new bool[Width, Height];
        Vector2Int[] neighbors = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            if (board[x, y] == 0 || visited[x, y]) continue;
            var group = new System.Collections.Generic.List<Vector2Int>();
            var queue = new System.Collections.Generic.Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(x, y));
            visited[x, y] = true;
            while (queue.Count > 0)
            {
                Vector2Int p = queue.Dequeue();
                group.Add(p);
                foreach (Vector2Int offset in neighbors)
                {
                    Vector2Int next = p + offset;
                    if (next.x < 0 || next.x >= Width || next.y < 0 || next.y >= Height || visited[next.x, next.y]) continue;
                    if (board[next.x, next.y] != board[x, y]) continue;
                    visited[next.x, next.y] = true;
                    queue.Enqueue(next);
                }
            }
            if (group.Count >= 4)
                foreach (Vector2Int p in group) erase[p.x, p.y] = true;
        }
        return erase;
    }

    private static int CountMarked(bool[,] marks)
    {
        int total = 0;
        foreach (bool marked in marks) if (marked) total++;
        return total;
    }

    private void ApplyGravity()
    {
        for (int x = 0; x < Width; x++)
        {
            int writeY = 0;
            for (int y = 0; y < Height; y++)
                if (board[x, y] != 0)
                {
                    int value = board[x, y];
                    board[x, y] = 0;
                    board[x, writeY++] = value;
                }
        }
    }

    private void CreateCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
        }
        camera.orthographic = true;
        camera.orthographicSize = 8f;
        camera.transform.position = new Vector3(2.5f, 5.5f, -10f);
        camera.backgroundColor = new Color(0.08f, 0.10f, 0.16f);
    }

    private void CreateVisuals()
    {
        tileSprite = MakeSprite(Color.white, false);
        puyoSprites = new Sprite[colors.Length + 1];
        for (int i = 1; i < puyoSprites.Length; i++) puyoSprites[i] = MakeSprite(colors[i - 1], true);

        CreateSprite("Board", new Vector3(2.5f, 5.5f, 2f), new Color(0.14f, 0.17f, 0.27f), tileSprite, new Vector3(6.25f, 12.25f, 1f));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            renderers[x, y] = CreateSprite("Puyo", new Vector3(x, y, 0f), Color.white, null, Vector3.one);

        scoreLabel = CreateText("Score", new Vector3(7.1f, 10.5f, 0f), 0.12f, TextAnchor.MiddleLeft);
        messageLabel = CreateText("Help", new Vector3(2.5f, -1.15f, 0f), 0.055f, TextAnchor.MiddleCenter);
    }

    private SpriteRenderer CreateSprite(string objectName, Vector3 position, Color tint, Sprite sprite, Vector3 scale)
    {
        var renderer = new GameObject(objectName).AddComponent<SpriteRenderer>();
        renderer.transform.position = position;
        renderer.transform.localScale = scale;
        renderer.color = tint;
        renderer.sprite = sprite;
        return renderer;
    }

    private TextMesh CreateText(string objectName, Vector3 position, float size, TextAnchor anchor)
    {
        TextMesh text = new GameObject(objectName).AddComponent<TextMesh>();
        text.transform.position = position;
        text.text = "";
        text.fontSize = 64;
        text.characterSize = size;
        text.anchor = anchor;
        text.alignment = TextAlignment.Center;
        text.color = Color.white;
        return text;
    }

    private Sprite MakeSprite(Color color, bool circle)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - (size - 1) * 0.5f) / (size * 0.5f);
            float dy = (y - (size - 1) * 0.5f) / (size * 0.5f);
            float radius = dx * dx + dy * dy;
            float alpha = !circle || radius < 0.92f ? 1f : 0f;
            texture.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
        }
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private void RenderBoard()
    {
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            int value = board[x, y];
            renderers[x, y].sprite = value == 0 ? null : puyoSprites[value];
        }
        if (!gameOver && !resolving)
        {
            renderers[pivot.x, pivot.y].sprite = puyoSprites[pivotColor];
            Vector2Int satellite = SatellitePosition(pivot, direction);
            renderers[satellite.x, satellite.y].sprite = puyoSprites[satelliteColor];
        }
        scoreLabel.text = "SCORE\n" + score;
    }
}
