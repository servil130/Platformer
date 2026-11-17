using UnityEngine;

/// <summary>
/// Projektil sihir. Dibuat otomatis oleh Staff (tanpa prefab), lengkap dengan trail dan efek benturan.
/// Kalau mau pakai prefab sendiri: prefab harus punya Projectile + Collider2D (Is Trigger).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : MonoBehaviour
{
    private Rigidbody2D rb;
    private Transform owner;
    private Color color = Color.white;
    private TrailRenderer trail;
    private SpriteRenderer sr;
    private int sortingLayerId;
    private int sortingOrder;
    private bool done;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;   // digerakkan lewat velocity, tidak kena gravitasi
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        sr = GetComponent<SpriteRenderer>();
    }

    /// <summary>Bikin projektil bawaan (bola sihir kecil) tanpa perlu prefab.</summary>
    public static Projectile CreateDefault(Color color)
    {
        var go = new GameObject("Projectile");
        go.transform.localScale = new Vector3(0.4f, 0.25f, 1f);

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = ShotFx.Circle();
        renderer.color = color;

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.4f;

        return go.AddComponent<Projectile>();
    }

    public void Launch(Vector2 dir, float speed, float lifetime, Transform ownerRoot,
                       Color projectileColor, int layerId, int order)
    {
        owner = ownerRoot;
        color = projectileColor;
        sortingLayerId = layerId;
        sortingOrder = order;

        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        rb.linearVelocity = dir.normalized * speed;

        if (sr != null)
        {
            sr.color = color;
            sr.sortingLayerID = layerId;
            sr.sortingOrder = order;
        }

        SetupTrail();
        Invoke(nameof(Expire), lifetime);
    }

    void SetupTrail()
    {
        var go = new GameObject("Trail");
        go.transform.SetParent(transform, false);

        trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.minVertexDistance = 0.05f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(1f, 0f));
        trail.sharedMaterial = ShotFx.SpriteMaterial();
        trail.sortingLayerID = sortingLayerId;
        trail.sortingOrder = sortingOrder - 1;

        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (done) return;
        if (other.isTrigger) return;                                  // abaikan trigger lain
        if (owner != null && other.transform.root == owner) return;   // jangan kena diri sendiri
        if (other.GetComponent<Projectile>() != null) return;

        Impact();
    }

    void Impact()
    {
        var burst = ShotFx.CreateBurst("HitBurst", color, 0.35f, 0.12f, 3.5f,
                                       ParticleSystemShapeType.Circle, 0f, sortingLayerId, sortingOrder + 1);
        burst.transform.position = transform.position;
        burst.Emit(10);
        Destroy(burst.gameObject, 1f);

        Despawn();
    }

    void Expire()
    {
        if (!done) Despawn();
    }

    void Despawn()
    {
        done = true;
        CancelInvoke();

        // Trail dilepas supaya memudar sendiri, tidak hilang mendadak
        if (trail != null)
        {
            trail.transform.SetParent(null);
            Destroy(trail.gameObject, trail.time + 0.05f);
        }
        Destroy(gameObject);
    }
}

/// <summary>Helper kecil untuk sprite & partikel yang dibuat lewat kode.</summary>
public static class ShotFx
{
    private static Sprite circle;
    private static Material material;

    /// <summary>Lingkaran lembut 1x1 unit (dibuat sekali lalu di-cache).</summary>
    public static Sprite Circle()
    {
        if (circle != null) return circle;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);                // smoothstep: tepi lembut
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return circle;
    }

    public static Material SpriteMaterial()
    {
        if (material != null) return material;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) return null;
        material = new Material(shader);
        return material;
    }

    /// <summary>Partikel semburan sekali tembak (pakai ps.Emit(n)). Bentuk Cone = searah +Z objek.</summary>
    public static ParticleSystem CreateBurst(string name, Color color, float lifetime, float size, float speed,
                                             ParticleSystemShapeType shapeType, float coneAngle,
                                             int sortingLayerId, int sortingOrder)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startColor = color;
        main.maxParticles = 64;

        var emission = ps.emission;
        emission.rateOverTime = 0f;                       // hanya lewat Emit()

        var shape = ps.shape;
        shape.shapeType = shapeType;
        if (shapeType == ParticleSystemShapeType.Cone) shape.angle = coneAngle;
        shape.radius = 0.05f;

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
        psr.sharedMaterial = SpriteMaterial();
        psr.sortingLayerID = sortingLayerId;
        psr.sortingOrder = sortingOrder;

        ps.Play();
        return ps;
    }
}