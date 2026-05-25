# Login_Fb

Proyecto full stack de autenticacion y perfil con backend en ASP.NET Core + Identity + JWT + PostgreSQL, y frontend en Blazor WebAssembly.

## Contenido
1. Descripcion general
2. Stack tecnico
3. Arquitectura
4. Funcionalidades
5. Requisitos
6. Configuracion
7. Arranque rapido
8. Base de datos y migraciones
9. Endpoints API
10. Frontend
11. Scripts utiles
12. Troubleshooting
13. Seguridad recomendada

## 1) Descripcion general
El sistema permite:
- Registro de usuario por email y password.
- Login por email/password.
- Login con Facebook.
- Refresh token y logout.
- Recuperacion de password por codigo.
- Perfil de usuario (lectura y actualizacion).
- Upload de foto de perfil y portada.

La API responde en formato estandar `ApiResponse<T>`.

## 2) Stack tecnico
- .NET SDK 10
- ASP.NET Core Web API
- Entity Framework Core + Npgsql
- PostgreSQL
- ASP.NET Core Identity
- JWT Bearer Auth
- MailKit (SMTP)
- Blazor WebAssembly

## 3) Arquitectura
Estructura por capas:

```text
Api/              -> Host HTTP, Program.cs, Controllers, configuracion
Application/      -> Casos de uso, DTOs, interfaces, reglas de negocio
Domain/           -> Entidades del dominio
Infrastructure/   -> EF Core, Identity, JWT, Email, Facebook, DI
Frontend/         -> Cliente Blazor WebAssembly
```

Dependencias principales:
- `Api -> Application, Infrastructure`
- `Infrastructure -> Application, Domain`
- `Application -> Domain`

## 4) Funcionalidades
### Autenticacion
- Registro: `POST /api/auth/register`
- Login: `POST /api/auth/login`
- Login Facebook: `POST /api/auth/facebook`
- Refresh token: `POST /api/auth/refresh-token`
- Logout: `POST /api/auth/logout`

### Recuperacion de cuenta
- Solicitar codigo: `POST /api/auth/forgot-password`
- Reset password: `POST /api/auth/reset-password`

### Perfil
- Usuario actual: `GET /api/auth/me`
- Perfil: `GET /api/auth/profile`
- Actualizar perfil: `PUT /api/auth/profile`
- Foto perfil: `POST /api/auth/profile/photo`
- Foto portada: `POST /api/auth/profile/cover`

## 5) Requisitos
### Software
- .NET SDK 10.0.x
- PostgreSQL 17 (o compatible)
- (Opcional) `dotnet-ef`

### Puertos por defecto
- API: `https://localhost:5001` y `http://localhost:5000`
- Frontend: `http://localhost:5126` (y perfil https en `https://localhost:7222`)

## 6) Configuracion
### API
Archivo: `Api/appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=login_fb;Username=postgres;Password=1234"
  },
  "Jwt": {
    "Key": "CAMBIAR_EN_PRODUCCION",
    "Issuer": "login_Fb_api",
    "Audience": "login_Fb_frontend",
    "ExpirationMinutes": 15
  },
  "Frontend": {
    "Url": "http://localhost:5126"
  }
}
```

Tambien puedes sobreescribir por variable de entorno:

```powershell
$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=login_fb;Username=postgres;Password=1234"
```

### Frontend
Archivo: `Frontend/wwwroot/appsettings.json`

```json
{
  "Api": {
    "BaseUrlHttp": "http://localhost:5000/api/auth/",
    "BaseUrlHttps": "https://localhost:5001/api/auth/"
  }
}
```

## 7) Arranque rapido
### 1. Restaurar y compilar
```powershell
dotnet restore
dotnet build
```

### 2. Levantar API
```powershell
dotnet run --project Api/Api.csproj --launch-profile http
```

### 3. Levantar Frontend
```powershell
dotnet run --project Frontend/Frontend.csproj --launch-profile http
```

### 4. Probar
- API Swagger: `https://localhost:5001/swagger`
- Frontend: `http://localhost:5126`

## 8) Base de datos y migraciones
### Creacion automatica al iniciar
El API ejecuta migraciones automaticamente en startup (`Api/Program.cs`):

```csharp
await dbContext.Database.MigrateAsync();
```

Esto aplica migraciones pendientes y crea la base si no existe.

### Importante
Si la credencial de PostgreSQL es incorrecta, la creacion automatica no podra ejecutarse.
Ejemplo de error tipico:
- `SqlState: 28P01` (password authentication failed)

### Comandos EF utiles
```powershell
dotnet ef migrations list --project Infrastructure/Infrastructure.csproj --startup-project Api/Api.csproj

dotnet ef database update --project Infrastructure/Infrastructure.csproj --startup-project Api/Api.csproj

dotnet ef migrations add NombreMigracion --project Infrastructure/Infrastructure.csproj --startup-project Api/Api.csproj
```

## 9) Endpoints API
Base URL: `/api/auth`

### Publicos
- `POST /register`
- `POST /login`
- `POST /facebook`
- `POST /forgot-password`
- `POST /reset-password`
- `POST /refresh-token`

### Protegidos (Bearer token)
- `POST /logout`
- `GET /me`
- `GET /profile`
- `PUT /profile`
- `POST /profile/photo` (`multipart/form-data`)
- `POST /profile/cover` (`multipart/form-data`)

## 10) Frontend
Rutas principales:
- `/login`
- `/register`
- `/forgot-password`
- `/reset-password`
- `/dashboard`

El `HttpClient` del frontend decide URL base segun esquema HTTP/HTTPS de la pagina (`Frontend/Program.cs`).

## 11) Scripts utiles
Archivo: `run-frontend.fish`

Objetivo:
- Liberar puerto `5126`
- Compilar frontend
- Ejecutar frontend en perfil `http`

Uso:
```fish
fish run-frontend.fish
```

## 12) Troubleshooting
### A) Frontend se queda "pegado" al arrancar
Causa frecuente: puerto `5126` ocupado.

Windows:
```powershell
netstat -ano | findstr :5126
Stop-Process -Id <PID> -Force
```

Linux/macOS:
```bash
lsof -i :5126
kill -9 <PID>
```

### B) `TypeError: Failed to fetch`
Revisar:
1. API levantada en `5000/5001`.
2. URL del frontend en `Frontend/wwwroot/appsettings.json`.
3. CORS permitido en `Api/Program.cs`.
4. Certificado dev HTTPS confiado.

### C) Error PostgreSQL `28P01`
`password authentication failed for user "postgres"`

Acciones:
1. Validar user/password/puerto en `ConnectionStrings:DefaultConnection`.
2. Probar con `psql`.
3. Reiniciar API despues de cambiar credenciales.

### D) Error WASM `MSB4216 / ComputeWasmBuildAssets`
El proyecto incluye workaround en `Frontend/Frontend.csproj` para ciertos builds de SDK 10.0.202.
Si actualizas SDK y deja de ser necesario, puedes retirar ese override.

## 13) Seguridad recomendada
- No subir secretos reales a Git (`Jwt:Key`, SMTP password, Facebook secret, DB password).
- Usar variables de entorno o Secret Manager en desarrollo:

```powershell
dotnet user-secrets init --project Api/Api.csproj
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=login_fb;Username=postgres;Password=***" --project Api/Api.csproj
```

- En produccion, no usar `Database.Migrate()` en arranque sin estrategia de despliegue controlada.

---

Si quieres, puedo preparar una segunda version del README enfocada a despliegue (Docker + pipeline CI/CD + variables por ambiente). 
