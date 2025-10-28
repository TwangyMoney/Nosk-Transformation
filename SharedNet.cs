#pragma warning disable 1591
using Hkmp.Networking.Packet;

namespace Nosk_Transformation.HKMP.Shared
{
    public enum NetAttack : byte { Roar = 1, Spit1 = 2, Leap = 3, RSJump = 4 }
    public enum C2SPacketId : byte { Toggle = 1, Attack = 2 }
    public enum S2CPacketId : byte { Toggle = 1, Attack = 2 }

    public class ToggleC2S : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public bool Active;
        public bool FacingRight;
        public void WriteData(IPacket p) { p.Write(Active); p.Write(FacingRight); }
        public void ReadData(IPacket p) { Active = p.ReadBool(); FacingRight = p.ReadBool(); }
    }

    public class AttackC2S : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public byte Attack;
        public bool FacingRight;
        public void WriteData(IPacket p) { p.Write(Attack); p.Write(FacingRight); }
        public void ReadData(IPacket p) { Attack = p.ReadByte(); FacingRight = p.ReadBool(); }
    }

    public class ToggleS2C : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public ushort SenderId;
        public bool Active;
        public bool FacingRight;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(Active); p.Write(FacingRight); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); Active = p.ReadBool(); FacingRight = p.ReadBool(); }
    }

    public class AttackS2C : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public ushort SenderId;
        public byte Attack;
        public bool FacingRight;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(Attack); p.Write(FacingRight); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); Attack = p.ReadByte(); FacingRight = p.ReadBool(); }
    }
}