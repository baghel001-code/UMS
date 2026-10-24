using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace AMS.Infrastructure.Persistence
{
    /// <summary>"ConnectionStrings:Ams" (SQL Server).</summary>
    public sealed class AmsDatabaseOptions
    {
        public string ConnectionString { get; set; } = "";
        /// <summary>Create the database objects on start-up (development; production DBAs run database/*.sql).</summary>
        public bool AutoMigrate { get; set; }
        /// <summary>Fill an empty database with demo data for this UMS tenant (development only).</summary>
        public Guid? DemoTenantId { get; set; }
    }

    /// <summary>
    /// Dapper wrapper used by every repository (same pattern as the other VMM applications): opens a connection per call, runs a stored
    /// procedure, logs slow queries and SQL errors with full context (parameters with secrets masked), and rethrows.
    /// </summary>
    public sealed class DapperHelper
    {
        private const int SlowQueryThresholdMs = 3000;
        private const int MaxLoggedValueLength = 200;
        private static readonly string[] SensitiveParamKeywords = { "password", "pwd", "secret", "token", "otp", "key" };

        private readonly string _connectionString;
        private readonly string _dataSource;
        private readonly string _database;
        private readonly ILogger<DapperHelper> _logger;

        public DapperHelper(AmsDatabaseOptions options, ILogger<DapperHelper> logger)
        {
            _logger = logger;
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
                throw new InvalidOperationException("Connection string 'Ams' is missing.");
            var builder = new SqlConnectionStringBuilder(options.ConnectionString);
            _dataSource = builder.DataSource;
            _database = builder.InitialCatalog;
            _connectionString = builder.ConnectionString;
        }

        public string Database => _database;

        public SqlConnection CreateConnection() => new(_connectionString);

        // ------------------------------------------------------------------ core

        private async Task<TResult> ExecuteCoreAsync<TResult>(string operation, string sql, object? logParam, Func<IDbConnection, Task<TResult>> action)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await using var connection = CreateConnection();
                await connection.OpenAsync();
                var result = await action(connection);
                sw.Stop();
                if (sw.ElapsedMilliseconds >= SlowQueryThresholdMs)
                    _logger.LogWarning("[{Operation}] SLOW QUERY: {Sql} took {ElapsedMs} ms | Params: {Params}", operation, sql, sw.ElapsedMilliseconds, DescribeParams(logParam));
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                if (ex is SqlException sqlEx)
                {
                    var all = new StringBuilder();
                    foreach (SqlError e in sqlEx.Errors) all.AppendLine($"  Msg {e.Number}, Level {e.Class}, State {e.State}, Procedure '{e.Procedure}', Line {e.LineNumber}: {e.Message}");
                    _logger.LogError(sqlEx, "[{Operation}] SQL Error {Number} | SQL: {Sql} | Server: {Server} | Database: {Database} | Elapsed: {Ms} ms | Params: {Params}{NewLine}{Errors}",
                        operation, sqlEx.Number, sql, _dataSource, _database, sw.ElapsedMilliseconds, DescribeParams(logParam), Environment.NewLine, all);
                }
                else if (ex is not OperationCanceledException)
                    _logger.LogError(ex, "[{Operation}] {Type} | SQL: {Sql} | Params: {Params}", operation, ex.GetType().Name, sql, DescribeParams(logParam));
                throw;
            }
        }

        private static string DescribeParams(object? param)
        {
            if (param == null) return "(none)";
            try
            {
                var pairs = new List<string>();
                if (param is DynamicParameters dp)
                {
                    foreach (var name in dp.ParameterNames)
                    {
                        object? value;
                        try { value = dp.Get<object>(name); } catch { value = "(unavailable)"; }
                        pairs.Add(Format(name, value));
                    }
                }
                else
                {
                    foreach (var p in param.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
                        pairs.Add(Format(p.Name, p.GetValue(param)));
                }
                return pairs.Count == 0 ? "(none)" : string.Join(", ", pairs);
            }
            catch (Exception ex)
            {
                return $"(could not describe parameters: {ex.Message})";
            }
        }

        private static string Format(string name, object? value)
        {
            if (SensitiveParamKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase))) return $"{name}=***";
            var text = value switch
            {
                null or DBNull => "NULL",
                string s => $"'{(s.Length <= MaxLoggedValueLength ? s : s[..MaxLoggedValueLength] + "…")}'",
                DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss"),
                ICollection c => $"(collection, {c.Count} items)",
                _ => value.ToString() ?? ""
            };
            return $"{name}={text}";
        }

        // ------------------------------------------------------------------ stored procedures

        public Task<List<T>> ListAsync<T>(string storedProc, object? param = null) =>
            ExecuteCoreAsync(nameof(ListAsync), storedProc, param, async c => (await c.QueryAsync<T>(storedProc, param, commandType: CommandType.StoredProcedure)).ToList());

        public Task<T?> SingleAsync<T>(string storedProc, object? param = null) =>
            ExecuteCoreAsync(nameof(SingleAsync), storedProc, param, c => c.QueryFirstOrDefaultAsync<T?>(storedProc, param, commandType: CommandType.StoredProcedure));

        /// <summary>Runs a procedure that returns several result sets; <paramref name="read"/> reads them in order.</summary>
        public Task<TResult> MultipleAsync<TResult>(string storedProc, object? param, Func<SqlMapper.GridReader, Task<TResult>> read) =>
            ExecuteCoreAsync(nameof(MultipleAsync), storedProc, param, async c =>
            {
                using var grid = await c.QueryMultipleAsync(storedProc, param, commandType: CommandType.StoredProcedure);
                return await read(grid);
            });

        /// <summary>
        /// Runs a write procedure following the @Error_Code / @Error_Message (and optional @Record_Id) output convention.
        /// </summary>
        public Task<Application.Interfaces.DbResult> WriteAsync(string storedProc, DynamicParameters parameters, bool returnsRecordId = false) =>
            ExecuteCoreAsync(nameof(WriteAsync), storedProc, parameters, async c =>
            {
                parameters.Add("@Error_Code", dbType: DbType.Int32, direction: ParameterDirection.Output);
                parameters.Add("@Error_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
                if (returnsRecordId) parameters.Add("@Record_Id", dbType: DbType.Int32, direction: ParameterDirection.Output);
                await c.ExecuteAsync(storedProc, parameters, commandType: CommandType.StoredProcedure);
                return new Application.Interfaces.DbResult(parameters.Get<int?>("@Error_Code") ?? -1, parameters.Get<string?>("@Error_Message") ?? "",
                    returnsRecordId ? parameters.Get<int?>("@Record_Id") ?? 0 : 0);
            });

        /// <summary>Runs a SQL script batch (database initializer).</summary>
        public Task ExecuteBatchAsync(string sql) =>
            ExecuteCoreAsync(nameof(ExecuteBatchAsync), sql.Length > 120 ? sql[..120] + "…" : sql, null, c => c.ExecuteAsync(sql, commandTimeout: 120));
    }
}
