using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Database
{
    public class SqlConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;


        public SqlConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }


        public IDbConnection CreateConnection()
        => new SqlConnection(_connectionString);
    }
}
