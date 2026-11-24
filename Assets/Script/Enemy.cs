using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Apa pun yang bisa kena damage (musuh, peti, dll). Dipanggil oleh Projectile.</summary>
public interface IDamageable
{
    void TakeDamage(int amount, Vector2 hitDirection);
}


[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public abstract class Enemy : MonoBehaviour, IDamageable
{
    [Header("Nyawa")]
    [SerializeField] private int maxHealth = 3;

    [Header("Efek kena hit")]
    [SerializeField] private Color hitColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField] private float hitFlashDuration = 0.12f;
    [SerializeField] private float knockbackForce = 4f;
    [SerializeField] private float knockbackUp = 2f;
    [SerializeField] private float staggerTime = 0.5f;

    [Header("Efek mati")]
    [SerializeField] private Color deathColor = new Color(0.5f, 0.9f, 0.4f, 1f);
    [SerializeField] private int deathParticles = 16;
    [SerializeField] private float deathShrinkTime = 0.12f;

    [Header("Health bar")]
    [SerializeField] private bool showHealthBar = true;
    [SerializeField] private float barWidth = 0.8f;
    [SerializeField] private float barHeight = 0.1f;
    [SerializeField] private float barVisibleTime = 1.5f;

    [Header("Visual")]
    [SerializeField] private bool spriteFacesRight = true;     // matikan kalau sprite aslinya menghadap kiri

    [Header("Suara (opsional)")]
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip deathSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.7f;

    [Header("Animator (nama parameter bool)")]
    [SerializeField] private string animHitParam = "IsHit";
    [SerializeField] private string animDeadParam = "IsDed";
    [SerializeField] private float hitAnimHold = 0.12f;        // lama IsHit menyala setelah kena hit

    [Header("Serangan")]
    [SerializeField] private int contactDamage = 1;            // damage ke player saat bersentuhan (0 = tidak ada)

    public int Health { get; private set; }
    public int MaxHealth => maxHealth;
    public bool IsDead { get; private set; }
    public event System.Action<Enemy> Died;

    // Akses untuk class turunan
    protected Rigidbody2D rb;
    protected Collider2D col;
    protected SpriteRenderer sr;
    protected Animator animator;
    protected int Facing { get; private set; } = 1;            // 1 = kanan, -1 = kiri
    protected bool IsStaggered => staggerTimer > 0f;
    protected Color OriginalColor { get; private set; } = Color.white;
    protected int SortingLayerId => sr != null ? sr.sortingLayerID : 0;
    protected int SortingOrder => sr != null ? sr.sortingOrder : 0;

    private Color baseColor = Color.white;
    private float flashTimer;
    private float staggerTimer;
    private float hitAnimTimer;
    private float barTimer;
    private Transform barRoot;
    private Transform barFill;
    private SpriteRenderer barFillRenderer;
    private HashSet<string> animParams;

    private Transform player;
    private float nextPlayerSearch;
    private static Sprite pixelSprite;

    // ---------------------------------------------------------------- Hook untuk class turunan
    protected virtual void OnAwake() { }
    protected virtual void Tick(float dt) { }
    protected virtual void FixedTick(float dt) { }
    protected virtual void OnDamaged(int amount, Vector2 hitDirection) { }
    protected virtual void OnDied() { }

    /// <summary>Override untuk mencegah damage sentuh pada kondisi tertentu (mis. saat tidur).</summary>
    protected virtual bool CanHurtPlayer() { return true; }

    // ---------------------------------------------------------------- Siklus hidup
    void Awake()
    {
        Health = maxHealth;
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponentInChildren<SpriteRenderer>();
        animator = GetComponentInChildren<Animator>();
        if (sr != null) { baseColor = sr.color; OriginalColor = sr.color; }

        if (rb.bodyType == RigidbodyType2D.Dynamic) rb.freezeRotation = true;

        CacheAnimParams();
        if (showHealthBar) SetupBar();
        OnAwake();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        staggerTimer -= dt;

        if (hitAnimTimer > 0f)
        {
            hitAnimTimer -= dt;
            if (hitAnimTimer <= 0f) AnimBool(animHitParam, false);
        }

        if (sr != null && flashTimer > 0f)
        {
            flashTimer -= dt;
            sr.color = flashTimer > 0f
                ? Color.Lerp(baseColor, hitColor, flashTimer / hitFlashDuration)
                : baseColor;
        }

        if (barRoot != null && barRoot.gameObject.activeSelf && !IsDead)
        {
            barTimer -= dt;
            if (barTimer <= 0f) barRoot.gameObject.SetActive(false);
        }

        if (!IsDead) Tick(dt);
    }

    void FixedUpdate()
    {
        if (!IsDead) FixedTick(Time.fixedDeltaTime);
    }

    // ---------------------------------------------------------------- Damage ke player
    void OnCollisionStay2D(Collision2D collision)
    {
        if (IsDead || contactDamage <= 0 || !CanHurtPlayer()) return;
        var health = collision.collider.GetComponentInParent<PlayerHealth>();
        if (health != null) health.TakeDamage(contactDamage, transform.position);
    }

    // ---------------------------------------------------------------- Damage & mati
    public void TakeDamage(int amount, Vector2 hitDirection)
    {
        if (IsDead || amount <= 0) return;

        Health = Mathf.Max(0, Health - amount);
        flashTimer = hitFlashDuration;
        staggerTimer = staggerTime;

        if (rb.bodyType == RigidbodyType2D.Dynamic)
        {
            float dirX = Mathf.Abs(hitDirection.x) > 0.01f ? Mathf.Sign(hitDirection.x) : 0f;
            rb.linearVelocity = new Vector2(dirX * knockbackForce, Mathf.Max(rb.linearVelocity.y, knockbackUp));
        }

        if (barRoot != null)
        {
            UpdateBar();
            barRoot.gameObject.SetActive(true);
            barTimer = barVisibleTime;
        }

        if (Health <= 0)
        {
            Die();
        }
        else
        {
            AnimBool(animHitParam, true);
            hitAnimTimer = hitAnimHold;
            PlaySound(hitSound);
            OnDamaged(amount, hitDirection);
        }
    }

    void Die()
    {
        IsDead = true;
        AnimBool(animDeadParam, true);
        OnDied();
        Died?.Invoke(this);

        foreach (var c in GetComponentsInChildren<Collider2D>()) c.enabled = false;
        rb.linearVelocity = Vector2.zero;
        rb.simulated = false;
        if (barRoot != null) barRoot.gameObject.SetActive(false);

        var burst = ShotFx.CreateBurst("DeathBurst", deathColor, 0.45f, 0.18f, 4.5f,
                                       ParticleSystemShapeType.Circle, 0f, SortingLayerId, SortingOrder + 1);
        burst.transform.position = col.bounds.center;
        burst.Emit(deathParticles);
        Destroy(burst.gameObject, 1.2f);

        PlaySound(deathSound);
        StartCoroutine(ShrinkAndDestroy());
    }

    IEnumerator ShrinkAndDestroy()
    {
        Vector3 start = transform.localScale;
        float t = 0f;
        while (t < deathShrinkTime)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(start, Vector3.zero, t / deathShrinkTime);
            yield return null;
        }
        Destroy(gameObject);
    }

    // ---------------------------------------------------------------- Helper untuk class turunan
    /// <summary>Player terdekat (dicari otomatis lewat komponen PlayerMovement).</summary>
    protected Transform Player
    {
        get
        {
            if (player == null && Time.time >= nextPlayerSearch)
            {
                nextPlayerSearch = Time.time + 0.5f;
                var p = FindFirstObjectByType<PlayerMovement>();
                if (p != null) player = p.transform;
            }
            return player;
        }
    }

    protected float DistanceToPlayer()
    {
        var p = Player;
        return p != null ? Vector2.Distance(transform.position, p.position) : float.PositiveInfinity;
    }

    protected void SetFacing(int dir)
    {
        Facing = dir >= 0 ? 1 : -1;
        if (sr != null) sr.flipX = spriteFacesRight ? Facing < 0 : Facing > 0;
    }

    protected void FacePlayer()
    {
        var p = Player;
        if (p != null) SetFacing(p.position.x >= transform.position.x ? 1 : -1);
    }

    /// <summary>Ganti warna dasar sprite (mis. gelap saat tidur). Kedip kena hit tetap jalan.</summary>
    protected void SetBaseColor(Color c)
    {
        baseColor = c;
        if (sr != null && flashTimer <= 0f) sr.color = c;
    }

    /// <summary>Gambar kecil di atas kepala: "!", "?", atau "Z".</summary>
    protected void ShowEmote(EmoteType type, float duration = 0.8f)
    {
        Vector3 pos = new Vector3(col.bounds.center.x, col.bounds.max.y + 0.45f, 0f);
        FloatingSprite.Spawn(EnemyFx.Emote(type), pos, EnemyFx.EmoteColor(type), duration, 0.15f,
                             1f, 1f, SortingLayerId, SortingOrder + 4, transform);
    }

    protected void SpawnSleepZ()
    {
        Vector3 pos = new Vector3(col.bounds.center.x + Facing * 0.15f, col.bounds.max.y + 0.1f, 0f);
        FloatingSprite.Spawn(EnemyFx.Emote(EmoteType.Sleep), pos, EnemyFx.EmoteColor(EmoteType.Sleep),
                             1.4f, 0.6f, 0.6f, 1f, SortingLayerId, SortingOrder + 4);
    }

    // Animator: parameter yang tidak ada di controller diabaikan (tidak ada warning)
    void CacheAnimParams()
    {
        animParams = new HashSet<string>();
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (var p in animator.parameters) animParams.Add(p.name);
    }

    protected void AnimBool(string name, bool value)   { if (animParams.Contains(name)) animator.SetBool(name, value); }
    protected void AnimFloat(string name, float value) { if (animParams.Contains(name)) animator.SetFloat(name, value); }
    protected void AnimTrigger(string name)            { if (animParams.Contains(name)) animator.SetTrigger(name); }

    void PlaySound(AudioClip clip)
    {
        if (clip == null) return;
        var go = new GameObject("EnemySfx");
        var a = go.AddComponent<AudioSource>();
        a.spatialBlend = 0f;
        a.pitch = Random.Range(0.95f, 1.05f);
        a.PlayOneShot(clip, soundVolume);
        Destroy(go, clip.length + 0.2f);
    }

    // ---------------------------------------------------------------- Health bar
    void SetupBar()
    {
        Vector3 s = transform.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(s.x));
        float sy = Mathf.Max(0.0001f, Mathf.Abs(s.y));

        var root = new GameObject("HealthBar");
        barRoot = root.transform;
        barRoot.SetParent(transform, false);
        barRoot.localScale = new Vector3(1f / sx, 1f / sy, 1f);
        float topY = col.bounds.max.y - transform.position.y;
        barRoot.localPosition = new Vector3(0f, (topY + 0.2f) / sy, 0f);

        float pad = 0.04f;
        var bg = new GameObject("Bg").AddComponent<SpriteRenderer>();
        bg.transform.SetParent(barRoot, false);
        bg.sprite = Pixel();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
        bg.sortingLayerID = SortingLayerId;
        bg.sortingOrder = SortingOrder + 2;
        bg.transform.localScale = new Vector3(barWidth + pad, barHeight + pad, 1f);
        bg.transform.localPosition = new Vector3(-(barWidth + pad) * 0.5f, 0f, 0f);

        barFillRenderer = new GameObject("Fill").AddComponent<SpriteRenderer>();
        barFill = barFillRenderer.transform;
        barFill.SetParent(barRoot, false);
        barFillRenderer.sprite = Pixel();
        barFillRenderer.sortingLayerID = SortingLayerId;
        barFillRenderer.sortingOrder = SortingOrder + 3;
        barFill.localPosition = new Vector3(-barWidth * 0.5f, 0f, 0f);

        UpdateBar();
        root.SetActive(false);
    }

    void UpdateBar()
    {
        if (barFill == null) return;
        float frac = Mathf.Clamp01((float)Health / maxHealth);
        barFill.localScale = new Vector3(barWidth * frac, barHeight, 1f);
        barFillRenderer.color = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.85f, 0.3f), frac);
    }

    static Sprite Pixel()
    {
        if (pixelSprite != null) return pixelSprite;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        pixelSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), 1f);
        return pixelSprite;
    }
}

