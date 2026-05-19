using System.ComponentModel.DataAnnotations;

namespace HardwareManagementSystem.ViewModels
{
    public class LoginViewModel
    {
        [Required]
        public string Username { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }
}