using NoCTF.Application.Authentication.Privacy;

namespace NoCTF.API.Security;

public sealed class RequestSourceAddress(IHttpContextAccessor accessor) : IRequestSourceAddress
{
    public string? Address
    {
        get
        {
            var address = accessor.HttpContext?.Connection.RemoteIpAddress;
            if (address is null) return null;
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString().Split('%')[0];
        }
    }
}
