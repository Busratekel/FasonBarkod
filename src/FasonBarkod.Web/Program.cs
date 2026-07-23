using System.Globalization;
using System.Text;
using FasonBarkod.Core.Entities;
using FasonBarkod.Infrastructure;
using FasonBarkod.Infrastructure.Configuration;
using FasonBarkod.Web.Configuration;
using FasonBarkod.Infrastructure.Data;
using FasonBarkod.Infrastructure.Services;
using FasonBarkod.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var trCulture = new CultureInfo("tr-TR");
CultureInfo.DefaultThreadCurrentCulture = trCulture;
CultureInfo.DefaultThreadCurrentUICulture = trCulture;

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(trCulture);
    options.SupportedCultures = [trCulture];
    options.SupportedUICultures = [trCulture];
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddInfrastructure(connectionString, builder.Configuration);
builder.Services.AddFasonBarkodApiClient(builder.Configuration);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 6;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<FasonBarkod.Web.Filters.QzTrayViewBagFilter>();
});
builder.Services.Configure<TestingOptions>(builder.Configuration.GetSection(TestingOptions.SectionName));
builder.Services.AddRazorPages();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPrintSettingsStore, DbPrintSettingsStore>();
builder.Services.AddScoped<IPrintSettingsAdminService, PrintSettingsAdminService>();
builder.Services.AddSingleton<IQzSigningService, QzSigningService>();
builder.Services.AddScoped<FasonBarkod.Web.Filters.QzTrayViewBagFilter>();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);

using (var scope = app.Services.CreateScope())
{
    var labelTemplateService = scope.ServiceProvider.GetRequiredService<ILabelTemplateService>();
    var printerOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PrinterOptions>>().Value;
    var templateRoot = Path.IsPathRooted(printerOptions.TemplateFolder)
        ? printerOptions.TemplateFolder
        : Path.Combine(AppContext.BaseDirectory, printerOptions.TemplateFolder);
    await labelTemplateService.EnsureSeededFromFilesAsync(templateRoot);
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
}

// İç ağda HTTP ile çalışır; sertifika / HTTPS gerekmez.
app.UseRequestLocalization();
app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Sas}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
