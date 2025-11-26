#pragma warning disable 1591
using Hkmp.Api.Client;
using Hkmp.Api.Client.Networking;
using Hkmp.Logging;
using Hkmp.Networking.Packet;
using Nosk_Transformation.HKMP.Shared;
using System;

namespace Nosk_Transformation.HKMP
{
    public class NoskClientNet
    {
        private static bool DEBUG_PRINTS = true;

        private static void Log(string msg)
        {
            if (DEBUG_PRINTS)
                Modding.Logger.Log($"[NoskClientNet] - {msg}");
        }

        private static void LogError(string msg)
        {
            if (DEBUG_PRINTS)
                Modding.Logger.LogError($"[NoskClientNet] - {msg}");
        }

        public static event Action<ushort, bool, bool, string> ToggleReceived;
        public static event Action<ushort, NetAttack, bool> AttackReceived;
        public static event Action<ushort, int> DamageReceived;
        public static event Action<ushort, bool> MoveReceived;
        public static event Action<ushort> MoveStopReceived;

        private static ushort _selfIdCache = ushort.MaxValue;
        private static System.DateTime _lastLocalEventAt = System.DateTime.MinValue;

        private static NoskClientNet _inst;
        private readonly IClientAddonNetworkSender<S2CPacketId> sender;
        private readonly IClientAddonNetworkReceiver<C2SPacketId> receiver;
        private readonly IClientApi clientApi;
        private bool connected;

        public NoskClientNet(ILogger logger, ClientAddon addon, IClientApi clientApi)
        {
            _inst = this;
            this.clientApi = clientApi;

            sender = clientApi.NetClient.GetNetworkSender<S2CPacketId>(addon);
            receiver = clientApi.NetClient.GetNetworkReceiver<C2SPacketId>(addon, InstantiatePacket);

            receiver.RegisterPacketHandler<ToggleC2S>(C2SPacketId.Toggle, data =>
            {
                try
                {
                    var now = System.DateTime.UtcNow;
                    if (_selfIdCache == ushort.MaxValue && (now - _lastLocalEventAt).TotalSeconds < 1.0) { _selfIdCache = data.SenderId; Log($"[NET] Learned self id={_selfIdCache} via Toggle"); return; }
                    ushort selfId = GetSelfId();
                    if ((selfId != ushort.MaxValue && data.SenderId == selfId) || (_selfIdCache != ushort.MaxValue && data.SenderId == _selfIdCache)) { Log("[NET] Filtered self Toggle"); return; }
                    Log($"[NET] ToggleC2S recv from {data.SenderId} active={data.Active} faceRight={data.FacingRight} scene={data.Scene}");
                    ToggleReceived?.Invoke(data.SenderId, data.Active, data.FacingRight, data.Scene);
                }
                catch (Exception e) { logger.Error($"Error handling toggle packet: {e}"); }
            });

            receiver.RegisterPacketHandler<AttackC2S>(C2SPacketId.Attack, data =>
            {
                try
                {
                    var now = System.DateTime.UtcNow;
                    if (_selfIdCache == ushort.MaxValue && (now - _lastLocalEventAt).TotalSeconds < 1.0) { _selfIdCache = data.SenderId; Log($"[NET] Learned self id={_selfIdCache} via Attack"); return; }
                    ushort selfId = GetSelfId();
                    if ((selfId != ushort.MaxValue && data.SenderId == selfId) || (_selfIdCache != ushort.MaxValue && data.SenderId == _selfIdCache)) { Log("[NET] Filtered self Attack"); return; }
                    Log($"[NET] AttackC2S recv from {data.SenderId} atk={data.Attack} faceRight={data.FacingRight}");
                    AttackReceived?.Invoke(data.SenderId, (NetAttack)data.Attack, data.FacingRight);
                }
                catch (Exception e) { logger.Error($"Error handling attack packet: {e}"); }
            });

            receiver.RegisterPacketHandler<MoveC2S>(C2SPacketId.Move, data =>
            {
                try
                {
                    var now = System.DateTime.UtcNow;
                    if (_selfIdCache == ushort.MaxValue && (now - _lastLocalEventAt).TotalSeconds < 1.0) { _selfIdCache = data.SenderId; Log($"[NET] Learned self id={_selfIdCache} via Move"); return; }
                    ushort selfId = GetSelfId();
                    if ((selfId != ushort.MaxValue && data.SenderId == selfId) || (_selfIdCache != ushort.MaxValue && data.SenderId == _selfIdCache)) { Log("[NET] Filtered self Move"); return; }
                    Log($"[NET] MoveC2S recv from {data.SenderId} faceRight={data.FacingRight}");
                    MoveReceived?.Invoke(data.SenderId, data.FacingRight);
                }
                catch (Exception e) { logger.Error($"Error handling move packet: {e}"); }
            });

            receiver.RegisterPacketHandler<MoveStopC2S>(C2SPacketId.MoveStop, data =>
            {
                try
                {
                    var now = System.DateTime.UtcNow;
                    if (_selfIdCache == ushort.MaxValue && (now - _lastLocalEventAt).TotalSeconds < 1.0) { _selfIdCache = data.SenderId; Log($"[NET] Learned self id={_selfIdCache} via MoveStop"); return; }
                    ushort selfId = GetSelfId();
                    if ((selfId != ushort.MaxValue && data.SenderId == selfId) || (_selfIdCache != ushort.MaxValue && data.SenderId == _selfIdCache)) { Log("[NET] Filtered self MoveStop"); return; }
                    Log($"[NET] MoveStopC2S recv from {data.SenderId}");
                    MoveStopReceived?.Invoke(data.SenderId);
                }
                catch (Exception e) { logger.Error($"Error handling move stop packet: {e}"); }
            });

            receiver.RegisterPacketHandler<DamageC2S>(C2SPacketId.Damage, data =>
            {
                try
                {
                    Log($"[NET] DamageC2S recv: player={data.SenderId}, newHP={data.NewHP}");
                    DamageReceived?.Invoke(data.SenderId, data.NewHP);
                }
                catch (Exception e) { logger.Error($"Error handling damage packet: {e}"); }
            });

            connected = false;
            clientApi.ClientManager.ConnectEvent += OnConnect;
            clientApi.ClientManager.DisconnectEvent += OnDisconnect;

            try { clientApi.ClientManager.PlayerDisconnectEvent += OnPlayerDisconnect; } catch { }
        }

