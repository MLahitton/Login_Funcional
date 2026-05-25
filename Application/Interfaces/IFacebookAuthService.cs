namespace Application.Interfaces;

public interface IFacebookAuthService
{
    string? LastError { get; }

    Task<FacebookUserInfo?> ValidateAndGetUserAsync(string accessToken, CancellationToken cancellationToken = default);
}

public sealed class FacebookUserInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PictureUrl { get; init; }
}
