using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Parry: tekan tombol, dan selama jendela singkat (default 0,2 detik) serangan musuh dinegasikan.
/// Parry berhasil: damage dibatalkan, musuh kena counter + pingsan + terpental, ada percikan, hit-stop singkat,
/// dan player kebal sebentar (tanpa berkedip). Parry meleset: player terkunci sebentar (recovery).
///
/// Tombol: klik kanan mouse, F, atau tombol West gamepad (X / Square).
/// Animator (opsional): parameter bool Parry atau IsParrying (menyala selama jendela parry dan recovery).
/// Dipanggil oleh PlayerHealth.TakeDamage -> TryParry().
/// </summary>
[RequireComponent(typeof(PlayerMovement), typeof(PlayerHealth))]
public class PlayerParry : MonoBehaviour
{
    [Header("Parry")]
    [SerializeField] private float activeTime = 0.2f;          // jendela parry
    [SerializeField] private float whiffRecovery = 0.3f;       // terkunci sebentar kalau meleset
    [SerializeField] private float cooldown = 0.6f;            // dihitung sejak parry dimulai
    [SerializeField] private float inputBuffer = 0.08f;

    [Header("Kalau berhasil")]
    [SerializeField] private float graceAfterSuccess = 0.4f;   // kebal sebentar setelah parry berhasil
    [SerializeField] private int counterDamage = 1;            // damage balasan ke musuh (0 = tidak ada)
    [SerializeField] private float enemyStaggerTime = 0.8f;    // musuh pingsan selama ini
    [SerializeField] private float enemyKnockback = 7f;

    [Header("Efek")]
    [SerializeField] private float hitStopTime = 0.08f;        // detik (waktu nyata)
    [SerializeField] private int sparkCount = 14;
    [SerializeField] private Color sparkColor = new Color(1f, 0.95f, 0.6f, 1f);
    [SerializeField] private Color shieldColor = new Color(0.45f, 0.85f, 1f, 1f);

    [Header("Input")]
    [SerializeField] private bool useRightMouse = true;
    [SerializeField] private bool useFKey = true;

    /// <summary>Jendela parry sedang aktif.</summary>
    public bool IsActive { get; private set; }
    /// <summary>Aktif atau sedang recovery (untuk animasi).</summary>
    public bool IsParrying => IsActive || recoveryTimer > 0f;

    public event System.Action ParrySucceeded;

    private PlayerMovement movement;
    private PlayerHealth health;
    private Collider2D col;
    private SpriteRenderer sr;
    private Animator animator;
    private HashSet<string> animParams;

    private float activeTimer, recoveryTimer, cooldownTimer, graceTimer, bufferTimer;
    private bool actionLocked;
    private bool hitStopping;

    private static Sprite ringSprite;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();
        col = GetComponent<Collider2D>();
        sr = GetComponentInChildren<SpriteRenderer>();
        animator = GetComponentInChildren<Animator>();

