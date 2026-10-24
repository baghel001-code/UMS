namespace AMS.Application.Interfaces
{
    /// <summary>
    /// How the web portal talks to the AMS API (same shape as the API-call service of the other VMM portals):
    /// <c>CallingAPI&lt;Response, Request&gt;(AmsApiNames.GET_ASSETS, request)</c>. The signed-in user's UMS access token is attached;
    /// failures come back as a response with responseCode/responseMessage set instead of an exception.
    /// </summary>
    public interface IAPICall
    {
        Task<TRes> CallingAPI<TRes, TReq>(string apiName, TReq requestBody, CancellationToken ct = default) where TRes : Domain.Base.BaseResponse, new();
    }

    /// <summary>Supplies the current user's (fresh) UMS access token to outgoing API calls; null when nobody is signed in.</summary>
    public interface IAccessTokenProvider
    {
        Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
        string? ClientIp { get; }
        /// <summary>The API reported that UMS ended the user's session (reason from the X-Session-End header: Replaced, Idle…).</summary>
        void SessionEnded(string reason);
    }
}
