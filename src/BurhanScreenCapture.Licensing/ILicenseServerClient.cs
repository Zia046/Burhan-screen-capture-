using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BurhanScreenCapture.Licensing;

public sealed record ActivationResult(bool Success, string? LicenseKey, string Message);

/// <summary>
/// Contract with the Burhan Nexus licensing server. See docs/LICENSING.md for the HTTP API.
/// </summary>
public interface ILicenseServerClient
{
    bool IsConfigured { get; }
    Task<ActivationResult> ActivateAsync(string licenseKey, string deviceId, string deviceName, CancellationToken ct = default);
    /// <summary>Signed revocation list text (see RevocationList), or null when unavailable.</summary>
    Task<string?> GetRevocationListAsync(CancellationToken ct = default);
    Task<bool> DeactivateAsync(string licenseKey, string deviceId, CancellationToken ct = default);
}

/// <summary>HTTPS client for the licensing server. Responses are only trusted after signature verification.</summary>
public sealed class HttpLicenseServerClient : ILicenseServerClient
{
    private readonly HttpClient _http;
    private readonly Uri? _baseUri;

    public HttpLicenseServerClient(string? baseUrl, HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var u)
            && u.Scheme == Uri.UriSchemeHttps)
            _baseUri = u;
    }

    public bool IsConfigured => _baseUri is not null;

    private sealed class Request
    {
        [JsonPropertyName("licenseKey")] public string LicenseKey { get; set; } = "";
        [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
        [JsonPropertyName("deviceName")] public string DeviceName { get; set; } = "";
        [JsonPropertyName("product")] public string Product { get; set; } = "BNX-SC-PRO";
    }

    private sealed class Response
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        /// <summary>A device-bound license key signed by the server (same format the generator produces).</summary>
        [JsonPropertyName("licenseKey")] public string? LicenseKey { get; set; }
    }

    public Task<ActivationResult> ActivateAsync(string licenseKey, string deviceId, string deviceName, CancellationToken ct = default) =>
        PostAsync("v1/activate", new Request { LicenseKey = licenseKey, DeviceId = deviceId, DeviceName = deviceName }, ct);

    public async Task<string?> GetRevocationListAsync(CancellationToken ct = default)
    {
        if (_baseUri is null) return null;
        try { return await _http.GetStringAsync(new Uri(_baseUri, "v1/revocations"), ct).ConfigureAwait(false); }
        catch { return null; }
    }

    public async Task<bool> DeactivateAsync(string licenseKey, string deviceId, CancellationToken ct = default)
    {
        var r = await PostAsync("v1/deactivate", new Request { LicenseKey = licenseKey, DeviceId = deviceId }, ct).ConfigureAwait(false);
        return r.Success;
    }

    private async Task<ActivationResult> PostAsync(string path, Request body, CancellationToken ct)
    {
        if (_baseUri is null)
            return new(false, null, "Online activation is not available yet. Use \"Import license file\" or contact Burhan Nexus.");
        try
        {
            using var resp = await _http.PostAsJsonAsync(new Uri(_baseUri, path), body, ct).ConfigureAwait(false);
            var data = await resp.Content.ReadFromJsonAsync<Response>(cancellationToken: ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode || data is null || !data.Ok)
                return new(false, null, data?.Message ?? $"The licensing server rejected the request ({(int)resp.StatusCode}).");
            return new(true, data.LicenseKey, data.Message ?? "Activated.");
        }
        catch (HttpRequestException)
        {
            return new(false, null, "Could not reach the licensing server. Check your internet connection and try again.");
        }
        catch (TaskCanceledException)
        {
            return new(false, null, "The licensing server did not respond in time. Please try again.");
        }
        catch (System.Text.Json.JsonException)
        {
            return new(false, null, "The licensing server sent an unexpected response.");
        }
    }
}
