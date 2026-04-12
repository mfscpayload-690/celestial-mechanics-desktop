using System.Net.Http;
using System.Security;

namespace CelestialMechanics.Desktop.Services;

/// <summary>
/// SEC-13: Stub for future NASA JPL Horizons API integration.
/// 
/// Security requirements:
/// - Whitelisted endpoint ONLY: https://ssd.jpl.nasa.gov/api/horizons.api
/// - AllowAutoRedirect=false (prevents SSRF via redirects)
/// - UseProxy=false (prevents proxy-based SSRF)
/// - 30s timeout, 5MB max response
/// - Body IDs are typed int — never user-controlled string in URL
/// - No cookie container, no credentials
/// </summary>
public sealed class NasaHorizonsService : IDisposable
{
    private static readonly Uri AllowedEndpoint =
        new("https://ssd.jpl.nasa.gov/api/horizons.api");

    private const int TimeoutSeconds = 30;
    private const long MaxResponseBytes = 5 * 1024 * 1024; // 5MB

    private readonly HttpClient _client;

    public NasaHorizonsService()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,   // SEC-13: prevent SSRF via redirects
            UseProxy = false,            // SEC-13: prevent proxy-based SSRF
            UseCookies = false,
            UseDefaultCredentials = false,
        };

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds),
            MaxResponseContentBufferSize = MaxResponseBytes,
        };
    }

    /// <summary>
    /// Queries ephemeris data for a body by its NASA NAIF integer ID.
    /// The body ID is a typed int — never user-controlled string concatenation in URL.
    /// </summary>
    /// <param name="naifId">NASA NAIF body ID (e.g., 399 = Earth, 10 = Sun).</param>
    /// <param name="startDate">Start date in ISO format (yyyy-MM-dd).</param>
    /// <param name="stopDate">Stop date in ISO format (yyyy-MM-dd).</param>
    /// <returns>Raw JSON response from Horizons API.</returns>
    public async Task<string> QueryEphemerisAsync(int naifId, DateTime startDate, DateTime stopDate)
    {
        // Build query from typed parameters only — no user string concatenation
        string start = startDate.ToString("yyyy-MM-dd");
        string stop = stopDate.ToString("yyyy-MM-dd");

        var builder = new UriBuilder(AllowedEndpoint);
        builder.Query = $"format=json&COMMAND='{naifId}'&OBJ_DATA='YES'&MAKE_EPHEM='YES'" +
                        $"&EPHEM_TYPE='VECTORS'&CENTER='500@0'&START_TIME='{start}'" +
                        $"&STOP_TIME='{stop}'&STEP_SIZE='1 d'&REF_SYSTEM='ICRF'";

        // Verify the final URI is still on the whitelisted host
        if (builder.Uri.Host != AllowedEndpoint.Host ||
            builder.Uri.Scheme != AllowedEndpoint.Scheme)
        {
            throw new SecurityException(
                "Horizons request was redirected to an unauthorized endpoint.");
        }

        var response = await _client.GetAsync(builder.Uri).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Verify response is not a redirect (defense in depth)
        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
        {
            throw new SecurityException("Unexpected redirect from Horizons API.");
        }

        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
