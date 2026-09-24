using FoMed.Infrastructure.Models;

namespace FoMed.Infrastructure.Authentication;

public interface ITokenService
{
    string CreateToken(User user);
}