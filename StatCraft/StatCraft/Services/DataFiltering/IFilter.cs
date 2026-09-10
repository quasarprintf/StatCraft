using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public interface IFilter<in T>
{
    bool? AcceptNull { get; set; }
    //TODO: not happy with this handling of acceptNull
    //but this refactor is getting too large for me to spend time finding a better way right now
    bool MatchesFilter(T candidate, bool? acceptNullOverride = null);
}
