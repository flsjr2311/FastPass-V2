namespace FastPass.Infrastructure.Database;

/// <summary>
/// Validações obrigatórias para migrations para evitar erros conhecidos e repetitivos.
/// Reduz tokens e frustração ao prevenir problemas antes que ocorram.
/// </summary>
public static class MigrationValidation
{
    /// <summary>
    /// Validar: DELETE statements devem sempre ter CASCADE ou deletar dependências primeiro
    /// </summary>
    public static void ValidateDeleteStatement(string sql)
    {
        if (!sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase))
            return;

        // Regra 1: Se deletar de devices, DEVE deletar access_attempts e turnstile_presence primeiro
        if (sql.Contains("fp_devices", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            if (!sql.Contains("fp_access_attempts", StringComparison.OrdinalIgnoreCase) &&
                !sql.Contains("fp_turnstile_presence", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "❌ ERRO PREVENIDO: DELETE de fp_devices sem deletar dependências primeiro!\n" +
                    "   Deve-se deletar: fp_access_attempts e fp_turnstile_presence antes.\n" +
                    "   Isso previne: 'Cannot delete or update a parent row: foreign key constraint fails'");
            }
        }
    }

    /// <summary>
    /// Validar: Migrations que afetam login devem ser testadas
    /// </summary>
    public static void ValidateLoginImpact(string sql)
    {
        var criticalTables = new[] { "fp_users", "fp_roles", "fp_permissions", "fp_devices" };
        
        if (sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase) ||
            sql.Contains("DROP", StringComparison.OrdinalIgnoreCase) ||
            sql.Contains("ALTER", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var table in criticalTables)
            {
                if (sql.Contains(table, StringComparison.OrdinalIgnoreCase))
                {
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.WriteLine($"⚠️  WARNING: Migration afeta tabela crítica: {table}");
                    System.Console.WriteLine("   Login pode quebrar se dados de admin forem deletados!");
                    System.Console.ResetColor();
                }
            }
        }
    }

    /// <summary>
    /// Validar: Não deletar records sem WHERE clause específica
    /// </summary>
    public static void ValidateWhereClause(string sql)
    {
        if (!sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase))
            return;

        // Regra: DELETE deve SEMPRE ter WHERE clause
        var deleteMatch = System.Text.RegularExpressions.Regex.Match(
            sql,
            @"DELETE\s+FROM\s+(\w+)(?!.*WHERE)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (deleteMatch.Success)
        {
            throw new InvalidOperationException(
                $"❌ ERRO PREVENIDO: DELETE sem WHERE clause!\n" +
                $"   Tabela: {deleteMatch.Groups[1].Value}\n" +
                $"   Risco: Deletar TODOS os records accidentalmente!");
        }
    }
}
