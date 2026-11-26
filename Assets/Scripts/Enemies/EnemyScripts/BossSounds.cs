using UnityEngine;

// Sonidos del jefe (va junto a BossEnemy). No cambia nada del comportamiento: escucha sus eventos y su estado.
// Pasos, aterrizaje, bramido antes de embestir, embestida (con fuego desde la fase 2), carga y explosion del burst,
// teletransporte, golpe recibido, golpe bloqueado, cambio de fase, muerte y un fuego de fondo desde la fase 2.
// Si un clip queda vacio, ese sonido simplemente no suena.
[RequireComponent(typeof(BossEnemy))]
public class BossSounds : MonoBehaviour
{
    [Header("Movimiento")]
    public AudioClip[] steps;
    public AudioClip land;

    [Header("Ataques")]
    [Tooltip("Bramido mientras avisa la embestida")]
    public AudioClip bellow;
    public AudioClip dash;
    [Tooltip("Embestida desde la fase 2 (con llamas). Si esta vacio usa la normal.")]
    public AudioClip dashFire;
    public AudioClip burstCharge;
    public AudioClip burstExplosion;
    public AudioClip teleport;

    [Header("Vida")]
    public AudioClip hurt;
    [Tooltip("Cuando el golpe no le hace dano (esta en el limite de la fase)")]
    public AudioClip blocked;
    public AudioClip phaseChange;
    public AudioClip death;

    [Header("Fuego de fondo (fase 2 en adelante)")]
    public AudioClip fireLoop;
    [Range(0f, 1f)] public float fireVolumePhase2 = 0.25f;
    [Range(0f, 1f)] public float fireVolumePhase3 = 0.45f;

    [Header("Volumen")]
    [Range(0f, 1f)] public float volume = 0.85f;
    [Range(0f, 1f)] public float stepsVolume = 0.55f;
    [Range(0f, 1f)] public float hurtVolume = 0.6f;
    [Range(0f, 0.3f)] public float pitchVariation = 0.07f;

    [Header("Pasos")]
    [Tooltip("Segundos entre pasos corriendo a velocidad 5 (mas rapido = pasos mas seguidos)")]
    public float stepInterval = 0.3f;

    private BossEnemy boss;
    private Rigidbody2D body;
    private AudioSource sfx, stepSource, voice, loopSource;
    private bool wasAirborne, wasTelegraphing, wasDashing, wasDead;
    private float stepTimer, lastHurt, lastBlocked, lastBellow;

    private void Awake()
    {
        boss = GetComponent<BossEnemy>();
        body = GetComponent<Rigidbody2D>();
        sfx = CreateSource();
        stepSource = CreateSource();
        voice = CreateSource();        // bellows/roars: a new one cuts the previous instead of piling up
        loopSource = CreateSource();
        loopSource.loop = true;
        loopSource.clip = fireLoop;
        loopSource.volume = 0f;
    }

    private void Start()
    {
        boss.Hurt += OnHurt;
        boss.TeleportedFrom += _ => Play(sfx, teleport);
        boss.onBurstStarted.AddListener(() => Play(sfx, burstCharge));
        boss.onEnergyBurst.AddListener(() => Play(sfx, burstExplosion));
        boss.onDamageBlocked.AddListener(OnBlocked);
        boss.onPhaseChanged.AddListener(_ => PlayVoice(phaseChange));
        boss.onBossEngaged.AddListener(() => PlayVoice(bellow));
        boss.OnDeath.AddListener(OnDeath);
    }

    private AudioSource CreateSource()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;   // 2D, like the player sounds
        return s;
    }

    private void Play(AudioSource source, AudioClip clip, float clipVolume = 1f)
    {
        if (clip == null || source == null) return;
        source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        source.PlayOneShot(clip, volume * clipVolume);
    }

    private void PlayVoice(AudioClip clip)
    {
        if (clip == null) return;
        voice.Stop();
        voice.clip = clip;
        voice.pitch = 1f + Random.Range(-pitchVariation, pitchVariation) * 0.5f;
        voice.volume = volume;
        voice.Play();
    }

    private void Update()
    {
        if (wasDead) return;

        // Landing
        bool airborne = boss.IsAirborneNow;
        if (wasAirborne && !airborne) Play(sfx, land);
        wasAirborne = airborne;

        // Bellow when it starts warning a charge (not on every chained charge)
        bool telegraphing = boss.IsTelegraphing;
        if (telegraphing && !wasTelegraphing && Time.time - lastBellow > 1.2f)
        {
            lastBellow = Time.time;
            PlayVoice(bellow);
        }
        wasTelegraphing = telegraphing;

        // Charge
        bool dashing = boss.IsDashing;
        if (dashing && !wasDashing) Play(sfx, boss.PhaseNumber >= 2 && dashFire != null ? dashFire : dash);
        wasDashing = dashing;

        // Footsteps while it runs on the ground (the charge has its own sound)
        float vx = body != null ? Mathf.Abs(body.linearVelocity.x) : 0f;
        if (!airborne && !dashing && vx > 1f && steps != null && steps.Length > 0)
        {
            stepTimer -= Time.deltaTime * Mathf.Clamp(vx / 5f, 0.6f, 2f);
            if (stepTimer <= 0f)
            {
                stepTimer = stepInterval;
                Play(stepSource, steps[Random.Range(0, steps.Length)], stepsVolume);
            }
        }
        else stepTimer = Mathf.Min(stepTimer, 0.05f);   // first step sounds right away

        // Fire crackling around it from phase 2
        if (fireLoop != null)
        {
            float target = boss.PhaseNumber >= 3 ? fireVolumePhase3 : boss.PhaseNumber == 2 ? fireVolumePhase2 : 0f;
            target *= volume;
            loopSource.volume = Mathf.MoveTowards(loopSource.volume, target, Time.deltaTime * 0.5f);
            if (loopSource.volume > 0.001f && !loopSource.isPlaying) loopSource.Play();
            else if (loopSource.volume <= 0.001f && loopSource.isPlaying) loopSource.Stop();
        }
    }

    private void OnHurt()
    {
        if (Time.time - lastHurt < 0.08f) return;
        lastHurt = Time.time;
        Play(sfx, hurt, hurtVolume);
    }

    private void OnBlocked()
    {
        if (Time.time - lastBlocked < 0.25f) return;
        lastBlocked = Time.time;
        Play(sfx, blocked, 0.8f);
    }

    private void OnDeath()
    {
        if (wasDead) return;
        wasDead = true;
        loopSource.Stop();
        voice.Stop();
        if (death == null) return;
        // On its own object: the boss can be destroyed while the death roar is still playing
        var go = new GameObject("Sonido muerte jefe");
        var s = go.AddComponent<AudioSource>();
        s.spatialBlend = 0f;
        s.PlayOneShot(death, volume);
        Destroy(go, death.length + 0.2f);
    }
}
