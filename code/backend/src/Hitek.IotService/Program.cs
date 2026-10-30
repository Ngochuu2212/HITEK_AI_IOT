using System.Security.Claims;
using System.Text;
using Hitek.IotService;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

// IoT Service: cầu nối mỏng giữa lớp thiết bị và Backend API. Không kết nối database, không
// chứa logic đánh giá rủi ro — chỉ nhận (MQTT là kênh chính, HTTP là đường dự phòng),
// chuẩn hoá, rồi chuyển sang Backend.
var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));
builder.Services.Configure<BackendApiOptions>(builder.Configuration.GetSection("BackendApi"));
builder.Services.Configure<IngestOptions>(builder.Configuration.GetSection("Ingest"));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IngestStats>();
builder.Services.AddSingleton<ReadingNormalizer>();
builder.Services.AddSingleton<IngestPipeline>();
builder.Services.AddSingleton<BackendTokenProvider>();
builder.Services.AddTransient<BackendAuthHandler>();

void ConfigureBackendClient(IServiceProvider sp, HttpClient client)
{
    var options = sp.GetRequiredService<IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
}

builder.Services.AddHttpClient(BackendTokenProvider.AuthClientName, ConfigureBackendClient);
builder.Services.AddHttpClient(BackendForwarder.ClientName, ConfigureBackendClient)
    .AddHttpMessageHandler<BackendAuthHandler>();

builder.Services.AddHostedService<MqttListener>();
builder.Services.AddHostedService<BackendForwarder>();

// Bên gọi đường HTTP dự phòng phải có token service do Backend phát (cùng khoá ký với Backend).
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var keyBytes = Encoding.UTF8.GetBytes(jwt.SigningKey);
if (keyBytes.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey phải trùng khoá ký của Backend và dài tối thiểu 32 byte. Đặt qua biến môi trường Jwt__SigningKey.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "role",
            NameClaimType = "name"
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await ApiError.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "UNAUTHORIZED",
                    "Thiếu hoặc sai token. Lấy token service tại POST /api/v1/auth/token của Backend.");
            },
            OnForbidden = context => ApiError.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, "FORBIDDEN",
                "Chỉ token service mới được gửi dữ liệu cảm biến.")
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Ingest", p => p.RequireRole("service"));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Đường HTTP dự phòng cho thiết bị hoặc công cụ không dùng được MQTT. Nội dung giống hệt tin
// MQTT ({ value, recordedAt }), mã cảm biến nằm trên đường dẫn như nằm trên topic, và đi qua
// đúng hàm xử lý của đường MQTT.
app.MapPost("/api/v1/readings/{sensorId:guid}", async (
        Guid sensorId, HttpRequest request, ClaimsPrincipal user,
        IngestPipeline pipeline, BackendTokenProvider tokens, CancellationToken ct) =>
    {
        // IoT Service chuyển tiếp bằng token của chính nó, nên token của tenant khác không
        // được dùng để đẩy dữ liệu vào tenant của IoT Service.
        if (tokens.TenantId is { } ownTenant && user.FindFirst("tenant_id")?.Value != ownTenant.ToString())
            return ApiError.Result(StatusCodes.Status403Forbidden, "TENANT_MISMATCH", "Token thuộc tenant khác với tenant của IoT Service.");

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        var result = pipeline.Accept(sensorId, buffer.ToArray(), "http");

        return result.IsValid
            ? Results.Accepted(value: new { sensorId, recordedAt = result.Reading!.RecordedAt, receivedAt = result.Reading.ReceivedAt })
            : ApiError.Result(StatusCodes.Status400BadRequest, "INVALID_READING", result.Error!);
    })
    .RequireAuthorization("Ingest");

app.MapGet("/api/v1/status", (IngestPipeline pipeline) => Results.Ok(new
    {
        mqttConnected = pipeline.Stats.MqttConnected,
        queueLength = pipeline.QueueLength,
        received = pipeline.Stats.Received,
        rejected = pipeline.Stats.Rejected,
        forwarded = pipeline.Stats.Forwarded,
        refusedByBackend = pipeline.Stats.RefusedByBackend,
        dropped = pipeline.Stats.Dropped,
        retries = pipeline.Stats.Retries,
        lastForwardedAt = pipeline.Stats.LastForwardedAt,
        lastBackendError = pipeline.Stats.LastBackendError
    }))
    .RequireAuthorization("Ingest");

app.MapGet("/health", (IngestPipeline pipeline, IOptions<MqttOptions> mqtt) =>
    Results.Ok(new
    {
        status = "ok",
        mqttConnected = pipeline.Stats.MqttConnected || !mqtt.Value.Enabled,
        queueLength = pipeline.QueueLength
    }));

app.Run();

public partial class Program;

internal static class ApiError
{
    // Cùng khung lỗi { error: { code, message } } với Backend.
    public static IResult Result(int status, string code, string message) =>
        Results.Json(new { error = new { code, message } }, statusCode: status);

    public static Task WriteAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = new { code, message } });
    }
}
