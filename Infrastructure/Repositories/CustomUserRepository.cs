using Dapper;
using Infrastructure.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Repositories
{
    public class CustomUserRepository
    {
        private readonly IDbConnectionFactory _factory;


        public CustomUserRepository(IDbConnectionFactory factory) => _factory = factory;


        //public async Task<IEnumerable<User>> SearchAsync(string? name, int page = 1, int pageSize = 20)
        //{
        //    using var conn = _factory.CreateConnection();
        //    var where = "WHERE 1=1";
        //    if (!string.IsNullOrWhiteSpace(name)) where += " AND UserName LIKE @Name";
        //    var sql = $@"SELECT * FROM [User]{where}ORDER BY UserName OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";


        //    return await conn.QueryAsync<User>(sql, new { Name = "%" + name + "%", Offset = (page - 1) * pageSize, PageSize = pageSize });
        //}
    }
}
