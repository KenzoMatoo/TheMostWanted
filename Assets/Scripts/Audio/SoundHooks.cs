using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Conecta los efectos de sonido (GameAudio) con lo que ya pasa en el juego, sin tocar los scripts de cada enemigo:
// escucha sus eventos (dano, muerte, captura...) y mira su estado (alerta, carga, apuntando...).
// Se crea sola en cada escena con jugador y revisa cada segundo si aparecieron enemigos, cajas o salidas nuevas.
// Tambien le pone a cada enemigo normal su explosion de particulas al morir (EnemyDeathExplosion).
public class SoundHooks : MonoBehaviour
{
    private readonly HashSet<Object> hooked = new HashSet<Object>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Spawn();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => Spawn();

    private static void Spawn()
    {
        if (FindAnyObjectByType<SoundHooks>() != null) return;
        if (FindAnyObjectByType<PlayerController>() == null) return;
        new GameObject("Sonidos (ganchos)").AddComponent<SoundHooks>();
    }

    private IEnumerator Start()
    {
        var player = FindAnyObjectByType<PlayerController>();
        if (player != null && player.GetComponent<PlayerSoundHook>() == null) player.gameObject.AddComponent<PlayerSoundHook>();
        while (true)
        {
            Scan();
            yield return new WaitForSeconds(1f);
        }
    }

    private void Scan()
    {
        foreach (var e in FindObjectsByType<EnemyBase>(FindObjectsSortMode.None))
        {
            if (e is BossEnemy) continue;
            if (e.GetComponent<EnemySoundHook>() == null) e.gameObject.AddComponent<EnemySoundHook>();
            // Normal enemies burst into particles the moment they die (training dummies revive, so not them)
            if (!(e is EnemyDummy) && e.GetComponent<EnemyDeathExplosion>() == null) e.gameObject.AddComponent<EnemyDeathExplosion>();
        }

        foreach (var box in FindObjectsByType<ThrowableBox>(FindObjectsSortMode.None))
        {
            if (!hooked.Add(box)) continue;
            var b = box;
            b.OnDamageTaken.AddListener(() => { if (!b.IsDead()) GameAudio.PlayAt("Caja_Golpe", b.transform.position, 0.8f); });
            b.OnDestroyed.AddListener(() => GameAudio.PlayAt("Caja_Rompe", b.transform.position));
            b.OnCaptured.AddListener(() => GameAudio.Play("Captura_Lograda", 0.8f));
            b.OnReleased.AddListener(() => GameAudio.Play("Lanzar", 0.8f));
        }

        foreach (var exit in FindObjectsByType<LevelExit>(FindObjectsSortMode.None))
        {
            if (!hooked.Add(exit)) continue;
            exit.onUnlocked.AddListener(() => GameAudio.Play("Salida_Abre"));
            exit.onPlayerEntered.AddListener(() => GameAudio.Play("Salida_Entrar"));
        }
    }
}

// Sounds of the player that come from systems without their own sound: absorbing, healing, the grappling hook
public class PlayerSoundHook : MonoBehaviour
{
    private CharacterCombat combat;
    private GrapplingHook hook;
    private PlayerStats stats;
    private FieldInfo absorbField;
    private bool wasHooked;
    private int lastHealth = -1;

    private void Start()
    {
        combat = GetComponent<CharacterCombat>();
        hook = GetComponent<GrapplingHook>();
        stats = GetComponent<PlayerStats>();
        absorbField = typeof(CharacterCombat).GetField("absorbProgress", BindingFlags.NonPublic | BindingFlags.Instance);
        if (combat != null) combat.Absorbed += () => GameAudio.Play("Absorber_Explosion");
        if (stats != null) lastHealth = stats.GetCurrentHealth();
    }

    private void Update()
    {
        // Absorbing: bubbling loop while E is held on a captured enemy
        bool absorbing = combat != null && absorbField != null && (float)absorbField.GetValue(combat) > 0f;
        GameAudio.SetLoop("Absorber_Carga", this, absorbing, 0.7f);

        // Grappling hook
        bool hooked = hook != null && hook.IsHooked();
        if (hooked && !wasHooked) GameAudio.Play("Gancho_Engancha", 0.8f);
        if (!hooked && wasHooked) GameAudio.Play("Gancho_Suelta", 0.6f);
        wasHooked = hooked;

        // Healing (absorbing, healing stations...)
        if (stats != null)
        {
            int h = stats.GetCurrentHealth();
            if (lastHealth >= 0 && h > lastHealth && !stats.IsDead()) GameAudio.Play("Curar", 0.6f, 0f);
            lastHealth = h;
        }
    }

    private void OnDisable() => GameAudio.SetLoop("Absorber_Carga", this, false);
}

// Sounds of a normal enemy: hit, death, capture, stun, plus the special moments of each kind
public class EnemySoundHook : MonoBehaviour
{
    private enum Kind { Other, Cactus, Bat, Goat, Tank, Sniper }

    private EnemyBase enemy;
    private MeleeChaserEnemy melee;
    private Rigidbody2D rb;
    private Kind kind;
    private FieldInfo spotField, stateField, phaseField, animField;
    private bool wasAttacking, wasSpotting;
    private string lastState, lastPhase;
    private float lastHit, lastStun, stepTimer, flapTimer;

