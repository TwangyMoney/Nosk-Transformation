#pragma warning disable 1591
using Hkmp.Networking.Packet;

namespace Nosk_Transformation.HKMP.Shared
{
    public enum NetAttack : byte { Roar = 1, Spit1 = 2, Leap = 3, RSJump = 4 }

    public enum S2CPacketId : byte { Toggle = 1, Attack = 2, IntroComplete = 3, Damage = 4, DamageReq = 5, DamageDelta = 6, JoinPool = 7, LeavePool = 8, Move, MoveStop}

    public enum C2SPacketId : byte { Toggle = 1, Attack = 2, IntroComplete = 3, Damage = 4, Move, MoveStop}

    public class ToggleS2C : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public ushort SenderId;
        public bool Active;
        public bool FacingRight;
        public string Scene;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(Active); p.Write(FacingRight); p.Write(Scene); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); Active = p.ReadBool(); FacingRight = p.ReadBool(); Scene = p.ReadString(); }
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

    public class IntroCompleteS2C : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => false;
        public ushort SenderId;
        public void WriteData(IPacket p) { p.Write(SenderId); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); }
    }

    public class DamageS2C : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => false;
        public ushort SenderId;
        public int NewHP;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(NewHP); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); NewHP = p.ReadInt(); }
    }

    public class ToggleC2S : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public ushort SenderId;
        public bool Active;
        public bool FacingRight;
        public string Scene;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(Active); p.Write(FacingRight); p.Write(Scene); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); Active = p.ReadBool(); FacingRight = p.ReadBool(); Scene = p.ReadString(); }
    }

    public class AttackC2S : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public ushort SenderId;
        public byte Attack;
        public bool FacingRight;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(Attack); p.Write(FacingRight); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); Attack = p.ReadByte(); FacingRight = p.ReadBool(); }
    }

    public class IntroCompleteC2S : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => false;
        public ushort SenderId;
        public void WriteData(IPacket p) { p.Write(SenderId); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); }
    }

    public class DamageC2S : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => false;
        public ushort SenderId;
        public int NewHP;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(NewHP); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); NewHP = p.ReadInt(); }
    }

    public class DamageDeltaS2C : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => false;
        public ushort SenderId;
        public ushort OwnerId;
        public int Delta;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(OwnerId); p.Write(Delta); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); OwnerId = p.ReadUShort(); Delta = p.ReadInt(); }
    }

    public class JoinPoolS2C : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public ushort SenderId;
        public ushort OwnerId;
        public int StartHp;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(OwnerId); p.Write(StartHp); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); OwnerId = p.ReadUShort(); StartHp = p.ReadInt(); }
    }

    public class LeavePoolS2C : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public ushort SenderId;
        public ushort OwnerId;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(OwnerId); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); OwnerId = p.ReadUShort(); }
    }
    public class MoveS2C : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public ushort SenderId;
        public bool FacingRight;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(FacingRight); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); FacingRight = p.ReadBool(); }
    }

    public class MoveStopS2C : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public ushort SenderId;
        public void WriteData(IPacket p) { p.Write(SenderId); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); }
    }

    public class MoveC2S : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public ushort SenderId;
        public bool FacingRight;
        public void WriteData(IPacket p) { p.Write(SenderId); p.Write(FacingRight); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); FacingRight = p.ReadBool(); }
    }

    public class MoveStopC2S : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public ushort SenderId;
        public void WriteData(IPacket p) { p.Write(SenderId); }
        public void ReadData(IPacket p) { SenderId = p.ReadUShort(); }
    }
}
