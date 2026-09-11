using System.Text;
using FastPass.Application.Catalog;
using FastPass.Infrastructure.Database;
using Microsoft.Extensions.Logging;

namespace FastPass.Infrastructure.Catalog;

public sealed class MySqlTurnstileMessageTemplateService : ITurnstileMessageTemplateService
{
    private readonly ILogger<MySqlTurnstileMessageTemplateService> _logger;
    private readonly FastPassDbConnectionFactory _dbFactory;

    public MySqlTurnstileMessageTemplateService(
        ILogger<MySqlTurnstileMessageTemplateService> logger,
        FastPassDbConnectionFactory dbFactory)
    {
        _logger = logger;
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<TurnstileMessageTemplateView>> ListAsync()
    {
        try
        {
            using var conn = _dbFactory.Create();
            await conn.OpenAsync();
            const string sql = @"
                SELECT template_id, line1, line2, active, updated_at
                FROM fp_turnstile_message_templates
                ORDER BY template_id ASC";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var result = new List<TurnstileMessageTemplateView>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new TurnstileMessageTemplateView(
                    TemplateId: reader.GetInt32(0),
                    Line1: reader.GetString(1),
                    Line2: reader.GetString(2),
                    Active: reader.GetBoolean(3),
                    UpdatedAt: reader.GetDateTime(4).ToUniversalTime().ToString("O")));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar templates de catraca.");
            throw;
        }
    }

    public async Task<TurnstileMessageTemplateView?> GetAsync(int templateId)
    {
        try
        {
            using var conn = _dbFactory.Create();
            await conn.OpenAsync();
            const string sql = @"
                SELECT template_id, line1, line2, active, updated_at
                FROM fp_turnstile_message_templates
                WHERE template_id = @TemplateId";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@TemplateId", templateId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new TurnstileMessageTemplateView(
                    TemplateId: reader.GetInt32(0),
                    Line1: reader.GetString(1),
                    Line2: reader.GetString(2),
                    Active: reader.GetBoolean(3),
                    UpdatedAt: reader.GetDateTime(4).ToUniversalTime().ToString("O"));
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter template de catraca {TemplateId}.", templateId);
            throw;
        }
    }

    public async Task<TurnstileMessageTemplateView> UpdateAsync(
        int templateId,
        UpdateTurnstileMessageTemplateCommand command)
    {
        // Validar comprimento (máx 16 chars por linha)
        if (command.Line1.Length > 16)
            throw new ArgumentException($"Linha 1 não pode exceder 16 caracteres (tem {command.Line1.Length}).");
        if (command.Line2.Length > 16)
            throw new ArgumentException($"Linha 2 não pode exceder 16 caracteres (tem {command.Line2.Length}).");

        try
        {
            using var conn = _dbFactory.Create();
            await conn.OpenAsync();
            const string sql = @"
                UPDATE fp_turnstile_message_templates
                SET line1 = @Line1, line2 = @Line2, active = @Active, updated_at = NOW()
                WHERE template_id = @TemplateId";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@TemplateId", templateId);
            cmd.Parameters.AddWithValue("@Line1", command.Line1);
            cmd.Parameters.AddWithValue("@Line2", command.Line2);
            cmd.Parameters.AddWithValue("@Active", command.Active);

            var affectedRows = await cmd.ExecuteNonQueryAsync();
            if (affectedRows == 0)
                throw new InvalidOperationException($"Template {templateId} não encontrado.");

            _logger.LogInformation("Template de catraca {TemplateId} atualizado: {Line1} / {Line2}", 
                templateId, command.Line1, command.Line2);

            // Retornar o template atualizado
            return (await GetAsync(templateId)) ?? throw new InvalidOperationException("Falha ao recuperar template atualizado.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar template de catraca {TemplateId}.", templateId);
            throw;
        }
    }

    public async Task<Dictionary<int, (string Line1, string Line2)>> ListActiveTemplatesForDeviceAsync()
    {
        try
        {
            using var conn = _dbFactory.Create();
            await conn.OpenAsync();
            const string sql = @"
                SELECT template_id, line1, line2
                FROM fp_turnstile_message_templates
                WHERE active = 1
                ORDER BY template_id ASC";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var result = new Dictionary<int, (string, string)>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var templateId = reader.GetInt32(0);
                var line1 = reader.GetString(1);
                var line2 = reader.GetString(2);
                result[templateId] = (line1, line2);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar templates ativos de catraca.");
            throw;
        }
    }
}
