using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menghubungkan PlayerHealth ke beberapa bar/hati buatanmu (misalnya 3 bar).
/// Total health dibagi rata ke semua bar. Bar paling kiri terisi pertama dan habis terakhir
/// (health berkurang dari kanan ke kiri).
///
/// Cara pakai: pasang di objek induk (mis. HealthUI) lalu seret tiap Image bar ke array "Bars".
/// Kalau "Bars" dikosongkan, semua Image anak (di bawah objek ini) dipakai berurutan sesuai hierarchy.
///
/// Dua mode:
///  1) Sprite : isi array "Sprites" dari KOSONG -> PENUH (mis. Heart2_0 ... Heart2_3), dipakai tiap bar.
///  2) Fill   : kosongkan "Sprites"; tiap Image dijadikan Filled (Horizontal) dan terisi sesuai porsinya.
/// Tips: kalau Max Health di PlayerHealth = jumlah bar x (jumlah sprite - 1), tiap langkah health pas
/// satu perubahan sprite. Contoh: 3 bar x 3 langkah = 9.
/// </summary>
public class HealthBarUI : MonoBehaviour
{
    [SerializeField] private PlayerHealth player;   // kosong = dicari otomatis di scene
    [SerializeField] private Image[] bars;          // urut dari kiri ke kanan; kosong = otomatis dari anak

    [Header("Mode sprite (urut dari kosong ke penuh)")]
    [SerializeField] private Sprite[] sprites;

    [Header("Mode fill")]
    [SerializeField] private float fillSpeed = 3f;  // kecepatan bar naik/turun

    private float[] targetFill;
    private bool useSprites;

    void Start()
    {
        if (bars == null || bars.Length == 0) bars = CollectChildImages();
        if (bars.Length == 0)
        {
            Debug.LogWarning("HealthBarUI: tidak ada Image bar. Isi array Bars atau taruh Image sebagai anak objek ini.", this);
            enabled = false;
            return;
        }

        useSprites = sprites != null && sprites.Length > 0;
        targetFill = new float[bars.Length];

        if (!useSprites)
        {
            foreach (var bar in bars)
            {
                if (bar == null) continue;
                bar.type = Image.Type.Filled;
                bar.fillMethod = Image.FillMethod.Horizontal;
                bar.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
        }

        if (player == null) player = FindFirstObjectByType<PlayerHealth>();
        if (player == null)
        {
            Debug.LogWarning("HealthBarUI: PlayerHealth tidak ditemukan di scene.", this);
            enabled = false;
            return;
        }

        player.HealthChanged += OnHealthChanged;
        OnHealthChanged(player.Current, player.Max);   // kondisi awal

        if (!useSprites)
            for (int i = 0; i < bars.Length; i++)
                if (bars[i] != null) bars[i].fillAmount = targetFill[i];   // langsung, tanpa animasi
    }

    void OnDestroy()
    {
        if (player != null) player.HealthChanged -= OnHealthChanged;
    }

    void Update()
    {
        if (useSprites) return;
        for (int i = 0; i < bars.Length; i++)
            if (bars[i] != null)
                bars[i].fillAmount = Mathf.MoveTowards(bars[i].fillAmount, targetFill[i], fillSpeed * Time.deltaTime);
    }

    void OnHealthChanged(int current, int max)
    {
        float perBar = (float)max / bars.Length;   // health yang ditampung satu bar

        for (int i = 0; i < bars.Length; i++)
        {
            // Porsi bar i: 0 = kosong, 1 = penuh. Bar 0 menampung health pertama, bar terakhir menampung sisanya.
            float portion = perBar > 0f ? Mathf.Clamp01((current - i * perBar) / perBar) : 0f;
            targetFill[i] = portion;

            if (!useSprites || bars[i] == null) continue;

            int last = sprites.Length - 1;
            // Bar yang masih berisi tidak pernah tampil kosong; kosong hanya kalau porsinya benar-benar 0
            int index = portion <= 0.0001f ? 0 : Mathf.Clamp(Mathf.CeilToInt(portion * last - 0.0001f), 1, last);
            bars[i].sprite = sprites[index];
        }
    }

    Image[] CollectChildImages()
    {
        var list = new List<Image>();
        foreach (var img in GetComponentsInChildren<Image>())
            if (img.gameObject != gameObject) list.Add(img);
        return list.ToArray();
    }
}