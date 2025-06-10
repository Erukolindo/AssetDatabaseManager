﻿using System;

public class Asset
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileSizeFormatted
    {
        get
        {
            if (FileSize < 1024)
                return $"{FileSize} B";
            else if (FileSize < 1024 * 1024)
                return $"{FileSize / 1024.0:F2} KB";
            else if (FileSize < 1024 * 1024 * 1024)
                return $"{FileSize / (1024.0 * 1024):F2} MB";
            else
                return $"{FileSize / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
    public DateTime DateCreated { get; set; }
    public DateTime DateModified { get; set; }
    public DateTime DateAdded { get; set; }
    public string AssetType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
}