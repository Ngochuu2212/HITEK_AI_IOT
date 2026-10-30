using System.Text.Json;

namespace Hitek.BackendApi.Common;

// Một chỗ duy nhất ghi khung lỗi { error: { code, message } }. Middleware bắt ngoại lệ
// và phần xác thực (401/403) đều đi qua đây, để mọi lỗi trả về cùng một hình dạng dù
// phát sinh ở tầng nào.
public static class ApiErrorWriter
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static Task WriteAsync(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new ApiErrorEnvelope(new ApiErrorBody(code, message));
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Options));
    }
}
