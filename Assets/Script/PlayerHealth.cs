using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Health player: damage, invincibility frames (berkedip), knockback, mati + respawn.
/// Tampilan health bar diurus HealthBarUI (pasang di objek HealthBar di Canvas).
/// Pasang di objek Player (yang punya PlayerMovement). Musuh memanggil: playerHealth.TakeDamage(jumlah, posisiMusuh).
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(PlayerMovement))]
public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 5;

    [Header("Setelah kena hit")]
    [SerializeField] private float invincibleTime = 1f;     // kebal sementara setelah kena
    [SerializeField] private float blinkInterval = 0.08f;
    [SerializeField, Range(0f, 1f)] private float blinkAlpha = 0.3f;
    [SerializeField] private Vector2 knockback = new Vector2(6f, 5f); // x dikali arah menjauhi sumber damage
    [SerializeField] private float knockbackLockTime = 0.25f;         // kontrol player dikunci sebentar

    [Header("Mati")]
    [SerializeField] private float respawnDelay = 1.5f;
    [SerializeField] private bool reloadSceneOnDeath = false; // false = respawn di posisi awal (tidak perlu Build Settings)

    public int Current { get; private set; }
    public int Max => maxHealth;
    public bool IsDead { get; private set; }

    /// <summary>(health sekarang, health maksimal)</summary>
    public event Action<int, int> HealthChanged;
    public event Action Died;

    private Rigidbody2D rb;
    private PlayerMovement movement;
    private Staff staff;
    private SpriteRenderer[] renderers;
    private Color[] baseColors;
    private Vector3 spawnPoint;
    private float invincibleTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        movement = GetComponent<PlayerMovement>();
        staff = GetComponentInChildren<Staff>();
        Current = maxHealth;
        spawnPoint = transform.position;
    }

    void Start()
    {
        // Setelah semua Awake selesai, supaya "Visual" buatan PlayerMovement ikut terambil
        renderers = GetComponentsInChildren<SpriteRenderer>();
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].color;

        HealthChanged?.Invoke(Current, maxHealth);
    }

    void Update()
    {
        if (invincibleTimer > 0f) invincibleTimer -= Time.deltaTime;
        UpdateBlink();
    }

    // ---------------------------------------------------------------- Damage & heal
    /// <summary>Kurangi health. Return true kalau damage benar-benar masuk (tidak sedang kebal/mati).</summary>
    public bool TakeDamage(int amount, Vector2 sourcePosition)
    {
        if (IsDead || amount <= 0 || invincibleTimer > 0f) return false;

        Current = Mathf.Max(0, Current - amount);
        invincibleTimer = invincibleTime;
        HealthChanged?.Invoke(Current, maxHealth);

        float dir = transform.position.x >= sourcePosition.x ? 1f : -1f;
        movement.ApplyKnockback(new Vector2(dir * knockback.x, knockback.y), knockbackLockTime);

        if (Current <= 0) Die();
        return true;
    }

    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        Current = Mathf.Min(maxHealth, Current + amount);
        HealthChanged?.Invoke(Current, maxHealth);
    }

    // ---------------------------------------------------------------- Mati
    void Die()
    {
        IsDead = true;
        movement.SetControlEnabled(false);
        if (staff != null) staff.enabled = false;
        Died?.Invoke();
        Invoke(nameof(Respawn), respawnDelay);
    }

    void Respawn()
    {
        if (reloadSceneOnDeath)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        transform.position = spawnPoint;
        rb.linearVelocity = Vector2.zero;
        Current = maxHealth;
        IsDead = false;
        invincibleTimer = invincibleTime;

        movement.SetControlEnabled(true);
        if (staff != null) staff.enabled = true;
        HealthChanged?.Invoke(Current, maxHealth);
    }

    // ---------------------------------------------------------------- Visual
    void UpdateBlink()
    {
        if (renderers == null) return;

        float alpha = 1f;
        if (IsDead) alpha = 0.4f;
        else if (invincibleTimer > 0f)
            alpha = (Mathf.FloorToInt(Time.time / blinkInterval) % 2 == 0) ? blinkAlpha : 1f;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            Color c = baseColors[i];
            c.a = baseColors[i].a * alpha;   // ubah alpha saja, jadi sprite yang sengaja disembunyikan tetap aman
            renderers[i].color = c;
        }
    }
}