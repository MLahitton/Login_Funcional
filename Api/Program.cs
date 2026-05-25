using System.Text;
using Application.DependencyInjection;
using Application.DTOs;
using Infrastructure.Data;
using Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

var apiWebRootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(apiWebRootPath);

builder.Services.Configure<Infrastructure.Settings.FacebookSettings>(
    builder.Configuration.GetSection("Facebook"));

// ======================================================
// 1. Controllers
// ======================================================
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var messages = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(error =>
                    string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? $"El campo {entry.Key} no es valido."
                        : error.ErrorMessage))
                .Distinct()
                .ToList();

            var message = messages.Count > 0
                ? string.Join(" ", messages)
                : "Datos invalidos.";

            return new BadRequestObjectResult(ApiResponse<object>.Fail(message));
        };
    });

// ======================================================
// 2. Application layer
// ======================================================
builder.Services.AddApplicationServices();

// ======================================================
// 3. Infrastructure layer
// ======================================================
builder.Services.AddInfrastructureServices(builder.Configuration);

// ======================================================
// 4. CORS
// ======================================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        var frontendUrl = builder.Configuration["Frontend:Url"];
        var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "https://localhost:5002",
            "http://localhost:5002",
            "https://localhost:7222",
            "http://localhost:5126",
            "https://localhost:7002",
            "http://localhost:7002",
            "https://localhost:7065",
            "http://localhost:5065"
        };

        if (!string.IsNullOrWhiteSpace(frontendUrl))
        {
            allowedOrigins.Add(frontendUrl);
        }

        policy.WithOrigins(allowedOrigins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// ======================================================
// 5. JWT Authentication
// ======================================================
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key no está configurado.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer no está configurado.");

var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Jwt:Audience no está configurado.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,

            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),

            ClockSkew = TimeSpan.Zero
        };
    });

// ======================================================
// 6. Authorization
// ======================================================
builder.Services.AddAuthorization();

// ======================================================
// 7. Swagger / OpenAPI
// ======================================================
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Login Facebook Auth API",
        Version = "v1",
        Description = "API de autenticación con Identity, JWT, PostgreSQL y recuperación por correo."
    });

    // Configuración compatible con Swashbuckle.AspNetCore 10.x + Microsoft.OpenApi.
    // En Swagger, debes pegar el token así:
    // Bearer TU_ACCESS_TOKEN
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Escribe: Bearer TU_ACCESS_TOKEN"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

// ======================================================
// 8. Build app
// ======================================================
var app = builder.Build();

await EnsureDatabaseReadyAsync(app);

var facebookAppId = app.Configuration["Facebook:AppId"];
var facebookAppSecret = app.Configuration["Facebook:AppSecret"];
if (string.IsNullOrWhiteSpace(facebookAppId) || string.IsNullOrWhiteSpace(facebookAppSecret))
{
    app.Logger.LogWarning("Facebook login: faltan AppId o AppSecret en la configuracion del API.");
}
else
{
    app.Logger.LogInformation("Facebook login configurado (AppId: {AppId}).", facebookAppId);
}

// ======================================================
// 9. Swagger middleware
// ======================================================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ======================================================
// 10. HTTPS
// ======================================================
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// ======================================================
// 11. CORS middleware
// ======================================================
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(apiWebRootPath)
});
app.UseCors("FrontendPolicy");

// ======================================================
// 12. Authentication middleware
// ======================================================
app.UseAuthentication();

// ======================================================
// 13. Authorization middleware
// ======================================================
app.UseAuthorization();

// ======================================================
// 14. Controllers
// ======================================================
app.MapControllers();

// ======================================================
// 15. Run
// ======================================================
app.Run();

static async Task EnsureDatabaseReadyAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Applies pending migrations and creates the database when it does not exist.
    await dbContext.Database.MigrateAsync();
}
