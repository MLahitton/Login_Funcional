using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Settings;

public class FacebookAuthService : IFacebookAuthService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public string? LastError { get; private set; }

    public FacebookAuthService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<FacebookUserInfo?> ValidateAndGetUserAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        LastError = null;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            LastError = "No se recibio token de Facebook.";
            return null;
        }

        var appId = GetSetting("Facebook:AppId");
        var appSecret = GetSetting("Facebook:AppSecret");

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
        {
            LastError = "No se pudo iniciar sesion con Facebook.";
            return null;
        }

        var appAccessToken = $"{appId}|{appSecret}";
        var debugUrl =
            $"https://graph.facebook.com/debug_token?input_token={Uri.EscapeDataString(accessToken)}&access_token={Uri.EscapeDataString(appAccessToken)}";

        var debugResponse = await _httpClient.GetFromJsonAsync<FacebookDebugTokenResponse>(
            debugUrl,
            cancellationToken);

        if (debugResponse?.Data is null)
        {
            LastError = "Respuesta invalida al validar token con Facebook.";
            return null;
        }

        if (!debugResponse.Data.IsValid)
        {
            LastError = debugResponse.Data.Error?.Message
                ?? "Token de Facebook invalido o expirado.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(debugResponse.Data.UserId))
        {
            LastError = "Facebook no devolvio el id de usuario.";
            return null;
        }

        var meUrl =
            $"https://graph.facebook.com/me?fields=id,name,email,picture.type(large)&access_token={Uri.EscapeDataString(accessToken)}";

        var profile = await _httpClient.GetFromJsonAsync<FacebookMeResponse>(meUrl, cancellationToken);
        if (profile is null || string.IsNullOrWhiteSpace(profile.Id))
        {
            LastError = "No se pudo leer el perfil de Facebook.";
            return null;
        }

        if (!string.Equals(profile.Id, debugResponse.Data.UserId, StringComparison.Ordinal))
        {
            LastError = "El token no coincide con el usuario de Facebook.";
            return null;
        }

        return new FacebookUserInfo
        {
            Id = profile.Id,
            Name = profile.Name ?? "Usuario Facebook",
            Email = profile.Email,
            PictureUrl = profile.Picture?.Data?.Url
        };
    }

    private string GetSetting(string key) =>
        _configuration[key]?.Trim() ?? string.Empty;

    private sealed class FacebookDebugTokenResponse
    {
        [JsonPropertyName("data")]
        public FacebookDebugTokenData? Data { get; set; }
    }

    private sealed class FacebookDebugTokenData
    {
        [JsonPropertyName("is_valid")]
        public bool IsValid { get; set; }

        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }

        [JsonPropertyName("error")]
        public FacebookGraphError? Error { get; set; }
    }

    private sealed class FacebookGraphError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("code")]
        public int Code { get; set; }
    }

    private sealed class FacebookMeResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("picture")]
        public FacebookPictureContainer? Picture { get; set; }
    }

    private sealed class FacebookPictureContainer
    {
        [JsonPropertyName("data")]
        public FacebookPictureData? Data { get; set; }
    }

    private sealed class FacebookPictureData
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