public enum EmoteType { Sleep, Exclaim, Question }

/// <summary>Sprite pixel-art kecil ("!", "?", "Z") yang digambar lewat kode.</summary>
public static class EnemyFx
{
    private static readonly Dictionary<EmoteType, Sprite> cache = new Dictionary<EmoteType, Sprite>();

    private static readonly string[] Z =
    {
        "#####",
        "   ##",
        "  ## ",
        " ##  ",
        "#####"
    };
    private static readonly string[] Exclaim =
    {
        "##", "##", "##", "##", "##", "  ", "##"
    };
    private static readonly string[] Question =
    {
        " ### ",
        "#   #",
        "   ##",
        "  ## ",
        "  #  ",
        "     ",
        "  #  "
    };

    public static Sprite Emote(EmoteType type)
    {
        if (cache.TryGetValue(type, out var s) && s != null) return s;
        string[] rows = type == EmoteType.Sleep ? Z : (type == EmoteType.Exclaim ? Exclaim : Question);
        s = Build(rows, 14f);
        cache[type] = s;
        return s;
    }

    public static Color EmoteColor(EmoteType type)
    {
        switch (type)
        {
            case EmoteType.Sleep: return new Color(0.8f, 0.9f, 1f, 1f);
            case EmoteType.Exclaim: return new Color(1f, 0.85f, 0.2f, 1f);
            default: return Color.white;
        }
    }

