using UnityEngine;
using UnityEngine.InputSystem;


[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Kecepatan")]
    [SerializeField] private float moveSpeed = 7f;

    [Header("Akselerasi & Deselerasi (darat)")]
    [SerializeField] private float groundAcceleration = 55f;
    [SerializeField] private float groundDeceleration = 45f;
    [SerializeField] private float turnAcceleration = 90f;

    [Header("Kontrol di udara")]
    [SerializeField] private float airAcceleration = 35f;
    [SerializeField] private float airDeceleration = 20f;

    [Header("Lompat")]
    [SerializeField] private float jumpForce = 12f;
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private float jumpBufferTime = 0.1f;
    [SerializeField] private float jumpCutMultiplier = 0.5f;
    [SerializeField] private float fallGravityMultiplier = 1.8f;
    [SerializeField] private float maxFallSpeed = 20f;

    [Header("Hang time di puncak lompatan")]
    [SerializeField] private float apexThreshold = 1.5f;
    [SerializeField] private float apexGravityMultiplier = 0.6f;

    [Header("Ground check (otomatis dari collider)")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckDistance = 0.05f;

    [Header("Posisi aman (untuk kembali setelah jatuh)")]
    [SerializeField] private float safeEdgeMargin = 0.4f;      // posisi aman harus sejauh ini dari tepi jurang

    [Header("Dash")]
    [SerializeField] private bool dashEnabled = true;
    [SerializeField] private float dashSpeed = 18f;
    [SerializeField] private float dashDuration = 0.16f;
    [SerializeField] private float dashCooldown = 0.45f;       // dihitung sejak dash dimulai
    [SerializeField] private int airDashes = 1;                // jumlah dash di udara sebelum menyentuh tanah lagi
    [SerializeField] private float dashBufferTime = 0.1f;
    [SerializeField] private float dashEndSpeedFactor = 0.5f;  // sisa kecepatan setelah dash (x moveSpeed)
    [SerializeField] private Vector2 dashStretchScale = new Vector2(1.3f, 0.8f);

    [Header("Dash - bayangan (afterimage)")]
    [SerializeField] private bool useAfterimage = true;
    [SerializeField] private float afterimageInterval = 0.03f;
    [SerializeField] private float afterimageLife = 0.25f;
    [SerializeField] private Color afterimageColor = new Color(0.5f, 0.8f, 1f, 0.6f);

    [Header("Visual")]
    [SerializeField] private SpriteRenderer sprite;
    [SerializeField] private bool spriteFacesRight = true;     // matikan kalau gambar aslinya menghadap kiri
    [SerializeField] private Animator animator;               // opsional: IsMoving (bool), Grounded (bool), Speed (float), VelocityY (float)

    [Header("Efek debu (dibuat otomatis)")]
    [SerializeField] private bool useDust = true;
    [SerializeField] private Color dustColor = new Color(0.85f, 0.8f, 0.7f, 0.8f);
    [SerializeField] private float dustMinSpeed = 3f;

    [Header("Efek lompat")]
    [SerializeField] private AudioClip jumpSound;             
    [SerializeField, Range(0f, 1f)] private float jumpVolume = 0.6f;
    [SerializeField] private int jumpDustCount = 10;

    [Header("Squash & Stretch")]
    [SerializeField] private bool useSquashStretch = true;
    [SerializeField] private Vector2 jumpStretch = new Vector2(0.85f, 1.2f);
    [SerializeField] private Vector2 landSquash = new Vector2(1.3f, 0.7f);
    [SerializeField] private float squashRecoverSpeed = 12f;

    private Rigidbody2D rb;
    private Collider2D col;
    private ContactFilter2D groundFilter;
    private readonly RaycastHit2D[] hitBuffer = new RaycastHit2D[4];

    private float moveInput;
    private bool jumpHeld;
    private bool jumpCutRequested;
    private float coyoteCounter;
    private float bufferCounter;
    private float defaultGravity;
    private float peakFallSpeed;
    private float nextTurnDustTime;
    private bool isGrounded;
    private bool wasGrounded;
    private bool controlEnabled = true;   // dipakai PlayerHealth (mati)
    private float controlLockTimer;       // dipakai PlayerHealth (knockback)

    private bool isDashing;
    private float dashTimer;
    private float dashCooldownTimer;
    private float dashBuffer;
    private float nextGhostTime;
    private int dashDir = 1;
    private int airDashesLeft;
    private int facing = 1;               // 1 = kanan, -1 = kiri

    private float groundedTime;
    private bool actionLocked;            // dipakai PlayerParry (tidak bisa jalan/lompat/dash)
    /// <summary>Posisi terakhir yang aman (berdiri mantap di tanah, jauh dari tepi). Dipakai PlayerHealth saat jatuh.</summary>
    public Vector2 SafePosition { get; private set; }

    private SpriteRenderer activeSprite;
    private Transform visual;
    private Vector3 visualBasePos;
    private float spriteBottomLocal;

    private System.Collections.Generic.HashSet<string> animParams;
    private ParticleSystem runDust;
    private ParticleSystem landDust;
    private ParticleSystem jumpDust;
    private AudioSource audioSource;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        defaultGravity = rb.gravityScale;
        SafePosition = transform.position;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // Tanpa friksi supaya tidak menempel di dinding saat lompat
        if (col.sharedMaterial == null)
            col.sharedMaterial = new PhysicsMaterial2D("Player_NoFriction") { friction = 0f, bounciness = 0f };

        // Ground check: hanya permukaan yang menghadap ke atas
        LayerMask mask = groundLayer;
        if (mask.value == 0) mask = Physics2D.AllLayers;
        groundFilter = new ContactFilter2D { useTriggers = false };
        groundFilter.SetLayerMask(mask);
        groundFilter.SetNormalAngle(45f, 135f);

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f; // 2D

        SetupVisual();
        CacheAnimParams();
        if (useDust) SetupDust();
    }

    void Start()
    {
        if (runDust != null) runDust.Play();
        if (landDust != null) landDust.Play();
        if (jumpDust != null) jumpDust.Play();
    }

    // ---------------------------------------------------------------- Update (input & visual)
    void Update()
    {
        if (controlEnabled && !actionLocked) ReadInput();
        else { moveInput = 0f; jumpHeld = false; dashBuffer = 0f; }

        if (moveInput != 0f) facing = moveInput > 0f ? 1 : -1;
        if (activeSprite != null)
            activeSprite.flipX = spriteFacesRight ? facing < 0 : facing > 0;

        if (isDashing && useAfterimage && Time.time >= nextGhostTime)
        {
            SpawnAfterimage();
            nextGhostTime = Time.time + afterimageInterval;
        }

        UpdateDust();
        UpdateSquash();

        SetAnimFloat("Speed", Mathf.Abs(rb.linearVelocity.x));
        SetAnimFloat("VelocityY", rb.linearVelocity.y);
        SetAnimBool("Grounded", isGrounded);
        SetAnimBool("IsMoving", isGrounded && !isDashing && Mathf.Abs(rb.linearVelocity.x) > 0.1f);
        SetAnimBool("IsDashing", isDashing);
    }

    // Animator menganimasikan SpriteRenderer asli; salin sprite-nya ke "Visual" supaya animasi tetap terlihat
    void LateUpdate()
    {
        if (sprite != null && activeSprite != null && activeSprite != sprite)
            activeSprite.sprite = sprite.sprite;
    }

    // Parameter yang tidak ada di controller diabaikan (tanpa warning)
    void CacheAnimParams()
    {
        animParams = new System.Collections.Generic.HashSet<string>();
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (var p in animator.parameters) animParams.Add(p.name);
    }

    void SetAnimFloat(string n, float v) { if (animParams.Contains(n)) animator.SetFloat(n, v); }
    void SetAnimBool(string n, bool v) { if (animParams.Contains(n)) animator.SetBool(n, v); }

    // ---------------------------------------------------------------- FixedUpdate (fisika)
    void FixedUpdate()
    {
        CheckGround();

        if (!isGrounded) peakFallSpeed = Mathf.Min(peakFallSpeed, rb.linearVelocity.y);
        if (isGrounded && !wasGrounded) OnLanded();
        wasGrounded = isGrounded;

        float dt = Time.fixedDeltaTime;
        controlLockTimer -= dt;
        coyoteCounter = isGrounded ? coyoteTime : coyoteCounter - dt;
        bufferCounter -= dt;
        dashCooldownTimer -= dt;
        dashBuffer -= dt;
        if (isGrounded && !isDashing) airDashesLeft = airDashes;

        // Catat posisi aman: sudah berdiri sebentar dan ada tanah di kiri-kanan (bukan di tepi jurang)
        groundedTime = isGrounded ? groundedTime + dt : 0f;
        if (isGrounded && !isDashing && groundedTime > 0.1f && HasSolidGroundAround())
            SafePosition = rb.position;

        if (isDashing)
        {
            dashTimer -= dt;
            if (dashTimer <= 0f)
            {
                EndDash();
            }
            else
            {
                rb.gravityScale = 0f;
                rb.linearVelocity = new Vector2(dashDir * dashSpeed, 0f);
                return;                                   // selama dash, gerak normal dimatikan
            }
        }
        else if (dashEnabled && dashBuffer > 0f && dashCooldownTimer <= 0f
                 && controlEnabled && controlLockTimer <= 0f && (isGrounded || airDashesLeft > 0))
        {
            StartDash();
            rb.gravityScale = 0f;
            rb.linearVelocity = new Vector2(dashDir * dashSpeed, 0f);
            return;
        }

        if (bufferCounter > 0f && coyoteCounter > 0f)
            Jump();

        // Lompat variabel: lepas tombol = lompatan lebih pendek
        if (jumpCutRequested)
        {
            if (rb.linearVelocity.y > 0f)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
            jumpCutRequested = false;
        }

        // Gerak horizontal dengan akselerasi / deselerasi
        float vx = rb.linearVelocity.x;
        float targetSpeed = moveInput * moveSpeed;
        bool hasInput = Mathf.Abs(moveInput) > 0.01f;
        bool turning = hasInput && Mathf.Abs(vx) > 0.1f && Mathf.Sign(moveInput) != Mathf.Sign(vx);

        float rate;
        if (isGrounded)
            rate = !hasInput ? groundDeceleration : (turning ? turnAcceleration : groundAcceleration);
        else
            rate = hasInput ? airAcceleration : airDeceleration;

        float newX = controlLockTimer > 0f ? vx : Mathf.MoveTowards(vx, targetSpeed, rate * dt);

        // Gravitasi: ringan di puncak (hang time), berat saat jatuh
        float vy = rb.linearVelocity.y;
        float g = defaultGravity;
        if (!isGrounded && jumpHeld && Mathf.Abs(vy) < apexThreshold) g *= apexGravityMultiplier;
        else if (vy < 0f) g *= fallGravityMultiplier;
        rb.gravityScale = g;

        rb.linearVelocity = new Vector2(newX, Mathf.Max(vy, -maxFallSpeed));
    }

    // ---------------------------------------------------------------- Posisi aman & teleport
    bool HasSolidGroundAround()
    {
        Bounds b = col.bounds;
        float reach = b.extents.x + safeEdgeMargin;
        return SolidGroundAt(b.center.x - reach, b.min.y) && SolidGroundAt(b.center.x + reach, b.min.y);
    }

    bool SolidGroundAt(float x, float feetY)
    {
        int n = Physics2D.Raycast(new Vector2(x, feetY + 0.1f), Vector2.down, groundFilter, hitBuffer, 0.4f);
        for (int i = 0; i < n; i++)
        {
            var c = hitBuffer[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<Enemy>() != null) continue;     // berdiri di atas musuh bukan tempat aman
            return true;
        }
        return false;
    }

    /// <summary>Pindahkan player (mis. setelah jatuh ke jurang) dan reset kecepatan & efek.</summary>
    public void TeleportTo(Vector2 position)
    {
        isDashing = false;
        rb.position = position;
        transform.position = position;
        rb.linearVelocity = Vector2.zero;
        peakFallSpeed = 0f;                 // supaya tidak ada efek mendarat besar
        coyoteCounter = 0f;
        bufferCounter = 0f;
        jumpCutRequested = false;
        controlLockTimer = 0.2f;            // diam sebentar supaya jelas player kembali
    }

    // ---------------------------------------------------------------- Dash
    void StartDash()
    {
        isDashing = true;
        dashTimer = dashDuration;
        dashCooldownTimer = dashCooldown;
        dashBuffer = 0f;
        jumpCutRequested = false;
        nextGhostTime = 0f;

        dashDir = Mathf.Abs(moveInput) > 0.01f ? (moveInput > 0f ? 1 : -1) : facing;
        facing = dashDir;
        if (!isGrounded) airDashesLeft--;

        if (visual != null && useSquashStretch)
            visual.localScale = new Vector3(dashStretchScale.x, dashStretchScale.y, 1f);
        if (landDust != null && isGrounded) landDust.Emit(8);
    }

    void EndDash()
    {
        isDashing = false;
        bool holdingSameDir = Mathf.Abs(moveInput) > 0.01f && (moveInput > 0f ? 1 : -1) == dashDir;
        float endSpeed = holdingSameDir ? moveSpeed : moveSpeed * dashEndSpeedFactor;
        rb.linearVelocity = new Vector2(dashDir * endSpeed, 0f);
    }

    // Salinan sprite (frame animasi saat ini) yang memudar, membentuk jejak di belakang player
    void SpawnAfterimage()
    {
        if (activeSprite == null || activeSprite.sprite == null) return;
        var go = new GameObject("Afterimage");
        go.transform.SetPositionAndRotation(activeSprite.transform.position, activeSprite.transform.rotation);
        go.transform.localScale = activeSprite.transform.lossyScale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = activeSprite.sprite;
        sr.flipX = activeSprite.flipX;
        sr.flipY = activeSprite.flipY;
        sr.sortingLayerID = activeSprite.sortingLayerID;
        sr.sortingOrder = activeSprite.sortingOrder - 1;
        sr.color = afterimageColor;
        go.AddComponent<AfterimageFade>().Init(sr, afterimageLife);
    }

    // ---------------------------------------------------------------- API untuk PlayerHealth
    /// <summary>Dorong player dan kunci kontrol horizontal sebentar.</summary>
    public void ApplyKnockback(Vector2 velocity, float lockTime)
    {
        rb.linearVelocity = velocity;
        controlLockTimer = lockTime;
        isDashing = false;
        bufferCounter = 0f;
        jumpCutRequested = false;
    }

    public bool IsDashing => isDashing;

    /// <summary>Kunci input gerak/lompat/dash (mis. selama parry). Player berhenti dengan deselerasi biasa.</summary>
    public void SetActionLock(bool locked)
    {
        actionLocked = locked;
        if (locked) { moveInput = 0f; jumpHeld = false; dashBuffer = 0f; }
    }

    public void SetControlEnabled(bool enabled)
    {
        controlEnabled = enabled;
        if (!enabled)
        {
            moveInput = 0f;
            jumpHeld = false;
            bufferCounter = 0f;
        }
    }

    // ---------------------------------------------------------------- Logika
    void CheckGround()
    {
        isGrounded = col.Cast(Vector2.down, groundFilter, hitBuffer, groundCheckDistance) > 0;
    }

    void Jump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        bufferCounter = 0f;
        coyoteCounter = 0f;

        if (useSquashStretch && visual != null)
            visual.localScale = new Vector3(jumpStretch.x, jumpStretch.y, 1f);
        if (jumpDust != null) jumpDust.Emit(jumpDustCount);
        if (jumpSound != null) audioSource.PlayOneShot(jumpSound, jumpVolume);
    }

    void OnLanded()
    {
        float t = Mathf.Clamp01(-peakFallSpeed / maxFallSpeed);
        peakFallSpeed = 0f;
        if (t < 0.15f) return; // jatuh pendek: tanpa efek

        if (useSquashStretch && visual != null)
            visual.localScale = new Vector3(Mathf.Lerp(1f, landSquash.x, t), Mathf.Lerp(1f, landSquash.y, t), 1f);
        if (landDust != null) landDust.Emit(Mathf.RoundToInt(Mathf.Lerp(4f, 12f, t)));
    }

    void ReadInput()
    {
        float x = 0f;
        bool pressed = false, held = false, released = false, dash = false;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            pressed |= kb.spaceKey.wasPressedThisFrame;
            held |= kb.spaceKey.isPressed;
            released |= kb.spaceKey.wasReleasedThisFrame;
            dash |= kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame;
        }

        var gp = Gamepad.current;
        if (gp != null)
        {
            float sx = gp.leftStick.x.ReadValue();
            if (Mathf.Abs(sx) > 0.2f) x += sx;
            pressed |= gp.buttonSouth.wasPressedThisFrame;
            held |= gp.buttonSouth.isPressed;
            released |= gp.buttonSouth.wasReleasedThisFrame;
            dash |= gp.buttonEast.wasPressedThisFrame;
        }

        moveInput = Mathf.Clamp(x, -1f, 1f);
        jumpHeld = held;
        if (pressed) bufferCounter = jumpBufferTime;
        if (released) jumpCutRequested = true;
        if (dash) dashBuffer = dashBufferTime;
    }

    // ---------------------------------------------------------------- Visual & efek
    void SetupVisual()
    {
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        activeSprite = sprite;
        if (sprite == null) return;

        // Sprite ada di objek yang sama dengan collider/rigidbody: skala objek ini akan merusak fisika.
        // Solusi: buat anak "Visual" dan pindahkan tampilan sprite ke sana.
        if (useSquashStretch && sprite.gameObject == gameObject)
        {
            var go = new GameObject("Visual");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite.sprite;
            sr.color = sprite.color;
            sr.sharedMaterial = sprite.sharedMaterial;
            sr.sortingLayerID = sprite.sortingLayerID;
            sr.sortingOrder = sprite.sortingOrder;
            sr.flipX = sprite.flipX;
            sr.flipY = sprite.flipY;
            sprite.enabled = false;
            activeSprite = sr;
        }

        if (activeSprite.sprite != null) spriteBottomLocal = activeSprite.sprite.bounds.min.y;

        if (activeSprite.transform != transform)
        {
            visual = activeSprite.transform;
            visualBasePos = visual.localPosition;
        }
    }

    void UpdateSquash()
    {
        if (!useSquashStretch || visual == null) return;
        visual.localScale = Vector3.Lerp(visual.localScale, Vector3.one, squashRecoverSpeed * Time.deltaTime);
        // Jaga telapak kaki tetap di tanah saat sprite di-squash
        visual.localPosition = visualBasePos + Vector3.up * (spriteBottomLocal * (1f - visual.localScale.y));
    }

    void UpdateDust()
    {
        if (runDust == null) return;
        float vx = rb.linearVelocity.x;

        var emission = runDust.emission;
        emission.enabled = isGrounded && Mathf.Abs(vx) > dustMinSpeed;

        // Semburan debu saat berbalik arah mendadak
        bool turning = moveInput != 0f && Mathf.Abs(vx) > moveSpeed * 0.6f && Mathf.Sign(moveInput) != Mathf.Sign(vx);
        if (isGrounded && turning && Time.time >= nextTurnDustTime)
        {
            runDust.Emit(5);
            nextTurnDustTime = Time.time + 0.15f;
        }
    }

    void SetupDust()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) return;
        var material = new Material(shader);

        float feetY = col.bounds.min.y - transform.position.y;
        runDust = CreateDust("RunDust", feetY, material, 0.35f, 0.12f, 1.2f, 35f, 18f);
        landDust = CreateDust("LandDust", feetY, material, 0.4f, 0.16f, 2.5f, 70f, 0f);
        // Sudut 85° = debu menyebar hampir datar ke kiri-kanan dari telapak kaki
        jumpDust = CreateDust("JumpDust", feetY, material, 0.35f, 0.14f, 2.8f, 85f, 0f);
    }

    ParticleSystem CreateDust(string objName, float feetY, Material material,
                              float lifetime, float size, float speed, float coneAngle, float rate)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, feetY, 0f);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // semburan ke atas

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // debu tertinggal di belakang
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startColor = dustColor;
        main.maxParticles = 100;

        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle;
        shape.radius = 0.1f;

        var colorModule = ps.colorOverLifetime;
        colorModule.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorModule.color = gradient;

        var sizeModule = ps.sizeOverLifetime;
        sizeModule.enabled = true;
        sizeModule.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = material;
        if (activeSprite != null)
        {
            psr.sortingLayerID = activeSprite.sortingLayerID;
            psr.sortingOrder = activeSprite.sortingOrder - 1;
        }
        return ps;
    }

    void OnDrawGizmosSelected()
    {
        var c = GetComponent<Collider2D>();
        if (c == null) return;
        Gizmos.color = Color.green;
        Vector3 a = new Vector3(c.bounds.min.x, c.bounds.min.y - groundCheckDistance, 0f);
        Vector3 b = new Vector3(c.bounds.max.x, c.bounds.min.y - groundCheckDistance, 0f);
        Gizmos.DrawLine(a, b);
    }
}

/// <summary>Sprite jejak dash: memudar lalu hilang sendiri.</summary>
public class AfterimageFade : MonoBehaviour
{
    private SpriteRenderer sr;
    private Color startColor;
    private float life, elapsed;

    public void Init(SpriteRenderer renderer, float lifetime)
    {
        sr = renderer;
        startColor = renderer.color;
        life = Mathf.Max(0.05f, lifetime);
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float k = elapsed / life;
        if (sr == null || k >= 1f) { Destroy(gameObject); return; }
        Color c = startColor;
        c.a = startColor.a * (1f - k);
        sr.color = c;
    }
}