using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Timer = System.Threading.Timer;
using TreeView = System.Windows.Controls.TreeView;
using Path = System.IO.Path;
using System.Windows.Threading;

namespace AssetDatabaseManager
{
    public partial class MainWindow : Window
    {
        private enum ViewMode
        {
            List,
            Grid
        }

        public bool UseAndMode { get; set; } = false;

        public IEnumerable<TagDisplay> TagDisplays;
        public IEnumerable<TagDisplay> AssetTags;

        private DatabaseHelper _databaseHelper;
        private AssetService assetService;
        private CategoryService categoryService;
        private TagService tagService;

        private Timer _debounceTimer;
        private const int DebounceDelay = 300; // milliseconds

        private List<Asset> selectedDataGridItems = new List<Asset>();
        private string originalAssetName = string.Empty;
        private string originalAssetDescription = string.Empty;
        private int originalAssetCategorySelectedIndex = -1;
        private List<int> originalAssetTagIds = new List<int>();
        private bool AssetEditsMade
        {
            get
            {
                // Compare name, description, category
                bool basicFieldsChanged = txtAssetName.Text != originalAssetName
                    || txtAssetDescription.Text != originalAssetDescription
                    || cmbAssetCategory.SelectedIndex != originalAssetCategorySelectedIndex;

                // Compare tags: get selected tag IDs from AssetTags
                var selectedTagIds = AssetTags?.Where(td => td.IsSelected).Select(td => td.Tag.Id).OrderBy(id => id).ToList() ?? new List<int>();
                var originalTagIdsSorted = originalAssetTagIds.OrderBy(id => id).ToList();
                bool tagsChanged = !selectedTagIds.SequenceEqual(originalTagIdsSorted);

                return basicFieldsChanged || tagsChanged;
            }
        }

        private ViewMode currentViewMode = ViewMode.List;

        private bool _suppressSelectionChanged = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeDatabaseAsync();
            assetService = new AssetService(_databaseHelper);
            categoryService = new CategoryService(_databaseHelper);
            tagService = new TagService(_databaseHelper);
            ttpSelector.TagToggled += (tag) =>
            {
                UpdateAssetDisplay();
            };
            TestForThumbnails();
            UpdateTagList();
            UpdateTreeView();
            currentViewMode = Properties.Settings.Default.UseGridView ? ViewMode.Grid : ViewMode.List;
            dgAssets.Visibility = currentViewMode == ViewMode.List ? Visibility.Visible : Visibility.Collapsed;
            lvGridAssets.Visibility = currentViewMode == ViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
            UpdateAssetDisplay();
        }

