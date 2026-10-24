namespace AMS.Domain.Base
{
    /// <summary>
    /// Every AMS API response (same contract as the other VMM applications): responseCode 0 = success, otherwise
    /// responseMessage says why. See <see cref="ResponseCodes"/>.
    /// </summary>
    public abstract class BaseResponse
    {
        public int responseCode { get; set; } = ResponseCodes.Success;
        public string? responseMessage { get; set; } = "SUCCESS";
        public int recordId { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsSuccess => responseCode == ResponseCodes.Success;
    }

    /// <summary>Plain result of an insert / update / delete.</summary>
    public sealed class InUpRes : BaseResponse { }

    public static class ResponseCodes
    {
        public const int Success = 0;
        /// <summary>The user's UMS session ended (expired, idle, signed in elsewhere, revoked): sign in again.</summary>
        public const int Unauthorized = 401;
        /// <summary>UMS did not allow the action; responseMessage carries UMS's reason.</summary>
        public const int Forbidden = 403;
        public const int NotFound = 1001;
        /// <summary>A business rule was broken (e.g. the asset is already checked out).</summary>
        public const int RuleViolation = 1002;
        public const int Duplicate = 1003;
        /// <summary>The API could not be reached or failed unexpectedly.</summary>
        public const int ApiError = 1004;
        /// <summary>UMS could not be reached to authorise the request.</summary>
        public const int AuthorizationUnavailable = 503;
    }
}
