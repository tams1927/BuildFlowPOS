using HardwareManagementSystem.Models;

namespace HardwareManagementSystem.ViewModels
{
    public class UserBranchListVm
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsGlobal { get; set; }
        public List<UserBranch> AssignedBranches { get; set; } = new();
    }
}
