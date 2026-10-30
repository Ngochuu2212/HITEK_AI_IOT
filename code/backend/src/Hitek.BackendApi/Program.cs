using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Hubs;
using Hitek.BackendApi.Monitoring;
using Hitek.BackendApi.RiskEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers(o => o.ModelBinderProviders.Insert(0, new UtcDateTimeModelBinderProvider()))
    .AddJsonOptions(o => ApiJson.Configure(o.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidation.ToErrorResponse);

builder.Services.AddSignalR()
    .AddJsonProtocol(o => ApiJson.Configure(o.PayloadSerializerOptions));

builder.Services.AddHitekAuth(builder.Configuration);

// Web Dashboard chạy trên trình duyệt ở một origin khác. Thiếu CORS thì mọi lời gọi từ
// đó đều bị trình duyệt chặn. Client SignalR gửi kèm credentials ở bước negotiate nên
// phải liệt kê origin cụ thể, không dùng được dấu * cho mọi origin.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// Tenant hiện tại lấy từ claim trong token; HitekDbContext dựa vào đây để lọc và đóng dấu dữ liệu.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantProvider, HttpTenantProvider>();

builder.Services.AddDbContext<HitekDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<RiskEngineOptions>(builder.Configuration.GetSection("RiskEngine"));
builder.Services.AddScoped<RiskEngineService>();
builder.Services.Configure<AiServiceOptions>(builder.Configuration.GetSection("AiService"));

// Tiêm đồng hồ thay vì gọi thẳng DateTime.UtcNow, để kiểm thử được các luật phụ
// thuộc thời gian như quy tắc ngoài khung giờ hay thời gian chờ trước khi đóng.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<SensorMonitorOptions>(builder.Configuration.GetSection("SensorMonitor"));
builder.Services.AddHostedService<SensorMonitorService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "HITEK AI-IoT Backend API",
        Version = "v1",
        Description = "Backend API, tiếp nhận dữ liệu IoT và Event & Risk Engine. Lấy token tại " +
                      "POST /api/v1/auth/login (người dùng) hoặc POST /api/v1/auth/token (service), " +
                      "rồi bấm Authorize và dán token vào."
    });

    var bearer = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    o.AddSecurityDefinition("Bearer", bearer);
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = [] });
});

var app = builder.Build();

// Khi chạy bằng Docker Compose, Backend tự dựng bảng của mình lúc khởi động để cả hệ
// thống lên được bằng một lệnh. Chạy tay ngoài Docker thì vẫn dùng dotnet-ef như cũ.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<HitekDbContext>().Database.MigrateAsync();
}

// Chỉ môi trường dev: tài khoản demo do script init của AI Service tạo ra chưa có mật khẩu,
// đặt mật khẩu mặc định để đăng nhập thử được ngay, không phụ thuộc compose nào chạy script nào.
var devPassword = app.Configuration["DevSeed:DefaultPassword"];
if (app.Environment.IsDevelopment() && !string.IsNullOrEmpty(devPassword))
{
    using var scope = app.Services.CreateScope();
    await DevAccounts.SetMissingPasswordsAsync(
        scope.ServiceProvider.GetRequiredService<HitekDbContext>(), devPassword, app.Logger);
}

app.UseMiddleware<ApiExceptionMiddleware>();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "HITEK AI-IoT Backend API");
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AlertsHub>("/hubs/alerts");

app.MapGet("/health", async (HitekDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "ok" })
            : Results.Json(new { status = "database_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous()
    .ExcludeFromDescription();

app.Run();

// Cho WebApplicationFactory trong dự án kiểm thử tích hợp nhìn thấy lớp Program.
public partial class Program;
