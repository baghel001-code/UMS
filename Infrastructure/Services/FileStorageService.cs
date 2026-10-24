using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services
{
    public class FileStorageService
    {
        private readonly string _basePath;
        public FileStorageService(string basePath) => _basePath = basePath;


        public async Task<string> SaveAsync(Stream content, string relativePath, CancellationToken ct = default)
        {
            var fullPath = Path.Combine(_basePath, relativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);


            using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
            await content.CopyToAsync(fs, ct);
            return fullPath;
        }


        public Task DeleteAsync(string relativePath)
        {
            var fullPath = Path.Combine(_basePath, relativePath);
            if (File.Exists(fullPath)) File.Delete(fullPath);
            return Task.CompletedTask;
        }
    }
}
