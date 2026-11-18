using System.Collections;
using UnityEngine;

/// <summary>Apa pun yang bisa kena damage (musuh, peti, dll).</summary>
public interface IDamageable
{
    void TakeDamage(int amount, Vector2 hitDirection);
}

/// <summary>
/// Pasang di musuh (Add Component > Enemy Health). Musuh bisa kena hit dari Projectile:
/// berkedip merah, terdorong, health bar muncul sebentar, lalu pecah jadi partikel saat nyawa habis.
/// Tanpa setup lain: efek dan health bar dibuat otomatis lewat kode.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class EnemyHealth : MonoBehaviour, IDamageable
{
    [Header("Nyawa")]
    [SerializeField] private int maxHealth = 3;

    [Header("Efek kena hit")]
    [SerializeField] private Color hitColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField] private float hitFlashDuration = 0.12f;
    [SerializeField] private float knockbackForce = 4f;        // dorongan horizontal
    [SerializeField] private float knockbackUp = 2f;           // sedikit terpental ke atas

    [Header("Efek mati")]
    [SerializeField] private Color deathColor = new Color(0.5f, 0.9f, 0.4f, 1f);
    [SerializeField] private int deathParticles = 16;
    [SerializeField] private float deathShrinkTime = 0.12f;

    [Header("Health bar")]
    [SerializeField] private bool showHealthBar = true;
    [SerializeField] private float barWidth = 0.8f;
    [SerializeField] private float barHeight = 0.1f;
    [SerializeField] private float barVisibleTime = 1.5f;      // detik setelah kena hit

    [Header("Suara (opsional)")]
    [SerializeField] private AudioClip hitSound;               // mis. hurt.wav
    [SerializeField] private AudioClip deathSound;             // mis. explosion.wav
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.7f;

    public int Health { get; private set; }
    public bool IsDead { get; private set; }
    public event System.Action<EnemyHealth> Died;

    private Rigidbody2D rb;
    private Collider2D col;
    private SpriteRenderer sr;
    private Color baseColor = Color.white;
    private float flashTimer;
    private float barTimer;
    private Transform barRoot;
    private Transform barFill;
    private SpriteRenderer barFillRenderer;

    private static Sprite pixelSprite;

    void Awake()
    {
        Health = maxHealth;
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;

        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
            rb.freezeRotation = true;                              // jangan berputar saat terdorong

        if (showHealthBar) SetupBar();
    }

    // ---------------------------------------------------------------- Kena damage
    public void TakeDamage(int amount, Vector2 hitDirection)
    {
        if (IsDead || amount <= 0) return;

        Health = Mathf.Max(0, Health - amount);
        flashTimer = hitFlashDuration;

        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
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

        if (Health <= 0) Die();
        else PlaySound(hitSound);
    }

    void Die()
    {
        IsDead = true;
        Died?.Invoke(this);

        foreach (var c in GetComponentsInChildren<Collider2D>()) c.enabled = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
        }
        if (barRoot != null) barRoot.gameObject.SetActive(false);

        int layerId = sr != null ? sr.sortingLayerID : 0;
        int order = sr != null ? sr.sortingOrder : 0;
        var burst = ShotFx.CreateBurst("DeathBurst", deathColor, 0.45f, 0.18f, 4.5f,
                                       ParticleSystemShapeType.Circle, 0f, layerId, order + 1);
        burst.transform.position = col != null ? (Vector3)col.bounds.center : transform.position;
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

    // ---------------------------------------------------------------- Visual
    void Update()
    {
        float dt = Time.deltaTime;

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
    }

    void SetupBar()
    {
        Vector3 s = transform.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(s.x));
        float sy = Mathf.Max(0.0001f, Mathf.Abs(s.y));

        var root = new GameObject("HealthBar");
        barRoot = root.transform;
        barRoot.SetParent(transform, false);
        barRoot.localScale = new Vector3(1f / sx, 1f / sy, 1f);    // ukuran bar tetap walau musuh di-scale
        float topY = col != null ? col.bounds.max.y - transform.position.y : 0.5f;
        barRoot.localPosition = new Vector3(0f, (topY + 0.2f) / sy, 0f);

        int layerId = sr != null ? sr.sortingLayerID : 0;
        int order = sr != null ? sr.sortingOrder : 0;

        float pad = 0.04f;
        var bg = new GameObject("Bg").AddComponent<SpriteRenderer>();
        bg.transform.SetParent(barRoot, false);
        bg.sprite = Pixel();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
        bg.sortingLayerID = layerId;
        bg.sortingOrder = order + 2;
        bg.transform.localScale = new Vector3(barWidth + pad, barHeight + pad, 1f);
        bg.transform.localPosition = new Vector3(-(barWidth + pad) * 0.5f, 0f, 0f);

        barFillRenderer = new GameObject("Fill").AddComponent<SpriteRenderer>();
        barFill = barFillRenderer.transform;
        barFill.SetParent(barRoot, false);
        barFillRenderer.sprite = Pixel();
        barFillRenderer.sortingLayerID = layerId;
        barFillRenderer.sortingOrder = order + 3;
        barFill.localPosition = new Vector3(-barWidth * 0.5f, 0f, 0f);

        UpdateBar();
        root.SetActive(false);                                     // muncul setelah kena hit pertama
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
        pixelSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), 1f); // pivot kiri
        return pixelSprite;
    }

    void PlaySound(AudioClip clip)
    {
        if (clip == null) return;
        var go = new GameObject("EnemySfx");
        var a = go.AddComponent<AudioSource>();
        a.spatialBlend = 0f;                                       // 2D: volume tidak turun karena jarak kamera
        a.pitch = Random.Range(0.95f, 1.05f);
        a.PlayOneShot(clip, soundVolume);
        Destroy(go, clip.length + 0.2f);
    }
}