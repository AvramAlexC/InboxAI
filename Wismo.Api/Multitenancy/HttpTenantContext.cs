namespace Wismo.Api.Multitenancy;

public sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public const string TenantHeaderName = "X-Tenant-Id";
    public const string TenantClaimName = "tenant_id";

    public int? TenantId
    {
        get
        {
            var tenantClaim = httpContextAccessor.HttpContext?.User?.FindFirst(TenantClaimName)?.Value;
            if (int.TryParse(tenantClaim, out var claimTenantId) && claimTenantId > 0)
            {
                return claimTenantId;
            }

            var rawTenantId = httpContextAccessor.HttpContext?.Request.Headers[TenantHeaderName].FirstOrDefault();
            if (int.TryParse(rawTenantId, out var headerTenantId) && headerTenantId > 0)
            {
                return headerTenantId;
            }

            return null;
        }
    }
}
