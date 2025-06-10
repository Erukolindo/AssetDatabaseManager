using System.Data.SQLite;
using System.Data.Common;
using System;
using System.IO;
using System.Threading.Tasks;

public class DatabaseHelper
{
    private readonly string _connectionString;
    private readonly string _databasePath;

    public DatabaseHelper()
    {
        // Store database in the user's Documents folder
        var documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var appFolder = Path.Combine(documentsFolder, "AssetManager");

        // Create app folder if it doesn't exist
        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
        }

        _databasePath = Path.Combine(appFolder, "assets.db");
        _connectionString = $"Data Source={_databasePath}";
    }

    // Initialize database - create tables if they don't exist
    public async Task InitializeDatabaseAsync()
    {
        using (var connection = new SQLiteConnection(_connectionString))
        {
            await connection.OpenAsync();

            // Create Assets table
            var createAssetsTable = @"
            CREATE TABLE IF NOT EXISTS Assets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                FilePath TEXT NOT NULL UNIQUE,
                ThumbnailPath TEXT,
                FileSize INTEGER,
                DateCreated DATETIME,
                DateModified DATETIME,
                DateAdded DATETIME DEFAULT CURRENT_TIMESTAMP,
                AssetType TEXT,
                Description TEXT,
                CategoryId INTEGER,
                FOREIGN KEY (CategoryId) REFERENCES Categories (Id)
            )";

            // Create Categories table
            var createCategoriesTable = @"
            CREATE TABLE IF NOT EXISTS Categories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CategoryName TEXT NOT NULL,
                ParentCategoryId INTEGER,
                FOREIGN KEY (ParentCategoryId) REFERENCES Categories (Id)
            )";

            // Create Tags table
            var createTagsTable = @"
            CREATE TABLE IF NOT EXISTS Tags (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TagName TEXT NOT NULL UNIQUE,
                Color TEXT
            )";

            // Create AssetTags junction table
            var createAssetTagsTable = @"
            CREATE TABLE IF NOT EXISTS AssetTags (
                AssetId INTEGER,
                TagId INTEGER,
                PRIMARY KEY (AssetId, TagId),
                FOREIGN KEY (AssetId) REFERENCES Assets (Id) ON DELETE CASCADE,
                FOREIGN KEY (TagId) REFERENCES Tags (Id) ON DELETE CASCADE
            )";

            // Execute table creation commands
            await ExecuteNonQueryAsync(connection, createCategoriesTable);
            await ExecuteNonQueryAsync(connection, createTagsTable);
            await ExecuteNonQueryAsync(connection, createAssetsTable);
            await ExecuteNonQueryAsync(connection, createAssetTagsTable);

            // Create some default categories
            await CreateDefaultCategoriesAsync(connection);
        }
    }

    private async Task CreateDefaultCategoriesAsync(SQLiteConnection connection)
    {
        // Check if categories already exist
        var checkQuery = "SELECT COUNT(*) FROM Categories";
        using (var checkCommand = new SQLiteCommand(checkQuery, connection))
        {
            var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

            if (count == 0)
            {
                // Insert default categories
                var defaultCategories = @"
                INSERT INTO Categories (CategoryName, ParentCategoryId) VALUES 
                ('Textures', NULL),
                ('Models', NULL),
                ('Audio', NULL),
                ('Scripts', NULL),
                ('UI', (SELECT Id FROM Categories WHERE CategoryName = 'Textures')),
                ('Environment', (SELECT Id FROM Categories WHERE CategoryName = 'Textures')),
                ('Characters', (SELECT Id FROM Categories WHERE CategoryName = 'Models')),
                ('Props', (SELECT Id FROM Categories WHERE CategoryName = 'Models')),
                ('Music', (SELECT Id FROM Categories WHERE CategoryName = 'Audio')),
                ('SFX', (SELECT Id FROM Categories WHERE CategoryName = 'Audio'))";

                await ExecuteNonQueryAsync(connection, defaultCategories);
            }
        }
    }

    private async Task ExecuteNonQueryAsync(SQLiteConnection connection, string sql)
    {
        using (var command = new SQLiteCommand(sql, connection))
        {
            await command.ExecuteNonQueryAsync();
        }
    }

    // Helper method to get a connection
    public SQLiteConnection GetConnection()
    {
        return new SQLiteConnection(_connectionString);
    }

    // Test the connection
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Database connection failed: {ex.Message}");
            return false;
        }
    }

    public string GetDatabasePath() => _databasePath;
    public bool DatabaseExists() => File.Exists(_databasePath);
}