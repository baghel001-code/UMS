using ApplicationLayer.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Database
{
    public class DapperUnitOfWork : IUnitOfWork, IDisposable
    {
        private readonly IDbConnection _connection;
        private readonly IDbTransaction _transaction;
        private bool _disposed;


        public IDbConnection Connection => _connection;
        public IDbTransaction Transaction => _transaction;


        public DapperUnitOfWork(IDbConnectionFactory factory)
        {
            _connection = factory.CreateConnection();
            _connection.Open();
            _transaction = _connection.BeginTransaction();
        }


        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            _transaction.Commit();
            return Task.FromResult(1);
        }


        public void Dispose()
        {
            if (_disposed) return;
            try
            {
                _transaction?.Dispose();
                _connection?.Dispose();
            }
            finally { _disposed = true; }
        }
    }
}
