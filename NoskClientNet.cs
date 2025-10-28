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
        public static event Action<ushort, bool, bool> ToggleReceived;
        public static event Action<ushort, NetAttack, bool> AttackReceived;

        private static NoskClientNet _inst;
        private readonly IClientAddonNetworkSender<C2SPacketId> sender;
        private readonly IClientAddonNetworkReceiver<S2CPacketId> receiver;
        private bool connected;

        public NoskClientNet(ILogger logger, ClientAddon addon, IClientApi clientApi)
        {
            _inst = this;
            receiver = clientApi.NetClient.GetNetworkReceiver<S2CPacketId>(addon, InstantiateClientPacket);
            sender = clientApi.NetClient.GetNetworkSender<C2SPacketId>(addon);

            receiver.RegisterPacketHandler<ToggleS2C>(S2CPacketId.Toggle, d =>
            {
                ToggleReceived?.Invoke(d.SenderId, d.Active, d.FacingRight);
            });

            receiver.RegisterPacketHandler<AttackS2C>(S2CPacketId.Attack, d =>
            {
                AttackReceived?.Invoke(d.SenderId, (NetAttack)d.Attack, d.FacingRight);
            });

            connected = false;
            clientApi.ClientManager.ConnectEvent += () => { connected = true; };
            clientApi.ClientManager.DisconnectEvent += () => { connected = false; };
        }

        private IPacketData InstantiateClientPacket(S2CPacketId id)
        {
            switch (id)
            {
                case S2CPacketId.Toggle: return new ToggleS2C();
                case S2CPacketId.Attack: return new AttackS2C();
                default: return null;
            }
        }

        public static void SendToggle(bool active, bool facingRight)
        {
            if (_inst == null || !_inst.connected) return;
            try { _inst.sender.SendSingleData(C2SPacketId.Toggle, new ToggleC2S { Active = active, FacingRight = facingRight }); }
            catch { }
        }

        public static void SendAttack(NetAttack atk, bool facingRight)
        {
            if (_inst == null || !_inst.connected) return;
            try { _inst.sender.SendSingleData(C2SPacketId.Attack, new AttackC2S { Attack = (byte)atk, FacingRight = facingRight }); }
            catch { }
        }
    }
}