#pragma warning disable 1591
using Hkmp.Api.Server;
using Hkmp.Api.Server.Networking;
using Hkmp.Logging;
using Hkmp.Networking.Packet;
using Nosk_Transformation.HKMP.Shared;
using System;

namespace Nosk_Transformation.HKMP
{
    public class NoskServerNet
    {
        private static bool DEBUG_PRINTS = true;

        private static void Log(string msg)
        {
            if (DEBUG_PRINTS)
                Modding.Logger.Log($"[NoskServerNet] - {msg}");
        }

        private static void LogError(string msg)
        {
            if (DEBUG_PRINTS)
                Modding.Logger.LogError($"[NoskServerNet] - {msg}");
        }

        private readonly IServerAddonNetworkReceiver<S2CPacketId> receiver;
        private readonly IServerAddonNetworkSender<C2SPacketId> sender;
        private readonly System.Collections.Generic.Dictionary<ushort, int> noskHpPools = new System.Collections.Generic.Dictionary<ushort, int>();
        private readonly ILogger logger;

        public NoskServerNet(ILogger logger, ServerAddon addon, INetServer netServer)
        {
            this.logger = logger;
            receiver = netServer.GetNetworkReceiver<S2CPacketId>(addon, InstantiatePacket);
            sender = netServer.GetNetworkSender<C2SPacketId>(addon);

            receiver.RegisterPacketHandler<ToggleS2C>(S2CPacketId.Toggle, (playerId, data) =>
            {
                try
                {
                    logger.Info($"Player {playerId} toggled Nosk: {data.Active}");

                    sender.BroadcastSingleData(C2SPacketId.Toggle, new ToggleC2S
                    {
                        SenderId = playerId,
                        Active = data.Active,
                        FacingRight = data.FacingRight,
                        Scene = data.Scene
                    });
                }
                catch (Exception e)
                {
                    logger.Error($"Error broadcasting toggle: {e}");
                }
            });

            receiver.RegisterPacketHandler<AttackS2C>(S2CPacketId.Attack, (playerId, data) =>
            {
                try
                {
                    logger.Info($"Player {playerId} performed attack: {data.Attack}");

                    sender.BroadcastSingleData(C2SPacketId.Attack, new AttackC2S
                    {
                        SenderId = playerId,
                        Attack = data.Attack,
                        FacingRight = data.FacingRight
                    });
                }
                catch (Exception e)
                {
                    logger.Error($"Error broadcasting attack: {e}");
                }
            });

            receiver.RegisterPacketHandler<IntroCompleteS2C>(S2CPacketId.IntroComplete, (playerId, data) =>
            {
                try
                {
                    logger.Info($"Player {playerId} intro complete");

                    sender.BroadcastSingleData(C2SPacketId.IntroComplete, new IntroCompleteC2S
                    {
                        SenderId = playerId
                    });
                }
                catch (Exception e)
                {
                    logger.Error($"Error broadcasting intro complete: {e}");
                }
            });

            receiver.RegisterPacketHandler<JoinPoolS2C>(S2CPacketId.JoinPool, (playerId, data) =>
            {
                try
                {
                    noskHpPools[data.OwnerId] = data.StartHp;

                    sender.BroadcastSingleData(C2SPacketId.Damage, new DamageC2S
                    {
                        SenderId = data.OwnerId,
                        NewHP = data.StartHp
                    });
                }
                catch (Exception e)
                {
                    logger.Error($"Error handling JoinPool: {e}");
                }
            });
            receiver.RegisterPacketHandler<MoveS2C>(S2CPacketId.Move, (playerId, data) =>
            {
                try
                {
                    sender.BroadcastSingleData(C2SPacketId.Move, new MoveC2S
                    {
                        SenderId = playerId,
                        FacingRight = data.FacingRight
                    });
                }
                catch (Exception e)
                {
                    logger.Error($"Error broadcasting move: {e}");
                }
            });

            receiver.RegisterPacketHandler<MoveStopS2C>(S2CPacketId.MoveStop, (playerId, data) =>
            {
                try
                {
                    sender.BroadcastSingleData(C2SPacketId.MoveStop, new MoveStopC2S
                    {
                        SenderId = playerId
                    });
                }
                catch (Exception e)
                {
                    logger.Error($"Error broadcasting move stop: {e}");
                }
            });
            receiver.RegisterPacketHandler<DamageDeltaS2C>(S2CPacketId.DamageDelta, (playerId, data) =>
            {
                try
                {
                    if (!noskHpPools.ContainsKey(data.OwnerId))
                    {
                        logger.Info($"DamageDelta for unknown pool {data.OwnerId}, ignoring");
                        return;
                    }

                    int cur = noskHpPools[data.OwnerId];
                    var newHp = cur - data.Delta;
                    if (newHp < 0) newHp = 0;
                    noskHpPools[data.OwnerId] = newHp;

                    sender.BroadcastSingleData(C2SPacketId.Damage, new DamageC2S
                    {
                        SenderId = data.OwnerId,
                        NewHP = newHp
                    });
                }
                catch (Exception e)
                {
                    logger.Error($"Error handling DamageDelta: {e}");
                }
            });

            receiver.RegisterPacketHandler<LeavePoolS2C>(S2CPacketId.LeavePool, (playerId, data) =>
            {
                try
                {
                    noskHpPools.Remove(data.OwnerId);
                }
                catch (Exception e)
                {
                    logger.Error($"Error handling LeavePool: {e}");
                }
            });

            receiver.RegisterPacketHandler<DamageS2C>(S2CPacketId.Damage, (playerId, data) =>
            {
                try
                {
                    logger.Info($"Player {playerId} Nosk HP: {data.NewHP}");

                    sender.BroadcastSingleData(C2SPacketId.Damage, new DamageC2S
                    {
                        SenderId = playerId,
                        NewHP = data.NewHP
                    });
                }
                catch (Exception e)
                {
                    logger.Error($"Error broadcasting damage: {e}");
                }
            });
        }

        private static IPacketData InstantiatePacket(S2CPacketId packetId)
        {
            switch (packetId)
            {
                case S2CPacketId.Toggle: return new ToggleS2C();
                case S2CPacketId.Attack: return new AttackS2C();
                case S2CPacketId.IntroComplete: return new IntroCompleteS2C();
                case S2CPacketId.Damage: return new DamageS2C();
                case S2CPacketId.Move: return new MoveS2C();
                case S2CPacketId.MoveStop: return new MoveStopS2C();
                case S2CPacketId.DamageDelta: return new DamageDeltaS2C();
                case S2CPacketId.JoinPool: return new JoinPoolS2C();
                case S2CPacketId.LeavePool: return new LeavePoolS2C();
                default: return null;
            }
        }
    }
}
