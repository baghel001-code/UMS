using Dapper;
using DomainLayer.Interfaces;
using Infrastructure.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Repositories
{
    public class DapperRepository<T> : IRepository<T> where T : class
    {
        private readonly IDbConnectionFactory _connectionFactory;


        public DapperRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }


        public async Task AddAsync(T entity, CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateConnection();
            var props = typeof(T).GetProperties().Where(p => p.CanRead).ToArray();
            var columns = string.Join(",", props.Select(p => "[" + p.Name + "]"));
            var values = string.Join(",", props.Select(p => "@" + p.Name));
            var sql = $"INSERT INTO [{typeof(T).Name}] ({columns}) VALUES ({values});";
            await conn.ExecuteAsync(sql, entity);
        }


        public async Task UpdateAsync(T entity, CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateConnection();
            var props = typeof(T).GetProperties().Where(p => p.CanRead && p.Name != "Id").ToArray();
            var setClause = string.Join(",", props.Select(p => "[" + p.Name + "]=@" + p.Name));
            var sql = $"UPDATE [{typeof(T).Name}] SET {setClause} WHERE Id = @Id;";
            await conn.ExecuteAsync(sql, entity);
        }


        public async Task DeleteAsync(T entity, CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateConnection();
            var idProp = typeof(T).GetProperty("Id");
            if (idProp == null) throw new InvalidOperationException("Entity has no Id property");
            var id = idProp.GetValue(entity);
            var sql = $"DELETE FROM [{typeof(T).Name}] WHERE Id = @Id";
            await conn.ExecuteAsync(sql, new { Id = id });
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
            var list = await conn.QueryAsync<T>(sql);
            return list.ToList();
        }
    }
}