        private async void InitializeDatabaseAsync()
        {
            _databaseHelper = new DatabaseHelper();

            try
            {
                await _databaseHelper.InitializeDatabaseAsync();

                bool isConnected = await _databaseHelper.TestConnectionAsync();
                if (isConnected)
                {
                    Title = $"Asset Manager - Connected to: {_databaseHelper.GetDatabasePath()}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize database: {ex.Message}", "Database Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestForThumbnails()
        {
            // Check if the thumbnail directory exists, if not create it
            string thumbnailPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssetDatabaseManager", "Thumbnails");
            if (!Directory.Exists(thumbnailPath))
            {
                Directory.CreateDirectory(thumbnailPath);
            }

            // Get all assets, check thumbnailPath for each, if it doesn't exist, generate a thumbnail
            var assets = assetService.GetAllAssetsAsync().Result;
            foreach (var asset in assets)
            {
                if (asset.ThumbnailPath == string.Empty)
                {
                    assetService.AssignThumbnailForAsset(asset);
                }
            }
        }

        private void UpdateTagList()
        {
            // Get all tags and update the tag list as TagDisplay objects
            var tags = tagService.GetAllTagsAsync().Result;

            if (tags == null || tags.Count == 0)
            {
                tagService.GenerateSampleTags();
                tags = tagService.GetAllTagsAsync().Result;
            }

            // Preserve IsSelected state for tags that already exist in TagDisplays
            var previousDisplays = TagDisplays?.ToDictionary(td => td.Tag.Id) ?? new Dictionary<int, TagDisplay>();

            TagDisplays = tags.Select(tag =>
            {
                if (previousDisplays.TryGetValue(tag.Id, out var existingDisplay))
                {
                    existingDisplay.Tag = tag;
                    return existingDisplay;
                }
                else
                {
                    return new TagDisplay { Tag = tag };
                }
            }).ToList();

            ttpSelector.itemsControl.ItemsSource = TagDisplays;

            // Preserve IsSelected state for tags that already exist in AssetTags
            previousDisplays = AssetTags?.ToDictionary(td => td.Tag.Id) ?? new Dictionary<int, TagDisplay>();

            AssetTags = tags.Select(tag =>
            {
                if (previousDisplays.TryGetValue(tag.Id, out var existingDisplay))
                {
                    existingDisplay.Tag = tag;
                    return existingDisplay;
                }
                else
                {
                    return new TagDisplay { Tag = tag };
                }
            }).ToList();

            ttpAssetTags.itemsControl.ItemsSource = AssetTags;
        }

        private void btnAddAssets_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "All Files (*.*)|*.*",
                Title = "Select Assets"
            };

            if (dialog.ShowDialog() == true)
            {
                int fileCount = dialog.FileNames.Length;
                assetLoadingBar.Visibility = Visibility.Visible;
                assetLoadingBar.Maximum = fileCount;
                assetLoadingBar.Value = 0;
                assetLoadingText.Text = $"Loading 0/{fileCount} files...";

                foreach (var file in dialog.FileNames)
                {
                    assetService.AddAssetAsync(file).ContinueWith(task =>
                    {
                        if (task.IsFaulted)
                        {
                            MessageBox.Show($"Error adding asset: {task.Exception.InnerException.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        else
                        {
                            Dispatcher.Invoke(() => { UpdateAssetDisplay(); UpdateTreeView(); });
                        }
                    });
                    assetLoadingBar.Value++;
                    Dispatcher.Invoke(() => assetLoadingText.Text = $"Loading {assetLoadingBar.Value}/{fileCount} files...");
                }
                assetLoadingBar.Visibility = Visibility.Hidden;
                assetLoadingText.Text = "Loading complete.";
            }
        }

        private void btnImportFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "Select a folder to import assets from"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var result = MessageBox.Show(
                    "Include subdirectories in import?",
                    "Import Options",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                bool includeSubdirectories = (result == MessageBoxResult.Yes);
                SearchOption searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                string[] files = Directory.GetFiles(dialog.SelectedPath, "*.*", searchOption);

                int fileCount = files.Length;
                assetLoadingBar.Visibility = Visibility.Visible;
                assetLoadingBar.Maximum = fileCount;
                assetLoadingBar.Value = 0;
                assetLoadingText.Text = $"Loading 0/{fileCount} files...";

                foreach (var file in files)
                {
                    // Skip system/hidden files
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Attributes.HasFlag(FileAttributes.Hidden) ||
                        fileInfo.Attributes.HasFlag(FileAttributes.System))
                        continue;

                    assetService.AddAssetAsync(file).ContinueWith(task =>
                    {
                        if (task.IsFaulted)
                        {
                            MessageBox.Show($"Error adding asset: {task.Exception.InnerException.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        else
                        {
                            Dispatcher.Invoke(() => { UpdateAssetDisplay(); UpdateTreeView(); });
                        }
                    });
                    assetLoadingBar.Value++;
                    Dispatcher.Invoke(() => assetLoadingText.Text = $"Loading {assetLoadingBar.Value}/{fileCount} files...");
                }
                assetLoadingBar.Visibility = Visibility.Hidden;
                assetLoadingText.Text = "Loading complete.";
            }
        }

        private void UpdateAssetDisplay()
        {
            if (assetService == null)
            {
                return;
            }

            AssetService.AssetFilter filter = new AssetService.AssetFilter
            {
                CategoryId = (tvCategories.SelectedItem as TreeViewItem)?.Tag as int?,
                SearchTerm = txtSearch.Text.Trim(),
                TagIds = ttpSelector.itemsControl.Items.Cast<TagDisplay>()
                    .Where(td => td.IsSelected)
                    .Select(td => td.Tag.Id)
                    .ToList(),
                AllTagsRequired = UseAndMode
            };

            List<Asset> filteredAssets = assetService.GetAssetsAsync(filter).Result;

            dgAssets.ItemsSource = filteredAssets;
            lvGridAssets.ItemsSource = filteredAssets;

            txtAssetCount.Text = $"{filteredAssets.Count} assets";

            originalAssetName = string.Empty;
            originalAssetDescription = string.Empty;
            originalAssetCategorySelectedIndex = -1;
            originalAssetTagIds = new List<int>();
        }

        // Helper to get expanded category IDs (recursive for all descendants)
        private HashSet<int> GetExpandedCategoryIds(ItemsControl parent)
        {
            var expandedIds = new HashSet<int>();
            for (int i = 0; i < parent.Items.Count; i++)
            {
                var tvi = parent.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (tvi != null)
                {
                    if (tvi.IsExpanded && tvi.Tag is int id)
                        expandedIds.Add(id);
                    if (tvi.Items.Count > 0)
                    {
                        // Recursively check all descendants
                        foreach (var childId in GetExpandedCategoryIds(tvi))
                            expandedIds.Add(childId);
                    }
                }
            }
            return expandedIds;
        }

        // Helper to expand categories by ID (recursive for all descendants)
        private void ExpandCategoriesById(ItemsControl parent, HashSet<int> expandedIds)
        {
            for (int i = 0; i < parent.Items.Count; i++)
            {
                var tvi = parent.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (tvi != null && tvi.Tag is int id)
                {
                    if (expandedIds.Contains(id))
                    {
                        tvi.IsExpanded = true;
                        tvi.UpdateLayout();
                    }
                    if (tvi.Items.Count > 0)
                        ExpandCategoriesById(tvi, expandedIds);
                }
            }
        }

        private void UpdateTreeView()
        {
            if (categoryService == null)
            {
                return;
            }

            // Save expanded state
            HashSet<int> expandedIds = GetExpandedCategoryIds(tvCategories);

            // Get all categories and asset counts
            List<Category> categories = categoryService.GetAllCategoriesAsync().Result;
            Dictionary<int, int> assetCounts = categoryService.GetAssetCountsByCategoryAsync().Result;

            tvCategories.Items.Clear();
            foreach (var category in categories.Where(c => c.ParentCategoryId == null))
            {
                int count = assetCounts.TryGetValue(category.Id, out int c) ? c : 0;
                TreeViewItem item = new TreeViewItem
                {
                    Header = $"{category.CategoryName} ({count})",
                    Tag = category.Id
                };
                item.Items.Add(new TreeViewItem { Header = "Loading..." }); // Placeholder for children
                tvCategories.Items.Add(item);
                LoadChildCategories(item, categories, assetCounts);
            }

            // Restore expanded state
            ExpandCategoriesById(tvCategories, expandedIds);

            // Also update cmbAssetCategory with every category, including child categories
            cmbAssetCategory.Items.Clear();
            //add a "-" placeholder that isn't selectable or visible to the user, but is used for multi editing when the seleted assets have different categories
            cmbAssetCategory.Items.Add(new ComboBoxItem { Content = "-", Tag = -2, IsEnabled = false, Visibility = Visibility.Collapsed });
            cmbAssetCategory.Items.Add(new ComboBoxItem { Content = "None", Tag = -1 });
            foreach (var category in categories)
            {
                int count = assetCounts.TryGetValue(category.Id, out int c) ? c : 0;
                cmbAssetCategory.Items.Add(new ComboBoxItem
                {
                    Content = category.CategoryName,
                    Tag = category.Id
                });
            }
        }

        private void LoadChildCategories(TreeViewItem parentItem, List<Category> allCategories, Dictionary<int, int> assetCounts)
        {
            int parentId = (int)parentItem.Tag;
            var childCategories = allCategories.Where(c => c.ParentCategoryId == parentId).ToList();
            foreach (var category in childCategories)
            {
                int count = assetCounts.TryGetValue(category.Id, out int c) ? c : 0;
                TreeViewItem childItem = new TreeViewItem
                {
                    Header = $"{category.CategoryName} ({count})",
                    Tag = category.Id
                };
                childItem.Items.Add(new TreeViewItem { Header = "Loading..." }); // Placeholder for children
                parentItem.Items.Add(childItem);
                LoadChildCategories(childItem, allCategories, assetCounts);
            }
            //remove the placeholder item
            if (parentItem.Items.Count > 0 && parentItem.Items[0] is TreeViewItem placeholder && placeholder.Header.ToString() == "Loading...")
            {
                parentItem.Items.RemoveAt(0);
            }
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = txtSearch.Text;

            // Clear immediately if empty
            if (string.IsNullOrWhiteSpace(query))
            {
                CancelDebounce();
                UpdateAssetDisplay();
                return;
            }

            // Restart debounce timer
            CancelDebounce();
            _debounceTimer = new Timer(_ =>
            {
                Dispatcher.Invoke(() => UpdateAssetDisplay());
            }, null, DebounceDelay, Timeout.Infinite);
        }

        private void txtSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CancelDebounce();
                string query = txtSearch.Text.Trim();
                if (!string.IsNullOrEmpty(query))
                    UpdateAssetDisplay();
            }
        }

        private void CancelDebounce()
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }


