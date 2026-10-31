
using FarmaDiApi.Configurations;
using FarmaDiApi.Models;
using FarmaDiCore.Common;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FarmaDiApi.Services;

public interface ILogService
{
    Task InsertLogAsync(SystemLog log);

    Task<ServiceResponse<(IEnumerable<SystemLog> Items, int TotalCount)>>
        GetLogsPagedAsync(
            int page,
            int limit,
            string? level = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            CancellationToken cancellationToken = default);

    Task<ServiceResponse<(IEnumerable<SystemLog> Items, int TotalCount)>>
        GetLogsByLevelPagedAsync(
            string level,
            int page,
            int limit,
            CancellationToken cancellationToken = default);

    Task<ServiceResponse<(IEnumerable<SystemLog> Items, int TotalCount)>>
        GetLogsByDateRangePagedAsync(
            DateTime startDate,
            DateTime endDate,
            int page,
            int limit,
            CancellationToken cancellationToken = default);

    Task<ServiceResponse<(IEnumerable<SystemLog> Items, int TotalCount)>>
        GetLogsTodayPagedAsync(
            int page,
            int limit,
            CancellationToken cancellationToken = default);
}

public class MongoLogService : ILogService
{
    private readonly IMongoCollection<SystemLog>? _collection;
    private readonly ILogger<MongoLogService> _logger;

    public MongoLogService(
        IOptions<MongoLogSettings> options,
        ILogger<MongoLogService> logger)
    {
        _logger = logger;

        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.ConnectionString) ||
            string.IsNullOrWhiteSpace(settings.DatabaseName) ||
            string.IsNullOrWhiteSpace(settings.CollectionName))
        {
            _logger.LogWarning(
                "Configuracion MongoLogSettings incompleta.");

            return;
        }

        try
        {
            var client = new MongoClient(
                settings.ConnectionString);

            var database = client.GetDatabase(
                settings.DatabaseName);

            _collection =
                database.GetCollection<SystemLog>(
                    settings.CollectionName);

            // No crear indices automaticamente.
            // En Azure Cosmos DB los indices y TTL
            // se administran por separado.
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "No se pudo inicializar el servicio de auditoria MongoDB.");
        }
    }

    public async Task InsertLogAsync(SystemLog log)
    {
        if (_collection == null)
        {
            throw new InvalidOperationException(
                "La coleccion de auditoria no esta configurada.");
        }

        // Propagar fallos de escritura para que
        // AuditLoggingMiddleware pueda registrarlos
        // sin interrumpir la respuesta HTTP.

        await _collection.InsertOneAsync(log);
    }

    public async Task<ServiceResponse<(
        IEnumerable<SystemLog> Items,
        int TotalCount)>> GetLogsPagedAsync(
            int page,
            int limit,
            string? level = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            CancellationToken cancellationToken = default)
    {
        if (_collection == null)
        {
            return ErrorResponse(
                "La coleccion de auditoria no esta configurada.");
        }

        try
        {
            page = Math.Max(page, 1);
            limit = Math.Clamp(limit, 1, 100);

            var builder = Builders<SystemLog>.Filter;
            var filter = builder.Empty;

            if (!string.IsNullOrWhiteSpace(level))
            {
                var validLevels = new[]
                {
                    "Information",
                    "Warning",
                    "Error"
                };

                var matchedLevel = validLevels
                    .FirstOrDefault(x =>
                        x.Equals(
                            level.Trim(),
                            StringComparison.OrdinalIgnoreCase));

                if (matchedLevel == null)
                {
                    return new ServiceResponse<(
                        IEnumerable<SystemLog> Items,
                        int TotalCount)>
                    {
                        Data = (Array.Empty<SystemLog>(), 0),
                        IsSuccess = true,
                        MessageCode = MessageCodes.Success,
                        Message = "No se encontraron registros."
                    };
                }

                filter &= builder.Eq(
                    x => x.Level, matchedLevel);
            }

            if (startDate.HasValue)
            {
                filter &= builder.Gte(
                    x => x.Timestamp,
                    ConvertirUtc(startDate.Value));
            }

            if (endDate.HasValue)
            {
                filter &= builder.Lte(
                    x => x.Timestamp,
                    ConvertirUtc(endDate.Value));
            }

            var totalLong =
                await _collection.CountDocumentsAsync(
                    filter,
                    cancellationToken: cancellationToken);

            var totalCount =
                (int)Math.Min(totalLong, int.MaxValue);

            var skipLong = ((long)page - 1) * limit;

            if (skipLong > int.MaxValue)
            {
                return new ServiceResponse<(
                    IEnumerable<SystemLog> Items,
                    int TotalCount)>
                {
                    Data = (Array.Empty<SystemLog>(), totalCount),
                    IsSuccess = true,
                    MessageCode = MessageCodes.Success,
                    Message = "Pagina fuera del rango disponible."
                };
            }

            var logs = await _collection
                .Find(filter)
                .SortByDescending(x => x.Timestamp)
                .Skip((int)skipLong)
                .Limit(limit)
                .ToListAsync(cancellationToken);

            return new ServiceResponse<(
                IEnumerable<SystemLog> Items,
                int TotalCount)>
            {
                Data = (logs, totalCount),
                IsSuccess = true,
                MessageCode = MessageCodes.Success,
                Message = "Logs obtenidos correctamente."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "No se pudieron consultar los registros de auditoria.");

            return ErrorResponse(
                "Error al consultar los registros de auditoria.");
        }
    }

    public Task<ServiceResponse<(
        IEnumerable<SystemLog> Items,
        int TotalCount)>> GetLogsByLevelPagedAsync(
            string level,
            int page,
            int limit,
            CancellationToken cancellationToken = default)
    {
        return GetLogsPagedAsync(
            page,
            limit,
            level: level,
            cancellationToken: cancellationToken);
    }

    public Task<ServiceResponse<(
        IEnumerable<SystemLog> Items,
        int TotalCount)>> GetLogsByDateRangePagedAsync(
            DateTime startDate,
            DateTime endDate,
            int page,
            int limit,
            CancellationToken cancellationToken = default)
    {
        var startUtc = ConvertirUtc(startDate.Date);
        var endUtc = ConvertirUtc(
            endDate.Date.AddDays(1).AddTicks(-1));

        return GetLogsPagedAsync(
            page,
            limit,
            startDate: startUtc,
            endDate: endUtc,
            cancellationToken: cancellationToken);
    }

    public Task<ServiceResponse<(
        IEnumerable<SystemLog> Items,
        int TotalCount)>> GetLogsTodayPagedAsync(
            int page,
            int limit,
            CancellationToken cancellationToken = default)
    {
        var startUtc = DateTime.UtcNow.Date;
        var endUtc = startUtc.AddDays(1).AddTicks(-1);

        return GetLogsPagedAsync(
            page,
            limit,
            startDate: startUtc,
            endDate: endUtc,
            cancellationToken: cancellationToken);
    }

    private static DateTime ConvertirUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        if (value.Kind == DateTimeKind.Local)
        {
            return value.ToUniversalTime();
        }

        return DateTime.SpecifyKind(
            value,
            DateTimeKind.Utc);
    }

    private static ServiceResponse<(
        IEnumerable<SystemLog> Items,
        int TotalCount)> ErrorResponse(string message)
    {
        return new ServiceResponse<(
            IEnumerable<SystemLog> Items,
            int TotalCount)>
        {
            Data = (Array.Empty<SystemLog>(), 0),
            IsSuccess = false,
            MessageCode = MessageCodes.ErrorDataBase,
            Message = message
        };
    }
}
