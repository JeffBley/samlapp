using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SamlSample.Web.Data;
using SamlSample.Web.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=samlsample.db"));

builder.Services.AddHttpClient("metadata", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISamlConfigurationService, SamlConfigurationService>();
builder.Services.AddScoped<ISecretHashingService, Pbkdf2SecretHashingService>();
builder.Services.AddSingleton<SamlMetadataParser>();
builder.Services.AddScoped<BootstrapDataService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Welcome";
        options.AccessDeniedPath = "/Home/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();

// Rate limiting: defend SAML ACS and bootstrap login endpoints against abuse
builder.Services.AddRateLimiter(options =>
{
    // SAML ACS: 20 assertion posts per IP per minute
    options.AddFixedWindowLimiter("saml-acs", policy =>
    {
        policy.Window = TimeSpan.FromMinutes(1);
        policy.PermitLimit = 20;
        policy.QueueLimit = 0;
        policy.AutoReplenishment = true;
    });
    // Bootstrap login: 10 attempts per IP per minute (dev/localhost only)
    options.AddFixedWindowLimiter("bootstrap-login", policy =>
    {
        policy.Window = TimeSpan.FromMinutes(1);
        policy.PermitLimit = 10;
        policy.QueueLimit = 0;
        policy.AutoReplenishment = true;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var bootstrapDataService = scope.ServiceProvider.GetRequiredService<BootstrapDataService>();
    var baseUrl = app.Urls.FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                  ?? app.Urls.FirstOrDefault();
    await bootstrapDataService.EnsureInitializedAsync(baseUrl);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'sameorigin'";
    await next();
});

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
