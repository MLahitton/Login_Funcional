using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Services;
using Infrastructure.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Infrastructure.DependencyInjection;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.Configure<EmailSettings>(configuration.GetSection("EmailSettings"));
        services.Configure<FacebookSettings>(configuration.GetSection("Facebook"));

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var rawConnection = configuration.GetConnectionString("DefaultConnection")
                ?? configuration["DATABASE_URL"];

            var normalizedConnection = NormalizePostgresConnectionString(rawConnection);
            options.UseNpgsql(normalizedConnection);
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.SignIn.RequireConfirmedEmail = false;

                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddHttpClient<IFacebookAuthService, FacebookAuthService>();

        return services;
    }

    private static string NormalizePostgresConnectionString(string? rawConnection)
    {
        if (string.IsNullOrWhiteSpace(rawConnection))
        {
            throw new InvalidOperationException("No se encontro la cadena de conexion de PostgreSQL.");
        }

        // Supports Render-style URLs: postgresql://user:pass@host:5432/dbname
        if (rawConnection.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            rawConnection.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(rawConnection);
            var userInfo = uri.UserInfo.Split(':', 2);

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = uri.AbsolutePath.Trim('/'),
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
                SslMode = SslMode.Disable,
                TrustServerCertificate = true
            };

            return builder.ConnectionString;
        }

        return rawConnection;
    }
}
