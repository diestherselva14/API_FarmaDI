
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using FarmaDiApi.Models;
using FarmaDiApi.Services;
using FarmaDiBusiness.DTOs;
using FarmaDiCore.Exceptions;

namespace FarmaDiApi.Middlewares;

public class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AuditLoggingMiddleware(
        RequestDelegate next,
        IHostEnvironment env,
        ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _env = env;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ILogService logService)
    {
        var timer = Stopwatch.StartNew();
        var traceId = context.TraceIdentifier;

        Exception? caughtException = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            caughtException = ex;

            if (context.Response.HasStarted)
            {
                throw;
            }

            await HandleExceptionAsync(context, ex, traceId);
        }
        finally
        {
            timer.Stop();

            var statusCode = context.Response.StatusCode;

            string level;

            if (caughtException is AppException appEx &&
                appEx.StatusCode < 500)
            {
                level = "Warning";
            }
            else if (caughtException != null || statusCode >= 500)
            {
                level = "Error";
            }
            else if (statusCode >= 400)
            {
                level = "Warning";
            }
            else
            {
                level = "Information";
            }

            // Registrar solamente metadatos.
            // No almacenar cuerpos, contraseñas,
            // tokens, parámetros de consulta ni IP.

            var log = new SystemLog
            {
                TraceId = traceId,
                Timestamp = DateTime.UtcNow,
                Environment = _env.EnvironmentName,
                Level = level,
                ClientIp = null,
                UserId = ObtenerIdentificadorUsuario(context),

                Request = new RequestDetail
                {
                    Method = context.Request.Method,
                    Path = context.Request.Path.Value,
                    QueryString = null,
                    Payload = null
                },

                Response = new ResponseDetail
                {
                    StatusCode = statusCode,
                    ElapsedMilliseconds =
                        timer.ElapsedMilliseconds
                }
            };

            // No guardar mensajes internos,
            // trazas de pila ni datos de excepciones.
            log.Exception = null;

            try
            {
                await logService.InsertLogAsync(log);
            }
            catch (Exception)
            {
                // Un fallo de Cosmos DB no debe
                // interrumpir la respuesta de la API.
                _logger.LogWarning(
                    "No se pudo registrar la auditoría. TraceId: {TraceId}",
                    traceId);
            }
        }
    }

    private static async Task HandleExceptionAsync(
        HttpContext context,
        Exception ex,
        string traceId)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.ContentType =
            "application/json; charset=utf-8";

        UnsuccessfulResponseDto responseDto;

        if (ex is AppException appEx)
        {
            context.Response.StatusCode = appEx.StatusCode;

            responseDto = new UnsuccessfulResponseDto
            {
                Code = appEx.StatusCode.ToString(),
                Message = appEx.StatusCode >= 500
                    ? "Ocurrió un error inesperado en el servidor."
                    : appEx.Message,
                Details = new { traceId }
            };
        }
        else
        {
            context.Response.StatusCode =
                StatusCodes.Status500InternalServerError;

            responseDto = new UnsuccessfulResponseDto
            {
                Code = "500",
                Message = "Ocurrió un error inesperado en el servidor.",
                Details = new { traceId }
            };
        }

        var json = JsonSerializer.Serialize(
            responseDto,
            SerializerOptions);

        await context.Response.WriteAsync(json);
    }

    private static string ObtenerIdentificadorUsuario(
        HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return "Anonimo";
        }

        // Registrar solo el identificador.
        // No guardar el token ni datos personales.

        var id =
            context.User.FindFirst(
                ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst(
                JwtRegisteredClaimNames.Sub)?.Value
            ?? context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst("id")?.Value;

        return id ?? "Autenticado";
    }
}