        private void tvCategories_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            UpdateAssetDisplay();
        }

        private void tvCategories_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var treeView = sender as TreeView;

            // Get the element that was clicked
            var hitTest = VisualTreeHelper.HitTest(treeView, e.GetPosition(treeView));

            // Check if we clicked on a TreeViewItem or its content
            var treeViewItem = FindParent<TreeViewItem>(hitTest.VisualHit);

            // If no TreeViewItem was found, clear selection recursively
            if (treeViewItem == null)
            {
                treeView.Focus();
                DeselectAllTreeViewItems(treeView);
            }

            UpdateAssetDisplay();
        }

        private void DeselectAllTreeViewItems(ItemsControl parent)
        {
            foreach (object item in parent.Items)
            {
                var tvi = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (tvi != null)
                {
                    tvi.IsSelected = false;
                    if (tvi.Items.Count > 0)
                    {
                        DeselectAllTreeViewItems(tvi);
                    }
                }
            }
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;

            if (parentObject is T parent)
                return parent;

            return FindParent<T>(parentObject);
        }

        private void btnNewCategory_Click(object sender, RoutedEventArgs e)
        {
            CreateNewCategory();
        }

        private void menuNewCategory_Click(object sender, RoutedEventArgs e)
        {
            CreateNewCategory();
        }

        private void CreateNewCategory()
        {
            var dialog = new InputWindow
            {
                Title = "New Category Name"
            };

            if (dialog.ShowDialog() == true)
            {
                //create a new category with the entered name, save it to the database, and update the tree view. If an item in tree view is selected, set the parent category to that item
                string categoryName = dialog.Result.Trim();
                if (string.IsNullOrEmpty(categoryName))
                {
                    MessageBox.Show("Category name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                int? parentCategoryId = null;
                if (tvCategories.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is int categoryId)
                {
                    parentCategoryId = categoryId;
                }
                try
                {
                    categoryService.AddCategoryAsync(categoryName, parentCategoryId).Wait();
                    UpdateTreeView();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error creating category: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnRenameCategory_Click(object sender, RoutedEventArgs e)
        {
            RenameCategory();
        }

        private void menuRenameCategory_Click(object sender, RoutedEventArgs e)
        {
            RenameCategory();
        }

        private void RenameCategory()
        {
            //if a category is selected in the tree view, prompt for a new name and rename it
            if (tvCategories.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is int categoryId)
            {
                var dialog = new InputWindow
                {
                    Title = "Rename Category",
                    InputTextBox = { Text = selectedItem.Header.ToString().Split('(')[0].Trim() } // Get the current category name
                };

                if (dialog.ShowDialog() == true)
                {
                    string newCategoryName = dialog.Result.Trim();
                    if (string.IsNullOrEmpty(newCategoryName))
                    {
                        MessageBox.Show("Category name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    try
                    {
                        categoryService.UpdateCategoryAsync(categoryId, newCategoryName).Wait();
                        UpdateTreeView();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error renaming category: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a category to rename.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnDeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            DeleteCategory();
        }

        private void menuDeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            DeleteCategory();
        }

        private void DeleteCategory()
        {
            //if a category is selected in the tree view, prompt for confirmation and delete it
            if (tvCategories.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is int categoryId)
            {
                var result = MessageBox.Show("Are you sure you want to delete this category?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        categoryService.DeleteCategoryAsync(categoryId).Wait();
                        UpdateTreeView();
                        UpdateAssetDisplay();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error deleting category: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a category to delete.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void tvCategories_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var treeView = sender as TreeView;
            var hitTest = VisualTreeHelper.HitTest(treeView, Mouse.GetPosition(treeView));
            var treeViewItem = FindParent<TreeViewItem>(hitTest.VisualHit);
            if (treeViewItem != null)
            {
                treeViewItem.IsSelected = true;
            }
        }

        private void dgAssets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedAssetChanged();
        }

        private void lvGridAssets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedAssetChanged();
        }

        private async void SelectedAssetChanged()
        {
            if (_suppressSelectionChanged)
                return;

            if (AssetEditsMade)
            {
                // Prompt to save changes if any edits were made
                var result = MessageBox.Show("You have unsaved changes. Do you want to save them?",
                    "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    await UpdateAssets(selectedDataGridItems);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    _suppressSelectionChanged = true;

                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        switch (currentViewMode)
                        {
                            case ViewMode.List:
                                dgAssets.SelectedItems.Clear();
                                dgAssets.UpdateLayout();

                                foreach (var item in selectedDataGridItems)
                                {
                                    dgAssets.SelectedItems.Add(item);
                                }
                                break;
                            case ViewMode.Grid:
                                lvGridAssets.SelectedItems.Clear();
                                lvGridAssets.UpdateLayout();

                                foreach (var item in selectedDataGridItems)
                                {
                                    lvGridAssets.SelectedItems.Add(item);
                                }
                                break;
                        }

                        _suppressSelectionChanged = false;
                    }), DispatcherPriority.Loaded);

                    return;
                }
            }

            if (currentViewMode == ViewMode.List ? dgAssets.SelectedItems.Count == 1 : lvGridAssets.SelectedItems.Count == 1)
            {
                var selectedAsset = (currentViewMode == ViewMode.List ? dgAssets.SelectedItem : lvGridAssets.SelectedItem) as Asset;
                if (selectedAsset == null)
                {
                    return;
                }
                imgPreview.Source = new BitmapImage(new Uri(selectedAsset.ThumbnailPath, UriKind.Absolute));
                txtAssetName.Text = selectedAsset.Name;
                txtAssetName.IsEnabled = true;
                txtAssetDescription.Text = selectedAsset.Description;
                txtAssetDescription.IsEnabled = true;
                cmbAssetCategory.SelectedItem = cmbAssetCategory.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(item => (int)item.Tag == selectedAsset.CategoryId) ?? cmbAssetCategory.Items[1]; // Default to "None" if not found
                cmbAssetCategory.IsEnabled = true;

                txtFilePath.Text = selectedAsset.FilePath;
                txtFileSize.Text = selectedAsset.FileSizeFormatted;
                txtDateCreated.Text = selectedAsset.DateCreated.ToString("g");
                txtDateModified.Text = selectedAsset.DateModified.ToString("g");

                // Get tags for the selected asset and update ttpAssetTags
                var tags = await tagService.GetTagsForAssetAsync(selectedAsset.Id);
                foreach (var tagDisplay in ttpAssetTags.itemsControl.Items.Cast<TagDisplay>())
                {
                    tagDisplay.IsSelected = tags.Any(t => t.Id == tagDisplay.Tag.Id);
                }
                ttpAssetTags.itemsControl.IsEnabled = true;

                btnSaveChanges.IsEnabled = true;
                btnDeleteAsset.IsEnabled = true;
                btnOpenFile.IsEnabled = true;
                btnShowInExplorer.IsEnabled = true;
                btnCopyPath.IsEnabled = true;
            }
            else if (currentViewMode == ViewMode.List ? dgAssets.SelectedItems.Count == 0 : lvGridAssets.SelectedItems.Count == 0)
            {
                // Clear the preview and details if no item is selected
                imgPreview.Source = null;
                txtAssetName.Text = string.Empty;
                txtAssetName.IsEnabled = false;
                txtAssetDescription.Text = string.Empty;
                txtAssetDescription.IsEnabled = false;
                cmbAssetCategory.SelectedIndex = 0;
                cmbAssetCategory.IsEnabled = false;
                txtFilePath.Text = string.Empty;
                txtFileSize.Text = string.Empty;
                txtDateCreated.Text = string.Empty;
                txtDateModified.Text = string.Empty;

                ttpAssetTags.itemsControl.Items.Cast<TagDisplay>().ToList().ForEach(td => td.IsSelected = false);
                ttpAssetTags.itemsControl.IsEnabled = false;

                btnSaveChanges.IsEnabled = false;
                btnDeleteAsset.IsEnabled = false;
                btnOpenFile.IsEnabled = false;
                btnShowInExplorer.IsEnabled = false;
                btnCopyPath.IsEnabled = false;
            }
            else
            {
                var selectedAssets = GetSelectedAssets();

                imgPreview.Source = null;
                //display a string.join of all selected asset names
                txtAssetName.Text = string.Join(", ", selectedAssets.Select(a => a.Name));
                txtAssetName.IsEnabled = false;
                //display description if all assets have the same one, otherwise "-"
                txtAssetDescription.Text = selectedAssets.All(a => a.Description == selectedAssets[0].Description) ? selectedAssets[0].Description : "-";
                txtAssetDescription.IsEnabled = true;
                //display category if all assets have the same one, otherwise "-" - the item[0] of the combo box
                cmbAssetCategory.SelectedItem = selectedAssets.All(a => a.CategoryId == selectedAssets[0].CategoryId)
                    ? cmbAssetCategory.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (int)item.Tag == selectedAssets[0].CategoryId) ?? cmbAssetCategory.Items[1]
                    : cmbAssetCategory.Items[0];
                cmbAssetCategory.IsEnabled = true;

                txtFilePath.Text = "-";
                txtFileSize.Text = "-";
                txtDateCreated.Text = "-";
                txtDateModified.Text = "-";

                ttpAssetTags.itemsControl.Items.Cast<TagDisplay>().ToList().ForEach(td => td.IsSelected = false);
                ttpAssetTags.itemsControl.IsEnabled = true;

                btnSaveChanges.IsEnabled = true;
                btnDeleteAsset.IsEnabled = true;
                btnOpenFile.IsEnabled = true;
                btnShowInExplorer.IsEnabled = true;
                btnCopyPath.IsEnabled = false;
            }

            selectedDataGridItems = GetSelectedAssets();
            originalAssetName = txtAssetName.Text;
            originalAssetDescription = txtAssetDescription.Text;
            originalAssetCategorySelectedIndex = cmbAssetCategory.SelectedIndex;
            originalAssetTagIds = ttpAssetTags.itemsControl.Items.Cast<TagDisplay>()
                .Where(td => td.IsSelected)
                .Select(td => td.Tag.Id)
                .ToList();

            txtSelectedCount.Text = $"{selectedDataGridItems.Count} selected";
        }

        private async Task UpdateSelectedAsset()
        {
            List<Asset> selectedAssets = GetSelectedAssets();

            await UpdateAssets(selectedAssets);
        }

        private async Task UpdateAssets(List<Asset> selectedAssets)
        {
            if (selectedAssets.Count == 0)
            {
                MessageBox.Show("Please select an asset to update.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (selectedAssets.Count == 1)
            {
                Asset selectedAsset = selectedAssets[0];

                selectedAsset.Name = txtAssetName.Text.Trim();
                selectedAsset.Description = txtAssetDescription.Text.Trim();
                selectedAsset.CategoryId = cmbAssetCategory.SelectedItem is ComboBoxItem selectedItem ? (int?)selectedItem.Tag : null;

                IEnumerable<int> tagIds = ttpAssetTags.itemsControl.Items.Cast<TagDisplay>()
                    .Where(td => td.IsSelected)
                    .Select(td => td.Tag.Id);

                await tagService.SetTagsForAssetAsync(selectedAsset.Id, tagIds);

                bool result = await assetService.UpdateAssetAsync(selectedAsset);

                if (!result)
                {
                    MessageBox.Show("Failed to update asset. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                bool changesWereMade = false;
                if (txtAssetDescription.Text != "-")
                {
                    // If the description is not "-", update all selected assets with the same description
                    string description = txtAssetDescription.Text.Trim();
                    foreach (var asset in selectedAssets)
                    {
                        asset.Description = description;
                    }
                    changesWereMade = true;
                }
                if (cmbAssetCategory.SelectedIndex != 0)
                {
                    int? categoryID = cmbAssetCategory.SelectedItem is ComboBoxItem selectedItem ? (int?)selectedItem.Tag : null;
                    foreach (var asset in selectedAssets)
                    {
                        asset.CategoryId = categoryID;
                    }
                    changesWereMade = true;
                }

                //if any tag buttons are on, changes were made
                if (ttpAssetTags.itemsControl.Items.Cast<TagDisplay>().Any(td => td.IsSelected))
                {
                    IEnumerable<int> tagIds = ttpAssetTags.itemsControl.Items.Cast<TagDisplay>()
                        .Where(td => td.IsSelected)
                        .Select(td => td.Tag.Id);
                    foreach (var asset in selectedAssets)
                    {
                        await tagService.SetTagsForAssetAsync(asset.Id, tagIds);
                    }
                }

                if (changesWereMade)
                {
                    foreach (var asset in selectedAssets)
                    {
                        await assetService.UpdateAssetAsync(asset);
                    }
                }
            }

            originalAssetName = txtAssetName.Text;
            originalAssetDescription = txtAssetDescription.Text;
            originalAssetCategorySelectedIndex = cmbAssetCategory.SelectedIndex;
            originalAssetTagIds = ttpAssetTags.itemsControl.Items.Cast<TagDisplay>()
                .Where(td => td.IsSelected)
                .Select(td => td.Tag.Id)
                .ToList();

            UpdateAssetDisplay();
        }

        private async void RemoveSelectedAsset()
        {
            List<Asset> selectedAssets = GetSelectedAssets();

            if (selectedAssets.Count == 0)
            {
                MessageBox.Show("Please select an asset to delete.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (selectedAssets.Count == 1)
            {
                bool result = await assetService.DeleteAssetAsync(selectedAssets[0].Id);

                if (!result)
                {
                    MessageBox.Show("Failed to delete asset. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    UpdateAssetDisplay();
                }
            }
            else
            {
                //show a confirmation dialog that includes the number of selected assets, if confirmation is given call DeteteAssetsAsync on each one
                var result = MessageBox.Show($"Are you sure you want to remove {selectedAssets.Count} selected assets? This will only remove them from the database.",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    foreach (var asset in selectedAssets)
                    {
                        await assetService.DeleteAssetAsync(asset.Id);
                    }
                }
            }
        }

        private void OpenSelectedAsset()
        {
            //open selected asset file in default application
            List<Asset> selectedAssets = GetSelectedAssets();

            if (selectedAssets.Count == 1)
            {
                System.Diagnostics.Process.Start(selectedAssets[0].FilePath);
            }
            else
            {
                // Show a "you're about to open multiple files" message with a specific number and only do so if the user confirms

                var result = MessageBox.Show($"You are about to open {selectedAssets.Count} files. Do you want to continue?",
                    "Confirm Open", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    foreach (var asset in selectedAssets)
                    {
                        System.Diagnostics.Process.Start(asset.FilePath);
                    }
                }
            }
        }

        private void OpenSelectedAssetFolder()
        {
            var selectedAssets = GetSelectedAssets();

            if (selectedAssets.Count == 1)
            {
                // Show the asset file in Windows Explorer
                string directory = Path.GetDirectoryName(selectedAssets[0].FilePath);
                if (directory != null)
                {
                    System.Diagnostics.Process.Start("explorer.exe", directory);
                }
            }
            else
            {
                // Show a "you're about to open multiple folders" message with a specific number and only do so if the user confirms. Don't open the same folder more than once
                var uniqueDirectories = selectedAssets.Select(a => Path.GetDirectoryName(a.FilePath)).Distinct().ToList();
                if (uniqueDirectories.Count == 1)
                {
                    System.Diagnostics.Process.Start("explorer.exe", uniqueDirectories[0]);
                }
                else
                {
                    var result = MessageBox.Show($"You are about to open {uniqueDirectories.Count} folders. Do you want to continue?",
                        "Confirm Open", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        foreach (var directory in uniqueDirectories)
                        {
                            System.Diagnostics.Process.Start("explorer.exe", directory);
                        }
                    }
                }
            }
        }

        private async void btnSaveChanges_Click(object sender, RoutedEventArgs e)
        {
            await UpdateSelectedAsset();
            UpdateTreeView();
        }

        private void btnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            txtSearch.Text = string.Empty;
        }

        private void btnDeleteAsset_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedAsset();
            UpdateTreeView();
        }

        private void btnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedAsset();
        }

        private void btnShowInExplorer_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedAssetFolder();
        }

        private void btnCopyPath_Click(object sender, RoutedEventArgs e)
        {
            var selectedAsset = (currentViewMode == ViewMode.List ? dgAssets.SelectedItem : lvGridAssets.SelectedItem) as Asset;
            if (selectedAsset != null)
            {
                System.Windows.Clipboard.SetText(selectedAsset.FilePath);
                MessageBox.Show("File path copied to clipboard.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void dgAssets_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedAsset();
        }

        private void lvGridAssets_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedAsset();
        }

        private void menuOpenAsset_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedAsset();
        }

        private void menuShowInExplorer_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedAssetFolder();
        }

        private void menuDeleteAsset_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedAsset();
            UpdateTreeView();
        }

        private void menuOpenAssetGrid_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedAsset();
        }

        private void menuShowInExplorerGrid_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedAssetFolder();
        }

        private void menuDeleteAssetGrid_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedAsset();
            UpdateTreeView();
        }

        private List<Asset> GetSelectedAssets()
        {
            switch (currentViewMode)
            {
                case ViewMode.Grid:
                    return lvGridAssets.SelectedItems.Cast<Asset>().ToList();
                case ViewMode.List:
                    return dgAssets.SelectedItems.Cast<Asset>().ToList();
                default:
                    throw new InvalidOperationException("Unknown view mode");
            }
        }

        private void ToggleAndOrTagFiltering()
        {
            UseAndMode = !UseAndMode;
            BtnAnd.IsChecked = UseAndMode;
            BtnOr.IsChecked = !UseAndMode;
            UpdateAssetDisplay();
        }

        private void BtnOr_Click(object sender, RoutedEventArgs e)
        {
            ToggleAndOrTagFiltering();
        }

        private void BtnAnd_Click(object sender, RoutedEventArgs e)
        {
            ToggleAndOrTagFiltering();
        }

        private void btnNewTag_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputWindow(true)
            {
                Title = "New Tag Settings"
            };

            if (dialog.ShowDialog() == true)
            {
                //create a new tag with the entered name and color, save it to the database, and update the tag displays list
                string tagName = dialog.Result.Trim();
                string tagColor = dialog.ResultColor;
                if (string.IsNullOrEmpty(tagName))
                {
                    MessageBox.Show("Tag name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                try
                {
                    tagService.AddTagAsync(tagName, tagColor).Wait();
                    UpdateTagList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error creating tag: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnEditTag_Click(object sender, RoutedEventArgs e)
        {
            var tagSelectionDialog = new SingleTagSelectionWindow();
            tagSelectionDialog.ttpSelector.itemsControl.ItemsSource = TagDisplays;
            if (tagSelectionDialog.ShowDialog() == true)
            {
                Tag selectedTag = tagSelectionDialog.result;
                var dialog = new InputWindow(true)
                {
                    Title = "Edit Tag Settings",
                    InputTextBox = { Text = selectedTag.TagName },
                    ColorPickerButton = { Visibility = Visibility.Visible }
                };
                dialog.SetDisplayedColor(selectedTag.Color);
                if (dialog.ShowDialog() == true)
                {
                    //update the selected tag with the new name and color, save it to the database, and update the tag displays list
                    string newTagName = dialog.Result.Trim();
                    string newTagColor = dialog.ResultColor;
                    if (string.IsNullOrEmpty(newTagName))
                    {
                        MessageBox.Show("Tag name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    selectedTag.TagName = newTagName;
                    selectedTag.Color = newTagColor;
                    try
                    {
                        tagService.UpdateTagAsync(selectedTag).Wait();
                        UpdateTagList();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error updating tag: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void btnDeleteTag_Click(object sender, RoutedEventArgs e)
        {
            var tagSelectionDialog = new SingleTagSelectionWindow();
            tagSelectionDialog.ttpSelector.itemsControl.ItemsSource = TagDisplays;
            if (tagSelectionDialog.ShowDialog() == true)
            {
                Tag selectedTag = tagSelectionDialog.result;
                var result = MessageBox.Show($"Are you sure you want to delete the tag '{selectedTag.TagName}'?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        tagService.RemoveTagAsync(selectedTag.Id).Wait();
                        UpdateTagList();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error deleting tag: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void btnGridView_Click(object sender, RoutedEventArgs e)
        {
            if(currentViewMode == ViewMode.Grid)
            {
                return;
            }

            currentViewMode = ViewMode.Grid;

            dgAssets.SelectedItems.Clear();
            dgAssets.Visibility = Visibility.Collapsed;
            lvGridAssets.Visibility = Visibility.Visible;

            Properties.Settings.Default.UseGridView = true;
            Properties.Settings.Default.Save();
        }

        private void btnListView_Click(object sender, RoutedEventArgs e)
        {
            if(currentViewMode == ViewMode.List)
            {
                return;
            }

            currentViewMode = ViewMode.List;

            lvGridAssets.SelectedItems.Clear();
            lvGridAssets.Visibility = Visibility.Collapsed;
            dgAssets.Visibility = Visibility.Visible;

            Properties.Settings.Default.UseGridView = false;
            Properties.Settings.Default.Save();
        }
    }
}
