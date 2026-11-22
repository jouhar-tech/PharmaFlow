using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Models;

namespace PharmaFlow.Services;

public interface IFirebaseCloudMessagingService
{
    Task<bool> SendToProfileAsync(
        long profileId,
        string title,
        string body,
        string relativeUrl,
        CancellationToken cancellationToken);
}

/// <summary>
/// Sends native Median app notifications directly through Firebase Cloud Messaging HTTP v1.
/// Browser Web Push remains handled by the existing VAPID sender.
/// </summary>
public sealed class FirebaseCloudMessagingService : IFirebaseCloudMessagingService
{
    private readonly ApplicationDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly FirebaseAccessTokenProvider _accessTokenProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FirebaseCloudMessagingService> _logger;

    public FirebaseCloudMessagingService(
        ApplicationDbContext db,
        HttpClient httpClient,
        FirebaseAccessTokenProvider accessTokenProvider,
        IConfiguration configuration,
        ILogger<FirebaseCloudMessagingService> logger)
    {
        _db = db;
        _httpClient = httpClient;
        _accessTokenProvider = accessTokenProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendToProfileAsync(
        long profileId,
        string title,
        string body,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        var devices = await _db.FcmDeviceTokens
            .Where(device =>
                device.ProfileId == profileId &&
                device.IsActive &&
                device.PermissionGranted)
            .ToListAsync(cancellationToken);

        if (devices.Count == 0)
            return false;

        var projectId = _configuration["Firebase:ProjectId"]?.Trim();
        if (string.IsNullOrWhiteSpace(projectId))
        {
            _logger.LogWarning("Firebase:ProjectId is missing; native push delivery was skipped.");
            return false;
        }

        var accessToken = await _accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return false;

        var appBaseUrl = (_configuration["Firebase:AppBaseUrl"] ?? "https://pharmahex.runasp.net")
            .Trim()
            .TrimEnd('/');
        var targetUrl = Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absoluteUrl)
            ? absoluteUrl.ToString()
            : appBaseUrl + (relativeUrl.StartsWith('/') ? relativeUrl : "/" + relativeUrl);

        var delivered = false;
        foreach (var device in devices)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    message = new
                    {
                        token = device.Token,
                        notification = new
                        {
                            title,
                            body
                        },
                        data = new Dictionary<string, string>
                        {
                            ["targetUrl"] = targetUrl,
                            ["source"] = "pharmahex"
                        },
                        android = new
                        {
                            priority = "HIGH",
                            notification = new
                            {
                                channel_id = "pharmahex_alerts",
                                sound = "default"
                            }
                        },
                        apns = new
                        {
                            payload = new
                            {
                                aps = new
                                {
                                    sound = "default"
                                }
                            }
                        }
                    }
                });

                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(projectId)}/messages:send");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    device.LastSuccessAt = DateTime.UtcNow;
                    device.UpdatedAt = DateTime.UtcNow;
                    delivered = true;
                    continue;
                }

                device.LastFailureAt = DateTime.UtcNow;
                device.UpdatedAt = DateTime.UtcNow;

                // FCM explicitly reports UNREGISTERED when an app installation token
                // is no longer valid (for example after app data is cleared/reinstall).
                if (responseBody.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase))
                {
                    device.IsActive = false;
                    _logger.LogInformation(
                        "Deactivated expired FCM token {DeviceTokenId} for profile {ProfileId}.",
                        device.DeviceTokenId,
                        profileId);
                }
                else
                {
                    _logger.LogWarning(
                        "FCM delivery failed for device {DeviceTokenId}, profile {ProfileId}. HTTP {StatusCode}; response: {Response}",
                        device.DeviceTokenId,
                        profileId,
                        (int)response.StatusCode,
                        responseBody.Length > 1200 ? responseBody[..1200] : responseBody);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                device.LastFailureAt = DateTime.UtcNow;
                device.UpdatedAt = DateTime.UtcNow;
                _logger.LogWarning(
                    ex,
                    "FCM delivery failed for device {DeviceTokenId}, profile {ProfileId}.",
                    device.DeviceTokenId,
                    profileId);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return delivered;
    }
}
