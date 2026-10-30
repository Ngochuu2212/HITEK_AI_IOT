using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Hitek.BackendApi.Common;

// Quy ước chung là mọi mốc thời gian đều UTC, nhưng bên gửi không phải lúc nào cũng
// viết theo đúng một kiểu. Python in giờ UTC thành "+00:00" thay vì "Z", thiết bị có
// thể gửi kèm múi giờ địa phương. System.Text.Json đọc mọi chuỗi có offset khác "Z"
// thành DateTime kiểu Local, còn Npgsql từ chối ghi DateTime Local vào cột timestamptz
// — kết quả là lỗi 500 ngay ở lần tích hợp đầu tiên với AI Service.
//
// Đổi hết về UTC ở cửa vào để phần còn lại của hệ thống chỉ phải biết một kiểu.
// Chuỗi không kèm múi giờ được coi là UTC, đúng theo quy ước chung.
public static class UtcDateTimeParser
{
    public static bool TryParse(string? text, out DateTime utc)
    {
        if (!string.IsNullOrWhiteSpace(text) &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
        {
            utc = value.UtcDateTime;
            return true;
        }

        utc = default;
        return false;
    }

    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (!UtcDateTimeParser.TryParse(text, out var utc))
            throw new JsonException($"'{text}' không phải mốc thời gian ISO 8601 hợp lệ.");
        return utc;
    }

    // Luôn ghi ra có hậu tố "Z", kể cả khi giá trị lỡ mang kiểu Unspecified.
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(UtcDateTimeParser.AsUtc(value).ToString("O", CultureInfo.InvariantCulture));
}

// Query string đi qua model binding chứ không qua JSON, và binder mặc định cũng đổi
// "...Z" thành giờ Local. Không có binder này thì ?from=...Z làm lỗi 500 y hệt bên trên.
public sealed class UtcDateTimeModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None) return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var text = result.FirstValue;
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;

        if (UtcDateTimeParser.TryParse(text, out var utc))
            bindingContext.Result = ModelBindingResult.Success(utc);
        else
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName, $"'{text}' không phải mốc thời gian ISO 8601 hợp lệ.");

        return Task.CompletedTask;
    }
}

public sealed class UtcDateTimeModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.UnderlyingOrModelType == typeof(DateTime) &&
        context.BindingInfo.BindingSource != BindingSource.Body
            ? new UtcDateTimeModelBinder()
            : null;
}
