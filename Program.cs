using MicroServices.Auth.API.Data;
using MicroServices.Auth.API.Models;
using MicroServices.Auth.API.Service;
using MicroServices.Auth.API.Service.IService;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configuraci�n mejorada de DbContext con pol�tica de reintentos
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var config = serviceProvider.GetRequiredService<IConfiguration>();
    options.UseSqlServer(
        config.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null
        )
    );
});

// Configuraci�n de Identity con pol�ticas de contrase�a
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// Configuraci�n de JWT
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("ApiSettings:JwtOptions"));

// Configuraci�n robusta de autenticaci�n JWT
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["ApiSettings:JwtOptions:Issuer"],
        ValidAudience = builder.Configuration["ApiSettings:JwtOptions:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["ApiSettings:JwtOptions:Secret"]))
    };

    // Manejo mejorado de eventos
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Console.WriteLine("Token validated successfully");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddControllers();

// Registro de servicios
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

// Configuraci�n avanzada de Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Microservicio de Autenticaci�n",
        Version = "v1",
        Description = "API para gesti�n de autenticaci�n y autorizaci�n",
        Contact = new OpenApiContact
        {
            Name = "Equipo de Desarrollo",
            Email = "soporte@tudominio.com"
        }
    });

    // Configurar seguridad JWT en Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header usando el esquema Bearer.\r\n\r\n" +
                      "Ingresa 'Bearer' [espacio] y luego tu token.\r\n\r\n" +
                      "Ejemplo: \"Bearer 12345abcdef\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // Incluir comentarios XML (si est�n disponibles)
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Configuraci�n de CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Aplicar migraciones autom�ticas al inicio con manejo robusto
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Iniciando aplicaci�n de migraciones para Auth API...");
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Esperar a que SQL Server est� disponible (especialmente en Docker)
        if (!dbContext.Database.CanConnect())
        {
            logger.LogWarning("Base de datos no disponible. Reintentando en 5 segundos...");
            Thread.Sleep(5000);
        }

        // Verificar migraciones pendientes
        var pendingMigrations = dbContext.Database.GetPendingMigrations().ToList();
        if (pendingMigrations.Any())
        {
            logger.LogInformation("Aplicando {count} migraciones...", pendingMigrations.Count);
            dbContext.Database.Migrate();
            logger.LogInformation("Migraciones aplicadas exitosamente");

            // Verificar si se necesita crear roles iniciales
            await EnsureRolesCreated(scope.ServiceProvider);
        }
        else
        {
            logger.LogInformation("No hay migraciones pendientes");
        }

        // Verificaci�n final de conexi�n
        if (dbContext.Database.CanConnect())
        {
            logger.LogInformation("Conexi�n con SQL Server establecida correctamente");
        }
        else
        {
            logger.LogError("Error cr�tico: No se pudo conectar a la base de datos");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error fatal durante la aplicaci�n de migraciones");
        throw; // Detener la aplicaci�n si hay errores cr�ticos
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(s =>
    {
        s.SwaggerEndpoint("/swagger/v1/swagger.json", "Auth API v1");
        s.RoutePrefix = string.Empty;
        s.ConfigObject.AdditionalItems["syntaxHighlight"] = new Dictionary<string, object>
        {
            ["activated"] = false // Mejora rendimiento en Swagger UI
        };
    });
}
else
{
    app.UseExceptionHandler("/error"); // Manejo de errores en producci�n
}

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Map("/error", () => Results.Problem()); // Endpoint para manejo de errores

app.Run();

// Funci�n para asegurar que los roles b�sicos est�n creados
async Task EnsureRolesCreated(IServiceProvider serviceProvider)
{
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var roles = new[] { "Admin", "User", "Guest" };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
                logger.LogInformation("Rol '{Role}' creado exitosamente", role);
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error al crear roles iniciales");
    }
}