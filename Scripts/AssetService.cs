using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;

public class AssetService
{
    private readonly DatabaseHelper _databaseHelper;

    public AssetService(DatabaseHelper databaseHelper)
    {
        _databaseHelper = databaseHelper;
    }

    private Dictionary<string, string> DefaultThumbnails = new Dictionary<string, string>
    {
        { "VIDEO", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "video-file-icon.png") },
        { "AUDIO", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "music-file-icon.png") },
        { "DOCUMENT", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "text-file-icon.png") },
        { "OTHER", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "generic-file-icon.png") }
    };

    private List<string> textAndCodeFileExtensions = new List<string>
    {
        ".txt", ".md", ".json", ".xml", ".html", ".css", ".js", ".cs", ".java", ".py", ".cpp", ".c", ".h"
    };
    private List<string> audioFileExtensions = new List<string>
    {
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma"
    };
    private List<string> videoFileExtensions = new List<string>
    {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm"
    };

    public class AssetFilter
    {
        public string SearchTerm { get; set; }
        public int? CategoryId { get; set; }
        public string AssetType { get; set; }
        public List<int> TagIds { get; set; } = new List<int>();
        public bool AllTagsRequired { get; set; } = false;

        public string SortBy { get; set; } = "DateAdded";
        public bool SortDescending { get; set; } = true;
    }

    // Add a new asset to the database
    public async Task<int> AddAssetAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        if (await AssetExistsAsync(filePath))
            return 0;

        var fileInfo = new FileInfo(filePath);

        var asset = new Asset
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            FileSize = fileInfo.Length,
            DateCreated = fileInfo.CreationTime,
            DateModified = fileInfo.LastWriteTime,
            AssetType = Path.GetExtension(filePath).TrimStart('.').ToUpper(),
            Description = ""
        };
        AssignThumbnailForAsset(asset);

        return await AddAssetAsync(asset);
    }

    public async Task<int> AddAssetAsync(Asset asset)
    {
        const string sql = @"
            INSERT INTO Assets (Name, FilePath, ThumbnailPath, FileSize, DateCreated, DateModified, AssetType, Description, CategoryId)
            VALUES (@Name, @FilePath, @ThumbnailPath, @FileSize, @DateCreated, @DateModified, @AssetType, @Description, @CategoryId);
            SELECT last_insert_rowid();";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@Name", asset.Name);
                command.Parameters.AddWithValue("@FilePath", asset.FilePath);
                command.Parameters.AddWithValue("@ThumbnailPath", asset.ThumbnailPath);
                command.Parameters.AddWithValue("@FileSize", asset.FileSize);
                command.Parameters.AddWithValue("@DateCreated", asset.DateCreated);
                command.Parameters.AddWithValue("@DateModified", asset.DateModified);
                command.Parameters.AddWithValue("@AssetType", asset.AssetType);
                command.Parameters.AddWithValue("@Description", asset.Description ?? "");
                command.Parameters.AddWithValue("@CategoryId", asset.CategoryId.HasValue ? (object)asset.CategoryId.Value : DBNull.Value);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
        }
    }

    public void AssignThumbnailForAsset(Asset asset)
    {
        // For non-image assets, use one of the default thumbnails based on asset type
        // check if the asset is an image by runnig it through a bitmap decoder and if it throws an exception, then it is not an image
        try
        {
            using (var stream = new FileStream(asset.FilePath, FileMode.Open, FileAccess.Read))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.Default
                );

                // If it loaded at least one frame, it's an image
                if (decoder.Frames.Count > 0)
                {
                    asset.ThumbnailPath = asset.FilePath;
                }
            }
        }
        catch
        {
            //identify asset type based on file extension, default to "OTHER" if not recognized
            var extension = Path.GetExtension(asset.FilePath).ToLowerInvariant();
            if( textAndCodeFileExtensions.Contains(extension))
            {
                asset.ThumbnailPath = DefaultThumbnails["DOCUMENT"];
            }
            else if (audioFileExtensions.Contains(extension))
            {
                asset.ThumbnailPath = DefaultThumbnails["AUDIO"];
            }
            else if (videoFileExtensions.Contains(extension))
            {
                asset.ThumbnailPath = DefaultThumbnails["VIDEO"];
            }
            else
            {
                asset.ThumbnailPath = DefaultThumbnails["OTHER"];
            }
        }
    }

    public async Task<List<Asset>> GetAllAssetsAsync()
    {
        const string sql = @"
            SELECT Id, Name, FilePath, ThumbnailPath, FileSize, DateCreated, DateModified, DateAdded, AssetType, Description, CategoryId
            FROM Assets
            ORDER BY DateAdded DESC";

        var assets = new List<Asset>();

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    assets.Add(MapReaderToAsset(reader));
                }
            }
        }

        return assets;
    }

    public async Task<List<Asset>> GetAssetsAsync(AssetFilter filter = null)
    {
        var sql = new StringBuilder(@"
        SELECT Id, Name, FilePath, ThumbnailPath, FileSize, DateCreated, DateModified, 
               DateAdded, AssetType, Description, CategoryId
        FROM Assets");

        var conditions = new List<string>();
        var parameters = new List<SQLiteParameter>();

        if (filter != null)
        {
            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                conditions.Add("(Name LIKE @SearchTerm OR Description LIKE @SearchTerm)");
                parameters.Add(new SQLiteParameter("@SearchTerm", $"%{filter.SearchTerm}%"));
            }

            if (filter.CategoryId.HasValue)
            {
                conditions.Add("CategoryId = @CategoryId");
                parameters.Add(new SQLiteParameter("@CategoryId", filter.CategoryId.Value));
            }

            if (!string.IsNullOrEmpty(filter.AssetType))
            {
                conditions.Add("AssetType = @AssetType");
                parameters.Add(new SQLiteParameter("@AssetType", filter.AssetType.ToUpper()));
            }

            if (filter.TagIds != null && filter.TagIds.Any())
            {
                if (filter.AllTagsRequired)
                {
                    // Count the number of matching tags per asset, and require it to match the number of TagIds
                    conditions.Add($@"
                        Id IN (
                            SELECT AssetId 
                            FROM AssetTags 
                            WHERE TagId IN ({string.Join(",", filter.TagIds)}) 
                            GROUP BY AssetId 
                            HAVING COUNT(DISTINCT TagId) = {filter.TagIds.Count}
                        )");
                }
                else
                {
                    conditions.Add("Id IN (SELECT AssetId FROM AssetTags WHERE TagId IN (" + string.Join(",", filter.TagIds) + "))");
                }
            }
        }

        if (conditions.Any())
        {
            sql.Append(" WHERE " + string.Join(" AND ", conditions));
        }

        sql.Append($" ORDER BY {filter?.SortBy ?? "DateAdded"} {(filter?.SortDescending == true ? "DESC" : "ASC")}");

        // Execute query with dynamic SQL and parameters...
        var assets = new List<Asset>();
        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql.ToString(), connection))
            {
                foreach (var param in parameters)
                {
                    command.Parameters.Add(param);
                }
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        assets.Add(MapReaderToAsset(reader));
                    }
                }
            }
        }
        return assets;
    }

    public async Task<bool> UpdateAssetAsync(Asset asset)
    {
        const string sql = @"
            UPDATE Assets 
            SET Name = @Name, Description = @Description, CategoryId = @CategoryId
            WHERE Id = @Id";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@Id", asset.Id);
                command.Parameters.AddWithValue("@Name", asset.Name);
                command.Parameters.AddWithValue("@Description", asset.Description ?? "");
                command.Parameters.AddWithValue("@CategoryId", asset.CategoryId.HasValue ? (object)asset.CategoryId.Value : DBNull.Value);

                var rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
        }
    }

    public async Task<bool> DeleteAssetAsync(int assetId)
    {
        const string sql = "DELETE FROM Assets WHERE Id = @Id";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@Id", assetId);
                var rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
        }
    }

    public async Task<Asset> GetAssetByIdAsync(int assetId)
    {
        const string sql = @"
            SELECT Id, Name, FilePath, ThumbnailPath, FileSize, DateCreated, DateModified, DateAdded, AssetType, Description, CategoryId
            FROM Assets
            WHERE Id = @Id";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@Id", assetId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return MapReaderToAsset(reader);
                    }
                }
            }
        }

        return null;
    }

    // Check if file path already exists in database
    public async Task<bool> AssetExistsAsync(string filePath)
    {
        const string sql = "SELECT COUNT(*) FROM Assets WHERE FilePath = @FilePath";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@FilePath", filePath);
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }
    }

    // Helper method to map database reader to Asset object
    private Asset MapReaderToAsset(DbDataReader reader)
    {
        return new Asset
        {
            Id = reader.GetInt32(0),                    // Id
            Name = reader.GetString(1),                 // Name
            FilePath = reader.GetString(2),             // FilePath
            ThumbnailPath = reader.GetString(3),        // ThumbnailPath
            FileSize = reader.GetInt64(4),              // FileSize
            DateCreated = reader.GetDateTime(5),        // DateCreated
            DateModified = reader.GetDateTime(6),       // DateModified
            DateAdded = reader.GetDateTime(7),          // DateAdded
            AssetType = reader.GetString(8),            // AssetType
            Description = reader.IsDBNull(9) ? "" : reader.GetString(9),  // Description
            CategoryId = reader.IsDBNull(10) ? (int?)null : reader.GetInt32(10)  // CategoryId
        };
    }
}