using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Data.Common;
using System.Threading.Tasks;

public class TagService
{
    private readonly DatabaseHelper _databaseHelper;

    public TagService(DatabaseHelper databaseHelper)
    {
        _databaseHelper = databaseHelper;
    }

    public async Task<List<Tag>> GetAllTagsAsync()
    {
        const string sql = @"
            SELECT Id, TagName, Color
            FROM Tags
            ORDER BY TagName";

        var tags = new List<Tag>();

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tags.Add(MapReaderToTag(reader));
                }
            }
        }

        return tags;
    }

    public void GenerateSampleTags()
    {
        // This method is for generating sample tags for testing purposes
        var sampleTags = new List<Tag>
        {
            new Tag { TagName = "Important", Color = "#e74c3c" },
            new Tag { TagName = "Work", Color = "#2ecc71" },
            new Tag { TagName = "Personal", Color = "#3498db" },
            new Tag { TagName = "Urgent", Color = "#f1c40f" },
            new Tag { TagName = "Archive", Color = "#9b59b6" }
        };
        foreach (var tag in sampleTags)
        {
            AddTagAsync(tag).Wait();
        }
    }

    public async Task<int> AddTagAsync(string tagName, string color = "#3498db")
    {
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentException("Tag name cannot be empty", nameof(tagName));

        if (await TagExistsAsync(tagName))
            throw new InvalidOperationException($"Tag '{tagName}' already exists");

        const string sql = @"
            INSERT INTO Tags (TagName, Color)
            VALUES (@TagName, @Color);
            SELECT last_insert_rowid();";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@TagName", tagName.Trim());
                command.Parameters.AddWithValue("@Color", color ?? "#3498db");

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
        }
    }

    public async Task<int> AddTagAsync(Tag tag)
    {
        return await AddTagAsync(tag.TagName, tag.Color);
    }

    public async Task<bool> RemoveTagAsync(int tagId)
    {
        // First, remove all associations with assets
        await RemoveTagFromAllAssetsAsync(tagId);

        // Then remove the tag itself
        const string sql = "DELETE FROM Tags WHERE Id = @Id";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@Id", tagId);
                var rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
        }
    }

    public async Task<Tag> GetTagByIdAsync(int tagId)
    {
        const string sql = @"
            SELECT Id, TagName, Color
            FROM Tags
            WHERE Id = @Id";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@Id", tagId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return MapReaderToTag(reader);
                    }
                }
            }
        }

        return null;
    }

    public async Task<Tag> GetTagByNameAsync(string tagName)
    {
        const string sql = @"
            SELECT Id, TagName, Color
            FROM Tags
            WHERE TagName = @TagName COLLATE NOCASE";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@TagName", tagName);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return MapReaderToTag(reader);
                    }
                }
            }
        }

        return null;
    }

    public async Task<bool> TagExistsAsync(string tagName)
    {
        const string sql = "SELECT COUNT(*) FROM Tags WHERE TagName = @TagName COLLATE NOCASE";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@TagName", tagName);
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }
    }

    public async Task<bool> UpdateTagAsync(Tag tag)
    {
        const string sql = @"
            UPDATE Tags 
            SET TagName = @TagName, Color = @Color
            WHERE Id = @Id";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@Id", tag.Id);
                command.Parameters.AddWithValue("@TagName", tag.TagName);
                command.Parameters.AddWithValue("@Color", tag.Color ?? "#3498db");

                var rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
        }
    }

    public async Task<List<Tag>> GetTagsForAssetAsync(int assetId)
    {
        const string sql = @"
            SELECT t.Id, t.TagName, t.Color
            FROM Tags t
            INNER JOIN AssetTags at ON t.Id = at.TagId
            WHERE at.AssetId = @AssetId
            ORDER BY t.TagName";

        var tags = new List<Tag>();

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@AssetId", assetId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        tags.Add(MapReaderToTag(reader));
                    }
                }
            }
        }

        return tags;
    }

    public async Task<bool> AddTagToAssetAsync(int assetId, int tagId)
    {
        // Check if association already exists
        if (await AssetHasTagAsync(assetId, tagId))
            return true;

        const string sql = @"
            INSERT INTO AssetTags (AssetId, TagId)
            VALUES (@AssetId, @TagId)";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@AssetId", assetId);
                command.Parameters.AddWithValue("@TagId", tagId);

                var rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
        }
    }

    public async Task<bool> RemoveTagFromAssetAsync(int assetId, int tagId)
    {
        const string sql = "DELETE FROM AssetTags WHERE AssetId = @AssetId AND TagId = @TagId";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@AssetId", assetId);
                command.Parameters.AddWithValue("@TagId", tagId);

                var rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
        }
    }

    public async Task<bool> AssetHasTagAsync(int assetId, int tagId)
    {
        const string sql = "SELECT COUNT(*) FROM AssetTags WHERE AssetId = @AssetId AND TagId = @TagId";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@AssetId", assetId);
                command.Parameters.AddWithValue("@TagId", tagId);

                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }
    }

    // Private helper methods
    private async Task RemoveTagFromAllAssetsAsync(int tagId)
    {
        const string sql = "DELETE FROM AssetTags WHERE TagId = @TagId";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@TagId", tagId);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    public async Task<List<int>> GetTagIdsForAssetAsync(int assetId)
    {
        const string sql = @"
            SELECT TagId
            FROM AssetTags
            WHERE AssetId = @AssetId";

        var tagIds = new List<int>();

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@AssetId", assetId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        tagIds.Add(reader.GetInt32(0));
                    }
                }
            }
        }

        return tagIds;
    }

    public async Task<bool> SetTagsForAssetAsync(int assetId, IEnumerable<int> tagIds)
    {
        // Remove all existing tags for the asset
        const string deleteSql = "DELETE FROM AssetTags WHERE AssetId = @AssetId";
        int rowsAffected = 0;
        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var deleteCommand = new SQLiteCommand(deleteSql, connection))
            {
                deleteCommand.Parameters.AddWithValue("@AssetId", assetId);
                rowsAffected = await deleteCommand.ExecuteNonQueryAsync();
            }

            // Add new tags
            const string insertSql = "INSERT INTO AssetTags (AssetId, TagId) VALUES (@AssetId, @TagId)";
            int insertCount = 0;
            foreach (var tagId in tagIds)
            {
                using (var insertCommand = new SQLiteCommand(insertSql, connection))
                {
                    insertCommand.Parameters.AddWithValue("@AssetId", assetId);
                    insertCommand.Parameters.AddWithValue("@TagId", tagId);
                    insertCount += await insertCommand.ExecuteNonQueryAsync();
                }
            }
        }

        return rowsAffected > 0;
    }

    private Tag MapReaderToTag(DbDataReader reader)
    {
        return new Tag
        {
            Id = reader.GetInt32(0),                    // Id
            TagName = reader.GetString(1),              // TagName
            Color = reader.IsDBNull(2) ? "#3498db" : reader.GetString(2)  // Color
        };
    }
}