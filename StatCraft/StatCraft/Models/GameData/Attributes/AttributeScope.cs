using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData.Attributes
{
    //not currently a flags enum, but using bit values in case I decide to make it one in the future
    public enum AttributeScope
    {
        UNKOWN = 0,
        Build = 1,
        BuildDetail = 2,
        Map = 4,
        Game = 8
    }
}
