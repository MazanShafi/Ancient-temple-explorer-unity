// ANCIENT TEMPLE EXPLORER -

using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class TempleGame : MonoBehaviour
{
    // ---------------------------------------------------------------- LEVEL
 
    static readonly string[] MAP =
    {
        "#####################",
        "#P..#.......#.....K.#",
        "#.#.#.#####.#.#####.#",
        "#.#...#.T.#.S.#...#.#",
        "#.#####.#.#####.#.#.#",
        "#.......#..S..#.#...#",
        "#.#######.###.#.#####",
        "#.#K..T.#.#...#..S..#",
        "#.#.###.#.#.#########",
        "E...#S....#...D....H#",
        "#.###.#####.#######.#",
        "#T....D...T...S.....#",
        "#####################"
    };

    // ---------------------------------------------------------------- TUNING (change freely)
    const float PlayerSpeed = 3.6f;
    const int StartLives = 3;
    const float EscapeTime = 35f;       // seconds to escape after taking the Heart
    const int ScoreKey = 100, ScoreDoor = 50, ScoreSpider = 200, ScoreHeart = 1000, ScorePerSecondLeft = 20, ScorePerLife = 100;
    const float FireCooldown = 0.3f, BulletSpeed = 9f;
    const float SpiderPatrolSpeed = 1.3f, SpiderChaseSpeed = 2.1f, SpiderSight = 4.5f;
    const int SpiderHealth = 2;

    // ---------------------------------------------------------------- TYPES
    enum State { Menu, Play, Won, Lost }
    class Spider { public Transform t; public SpriteRenderer sr; public Vector2 pos, dir; public int hp = SpiderHealth; public float turn, flash; }
    class Trap { public SpriteRenderer sr; public Vector2 pos; public float phase; }
    class Pickup { public Vector2 pos; public GameObject go; }
    class Bullet { public Transform t; public Vector2 pos, dir; public float life = 1.5f; }

    // ---------------------------------------------------------------- STATE
    static TempleGame instance;
    State state = State.Menu;
    int cols, rows;
    char[,] grid;
    Transform world;
    Camera cam;
    readonly Dictionary<Vector2Int, GameObject> doorObjs = new Dictionary<Vector2Int, GameObject>();
    readonly List<Spider> spiders = new List<Spider>();
    readonly List<Trap> traps = new List<Trap>();
    readonly List<Pickup> keyItems = new List<Pickup>();
    readonly List<Bullet> bullets = new List<Bullet>();
    SpriteRenderer exitSr, playerSr;
    GameObject heartObj;
    Transform playerT, torchT, darkT;
    Vector2 startPos, exitPos, heartPos, playerPos, facing = Vector2.right;
    int lives, keys, score;
    float invuln, fireCd, gameTime, sealTimer, messageT, shake;
    bool exitOpen, heartTaken;
    string playerName = "Explorer", message = "", resultText = "", resultDetail = "";

    Sprite wallSpr, floorSpr, doorSpr, exitClosedSpr, exitOpenSpr, circleSpr, keySpr, heartSpr, spikeUpSpr, spikeDownSpr, spiderSpr, darkSpr;

    // ---------------------------------------------------------------- BOOT
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        new GameObject("TempleGame").AddComponent<TempleGame>();
    }

    void Awake()
    {
        instance = this;
        playerName = PlayerPrefs.GetString("lastName", "Explorer");
        BuildSprites();
        cam = Camera.main;
        if (cam == null)
        {
            var g = new GameObject("Main Camera");
            g.tag = "MainCamera";
            cam = g.AddComponent<Camera>();
            g.AddComponent<AudioListener>();
        }
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.03f, 0.03f);
        BuildWorld();            // show the temple behind the menu
    }

    // ---------------------------------------------------------------- SPRITES
    static Sprite Make(int n, System.Func<float, float, Color> f)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = f(x, y);
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    static float Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }
    }

    static float SegDist(float px, float py, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay;
        float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
        float dx = px - (ax + vx * t), dy = py - (ay + vy * t);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    void BuildSprites()
    {
        Color clear = new Color(0, 0, 0, 0);
        Color gold = new Color(1f, 0.8f, 0.15f);

        wallSpr = Make(32, (x, y) =>
        {
            int ix = (int)x, iy = (int)y;
            int off = ((iy / 8) % 2) * 8;
            bool mortar = iy % 8 == 0 || (ix + off) % 16 == 0;
            Color c = new Color(0.46f, 0.38f, 0.28f) * (0.85f + Hash(ix / 2, iy / 2) * 0.3f);
            if (mortar) c = new Color(0.2f, 0.16f, 0.12f);
            else if (Hash(ix / 4 + 50, iy / 4) > 0.84f) c = Color.Lerp(c, new Color(0.2f, 0.42f, 0.15f), 0.7f);
            c.a = 1; return c;
        });
        floorSpr = Make(32, (x, y) =>
        {
            Color c = new Color(0.24f, 0.19f, 0.14f) * (0.8f + Hash((int)x / 2, (int)y / 2) * 0.35f);
            if (x < 1 || y < 1) c *= 0.7f;
            c.a = 1; return c;
        });
        doorSpr = Make(32, (x, y) =>
        {
            float dx = x - 15.5f, dy = y - 15.5f;
            if (Mathf.Sqrt(dx * dx + dy * dy) < 5f) return gold;
            if (x < 2 || x > 29 || y < 2 || y > 29) return new Color(0.25f, 0.15f, 0.07f);
            Color c = new Color(0.5f, 0.3f, 0.12f) * (((int)x / 8) % 2 == 0 ? 1f : 0.82f);
            c.a = 1; return c;
        });
        exitClosedSpr = Make(32, (x, y) =>
        {
            Color c = new Color(0.32f, 0.32f, 0.38f) * (0.8f + Hash((int)x / 3, (int)y / 3) * 0.3f);
            float dx = x - 15.5f, dy = y - 15.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d > 7 && d < 9) c = new Color(0.7f, 0.2f, 0.2f);
            c.a = 1; return c;
        });
        exitOpenSpr = Make(32, (x, y) =>
        {
            float dx = x - 15.5f, dy = y - 15.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / 22f;
            return Color.Lerp(new Color(1f, 1f, 0.9f), new Color(1f, 0.75f, 0.2f), d);
        });
        circleSpr = Make(32, (x, y) =>
        {
            float dx = x - 15.5f, dy = y - 15.5f;
            return new Color(1, 1, 1, Mathf.Clamp01(16f - Mathf.Sqrt(dx * dx + dy * dy)));
        });
        keySpr = Make(32, (x, y) =>
        {
            float dx = x - 15.5f, dy = y - 22f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d < 7 && d > 3.5f) return gold;
            if (Mathf.Abs(x - 15.5f) < 1.8f && y > 4 && y < 16) return gold;
            if (y > 5 && y < 9 && x > 16 && x < 22) return gold;
            if (y > 10 && y < 13 && x > 16 && x < 21) return gold;
            return clear;
        });
        heartSpr = Make(32, (x, y) =>
        {
            float nx = (x - 15.5f) / 13f, ny = (y - 14f) / 13f;
            float a = nx * nx + ny * ny - 1f;
            float v = a * a * a - nx * nx * ny * ny * ny;
            if (v > 0) return clear;
            Color c = Color.Lerp(new Color(1f, 0.6f, 0.05f), new Color(1f, 0.95f, 0.4f), (y - 4f) / 22f);
            if (nx < -0.2f && ny > 0.2f && nx > -0.7f && ny < 0.7f) c = Color.white;
            return c;
        });
        spikeDownSpr = Make(32, (x, y) =>
        {
            if (x < 2 || x > 29 || y < 2 || y > 29) return new Color(0.35f, 0.35f, 0.4f);
            for (int i = 0; i < 4; i++)
            {
                float cx = 5 + i * 7;
                if (Mathf.Abs(x - cx) < 1.5f && Mathf.Abs(y - 16) < 1.5f) return new Color(0.1f, 0.1f, 0.12f);
            }
            return new Color(0.28f, 0.28f, 0.33f);
        });
        spikeUpSpr = Make(32, (x, y) =>
        {
            if (x < 2 || x > 29 || y < 2 || y > 29) return new Color(0.35f, 0.35f, 0.4f);
            for (int i = 0; i < 4; i++)
            {
                float cx = 5 + i * 7;
                float hw = 3.2f * (1f - (y - 4f) / 24f);
                if (y >= 4 && y <= 28 && Mathf.Abs(x - cx) <= hw)
                    return x < cx ? new Color(0.9f, 0.92f, 1f) : new Color(0.55f, 0.6f, 0.7f);
            }
            return new Color(0.2f, 0.2f, 0.25f);
        });
        spiderSpr = Make(32, (x, y) =>
        {
            float dx = x - 15.5f, dy = y - 15.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d < 7f)
            {
                Color c = new Color(0.2f, 0.05f, 0.08f);
                float ex = Mathf.Abs(dx) - 3f, ey = dy - 4f;
                if (ex * ex + ey * ey < 2.6f) c = new Color(1f, 0.1f, 0.1f);
                c.a = 1; return c;
            }
            float ax = Mathf.Abs(dx);
            for (int i = 0; i < 4; i++)
            {
                float ty = (i - 1.5f) * 9f;
                if (SegDist(ax, dy, 4f, (i - 1.5f) * 2f, 15f, ty) < 1.2f) return new Color(0.3f, 0.08f, 0.1f);
            }
            return clear;
        });
        darkSpr = Make(128, (x, y) =>
        {
            float dx = (x - 63.5f) / 64f, dy = (y - 63.5f) / 64f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float t = Mathf.Clamp01((d - 0.2f) / 0.3f);
            t = t * t * (3f - 2f * t);
            return new Color(0.02f, 0.01f, 0.03f, t * 0.82f);
        });
    }

    // ---------------------------------------------------------------- WORLD
    SpriteRenderer Spr(Sprite s, Vector2 pos, int order, Transform parent, float scale = 1f, Color? col = null)
    {
        var g = new GameObject("s");
        g.transform.SetParent(parent, false);
        g.transform.localPosition = new Vector3(pos.x, pos.y, 0);
        g.transform.localScale = Vector3.one * scale;
        var r = g.AddComponent<SpriteRenderer>();
        r.sprite = s; r.sortingOrder = order;
        if (col.HasValue) r.color = col.Value;
        return r;
    }

    void BuildWorld()
    {
        if (world != null) Destroy(world.gameObject);
        world = new GameObject("World").transform;
        spiders.Clear(); traps.Clear(); keyItems.Clear(); bullets.Clear(); doorObjs.Clear();
        rows = MAP.Length; cols = MAP[0].Length;
        grid = new char[cols, rows];
        int trapIndex = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Vector2 p = new Vector2(c, rows - 1 - r);
                grid[c, r] = '.';
                Spr(floorSpr, p, 0, world);
                switch (MAP[r][c])
                {
                    case '#': grid[c, r] = '#'; Spr(wallSpr, p, 1, world); break;
                    case 'D': grid[c, r] = 'D'; doorObjs[new Vector2Int(c, r)] = Spr(doorSpr, p, 2, world).gameObject; break;
                    case 'E': grid[c, r] = 'E'; exitPos = p; exitSr = Spr(exitClosedSpr, p, 2, world); break;
                    case 'P': startPos = p; break;
                    case 'K': keyItems.Add(new Pickup { pos = p, go = Spr(keySpr, p, 5, world, 0.8f).gameObject }); break;
                    case 'S': spiders.Add(NewSpider(p)); break;
                    case 'T': traps.Add(new Trap { pos = p, phase = trapIndex++ * 0.7f, sr = Spr(spikeDownSpr, p, 3, world) }); break;
                    case 'H': heartPos = p; heartObj = Spr(heartSpr, p, 5, world, 1.1f).gameObject; break;
                }
            }
        }
        // player (top-down explorer with hat)
        playerPos = startPos;
        playerT = new GameObject("Player").transform;
        playerT.SetParent(world, false);
        playerT.position = startPos;
        playerSr = Spr(circleSpr, Vector2.zero, 10, playerT, 0.62f, new Color(0.62f, 0.5f, 0.28f));
        Spr(circleSpr, Vector2.zero, 11, playerT, 0.5f, new Color(0.45f, 0.28f, 0.1f));
        Spr(circleSpr, Vector2.zero, 12, playerT, 0.26f, new Color(0.3f, 0.17f, 0.06f));
        torchT = Spr(circleSpr, new Vector2(0.35f, 0), 12, playerT, 0.16f, new Color(1f, 0.85f, 0.3f)).transform;
        darkT = Spr(darkSpr, Vector2.zero, 50, playerT, 26f).transform;

        cam.transform.position = new Vector3((cols - 1) / 2f, (rows - 1) / 2f, -10f);
    }

    Spider NewSpider(Vector2 p)
    {
        var sr = Spr(spiderSpr, p, 6, world, 0.95f);
        return new Spider { t = sr.transform, sr = sr, pos = p, dir = RandCard(), turn = Random.Range(1f, 3f) };
    }

    static Vector2 RandCard()
    {
        switch (Random.Range(0, 4))
        {
            case 0: return Vector2.up;
            case 1: return Vector2.down;
            case 2: return Vector2.left;
            default: return Vector2.right;
        }
    }

    // ---------------------------------------------------------------- GAME FLOW
    void StartGame()
    {
        playerName = string.IsNullOrWhiteSpace(playerName) ? "Explorer" : playerName.Trim();
        PlayerPrefs.SetString("lastName", playerName);
        BuildWorld();
        lives = StartLives; keys = 0; score = 0; invuln = 0; fireCd = 0; gameTime = 0;
        exitOpen = false; heartTaken = false; shake = 0; facing = Vector2.right;
        Say("The doors slam shut behind you! Find the keys...", 4f);
        state = State.Play;
    }

    void EndGame(bool win, string text, string detail)
    {
        state = win ? State.Won : State.Lost;
        resultText = text; resultDetail = detail;
        string bk = "best_" + playerName, pk = "plays_" + playerName;
        PlayerPrefs.SetInt(pk, PlayerPrefs.GetInt(pk, 0) + 1);
        if (score > PlayerPrefs.GetInt(bk, 0)) PlayerPrefs.SetInt(bk, score);
        if (score > PlayerPrefs.GetInt("hiScore", 0)) { PlayerPrefs.SetInt("hiScore", score); PlayerPrefs.SetString("hiName", playerName); }
        PlayerPrefs.Save();
    }

    void Say(string m, float t = 3f) { message = m; messageT = t; }

    void Hurt(string why)
    {
        if (invuln > 0f || state != State.Play) return;
        lives--; invuln = 2f; shake = 0.3f;
        Say(why + " Lives left: " + Mathf.Max(0, lives), 2f);
        if (lives <= 0) EndGame(false, "YOU PERISHED", why);
    }

    // ---------------------------------------------------------------- COLLISION (grid based)
    char Tile(float wx, float wy)
    {
        int c = Mathf.RoundToInt(wx), r = rows - 1 - Mathf.RoundToInt(wy);
        if (c < 0 || r < 0 || c >= cols || r >= rows) return '#';
        return grid[c, r];
    }

    bool Blocked(Vector2 p, float h, bool isPlayer)
    {
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                float wx = p.x + sx * h, wy = p.y + sy * h;
                char ch = Tile(wx, wy);
                if (ch == '#' || ch == 'E') return true;
                if (ch == 'D')
                {
                    if (isPlayer && keys > 0) { OpenDoor(Mathf.RoundToInt(wx), rows - 1 - Mathf.RoundToInt(wy)); continue; }
                    if (isPlayer) Say("Locked! You need a key.", 1.5f);
                    return true;
                }
            }
        return false;
    }

    void OpenDoor(int c, int r)
    {
        keys--; score += ScoreDoor;
        grid[c, r] = '.';
        var k = new Vector2Int(c, r);
        if (doorObjs.ContainsKey(k)) { Destroy(doorObjs[k]); doorObjs.Remove(k); }
        Say("Door unlocked! (+" + ScoreDoor + ")", 2f);
    }

    // ---------------------------------------------------------------- UPDATE
    void Update()
    {
        if (state == State.Menu)
        {
            // idle animation of the temple behind the menu
            return;
        }
        if (state != State.Play) return;
        if (Inp.Restart()) { StartGame(); return; }

        float dt = Time.deltaTime;
        gameTime += dt;
        if (messageT > 0) messageT -= dt;
        if (invuln > 0) invuln -= dt;
        if (fireCd > 0) fireCd -= dt;
        if (shake > 0) shake -= dt;

        // --- player movement
        Vector2 mv = Inp.Move();
        if (mv.sqrMagnitude > 1f) mv.Normalize();
        if (mv.sqrMagnitude > 0.01f) facing = mv.normalized;
        Vector2 d = mv * PlayerSpeed * dt;
        Vector2 n = new Vector2(playerPos.x + d.x, playerPos.y);
        if (!Blocked(n, 0.27f, true)) playerPos = n;
        n = new Vector2(playerPos.x, playerPos.y + d.y);
        if (!Blocked(n, 0.27f, true)) playerPos = n;
        playerT.position = playerPos;
        torchT.localPosition = facing * 0.35f;
        playerSr.color = invuln > 0 && Mathf.Sin(Time.time * 30f) > 0 ? new Color(1, 0.3f, 0.3f) : new Color(0.62f, 0.5f, 0.28f);
        darkT.localScale = Vector3.one * (26f + Mathf.Sin(Time.time * 9f) * 0.3f);

        // --- shooting
        if (Inp.Fire() && fireCd <= 0f)
        {
            fireCd = FireCooldown;
            var br = Spr(circleSpr, Vector2.zero, 9, world, 0.22f, new Color(1f, 0.9f, 0.3f));
            Vector2 sp = playerPos + facing * 0.4f;
            br.transform.position = sp;
            bullets.Add(new Bullet { t = br.transform, pos = sp, dir = facing });
        }
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            var b = bullets[i];
            b.pos += b.dir * BulletSpeed * dt; b.life -= dt;
            b.t.position = b.pos;
            bool dead = b.life <= 0f || Blocked(b.pos, 0.08f, false);
            if (!dead)
                for (int s = spiders.Count - 1; s >= 0; s--)
                {
                    var sp2 = spiders[s];
                    if (Vector2.Distance(sp2.pos, b.pos) < 0.5f)
                    {
                        dead = true; sp2.hp--; sp2.flash = 1f;
                        if (sp2.hp <= 0)
                        {
                            score += ScoreSpider; Destroy(sp2.t.gameObject); spiders.RemoveAt(s);
                            Say("Spider destroyed! (+" + ScoreSpider + ")", 1.5f);
                        }
                        break;
                    }
                }
            if (dead) { Destroy(b.t.gameObject); bullets.RemoveAt(i); }
        }

        // --- spiders
        foreach (var s in spiders)
        {
            Vector2 toP = playerPos - s.pos;
            float dist = toP.magnitude;
            bool chase = dist < SpiderSight;
            Vector2 dir = chase ? toP / dist : s.dir;
            Vector2 step = dir * (chase ? SpiderChaseSpeed : SpiderPatrolSpeed) * dt;
            bool moved = false;
            if (Mathf.Abs(step.x) > 0.0001f && !Blocked(s.pos + new Vector2(step.x, 0), 0.3f, false)) { s.pos.x += step.x; moved = true; }
            if (Mathf.Abs(step.y) > 0.0001f && !Blocked(s.pos + new Vector2(0, step.y), 0.3f, false)) { s.pos.y += step.y; moved = true; }
            s.turn -= dt;
            if (!chase && (!moved || s.turn <= 0f)) { s.dir = RandCard(); s.turn = Random.Range(1.5f, 3.5f); }
            s.t.position = s.pos;
            if (dir.sqrMagnitude > 0.01f) s.t.up = new Vector3(dir.x, dir.y, 0);
            if (s.flash > 0) s.flash -= dt * 4f;
            s.sr.color = Color.Lerp(Color.white, new Color(1f, 0.3f, 0.3f), Mathf.Clamp01(s.flash));
            if (Vector2.Distance(s.pos, playerPos) < 0.55f) Hurt("A spider bit you!");
        }

        // --- traps (3s cycle: warning, then spikes up)
        foreach (var t in traps)
        {
            float ph = (gameTime + t.phase) % 3f;
            bool up = ph > 1.8f, warn = ph > 1.3f && ph <= 1.8f;
            t.sr.sprite = up ? spikeUpSpr : spikeDownSpr;
            t.sr.color = warn ? new Color(1f, 0.55f, 0.4f) : Color.white;
            if (up && Vector2.Distance(t.pos, playerPos) < 0.5f) Hurt("Spikes!");
        }

        // --- pickups
        for (int i = keyItems.Count - 1; i >= 0; i--)
            if (Vector2.Distance(keyItems[i].pos, playerPos) < 0.5f)
            {
                keys++; score += ScoreKey; Destroy(keyItems[i].go); keyItems.RemoveAt(i);
                Say("Golden key found! (+" + ScoreKey + ")", 2f);
            }
        if (!heartTaken)
        {
            heartObj.transform.localScale = Vector3.one * (1.1f + Mathf.Sin(Time.time * 4f) * 0.12f);
            if (Vector2.Distance(heartPos, playerPos) < 0.6f)
            {
                heartTaken = true; exitOpen = true; sealTimer = EscapeTime; score += ScoreHeart; shake = 0.6f;
                Destroy(heartObj);
                exitSr.sprite = exitOpenSpr;
                grid[Mathf.RoundToInt(exitPos.x), rows - 1 - Mathf.RoundToInt(exitPos.y)] = '.';
                Say("HEART OF ETERNITY! The temple is collapsing - RUN TO THE EXIT!", 5f);
            }
        }
        else
        {
            sealTimer -= dt;
            if (Vector2.Distance(exitPos, playerPos) < 0.6f)
            {
                int bonus = Mathf.CeilToInt(sealTimer) * ScorePerSecondLeft + lives * ScorePerLife;
                score += bonus;
                EndGame(true, "YOU ESCAPED!", "Escape bonus: +" + bonus);
            }
            else if (sealTimer <= 0f)
            {
                sealTimer = 0;
                EndGame(false, "SEALED IN FOREVER", "The temple closed before you reached the exit.");
            }
        }
    }

    void LateUpdate()
    {
        if (cam == null) return;
        float aspect = Mathf.Max(0.1f, (float)Screen.width / Screen.height);
        cam.orthographicSize = Mathf.Max(rows / 2f + 0.6f, (cols / 2f + 0.6f) / aspect);
        Vector3 c = new Vector3((cols - 1) / 2f, (rows - 1) / 2f, -10f);
        float sh = (state == State.Play && (shake > 0f || (heartTaken && sealTimer > 0f))) ? (shake > 0f ? 0.12f : 0.03f) : 0f;
        if (sh > 0f) c += (Vector3)(Random.insideUnitCircle * sh);
        cam.transform.position = c;
    }

    // ---------------------------------------------------------------- UI (IMGUI, no packages needed)
    GUIStyle big, mid, small, btn, field;

    void Txt(Rect r, string t, GUIStyle st, Color col)
    {
        var old = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, st);
        GUI.color = col;
        GUI.Label(r, t, st);
        GUI.color = old;
    }

    void Panel(Rect r, float a)
    {
        var old = GUI.color;
        GUI.color = new Color(0.05f, 0.03f, 0.02f, a);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.9f, 0.7f, 0.25f, 1f);
        float b = 3;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, b), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - b, r.width, b), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, b, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - b, r.y, b, r.height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    void OnGUI()
    {
        float s = Screen.height / 720f, W = Screen.width, H = Screen.height;
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            mid = new GUIStyle(big);
            small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            btn = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            field = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleCenter };
        }
        big.fontSize = (int)(44 * s); mid.fontSize = (int)(26 * s); small.fontSize = (int)(20 * s);
        btn.fontSize = (int)(26 * s); field.fontSize = (int)(26 * s);
        Color gold = new Color(1f, 0.82f, 0.3f);

        if (state == State.Menu)
        {
            Rect p = new Rect(W / 2 - 400 * s, H / 2 - 320 * s, 800 * s, 640 * s);
            Panel(p, 0.88f);
            Txt(new Rect(p.x, p.y + 15 * s, p.width, 70 * s), "ANCIENT TEMPLE EXPLORER", big, gold);
            Txt(new Rect(p.x, p.y + 85 * s, p.width, 40 * s), "Find the Heart of Eternity... and Escape!", mid, Color.white);
            string[] lines =
            {
                "The doors slammed shut. Escape the temple!",
                "Move: WASD / Arrow keys     Shoot: SPACE / Left click     Restart: R",
                "Collect KEYS to open DOORS (+100 / +50). Shoot SPIDERS (+200). Avoid SPIKES.",
                "Grab the HEART (+1000), then reach the glowing EXIT before the temple seals!",
                "Faster escape + more lives left = bigger bonus.  You have 3 lives."
            };
            for (int i = 0; i < lines.Length; i++)
                Txt(new Rect(p.x + 20 * s, p.y + (150 + i * 42) * s, p.width - 40 * s, 40 * s), lines[i], small, i == 0 ? gold : Color.white);
            Txt(new Rect(p.x, p.y + 380 * s, p.width, 36 * s), "Explorer name:", mid, gold);
            playerName = GUI.TextField(new Rect(W / 2 - 160 * s, p.y + 420 * s, 320 * s, 44 * s), playerName ?? "", 12, field);
            string key = "best_" + (string.IsNullOrWhiteSpace(playerName) ? "Explorer" : playerName.Trim());
            string prof = "Games played: " + PlayerPrefs.GetInt("plays_" + playerName, 0) + "   Your best: " + PlayerPrefs.GetInt(key, 0)
                          + "   Top score: " + PlayerPrefs.GetInt("hiScore", 0) + " (" + PlayerPrefs.GetString("hiName", "-") + ")";
            Txt(new Rect(p.x, p.y + 472 * s, p.width, 36 * s), prof, small, Color.white);
            if (GUI.Button(new Rect(W / 2 - 150 * s, p.y + 530 * s, 300 * s, 60 * s), "ENTER THE TEMPLE", btn)) StartGame();
            return;
        }

        // HUD
        Panel(new Rect(0, 0, W, 46 * s), 0.7f);
        string hud = playerName + "     Score: " + score + "     Lives: " + lives + "     Keys: " + keys
                     + "     Best: " + PlayerPrefs.GetInt("best_" + playerName, 0);
        small.alignment = TextAnchor.MiddleLeft;
        Txt(new Rect(16 * s, 0, W, 46 * s), hud, small, Color.white);
        small.alignment = TextAnchor.MiddleCenter;
        if (state == State.Play)
        {
            if (heartTaken)
            {
                Color tc = sealTimer < 10f && Mathf.Sin(Time.time * 12f) > 0 ? Color.yellow : new Color(1f, 0.3f, 0.25f);
                Txt(new Rect(0, 52 * s, W, 60 * s), "ESCAPE! Temple seals in " + Mathf.CeilToInt(sealTimer) + "s", big, tc);
            }
            else
                Txt(new Rect(0, H - 40 * s, W, 36 * s), "Objective: " + (keyItems.Count + keys > 0 ? "find keys, open doors, reach the Heart of Eternity" : "reach the Heart of Eternity"), small, new Color(1, 1, 1, 0.8f));
            if (messageT > 0)
                Txt(new Rect(0, H - 100 * s, W, 50 * s), message, mid, gold);
        }
        else
        {
            bool win = state == State.Won;
            Rect p = new Rect(W / 2 - 330 * s, H / 2 - 220 * s, 660 * s, 440 * s);
            Panel(p, 0.92f);
            Txt(new Rect(p.x, p.y + 20 * s, p.width, 70 * s), resultText, big, win ? gold : new Color(1f, 0.35f, 0.3f));
            Txt(new Rect(p.x + 20 * s, p.y + 100 * s, p.width - 40 * s, 40 * s), resultDetail, small, Color.white);
            Txt(new Rect(p.x, p.y + 150 * s, p.width, 50 * s), "Final score: " + score, mid, Color.white);
            Txt(new Rect(p.x, p.y + 200 * s, p.width, 40 * s), playerName + "'s best: " + PlayerPrefs.GetInt("best_" + playerName, 0)
                + "   Top: " + PlayerPrefs.GetInt("hiScore", 0) + " (" + PlayerPrefs.GetString("hiName", "-") + ")", small, Color.white);
            if (GUI.Button(new Rect(p.x + 60 * s, p.y + 290 * s, 250 * s, 60 * s), "PLAY AGAIN", btn)) StartGame();
            if (GUI.Button(new Rect(p.xMax - 310 * s, p.y + 290 * s, 250 * s, 60 * s), "MAIN MENU", btn)) { BuildWorld(); state = State.Menu; }
        }
    }
}

// ---------------------------------------------------------------- INPUT 
static class Inp
{
    public static Vector2 Move()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return Vector2.zero;
        float x = ((k.dKey.isPressed || k.rightArrowKey.isPressed) ? 1 : 0) - ((k.aKey.isPressed || k.leftArrowKey.isPressed) ? 1 : 0);
        float y = ((k.wKey.isPressed || k.upArrowKey.isPressed) ? 1 : 0) - ((k.sKey.isPressed || k.downArrowKey.isPressed) ? 1 : 0);
        return new Vector2(x, y);
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    public static bool Fire()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current; var m = Mouse.current;
        return (k != null && k.spaceKey.isPressed) || (m != null && m.leftButton.isPressed);
#else
        return Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0);
#endif
    }

    public static bool Restart()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && k.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }
}
