using System.Net;

namespace Hitek.BackendApi.Common;

// Thrown by controllers/services to produce the standard { error: { code, message } } envelope.
public class ApiException(HttpStatusCode statusCode, string code, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;

    public static ApiException NotFound(string code, string message) => new(HttpStatusCode.NotFound, code, message);
    public static ApiException Conflict(string code, string message) => new(HttpStatusCode.Conflict, code, message);
    public static ApiException BadRequest(string code, string message) => new(HttpStatusCode.BadRequest, code, message);
    public static ApiException Unauthorized(string code, string message) => new(HttpStatusCode.Unauthorized, code, message);
}

public record ApiErrorBody(string Code, string Message);
public record ApiErrorEnvelope(ApiErrorBody Error);
