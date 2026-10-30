using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Hitek.BackendApi.Common;

public static class ApiJson
{
    // Dùng chung cho REST lẫn SignalR, để một cảnh báo đẩy qua kênh realtime có đúng
    // hình dạng như khi đọc qua GET /api/v1/alerts.
    //
    // Enum ghi thành chuỗi chữ hoa (CRITICAL, PIR, ACKNOWLEDGED) theo đúng contract đã
    // thống nhất liên nhóm, thay vì số thứ tự mặc định của .NET — Dashboard nhận về
    // "level": 3 thì không ai đọc hiểu, và đổi thứ tự enum là âm thầm đổi nghĩa dữ liệu.
    // Chiều đọc vẫn chấp nhận không phân biệt hoa thường.
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new UtcDateTimeJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
    }
}

public static class ApiValidation
{
    // [ApiController] mặc định trả lỗi validation dưới dạng ProblemDetails, lệch với khung
    // lỗi chung { error: { code, message } } mà mọi bên gọi đang dựa vào.
    public static IActionResult ToErrorResponse(ActionContext context)
    {
        var entries = context.ModelState.Where(entry => entry.Value is { Errors.Count: > 0 }).ToList();

        // Một trường JSON sai kiểu kéo theo lỗi "The request field is required." cho cả tham số
        // body. Lỗi thứ hai chỉ gây rối, bỏ đi khi đã có lỗi chỉ đúng vào trường.
        var hasFieldErrors = entries.Any(entry => entry.Key.StartsWith('$'));

        var messages = entries
            .Where(entry => !hasFieldErrors || entry.Key.StartsWith('$'))
            .SelectMany(entry => entry.Value!.Errors.Select(error =>
            {
                var text = string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? error.Exception?.Message ?? "Giá trị không hợp lệ."
                    : error.ErrorMessage;

                // Thông báo gốc của System.Text.Json lộ tên kiểu .NET bên trong, không giúp gì bên gọi.
                if (text.StartsWith("The JSON value could not be converted", StringComparison.Ordinal))
                    text = "Giá trị sai kiểu hoặc sai định dạng.";
                else if (text.Contains("is an invalid", StringComparison.Ordinal) || text.Contains("LineNumber", StringComparison.Ordinal))
                    text = "Nội dung không phải JSON hợp lệ (kiểm tra cú pháp và mã hoá UTF-8).";

                var field = entry.Key.TrimStart('$', '.');
                return string.IsNullOrEmpty(field) ? text : $"{field}: {text}";
            }))
            .Distinct()
            .ToList();

        var message = messages.Count > 0 ? string.Join(" ", messages) : "Dữ liệu gửi lên không hợp lệ.";
        return new BadRequestObjectResult(new ApiErrorEnvelope(new ApiErrorBody("VALIDATION_ERROR", message)));
    }
}