    private static Sprite Build(string[] rows, float ppu)
    {
        int w = rows[0].Length, h = rows.Length;
        var tex = new Texture2D(w + 2, h + 2, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color[(w + 2) * (h + 2)];

        bool Filled(int x, int ry) => x >= 0 && x < w && ry >= 0 && ry < h && rows[ry][x] == '#';

        for (int ty = 0; ty < h + 2; ty++)
        {
            for (int tx = 0; tx < w + 2; tx++)
            {
                int x = tx - 1;
                int ry = h - ty;                                  // baris teks dari atas
                Color c = new Color(0f, 0f, 0f, 0f);
                if (Filled(x, ry)) c = Color.white;
                else
                {
                    bool near = false;
                    for (int dx = -1; dx <= 1 && !near; dx++)
                        for (int dy = -1; dy <= 1 && !near; dy++)
                            if (Filled(x + dx, ry + dy)) near = true;
                    if (near) c = new Color(0.08f, 0.08f, 0.1f, 1f);   // garis tepi gelap
                }
                pixels[ty * (w + 2) + tx] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w + 2, h + 2), new Vector2(0.5f, 0.5f), ppu);
    }
}

/// <summary>Sprite sementara yang naik, membesar sedikit, memudar, lalu hilang sendiri.</summary>
public class FloatingSprite : MonoBehaviour
{
    private SpriteRenderer sr;
    private Color color;
    private float life, rise, startScale, endScale, elapsed;
    private Transform follow;
    private Vector3 followOffset, basePos;

