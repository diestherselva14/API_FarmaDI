using FarmaDi.DataAccess.Repositories;
using FarmaDiApi.Configurations;
using FarmaDiApi.Middlewares;
using FarmaDiApi.Services;
using FarmaDiBusiness.Interfaces;
using FarmaDiBusiness.Services;
using FarmaDiDataAccess.Interfaces;
using FarmaDiDataAccess.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var JwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = JwtSettings["SecretKey"];


builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSwaggerGen(options =>
{
    // 1. Define el esquema de seguridad (Security Scheme)
    // Esto le dice a Swagger que la API usa autenticación "Bearer" (JWT).
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", // El nombre del header HTTP
        Type = SecuritySchemeType.ApiKey, // Tipo de esquema
        Scheme = "bearer", // El nombre del esquema (debe ser minúscula)
        BearerFormat = "JWT", // Formato del token
        In = ParameterLocation.Header, // Dónde se envía el token
        Description = "Introduce tu token JWT usando este formato: Bearer {token}"
    });

    // 2. Añade el requisito de seguridad (Security Requirement)
    // Esto le dice a Swagger que debe aplicar el esquema "Bearer" a los endpoints.
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
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
            new string[] {}
        }
    });
});


//Configuracion del servicio de Autenticacion
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = JwtSettings["Issuer"],

        ValidateAudience = true,
        ValidAudience = JwtSettings["Audience"],

        ValidateLifetime = true,

        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!))
    };
});

builder.Services.Configure<MongoLogSettings>(
    builder.Configuration.GetSection("MongoLogSettings"));

builder.Services.AddSingleton<ILogService, MongoLogService>();



builder.Services.AddScoped<IBrandsService, BrandsService>();
builder.Services.AddScoped<IBrandsRepository, BrandsRepository>();


builder.Services.AddScoped<ICategoriesService, CategoriesService>();
builder.Services.AddScoped<ICategoriesRepository, CategoriesRepository>();


builder.Services.AddScoped<IPresentationService, PresentationService>();
builder.Services.AddScoped<IPresentationRepository, PresentationRepository>();

builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IStockRepository, StockRepository>();

builder.Services.AddScoped<IInventoryLossService, InventoryLossService>();
builder.Services.AddScoped<IInventoryLossRepository, InventoryLossRepository>();

builder.Services.AddScoped<IProductBatchesService, ProductBatchesService>();
builder.Services.AddScoped<IProductBatchesRepository, ProductBatchesRepository>();


builder.Services.AddScoped<IRolService, RolesService>();
builder.Services.AddScoped<IRolesRepository, RolesRepository>();


builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<ISuppliersRepository, SuppliersRepository>();


builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAuthRepository, AuthRepository>();


builder.Services.AddScoped<IProductService, ProductsService>();
builder.Services.AddScoped<IProductsRepository, ProductsRepository>();


builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();

builder.Services.AddScoped<IPurchaseService, PurchaseService>();
builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();

builder.Services.AddScoped<ISaleService, SaleService>();
builder.Services.AddScoped<ISalesRepository, SalesRepository>();

builder.Services.AddScoped<IUsersService, UsersService>();
builder.Services.AddScoped<IUsersRepository, UsersRepository>();

builder.Services.AddScoped<IKPIDashboardService, KPIDashboardService>();
builder.Services.AddScoped<IKPIDashboardRepository, DashboardRepository>();


builder.Services.AddScoped<IConcentrationServices, ConcentrationServices>();
builder.Services.AddScoped<IConcentrationRepository, ConcentrationRepository>();


builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirFrontend", policy =>
    {
        policy.AllowAnyOrigin()    // Permite que tu LiveServer (puerto 5500) se conecte
              .AllowAnyHeader()    // Permite que se envíe contenido JSON
              .AllowAnyMethod();   // Permite usar POST, GET, PUT, etc.
    });
});
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseCors("PermitirFrontend");

app.UseMiddleware<AuditLoggingMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();
