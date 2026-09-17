using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public interface IWrappedFilter<T,F> : IFilter<T>
{
    IFilter<F> Filter { get; set; }
}
