using System;
using System.Globalization;
using Dapper;

namespace StatCraft.Services.DatabaseRepository
{
    // Every DateTimeOffset column in this app is stored as an ISO-8601 TEXT value (the "o" invariant
    // format), matching the round-trip convention already used throughout the codebase. Dapper has no
    // built-in string<->DateTimeOffset conversion, so this handler bridges the two.
    internal sealed class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public static readonly DateTimeOffsetTypeHandler Instance = new();

        public override DateTimeOffset Parse(object value) =>
            DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture);

        public override void SetValue(System.Data.IDbDataParameter parameter, DateTimeOffset value) =>
            parameter.Value = value.ToString("o", CultureInfo.InvariantCulture);
    }

    internal static class DapperTypeHandlers
    {
        // Type initializers run at most once per AppDomain, so referencing this from every repository's
        // constructor is enough to guarantee the handler is registered before any query runs.
        static DapperTypeHandlers()
        {
            SqlMapper.AddTypeHandler(DateTimeOffsetTypeHandler.Instance);
        }

        internal static void EnsureRegistered()
        {
            // Body intentionally empty — merely touching this type triggers the static constructor above.
        }
    }
}
