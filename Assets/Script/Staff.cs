using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Staff yang mengikuti arah mouse dan menembakkan projektil (klik kiri).
/// Pasang di objek "Staff" (anak dari Player). Saat menembak, staff berhenti mengikuti mouse
/// sebentar (shootPause) sambil kena recoil + muzzle flash, lalu lanjut mengikuti mouse lagi.
/// </summary>
public class Staff : MonoBehaviour
{
    [Header("Ikut mouse")]
    [SerializeField] private float orbitRadius = 0.6f;          // jarak staff dari tengah player
    [SerializeField] private Vector2 pivotOffset = Vector2.zero; // geser titik putar (mis. ke tinggi tangan)
    [SerializeField] private float followSpeed = 25f;           // naik = lebih cepat/kaku ikut mouse
    [SerializeField] private float spriteAngleOffset = 0f;      // 0 = sprite menghadap kanan. Kalau miring 45° ke kanan-atas, isi -45
    [SerializeField] private bool flipWhenAimingLeft = true;    // cegah sprite terbalik saat membidik ke kiri

    [Header("Tembak")]
    [SerializeField] private Projectile projectilePrefab;       // opsional; kosong = projektil dibuat otomatis
    [SerializeField] private bool holdToFire = true;            // tahan klik kiri = tembak terus
    [SerializeField] private float fireCooldown = 0.3f;
    [SerializeField] private float shootPause = 0.15f;          // lama staff berhenti ikut mouse setelah menembak
    [SerializeField] private float muzzleDistance = 0.5f;       // jarak ujung staff dari pusat staff
    [SerializeField] private float projectileSpeed = 14f;
    [SerializeField] private int projectileDamage = 1;          // damage per tembakan ke musuh
    [SerializeField] private float projectileLifetime = 2f;
    [SerializeField] private Color projectileColor = new Color(0.45f, 0.85f, 1f, 1f);
    [SerializeField] private AudioClip shootSound;              // mis. tap.wav / power_up.wav
    [SerializeField, Range(0f, 1f)] private float shootVolume = 0.6f;

    [Header("Efek tembak")]
    [SerializeField] private float recoilDistance = 0.2f;       // staff terdorong ke belakang
    [SerializeField] private float punchScale = 1.3f;           // staff membesar sesaat
    [SerializeField] private float flashSize = 0.7f;
    [SerializeField] private float flashDuration = 0.08f;
    [SerializeField] private int sparkCount = 8;

    private Transform pivotTransform;
    private SpriteRenderer sr;
    private AudioSource audioSource;
    private Camera cam;
    private Vector3 baseScale;

    private float aimAngle;
    private float targetAngle;
    private float pauseTimer;
    private float cooldownTimer;

    private SpriteRenderer flash;
    private float flashTimer;
    private ParticleSystem sparks;

    private Vector2 Pivot
    {
        get
        {
            Vector2 p = pivotTransform != null ? (Vector2)pivotTransform.position : (Vector2)transform.position;
            return p + pivotOffset;
        }
    }

    void Awake()
    {
        pivotTransform = transform.parent;
        sr = GetComponentInChildren<SpriteRenderer>();
        baseScale = transform.localScale;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        // Mulai dari arah staff sekarang (supaya tidak "loncat" di frame pertama)
        Vector2 offset = (Vector2)transform.position - Pivot;
        aimAngle = offset.sqrMagnitude > 0.0001f ? Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg : 0f;
        targetAngle = aimAngle;

        SetupFx();
    }

    void OnDestroy()
    {
        if (flash != null) Destroy(flash.gameObject);
        if (sparks != null) Destroy(sparks.gameObject);
    }

    // ---------------------------------------------------------------- Input & logika
    void Update()
    {
        float dt = Time.deltaTime;
        cooldownTimer -= dt;
        pauseTimer -= dt;

        if (TryGetMouseWorld(out Vector2 mouseWorld))
        {
            Vector2 toMouse = mouseWorld - Pivot;
            if (toMouse.sqrMagnitude > 0.0001f)
                targetAngle = Mathf.Atan2(toMouse.y, toMouse.x) * Mathf.Rad2Deg;

            // Selama jeda tembak, arah dikunci. Setelah itu lanjut ikut mouse dengan halus.
            if (pauseTimer <= 0f)
                aimAngle = Mathf.LerpAngle(aimAngle, targetAngle, 1f - Mathf.Exp(-followSpeed * dt));

            var mouse = Mouse.current;
            bool wantsFire = mouse.leftButton.wasPressedThisFrame || (holdToFire && mouse.leftButton.isPressed);
            if (wantsFire && cooldownTimer <= 0f)
                Shoot();
        }

        UpdateFlash(dt);
    }

