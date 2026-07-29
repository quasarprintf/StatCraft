using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData.Race
{
    [Flags]
    public enum Matchups
    {
        None = 0,
        VsZ = 1 << 0,
        VsT = 1 << 1,
        VsP = 1 << 2,
    }
}
