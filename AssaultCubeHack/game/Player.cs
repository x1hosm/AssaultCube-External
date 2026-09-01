using System;

namespace AssaultCubeHack
{
    internal class Player
    {
        private readonly int pointerPlayer;

        public Player(int pointerPlayer)
        {
            this.pointerPlayer = pointerPlayer;
        }

        public int Address
        {
            get { return pointerPlayer; }
        }

        public int Health
        {
            get { return Memory.Read<int>(pointerPlayer + Offsets.Health); }
        }

        public int Armour
        {
            get { return Memory.Read<int>(pointerPlayer + Offsets.Armour); }
        }

        public int Team
        {
            get { return Memory.Read<int>(pointerPlayer + Offsets.Team); }
        }

        public int State
        {
            get { return Memory.Read<int>(pointerPlayer + Offsets.State); }
        }

        public string Name
        {
            get { return Memory.ReadString(pointerPlayer + Offsets.Name, 16); }
        }

        public Vector3 PositionFoot
        {
            get
            {
                return new Vector3(
                    Memory.Read<float>(pointerPlayer + Offsets.PositionX),
                    Memory.Read<float>(pointerPlayer + Offsets.PositionY),
                    Memory.Read<float>(pointerPlayer + Offsets.PositionZ)
                );
            }
        }

        public Vector3 PositionHead
        {
            get
            {
                Vector3 foot = PositionFoot;
                const float EYE_HEIGHT = 6.0f;   
                return new Vector3(foot.x, foot.y, foot.z + EYE_HEIGHT);
            }
        }
    }
}