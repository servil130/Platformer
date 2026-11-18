using UnityEngine;

/// <summary>
/// Enemy slime: Sleep -> WakeUp -> Look -> Chase -> (player jauh) SleepDown -> Sleep.
/// Memakai parameter Animator:  Awake (bool)  dan  IsWalking (bool).
/// Pasang di objek Slime (butuh Rigidbody2D + Collider2D + Animator + SpriteRenderer).
/// Player dicari otomatis lewat komponen PlayerMovement (tidak perlu tag).
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class SlimeEnemy : MonoBehaviour
{
    private enum State { Sleep, WakeUp, Look, Chase, SleepDown }

    [Header("Deteksi")]
    [SerializeField] private float detectRange = 5f;       // player masuk jarak ini -> slime bangun
    [SerializeField] private float loseRange = 12f;        // player lebih jauh dari ini -> slime tidur lagi (0 = tidak pernah)
    [SerializeField] private float verticalTolerance = 3f; // beda tinggi maksimal agar player terdeteksi

    [Header("Perilaku")]
    [SerializeField] private float lookDuration = 0.6f;    // lama menoleh ke player sebelum mengejar
    [SerializeField] private float chaseSpeed = 3f;
    [SerializeField] private float chaseAcceleration = 20f;
    [SerializeField] private float stopDistance = 0.4f;    // berhenti kalau sudah sedekat ini (horizontal)
    [SerializeField] private bool stopAtLedge = true;      // jangan jatuh dari tepi platform
    [SerializeField] private LayerMask groundLayer;        // kosong = semua layer
    [SerializeField] private float ledgeCheckForward = 0.5f;
    [SerializeField] private float ledgeCheckDepth = 1f;

    [Header("Serangan")]
    [SerializeField] private int contactDamage = 1;        // damage saat slime (yang sudah bangun) menyentuh player

    [Header("Animator")]
    [SerializeField] private string awakeParam = "Awake";
    [SerializeField] private string walkingParam = "IsWalking";
    [SerializeField] private string wakeUpClip = "WakeUp";       // nama clip (untuk membaca durasi)
    [SerializeField] private string sleepDownClip = "SleepDown";
    [SerializeField] private float wakeUpDurationFallback = 1f;  // dipakai kalau clip tidak ketemu
    [SerializeField] private float sleepDownDurationFallback = 1f;

    [Header("Visual")]
    [SerializeField] private SpriteRenderer sprite;
    [SerializeField] private bool spriteFacesRight = true; // centang kalau sprite aslinya menghadap kanan

    private Rigidbody2D rb;
    private Collider2D col;
    private Animator animator;
    private Transform player;

    private int awakeHash;
    private int walkingHash;

    private State state = State.Sleep;
    private float stateTimer;
    private float wakeUpDuration;
    private float sleepDownDuration;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        animator = GetComponentInChildren<Animator>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();

        rb.freezeRotation = true;

        awakeHash = Animator.StringToHash(awakeParam);
        walkingHash = Animator.StringToHash(walkingParam);

        wakeUpDuration = FindClipLength(wakeUpClip, wakeUpDurationFallback);
        sleepDownDuration = FindClipLength(sleepDownClip, sleepDownDurationFallback);
    }

    void Start()
    {
        var p = FindFirstObjectByType<PlayerMovement>();
        if (p != null) player = p.transform;
        else Debug.LogWarning("SlimeEnemy: PlayerMovement tidak ditemukan di scene.", this);

        EnterState(State.Sleep);
    }

    void Update()
    {
        if (player == null) return;

        stateTimer += Time.deltaTime;

        switch (state)
        {
            case State.Sleep:
                if (PlayerInRange(detectRange)) EnterState(State.WakeUp);
                break;

            case State.WakeUp:
                if (stateTimer >= wakeUpDuration) EnterState(State.Look);
                break;

            case State.Look:
                FacePlayer();
                if (stateTimer >= lookDuration) EnterState(State.Chase);
                break;

            case State.Chase:
                if (loseRange > 0f && !PlayerInRange(loseRange)) EnterState(State.SleepDown);
                break;

            case State.SleepDown:
                if (stateTimer >= sleepDownDuration) EnterState(State.Sleep);
                break;
        }
    }

    void FixedUpdate()
    {
        float targetSpeed = 0f;

        if (state == State.Chase && player != null)
        {
            float dx = player.position.x - transform.position.x;
            if (Mathf.Abs(dx) > stopDistance)
            {
                float dir = Mathf.Sign(dx);
                if (!(stopAtLedge && IsLedgeAhead(dir)))
                    targetSpeed = dir * chaseSpeed;
                FaceDirection(dir);
            }
        }

        float vx = Mathf.MoveTowards(rb.linearVelocity.x, targetSpeed, chaseAcceleration * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);

        SetBool(walkingHash, state == State.Chase && Mathf.Abs(vx) > 0.05f);
    }

    // ---------------------------------------------------------------- Damage
    void OnCollisionStay2D(Collision2D collision)
    {
        if (state == State.Sleep || state == State.SleepDown) return;   // slime tidur tidak menyerang

        var health = collision.collider.GetComponentInParent<PlayerHealth>();
        if (health != null) health.TakeDamage(contactDamage, transform.position);
    }

    // ---------------------------------------------------------------- State
    void EnterState(State next)
    {
        state = next;
        stateTimer = 0f;

        switch (next)
        {
            case State.Sleep:
                SetBool(awakeHash, false);
                SetBool(walkingHash, false);
                break;

            case State.WakeUp:
                SetBool(awakeHash, true);       // Animator: Sleep -> WakeUp -> Idle
                break;

            case State.Look:
                FacePlayer();                   // animasi Idle sedang berjalan
                break;

            case State.Chase:
                break;                          // IsWalking diatur di FixedUpdate

            case State.SleepDown:
                SetBool(walkingHash, false);
                SetBool(awakeHash, false);      // Animator: Idle/Walk -> SleepDown -> Sleep
                break;
        }
    }

    // ---------------------------------------------------------------- Helper
    bool PlayerInRange(float range)
    {
        Vector2 d = player.position - transform.position;
        return Mathf.Abs(d.y) <= verticalTolerance && d.sqrMagnitude <= range * range;
    }

    void FacePlayer()
    {
        float dx = player.position.x - transform.position.x;
        if (Mathf.Abs(dx) > 0.01f) FaceDirection(Mathf.Sign(dx));
    }

    void FaceDirection(float dir)
    {
        if (sprite == null) return;
        sprite.flipX = spriteFacesRight ? dir < 0f : dir > 0f;
    }

    bool IsLedgeAhead(float dir)
    {
        Bounds b = col.bounds;
        Vector2 origin = new Vector2(b.center.x + dir * (b.extents.x + ledgeCheckForward), b.min.y + 0.05f);
        LayerMask mask = groundLayer.value == 0 ? Physics2D.AllLayers : groundLayer;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, ledgeCheckDepth, mask);
        return hit.collider == null || hit.collider == col;
    }

    void SetBool(int hash, bool value)
    {
        if (animator != null) animator.SetBool(hash, value);
    }

    float FindClipLength(string clipName, float fallback)
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                if (clip != null && clip.name == clipName && clip.length > 0f)
                    return clip.length;
        }
        return fallback;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);
        if (loseRange > 0f)
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, loseRange);
        }
    }
}