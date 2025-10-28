#pragma warning disable 1591
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using Modding;
using HutongGames.PlayMaker;
using UnityEngine;
using Satchel.BetterMenus;
using On;
using Hkmp.Api.Client;
using Hkmp.Api.Server;
using Nosk_Transformation.HKMP;
using Nosk_Transformation.HKMP.Shared;

namespace Nosk_Transformation
{
    public class Nosk_Transformation : Mod, ICustomMenuMod, IGlobalSettings<Nosk_Transformation.GlobalSettings>
    {
        internal static Nosk_Transformation Instance;
        private static readonly string ModName = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().ManifestModule.Name);

        public class GlobalSettings
        {
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
        }

        private GlobalSettings Settings = new GlobalSettings();

        private const string NoskSceneName = "Deepnest_32";
        private const string NoskObjectName = "Mimic Spider";

        private static readonly Vector3 BaseOffset = new Vector3(0f, 1.4f, -0.46f);
        private static readonly Vector3 PixelOffset = new Vector3(0f, 0.06f, -0.10f);

        private GameObject noskInstance;
        private GameObject noskPrefab;
        private bool isNosk = false;

        private tk2dSpriteAnimator noskAnimator;
        private PlayMakerFSM mimicFsm;
        private PlayMakerFSM globAudioFsm;

        private bool attackPlaying = false;
        private bool roofMode = false;
        private bool lastMoving = false;
        private bool lastFacingRight = true;

        private float chargeShiftTimer = -1f;
        private Coroutine moveWarnCoro;
        private bool suppressMoveState;

        private string lastMimicState;
        private string idleClipName;
        private string walkClipName;

        private Vector3 dynamicOffset = Vector3.zero;
        private Coroutine jumpBoostCoro;
        private const float JumpBoostHeight = 1.2f;
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

        private const float RoarCaptureDelay = 0.10f;
        private const float RoarAutoKillDelay = 3.00f; // no longer used for timing
        private static readonly string[] RoarEmitterKeywords = { "Roar", "Wave" };
        private static readonly string EmitterFsmName = "emitter";

        private readonly Dictionary<GameObject, float> trackedRoarEmitters = new Dictionary<GameObject, float>(64);
        private bool allowRoarCleanup = false;

        private HashSet<int> roarSpawnSnapshot;

