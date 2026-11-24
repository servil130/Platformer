using UnityEngine;

/// <summary>
/// Slime: tidur -> (ditembak) bangun -> berjalan ke arah datangnya tembakan -> (melihat player) diam sejenak -> mengejar.
///
/// Parameter Animator (semua bool, sesuai Slime.controller):
///   Awake     : false = tidur (Idle/Walk -> SleepDown -> Sleep), true = bangun (Sleep -> WakeUp -> Idle)
///   IsWalking : true saat berjalan
///   IsHit     : menyala sebentar saat kena tembak (diatur oleh Enemy)
///   IsDed     : menyala saat mati (diatur oleh Enemy)
/// Parameter yang tidak ada di controller diabaikan. Slime yang menyentuh player (saat bangun) melukainya.
/// </summary>
public class Slime : GroundEnemy
{
    public enum State { Sleeping, Waking, Investigating, Alert, Chasing, Idle }

    [Header("Animator")]
    [SerializeField] private string awakeParam = "Awake";
    [SerializeField] private string walkingParam = "IsWalking";

    [Header("Tidur & bangun")]
    [SerializeField] private bool startAsleep = true;
    [SerializeField] private float wakeDuration = 0.8f;        // lama state bangun (tunggu animasi WakeUp selesai)
    [SerializeField] private Color sleepTint = new Color(0.65f, 0.7f, 0.85f, 1f);
    [SerializeField] private bool wakeWhenPlayerClose = false;
    [SerializeField] private float wakeCloseRange = 1.5f;
    [SerializeField] private bool fallAsleepAgain = true;
    [SerializeField] private float idleTimeBeforeSleep = 4f;

    [Header("Menyelidiki (ke arah tembakan)")]
    [SerializeField] private float investigateTime = 3f;

    [Header("Melihat player & mengejar")]
    [SerializeField] private float alertPause = 0.6f;          // diam sejenak sebelum mengejar
    [SerializeField] private float alertHop = 3f;              // lompat kecil saat kaget (0 = tidak)
    [SerializeField] private float loseSightTime = 2.5f;       // detik kehilangan jejak sebelum menyerah
    [SerializeField] private float stopDistance = 0.9f;        // berhenti sedekat ini dari player

    public State Current { get; private set; }

    private float stateTimer;
    private float blockedTimer;
    private float lostTimer;
    private float lastSeenX;
    private float nextZTime;
    private int investigateDir = 1;

    protected override void OnAwake()
    {
        base.OnAwake();
        SetState(startAsleep ? State.Sleeping : State.Idle);
    }

    // ---------------------------------------------------------------- State machine
    void SetState(State next)
    {
        Current = next;
        stateTimer = 0f;
        blockedTimer = 0f;

        switch (next)
        {
            case State.Sleeping:
                StopWalking();
                AnimBool(awakeParam, false);
                AnimBool(walkingParam, false);
                SetBaseColor(sleepTint);
                nextZTime = Time.time + 0.3f;
                break;

            case State.Waking:
                StopWalking();
                SetFacing(investigateDir);
                SetBaseColor(OriginalColor);
                AnimBool(awakeParam, true);
                break;

            case State.Investigating:
                AnimBool(awakeParam, true);
                ShowEmote(EmoteType.Question, 0.9f);
                break;

            case State.Alert:
                StopWalking();
                FacePlayer();
                ShowEmote(EmoteType.Exclaim, alertPause + 0.2f);
                if (alertHop > 0f && IsGrounded)
                    rb.linearVelocity = new Vector2(0f, alertHop);
                break;

            case State.Chasing:
                lostTimer = 0f;
                break;

            case State.Idle:
                StopWalking();
                break;
        }
    }

    protected override void Tick(float dt)
    {
        stateTimer += dt;

        switch (Current)
        {
            case State.Sleeping:
                if (Time.time >= nextZTime) { SpawnSleepZ(); nextZTime = Time.time + 1.1f; }
                if (wakeWhenPlayerClose && DistanceToPlayer() < wakeCloseRange)
                {
                    FacePlayerDir();
                    SetState(State.Waking);
                }
                break;

            case State.Waking:
                if (stateTimer >= wakeDuration)
                    SetState(CanSeePlayer(detectionRange) ? State.Alert : State.Investigating);
                break;

            case State.Investigating:
                if (CanSeePlayer(detectionRange)) { SetState(State.Alert); break; }

                bool moved = Walk(investigateDir, walkSpeed);
                blockedTimer = moved ? 0f : blockedTimer + dt;
                if (stateTimer >= investigateTime || blockedTimer > 0.4f) SetState(State.Idle);
                break;

            case State.Idle:
                if (CanSeePlayer(detectionRange)) SetState(State.Alert);
                else if (fallAsleepAgain && stateTimer >= idleTimeBeforeSleep) SetState(State.Sleeping);
                break;

            case State.Alert:
                FacePlayer();
                if (stateTimer >= alertPause) SetState(State.Chasing);
                break;

            case State.Chasing:
                ChaseTick(dt);
                break;
        }

        bool walking = (Current == State.Investigating || Current == State.Chasing)
                       && Mathf.Abs(rb.linearVelocity.x) > 0.1f;
        AnimBool(walkingParam, walking);
    }

    // Slime yang tidur atau baru bangun tidak melukai player
    protected override bool CanHurtPlayer()
    {
        return Current != State.Sleeping && Current != State.Waking;
    }

    void ChaseTick(float dt)
    {
        var p = Player;
        if (p == null) { SetState(State.Idle); return; }

        if (CanSeePlayer(loseRange))
        {
            lostTimer = 0f;
            lastSeenX = p.position.x;
        }
        else
        {
            lostTimer += dt;
            if (lostTimer >= loseSightTime)
            {
                investigateDir = lastSeenX >= transform.position.x ? 1 : -1;
                SetState(State.Investigating);
                return;
            }
        }

        float dx = lastSeenX - transform.position.x;
        if (Mathf.Abs(dx) > stopDistance) Walk(dx, chaseSpeed);
        else { StopWalking(); SetFacing(dx >= 0f ? 1 : -1); }
    }

    // ---------------------------------------------------------------- Reaksi kena tembak
    protected override void OnDamaged(int amount, Vector2 hitDirection)
    {
        // Tembakan bergerak searah hitDirection, jadi penembaknya ada di arah sebaliknya
        int fromDir = Mathf.Abs(hitDirection.x) > 0.01f ? (hitDirection.x > 0f ? -1 : 1) : -Facing;

        switch (Current)
        {
            case State.Sleeping:
                investigateDir = fromDir;
                SetState(State.Waking);
                break;

            case State.Waking:
                investigateDir = fromDir;                      // tembakan terbaru yang dituju
                break;

            case State.Idle:
            case State.Investigating:
                if (!CanSeePlayer(detectionRange))
                {
                    investigateDir = fromDir;
                    SetState(State.Investigating);
                }
                break;

            // Alert / Chasing: sudah tahu di mana player, abaikan
        }
    }

    void FacePlayerDir()
    {
        var p = Player;
        if (p != null) investigateDir = p.position.x >= transform.position.x ? 1 : -1;
    }
}