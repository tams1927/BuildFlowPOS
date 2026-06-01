namespace HardwareManagementSystem.Services
{
    public interface ITenantContext
    {
        int? CurrentTenantId { get; }
        bool IsGlobalUser { get; }

        Task<int?> GetCurrentTenantIdAsync();
    }
}
