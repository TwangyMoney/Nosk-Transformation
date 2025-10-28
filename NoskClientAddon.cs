#pragma warning disable 1591
using Hkmp.Api.Client;
using UnityEngine;
using System.Reflection;

namespace Nosk_Transformation.HKMP
{
    public class NoskClientAddon : ClientAddon
    {
        private static IClientApi _clientApi;

        public override void Initialize(IClientApi clientApi)
        {
            _clientApi = clientApi;
            new NoskClientNet(Logger, this, clientApi);
        }

        public static bool TryGetRemoteHeroPosition(ushort playerId, out Vector3 pos)
        {
            pos = default;

            try
            {
                var cm = _clientApi?.ClientManager;
                if (cm != null)
                {
                    var cmType = cm.GetType();
                    var pm = cmType.GetProperty("PlayerManager")?.GetValue(cm);
                    if (pm != null)
                    {
                        var pmType = pm.GetType();
                        var getPlayer = pmType.GetMethod("GetPlayer", BindingFlags.Public | BindingFlags.Instance);
                        var remote = getPlayer?.Invoke(pm, new object[] { playerId });

                        if (remote != null)
                        {
                            var rt = remote.GetType();

                            var goProp = rt.GetProperty("GameObject", BindingFlags.Public | BindingFlags.Instance);
                            if (goProp != null)
                            {
                                var go = goProp.GetValue(remote) as GameObject;
                                if (go != null)
                                {
                                    pos = go.transform.position;
                                    return true;
                                }
                            }

                            var getGo = rt.GetMethod("GetGameObject", BindingFlags.Public | BindingFlags.Instance);
                            if (getGo != null)
                            {
                                var go = getGo.Invoke(remote, null) as GameObject;
                                if (go != null)
                                {
                                    pos = go.transform.position;
                                    return true;
                                }
                            }

                            var posProp = rt.GetProperty("Position", BindingFlags.Public | BindingFlags.Instance);
                            if (posProp != null)
                            {
                                var v = posProp.GetValue(remote);
                                if (v is Vector3 v3)
                                {
                                    pos = v3;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                string pidStr = playerId.ToString();
                foreach (var t in GameObject.FindObjectsOfType<Transform>())
                {
                    var go = t?.gameObject;
                    if (go == null || !go.activeInHierarchy) continue;
                    var n = go.name;
                    if (string.IsNullOrEmpty(n)) continue;

                    bool looksKnight = n.IndexOf("Knight", System.StringComparison.OrdinalIgnoreCase) >= 0
                                    || n.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0
                                    || n.IndexOf("Remote", System.StringComparison.OrdinalIgnoreCase) >= 0;

                    if (looksKnight && n.IndexOf(pidStr, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        pos = t.position;
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        protected override string Name => "Nosk_Transformation";
        protected override string Version => "1.0.0";
        public override bool NeedsNetwork => true;
    }
}