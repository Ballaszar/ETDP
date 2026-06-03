using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETD.Api.Data;
using ETD.Api.Models;

namespace ETD.Api.Data
{
    public class SourceMaterialsRepository
    {
        private readonly ApplicationDbContext _db;

        public SourceMaterialsRepository(ApplicationDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<SourceMaterial> AddAsync(SourceMaterial item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _db.SourceMaterials.Add(item);
            await _db.SaveChangesAsync();
            return item;
        }

        public async Task AddRangeAsync(IEnumerable<SourceMaterial> items)
        {
            if (items == null) return;
            _db.SourceMaterials.AddRange(items.Where(x => x != null));
            await _db.SaveChangesAsync();
        }

        public bool ExistsByFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            return _db.SourceMaterials.Any(x => x.FilePath == filePath || x.FileName == filePath);
        }
    }
}
