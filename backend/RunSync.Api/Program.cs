// Program.cs
// Application entry point and composition root for RunSync API.
// Uses the ASP.NET Core 8 minimal hosting model (no Startup.cs).
//
// Wiring order matters:
//   1. Service registration (DI container)
//   2. Middleware pipeline (order is critical — exception handler must be first)
//
// All secrets (JWT key, DB connection string, Strava credentials) come from:
//   - Local dev:    appsettings.Development.json (git-ignored)
//   - Production:   AWS Secrets Manager → Elastic Beanstalk environment variables
//
// → See appsettings.json for the configuration structure (values are empty strings)
// → See ExceptionHandlingMiddleware.cs for the global error handler
// → See RunSyncDbContext.cs for the database setup

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RunSync.Api.Data;
using RunSync.Api.Middleware;
using RunSync.Api.Models.Config;
using RunSync.Api.Services;
using RunSync.Api.Services.Interfaces;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ── 1. Configuration ──────────────────────────────────────────────────────────

builder.Services.Configure<StravaConfig>(
    builder.Configuration.GetSection("Strava"));

// ── 2. Database ───────────────────────────────────────────────────────────────

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Database connection string 'DefaultConnection' is not configured.");

// MySqlServerVersion avoids a live connection at startup/migration time (AutoDetect would connect).
// Update the version number if your MySQL server is newer than 8.0.
builder.Services.AddDbContext<RunSyncDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0))));

// ── 3. Application Services (DI Registration) ─────────────────────────────────

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IStravaService, StravaService>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<ITrainingPlanService, TrainingPlanService>();

// HttpClient factory for StravaService — avoids socket exhaustion from newing HttpClient
builder.Services.AddHttpClient();

// ── 4. Authentication (JWT Bearer) ───────────────────────────────────────────

string jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT signing key 'Jwt:Key' is not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            // Reject tokens within 5 seconds of expiry to avoid race conditions
            ClockSkew = TimeSpan.FromSeconds(5)
        };
    });

builder.Services.AddAuthorization();

// ── 5. CORS ───────────────────────────────────────────────────────────────────

string allowedOrigin = builder.Configuration["Cors:AllowedOrigin"]
    ?? throw new InvalidOperationException("CORS allowed origin 'Cors:AllowedOrigin' is not configured.");

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod()
              // Required so the frontend can read the Authorization header from responses
              .WithExposedHeaders("Authorization");
    });
});

// ── 6. Controllers + Swagger ──────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RunSync API",
        Version = "v1",
        Description = "Backend API for syncing Strava activities to a half marathon training plan."
    });

    // Add the "Authorize" button to Swagger UI so JWT tokens can be tested directly
    OpenApiSecurityScheme jwtSecurityScheme = new()
    {
        BearerFormat = "JWT",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        Description = "Enter your JWT token (without 'Bearer ' prefix)",
        Reference = new OpenApiReference
        {
            Id = JwtBearerDefaults.AuthenticationScheme,
            Type = ReferenceType.SecurityScheme
        }
    };

    options.AddSecurityDefinition(jwtSecurityScheme.Reference.Id, jwtSecurityScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { jwtSecurityScheme, Array.Empty<string>() }
    });
});

// ── 7. Build + Configure Pipeline ─────────────────────────────────────────────

WebApplication app = builder.Build();

// Apply any pending EF Core migrations automatically on startup.
// Safe to run on every restart — EF skips migrations already recorded in __EFMigrationsHistory.
using (IServiceScope scope = app.Services.CreateScope())
{
    RunSyncDbContext db = scope.ServiceProvider.GetRequiredService<RunSyncDbContext>();
    await db.Database.MigrateAsync();
}

// Exception handler must be first — it wraps the entire pipeline
app.UseExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "RunSync API v1");
        c.RoutePrefix = string.Empty; // Swagger at root: http://localhost:5000/
    });
}

app.UseHttpsRedirection();

// CORS must come before Authentication/Authorization
app.UseCors("FrontendPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Partial class declaration enables WebApplicationFactory in integration tests
public partial class Program { }
