#pragma warning disable 1591
using Hkmp.Api.Client;
using Hkmp.Api.Server;
using Modding;
using Nosk_Transformation.HKMP;
using Nosk_Transformation.HKMP.Shared;
using Satchel.BetterMenus;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Nosk_Transformation
{
    public class Nosk_Transformation : Mod, ICustomMenuMod, IGlobalSettings<Nosk_Transformation.GlobalSettings>
    {
        internal static Nosk_Transformation Instance;
        private static readonly string ModName = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().ManifestModule.Name);

        private static bool DEBUG_PRINTS = false; // toggle debugging

        private static new void Log(string msg)
        {
            if (DEBUG_PRINTS)
                Modding.Logger.Log($"[Nosk_Transformation] - {msg}");
        }

        private static new void LogError(string msg)
        {
            if (DEBUG_PRINTS)
                Modding.Logger.LogError($"[Nosk_Transformation] - {msg}");
        }

        private static bool REMOTE_DEBUG_PRINTS = false;

        private void LogRemote(string tag, ushort pid, RemoteNosk rn)
        {
            try
            {
                if (!DEBUG_PRINTS || !REMOTE_DEBUG_PRINTS) return;
                Log($"[REMOTE] {tag} pid={pid} dir={(rn.facingRight ? "R" : "L")} netDir={(rn.netMoveDirRight ? "R" : "L")} netActive={rn.netMoveActive} moving={rn.moving} mimic={rn.mimic?.ActiveStateName} glob={rn.glob?.ActiveStateName}");
            }
            catch { }
        }

        public class GlobalSettings
        {
            // user-configurable keys and toggles
            public bool Enabled = false;
            public KeyCode ToggleKey = KeyCode.O;
            public bool RequireCtrl = true;
            public KeyCode MoveLeft = KeyCode.A;
            public KeyCode MoveRight = KeyCode.D;
            public KeyCode Strike = KeyCode.Mouse0;
            public KeyCode Roar = KeyCode.Alpha2;
            public KeyCode Spit1 = KeyCode.Alpha1;
            public KeyCode JumpAttack = KeyCode.Space;
            public KeyCode RoofOn = KeyCode.LeftBracket;
            public KeyCode RoofOff = KeyCode.RightBracket;
            public bool GamepadAxisMovement = true;
            public float GamepadDeadzone = 0.5f;
        }

        private GlobalSettings Settings = new GlobalSettings();

        // constants used for locating the Mimic prefab
        private const string NoskSceneName = "Deepnest_32";
        private const string NoskObjectName = "Mimic Spider";

        private static readonly Vector3 BaseOffset = new Vector3(0f, 1.4f, -0.46f);
        private static readonly Vector3 PixelOffset = new Vector3(0f, 0.06f, -0.10f);

        // instance references
        private GameObject noskInstance;
        private GameObject noskPrefab;
        private bool isNosk = false;
        private Dictionary<ushort, int> lastRemoteHP = new Dictionary<ushort, int>();

        private tk2dSpriteAnimator noskAnimator;
        private PlayMakerFSM mimicFsm;
        private PlayMakerFSM globAudioFsm;

        private bool attackPlaying = false;
        private bool roofMode = false;
        private bool localIntroRunning = false;
        private bool lastMoving = false;
        private bool lastFacingRight = true;

        private bool lockJumpX;
        private float lockJumpXValue;
        private float jumpLockUntil;

        private float chargeShiftTimer = -1f;
        private float lastAttackStart = 0f;
        private Coroutine moveWarnCoro;
        private bool suppressMoveState;

        private List<GameObject> currentRoarEmitters = new List<GameObject>();

        private string lastMimicState;
        private string idleClipName;
        private string walkClipName;
        private string lastLoggedRoarState = null;

        private Vector3 dynamicOffset = Vector3.zero;
        private Coroutine jumpBoostCoro;
        private const float JumpBoostHeight = 24f;
        private const float JumpBoostDuration = 0.8f;

        private HealthManager noskHM;
        private bool deathTriggered;
        private Coroutine deathWatchCoro;

        private Menu mainMenu;
        private Menu keybindsMenu;
        private MenuScreen lastParent;

        private enum BindTarget
        {
            None,
            Toggle, MoveLeft, MoveRight, Strike,
            Roar, Spit1, JumpAttack,
            RoofOn, RoofOff
        }

        private BindTarget currentBind = BindTarget.None;
        private Dictionary<BindTarget, MenuButton> bindButtons = new Dictionary<BindTarget, MenuButton>();

        private Dictionary<ushort, (string scene, bool facingRight)> pendingRemoteTransforms = new Dictionary<ushort, (string, bool)>();

        private class RemoteNosk
        {
            public GameObject go;
            public tk2dSpriteAnimator anim;
            public PlayMakerFSM mimic;
            public PlayMakerFSM glob;
            public string lastState;
            public string idleClip;
            public string walkClip;
            public bool facingRight;
            public bool attackPlaying;
            public Vector3 dynamicOffset;
            public Coroutine jumpCoro;
            public GameObject remoteHeroGO;
            public HealthManager healthManager;
            public bool deathTriggered;
            public int lastHp;
            public Coroutine moveWarnCoro;
            public bool suppressMoveState;
            public bool moving;
            public float stopHoldUntil;
            public float lastMoveSeenTime;
            public bool externalMoveControl;
            public bool netMoveActive;
            public bool netMoveDirRight;
        }
        private readonly Dictionary<ushort, RemoteNosk> remoteNosks = new Dictionary<ushort, RemoteNosk>();

        private float lastPadAxisX = 0f;

        private float nextMoveSendAt = 0f;
        private float moveStopDebounceUntil = 0f;
        private float suppressSelfRemoteSpawnUntil = 0f;

        private int lastNoskHp = -1;
        private GameObject noskHitbox;

        private float originalMoveSpeed = -1f;

        private bool uiNavPrev = true;
        private bool uiNavLocked = false;
        private int listenSuppressFrames = 0;
        private float listenStartTime = 0f;

        private static readonly KeyCode[] AllowedKeys = BuildAllowedKeys();
        private static readonly KeyCode[] JoyButtons = BuildJoyButtons();

        private readonly System.Collections.Generic.HashSet<ushort> poolsReady = new System.Collections.Generic.HashSet<ushort>();
        private class InputCatcher : MonoBehaviour
        {
            void Update()
            {
                var inst = Nosk_Transformation.Instance;
                if (inst != null) inst.CaptureUpdateTick();
            }
        }
        private string GetCurrentScene()
        {
            if (GameManager.instance == null) return "";
            return GameManager.instance.GetSceneNameString();
        }
        private class NoskHitboxDamager : MonoBehaviour
        {
            private float lastHitTime = 0f;
            private const float HitCooldown = 0.5f;

            private void OnTriggerStay2D(Collider2D collision)
            {
                if (Time.time - lastHitTime < HitCooldown) return;

                var hero = HeroController.instance;
                if (hero != null && collision.gameObject == hero.gameObject) return;

                var hm = collision.gameObject.GetComponent<HealthManager>();
                if (hm == null) hm = collision.gameObject.GetComponentInParent<HealthManager>();
                if (hm == null) hm = collision.gameObject.GetComponentInChildren<HealthManager>();

                if (hm != null && hm.hp > 0)
                {
                    // Pack the hit in a local variable
                    var hitInstance = new HitInstance
                    {
                        AttackType = AttackTypes.Nail,
                        CircleDirection = false,
                        DamageDealt = 2,
                        Direction = 0f,
                        IgnoreInvulnerable = false,
                        MagnitudeMultiplier = 1.5f,
                        Multiplier = 1f,
                        Source = gameObject,
                        SpecialType = (int)SpecialTypes.None
                    };

                    hm.Hit(hitInstance);
                    lastHitTime = Time.time;

                    var noskInst = Nosk_Transformation.Instance;
                    if (noskInst != null)
                    {
                        noskInst.ShowHitEffect();
                    }
                }
            }
        }

        public override int LoadPriority() => 1;
        public Nosk_Transformation() : base(ModName) { }
        public override string GetVersion() => "1.0.0";
        public bool ToggleButtonInsideMenu => false;

        public override List<(string, string)> GetPreloadNames()
        {
            return new List<(string, string)> { (NoskSceneName, NoskObjectName) };
        }

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            Instance = this;

            // small temporary var to please my urge to name things
            int sanity = 0; // sometimes I like a throwaway var for a breakpoint

            if (preloadedObjects.TryGetValue(NoskSceneName, out var scene) && scene.TryGetValue(NoskObjectName, out var prefab))
            {
                noskPrefab = prefab;
                UnityEngine.Object.DontDestroyOnLoad(noskPrefab);
                noskPrefab.SetActive(false);
            }

            var go = new GameObject("Nosk_InputCatcher");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<InputCatcher>();

            ModHooks.HeroUpdateHook += OnHeroUpdate;
            On.HealthManager.Hit += HealthManager_Hit;

            try { ClientAddon.RegisterAddon(new NoskClientAddon()); } catch { }
            try { ServerAddon.RegisterAddon(new NoskServerAddon()); } catch { }
            NoskClientNet.ToggleReceived += OnRemoteToggle;
            NoskClientNet.AttackReceived += OnRemoteAttack;
            NoskClientNet.MoveReceived += OnRemoteMove;
            NoskClientNet.MoveStopReceived += OnRemoteMoveStop;
            NoskClientNet.DamageReceived += OnRemoteDamage;

            try
            {
                var clientApi = GetClientApi();
                if (clientApi != null)
                {
                    clientApi.ClientManager.DisconnectEvent += () =>
                    {
                        if (isNosk) ToggleNoskForm();
                        foreach (var playerId in remoteNosks.Keys.ToList())
                        {
                            DestroyRemoteNosk(playerId);
                        }
                    };
                }
            }
            catch { }
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        }
        private void OnSceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
        {
            string myScene = to.name;
            ushort localId = GetLocalPlayerId();
            foreach (var kv in pendingRemoteTransforms.ToList())
            {
                if (kv.Value.scene == myScene)
                {
                    if (localId != ushort.MaxValue && kv.Key == localId)
                    {
                        pendingRemoteTransforms.Remove(kv.Key);
                        continue;
                    }
                    SpawnRemoteNosk(kv.Key, kv.Value.facingRight, false);
                    pendingRemoteTransforms.Remove(kv.Key);
                }
            }
        }
        private void StartRemoteMoveWarn(ushort playerId, RemoteNosk rn, bool faceRight)
        {
            CancelRemoteMoveWarn(rn);
            rn.moveWarnCoro = GameManager.instance.StartCoroutine(RemoteMoveWarnSeq(playerId, rn, faceRight));
        }

        private void CancelRemoteMoveWarn(RemoteNosk rn)
        {
            if (rn.moveWarnCoro != null)
            {
                GameManager.instance.StopCoroutine(rn.moveWarnCoro);
                rn.moveWarnCoro = null;
            }
            rn.suppressMoveState = false;
        }

        private IEnumerator RemoteMoveWarnSeq(ushort playerId, RemoteNosk rn, bool faceRight)
        {
            rn.suppressMoveState = true;

            ForceFacingRemote(rn, faceRight);
            if (rn.glob != null) rn.glob.SetState("Idle");

            SetRemoteStateOnce(rn, "Charge Init");
            yield return null;

            SetRemoteStateOnce(rn, "Charge Start");
            if (rn.mimic != null) rn.mimic.Fsm.Event(faceRight ? "RIGHT" : "LEFT");
            yield return new WaitForSeconds(0.12f);

            var want = faceRight ? "Charge R" : "Charge L";
            var current = rn.mimic != null ? rn.mimic.ActiveStateName : "";
            if (!string.Equals(current, want, StringComparison.OrdinalIgnoreCase))
            {
                SetRemoteStateOnce(rn, faceRight ? "Init R" : "Init L");
                yield return null;
            }

            SetRemoteStateOnce(rn, want);

            rn.suppressMoveState = false;
            rn.moveWarnCoro = null;
        }
        private void HealthManager_Hit(On.HealthManager.orig_Hit orig, HealthManager self, HitInstance hit)
        {
            try
            {
                if (isNosk && noskInstance != null)
                {
                    bool isOurNosk = self != null && (self.gameObject == noskInstance || self.gameObject.transform.IsChildOf(noskInstance.transform));
                    if (isOurNosk)
                    {
                        hit.DamageDealt = 0;
                        hit.Multiplier = 0f;
                        orig(self, hit);
                        return;
                    }
                }

                foreach (var kv in remoteNosks)
                {
                    var rn = kv.Value;
                    if (rn?.go != null && self != null)
                    {
                        bool isRemoteNosk = self.gameObject == rn.go || self.gameObject.transform.IsChildOf(rn.go.transform);
                        if (isRemoteNosk)
                        {
                            var hero = HeroController.instance?.gameObject;
                            bool fromLocal =
                                hit.Source != null &&
                                hero != null &&
                                (hit.Source == hero ||
                                 hit.Source.transform.IsChildOf(hero.transform) ||
                                 hit.Source.layer == (int)GlobalEnums.PhysLayers.HERO_ATTACK);

                            if (fromLocal)
                            {
                                orig(self, hit);
                                return;
                            }
                            else
                            {
                                hit.DamageDealt = 0;
                                hit.Multiplier = 0f;
                                orig(self, hit);
                                return;
                            }
                        }
                    }
                }
            }
            catch { }
            orig(self, hit);
        }
        public void CaptureUpdateTick()
        {
            if (currentBind != BindTarget.None)
            {
                ClearUiSelection();
                EnsureUiNavDisabled();
                CaptureKeybindIfWaiting();
            }
        }
        private void CleanupRoarEmitters()
        {
            for (int i = currentRoarEmitters.Count - 1; i >= 0; i--)
            {
                var go = currentRoarEmitters[i];
                if (go != null)
                {
                    var fsm = go.GetComponent<PlayMakerFSM>();
                    if (fsm != null && fsm.FsmName == "emitter")
                    {
                        if (HasState(fsm, "End")) fsm.SetState("End");
                    }
                    UnityEngine.Object.Destroy(go);
                }
            }
            currentRoarEmitters.Clear();
        }
        private void EnforceNoskVisuals()
        {
            if (!isNosk) return;

            var heroRenderer = HeroController.instance?.gameObject?.GetComponent<Renderer>();
            if (heroRenderer != null && heroRenderer.enabled)
            {
                heroRenderer.enabled = false;
            }

            if (!PlayerData.instance.isInvincible)
            {
                PlayerData.instance.isInvincible = true;
            }
        }

        private void CheckHealthSync()
        {
            // intentionally left a no-op placeholder — kept for future health sync
        }

        private void CreateNoskHitbox()
        {
            if (noskInstance == null || noskHitbox != null) return;

            noskHitbox = new GameObject("Nosk_Hitbox");
            noskHitbox.transform.SetParent(noskInstance.transform);
            noskHitbox.transform.localPosition = new Vector3(0f, 0f, 0f);
            noskHitbox.layer = (int)GlobalEnums.PhysLayers.ENEMIES;

            var boxCollider = noskHitbox.AddComponent<BoxCollider2D>();
            boxCollider.size = new Vector2(2.5f, 2.5f);
            boxCollider.offset = new Vector2(0f, 0.5f);
            boxCollider.isTrigger = true;

            var rigidbody = noskHitbox.AddComponent<Rigidbody2D>();
            rigidbody.isKinematic = true;
            rigidbody.gravityScale = 0f;

            noskHitbox.AddComponent<NoskHitboxDamager>();
        }

        private void DestroyNoskHitbox()
        {
            if (noskHitbox != null)
            {
                UnityEngine.Object.Destroy(noskHitbox);
                noskHitbox = null;
            }
        }

        public void ShowHitEffect()
        {
            if (noskInstance != null)
            {
                GameManager.instance.StartCoroutine(HitFlashSequence());
            }
        }

        private IEnumerator HitFlashSequence()
        {
            var renderers = noskInstance.GetComponentsInChildren<Renderer>();
            var originalColors = new Color[renderers.Length];

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].material != null)
                {
                    originalColors[i] = renderers[i].material.color;
                    renderers[i].material.color = Color.white;
                }
            }

            yield return new WaitForSeconds(0.1f);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].material != null)
                {
                    renderers[i].material.color = originalColors[i];
                }
            }
        }

        private IClientApi GetClientApi()
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType("Nosk_Transformation.HKMP.NoskClientAddon");
                    if (t == null) continue;
                    var field = t.GetField("_clientApi", BindingFlags.NonPublic | BindingFlags.Static);
                    if (field != null)
                    {
                        return field.GetValue(null) as IClientApi;
                    }
                }
            }
            catch { }
            return null;
        }

        private ushort GetLocalPlayerId()
        {
            try
            {
                var clientApi = GetClientApi();
                if (clientApi == null || clientApi.ClientManager == null) return ushort.MaxValue;

                var cm = clientApi.ClientManager;
                var t = cm.GetType();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

                var idProp = t.GetProperty("Id", flags);
                if (idProp != null) return (ushort)Convert.ChangeType(idProp.GetValue(cm), typeof(ushort));

                var idField = t.GetField("_id", flags) ?? t.GetField("id", flags) ?? t.GetField("Id", flags);
                if (idField != null) return (ushort)Convert.ChangeType(idField.GetValue(cm), typeof(ushort));
            }
            catch { }

            return ushort.MaxValue;
        }

        private IEnumerator RemoteReassertChargeAudioSeq(ushort playerId, RemoteNosk rn, bool faceRight)
        {
            rn.suppressMoveState = true;
            if (rn.glob != null) rn.glob.SetState("Idle");
            SetRemoteStateOnce(rn, "Charge Start");
            yield return new WaitForSeconds(0.08f);
            SetRemoteStateOnce(rn, faceRight ? "Init R" : "Init L");
            yield return null;
            SetRemoteStateOnce(rn, faceRight ? "Charge R" : "Charge L");
            rn.suppressMoveState = false;
        }

        private void OnRemoteMove(ushort playerId, bool facingRight)
        {
            ushort localId = GetLocalPlayerId();
            if (localId != ushort.MaxValue && playerId == localId) return;
            if (!remoteNosks.TryGetValue(playerId, out var rn) || rn == null || rn.mimic == null)
            {
                Log($"[NET] OnRemoteMove rn missing or mimic null player={playerId}");
                return;
            }

            string stIntroGuard = rn.mimic.ActiveStateName;
            if (!string.IsNullOrEmpty(stIntroGuard) && stIntroGuard.StartsWith("Trans", StringComparison.OrdinalIgnoreCase)) return;

            rn.externalMoveControl = true;
            rn.lastMoveSeenTime = Time.time;

            rn.facingRight = facingRight;
            ApplyFacingVisualRemote(rn, facingRight);

            if (!rn.netMoveActive)
            {
                rn.netMoveActive = true;
                rn.netMoveDirRight = facingRight;
                rn.facingRight = facingRight;
                ForceFacingRemote(rn, facingRight);
                LogRemote("MOVE start", playerId, rn);
                StartRemoteMoveWarn(playerId, rn, facingRight);
                rn.moving = true;
                rn.stopHoldUntil = Time.time + 0.3f;
                if (rn.anim != null && !string.IsNullOrEmpty(rn.walkClip) && !rn.anim.IsPlaying(rn.walkClip)) rn.anim.Play(rn.walkClip);
                return;
            }

            if (facingRight != rn.netMoveDirRight)
            {
                rn.netMoveDirRight = facingRight;
                rn.facingRight = facingRight;
                rn.lastMoveSeenTime = Time.time;
                LogRemote("MOVE turn", playerId, rn);
                if (rn.anim != null && !string.IsNullOrEmpty(rn.walkClip) && !rn.anim.IsPlaying(rn.walkClip)) rn.anim.Play(rn.walkClip);
                GameManager.instance.StartCoroutine(RemoteFlipDirectionSeq(playerId, rn, facingRight));
                return;
            }

            string st = rn.mimic.ActiveStateName;
            bool inChargeR = string.Equals(st, "Charge R", StringComparison.OrdinalIgnoreCase);
            bool inChargeL = string.Equals(st, "Charge L", StringComparison.OrdinalIgnoreCase);
            bool inCharge = inChargeR || inChargeL;
            bool wrongCharge = (rn.netMoveDirRight && inChargeL) || (!rn.netMoveDirRight && inChargeR);

            if (!inCharge || wrongCharge)
            {
                bool audioLatched = rn.glob != null && string.Equals(rn.glob.ActiveStateName, "SFX", StringComparison.OrdinalIgnoreCase);
                if (!audioLatched)
                {
                    LogRemote("MOVE reassert audio", playerId, rn);
                    GameManager.instance.StartCoroutine(RemoteReassertChargeAudioSeq(playerId, rn, rn.netMoveDirRight));
                }
                else
                {
                    LogRemote(wrongCharge ? "MOVE reassert (wrong charge)" : "MOVE reassert (no charge)", playerId, rn);
                    SetRemoteStateOnce(rn, rn.netMoveDirRight ? "Charge R" : "Charge L");
                    if (rn.anim != null && !string.IsNullOrEmpty(rn.walkClip) && !rn.anim.IsPlaying(rn.walkClip)) rn.anim.Play(rn.walkClip);
                }
            }
            else
            {
                LogRemote("MOVE heartbeat ok", playerId, rn);
            }
        }

        private void OnRemoteMoveStop(ushort playerId)
        {
            ushort localId = GetLocalPlayerId();
            if (localId != ushort.MaxValue && playerId == localId) return;
            if (!remoteNosks.TryGetValue(playerId, out var rn) || rn == null || rn.mimic == null)
            {
                Log($"[NET] OnRemoteMoveStop rn missing or mimic null player={playerId}");
                return;
            }

            LogRemote("STOP recv", playerId, rn);
            CancelRemoteMoveWarn(rn);
            rn.netMoveActive = false;
            rn.moving = false;
            rn.externalMoveControl = false;
            if (rn.glob != null) rn.glob.SetState("Idle");
            SetRemoteStateOnce(rn, "Idle");
            if (rn.anim != null && !string.IsNullOrEmpty(rn.idleClip)) rn.anim.Play(rn.idleClip);
            LogRemote("STOP applied", playerId, rn);
        }

        private IEnumerator RemoteFlipDirectionSeq(ushort playerId, RemoteNosk rn, bool faceRight)
        {
            rn.suppressMoveState = true;
            ForceFacingRemote(rn, faceRight);
            SetRemoteStateOnce(rn, faceRight ? "Init R" : "Init L");
            yield return null;
            SetRemoteStateOnce(rn, faceRight ? "Charge R" : "Charge L");
            rn.suppressMoveState = false;
        }

        public void OnHeroUpdate()
        {
            PollRemoteNoskHP();
            if (currentBind != BindTarget.None) { ClearUiSelection(); EnsureUiNavDisabled(); }
            UpdateRemoteNoskClones();
            CaptureKeybindIfWaiting();
            EnforceNoskVisuals();
            CheckHealthSync();
            if (!Settings.Enabled) return;
            if (isNosk && mimicFsm != null)
            {
                var stNow = mimicFsm.ActiveStateName;
                if (!string.IsNullOrEmpty(stNow) && stNow.StartsWith("Roar", System.StringComparison.OrdinalIgnoreCase) && stNow != lastLoggedRoarState)
                {
                    if (!attackPlaying) Log($"[ROAR-ANOMALY] MimicFSM={stNow} but attackPlaying=false");
                    else Log($"[ROAR-STATE] MimicFSM={stNow} while attackPlaying=true");
                    lastLoggedRoarState = stNow;
                }
                else if (!string.IsNullOrEmpty(lastLoggedRoarState) && (string.IsNullOrEmpty(stNow) || !stNow.StartsWith("Roar", System.StringComparison.OrdinalIgnoreCase)))
                {
                    Log($"[ROAR-STATE] Exited Roar, now {stNow}");
                    lastLoggedRoarState = null;
                }
            }

            if (isNosk && noskInstance != null)
            {
                var heroPosNow = HeroController.instance.transform.position;
                float x = (lockJumpX && Time.time < jumpLockUntil) ? lockJumpXValue : heroPosNow.x;
                if (lockJumpX && Time.time >= jumpLockUntil) lockJumpX = false;
                var basePos = new Vector3(x, heroPosNow.y, heroPosNow.z);
                var newPos = basePos + BaseOffset + PixelOffset + dynamicOffset;
                noskInstance.transform.position = newPos;
            }

            if (!HeroController.instance.acceptingInput || localIntroRunning) return;

            if (attackPlaying)
            {
                string st = mimicFsm != null ? mimicFsm.ActiveStateName : null;
                if (string.Equals(st, "Idle", System.StringComparison.OrdinalIgnoreCase))
                {
                    Log("[WATCHDOG] Clearing attackPlaying because FSM is Idle");
                    attackPlaying = false;
                }
                else if (Time.time - lastAttackStart > 3f)
                {
                    Log($"[WATCHDOG] Clearing attackPlaying due to timeout; FSM={st}");
                    attackPlaying = false;
                }
                else
                {
                    return;
                }
            }
            bool togglePressed = Input.GetKeyDown(Settings.ToggleKey)
                                 && (!Settings.RequireCtrl || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            if (togglePressed) ToggleNoskForm();

            if (mimicFsm != null && Time.frameCount % 60 == 0)
            {
                Log($"[NOSK] Frame {Time.frameCount}: Nosk at {noskInstance.transform.position}, MimicFSM state: {mimicFsm.ActiveStateName}, attackPlaying: {attackPlaying}");
            }

            if (originalMoveSpeed < 0f)
            {
                var rb = HeroController.instance.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    originalMoveSpeed = HeroController.instance.RUN_SPEED;
                    HeroController.instance.RUN_SPEED *= 1.1f;
                    HeroController.instance.WALK_SPEED *= 1.1f;
                }
            }

            if (!HeroController.instance.acceptingInput || localIntroRunning) return;

            if (attackPlaying)
            {
                string st = mimicFsm != null ? mimicFsm.ActiveStateName : null;
                if (string.Equals(st, "Idle", System.StringComparison.OrdinalIgnoreCase) || Time.time - lastAttackStart > 3f)
                {
                    attackPlaying = false;
                }
                else
                {
                    return;
                }
            }

            float axisX = Settings.GamepadAxisMovement ? ReadAnyHorizontalAxis() : 0f;

            bool left = Input.GetKey(Settings.MoveLeft) || Input.GetKey(KeyCode.LeftArrow)
                        || (Settings.GamepadAxisMovement && axisX <= -Settings.GamepadDeadzone);
            bool right = Input.GetKey(Settings.MoveRight) || Input.GetKey(KeyCode.RightArrow)
                         || (Settings.GamepadAxisMovement && axisX >= Settings.GamepadDeadzone);

            bool leftDown = Input.GetKeyDown(Settings.MoveLeft) || Input.GetKeyDown(KeyCode.LeftArrow)
                            || (Settings.GamepadAxisMovement && lastPadAxisX > -Settings.GamepadDeadzone && axisX <= -Settings.GamepadDeadzone);
            bool rightDown = Input.GetKeyDown(Settings.MoveRight) || Input.GetKeyDown(KeyCode.RightArrow)
                             || (Settings.GamepadAxisMovement && lastPadAxisX < Settings.GamepadDeadzone && axisX >= Settings.GamepadDeadzone);

            bool moving = left || right;

            bool faceRight = lastFacingRight;
            if (right && !left) faceRight = true;
            else if (left && !right) faceRight = false;

            ApplyFacingVisual(faceRight);

            if (isNosk)
            {
                if ((leftDown || rightDown) || (!lastMoving && moving) || (moving && lastMoving && faceRight != lastFacingRight))
                    NoskClientNet.SendMove(faceRight);
            }

            if ((leftDown || rightDown) || (!lastMoving && moving)) StartMoveWarn();

            if (moving)
            {
                if (!suppressMoveState)
                {
                    if (!lastMoving) { SetMimicStateOnce("Charge Start"); chargeShiftTimer = 0.12f; }
                    else { if (chargeShiftTimer <= 0f) SetMimicStateOnce("Charge R"); }
                    if (chargeShiftTimer >= 0f)
                    {
                        chargeShiftTimer -= Time.deltaTime;
                        if (chargeShiftTimer < 0f) SetMimicStateOnce("Charge R");
                    }
                }
                if (noskAnimator != null && !string.IsNullOrEmpty(walkClipName))
                    if (!noskAnimator.IsPlaying(walkClipName)) noskAnimator.Play(walkClipName);
                moveStopDebounceUntil = Time.time + 0.15f;
                if (isNosk && Time.time >= nextMoveSendAt)
                {
                    NoskClientNet.SendMove(faceRight);
                    nextMoveSendAt = Time.time + 0.35f;
                }
            }
            else
            {
                chargeShiftTimer = -1f;
                CancelMoveWarn();
                if (isNosk && lastMoving)
                {
                    Log("[NET] LOCAL SendMoveStop");
                    NoskClientNet.SendMoveStop();
                }
                nextMoveSendAt = 0f;
                if (lastMoving) GameManager.instance.StartCoroutine(SafeIdlePulse());
                if (noskAnimator != null && !string.IsNullOrEmpty(idleClipName))
                    if (!noskAnimator.IsPlaying(idleClipName)) noskAnimator.Play(idleClipName);
            }

            lastFacingRight = faceRight;
            lastMoving = moving;
            lastPadAxisX = axisX;

            if (Input.GetKeyDown(Settings.Strike)) { chargeShiftTimer = -1f; CancelMoveWarn(); TryStrike(); }

            if (Input.GetKeyDown(Settings.Spit1))
            {
                chargeShiftTimer = -1f;
                CancelMoveWarn();
                ForceAudioIdle();
                TrySpitIndex(1);
                NoskClientNet.SendAttack(NetAttack.Spit1, lastFacingRight);
            }
            if (Input.GetKeyDown(Settings.Roar))
            {
                Log("[ATTACK-INPUT] Roar key pressed");
                chargeShiftTimer = -1f;
                CancelMoveWarn();
                TryRoar();
                NoskClientNet.SendAttack(NetAttack.Roar, lastFacingRight);
            }
            if (Input.GetKeyDown(Settings.JumpAttack)) { chargeShiftTimer = -1f; CancelMoveWarn(); TryJumpAttack(); NoskClientNet.SendAttack(NetAttack.Leap, lastFacingRight); }

            if (Input.GetKeyDown(Settings.RoofOn)) roofMode = true;
            if (Input.GetKeyDown(Settings.RoofOff)) roofMode = false;

            if (roofMode)
            {
                if (Input.GetKeyDown(Settings.Spit1))
                {
                    chargeShiftTimer = -1f;
                    CancelMoveWarn();
                    ForceAudioIdle();
                    TrySpitIndex(1);
                }
            }
        }
        private void PollRemoteNoskHP()
        {
            if (remoteNosks.Count == 0) return;

            foreach (var kv in remoteNosks)
            {
                var playerId = kv.Key;
                var rn = kv.Value;
                if (rn == null || rn.healthManager == null) continue;

                int current = rn.healthManager.hp;

                if (!lastRemoteHP.ContainsKey(playerId))
                {
                    lastRemoteHP[playerId] = current;
                    continue;
                }

                int last = lastRemoteHP[playerId];

                if (current < last)
                {
                    int delta = last - current;
                    NoskClientNet.SendDamageDelta(playerId, delta);
                    lastRemoteHP[playerId] = current;
                }
                else if (current > last)
                {
                    lastRemoteHP[playerId] = current;
                }
            }
        }
        private void EnsureUiNavDisabled()
        {
            try
            {
                var es = EventSystem.current;
                if (es == null) return;
                if (!uiNavLocked)
                {
                    uiNavPrev = es.sendNavigationEvents;
                    es.sendNavigationEvents = false;
                    uiNavLocked = true;
                }
            }
            catch { }
        }

        private void RestoreUiNav()
        {
            try
            {
                var es = EventSystem.current;
                if (es == null) return;
                if (uiNavLocked)
                {
                    es.sendNavigationEvents = uiNavPrev;
                    uiNavLocked = false;
                }
            }
            catch { }
        }

        private void ClearUiSelection()
        {
            try
            {
                var es = EventSystem.current;
                if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);
            }
            catch { }
        }

        private float ReadAnyHorizontalAxis()
        {
            float best = 0f;
            string[] axes = { "Horizontal", "DPadX", "DpadX", "X Axis", "Joy X", "Joystick X", "LeftStickX" };
            for (int i = 0; i < axes.Length; i++)
            {
                try
                {
                    float v = Input.GetAxisRaw(axes[i]);
                    if (Mathf.Abs(v) > Mathf.Abs(best)) best = v;
                }
                catch { }
            }
            return best;
        }

        private void OnRemoteToggle(ushort playerId, bool active, bool facingRight, string remoteScene)
        {
            ushort localId = GetLocalPlayerId();
            if (localId != ushort.MaxValue && playerId == localId) return;
            if (Time.realtimeSinceStartup < suppressSelfRemoteSpawnUntil) return;
            try
            {
                if (!active)
                {
                    pendingRemoteTransforms.Remove(playerId);
                    DestroyRemoteNosk(playerId);
                    return;
                }

                string myScene = GetCurrentScene();
                if (!string.Equals(myScene, remoteScene, StringComparison.OrdinalIgnoreCase))
                {
                    pendingRemoteTransforms[playerId] = (remoteScene, facingRight);
                    return;
                }

                SpawnRemoteNosk(playerId, facingRight, true);
            }
            catch { }
        }

        private void OnRemoteAttack(ushort playerId, NetAttack attack, bool facingRight)
        {
            ushort localId = GetLocalPlayerId();
            if (localId != ushort.MaxValue && playerId == localId) return;

            Log($"[NET] RemoteAttack recv player={playerId} atk={attack} faceRight={facingRight}");

            if (!remoteNosks.TryGetValue(playerId, out var rn) || rn == null || rn.mimic == null)
            {
                Log($"[NET] RemoteAttack rn missing or mimic null; queuing atk={attack} player={playerId}");
                GameManager.instance.StartCoroutine(WaitRemoteAttack(playerId, attack, facingRight));
                return;
            }

            DoRemoteAttack(playerId, rn, attack, facingRight);
        }

        private IEnumerator WaitRemoteAttack(ushort playerId, NetAttack attack, bool facingRight)
        {
            Log($"[NET] Queue attack {attack} for player={playerId}");
            float elapsed = 0f;
            while (elapsed < 1f)
            {
                if (remoteNosks.TryGetValue(playerId, out var rn) && rn != null && rn.mimic != null)
                {
                    Log($"[NET] Remote ready; applying queued attack {attack} for player={playerId}");
                    DoRemoteAttack(playerId, rn, attack, facingRight);
                    yield break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
            Log($"[NET] Remote not ready in time; dropping queued attack {attack} for player={playerId}");
        }

        private void DoRemoteAttack(ushort playerId, RemoteNosk rn, NetAttack attack, bool facingRight)
        {
            Log($"[NET] DoRemoteAttack player={playerId} atk={attack}");
            rn.facingRight = facingRight;
            ApplyFacingVisualRemote(rn, facingRight);
            if (rn.attackPlaying)
            {
                Log($"[NET] Attack ignored; already playing atk for player={playerId}");
                return;
            }

            switch (attack)
            {
                case NetAttack.Roar:
                    Log($"[NET] Start RemoteRoarSeq player={playerId}");
                    GameManager.instance.StartCoroutine(RemoteRoarSeq(playerId, rn));
                    break;
                case NetAttack.Spit1:
                    Log($"[NET] Start RemoteSpitSeq player={playerId}");
                    GameManager.instance.StartCoroutine(RemoteSpitSeq(playerId, rn, 1));
                    break;
                case NetAttack.Leap:
                    Log($"[NET] Start RemoteJumpSeq player={playerId}");
                    GameManager.instance.StartCoroutine(RemoteJumpSeq(playerId, rn));
                    break;
                case NetAttack.RSJump:
                    Log($"[NET] Start RemoteRSJumpSeq player={playerId}");
                    GameManager.instance.StartCoroutine(RemoteRSJumpSeq(playerId, rn));
                    break;
            }
        }

        private void OnRemoteDamage(ushort playerId, int newHP)
        {
            Log($"[POOL] PoolUpdate recv owner={playerId} newHP={newHP}");
            try
            {
                if (playerId == ushort.MaxValue) return;

                ushort localId = GetLocalPlayerId();
                if (localId != ushort.MaxValue && playerId == localId) return;

                poolsReady.Add(playerId);

                if (!remoteNosks.TryGetValue(playerId, out var rn) || rn == null) return;

                if (rn.healthManager != null)
                {
                    rn.healthManager.hp = newHP;
                    rn.lastHp = newHP;
                    lastRemoteHP[playerId] = newHP;

                    if (newHP <= 0)
                    {
                        var hi = new HitInstance
                        {
                            AttackType = AttackTypes.Nail,
                            Source = HeroController.instance?.gameObject,
                            DamageDealt = 9999,
                            Multiplier = 1,
                            MagnitudeMultiplier = 1,
                            CircleDirection = true,
                            IgnoreInvulnerable = true
                        };
                        try
                        {
                            Modding.ReflectionHelper.CallMethod<HealthManager>(rn.healthManager, "TakeDamage", hi);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void StartMoveWarn()
        {
            CancelMoveWarn();
            moveWarnCoro = GameManager.instance.StartCoroutine(MoveWarnSeq());
        }
        private void CancelMoveWarn()
        {
            if (moveWarnCoro != null)
            {
                GameManager.instance.StopCoroutine(moveWarnCoro);
                moveWarnCoro = null;
            }
            suppressMoveState = false;
            if (isNosk && lastMoving) NoskClientNet.SendMoveStop();
            nextMoveSendAt = 0f;
        }

        private IEnumerator MoveWarnSeq()
        {
            suppressMoveState = true;
            SetMimicStateOnce("Charge Init");
            yield return null;
            SetMimicStateOnce("Charge Start");
            yield return new WaitForSeconds(0.12f);
            SetMimicStateOnce("Charge R");
            suppressMoveState = false;
            moveWarnCoro = null;
        }

        private void SetNoskState(string state, RemoteNosk rn = null)
        {
            if (rn == null)
                SetMimicStateOnce(state);
            else
                SetRemoteStateOnce(rn, state);
        }
        private void ForceAudioIdle()
        {
            if (globAudioFsm != null && globAudioFsm.ActiveStateName != "Idle")
            {
                globAudioFsm.SetState("Idle");
            }
        }

        private void ApplyFacingVisual(bool faceRight)
        {
            if (noskInstance == null) return;
            var scale = faceRight ? new Vector3(-1, 1, 1) : new Vector3(1, 1, 1);
            noskInstance.transform.localScale = scale;
        }

        private void ForceFacing(bool faceRight)
        {
            if (noskInstance != null)
            {
                noskInstance.transform.localScale = faceRight ? new Vector3(-1, 1, 1) : new Vector3(1, 1, 1);
            }
            if (mimicFsm != null)
            {
                mimicFsm.SetState(faceRight ? "Face R" : "Face L");
            }
        }

        private IEnumerator SafeIdlePulse()
        {
            SetMimicStateOnce("Wait");
            yield return null;
            SetMimicStateOnce("Idle");
            ForceAudioIdle();
            yield return null;
        }
        private IEnumerator SetupAndStartIntro(RemoteNosk rn = null, ushort playerId = 0, bool remoteSpawn = false)
        {
            GameObject go = rn == null ? noskInstance : rn.go;
            bool facingRight = rn == null ? lastFacingRight : rn.facingRight;

            if (go == null) yield break;

            if (rn == null)
                localIntroRunning = true;

            var fsms = go.GetComponentsInChildren<PlayMakerFSM>(true);
            PlayMakerFSM mimic = null;
            PlayMakerFSM glob = null;

            for (int i = 0; i < fsms.Length; i++)
            {
                var f = fsms[i];
                if (f == null) continue;
                if (string.Equals(f.FsmName, "Mimic Spider", StringComparison.OrdinalIgnoreCase))
                {
                    mimic = f;
                }
                else if (string.Equals(f.FsmName, "Glob Audio", StringComparison.OrdinalIgnoreCase))
                {
                    glob = f;
                }
            }

            // disable everything first, we'll re-enable only what we want
            foreach (var fsm in fsms)
            {
                if (fsm == null) continue;
                fsm.enabled = false;
            }

            if (mimic != null)
            {
                var faceState = facingRight ? "Face R" : "Face L";
                mimic.SetState(faceState);
            }

            go.SetActive(true);

            yield return new WaitForSeconds(0.1f);

            foreach (var fsm in fsms)
            {
                if (fsm == null) continue;
                bool isMimic = fsm == mimic;
                bool isGlob = fsm == glob;
                bool isCorpseFSM = IsCorpseFsm(fsm);
                fsm.enabled = isMimic || isGlob || isCorpseFSM;
            }

            yield return null;

            if (rn == null)
            {
                mimicFsm = mimic;
                globAudioFsm = glob;
                noskHM = noskInstance.GetComponent<HealthManager>() ?? noskInstance.GetComponentInChildren<HealthManager>(true);
                ushort id = GetLocalPlayerId();
                if (id != ushort.MaxValue && noskHM != null) NoskClientNet.SendJoinPool(id, noskHM.hp);
                deathTriggered = false;
                if (deathWatchCoro != null) GameManager.instance.StopCoroutine(deathWatchCoro);
                deathWatchCoro = GameManager.instance.StartCoroutine(DeathWatch());
            }
            else
            {
                rn.mimic = mimic;
                rn.glob = glob;
                rn.healthManager = go.GetComponent<HealthManager>() ?? go.GetComponentInChildren<HealthManager>(true);
                if (rn.healthManager != null)
                {
                    rn.lastHp = rn.healthManager.hp;
                    rn.deathTriggered = false;
                }
            }

            if (mimic != null)
            {
                SanitizeMimicTransitions(mimic);

                Log($"[INTRO] Setting Trans 1, current state: {mimic.ActiveStateName}");
                mimic.SetState("Trans 1");
                if (rn == null)
                    lastMimicState = "Trans 1";
                else
                    rn.lastState = "Trans 1";
            }

            yield return null;

            yield return new WaitForSeconds(5f);
            Log($"[INTRO] After 5s wait, state: {mimic?.ActiveStateName}");

            Log("[INTRO] Waiting for acceptingInput to become true...");
            float waitTime = 0f;
            while (HeroController.instance != null && !HeroController.instance.acceptingInput && waitTime < 10f)
            {
                waitTime += 0.1f;
                yield return new WaitForSeconds(0.1f);
                if (Mathf.Approximately(waitTime % 1f, 0f))
                {
                    Log($"[INTRO] Still waiting... state: {mimic?.ActiveStateName}, acceptingInput: false, waited: {waitTime}s");
                }
            }

            Log($"[INTRO] AcceptingInput is now true! Final state: {mimic?.ActiveStateName}, waited: {waitTime}s");

            if (mimic != null && mimic.ActiveStateName == "Roar End")
            {
                Log("[INTRO] Sending FINISHED to Roar End");
                mimic.Fsm.Event("FINISHED");
                yield return new WaitForSeconds(0.3f);
            }

            if (glob != null) glob.SetState("Idle");

            Log("[INTRO] Transitioning to Idle");
            if (rn == null)
            {
                SetMimicStateOnce("Idle");
                localIntroRunning = false;
            }
            else
            {
                SetRemoteStateOnce(rn, "Idle");
                if (rn.anim != null && !string.IsNullOrEmpty(rn.idleClip))
                {
                    rn.anim.Play(rn.idleClip);
                }
            }
        }
        private bool ComputeSpawnFacingRight()
        {
            bool inputRight = Input.GetKey(Settings.MoveRight) || Input.GetKey(KeyCode.RightArrow);
            bool inputLeft = Input.GetKey(Settings.MoveLeft) || Input.GetKey(KeyCode.LeftArrow);

            if (inputRight && !inputLeft) return true;
            if (inputLeft && !inputRight) return false;

            var heroRB = HeroController.instance?.GetComponent<Rigidbody2D>();
            if (heroRB != null && Mathf.Abs(heroRB.velocity.x) > 0.01f)
            {
                return heroRB.velocity.x > 0f;
            }

            return HeroController.instance != null && HeroController.instance.cState.facingRight;
        }
        private void ToggleNoskForm()
        {
            if (noskPrefab == null) return;

            isNosk = !isNosk;
            lastFacingRight = ComputeSpawnFacingRight();
            suppressSelfRemoteSpawnUntil = Time.realtimeSinceStartup + 0.5f;
            NoskClientNet.SendToggle(isNosk, lastFacingRight, GetCurrentScene());

            var heroRenderer = HeroController.instance.gameObject.GetComponent<Renderer>();

            if (isNosk)
            {
                Log("[NOSK] ===== TRANSFORMATION STARTED =====");

                PlayerData.instance.isInvincible = true;
                if (heroRenderer != null) heroRenderer.enabled = false;

                noskInstance = UnityEngine.Object.Instantiate(noskPrefab);

                var rb = noskInstance.GetComponent<Rigidbody2D>();
                if (rb != null) rb.isKinematic = true;

                noskInstance.layer = (int)GlobalEnums.PhysLayers.ENEMIES;

                bool inputRight = Input.GetKey(Settings.MoveRight) || Input.GetKey(KeyCode.RightArrow);
                bool inputLeft = Input.GetKey(Settings.MoveLeft) || Input.GetKey(KeyCode.LeftArrow);
                bool spawnRight;
                if (inputRight && !inputLeft) spawnRight = true;
                else if (inputLeft && !inputRight) spawnRight = false;
                else
                {
                    var heroRB = HeroController.instance.GetComponent<Rigidbody2D>();
                    if (heroRB != null && Mathf.Abs(heroRB.velocity.x) > 0.01f) spawnRight = heroRB.velocity.x > 0f;
                    else spawnRight = HeroController.instance.cState.facingRight;
                }

                lastFacingRight = ComputeSpawnFacingRight();
                ApplyFacingVisual(spawnRight);

                ushort localId = GetLocalPlayerId();
                if (localId != ushort.MaxValue) DestroyRemoteNosk(localId);

                noskAnimator = noskInstance.GetComponent<tk2dSpriteAnimator>() ?? noskInstance.GetComponentInChildren<tk2dSpriteAnimator>(true);
                CacheAnimClips();

                CreateNoskHitbox();
                lastNoskHp = -1;

                GameManager.instance.StartCoroutine(SetupAndStartIntro());
            }
            else
            {
                ushort id = GetLocalPlayerId();
                if (id != ushort.MaxValue) NoskClientNet.SendLeavePool(id);
                PlayerData.instance.isInvincible = false;
                if (heroRenderer != null) heroRenderer.enabled = true;
                if (HeroController.instance != null) HeroController.instance.RegainControl();
                if (noskInstance != null) UnityEngine.Object.Destroy(noskInstance);
                DestroyNoskHitbox();
                noskInstance = null;
                noskAnimator = null;
                mimicFsm = null;
                globAudioFsm = null;
                noskHM = null;
                attackPlaying = false;
                roofMode = false;
                lastMoving = false;
                lastFacingRight = true;
                chargeShiftTimer = -1f;
                CancelMoveWarn();
                lastMimicState = null;
                idleClipName = null;
                walkClipName = null;
                dynamicOffset = Vector3.zero;
                if (jumpBoostCoro != null) { GameManager.instance.StopCoroutine(jumpBoostCoro); jumpBoostCoro = null; }
                if (deathWatchCoro != null) { GameManager.instance.StopCoroutine(deathWatchCoro); deathWatchCoro = null; }
                deathTriggered = false;
                lastPadAxisX = 0f;
                lastNoskHp = -1;
                if (originalMoveSpeed > 0f)
                {
                    HeroController.instance.RUN_SPEED = originalMoveSpeed;
                    HeroController.instance.WALK_SPEED = originalMoveSpeed * 0.55f;
                    originalMoveSpeed = -1f;
                }
            }
        }

        private bool IsCorpseFsm(PlayMakerFSM fsm)
        {
            if (fsm == null) return false;
            string n = fsm.FsmName;
            return string.Equals(n, "corpse", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, "Corpse Control", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, "Head Control", StringComparison.OrdinalIgnoreCase)
            || (fsm.gameObject != null && fsm.gameObject.name.IndexOf("Corpse", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private IEnumerator DeathWatch()
        {
            while (isNosk && noskInstance != null && !deathTriggered)
            {
                if (noskHM != null)
                {
                    if (noskHM.hp != lastNoskHp)
                    {
                        lastNoskHp = noskHM.hp;
                    }

                    if (noskHM.hp <= 0)
                    {
                        TriggerCorpseDeath();
                        yield break;
                    }
                }
                yield return null;
                if (globAudioFsm != null) globAudioFsm.enabled = false;
            }
        }

        private void TriggerCorpseDeath()
        {
            deathTriggered = true;
            ForceAudioIdle();
            var fsms = noskInstance.GetComponentsInChildren<PlayMakerFSM>(true);
            var corpse = fsms.FirstOrDefault(f => f != null && string.Equals(f.FsmName, "corpse", StringComparison.OrdinalIgnoreCase));
            var corpseCtrl = fsms.FirstOrDefault(f => f != null && string.Equals(f.FsmName, "Corpse Control", StringComparison.OrdinalIgnoreCase));
            if (corpse != null) corpse.SetState("Blow");
            if (corpseCtrl != null) corpseCtrl.SetState("Blow");
            if (mimicFsm != null) mimicFsm.enabled = false;

            GameManager.instance.StartCoroutine(UntransformAfterExplosion());
        }

        private IEnumerator UntransformAfterExplosion()
        {
            yield return new WaitForSeconds(2f);

            if (isNosk)
            {
                ToggleNoskForm();
            }
        }

        private void SanitizeMimicTransitions(PlayMakerFSM fsm)
        {
            foreach (var st in fsm.Fsm.States)
            {
                var name = st.Name;
                if (name.IndexOf("Roar", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                var keep = new List<HutongGames.PlayMaker.FsmTransition>();
                bool isChargeStart = string.Equals(name, "Charge Start", StringComparison.OrdinalIgnoreCase);
                foreach (var tr in st.Transitions)
                {
                    bool isIdleish = string.Equals(name, "Idle", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(name, "Hollow Idle", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(name, "Wait", StringComparison.OrdinalIgnoreCase);
                    bool isCharge = string.Equals(name, "Charge R", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(name, "Charge L", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(name, "Charge Start", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(name, "Charge Init", StringComparison.OrdinalIgnoreCase);
                    bool toSelf = string.Equals(tr.ToState, name, StringComparison.OrdinalIgnoreCase);
                    bool toIdle = string.Equals(tr.ToState, "Idle", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(tr.ToState, "Hollow Idle", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(tr.ToState, "Wait", StringComparison.OrdinalIgnoreCase);
                    bool isFinished = string.Equals(tr.EventName, "FINISHED", StringComparison.OrdinalIgnoreCase);

                    bool leftRightEvent = string.Equals(tr.EventName, "LEFT", StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(tr.EventName, "RIGHT", StringComparison.OrdinalIgnoreCase);
                    bool toInitLR = string.Equals(tr.ToState, "Init R", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(tr.ToState, "Init L", StringComparison.OrdinalIgnoreCase);

                    if (isChargeStart && (leftRightEvent || toInitLR))
                    {
                        keep.Add(tr);
                        continue;
                    }

                    if (isIdleish || isCharge)
                    {
                        if (toSelf || toIdle || isFinished) keep.Add(tr);
                    }
                    else
                    {
                        if (toIdle || toSelf || isFinished) keep.Add(tr);
                    }
                }
                if (isChargeStart)
                {
                    try
                    {
                        var kept = keep.Select(t => $"{t.EventName}->{t.ToState}").ToArray();
                        Log($"[FSM] Charge Start kept: {string.Join(", ", kept)}");
                    }
                    catch { }
                }
                st.Transitions = keep.ToArray();
            }
        }

        private void SetMimicStateOnce(string state)
        {
            if (mimicFsm == null) return;
            if (state.StartsWith("Trans", System.StringComparison.OrdinalIgnoreCase)) return;
            if (state.Equals("Encountered", System.StringComparison.OrdinalIgnoreCase)) return;
            if (state.Equals("Wake", System.StringComparison.OrdinalIgnoreCase)) return;
            if (lastMimicState == state) return;

            if (state.IndexOf("Roar", System.StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(state, "Idle", System.StringComparison.OrdinalIgnoreCase))
            {
                Log($"[FSM] SetMimicStateOnce -> {state} (from {lastMimicState})");
            }

            mimicFsm.SetState(state);
            lastMimicState = state;
        }

        private IEnumerator SpitRoarSeq(RemoteNosk rn = null)
        {
            if (rn == null)
                attackPlaying = true;
            else
                rn.attackPlaying = true;
            SetNoskState("Spit Antic", rn);
            yield return new WaitForSeconds(0.22f);
            SetNoskState("Spit 1", rn);
            yield return new WaitForSeconds(0.4f);
            SetNoskState("Spit Recover", rn);
            yield return new WaitForSeconds(0.25f);

            SetNoskState("Roar Init", rn);
            yield return new WaitForSeconds(0.35f);
            SetNoskState("Roar Loop", rn);
            yield return new WaitForSeconds(0.1f);

            var emitters = GameObject.FindObjectsOfType<GameObject>().Where(g =>
                g != null && g.name.Contains("Roar") && g.name.Contains("Wave")).ToList();
            currentRoarEmitters.AddRange(emitters);

            yield return new WaitForSeconds(0.5f);
            SetNoskState("Roar Finish", rn);
            yield return new WaitForSeconds(0.35f);

            bool hasRoarEnd = (rn == null) ? HasMimicState("Roar End") : HasState(rn.mimic, "Roar End");
            if (hasRoarEnd)
            {
                SetNoskState("Roar End", rn);
                yield return new WaitForSeconds(0.2f);
            }

            CleanupRoarEmitters();
            SetNoskState("Idle", rn);

            if (rn == null)
                attackPlaying = false;
            else
                rn.attackPlaying = false;
        }

        private bool HasMimicState(string name)
        {
            if (mimicFsm == null || mimicFsm.Fsm == null || mimicFsm.Fsm.States == null) return false;
            var states = mimicFsm.Fsm.States;
            for (int i = 0; i < states.Length; i++)
                if (string.Equals(states[i].Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private bool HasState(PlayMakerFSM fsm, string state)
        {
            if (fsm == null || fsm.Fsm == null || fsm.Fsm.States == null) return false;
            var states = fsm.Fsm.States;
            for (int i = 0; i < states.Length; i++)
                if (string.Equals(states[i].Name, state, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static List<PlayMakerFSM> GetAllFsms()
        {
            try { return new List<PlayMakerFSM>(Resources.FindObjectsOfTypeAll<PlayMakerFSM>()); }
            catch { return new List<PlayMakerFSM>(GameObject.FindObjectsOfType<PlayMakerFSM>()); }
        }

        private void CleanupRoarEmittersNear(Vector3 pos, float radius)
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
                for (int i = 0; i < all.Length; i++)
                {
                    var f = all[i];
                    if (f == null) continue;
                    if (!string.Equals(f.FsmName, "emitter", StringComparison.OrdinalIgnoreCase)) continue;
                    var go = f.gameObject;
                    if (go == null || !go.activeInHierarchy) continue;
                    var n = go.name ?? "";
                    if (n.IndexOf("Roar", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Wave", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    float d2;
                    try { d2 = (go.transform.position - pos).sqrMagnitude; } catch { continue; }
                    if (d2 > radius * radius) continue;

                    if (HasState(f, "End")) f.SetState("End");
                    if (HasState(f, "Destroy")) f.SetState("Destroy");
                    UnityEngine.Object.Destroy(go);
                }
            }
            catch { }
        }

        private void TryRoar()
        {
            if (attackPlaying || mimicFsm == null)
            {
                Log($"[ROAR] TryRoar blocked: attackPlaying={attackPlaying}, mimicFsmNull={mimicFsm == null}");
                return;
            }
            Log("[ROAR] Starting RoarSeq");
            lastAttackStart = Time.time;
            GameManager.instance.StartCoroutine(RoarSeq());
        }

        private IEnumerator RoarSeq()
        {
            attackPlaying = true;
            lastAttackStart = Time.time;

            Log("[ROAR] -> Roar Init");
            SetMimicStateOnce("Roar Init");
            yield return new WaitForSeconds(0.35f);

            Log("[ROAR] -> Roar Loop");
            SetMimicStateOnce("Roar Loop");
            yield return new WaitForSeconds(0.6f);

            Log("[ROAR] -> Roar Finish");
            SetMimicStateOnce("Roar Finish");
            yield return new WaitForSeconds(0.35f);

            if (HasMimicState("Roar End"))
            {
                Log("[ROAR] -> Roar End");
                SetMimicStateOnce("Roar End");
                yield return new WaitForSeconds(0.2f);
            }

            if (noskInstance != null) CleanupRoarEmittersNear(noskInstance.transform.position, 60f);

            Log("[ROAR] -> Idle");
            SetMimicStateOnce("Idle");
            attackPlaying = false;
        }

        private void TrySpitIndex(int i)
        {
            if (attackPlaying || mimicFsm == null) return;
            lastAttackStart = Time.time;
            GameManager.instance.StartCoroutine(SpitSeq(i));
        }

        private IEnumerator SpitSeq(int i)
        {
            attackPlaying = true;
            lastAttackStart = Time.time;
            SetMimicStateOnce("Spit Antic");
            yield return new WaitForSeconds(0.22f);
            string target = i switch { 1 => "Spit 1", _ => "Spit 1" };
            SetMimicStateOnce(target);
            yield return new WaitForSeconds(0.4f);
            SetMimicStateOnce("Spit Recover");
            yield return new WaitForSeconds(0.25f);
            SetMimicStateOnce("Idle");
            attackPlaying = false;
        }

        private void TryJumpAttack()
        {
            if (attackPlaying || mimicFsm == null) return;
            lastAttackStart = Time.time;
            GameManager.instance.StartCoroutine(JumpSeq());
        }

        private IEnumerator JumpSeq()
        {
            attackPlaying = true;
            lastAttackStart = Time.time;

            var heroPos = HeroController.instance != null ? HeroController.instance.transform.position : Vector3.zero;
            lockJumpX = true;
            lockJumpXValue = heroPos.x;
            jumpLockUntil = Time.time + JumpBoostDuration + 0.2f;

            ForceFacing(lastFacingRight);
            yield return null;
            SetMimicStateOnce("Charge Init");
            yield return new WaitForSeconds(0.05f);
            ForceFacing(lastFacingRight);
            StartJumpBoost();
            SetMimicStateOnce("Jump Antic");
            yield return new WaitForSeconds(0.2f);
            SetMimicStateOnce("Launch");
            yield return new WaitForSeconds(0.15f);
            SetMimicStateOnce("Rising");
            yield return new WaitForSeconds(0.25f);
            SetMimicStateOnce("Falling");
            yield return new WaitForSeconds(0.25f);
            SetMimicStateOnce("Land 2");
            yield return new WaitForSeconds(0.15f);
            SetMimicStateOnce("Idle");
            attackPlaying = false;
        }

        private void StartJumpBoost()
        {
            if (jumpBoostCoro != null) GameManager.instance.StopCoroutine(jumpBoostCoro);
            jumpBoostCoro = GameManager.instance.StartCoroutine(JumpBoostArc());
        }

        private IEnumerator JumpBoostArc()
        {
            float t = 0f;
            while (t < JumpBoostDuration)
            {
                float u = t / JumpBoostDuration;
                float y = 4f * JumpBoostHeight * u * (1f - u);
                dynamicOffset = new Vector3(0f, y, 0f);
                t += Time.deltaTime;
                yield return null;
            }
            dynamicOffset = Vector3.zero;
            jumpBoostCoro = null;
        }

        private void TryStrike()
        {
            if (attackPlaying || mimicFsm == null) return;
            GameManager.instance.StartCoroutine(StrikeSeq());
        }

        private IEnumerator StrikeSeq()
        {
            attackPlaying = true;
            SetMimicStateOnce("C Antic 2");
            yield return new WaitForSeconds(0.12f);
            SetMimicStateOnce("C Antic 3");
            yield return new WaitForSeconds(0.10f);
            SetMimicStateOnce("Idle");
            attackPlaying = false;
        }

        private void CacheAnimClips()
        {
            idleClipName = null;
            walkClipName = null;

            if (noskAnimator == null || noskAnimator.Library == null || noskAnimator.Library.clips == null) return;

            var names = noskAnimator.Library.clips.Where(c => c != null && !string.IsNullOrEmpty(c.name)).Select(c => c.name).ToList();

            idleClipName = names.FirstOrDefault(n => string.Equals(n, "Idle", StringComparison.OrdinalIgnoreCase))
                           ?? names.FirstOrDefault(n => n.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0);

            walkClipName =
                names.FirstOrDefault(n => string.Equals(n, "Charge", StringComparison.OrdinalIgnoreCase)) ??
                names.FirstOrDefault(n => n.IndexOf("Charge R", StringComparison.OrdinalIgnoreCase) >= 0) ??
                names.FirstOrDefault(n => n.IndexOf("Charge L", StringComparison.OrdinalIgnoreCase) >= 0) ??
                names.FirstOrDefault(n => n.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0) ??
                names.FirstOrDefault(n => n.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0) ??
                names.FirstOrDefault(n => n.IndexOf("Move", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggle)
        {
            if (mainMenu == null)
            {
                mainMenu = new Menu(ModName, new Element[]
                {
                    new HorizontalOption("Enable Mod","",new[] { "Off", "On" }, i => { Settings.Enabled = (i == 1); }, () => Settings.Enabled ? 1 : 0),
                    new MenuButton("Edit Keybinds","",(btn)=>{ lastParent = modListMenu; UIManager.instance.UIGoToDynamicMenu(GetKeybindsMenu(modListMenu)); }),
                    new MenuButton("Fix Mod","",(btn)=>{ FixMod(); })
                });
            }
            return mainMenu.GetMenuScreen(modListMenu);
        }

        private int DeadzoneToIndex(float v)
        {
            if (v < 0.375f) return 0;
            if (v < 0.625f) return 1;
            return 2;
        }

        private MenuScreen GetKeybindsMenu(MenuScreen parent)
        {
            bindButtons.Clear();
            keybindsMenu = new Menu($"{ModName} Keybinds", new Element[]
            {
                BindButton(BindTarget.Toggle,     $"Toggle Transform: {Settings.ToggleKey}"),
                new HorizontalOption("Require Ctrl","",new[]{ "No", "Yes" }, i=> Settings.RequireCtrl=(i==1), ()=> Settings.RequireCtrl?1:0),
                BindButton(BindTarget.MoveLeft,   $"Left Charge: {Settings.MoveLeft}"),
                BindButton(BindTarget.MoveRight,  $"Right Charge: {Settings.MoveRight}"),
                new HorizontalOption("Gamepad Movement","Use left stick for movement", new[]{ "Off","On" },
                    i => Settings.GamepadAxisMovement = (i==1),
                    () => Settings.GamepadAxisMovement?1:0),
                new HorizontalOption("Gamepad Deadzone","", new[]{ "Low","Medium","High" },
                    i => Settings.GamepadDeadzone = i==0 ? 0.25f : (i==1 ? 0.5f : 0.75f),
                    () => DeadzoneToIndex(Settings.GamepadDeadzone)),
                BindButton(BindTarget.Strike,     $"Strike: {Settings.Strike}"),
                BindButton(BindTarget.Roar,       $"Roar: {Settings.Roar}"),
                BindButton(BindTarget.Spit1,      $"Infectious Outburst: {Settings.Spit1}"),
                BindButton(BindTarget.JumpAttack, $"Leap: {Settings.JumpAttack}"),
                BindButton(BindTarget.RoofOn,     $"Roof Mode On: {Settings.RoofOn}"),
                BindButton(BindTarget.RoofOff,    $"Roof Mode Off: {Settings.RoofOff}"),
                new MenuButton("Back","",(btn)=>{ UIManager.instance.UIGoToDynamicMenu(mainMenu.GetMenuScreen(parent)); })
            });
            return keybindsMenu.GetMenuScreen(parent);
        }

        private MenuButton BindButton(BindTarget target, string label)
        {
            var btn = new MenuButton(label, "", (b) => StartBind(target));
            bindButtons[target] = btn;
            return btn;
        }

        private void StartBind(BindTarget target)
        {
            currentBind = target;
            if (bindButtons.TryGetValue(target, out var btn)) btn.Name = ToListeningLabel(target);
            if (keybindsMenu != null && lastParent != null) UIManager.instance.UIGoToDynamicMenu(keybindsMenu.GetMenuScreen(lastParent));
            ClearUiSelection();
            EnsureUiNavDisabled();
            listenSuppressFrames = 2;
            listenStartTime = Time.unscaledTime;
        }

        private string ToListeningLabel(BindTarget target)
        {
            string pretty = target switch
            {
                BindTarget.Toggle => "Toggle Transform",
                BindTarget.MoveLeft => "Left Charge",
                BindTarget.MoveRight => "Right Charge",
                BindTarget.Strike => "Strike",
                BindTarget.Roar => "Roar",
                BindTarget.Spit1 => "Infectious Outburst",
                BindTarget.JumpAttack => "Leap",
                BindTarget.RoofOn => "Roof Mode On",
                BindTarget.RoofOff => "Roof Mode Off",
                _ => target.ToString()
            };
            return $"{pretty}: Press any key or controller button...";
        }

        private void CaptureKeybindIfWaiting()
        {
            if (currentBind == BindTarget.None) return;
            if (listenSuppressFrames > 0) { listenSuppressFrames--; return; }
            if (TryGetAllowedKeyDown(out var captured))
            {
                switch (currentBind)
                {
                    case BindTarget.Toggle: Settings.ToggleKey = captured; break;
                    case BindTarget.MoveLeft: Settings.MoveLeft = captured; break;
                    case BindTarget.MoveRight: Settings.MoveRight = captured; break;
                    case BindTarget.Strike: Settings.Strike = captured; break;
                    case BindTarget.Roar: Settings.Roar = captured; break;
                    case BindTarget.Spit1: Settings.Spit1 = captured; break;
                    case BindTarget.JumpAttack: Settings.JumpAttack = captured; break;
                    case BindTarget.RoofOn: Settings.RoofOn = captured; break;
                    case BindTarget.RoofOff: Settings.RoofOff = captured; break;
                }

                if (bindButtons.TryGetValue(currentBind, out var btn))
                {
                    string label = currentBind switch
                    {
                        BindTarget.Toggle => $"Toggle Transform: {Settings.ToggleKey}",
                        BindTarget.MoveLeft => $"Left Charge: {Settings.MoveLeft}",
                        BindTarget.MoveRight => $"Right Charge: {Settings.MoveRight}",
                        BindTarget.Strike => $"Strike: {Settings.Strike}",
                        BindTarget.Roar => $"Roar: {Settings.Roar}",
                        BindTarget.Spit1 => $"Infectious Outburst: {Settings.Spit1}",
                        BindTarget.JumpAttack => $"Leap: {Settings.JumpAttack}",
                        BindTarget.RoofOn => $"Roof Mode On: {Settings.RoofOn}",
                        BindTarget.RoofOff => $"Roof Mode Off: {Settings.RoofOff}",
                        _ => btn.Name
                    };
                    btn.Name = label;
                }

                currentBind = BindTarget.None;
                RestoreUiNav();
                if (keybindsMenu != null && lastParent != null) UIManager.instance.UIGoToDynamicMenu(keybindsMenu.GetMenuScreen(lastParent));
                return;
            }
        }

        private static KeyCode[] BuildJoyButtons()
        {
            var arr = new KeyCode[30];
            for (int i = 0; i < 30; i++) arr[i] = (KeyCode)((int)KeyCode.JoystickButton0 + i);
            return arr;
        }

        private static KeyCode[] BuildAllowedKeys()
        {
            var list = new List<KeyCode>();

            for (int i = (int)KeyCode.A; i <= (int)KeyCode.Z; i++) list.Add((KeyCode)i);
            for (int i = (int)KeyCode.Alpha0; i <= (int)KeyCode.Alpha9; i++) list.Add((KeyCode)i);
            for (int i = (int)KeyCode.Keypad0; i <= (int)KeyCode.Keypad9; i++) list.Add((KeyCode)i);
            for (int i = (int)KeyCode.F1; i <= (int)KeyCode.F15; i++) list.Add((KeyCode)i);

            list.Add(KeyCode.Space);
            list.Add(KeyCode.Tab);
            list.Add(KeyCode.Return);
            list.Add(KeyCode.KeypadEnter);
            list.Add(KeyCode.Escape);
            list.Add(KeyCode.Backspace);
            list.Add(KeyCode.Insert);
            list.Add(KeyCode.Delete);
            list.Add(KeyCode.Home);
            list.Add(KeyCode.End);
            list.Add(KeyCode.PageUp);
            list.Add(KeyCode.PageDown);
            list.Add(KeyCode.UpArrow);
            list.Add(KeyCode.DownArrow);
            list.Add(KeyCode.LeftArrow);
            list.Add(KeyCode.RightArrow);

            list.Add(KeyCode.LeftShift);
            list.Add(KeyCode.RightShift);
            list.Add(KeyCode.LeftControl);
            list.Add(KeyCode.RightControl);
            list.Add(KeyCode.LeftAlt);
            list.Add(KeyCode.RightAlt);
            list.Add(KeyCode.CapsLock);
            list.Add(KeyCode.Numlock);
            list.Add(KeyCode.ScrollLock);
            list.Add(KeyCode.Print);
            list.Add(KeyCode.Pause);

            list.Add(KeyCode.BackQuote);
            list.Add(KeyCode.Minus);
            list.Add(KeyCode.Equals);
            list.Add(KeyCode.LeftBracket);
            list.Add(KeyCode.RightBracket);
            list.Add(KeyCode.Backslash);
            list.Add(KeyCode.Semicolon);
            list.Add(KeyCode.Quote);
            list.Add(KeyCode.Comma);
            list.Add(KeyCode.Period);
            list.Add(KeyCode.Slash);

            for (int i = 0; i <= 6; i++) list.Add((KeyCode)((int)KeyCode.Mouse0 + i));

            for (int i = 0; i < 30; i++) list.Add((KeyCode)((int)KeyCode.JoystickButton0 + i));

            return list.ToArray();
        }

        private bool TryGetAllowedKeyDown(out KeyCode key)
        {
            for (int i = 0; i < AllowedKeys.Length; i++)
            {
                var kc = AllowedKeys[i];
                if (Input.GetKeyDown(kc)) { key = kc; return true; }
            }
            key = KeyCode.None;
            return false;
        }

        public void OnLoadGlobal(GlobalSettings s) { Settings = s ?? new GlobalSettings(); }
        public GlobalSettings OnSaveGlobal() => Settings;
        private void AggressiveStopRoarEmitters()
        {
            try
            {
                var allFsms = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
                for (int i = 0; i < allFsms.Length; i++)
                {
                    var f = allFsms[i];
                    if (f == null) continue;
                    if (!string.Equals(f.FsmName, "emitter", StringComparison.OrdinalIgnoreCase)) continue;
                    var go = f.gameObject;
                    if (go == null || !go.activeInHierarchy) continue;
                    if (HasState(f, "End")) f.SetState("End");
                }
            }
            catch { }
            try
            {
                if (currentRoarEmitters != null)
                {
                    for (int i = currentRoarEmitters.Count - 1; i >= 0; i--)
                    {
                        var go = currentRoarEmitters[i];
                        if (go == null) continue;
                        var f = go.GetComponent<PlayMakerFSM>();
                        if (f != null && string.Equals(f.FsmName, "emitter", StringComparison.OrdinalIgnoreCase))
                        {
                            if (HasState(f, "End")) f.SetState("End");
                        }
                        UnityEngine.Object.Destroy(go);
                    }
                    currentRoarEmitters.Clear();
                }
            }
            catch { }
        }
        private void FixMod()
        {
            try
            {
                if (!isNosk)
                {
                    if (noskInstance != null) UnityEngine.Object.Destroy(noskInstance);
                    noskInstance = null;
                    noskAnimator = null;
                    mimicFsm = null;
                    globAudioFsm = null;
                    noskHM = null;
                    attackPlaying = false;
                    roofMode = false;
                    lastMoving = false;
                    lastFacingRight = true;
                    chargeShiftTimer = -1f;
                    CancelMoveWarn();
                    lastMimicState = null;
                    idleClipName = null;
                    walkClipName = null;
                    dynamicOffset = Vector3.zero;
                    if (jumpBoostCoro != null) { GameManager.instance.StopCoroutine(jumpBoostCoro); jumpBoostCoro = null; }
                    if (deathWatchCoro != null) { GameManager.instance.StopCoroutine(deathWatchCoro); deathWatchCoro = null; }
                    deathTriggered = false;
                    PlayerData.instance.isInvincible = false;
                    lastPadAxisX = 0f;
                    if (originalMoveSpeed > 0f)
                    {
                        HeroController.instance.RUN_SPEED = originalMoveSpeed;
                        HeroController.instance.WALK_SPEED = originalMoveSpeed * 0.55f;
                        originalMoveSpeed = -1f;
                    }
                    lastNoskHp = -1;
                    DestroyNoskHitbox();
                    if (HeroController.instance != null) HeroController.instance.RegainControl();
                    AggressiveStopRoarEmitters();
                    return;
                }

                if (HeroController.instance != null) HeroController.instance.RegainControl();

                attackPlaying = false;
                localIntroRunning = false;
                suppressMoveState = false;
                chargeShiftTimer = -1f;
                CancelMoveWarn();
                dynamicOffset = Vector3.zero;
                if (jumpBoostCoro != null) { GameManager.instance.StopCoroutine(jumpBoostCoro); jumpBoostCoro = null; }

                if (mimicFsm != null)
                {
                    SanitizeMimicTransitions(mimicFsm);
                    var st = mimicFsm.ActiveStateName;
                    if (!string.IsNullOrEmpty(st))
                    {
                        if (st.IndexOf("Roar", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            mimicFsm.SetState("Roar End");
                            mimicFsm.Fsm.Event("FINISHED");
                        }
                        else if (st.StartsWith("Trans", StringComparison.OrdinalIgnoreCase) || string.Equals(st, "GG Pause", StringComparison.OrdinalIgnoreCase) || string.Equals(st, "GG Activate", StringComparison.OrdinalIgnoreCase) || string.Equals(st, "Hollow Idle", StringComparison.OrdinalIgnoreCase))
                        {
                            mimicFsm.SetState("Idle");
                        }
                    }
                    mimicFsm.SetState("Idle");
                    lastMimicState = "Idle";
                }

                if (globAudioFsm != null) globAudioFsm.SetState("Idle");

                if (noskAnimator != null && !string.IsNullOrEmpty(idleClipName))
                {
                    noskAnimator.Play(idleClipName);
                }

                ForceAudioIdle();
                AggressiveStopRoarEmitters();

                foreach (var kv in remoteNosks)
                {
                    var rn = kv.Value;
                    if (rn == null) continue;

                    rn.attackPlaying = false;
                    rn.dynamicOffset = Vector3.zero;
                    if (rn.jumpCoro != null) { GameManager.instance.StopCoroutine(rn.jumpCoro); rn.jumpCoro = null; }

                    if (rn.mimic != null)
                    {
                        SanitizeMimicTransitions(rn.mimic);
                        var rs = rn.mimic.ActiveStateName;
                        if (!string.IsNullOrEmpty(rs))
                        {
                            if (rs.IndexOf("Roar", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                rn.mimic.SetState("Roar End");
                                rn.mimic.Fsm.Event("FINISHED");
                            }
                            else if (rs.StartsWith("Trans", StringComparison.OrdinalIgnoreCase) || string.Equals(rs, "GG Pause", StringComparison.OrdinalIgnoreCase) || string.Equals(rs, "GG Activate", StringComparison.OrdinalIgnoreCase) || string.Equals(rs, "Hollow Idle", StringComparison.OrdinalIgnoreCase))
                            {
                                rn.mimic.SetState("Idle");
                            }
                        }
                        rn.mimic.SetState("Idle");
                        rn.lastState = "Idle";
                    }

                    if (rn.glob != null) rn.glob.SetState("Idle");
                    if (rn.anim != null) rn.anim.enabled = true;
                }
            }
            catch { }
        }

        private void UpdateRemoteNoskClones()
        {
            if (remoteNosks.Count == 0) return;

            foreach (var kv in remoteNosks)
            {
                var playerId = kv.Key;
                var rn = kv.Value;
                if (rn?.go == null) continue;

                if (rn.healthManager != null && !rn.deathTriggered)
                {
                    if (rn.healthManager.hp <= 0)
                    {
                        TriggerRemoteNoskDeath(playerId, rn);
                        continue;
                    }
                }

                if (TryGetRemoteHeroPosition(playerId, out var heroPos))
                {
                    rn.go.transform.position = heroPos + BaseOffset + PixelOffset + rn.dynamicOffset;
                }
            }
        }

        private bool TryGetRemoteHeroPosition(ushort playerId, out Vector3 pos)
        {
            pos = default;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType("Nosk_Transformation.HKMP.NoskClientAddon");
                    if (t == null) continue;
                    var m = t.GetMethod("TryGetRemoteHeroPosition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (m != null)
                    {
                        object[] args = { playerId, null };
                        var ok = (bool)m.Invoke(null, args);
                        if (ok)
                        {
                            pos = (Vector3)args[1];
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                var transforms = GameObject.FindObjectsOfType<Transform>();
                string pidStr = playerId.ToString();
                foreach (var t in transforms)
                {
                    var go = t?.gameObject;
                    if (go == null || !go.activeInHierarchy) continue;
                    string n = go.name;
                    if (string.IsNullOrEmpty(n)) continue;

                    bool looksKnight = n.IndexOf("Knight", StringComparison.OrdinalIgnoreCase) >= 0
                                    || n.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0
                                    || n.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (looksKnight && n.IndexOf(pidStr, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        pos = t.position;
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }
        private void SpawnRemoteNosk(ushort playerId, bool facingRight, bool playIntro)
        {
            ushort localId = GetLocalPlayerId();
            if (localId != ushort.MaxValue && playerId == localId) return;
            if (noskPrefab == null) return;

            DestroyRemoteNosk(playerId);

            var go = UnityEngine.Object.Instantiate(noskPrefab);

            var rb = go.GetComponent<Rigidbody2D>();
            if (rb != null) rb.isKinematic = true;

            go.layer = (int)GlobalEnums.PhysLayers.ENEMIES;

            go.transform.localScale = facingRight ? new Vector3(-1, 1, 1) : new Vector3(1, 1, 1);

            var anim = go.GetComponent<tk2dSpriteAnimator>() ?? go.GetComponentInChildren<tk2dSpriteAnimator>(true);

            if (TryGetRemoteHeroPosition(playerId, out var pos))
                go.transform.position = pos + BaseOffset + PixelOffset;

            go.SetActive(true);

            var rn = new RemoteNosk
            {
                go = go,
                anim = anim,
                facingRight = facingRight,
                dynamicOffset = Vector3.zero
            };

            (rn.idleClip, rn.walkClip) = GetAnimClipsFrom(anim);

            remoteNosks[playerId] = rn;

            GameManager.instance.StartCoroutine(InitRemoteNoskSynced(playerId, rn, facingRight, playIntro));
            GameManager.instance.StartCoroutine(HideRemotePlayerAggressively(playerId));
        }

        private void DestroyRemoteNosk(ushort playerId)
        {
            if (!remoteNosks.TryGetValue(playerId, out var rn)) return;

            if (rn.remoteHeroGO != null)
            {
                foreach (var rend in rn.remoteHeroGO.GetComponentsInChildren<Renderer>(true))
                {
                    if (rend != null) rend.enabled = true;
                }
            }

            if (rn.jumpCoro != null) { GameManager.instance.StopCoroutine(rn.jumpCoro); rn.jumpCoro = null; }
            if (rn.moveWarnCoro != null) { GameManager.instance.StopCoroutine(rn.moveWarnCoro); rn.moveWarnCoro = null; }
            rn.suppressMoveState = false;
            if (rn.go != null) UnityEngine.Object.Destroy(rn.go);
            remoteNosks.Remove(playerId);
        }

        private (string idle, string walk) GetAnimClipsFrom(tk2dSpriteAnimator animator)
        {
            string idle = null, walk = null;
            if (animator?.Library?.clips != null)
            {
                var names = animator.Library.clips.Where(c => c != null && !string.IsNullOrEmpty(c.name))
                                                  .Select(c => c.name).ToList();

                idle = names.FirstOrDefault(n => string.Equals(n, "Idle", StringComparison.OrdinalIgnoreCase))
                     ?? names.FirstOrDefault(n => n.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0);

                walk =
                    names.FirstOrDefault(n => string.Equals(n, "Charge", StringComparison.OrdinalIgnoreCase)) ??
                    names.FirstOrDefault(n => n.IndexOf("Charge R", StringComparison.OrdinalIgnoreCase) >= 0) ??
                    names.FirstOrDefault(n => n.IndexOf("Charge L", StringComparison.OrdinalIgnoreCase) >= 0) ??
                    names.FirstOrDefault(n => n.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0) ??
                    names.FirstOrDefault(n => n.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0) ??
                    names.FirstOrDefault(n => n.IndexOf("Move", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return (idle, walk);
        }
        private IEnumerator JoinPoolWhenReady()
        {
            float t = 0f;
            ushort id = ushort.MaxValue;
            while ((id = GetLocalPlayerId()) == ushort.MaxValue || noskHM == null || noskHM.hp <= 0)
            {
                t += Time.deltaTime;
                if (t > 5f) yield break;
                yield return null;
            }
            NoskClientNet.SendJoinPool(id, noskHM.hp);
        }
        private void ApplyFacingVisualRemote(RemoteNosk rn, bool faceRight)
        {
            if (rn?.go == null) return;
            rn.go.transform.localScale = faceRight ? new Vector3(-1, 1, 1) : new Vector3(1, 1, 1);
        }

        private void ForceFacingRemote(RemoteNosk rn, bool faceRight)
        {
            if (rn?.go != null) rn.go.transform.localScale = faceRight ? new Vector3(-1, 1, 1) : new Vector3(1, 1, 1);
            if (rn?.mimic != null) rn.mimic.SetState(faceRight ? "Face R" : "Face L");
        }

        private void SetRemoteStateOnce(RemoteNosk rn, string state)
        {
            if (rn?.mimic == null) return;
            if (state.StartsWith("Trans", StringComparison.OrdinalIgnoreCase)) return;
            if (state.Equals("Encountered", StringComparison.OrdinalIgnoreCase)) return;
            if (state.Equals("Wake", StringComparison.OrdinalIgnoreCase)) return;
            if (rn.lastState == state) return;

            rn.mimic.SetState(state);
            rn.lastState = state;
        }

        private IEnumerator InitRemoteNoskSynced(ushort playerId, RemoteNosk rn, bool facingRight, bool playIntro)
        {
            if (!remoteNosks.ContainsKey(playerId) || rn?.go == null) yield break;

            var fsms = rn.go.GetComponentsInChildren<PlayMakerFSM>(true);
            rn.mimic = System.Linq.Enumerable.FirstOrDefault(fsms, f => f != null && string.Equals(f.FsmName, "Mimic Spider", System.StringComparison.OrdinalIgnoreCase));
            rn.glob = System.Linq.Enumerable.FirstOrDefault(fsms, f => f != null && string.Equals(f.FsmName, "Glob Audio", System.StringComparison.OrdinalIgnoreCase));

            foreach (var fsm in fsms)
            {
                if (fsm == null) continue;
                fsm.enabled = false;
            }

            if (rn.mimic != null)
            {
                var faceState = facingRight ? "Face R" : "Face L";
                rn.mimic.SetState(faceState);
                rn.facingRight = facingRight;
                ApplyFacingVisualRemote(rn, facingRight);
            }

            rn.go.SetActive(true);

            yield return new WaitForSeconds(0.1f);

            foreach (var fsm in fsms)
            {
                if (fsm == null) continue;
                bool isMimic = fsm == rn.mimic;
                bool isGlob = fsm == rn.glob;
                bool isCorpseFSM = IsCorpseFsm(fsm);
                fsm.enabled = isMimic || isGlob || isCorpseFSM;
            }

            yield return null;

            rn.healthManager = rn.go.GetComponent<HealthManager>() ?? rn.go.GetComponentInChildren<HealthManager>(true);
            if (rn.healthManager != null)
            {
                rn.lastHp = rn.healthManager.hp;
                rn.deathTriggered = false;
            }

            if (rn.mimic != null)
            {
                SanitizeMimicTransitions(rn.mimic);

                if (playIntro)
                {
                    rn.mimic.SetState("Trans 1");
                    rn.lastState = "Trans 1";
                    yield return null;
                }
                else
                {
                    rn.mimic.SetState("Idle");
                    rn.lastState = "Idle";
                    yield return null;
                }
            }

            if (rn.glob != null) rn.glob.SetState("Idle");

            if (!playIntro && rn.anim != null && !string.IsNullOrEmpty(rn.idleClip))
            {
                rn.anim.enabled = true;
                rn.anim.Play(rn.idleClip);
            }
        }

        private IEnumerator HideRemotePlayerAggressively(ushort playerId)
        {
            for (int attempts = 0; attempts < 50; attempts++)
            {
                var allObjects = GameObject.FindObjectsOfType<GameObject>();
                string pidStr = playerId.ToString();

                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    var name = obj.name;
                    if (string.IsNullOrEmpty(name)) continue;

                    bool isRemotePlayer = (name.Contains("Knight") || name.Contains("Player") || name.Contains("Remote"))
                                         && name.Contains(pidStr);

                    if (isRemotePlayer)
                    {
                        if (remoteNosks.TryGetValue(playerId, out var rnRef))
                        {
                            rnRef.remoteHeroGO = obj;
                        }

                        var tk2dSprites = obj.GetComponentsInChildren<tk2dSprite>(true);
                        foreach (var tk2d in tk2dSprites)
                        {
                            if (tk2d != null)
                            {
                                var rend = tk2d.GetComponent<Renderer>();
                                if (rend != null) rend.enabled = false;
                            }
                        }

                        var allRenderers = obj.GetComponentsInChildren<Renderer>(true);
                        foreach (var rend in allRenderers)
                        {
                            if (rend != null) rend.enabled = false;
                        }

                        var allSpriteRenderers = obj.GetComponentsInChildren<SpriteRenderer>(true);
                        foreach (var sr in allSpriteRenderers)
                        {
                            if (sr != null) sr.enabled = false;
                        }

                        var allMeshRenderers = obj.GetComponentsInChildren<MeshRenderer>(true);
                        foreach (var mr in allMeshRenderers)
                        {
                            if (mr != null) mr.enabled = false;
                        }

                        yield break;
                    }
                }

                yield return new WaitForSeconds(0.1f);
            }
        }

        private void TriggerRemoteNoskDeath(ushort playerId, RemoteNosk rn)
        {
            rn.deathTriggered = true;

            var fsms = rn.go.GetComponentsInChildren<PlayMakerFSM>(true);
            var corpse = fsms.FirstOrDefault(f => f != null && string.Equals(f.FsmName, "corpse", StringComparison.OrdinalIgnoreCase));
            var corpseCtrl = fsms.FirstOrDefault(f => f != null && string.Equals(f.FsmName, "Corpse Control", StringComparison.OrdinalIgnoreCase));

            if (corpse != null) corpse.SetState("Blow");
            if (corpseCtrl != null) corpseCtrl.SetState("Blow");
            if (rn.mimic != null) rn.mimic.enabled = false;

            GameManager.instance.StartCoroutine(RemoveRemoteNoskAfterExplosion(playerId));
        }

        private IEnumerator RemoveRemoteNoskAfterExplosion(ushort playerId)
        {
            yield return new WaitForSeconds(2f);

            DestroyRemoteNosk(playerId);
        }

        private IEnumerator RemoteRoarSeq(ushort playerId, RemoteNosk rn)
        {
            rn.attackPlaying = true;

            SetRemoteStateOnce(rn, "Roar Init");
            yield return new WaitForSeconds(0.35f);

            SetRemoteStateOnce(rn, "Roar Loop");
            yield return new WaitForSeconds(0.6f);

            SetRemoteStateOnce(rn, "Roar Finish");
            yield return new WaitForSeconds(0.35f);

            if (HasState(rn.mimic, "Roar End"))
            {
                SetRemoteStateOnce(rn, "Roar End");
                yield return new WaitForSeconds(0.2f);
            }

            CleanupRoarEmittersNear(rn.go.transform.position, 60f);

            SetRemoteStateOnce(rn, "Idle");
            rn.attackPlaying = false;
        }

        private IEnumerator RemoteSpitSeq(ushort playerId, RemoteNosk rn, int i)
        {
            rn.attackPlaying = true;

            SetRemoteStateOnce(rn, "Spit Antic");
            yield return new WaitForSeconds(0.22f);

            string target = i switch { 1 => "Spit 1", _ => "Spit 1" };
            SetRemoteStateOnce(rn, target);
            yield return new WaitForSeconds(0.4f);

            SetRemoteStateOnce(rn, "Spit Recover");
            yield return new WaitForSeconds(0.25f);

            SetRemoteStateOnce(rn, "Idle");
            rn.attackPlaying = false;
        }

        private IEnumerator RemoteJumpSeq(ushort playerId, RemoteNosk rn)
        {
            rn.attackPlaying = true;

            ForceFacingRemote(rn, rn.facingRight);
            yield return null;
            SetRemoteStateOnce(rn, "Charge Init");
            yield return new WaitForSeconds(0.05f);

            ForceFacingRemote(rn, rn.facingRight);
            StartRemoteJumpBoost(rn);

            SetRemoteStateOnce(rn, "Jump Antic");
            yield return new WaitForSeconds(0.2f);

            SetRemoteStateOnce(rn, "Launch");
            yield return new WaitForSeconds(0.15f);

            SetRemoteStateOnce(rn, "Rising");
            yield return new WaitForSeconds(0.25f);

            SetRemoteStateOnce(rn, "Falling");
            yield return new WaitForSeconds(0.25f);

            SetRemoteStateOnce(rn, "Land 2");
            yield return new WaitForSeconds(0.15f);

            SetRemoteStateOnce(rn, "Idle");
            rn.attackPlaying = false;
        }

        private IEnumerator RemoteRSJumpSeq(ushort playerId, RemoteNosk rn)
        {
            rn.attackPlaying = true;

            SetRemoteStateOnce(rn, "RS Jump Antic");
            yield return new WaitForSeconds(0.2f);

            SetRemoteStateOnce(rn, "RS Jump");
            yield return new WaitForSeconds(0.35f);

            SetRemoteStateOnce(rn, "Land 2");
            yield return new WaitForSeconds(0.15f);

            SetRemoteStateOnce(rn, "Idle");
            rn.attackPlaying = false;
        }

        private void StartRemoteJumpBoost(RemoteNosk rn)
        {
            if (rn.jumpCoro != null) GameManager.instance.StopCoroutine(rn.jumpCoro);
            rn.jumpCoro = GameManager.instance.StartCoroutine(RemoteJumpBoostArc(rn));
        }

        private IEnumerator RemoteJumpBoostArc(RemoteNosk rn)
        {
            float t = 0f;
            while (t < JumpBoostDuration)
            {
                float u = t / JumpBoostDuration;
                float y = 4f * JumpBoostHeight * u * (1f - u);
                rn.dynamicOffset = new Vector3(0f, y, 0f);
                t += Time.deltaTime;
                yield return null;
            }
            rn.dynamicOffset = Vector3.zero;
            rn.jumpCoro = null;
        }
    }
}
