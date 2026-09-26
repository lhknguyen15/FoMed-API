using FoMed.Application.DTO;
using FoMed.Application.Services.Clinical;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;

namespace FoMed.Api.Middleware;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var sql = exception as SqlException ?? exception.InnerException as SqlException;
        var status = exception is ClinicException clinic ? clinic.StatusCode :
            sql?.Number is 2601 or 2627 or 1205 or 51001 ? 409 : 500;
        var message = exception is ClinicException ? exception.Message : status == 409
            ? "Dữ liệu đã thay đổi hoặc đang được xử lý. Vui lòng tải lại và thử lại."
            : "Có lỗi khi xử lý yêu cầu.";
        if (status == 500) logger.LogError(exception, "Request failed: {Path}", context.Request.Path);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new HTTPResponseData<object?> { DataResponse = null, Message = message, StatusCode = status }, ct);
        return true;
    }
}