    bool TryGetMouseWorld(out Vector2 world)
    {
        world = default;
        if (Mouse.current == null) return false;
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        Vector3 screen = Mouse.current.position.ReadValue();
        screen.z = Mathf.Abs(cam.transform.position.z - transform.position.z);
        world = cam.ScreenToWorldPoint(screen);
        return true;
    }

    void Shoot()
    {
        // Bidik tepat ke mouse saat klik, lalu kunci arah selama jeda
        aimAngle = targetAngle;
        pauseTimer = shootPause;
        cooldownTimer = Mathf.Max(fireCooldown, shootPause);

        Vector2 dir = AngleToDir(aimAngle);
        Vector2 muzzle = Pivot + dir * (orbitRadius + muzzleDistance);

        int layerId = sr != null ? sr.sortingLayerID : 0;
        int order = sr != null ? sr.sortingOrder : 1;

        // Projektil
        Projectile p = projectilePrefab != null
            ? Instantiate(projectilePrefab, muzzle, Quaternion.identity)
            : Projectile.CreateDefault(projectileColor);
        p.transform.position = muzzle;
        p.Damage = projectileDamage;
        p.Launch(dir, projectileSpeed, projectileLifetime, transform.root, projectileColor, layerId, order);

        // Muzzle flash
        if (flash != null)
        {
            flash.transform.position = new Vector3(muzzle.x, muzzle.y, transform.position.z);
            flash.color = Color.Lerp(projectileColor, Color.white, 0.6f); // reset alpha
            flash.enabled = true;
            flashTimer = flashDuration;
        }

        // Percikan searah tembakan
        if (sparks != null)
        {
            sparks.transform.SetPositionAndRotation(
                new Vector3(muzzle.x, muzzle.y, transform.position.z),
                Quaternion.LookRotation(new Vector3(dir.x, dir.y, 0f), Vector3.forward));
            sparks.Emit(sparkCount);
        }

        // Suara
        if (shootSound != null)
        {
            audioSource.pitch = Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(shootSound, shootVolume);
        }
    }

    // ---------------------------------------------------------------- Pose staff
    void LateUpdate()
    {
        Vector2 pivot = Pivot;
        Vector2 dir = AngleToDir(aimAngle);

        // t = 1 tepat saat menembak, turun ke 0 saat jeda selesai
        float t = shootPause > 0f ? Mathf.Clamp01(pauseTimer / shootPause) : 0f;
        float recoil = recoilDistance * t * t;
        float radius = orbitRadius - recoil;

        transform.position = new Vector3(pivot.x + dir.x * radius, pivot.y + dir.y * radius, transform.position.z);

        bool aimingLeft = flipWhenAimingLeft && dir.x < 0f;
        if (sr != null) sr.flipY = aimingLeft;
        float offset = aimingLeft ? -spriteAngleOffset : spriteAngleOffset;
        transform.rotation = Quaternion.Euler(0f, 0f, aimAngle + offset);

        transform.localScale = baseScale * (1f + (punchScale - 1f) * t);
    }

    static Vector2 AngleToDir(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
    }

    // ---------------------------------------------------------------- Efek
    void SetupFx()
    {
        int layerId = sr != null ? sr.sortingLayerID : 0;
        int order = sr != null ? sr.sortingOrder : 1;

        var flashGo = new GameObject("MuzzleFlash");
        flash = flashGo.AddComponent<SpriteRenderer>();
        flash.sprite = ShotFx.Circle();
        flash.color = Color.Lerp(projectileColor, Color.white, 0.6f);
        flash.sortingLayerID = layerId;
        flash.sortingOrder = order + 1;
        flash.enabled = false;

        sparks = ShotFx.CreateBurst("MuzzleSparks", projectileColor, 0.25f, 0.08f, 5f,
                                    ParticleSystemShapeType.Cone, 25f, layerId, order + 1);
    }

    void UpdateFlash(float dt)
    {
        if (flash == null || !flash.enabled) return;

        flashTimer -= dt;
        if (flashTimer <= 0f)
        {
            flash.enabled = false;
            return;
        }

        float t = flashTimer / flashDuration;                       // 1 -> 0
        flash.transform.localScale = Vector3.one * (flashSize * (0.4f + 0.6f * t));
        Color c = flash.color;
        c.a = t;
        flash.color = c;
    }
}