    public static FloatingSprite Spawn(Sprite sprite, Vector3 position, Color color, float lifetime, float rise,
                                       float startScale, float endScale, int sortingLayerId, int sortingOrder,
                                       Transform follow = null)
    {
        var go = new GameObject("Emote");
        go.transform.position = position;
        var f = go.AddComponent<FloatingSprite>();
        f.sr = go.AddComponent<SpriteRenderer>();
        f.sr.sprite = sprite;
        f.sr.color = color;
        f.sr.sortingLayerID = sortingLayerId;
        f.sr.sortingOrder = sortingOrder;
        f.color = color;
        f.life = Mathf.Max(0.05f, lifetime);
        f.rise = rise;
        f.startScale = startScale;
        f.endScale = endScale;
        f.follow = follow;
        f.basePos = position;
        if (follow != null) f.followOffset = position - follow.position;
        go.transform.localScale = Vector3.zero;
        return f;
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / life);

        if (follow != null) basePos = follow.position + followOffset;
        transform.position = basePos + Vector3.up * (rise * t);

        // "pop" singkat di awal
        float pop = elapsed < 0.12f ? Mathf.Lerp(0f, 1.3f, elapsed / 0.12f)
                  : elapsed < 0.2f ? Mathf.Lerp(1.3f, 1f, (elapsed - 0.12f) / 0.08f)
                  : 1f;
        transform.localScale = Vector3.one * (Mathf.Lerp(startScale, endScale, t) * pop);

        Color c = color;
        c.a = color.a * (t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f);
        sr.color = c;

        if (elapsed >= life) Destroy(gameObject);
    }
}