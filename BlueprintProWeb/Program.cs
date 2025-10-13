using BlueprintProWeb.Data;
using BlueprintProWeb.Hubs;
using BlueprintProWeb.Models;
using BlueprintProWeb.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BlueprintProWeb.Settings;
using OpenAI;
using OpenAI.Embeddings;
using System.Text.Json;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BlueprintPro API",
        Version = "v1"
    });

    // JWT Bearer auth so [Authorize] endpoints are visible
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
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});



builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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

// make sure this is included

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var apiKey = cfg["OpenAI:ApiKey"]; // make sure appsettings.json has OpenAI:ApiKey
    if (string.IsNullOrWhiteSpace(apiKey))
        throw new InvalidOperationException("OpenAI:ApiKey not configured.");

    // keep an OpenAIClient if you also use chat, etc.
    var openAiClient = new OpenAIClient(apiKey);
    return openAiClient;
});

// Register EmbeddingClient directly (recommended)
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var apiKey = cfg["OpenAI:ApiKey"];
    // model name is the first arg
    return new EmbeddingClient("text-embedding-3-small", apiKey);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAndroidApp",
        policy =>
        {
            policy.WithOrigins("http://10.0.2.2:8080", "http://localhost:8080") // For emulator
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});


builder.Services.AddSignalR();

builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));


var app = builder.Build();

// Configure the HTTP request pipeline.

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseCors("AllowAndroidApp");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");


app.Run();