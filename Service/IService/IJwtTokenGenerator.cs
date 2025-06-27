using MicroServices.Auth.API.Models;
using System.Collections.Generic;

namespace MicroServices.Auth.API.Service.IService
{
    public interface IJwtTokenGenerator
    {
        // Este metodo se encarga de generar el token con los datos del usuario
        // El usuario logeado
        // Una cadena que es el token jwt
        string GenerateToken(ApplicationUser applicationUser, IEnumerable<string> roles);
    }
}