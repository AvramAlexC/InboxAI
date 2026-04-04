using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using Wismo.Api.Multitenancy;

namespace Wismo.Api.Realtime;

[Authorize]
public sealed class TenantDashboardHub : Hub<ITenantDashboardClient>
{
    public const string HubPath = "/hubs/dashboard";

    private const string TenantGroupPrefix = "tenant:";

    public override async Task OnConnectedAsync()
    {
        if (!TryGetTenantId(Context.User, out var tenantId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GetTenantGroupName(tenantId));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetTenantId(Context.User, out var tenantId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetTenantGroupName(tenantId));
        }

        await base.OnDisconnectedAsync(exception);
    }

    public static string GetTenantGroupName(int tenantId) => $"{TenantGroupPrefix}{tenantId}";

    private static bool TryGetTenantId(ClaimsPrincipal? user, out int tenantId)
    {
        tenantId = 0;

        var tenantClaim = user?.FindFirst(HttpTenantContext.TenantClaimName)?.Value;
        return int.TryParse(tenantClaim, out tenantId) && tenantId > 0;
    }
}
