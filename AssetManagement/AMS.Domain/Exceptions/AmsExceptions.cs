using AMS.Domain.Base;

namespace AMS.Domain.Exceptions
{
    /// <summary>A request the API refuses, with the response code to return (rule broken, not found, duplicate…).</summary>
    public class AmsException : Exception
    {
        public int Code { get; }
        public AmsException(int code, string message) : base(message) => Code = code;

        public static AmsException Rule(string message) => new(ResponseCodes.RuleViolation, message);
        public static AmsException NotFound(string what) => new(ResponseCodes.NotFound, $"{what} was not found.");
    }

    /// <summary>UMS denied the action; the message is UMS's reason (e.g. "Policy: Deny 'Managers cannot dispose high-value assets'…").</summary>
    public class AccessDeniedException : AmsException
    {
        public string Permission { get; }
        public AccessDeniedException(string permission, string reason) : base(ResponseCodes.Forbidden, reason) => Permission = permission;
    }

    /// <summary>The user's UMS session is no longer valid (401 from UMS).</summary>
    public class SessionEndedException : AmsException
    {
        public string Reason { get; }
        public SessionEndedException(string reason, string message) : base(ResponseCodes.Unauthorized, message) => Reason = reason;
    }

    /// <summary>UMS could not be reached: fail closed.</summary>
    public class AuthorizationUnavailableException : AmsException
    {
        public AuthorizationUnavailableException(string message, Exception? inner = null) : base(ResponseCodes.AuthorizationUnavailable, message)
        {
            if (inner != null) Data["inner"] = inner.Message;
        }
    }
}
