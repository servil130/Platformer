using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Gerak")]
    [SerializeField] private float moveSpeed = 7f;
    [SerializeField] private float acceleration = 60f;
    [SerializeField] private float deceleration = 70f;

    [Header("Lompat")]
    [SerializeField] private float jumpForce = 12f;
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private float jumpBufferTime = 0.1f;
    [SerializeField] private float jumpCutMultiplier = 0.5f;
    [SerializeField] private float fallGravityMultiplier = 1.8f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;          // objek kosong di bawah kaki
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.4f, 0.08f);
    [SerializeField] private LayerMask groundLayer;          // set ke layer Platform

    [Header("Visual (opsional)")]
    [SerializeField] private SpriteRenderer sprite;
    [SerializeField] private Animator animator;              // parameter: Speed (float), Grounded (bool)

    private Rigidbody2D rb;
    private float moveInput;
    private float coyoteCounter;
    private float bufferCounter;
    private float defaultGravity;
    private bool isGrounded;
    private bool wasGrounded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        defaultGravity = rb.gravityScale;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
    }

    void Update()
    {
        ReadInput();

        isGrounded = Physics2D.OverlapBox(groundCheck.position, groundCheckSize, 0f, groundLayer);

        // Coyote time & jump buffer
        coyoteCounter = isGrounded ? coyoteTime : coyoteCounter - Time.deltaTime;
        bufferCounter -= Time.deltaTime;

        if (bufferCounter > 0f && coyoteCounter > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            bufferCounter = 0f;
            coyoteCounter = 0f;
        }

        // Lompat variabel: lepas tombol = lompatan lebih pendek
        if (JumpReleased() && rb.linearVelocity.y > 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);

        // Flip sprite
        if (sprite != null && moveInput != 0f)
            sprite.flipX = moveInput < 0f;

        // Animasi
        if (animator != null)
        {
            animator.SetFloat("Speed", Mathf.Abs(rb.linearVelocity.x));
            animator.SetBool("Grounded", isGrounded);
        }

        // Titik untuk efek mendarat (debu, suara) nanti
        if (isGrounded && !wasGrounded)
            OnLanded();
        wasGrounded = isGrounded;
    }

    void FixedUpdate()
    {
        // Akselerasi halus, bukan langsung berhenti
        float targetSpeed = moveInput * moveSpeed;
        float rate = Mathf.Abs(targetSpeed) > 0.01f ? acceleration : deceleration;
        float newX = Mathf.MoveTowards(rb.linearVelocity.x, targetSpeed, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);

        // Jatuh lebih berat supaya lompatan terasa tegas
        rb.gravityScale = rb.linearVelocity.y < 0f ? defaultGravity * fallGravityMultiplier : defaultGravity;
    }

    void ReadInput()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        moveInput = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) moveInput -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) moveInput += 1f;

        if (kb.spaceKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)
            bufferCounter = jumpBufferTime;
    }

    bool JumpReleased()
    {
        var kb = Keyboard.current;
        return kb != null &&
               (kb.spaceKey.wasReleasedThisFrame || kb.wKey.wasReleasedThisFrame || kb.upArrowKey.wasReleasedThisFrame);
    }

    void OnLanded()
    {
        // Tambahkan dust particle / suara mendarat di sini
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(groundCheck.position, groundCheckSize);
    }
}