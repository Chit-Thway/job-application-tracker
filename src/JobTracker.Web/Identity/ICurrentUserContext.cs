using System.Security.Claims;

namespace JobTracker.Web.Identity;

public interface ICurrentUserContext
{
    string? UserId { get; }
}

public sealed class HttpCurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    public string? UserId =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
