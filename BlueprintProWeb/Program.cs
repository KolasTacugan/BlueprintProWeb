using BlueprintProWeb.Data;
using BlueprintProWeb.Hubs;
using BlueprintProWeb.Models;
using BlueprintProWeb.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Embeddings;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using BlueprintProWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// ======================================================
// 1. ADD SERVICES
// ======================================================

// MVC + JSON settings
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// Custom services
builder.Services.AddScoped<ImageService>();

// Swagger + JWT
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BlueprintPro API",
        Version = "v1"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT token"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Identity
builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;

    options.User.RequireUniqueEmail = true;

    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
    options.SignIn.RequireConfirmedPhoneNumber = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// CORS for Android + Azure
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAndroidApp", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// SignalR
builder.Services.AddSignalR();

// Stripe
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));

// OpenAI services
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var apiKey = cfg["OpenAI:ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
        throw new InvalidOperationException("OpenAI:ApiKey not configured.");

    return new OpenAIClient(apiKey);
});

builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    return new EmbeddingClient("text-embedding-3-small", cfg["OpenAI:ApiKey"]);
});

// ======================================================
// 2. BUILD APP
// ======================================================

var app = builder.Build();

// ======================================================
// 3. MIDDLEWARE PIPELINE
// ======================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Swagger enabled on Azure + local
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// CORS for Android App
app.UseCors("AllowAndroidApp");

app.UseAuthentication();
app.UseAuthorization();

// ======================================================
// 4. ROUTES
// ======================================================

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

// API Controllers
app.MapControllers();

// SignalR Hubs
app.MapHub<ChatHub>("/chatHub");
app.MapHub<NotificationHub>("/notificationHub");

// ======================================================
app.Run();
