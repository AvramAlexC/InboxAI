namespace Wismo.Api.Multitenancy;

public interface ITenantContext
{
    int? TenantId { get; }
}
