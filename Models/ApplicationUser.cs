using Microsoft.AspNetCore.Identity;

namespace MicroServices.Auth.API.Models
{
    public class ApplicationUser: IdentityUser
    {
        public string Name { get; set; }
    }
}
