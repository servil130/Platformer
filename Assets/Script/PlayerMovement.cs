using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gerakan player 2D platformer (Unity 6). Tanpa setup tambahan:
/// ground check, material tanpa friksi, efek debu, dan squash & stretch dibuat otomatis.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Kecepatan")]
    [SerializeField] private float moveSpeed = 7f;

    [Header("Akselerasi & Deselerasi (darat)")]
    [SerializeField] private float groundAcceleration = 55f;   // naik = lebih cepat mencapai moveSpeed
    [SerializeField] private float groundDeceleration = 45f;   // naik = lebih cepat berhenti (turun = licin)
    [SerializeField] private float turnAcceleration = 90f;     // saat berbalik arah

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
    [SerializeField] private float apexThreshold = 1.5f;       // |kecepatan Y| di bawah ini dianggap puncak
    [SerializeField] private float apexGravityMultiplier = 0.6f;

    [Header("Ground check (otomatis dari collider)")]
    [SerializeField] private LayerMask groundLayer;            // kosong (Nothing) = semua layer
    [SerializeField] private float groundCheckDistance = 0.05f;

    [Header("Visual")]
    [SerializeField] private SpriteRenderer sprite;
    [SerializeField] private Animator animator;                // opsional: parameter Speed (float), Grounded (bool)

    [Header("Efek debu (dibuat otomatis)")]
    [SerializeField] private bool useDust = true;
    [SerializeField] private Color dustColor = new Color(0.85f, 0.8f, 0.7f, 0.8f);
    [SerializeField] private float dustMinSpeed = 3f;

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

    private SpriteRenderer activeSprite;
    private Transform visual;
    private Vector3 visualBasePos;
    private float spriteBottomLocal;

    private ParticleSystem runDust;
    private ParticleSystem landDust;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        defaultGravity = rb.gravityScale;
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

        SetupVisual();
        if (useDust) SetupDust();
    }

    void Start()
    {
        if (runDust != null) runDust.Play();
        if (landDust != null) landDust.Play();
    }

    // ---------------------------------------------------------------- Update (input & visual)
    void Update()
    {
        ReadInput();

        if (activeSprite != null && moveInput != 0f)
            activeSprite.flipX = moveInput < 0f;

        UpdateDust();
        UpdateSquash();

        if (animator != null)
        {
            animator.SetFloat("Speed", Mathf.Abs(rb.linearVelocity.x));
            animator.SetBool("Grounded", isGrounded);
        }
    }

    // ---------------------------------------------------------------- FixedUpdate (fisika)
    void FixedUpdate()
    {
        CheckGround();

        if (!isGrounded) peakFallSpeed = Mathf.Min(peakFallSpeed, rb.linearVelocity.y);
        if (isGrounded && !wasGrounded) OnLanded();
        wasGrounded = isGrounded;

        float dt = Time.fixedDeltaTime;
        coyoteCounter = isGrounded ? coyoteTime : coyoteCounter - dt;
        bufferCounter -= dt;

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

        float newX = Mathf.MoveTowards(vx, targetSpeed, rate * dt);

        // Gravitasi: ringan di puncak (hang time), berat saat jatuh
        float vy = rb.linearVelocity.y;
        float g = defaultGravity;
        if (!isGrounded && jumpHeld && Mathf.Abs(vy) < apexThreshold) g *= apexGravityMultiplier;
        else if (vy < 0f) g *= fallGravityMultiplier;
        rb.gravityScale = g;

        rb.linearVelocity = new Vector2(newX, Mathf.Max(vy, -maxFallSpeed));
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
        if (landDust != null) landDust.Emit(6);
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
        bool pressed = false, held = false, released = false;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            pressed |= kb.spaceKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            held |= kb.spaceKey.isPressed || kb.wKey.isPressed || kb.upArrowKey.isPressed;
            released |= kb.spaceKey.wasReleasedThisFrame || kb.wKey.wasReleasedThisFrame || kb.upArrowKey.wasReleasedThisFrame;
        }

        var gp = Gamepad.current;
        if (gp != null)
        {
            float sx = gp.leftStick.x.ReadValue();
            if (Mathf.Abs(sx) > 0.2f) x += sx;
            pressed |= gp.buttonSouth.wasPressedThisFrame;
            held |= gp.buttonSouth.isPressed;
            released |= gp.buttonSouth.wasReleasedThisFrame;
        }

        moveInput = Mathf.Clamp(x, -1f, 1f);
        jumpHeld = held;
        if (pressed) bufferCounter = jumpBufferTime;
        if (released) jumpCutRequested = true;
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