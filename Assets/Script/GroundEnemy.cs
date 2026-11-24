using UnityEngine;

/// <summary>
/// Musuh yang berjalan di tanah (slime, skeleton, goblin, dll).
/// Menyediakan: jalan dengan akselerasi, cek tembok & tepi jurang, dan deteksi player (jarak + garis pandang).
/// Class turunan cukup memanggil Walk(), StopWalking(), CanSeePlayer() di Tick().
/// </summary>
public abstract class GroundEnemy : Enemy
{
    [Header("Gerak")]
    [SerializeField] protected float walkSpeed = 1.5f;
    [SerializeField] protected float chaseSpeed = 3f;
    [SerializeField] private float acceleration = 20f;
    [SerializeField] private bool avoidEdges = true;           // berhenti di tepi jurang
    [SerializeField] private float edgeCheckDepth = 0.4f;

    [Header("Deteksi player")]
    [SerializeField] protected float detectionRange = 5f;
    [SerializeField] protected float loseRange = 8f;           // jarak maksimal saat sedang mengejar
    [SerializeField] private float detectionHeight = 2f;       // selisih tinggi maksimal
    [SerializeField] private bool requireLineOfSight = true;   // terhalang tembok = tidak terlihat

    protected bool IsGrounded { get; private set; }

    private float targetSpeedX;
    private ContactFilter2D solidFilter;
    private ContactFilter2D groundFilter;
    private readonly RaycastHit2D[] hits = new RaycastHit2D[8];

    protected override void OnAwake()
    {
        solidFilter = new ContactFilter2D { useTriggers = false };
        groundFilter = new ContactFilter2D { useTriggers = false };
        groundFilter.SetNormalAngle(45f, 135f);                // hanya permukaan yang menghadap ke atas
    }

    protected override void FixedTick(float dt)
    {
        IsGrounded = col.Cast(Vector2.down, groundFilter, hits, 0.05f) > 0;

        if (IsStaggered) return;                               // biarkan dorongan hit terasa

        float rate = acceleration * (IsGrounded ? 1f : 0.3f);
        float vx = Mathf.MoveTowards(rb.linearVelocity.x, targetSpeedX, rate * dt);
        rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);
    }

    protected override void OnDied() { targetSpeedX = 0f; }

    // ---------------------------------------------------------------- Gerak
    /// <summary>Jalan ke arah dirX. Return false kalau terhalang tembok / tepi jurang.</summary>
    protected bool Walk(float dirX, float speed)
    {
        int dir = dirX >= 0f ? 1 : -1;
        SetFacing(dir);

        if (!IsGrounded) return true;                          // di udara: biarkan momentum

        if (WallAhead(dir) || (avoidEdges && !GroundAhead(dir)))
        {
            targetSpeedX = 0f;
            return false;
        }
        targetSpeedX = dir * speed;
        return true;
    }

    protected void StopWalking() { targetSpeedX = 0f; }

    protected bool WallAhead(int dir)
    {
        int n = col.Cast(new Vector2(dir, 0f), solidFilter, hits, 0.06f);
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].collider;
            if (c == null) continue;
            if (c.GetComponentInParent<PlayerMovement>() != null) continue;   // player bukan tembok
            if (c.GetComponentInParent<Enemy>() != null) continue;            // musuh lain juga bukan
            if (Mathf.Abs(hits[i].normal.y) < 0.5f) return true;
        }
        return false;
    }

    protected bool GroundAhead(int dir)
    {
        Bounds b = col.bounds;
        Vector2 origin = new Vector2(b.center.x + dir * (b.extents.x + 0.05f), b.min.y + 0.1f);
        int n = Physics2D.Raycast(origin, Vector2.down, groundFilter, hits, edgeCheckDepth + 0.1f);
        for (int i = 0; i < n; i++)
            if (hits[i].collider != null) return true;
        return false;
    }

    // ---------------------------------------------------------------- Deteksi
    /// <summary>Player terlihat? (dalam jarak, tidak terlalu beda tinggi, dan tidak terhalang tembok)</summary>
    protected bool CanSeePlayer(float range)
    {
        var p = Player;
        if (p == null) return false;

        Vector2 from = col.bounds.center;
        Vector2 to = p.position;
        Vector2 d = to - from;
        if (d.magnitude > range || Mathf.Abs(d.y) > detectionHeight) return false;
        if (!requireLineOfSight) return true;

        int n = Physics2D.Linecast(from, to, solidFilter, hits);
        float playerDist = float.PositiveInfinity;
        float blockDist = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<PlayerMovement>() != null) playerDist = Mathf.Min(playerDist, hits[i].distance);
            else if (c.GetComponentInParent<Enemy>() == null) blockDist = Mathf.Min(blockDist, hits[i].distance);
        }
        return playerDist < blockDist;
    }
}