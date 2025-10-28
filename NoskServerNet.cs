#pragma warning disable 1591
using Hkmp.Api.Server;
using Hkmp.Api.Server.Networking;
using Hkmp.Logging;
using Hkmp.Networking.Packet;
using Nosk_Transformation.HKMP.Shared;

namespace Nosk_Transformation.HKMP
{
    public class NoskServerNet
    {
        private readonly IServerAddonNetworkReceiver<C2SPacketId> receiver;
        private readonly IServerAddonNetworkSender<S2CPacketId> sender;

        public NoskServerNet(ILogger logger, ServerAddon addon, INetServer netServer)
        {
            receiver = netServer.GetNetworkReceiver<C2SPacketId>(addon, InstantiateServerPacket);
            sender = netServer.GetNetworkSender<S2CPacketId>(addon);

            receiver.RegisterPacketHandler<ToggleC2S>(C2SPacketId.Toggle, (playerId, data) =>
            {
                sender.SendSingleData(S2CPacketId.Toggle, new ToggleS2C { SenderId = playerId, Active = data.Active, FacingRight = data.FacingRight });
            });

            receiver.RegisterPacketHandler<AttackC2S>(C2SPacketId.Attack, (playerId, data) =>
            {
                sender.SendSingleData(S2CPacketId.Attack, new AttackS2C { SenderId = playerId, Attack = data.Attack, FacingRight = data.FacingRight });
            });
        }

        private static IPacketData InstantiateServerPacket(C2SPacketId packetId)
        {
            switch (packetId)
            {
                case C2SPacketId.Toggle: return new ToggleC2S();
                case C2SPacketId.Attack: return new AttackC2S();
                default: return null;
            }
        }
    }
}