    private void Start()
    {
        enemy = GetComponent<EnemyBase>();
        melee = enemy as MeleeChaserEnemy;
        rb = GetComponent<Rigidbody2D>();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        switch (enemy)
        {
            case EnemyBandido _: kind = Kind.Cactus; spotField = typeof(EnemyBandido).GetField("isSpotting", flags); break;
            case EnemyEvilBat _: kind = Kind.Bat; spotField = typeof(EnemyEvilBat).GetField("isSpotting", flags); break;
            case EnemyDasher _:
                kind = Kind.Goat;
                phaseField = typeof(EnemyDasher).GetField("dashPhase", flags);
                animField = typeof(EnemyDasher).GetField("currentAnim", flags);
                break;
            case TankEnemy _: kind = Kind.Tank; stateField = typeof(TankEnemy).GetField("state", flags); break;
            case SniperEnemy _: kind = Kind.Sniper; stateField = typeof(SniperEnemy).GetField("state", flags); break;
        }

        enemy.OnDamageTaken.AddListener(OnHit);
        enemy.OnDeath.AddListener(() => { Here("Enemigo_Muerte"); GameAudio.SetLoop("Laser_Apuntando", this, false); });
        enemy.OnCaptureStarted.AddListener(() => GameAudio.Play("Lazo", 0.8f));
        enemy.OnCaptured.AddListener(() => GameAudio.Play("Captura_Lograda", 0.8f));
        enemy.OnCaptureCanceled.AddListener(() => GameAudio.Play("Captura_Fallida", 0.7f));
        enemy.OnReleased.AddListener(() => GameAudio.Play("Lanzar", 0.8f));
        enemy.OnStunnedChanged.AddListener(OnStun);
    }

    private void Here(string name, float volume = 1f) => GameAudio.PlayAt(name, transform.position, volume);

    private void OnHit()
    {
        if (Time.time - lastHit < 0.06f || enemy.IsDead()) return;
        lastHit = Time.time;
        Here("Enemigo_Golpe", 0.8f);
    }

    private void OnStun(float value)
    {
        if (value >= 95f && lastStun < 95f) Here("Enemigo_Aturdido", 0.6f);
        lastStun = value;
    }

    private void LateUpdate()
    {
        if (enemy == null || enemy.IsDead()) return;
        bool near = GameAudio.DistanceFactor(transform.position) > 0.05f;

        // Melee attack (cactus swing, bat bite; the goat's charge is handled below)
        if (melee != null)
        {
            bool attacking = melee.isAttacking;
            if (attacking && !wasAttacking)
            {
                if (kind == Kind.Cactus) Here("Cactus_Ataque");
                else if (kind == Kind.Bat) Here("Murcielago_Ataque");
                else if (kind == Kind.Goat) Here(animField != null && (animField.GetValue(enemy) as string) == "Alerta" ? "Cabra_Alerta" : "Cabra_Bufido");
            }
            wasAttacking = attacking;
        }

        // "Spotting" the player
        if (spotField != null)
        {
            bool spotting = (bool)spotField.GetValue(enemy);
            if (spotting && !wasSpotting) Here(kind == Kind.Cactus ? "Cactus_Engano" : "Murcielago_Chillido");
            wasSpotting = spotting;
        }

        // Goat: charge and crash
        if (phaseField != null)
        {
            string phase = phaseField.GetValue(enemy).ToString();
            if (phase != lastPhase && wasAttacking)
            {
                if (phase == "Dashing") Here("Cabra_Carga");
                else if (phase == "Recovering" && lastPhase == "Dashing") Here("Cabra_Choque");
            }
            lastPhase = phase;
        }

        // Tank / sniper: alert, laser while aiming, lock-on (their shots sound from the bullet)
        if (stateField != null)
        {
            string state = stateField.GetValue(enemy).ToString();
            if (state != lastState)
            {
                if (state == "Spotting") Here(kind == Kind.Tank ? "Tanque_Alerta" : "Rifle_Alerta");
                if (state == "Locked") Here("Laser_Fijado", 0.8f);
            }
            lastState = state;
            if (kind == Kind.Sniper)
                GameAudio.SetLoop("Laser_Apuntando", this, state == "Tracking" && near, 0.35f * GameAudio.DistanceFactor(transform.position));
        }

        if (!near || rb == null) return;

        // Cactus footsteps
        if (kind == Kind.Cactus && enemy is EnemyBandido b && b.isGrounded && Mathf.Abs(rb.linearVelocity.x) > 1f)
        {
            stepTimer -= Time.deltaTime;
            if (stepTimer <= 0f) { stepTimer = 0.32f; Here("Paso_Enemigo", 0.4f); }
        }

        // Bat wings
        if (kind == Kind.Bat)
        {
            flapTimer -= Time.deltaTime;
            if (flapTimer <= 0f) { flapTimer = Random.Range(0.45f, 0.7f); Here("Murcielago_Aleteo", 0.25f); }
        }
    }

    private void OnDisable() => GameAudio.SetLoop("Laser_Apuntando", this, false);
}
