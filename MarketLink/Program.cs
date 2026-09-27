using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using MarketLink.Models;
using MarketLink.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString =
    builder.Configuration.GetConnectionString("ApplicationDbContextConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'userDbContextConnection' not found."
    );

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString)
);

builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<ICustomerRecommendationService, CustomerRecommendationService>();
builder.Services.AddScoped<IIntelligentSearchService, IntelligentSearchService>();
builder.Services.AddScoped<IMarketLinkAssistantService, MarketLinkAssistantService>();

builder.Services.AddHttpClient<IGroqAssistantService, GroqAssistantService>(client =>
{
    var baseUrl = builder.Configuration["Groq:BaseUrl"]
        ?? "https://api.groq.com/openai/v1/";

    client.BaseAddress = new Uri(
        baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/"
    );

    client.Timeout = TimeSpan.FromSeconds(45);
});

builder.Services.AddHttpClient<IMarketLinkAiService, MarketLinkAiService>(client =>
{
    var baseUrl = builder.Configuration["AiService:BaseUrl"]
        ?? "http://127.0.0.1:8001/";

    client.BaseAddress = new Uri(
        baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/"
    );

    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedService<LocalAiHostedService>();
builder.Services.AddScoped<IAssistantActionService, AssistantActionService>();
builder.Services.AddHostedService<RestockAlertWorker>();
builder.Services.AddScoped<IAnomalyDetectionService, AnomalyDetectionService>();
builder.Services.AddHostedService<AnomalyDetectionWorker>();
builder.Services.AddScoped<IWasteRiskService, WasteRiskService>();
builder.Services.AddHostedService<WasteRiskWorker>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT Key is missing.");

        var jwtIssuer = builder.Configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("JWT Issuer is missing.");

        var jwtAudience = builder.Configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("JWT Audience is missing.");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),

            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token))
                {
                    context.Token =
                        context.Request.Cookies["accessToken"];
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddControllersWithViews();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider
        .GetRequiredService<ApplicationDbContext>();

    if (!await context.roles.AnyAsync())
    {
        context.roles.AddRange(
            new Role
            {
                RoleName = "Customer",
                Description = "MarketLink customer"
            },
            new Role
            {
                RoleName = "Farmer",
                Description = "MarketLink farmer"
            },
            new Role
            {
                RoleName = "Admin",
                Description = "MarketLink administrator"
            }
        );

        await context.SaveChangesAsync();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.Run();