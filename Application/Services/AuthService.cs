using System.Security.Cryptography;
using System.Text;
using Application.DTOs;
using Application.Interfaces;
using Application.Validation;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using IdentityUserLoginInfo = Microsoft.AspNetCore.Identity.UserLoginInfo;

namespace Application.Services;

public class AuthService : IAuthService
{
    private const int CodeExpirationMinutes = 10;
    private const int MaxCodeAttempts = 5;
    private const int RefreshTokenDays = 7;

    private const string FacebookLoginProvider = "Facebook";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly IEmailService _emailService;
    private readonly IFacebookAuthService _facebookAuthService;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IJwtService jwtService,
        IEmailService emailService,
        IFacebookAuthService facebookAuthService)
    {
        _userManager = userManager;
        _context = context;
        _jwtService = jwtService;
        _emailService = emailService;
        _facebookAuthService = facebookAuthService;
    }

    public async Task<ApiResponse<AuthResponse>> RegisterAsync(RegisterRequest request)
    {
        var fullName = request.FullName?.Trim() ?? string.Empty;
        if (fullName.Length < 2)
        {
            return ApiResponse<AuthResponse>.Fail("El nombre debe tener al menos 2 caracteres.");
        }

        var passwordErrors = PasswordValidator.GetErrors(request.Password);
        if (passwordErrors.Count > 0)
        {
            return ApiResponse<AuthResponse>.Fail(string.Join(" ", passwordErrors));
        }

        var email = NormalizeEmail(request.Email);

        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            var existingLogins = await _userManager.GetLoginsAsync(existingUser);
            if (existingLogins.Any(x => x.LoginProvider == FacebookLoginProvider))
            {
                return ApiResponse<AuthResponse>.Fail(
                    "Este correo ya esta registrado con Facebook. Usa \"Continuar con Facebook\" o registra otro correo.");
            }

            return ApiResponse<AuthResponse>.Fail("Ya existe una cuenta registrada con ese correo.");
        }

        var user = new ApplicationUser
        {
            FullName = fullName,
            Email = email,
            UserName = email,
            EmailConfirmed = true
        };

        foreach (var validator in _userManager.PasswordValidators)
        {
            var validation = await validator.ValidateAsync(_userManager, user, request.Password);
            if (!validation.Succeeded)
            {
                return ApiResponse<AuthResponse>.Fail(FormatIdentityErrors(validation));
            }
        }

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return ApiResponse<AuthResponse>.Fail(FormatIdentityErrors(result));
        }

        var refreshToken = _jwtService.GenerateRefreshToken();
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _jwtService.HashToken(refreshToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshTokenDays)
        });

        await _context.SaveChangesAsync();

        var authResponse = await BuildAuthResponseAsync(user, refreshToken);
        return ApiResponse<AuthResponse>.Ok(authResponse, "Cuenta creada correctamente.");
    }

    public async Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return ApiResponse<AuthResponse>.Fail("Credenciales invalidas.");
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return ApiResponse<AuthResponse>.Fail("La cuenta esta bloqueada temporalmente por intentos fallidos.");
        }

        var passwordIsValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordIsValid)
        {
            await _userManager.AccessFailedAsync(user);

            var logins = await _userManager.GetLoginsAsync(user);
            if (logins.Any(x => x.LoginProvider == FacebookLoginProvider))
            {
                return ApiResponse<AuthResponse>.Fail(
                    "Esta cuenta se creo con Facebook. Usa el boton \"Continuar con Facebook\" (no la contrasena de facebook.com).");
            }

            return ApiResponse<AuthResponse>.Fail("Credenciales invalidas.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        var refreshToken = _jwtService.GenerateRefreshToken();
        var refreshTokenHash = _jwtService.HashToken(refreshToken);

        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshTokenDays)
        });

        await _context.SaveChangesAsync();

        var authResponse = await BuildAuthResponseAsync(user, refreshToken);

        return ApiResponse<AuthResponse>.Ok(authResponse, "Login exitoso.");
    }

    public async Task<ApiResponse<AuthResponse>> FacebookLoginAsync(FacebookLoginRequest request)
    {
        var facebookUser = await _facebookAuthService.ValidateAndGetUserAsync(request.AccessToken);
        if (facebookUser is null)
        {
            var detail = _facebookAuthService.LastError
                ?? "No se pudo validar el inicio de sesion con Facebook.";
            return ApiResponse<AuthResponse>.Fail(detail);
        }

        var user = await _userManager.FindByLoginAsync(FacebookLoginProvider, facebookUser.Id);

        if (user is null && !string.IsNullOrWhiteSpace(facebookUser.Email))
        {
            user = await _userManager.FindByEmailAsync(NormalizeEmail(facebookUser.Email));
        }

        if (user is null)
        {
            var email = !string.IsNullOrWhiteSpace(facebookUser.Email)
                ? NormalizeEmail(facebookUser.Email)
                : $"facebook_{facebookUser.Id}@facebook.login";

            user = new ApplicationUser
            {
                FullName = facebookUser.Name.Trim(),
                Email = email,
                UserName = email,
                EmailConfirmed = true,
                ProfilePhotoUrl = facebookUser.PictureUrl ?? string.Empty
            };

            var createResult = await _userManager.CreateAsync(user, GenerateSecurePassword());
            if (!createResult.Succeeded)
            {
                return ApiResponse<AuthResponse>.Fail(FormatIdentityErrors(createResult));
            }

            var addLoginResult = await _userManager.AddLoginAsync(
                user,
                new IdentityUserLoginInfo(FacebookLoginProvider, facebookUser.Id, FacebookLoginProvider));

            if (!addLoginResult.Succeeded)
            {
                return ApiResponse<AuthResponse>.Fail(FormatIdentityErrors(addLoginResult));
            }
        }
        else
        {
            var existingLogins = await _userManager.GetLoginsAsync(user);
            var facebookLogin = existingLogins.FirstOrDefault(x => x.LoginProvider == FacebookLoginProvider);

            if (facebookLogin is null)
            {
                var addLoginResult = await _userManager.AddLoginAsync(
                    user,
                    new IdentityUserLoginInfo(FacebookLoginProvider, facebookUser.Id, FacebookLoginProvider));

                if (!addLoginResult.Succeeded)
                {
                    return ApiResponse<AuthResponse>.Fail(FormatIdentityErrors(addLoginResult));
                }
            }
            else if (!string.Equals(facebookLogin.ProviderKey, facebookUser.Id, StringComparison.Ordinal))
            {
                return ApiResponse<AuthResponse>.Fail(
                    "Este correo ya esta vinculado a otra cuenta de Facebook. Usa el correo correcto o inicia sesion con email.");
            }

            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
            }

            if (string.IsNullOrWhiteSpace(user.ProfilePhotoUrl) &&
                !string.IsNullOrWhiteSpace(facebookUser.PictureUrl))
            {
                user.ProfilePhotoUrl = facebookUser.PictureUrl;
            }

            if (string.IsNullOrWhiteSpace(user.FullName) && !string.IsNullOrWhiteSpace(facebookUser.Name))
            {
                user.FullName = facebookUser.Name.Trim();
            }

            await _userManager.UpdateAsync(user);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return ApiResponse<AuthResponse>.Fail("La cuenta esta bloqueada temporalmente.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        var refreshToken = _jwtService.GenerateRefreshToken();
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _jwtService.HashToken(refreshToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshTokenDays)
        });

        await _context.SaveChangesAsync();

        var authResponse = await BuildAuthResponseAsync(user, refreshToken);
        return ApiResponse<AuthResponse>.Ok(authResponse, "Login con Facebook exitoso.");
    }

    public async Task<ApiResponse<string>> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var genericMessage = "Si el correo existe, se enviara un codigo de recuperacion.";
        var email = NormalizeEmail(request.Email);
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return ApiResponse<string>.Ok(string.Empty, genericMessage);
        }

        var code = GenerateSixDigitCode();
        await SavePasswordResetCodeAsync(user.Id, code);
        await _emailService.SendPasswordResetCodeAsync(email, user.FullName, code);

        return ApiResponse<string>.Ok(string.Empty, genericMessage);
    }

    public async Task<ApiResponse<string>> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return ApiResponse<string>.Fail("Codigo invalido o usuario no encontrado.");
        }

        var record = await _context.PasswordResetCodes
            .Where(x => x.UserId == user.Id && x.UsedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (record is null)
        {
            return ApiResponse<string>.Fail("Codigo invalido o vencido.");
        }

        if (record.ExpiresAtUtc < DateTime.UtcNow)
        {
            return ApiResponse<string>.Fail("El codigo expiro. Solicita uno nuevo.");
        }

        if (record.Attempts >= MaxCodeAttempts)
        {
            return ApiResponse<string>.Fail("Demasiados intentos. Solicita un nuevo codigo.");
        }

        var expectedHash = HashCode(request.Code, user.Id, "password-reset");
        if (record.CodeHash != expectedHash)
        {
            record.Attempts++;
            await _context.SaveChangesAsync();
            return ApiResponse<string>.Fail("Codigo invalido.");
        }

        var identityResetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, identityResetToken, request.NewPassword);

        if (!result.Succeeded)
        {
            return ApiResponse<string>.Fail(FormatIdentityErrors(result));
        }

        record.UsedAtUtc = DateTime.UtcNow;

        var activeRefreshTokens = await _context.RefreshTokens
            .Where(x => x.UserId == user.Id && x.RevokedAtUtc == null)
            .ToListAsync();

        foreach (var token in activeRefreshTokens)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return ApiResponse<string>.Ok(string.Empty, "Contrasena actualizada correctamente. Inicia sesion con la nueva contrasena.");
    }

    public async Task<ApiResponse<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request)
    {
        var incomingTokenHash = _jwtService.HashToken(request.RefreshToken);

        var storedToken = await _context.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TokenHash == incomingTokenHash);

        if (storedToken is null || !storedToken.IsActive)
        {
            return ApiResponse<AuthResponse>.Fail("Refresh token invalido o expirado.");
        }

        var newRefreshToken = _jwtService.GenerateRefreshToken();
        var newRefreshTokenHash = _jwtService.HashToken(newRefreshToken);

        storedToken.RevokedAtUtc = DateTime.UtcNow;
        storedToken.ReplacedByTokenHash = newRefreshTokenHash;

        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = storedToken.UserId,
            TokenHash = newRefreshTokenHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshTokenDays)
        });

        await _context.SaveChangesAsync();

        var authResponse = await BuildAuthResponseAsync(storedToken.User, newRefreshToken);

        return ApiResponse<AuthResponse>.Ok(authResponse, "Token renovado correctamente.");
    }

    public async Task<ApiResponse<string>> LogoutAsync(LogoutRequest request)
    {
        var tokenHash = _jwtService.HashToken(request.RefreshToken);

        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash);

        if (storedToken is not null && storedToken.RevokedAtUtc is null)
        {
            storedToken.RevokedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return ApiResponse<string>.Ok(string.Empty, "Sesion cerrada correctamente.");
    }

    public async Task<ApiResponse<UserResponse>> GetCurrentUserAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return ApiResponse<UserResponse>.Fail("Usuario no encontrado.");
        }

        return ApiResponse<UserResponse>.Ok(UserResponse.FromUser(user), "Usuario autenticado.");
    }

    public async Task<ApiResponse<ProfileResponse>> GetProfileAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return ApiResponse<ProfileResponse>.Fail("Usuario no encontrado.");
        }

        return ApiResponse<ProfileResponse>.Ok(ProfileResponse.FromUser(user), "Perfil cargado.");
    }

    public async Task<ApiResponse<ProfileResponse>> UpdateProfileAsync(string userId, UpdateProfileRequest request)
    {
        var user = await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return ApiResponse<ProfileResponse>.Fail("Usuario no encontrado.");
        }

        user.FullName = request.FullName.Trim();
        user.Bio = (request.Bio ?? string.Empty).Trim();
        user.City = (request.City ?? string.Empty).Trim();
        user.StatusMessage = string.IsNullOrWhiteSpace(request.StatusMessage)
            ? "Activo"
            : request.StatusMessage.Trim();

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return ApiResponse<ProfileResponse>.Fail(FormatIdentityErrors(updateResult));
        }

        return ApiResponse<ProfileResponse>.Ok(ProfileResponse.FromUser(user), "Perfil actualizado.");
    }

    public async Task<ApiResponse<ProfileResponse>> UpdateProfileImagesAsync(
        string userId,
        string? profilePhotoUrl,
        string? coverPhotoUrl)
    {
        var user = await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return ApiResponse<ProfileResponse>.Fail("Usuario no encontrado.");
        }

        if (!string.IsNullOrWhiteSpace(profilePhotoUrl))
        {
            user.ProfilePhotoUrl = profilePhotoUrl;
        }

        if (!string.IsNullOrWhiteSpace(coverPhotoUrl))
        {
            user.CoverPhotoUrl = coverPhotoUrl;
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return ApiResponse<ProfileResponse>.Fail(FormatIdentityErrors(updateResult));
        }

        return ApiResponse<ProfileResponse>.Ok(ProfileResponse.FromUser(user), "Imagenes de perfil actualizadas.");
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(ApplicationUser user, string refreshToken)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _jwtService.GenerateAccessToken(user, roles);

        return new AuthResponse
        {
            AccessToken = accessToken.Token,
            RefreshToken = refreshToken,
            ExpiresAtUtc = accessToken.ExpiresAtUtc,
            User = UserResponse.FromUser(user)
        };
    }

    private async Task SavePasswordResetCodeAsync(string userId, string code)
    {
        var oldCodes = await _context.PasswordResetCodes
            .Where(x => x.UserId == userId && x.UsedAtUtc == null)
            .ToListAsync();

        foreach (var oldCode in oldCodes)
        {
            oldCode.UsedAtUtc = DateTime.UtcNow;
        }

        _context.PasswordResetCodes.Add(new PasswordResetCode
        {
            UserId = userId,
            CodeHash = HashCode(code, userId, "password-reset"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(CodeExpirationMinutes)
        });

        await _context.SaveChangesAsync();
    }

    private static string GenerateSixDigitCode()
    {
        return RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
    }

    private static string GenerateSecurePassword()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "!Aa1";
    }

    private static string HashCode(string code, string userId, string purpose)
    {
        var rawValue = $"{purpose}:{userId}:{code}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawValue));
        return Convert.ToHexString(bytes);
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static string FormatIdentityErrors(IdentityResult result)
    {
        return string.Join(" | ", result.Errors.Select(x => x.Description));
    }
}