        animParams = new HashSet<string>();
        if (animator != null && animator.runtimeAnimatorController != null)
            foreach (var p in animator.parameters) animParams.Add(p.name);
    }

    void OnDisable()
    {
        if (hitStopping) { Time.timeScale = 1f; hitStopping = false; }
        if (actionLocked) { movement.SetActionLock(false); actionLocked = false; }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        cooldownTimer -= dt;
        graceTimer -= dt;
        bufferTimer -= dt;

        if (health.IsDead)
        {
            IsActive = false;
            recoveryTimer = 0f;
        }
        else
        {
            if (ParryPressed()) bufferTimer = inputBuffer;

            if (IsActive)
            {
                activeTimer -= dt;
                if (activeTimer <= 0f)
                {
                    IsActive = false;
                    recoveryTimer = whiffRecovery;             // meleset
                }
            }
            else if (recoveryTimer > 0f)
            {
                recoveryTimer -= dt;
            }

            if (!IsActive && recoveryTimer <= 0f && cooldownTimer <= 0f && bufferTimer > 0f && !movement.IsDashing)
                StartParry();
        }

        bool locked = IsParrying;
        if (locked != actionLocked)
        {
            actionLocked = locked;
            movement.SetActionLock(locked);                    // tidak bisa jalan/lompat/dash selama parry
        }
        if (animParams.Contains("Parry")) animator.SetBool("Parry", locked);
        if (animParams.Contains("IsParrying")) animator.SetBool("IsParrying", locked);
    }

    // ---------------------------------------------------------------- Parry
    bool ParryPressed()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        var gp = Gamepad.current;
        return (useFKey && kb != null && kb.fKey.wasPressedThisFrame)
            || (useRightMouse && mouse != null && mouse.rightButton.wasPressedThisFrame)
            || (gp != null && gp.buttonWest.wasPressedThisFrame);
    }

    void StartParry()
    {
        IsActive = true;
        activeTimer = activeTime;
        cooldownTimer = cooldown;
        bufferTimer = 0f;
        SpawnRing(shieldColor, activeTime, 0.7f, 1.1f);
    }

    /// <summary>
    /// Dipanggil PlayerHealth saat player akan kena damage. Return true kalau damage dibatalkan.
    /// </summary>
    public bool TryParry(Vector2 sourcePosition, Component source)
    {
        if (IsActive)
        {
            OnSuccess(sourcePosition, source);
            return true;
        }
        return graceTimer > 0f;                                // kebal sebentar setelah parry berhasil
    }

    void OnSuccess(Vector2 sourcePosition, Component source)
    {
        IsActive = false;
        recoveryTimer = 0f;
        graceTimer = graceAfterSuccess;
        cooldownTimer = 0.1f;                                  // hadiah: bisa parry lagi hampir langsung

        Vector2 mid = ((Vector2)col.bounds.center + sourcePosition) * 0.5f;
        int layerId = sr != null ? sr.sortingLayerID : 0;
        int order = sr != null ? sr.sortingOrder : 0;

        var burst = ShotFx.CreateBurst("ParrySpark", sparkColor, 0.25f, 0.12f, 6f,
                                       ParticleSystemShapeType.Circle, 0f, layerId, order + 6);
        burst.transform.position = mid;
        burst.Emit(sparkCount);
        Destroy(burst.gameObject, 1f);
        SpawnRing(Color.white, 0.3f, 0.8f, 1.8f);

        // Counter ke musuh
        var enemy = source != null ? source.GetComponentInParent<Enemy>() : null;
        if (enemy != null && !enemy.IsDead)
        {
            float dirX = enemy.transform.position.x >= transform.position.x ? 1f : -1f;
            enemy.TakeDamage(counterDamage, new Vector2(dirX, 0f));
            if (!enemy.IsDead) enemy.Stun(enemyStaggerTime, new Vector2(dirX * enemyKnockback, 3f));
        }

        if (hitStopTime > 0f && !hitStopping && Time.timeScale > 0.5f) StartCoroutine(HitStop());
        ParrySucceeded?.Invoke();
    }

    IEnumerator HitStop()
    {
        hitStopping = true;
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(hitStopTime);
        Time.timeScale = 1f;
        hitStopping = false;
    }

    // ---------------------------------------------------------------- Efek cincin
    void SpawnRing(Color color, float life, float startScale, float endScale)
    {
        Vector3 pos = col.bounds.center;
        int layerId = sr != null ? sr.sortingLayerID : 0;
        int order = sr != null ? sr.sortingOrder : 0;
        FloatingSprite.Spawn(Ring(), pos, color, life, 0f, startScale, endScale, layerId, order + 5, transform);
    }

    static Sprite Ring()
    {
        if (ringSprite != null) return ringSprite;
        const int size = 24;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                pixels[y * size + x] = (d >= 9.2f && d <= 11f) ? Color.white : new Color(0f, 0f, 0f, 0f);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        return ringSprite;
    }
}