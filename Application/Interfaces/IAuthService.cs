using Application.DTOs;

namespace Application.Interfaces;

public interface IAuthService
{
    Task<ApiResponse<AuthResponse>> RegisterAsync(RegisterRequest request);

    Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request);

    Task<ApiResponse<AuthResponse>> FacebookLoginAsync(FacebookLoginRequest request);

    Task<ApiResponse<string>> ForgotPasswordAsync(ForgotPasswordRequest request);

    Task<ApiResponse<string>> ResetPasswordAsync(ResetPasswordRequest request);

    Task<ApiResponse<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request);

    Task<ApiResponse<string>> LogoutAsync(LogoutRequest request);

    Task<ApiResponse<UserResponse>> GetCurrentUserAsync(string userId);

    Task<ApiResponse<ProfileResponse>> GetProfileAsync(string userId);

    Task<ApiResponse<ProfileResponse>> UpdateProfileAsync(string userId, UpdateProfileRequest request);

    Task<ApiResponse<ProfileResponse>> UpdateProfileImagesAsync(
        string userId,
        string? profilePhotoUrl,
        string? coverPhotoUrl);
}