        // Remote clones (HKMP)
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
        }
        private readonly Dictionary<ushort, RemoteNosk> remoteNosks = new Dictionary<ushort, RemoteNosk>();

        public override int LoadPriority() => 1;
        public Nosk_Transformation() : base(ModName) { }
        public override string GetVersion() => "1.1.0";
        public bool ToggleButtonInsideMenu => false;

        public override List<(string, string)> GetPreloadNames()
        {
            return new List<(string, string)> { (NoskSceneName, NoskObjectName) };
        }

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            Instance = this;

            if (preloadedObjects.TryGetValue(NoskSceneName, out var scene) && scene.TryGetValue(NoskObjectName, out var prefab))
            {
                noskPrefab = prefab;
                UnityEngine.Object.DontDestroyOnLoad(noskPrefab);
                noskPrefab.SetActive(false);
            }

            ModHooks.HeroUpdateHook += OnHeroUpdate;
            On.HealthManager.Hit += HealthManager_Hit;

            try { ClientAddon.RegisterAddon(new NoskClientAddon()); } catch { }
            try { ServerAddon.RegisterAddon(new NoskServerAddon()); } catch { }
            NoskClientNet.ToggleReceived += OnRemoteToggle;
            NoskClientNet.AttackReceived += OnRemoteAttack;
        }

        private void HealthManager_Hit(On.HealthManager.orig_Hit orig, HealthManager self, HitInstance hit)
        {
            try
            {
                if (isNosk && noskInstance != null)
                {
                    bool isOurNosk = self != null && (self.gameObject == noskInstance || self.gameObject.transform.IsChildOf(noskInstance.transform));
                    if (isOurNosk) { hit.DamageDealt = 0; hit.Multiplier = 0f; }
                }
            }
            catch { }
            orig(self, hit);
        }

        public void OnHeroUpdate()
        {
            // Always update remote clones so viewers see others even with mod "disabled"
            UpdateRemoteNoskClones();

            CaptureKeybindIfWaiting();
            if (!Settings.Enabled) return;

            bool togglePressed = Input.GetKeyDown(Settings.ToggleKey) && (!Settings.RequireCtrl || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            if (togglePressed) ToggleNoskForm();

            if (!isNosk || noskInstance == null) return;

            noskInstance.transform.position = HeroController.instance.transform.position + BaseOffset + PixelOffset + dynamicOffset;

            // old per-frame 3s cleanup removed

            if (!HeroController.instance.acceptingInput || attackPlaying) return;

            bool left = Input.GetKey(Settings.MoveLeft) || Input.GetKey(KeyCode.LeftArrow);
            bool right = Input.GetKey(Settings.MoveRight) || Input.GetKey(KeyCode.RightArrow);
            bool leftDown = Input.GetKeyDown(Settings.MoveLeft) || Input.GetKeyDown(KeyCode.LeftArrow);
            bool rightDown = Input.GetKeyDown(Settings.MoveRight) || Input.GetKeyDown(KeyCode.RightArrow);
            bool moving = left || right;

            bool faceRight = lastFacingRight;
            if (right && !left) faceRight = true;
            else if (left && !right) faceRight = false;

            ApplyFacingVisual(faceRight);

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
            }
            else
            {
                chargeShiftTimer = -1f;
                CancelMoveWarn();
                if (lastMoving) GameManager.instance.StartCoroutine(SafeIdlePulse());
                if (noskAnimator != null && !string.IsNullOrEmpty(idleClipName))
                    if (!noskAnimator.IsPlaying(idleClipName)) noskAnimator.Play(idleClipName);
                ForceAudioIdle();
            }

            lastFacingRight = faceRight;
            lastMoving = moving;

            if (Input.GetKeyDown(Settings.Strike)) { chargeShiftTimer = -1f; CancelMoveWarn(); ForceAudioIdle(); TryStrike(); }

            if (Input.GetKeyDown(Settings.Spit1)) { chargeShiftTimer = -1f; CancelMoveWarn(); ForceAudioIdle(); TrySpitIndex(1); NoskClientNet.SendAttack(NetAttack.Spit1, lastFacingRight); }
            if (Input.GetKeyDown(Settings.Roar)) { chargeShiftTimer = -1f; CancelMoveWarn(); TryRoar(); NoskClientNet.SendAttack(NetAttack.Roar, lastFacingRight); }
            if (Input.GetKeyDown(Settings.JumpAttack)) { chargeShiftTimer = -1f; CancelMoveWarn(); TryJumpAttack(); NoskClientNet.SendAttack(NetAttack.Leap, lastFacingRight); }

            if (Input.GetKeyDown(Settings.RoofOn)) roofMode = true;
            if (Input.GetKeyDown(Settings.RoofOff)) roofMode = false;

            if (roofMode)
            {
                if (Input.GetKeyDown(Settings.Spit1)) { chargeShiftTimer = -1f; CancelMoveWarn(); ForceAudioIdle(); TryRoofRSJump(); NoskClientNet.SendAttack(NetAttack.RSJump, lastFacingRight); }
            }
        }

        private void OnRemoteToggle(ushort playerId, bool active, bool facingRight)
        {
            try
            {
                if (active) SpawnRemoteNosk(playerId, facingRight);
                else DestroyRemoteNosk(playerId);
            }
            catch { }
        }

        private void OnRemoteAttack(ushort playerId, NetAttack attack, bool facingRight)
        {
            if (!remoteNosks.TryGetValue(playerId, out var rn) || rn?.mimic == null) return;

            rn.facingRight = facingRight;
            ApplyFacingVisualRemote(rn, facingRight);

            if (rn.attackPlaying) return;

            switch (attack)
            {
                case NetAttack.Roar:
                    GameManager.instance.StartCoroutine(RemoteRoarSeq(playerId, rn));
                    break;
                case NetAttack.Spit1:
                    GameManager.instance.StartCoroutine(RemoteSpitSeq(playerId, rn, 1));
                    break;
                case NetAttack.Leap:
                    GameManager.instance.StartCoroutine(RemoteJumpSeq(playerId, rn));
                    break;
                case NetAttack.RSJump:
                    GameManager.instance.StartCoroutine(RemoteRSJumpSeq(playerId, rn));
                    break;
            }
        }

        private void StartMoveWarn()
        {
            CancelMoveWarn();
            moveWarnCoro = GameManager.instance.StartCoroutine(MoveWarnSeq());
        }

        private void CancelMoveWarn()
        {
            if (moveWarnCoro != null) { GameManager.instance.StopCoroutine(moveWarnCoro); moveWarnCoro = null; }
            suppressMoveState = false;
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

        private void ForceAudioIdle()
        {
            if (globAudioFsm != null && globAudioFsm.ActiveStateName != "Idle") globAudioFsm.SetState("Idle");
        }

        private void ApplyFacingVisual(bool faceRight)
        {
            if (noskInstance == null) return;
            noskInstance.transform.localScale = faceRight ? new Vector3(-1, 1, 1) : new Vector3(1, 1, 1);
        }

        private void ForceFacing(bool faceRight)
        {
            if (noskInstance != null) noskInstance.transform.localScale = faceRight ? new Vector3(-1, 1, 1) : new Vector3(1, 1, 1);
            if (mimicFsm != null) mimicFsm.SetState(faceRight ? "Face R" : "Face L");
        }

        private IEnumerator SafeIdlePulse()
        {
            SetMimicStateOnce("Wait");
            yield return null;
            SetMimicStateOnce("Idle");
            ForceAudioIdle();
            yield return null;
        }

        private void ToggleNoskForm()
        {
            if (noskPrefab == null) return;

            isNosk = !isNosk;
            NoskClientNet.SendToggle(isNosk, lastFacingRight);

            var heroRenderer = HeroController.instance.gameObject.GetComponent<Renderer>();

            if (isNosk)
            {
                allowRoarCleanup = false;
                roarSpawnSnapshot = null;

                PlayerData.instance.isInvincible = true;
                if (heroRenderer != null) heroRenderer.enabled = false;

                noskInstance = UnityEngine.Object.Instantiate(noskPrefab);

                var rb = noskInstance.GetComponent<Rigidbody2D>();
                if (rb != null) rb.isKinematic = true;

                noskInstance.layer = (int)GlobalEnums.PhysLayers.HERO_BOX;

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

                lastFacingRight = spawnRight;
                ApplyFacingVisual(spawnRight);

                noskAnimator = noskInstance.GetComponent<tk2dSpriteAnimator>() ?? noskInstance.GetComponentInChildren<tk2dSpriteAnimator>(true);
                CacheAnimClips();

                noskInstance.SetActive(true);

                GameManager.instance.StartCoroutine(MakeBraindeadOnRegainControl());
            }
            else
            {
                PlayerData.instance.isInvincible = false;
                if (heroRenderer != null) heroRenderer.enabled = true;
                if (HeroController.instance != null) HeroController.instance.RegainControl();
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
                trackedRoarEmitters.Clear();
                allowRoarCleanup = false;
                roarSpawnSnapshot = null;
            }
        }

        private IEnumerator MakeBraindeadOnRegainControl()
        {
            yield return new WaitForSeconds(3f);
            yield return new WaitUntil(() => HeroController.instance.acceptingInput);

            if (!isNosk || noskInstance == null) yield break;

            var fsms = noskInstance.GetComponentsInChildren<PlayMakerFSM>(true);
            mimicFsm = fsms.FirstOrDefault(f => f != null && string.Equals(f.FsmName, "Mimic Spider", StringComparison.OrdinalIgnoreCase));
            globAudioFsm = fsms.FirstOrDefault(f => f != null && string.Equals(f.FsmName, "Glob Audio", StringComparison.OrdinalIgnoreCase));

            foreach (var fsm in fsms)
            {
                if (fsm == null) continue;
                bool isMimic = fsm == mimicFsm;
                bool isGlob = fsm == globAudioFsm;
                bool isCorpseFSM = IsCorpseFsm(fsm);
                fsm.enabled = isMimic || isGlob || isCorpseFSM;
            }

            if (mimicFsm != null)
            {
                SanitizeMimicTransitions(mimicFsm);
                lastMimicState = mimicFsm.ActiveStateName;
                var faceState = lastFacingRight ? "Face R" : "Face L";
                if (mimicFsm.ActiveStateName != faceState) mimicFsm.SetState(faceState);
                yield return null;
                SetMimicStateOnce("Idle");
            }

            ForceAudioIdle();

            noskHM = noskInstance.GetComponent<HealthManager>() ?? noskInstance.GetComponentInChildren<HealthManager>(true);
            deathTriggered = false;
            if (deathWatchCoro != null) GameManager.instance.StopCoroutine(deathWatchCoro);
            deathWatchCoro = GameManager.instance.StartCoroutine(DeathWatch());

            allowRoarCleanup = true;
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
                if (noskHM != null && noskHM.hp <= 0) { TriggerCorpseDeath(); yield break; }
                yield return null;
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
        }

        private void SanitizeMimicTransitions(PlayMakerFSM fsm)
        {
            foreach (var st in fsm.Fsm.States)
            {
                var name = st.Name;
                if (name.IndexOf("Roar", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                var keep = new List<FsmTransition>();
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

                    if (isIdleish || isCharge)
                    {
                        if (toSelf || toIdle || isFinished) keep.Add(tr);
                    }
                    else
                    {
                        if (toIdle || toSelf || isFinished) keep.Add(tr);
                    }
                }
                st.Transitions = keep.ToArray();
            }
        }

        private void SetMimicStateOnce(string state)
        {
            if (mimicFsm == null) return;
            if (state.StartsWith("Trans", StringComparison.OrdinalIgnoreCase)) return;
            if (state.Equals("Encountered", StringComparison.OrdinalIgnoreCase)) return;
            if (state.Equals("Wake", StringComparison.OrdinalIgnoreCase)) return;
            if (lastMimicState == state) return;

            mimicFsm.SetState(state);
            lastMimicState = state;

            if (string.Equals(state, "Idle", StringComparison.OrdinalIgnoreCase))
                CleanupRoarEmittersOnIdle();
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

        private void TrackAndAutoCleanRoarEmitters() { /* legacy timing cleaner (unused) */ }

        private List<GameObject> FindRoarEmittersNearNosk()
        {
            var list = new List<GameObject>();
            if (noskInstance == null) return list;
            var pos = noskInstance.transform.position;
            var fsms = GetAllFsms();
            for (int i = 0; i < fsms.Count; i++)
            {
                var f = fsms[i];
                if (f == null) continue;
                if (!string.Equals(f.FsmName, EmitterFsmName, StringComparison.OrdinalIgnoreCase)) continue;
                var go = f.gameObject;
                if (go == null || !go.activeInHierarchy) continue;
                var n = go.name;
                if (string.IsNullOrEmpty(n)) continue;
                bool looksRoar = false;
                for (int k = 0; k < RoarEmitterKeywords.Length; k++)
                {
                    if (n.IndexOf(RoarEmitterKeywords[k], StringComparison.OrdinalIgnoreCase) >= 0) { looksRoar = true; break; }
                }
                if (!looksRoar) continue;
                float d2;
                try { d2 = (go.transform.position - pos).sqrMagnitude; } catch { continue; }
                if (d2 <= 40f * 40f) list.Add(go);
            }
            return list;
        }

        private void CleanupStaleRoarEmitters()
        {
            var stale = FindRoarEmittersNearNosk();
            for (int i = 0; i < stale.Count; i++)
            {
                var go = stale[i];
                if (go == null) continue;
                var f = go.GetComponent<PlayMakerFSM>();
                if (f != null && string.Equals(f.FsmName, EmitterFsmName, StringComparison.OrdinalIgnoreCase))
                {
                    if (HasState(f, "End")) f.SetState("End");
                    if (HasState(f, "Destroy")) f.SetState("Destroy");
                }
                UnityEngine.Object.Destroy(go);
                trackedRoarEmitters.Remove(go);
            }
        }

        private IEnumerator ScheduleRoarEmittersCleanup() { yield break; } // not used

        private HashSet<int> SnapshotNearIds()
        {
            var set = new HashSet<int>();
            var near = FindRoarEmittersNearNosk();
            for (int i = 0; i < near.Count; i++)
            {
                var go = near[i];
                if (go != null) set.Add(go.GetInstanceID());
            }
            return set;
        }

        private List<GameObject> NewNearSince(HashSet<int> before)
        {
            var list = new List<GameObject>();
            var near = FindRoarEmittersNearNosk();
            for (int i = 0; i < near.Count; i++)
            {
                var go = near[i];
                if (go == null) continue;
                if (!before.Contains(go.GetInstanceID())) list.Add(go);
            }
            return list;
        }

        private void TryRoar()
        {
            if (attackPlaying || mimicFsm == null) return;
            if (allowRoarCleanup) CleanupStaleRoarEmitters();
            roarSpawnSnapshot = allowRoarCleanup ? SnapshotNearIds() : null;
            GameManager.instance.StartCoroutine(RoarSeq());
        }

        private IEnumerator RoarSeq()
        {
            attackPlaying = true;

            SetMimicStateOnce("Roar Init");
            yield return new WaitForSeconds(0.35f);

            SetMimicStateOnce("Roar Loop");
            yield return new WaitForSeconds(0.6f);

            SetMimicStateOnce("Roar Finish");
            yield return new WaitForSeconds(0.35f);

            if (HasMimicState("Roar End"))
            {
                SetMimicStateOnce("Roar End");
                yield return new WaitForSeconds(0.2f);
            }

            SetMimicStateOnce("Idle");
            attackPlaying = false;
        }

        private void CleanupRoarEmittersOnIdle()
        {
            if (!allowRoarCleanup || roarSpawnSnapshot == null) return;

            var born = NewNearSince(roarSpawnSnapshot);
            for (int i = 0; i < born.Count; i++)
            {
                var go = born[i];
                if (go == null) continue;

                var f = go.GetComponent<PlayMakerFSM>();
                if (f != null && string.Equals(f.FsmName, EmitterFsmName, StringComparison.OrdinalIgnoreCase))
                {
                    if (HasState(f, "End")) f.SetState("End");
                    if (HasState(f, "Destroy")) f.SetState("Destroy");
                }

                UnityEngine.Object.Destroy(go);
                trackedRoarEmitters.Remove(go);
            }

            roarSpawnSnapshot = null;
        }

        private void TrySpitIndex(int i)
        {
            if (attackPlaying || mimicFsm == null) return;
            GameManager.instance.StartCoroutine(SpitSeq(i));
        }

        private IEnumerator SpitSeq(int i)
        {
            attackPlaying = true;
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
            GameManager.instance.StartCoroutine(JumpSeq());
        }

        private IEnumerator JumpSeq()
        {
            attackPlaying = true;
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

        private void TryRoofRSJump()
        {
            if (attackPlaying || mimicFsm == null) return;
            GameManager.instance.StartCoroutine(RSJumpSeq());
        }

        private IEnumerator RSJumpSeq()
        {
            attackPlaying = true;
            SetMimicStateOnce("RS Jump Antic");
            yield return new WaitForSeconds(0.2f);
            SetMimicStateOnce("RS Jump");
            yield return new WaitForSeconds(0.35f);
            SetMimicStateOnce("Land 2");
            yield return new WaitForSeconds(0.15f);
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

        private MenuScreen GetKeybindsMenu(MenuScreen parent)
        {
            bindButtons.Clear();
            keybindsMenu = new Menu($"{ModName} Keybinds", new Element[]
            {
                BindButton(BindTarget.Toggle,     $"Toggle Transform: {Settings.ToggleKey}"),
                new HorizontalOption("Require Ctrl","",new[]{ "No", "Yes" }, i=> Settings.RequireCtrl=(i==1), ()=> Settings.RequireCtrl?1:0),
                BindButton(BindTarget.MoveLeft,   $"Left Charge: {Settings.MoveLeft}"),
                BindButton(BindTarget.MoveRight,  $"Right Charge: {Settings.MoveRight}"),
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
            return $"{pretty}: Press any key...";
        }

        private void CaptureKeybindIfWaiting()
        {
            if (currentBind == BindTarget.None) return;
            if (!TryGetAnyKeyDown(out var captured)) return;

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
            if (keybindsMenu != null && lastParent != null) UIManager.instance.UIGoToDynamicMenu(keybindsMenu.GetMenuScreen(lastParent));
        }

        private bool TryGetAnyKeyDown(out KeyCode key)
        {
            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.None) continue;
                if (Input.GetKeyDown(kc)) { key = kc; return true; }
            }
            key = KeyCode.None;
            return false;
        }

        public void OnLoadGlobal(GlobalSettings s) { Settings = s ?? new GlobalSettings(); }
        public GlobalSettings OnSaveGlobal() => Settings;

        private void FixMod()
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
                trackedRoarEmitters.Clear();
                allowRoarCleanup = false;
                roarSpawnSnapshot = null;
                PlayerData.instance.isInvincible = false;
                return;
            }

            if (noskInstance != null)
            {
                var fsms = noskInstance.GetComponentsInChildren<PlayMakerFSM>(true);
                mimicFsm = fsms.FirstOrDefault(f => f != null && string.Equals(f.FsmName, "Mimic Spider", StringComparison.OrdinalIgnoreCase));
                globAudioFsm = fsms.FirstOrDefault(f => f != null && string.Equals(f.FsmName, "Glob Audio", StringComparison.OrdinalIgnoreCase));

                foreach (var fsm in fsms)
                {
                    if (fsm == null) continue;
                    bool isMimic = fsm == mimicFsm;
                    bool isGlob = fsm == globAudioFsm;
                    bool isCorpseFSM = IsCorpseFsm(fsm);
                    fsm.enabled = isMimic || isGlob || isCorpseFSM;
                }

                if (mimicFsm != null)
                {
                    SanitizeMimicTransitions(mimicFsm);
                    var faceState = lastFacingRight ? "Face R" : "Face L";
                    if (mimicFsm.ActiveStateName != faceState) mimicFsm.SetState(faceState);
                    mimicFsm.SetState("Idle");
                    lastMimicState = "Idle";
                }
                if (globAudioFsm != null) globAudioFsm.SetState("Idle");

                noskAnimator = noskInstance.GetComponent<tk2dSpriteAnimator>() ?? noskInstance.GetComponentInChildren<tk2dSpriteAnimator>(true);

                noskHM = noskInstance.GetComponent<HealthManager>() ?? noskInstance.GetComponentInChildren<HealthManager>(true);
                deathTriggered = false;
                if (deathWatchCoro != null) GameManager.instance.StopCoroutine(deathWatchCoro);
                deathWatchCoro = GameManager.instance.StartCoroutine(DeathWatch());
            }

            attackPlaying = false;
            roofMode = false;
            lastMoving = false;
            lastFacingRight = true;
            chargeShiftTimer = -1f;
            PlayerData.instance.isInvincible = true;
        }

        // Remote helpers

        private void UpdateRemoteNoskClones()
        {
            if (remoteNosks.Count == 0) return;

            foreach (var kv in remoteNosks)
            {
                var playerId = kv.Key;
                var rn = kv.Value;
                if (rn?.go == null) continue;

                if (TryGetRemoteHeroPosition(playerId, out var heroPos))
                {
                    rn.go.transform.position = heroPos + BaseOffset + PixelOffset + rn.dynamicOffset;
                    ApplyFacingVisualRemote(rn, rn.facingRight);
                }
            }
        }

        private bool TryGetRemoteHeroPosition(ushort playerId, out Vector3 pos)
        {
            pos = default;

            // Try calling NoskClientAddon.TryGetRemoteHeroPosition (static) if present
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

            // Heuristic fallback: scan scene for HKMP proxy objects
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

        private void SpawnRemoteNosk(ushort playerId, bool facingRight)
        {
            if (noskPrefab == null) return;

            DestroyRemoteNosk(playerId);

            var go = UnityEngine.Object.Instantiate(noskPrefab);
            go.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(go);

            var rb = go.GetComponent<Rigidbody2D>();
            if (rb != null) rb.isKinematic = true;

            foreach (var col in go.GetComponentsInChildren<Collider2D>(true))
                col.enabled = false;

            go.layer = (int)GlobalEnums.PhysLayers.HERO_BOX;

            var anim = go.GetComponent<tk2dSpriteAnimator>() ?? go.GetComponentInChildren<tk2dSpriteAnimator>(true);
            var fsms = go.GetComponentsInChildren<PlayMakerFSM>(true);
            var mimic = fsms.FirstOrDefault(f => f != null && string.Equals(f.FsmName, "Mimic Spider", StringComparison.OrdinalIgnoreCase));
            var glob = fsms.FirstOrDefault(f => f != null && string.Equals(f.FsmName, "Glob Audio", StringComparison.OrdinalIgnoreCase));

            foreach (var fsm in fsms)
            {
                if (fsm == null) continue;
                bool isMimic = fsm == mimic;
                bool isGlob = fsm == glob;
                bool isCorpse = IsCorpseFsm(fsm);
                fsm.enabled = isMimic || isGlob || isCorpse;
            }

            var rn = new RemoteNosk
            {
                go = go,
                anim = anim,
                mimic = mimic,
                glob = glob,
                facingRight = facingRight,
                dynamicOffset = Vector3.zero
            };

            (rn.idleClip, rn.walkClip) = GetAnimClipsFrom(anim);

            ApplyFacingVisualRemote(rn, facingRight);

            if (TryGetRemoteHeroPosition(playerId, out var pos))
                go.transform.position = pos + BaseOffset + PixelOffset;

            go.SetActive(true);

            if (rn.mimic != null)
            {
                SanitizeMimicTransitions(rn.mimic);
                ForceFacingRemote(rn, facingRight);
                rn.mimic.SetState("Idle");
                rn.lastState = "Idle";
            }

            if (rn.glob != null) rn.glob.SetState("Idle");

            remoteNosks[playerId] = rn;
        }

        private void DestroyRemoteNosk(ushort playerId)
        {
            if (!remoteNosks.TryGetValue(playerId, out var rn)) return;

            if (rn.jumpCoro != null) { GameManager.instance.StopCoroutine(rn.jumpCoro); rn.jumpCoro = null; }
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