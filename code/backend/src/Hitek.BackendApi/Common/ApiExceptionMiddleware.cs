using System.Net;

namespace Hitek.BackendApi.Common;

public class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException ex)
        {
            await ApiErrorWriter.WriteAsync(context, (int)ex.StatusCode, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            await ApiErrorWriter.WriteAsync(context, (int)HttpStatusCode.InternalServerError,
                "INTERNAL_ERROR", "Đã có lỗi hệ thống xảy ra.");
        }
    }
}
