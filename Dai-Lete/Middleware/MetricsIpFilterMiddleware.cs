using System.Net;
using Microsoft.Extensions.Options;

namespace Dai_Lete.Middleware;

public class MetricsIpFilterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<MetricsIpFilterMiddleware> _logger;
    private readonly MetricsIpFilterOptions _options;

    public MetricsIpFilterMiddleware(RequestDelegate next, ILogger<MetricsIpFilterMiddleware> logger, IOptions<MetricsIpFilterOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/metrics"))
        {
            var clientIp = GetClientIpAddress(context);
            
            if (!IsIpAllowed(clientIp))
            {
                _logger.LogWarning("Metrics access denied for IP: {ClientIp}", clientIp);
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Access denied");
                return;
            }

            _logger.LogDebug("Metrics access granted for IP: {ClientIp}", clientIp);
        }

        await _next(context);
    }

    private string GetClientIpAddress(HttpContext context)
    {
        var xForwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xForwardedFor))
        {
            var ips = xForwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (ips.Length > 0)
            {
                return ips[0].Trim();
            }
        }

        var xRealIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xRealIp))
        {
            return xRealIp.Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private bool IsIpAllowed(string clientIp)
    {
        if (string.IsNullOrEmpty(clientIp) || clientIp == "unknown")
        {
            return false;
        }

        if (!IPAddress.TryParse(clientIp, out var ipAddress))
        {
            _logger.LogWarning("Invalid IP address format: {ClientIp}", clientIp);
            return false;
        }

        foreach (var allowedIp in _options.AllowedIps)
        {
            if (string.IsNullOrEmpty(allowedIp)) continue;

            if (allowedIp.Contains('/'))
            {
                if (IsIpInCidrRange(ipAddress, allowedIp))
                {
                    return true;
                }
            }
            else
            {
                if (IPAddress.TryParse(allowedIp, out var allowedIpAddress) && 
                    ipAddress.Equals(allowedIpAddress))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsIpInCidrRange(IPAddress ipAddress, string cidrRange)
    {
        try
        {
            var parts = cidrRange.Split('/');
            if (parts.Length != 2) return false;

            if (!IPAddress.TryParse(parts[0], out var networkAddress) || 
                !int.TryParse(parts[1], out var prefixLength))
            {
                return false;
            }

            if (ipAddress.AddressFamily != networkAddress.AddressFamily)
            {
                return false;
            }

            var addressBytes = ipAddress.GetAddressBytes();
            var networkBytes = networkAddress.GetAddressBytes();

            var bytesToCheck = prefixLength / 8;
            var bitsToCheck = prefixLength % 8;

            for (int i = 0; i < bytesToCheck; i++)
            {
                if (addressBytes[i] != networkBytes[i])
                {
                    return false;
                }
            }

            if (bitsToCheck > 0 && bytesToCheck < addressBytes.Length)
            {
                var mask = (byte)(0xFF << (8 - bitsToCheck));
                if ((addressBytes[bytesToCheck] & mask) != (networkBytes[bytesToCheck] & mask))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}

public class MetricsIpFilterOptions
{
    public const string SectionName = "MetricsIpFilter";
    
    public List<string> AllowedIps { get; set; } = new();
}