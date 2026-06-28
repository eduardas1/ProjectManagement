using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Workboard.Infrastructure;
using Workboard.Infrastructure.Data;
using Workboard.Application.Interfaces;
using Workboard.Application.Projects;
using Workboard.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestLineSize = 32768);
var storageDirectory = Path.GetFullPath(builder.Configuration["Storage:Directory"] ?? "App_Data",
    builder.Environment.ContentRootPath);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddMediatR(options => options.RegisterServicesFromAssemblyContaining<CreateProjectCommand>());
builder.Services.AddInfrastructure(storageDirectory);
TimeZoneInfo reportingTimeZone;
try
{
    reportingTimeZone = TimeZoneInfo.FindSystemTimeZoneById(builder.Configuration["Reporting:TimeZone"] ?? "Europe/Vilnius");
}
catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
{
    Console.Error.WriteLine("Reporting:TimeZone must name an installed time zone.");
    return 1;
}
builder.Services.AddSingleton(provider => new Workboard.Application.Reports.ReportingCalendar(
    provider.GetRequiredService<TimeProvider>(), reportingTimeZone));
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAuthorization(options => options.FallbackPolicy =
    new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = ".Workboard.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Home/Status/404";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest") context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest") context.Response.StatusCode = StatusCodes.Status403Forbidden;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = ".Workboard.Antiforgery";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

await using var app = builder.Build();
try
{
    await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);
}
catch (Exception exception)
{
    app.Logger.LogError(exception, "Unable to initialize application storage and accounts.");
    return 1;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

await app.RunAsync();
return 0;
