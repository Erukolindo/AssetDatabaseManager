using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Data.Common;
using System.Threading.Tasks;

public class CategoryService
{
    private readonly DatabaseHelper _databaseHelper;

    public CategoryService(DatabaseHelper databaseHelper)
    {
        _databaseHelper = databaseHelper;
    }

    public async Task<List<Category>> GetAllCategoriesAsync()
    {
        const string sql = @"
            SELECT Id, CategoryName, ParentCategoryId
            FROM Categories
            ORDER BY CategoryName";

        var categories = new List<Category>();

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    categories.Add(MapReaderToCategory(reader));
                }
            }
        }

        return categories;
    }

    public async Task<List<Category>> GetRootCategoriesAsync()
    {
        const string sql = @"
            SELECT Id, CategoryName, ParentCategoryId
            FROM Categories
            WHERE ParentCategoryId IS NULL
            ORDER BY CategoryName";

        var categories = new List<Category>();

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    categories.Add(MapReaderToCategory(reader));
                }
            }
        }

        return categories;
    }

    public async Task<List<Category>> GetChildCategoriesAsync(int parentId)
    {
        const string sql = @"
            SELECT Id, CategoryName, ParentCategoryId
            FROM Categories
            WHERE ParentCategoryId = @ParentId
            ORDER BY CategoryName";

        var categories = new List<Category>();

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@ParentId", parentId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        categories.Add(MapReaderToCategory(reader));
                    }
                }
            }
        }

        return categories;
    }

    public async Task<Category> GetCategoryByIdAsync(int categoryId)
    {
        const string sql = @"
            SELECT Id, CategoryName, ParentCategoryId
            FROM Categories
            WHERE Id = @Id";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@Id", categoryId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return MapReaderToCategory(reader);
                    }
                }
            }
        }

        return null;
    }

    public async Task<int> AddCategoryAsync(string categoryName, int? parentCategoryId = null)
    {
        const string sql = @"
            INSERT INTO Categories (CategoryName, ParentCategoryId)
            VALUES (@CategoryName, @ParentCategoryId);
            SELECT last_insert_rowid();";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@CategoryName", categoryName);
                command.Parameters.AddWithValue("@ParentCategoryId", parentCategoryId.HasValue ? (object)parentCategoryId.Value : DBNull.Value);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
        }
    }

    public async Task<bool> UpdateCategoryAsync(int categoryId, string newName)
    {
        const string sql = @"
            UPDATE Categories 
            SET CategoryName = @CategoryName
            WHERE Id = @Id";

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@Id", categoryId);
                command.Parameters.AddWithValue("@CategoryName", newName);

                var rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
        }
    }

    public async Task<bool> DeleteCategoryAsync(int categoryId, int? moveAssetsToCategory = null)
    {
        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    // Move assets to another category or set to null
                    const string updateAssetsSql = @"
                        UPDATE Assets 
                        SET CategoryId = @NewCategoryId 
                        WHERE CategoryId = @OldCategoryId";

                    using (var updateCommand = new SQLiteCommand(updateAssetsSql, connection, transaction))
                    {
                        updateCommand.Parameters.AddWithValue("@NewCategoryId", moveAssetsToCategory.HasValue ? (object)moveAssetsToCategory.Value : DBNull.Value);
                        updateCommand.Parameters.AddWithValue("@OldCategoryId", categoryId);
                        await updateCommand.ExecuteNonQueryAsync();
                    }

                    // Move child categories to parent or make them root categories
                    const string updateChildrenSql = @"
                        UPDATE Categories 
                        SET ParentCategoryId = (
                            SELECT ParentCategoryId FROM Categories WHERE Id = @CategoryId
                        )
                        WHERE ParentCategoryId = @CategoryId";

                    using (var updateChildrenCommand = new SQLiteCommand(updateChildrenSql, connection, transaction))
                    {
                        updateChildrenCommand.Parameters.AddWithValue("@CategoryId", categoryId);
                        await updateChildrenCommand.ExecuteNonQueryAsync();
                    }

                    // Delete the category
                    const string deleteSql = "DELETE FROM Categories WHERE Id = @Id";
                    using (var deleteCommand = new SQLiteCommand(deleteSql, connection, transaction))
                    {
                        deleteCommand.Parameters.AddWithValue("@Id", categoryId);
                        var rowsAffected = await deleteCommand.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            transaction.Commit();
                            return true;
                        }
                        else
                        {
                            transaction.Rollback();
                            return false;
                        }
                    }
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }
    }

    public async Task<Dictionary<int, int>> GetAssetCountsByCategoryAsync()
    {
        const string sql = @"
            SELECT CategoryId, COUNT(*) as AssetCount
            FROM Assets
            WHERE CategoryId IS NOT NULL
            GROUP BY CategoryId";

        var counts = new Dictionary<int, int>();

        using (var connection = _databaseHelper.GetConnection())
        {
            await connection.OpenAsync();
            using (var command = new SQLiteCommand(sql, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var categoryId = reader.GetInt32(0);
                    var count = reader.GetInt32(1);
                    counts[categoryId] = count;
                }
            }
        }

        return counts;
    }

    // Helper method to map database reader to Category object
    private Category MapReaderToCategory(DbDataReader reader)
    {
        return new Category
        {
            Id = reader.GetInt32(0),                    // Id
            CategoryName = reader.GetString(1),         // CategoryName
            ParentCategoryId = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)  // ParentCategoryId
        };
    }
}