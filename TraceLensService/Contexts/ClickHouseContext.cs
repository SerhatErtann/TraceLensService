using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;
using System.Data.Common;
using TraceLensService.Common;

namespace TraceLensService.Contexts
{
    /// <summary>ClickHouse'a parametreli sorgu atan ince katman. Sorgu sınıfları (TraceQueries, AlertQueries) bunu kullanır.</summary>
    public class ClickHouseContext(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        private readonly string _connectionString = configuration.GetConnectionString(GlobalConsts.ClickHouseConnection)
            ?? throw new InvalidOperationException($"ConnectionStrings:{GlobalConsts.ClickHouseConnection} tanımlı değil");

        public async Task<List<T>> QueryAsync<T>(
            string sql, IReadOnlyDictionary<string, object> parameters, Func<DbDataReader, T> map, CancellationToken ct = default)
        {
            using ClickHouseConnection connection = Open();
            using ClickHouseCommand command = CreateCommand(connection, sql, parameters);
            using DbDataReader reader = await command.ExecuteReaderAsync(ct);
            List<T> result = [];
            while (await reader.ReadAsync(ct))
                result.Add(map(reader));
            return result;
        }

        public async Task ExecuteAsync(string sql, IReadOnlyDictionary<string, object>? parameters = null, CancellationToken ct = default)
        {
            using ClickHouseConnection connection = Open();
            using ClickHouseCommand command = CreateCommand(connection, sql, parameters ?? new Dictionary<string, object>());
            await command.ExecuteNonQueryAsync(ct);
        }

        private ClickHouseConnection Open() =>
            new(_connectionString, httpClientFactory.CreateClient(GlobalConsts.ClickHouseHttpClient));

        private static ClickHouseCommand CreateCommand(
            ClickHouseConnection connection, string sql, IReadOnlyDictionary<string, object> parameters)
        {
            ClickHouseCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters)
                command.AddParameter(name, value);
            return command;
        }
    }
}
