using System.Text;
using FasonBarkod.Api.Configuration;
using FasonBarkod.Api.Middleware;
using FasonBarkod.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.Configure<ApiSecurityOptions>(
    builder.Configuration.GetSection(ApiSecurityOptions.SectionName));

builder.Services.AddInfrastructure(connectionString, builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Migration Web projesinde uygulanır (aynı veritabanı paylaşılır).

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// İç ağda HTTP ile çalışır; sertifika / HTTPS gerekmez.
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();
