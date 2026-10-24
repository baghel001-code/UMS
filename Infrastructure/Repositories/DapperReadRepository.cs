using DomainLayer.Interfaces;
using Infrastructure.Database;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Repositories
{
    public class DapperReadRepository<T> : IReadRepository<T> where T : class
    {
        private readonly IDbConnectionFactory _connectionFactory;


        public DapperReadRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }


        public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = $"SELECT * FROM [{typeof(T).Name}] WHERE Id = @Id";
            return await conn.QueryFirstOrDefaultAsync<T>(sql, new { Id = id });
        }


        public async Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = $"SELECT * FROM [{typeof(T).Name}]";
            var data = await conn.QueryAsync<T>(sql);
            return data.ToList();
        }
    }
}
