using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.Battlenet
{
    public enum AuthFailureReason
    {
        UserCancelled,
        Timeout,
        PortInUse,
        StateMismatch,
        TokenExchangeFailed,
        UserInfoFailed,
        BrowserLaunchFailed,
    }

    public class BattleNetAuthException : Exception
    {
        public AuthFailureReason Reason { get; }

        public BattleNetAuthException(AuthFailureReason reason, string message, Exception? inner = null)
            : base(message, inner)
        {
            Reason = reason;
        }
    }
}