        private ushort GetSelfId()
        {
            try
            {
                var cm = clientApi.ClientManager;
                if (cm == null) return ushort.MaxValue;
                var t = cm.GetType();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var idProp = t.GetProperty("Id", flags);
                if (idProp != null)
                {
                    object v = idProp.GetValue(cm);
                    if (v is ushort u1) return u1;
                    return (ushort)Convert.ChangeType(v, typeof(ushort));
                }
                var idField = t.GetField("Id", flags) ?? t.GetField("_id", flags) ?? t.GetField("id", flags);
                if (idField != null)
                {
                    object v = idField.GetValue(cm);
                    if (v is ushort u2) return u2;
                    return (ushort)Convert.ChangeType(v, typeof(ushort));
                }
            }
            catch { }
            return ushort.MaxValue;
        }

        private void OnConnect()
        {
            connected = true;
            _selfIdCache = ushort.MaxValue;
        }

        private void OnDisconnect()
        {
            connected = false;
        }

        private void OnPlayerDisconnect(IClientPlayer player)
        {
            try
            {
                var playerId = player.Id;
                ToggleReceived?.Invoke(playerId, false, false, "");
            }
            catch { }
        }

        private IPacketData InstantiatePacket(C2SPacketId id)
        {
            switch (id)
            {
                case C2SPacketId.Toggle: return new ToggleC2S();
                case C2SPacketId.Attack: return new AttackC2S();
                case C2SPacketId.Move: return new MoveC2S();
                case C2SPacketId.MoveStop: return new MoveStopC2S();
                case C2SPacketId.Damage: return new DamageC2S();
                default: return null;
            }
        }

        public static void SendMove(bool facingRight)
        {
            if (_inst == null || !_inst.connected) return;
            try
            {
                _lastLocalEventAt = System.DateTime.UtcNow;
                _inst.sender.SendSingleData(S2CPacketId.Move, new MoveS2C
                {
                    SenderId = 0,
                    FacingRight = facingRight
                });
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"[Nosk] Failed to send Move: {e.Message}");
            }
        }

        public static void SendMoveStop()
        {
            if (_inst == null || !_inst.connected) return;
            try
            {
                _lastLocalEventAt = System.DateTime.UtcNow;
                _inst.sender.SendSingleData(S2CPacketId.MoveStop, new MoveStopS2C
                {
                    SenderId = 0
                });
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"[Nosk] Failed to send MoveStop: {e.Message}");
            }
        }

        public static void SendToggle(bool active, bool facingRight, string scene)
        {
            if (_inst == null || !_inst.connected) return;
            try
            {
                _lastLocalEventAt = System.DateTime.UtcNow;
                _inst.sender.SendSingleData(S2CPacketId.Toggle, new ToggleS2C
                {
                    SenderId = 0,
                    Active = active,
                    FacingRight = facingRight,
                    Scene = scene
                });
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"[Nosk] Failed to send toggle: {e.Message}");
            }
        }

        public static void SendAttack(NetAttack atk, bool facingRight)
        {
            if (_inst == null || !_inst.connected) return;
            try
            {
                _lastLocalEventAt = System.DateTime.UtcNow;
                _inst.sender.SendSingleData(S2CPacketId.Attack, new AttackS2C
                {
                    SenderId = 0,
                    Attack = (byte)atk,
                    FacingRight = facingRight
                });
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"[Nosk] Failed to send attack: {e.Message}");
            }
        }

        public static void SendJoinPool(ushort ownerId, int startHp)
        {
            if (_inst == null || !_inst.connected) return;
            try
            {
                _lastLocalEventAt = System.DateTime.UtcNow;
                _inst.sender.SendSingleData(S2CPacketId.JoinPool, new JoinPoolS2C
                {
                    SenderId = 0,
                    OwnerId = ownerId,
                    StartHp = startHp
                });
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"[Nosk] Failed to send JoinPool: {e.Message}");
            }
        }

        public static void SendLeavePool(ushort ownerId)
        {
            if (_inst == null || !_inst.connected) return;
            try
            {
                _lastLocalEventAt = System.DateTime.UtcNow;
                _inst.sender.SendSingleData(S2CPacketId.LeavePool, new LeavePoolS2C
                {
                    SenderId = 0,
                    OwnerId = ownerId
                });
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"[Nosk] Failed to send LeavePool: {e.Message}");
            }
        }

        public static void SendDamageDelta(ushort ownerId, int delta)
        {
            if (_inst == null || !_inst.connected) return;
            try
            {
                _lastLocalEventAt = System.DateTime.UtcNow;
                _inst.sender.SendSingleData(S2CPacketId.DamageDelta, new DamageDeltaS2C
                {
                    SenderId = 0,
                    OwnerId = ownerId,
                    Delta = delta
                });
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"[Nosk] Failed to send DamageDelta: {e.Message}");
            }
        }
    }
}
