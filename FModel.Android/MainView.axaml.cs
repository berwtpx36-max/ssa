using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CUE4Parse.Compression;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.VirtualFileSystem;
using System.Text.RegularExpressions;

namespace FModel.Android;

public partial class MainView : UserControl
{
    private readonly List<IStorageFile> _folderFiles = [];
    private IReadOnlyList<string> _archivePaths = [];

    public MainView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void PickPackageFolder_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            StatusText.Text = "Dosya sağlayıcısı kullanılamıyor.";
            return;
        }

        try
        {
            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Pak dosyalarının bulunduğu klasörü seçin"
            });

            if (folders.Count == 0)
                return;

            _folderFiles.Clear();
            await foreach (var item in folders[0].GetItemsAsync())
            {
                if (item is IStorageFile file)
                    _folderFiles.Add(file);
            }

            var packages = _folderFiles
                .Where(file => HasExtension(file.Name, ".pak") || HasExtension(file.Name, ".utoc"))
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(file => new PackageChoice(file.Name, file))
                .ToArray();

            PackageComboBox.ItemsSource = packages;
            PackageComboBox.SelectedItem = packages.FirstOrDefault();
            _archivePaths = [];
            FilesListBox.ItemsSource = null;
            SearchBox.IsEnabled = false;

            StatusText.Text = packages.Length == 0
                ? "Seçilen klasörde .pak veya .utoc dosyası bulunamadı."
                : $"{packages.Length} pak dosyası bulundu. Açmak için dosyayı seçip Aç'a dokunun.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Klasör okunamadı: {exception.Message}";
        }
    }

    private async void OpenPackage_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (PackageComboBox.SelectedItem is not PackageChoice package)
        {
            StatusText.Text = "Önce açılacak bir .pak veya .utoc dosyası seçin.";
            return;
        }

        var cacheRoot = global::Android.App.Application.Context.CacheDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            StatusText.Text = "Uygulama önbelleği kullanılamıyor.";
            return;
        }

        var stagingDirectory = Path.Combine(cacheRoot, "archives", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        OpenPackageButton.IsEnabled = false;
        PickPackageButton.IsEnabled = false;
        StatusText.Text = "Pak ve gerekli eşlikçi dosyalar uygulama önbelleğine kopyalanıyor…";

        try
        {
            var stagedPath = await StagePackageAsync(package, stagingDirectory);
            StatusText.Text = "Arşiv okunuyor…";
            var files = await Task.Run(() => ReadArchivePaths(stagedPath));

            _archivePaths = files;
            SearchBox.IsEnabled = true;
            SearchBox.Text = string.Empty;
            UpdateFileList();
            StatusText.Text = files.Count == 0
                ? "Arşiv açıldı ancak listelenebilir dosya yolu bulunamadı."
                : $"{files.Count:N0} dosya yolu yüklendi.";
        }
        catch (OodleException exception)
        {
            _archivePaths = [];
            FilesListBox.ItemsSource = null;
            SearchBox.IsEnabled = false;
            StatusText.Text = $"Oodle desteklenmiyor: {exception.Message}";
        }
        catch (Exception exception)
        {
            _archivePaths = [];
            FilesListBox.ItemsSource = null;
            SearchBox.IsEnabled = false;
            StatusText.Text = $"Arşiv açılamadı: {exception.Message}";
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
            }
            catch (Exception exception)
            {
                StatusText.Text += $" Geçici dosyalar silinemedi: {exception.Message}";
            }

            OpenPackageButton.IsEnabled = true;
            PickPackageButton.IsEnabled = true;
        }
    }

    private async Task<string> StagePackageAsync(PackageChoice package, string stagingDirectory)
    {
        var stagedPath = Path.Combine(stagingDirectory, package.Name);
        await CopyStorageFileAsync(package.File, stagedPath);

        if (HasExtension(package.Name, ".utoc"))
        {
            var containerStem = Path.GetFileNameWithoutExtension(package.Name);
            var partitionPattern = new Regex(
                $"^{Regex.Escape(containerStem)}(?:_s\\d+)?\\.ucas$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (var containerFile in _folderFiles.Where(file => partitionPattern.IsMatch(file.Name)))
                await CopyStorageFileAsync(containerFile, Path.Combine(stagingDirectory, containerFile.Name));
        }

        return stagedPath;
    }

    private static async Task CopyStorageFileAsync(IStorageFile source, string destinationPath)
    {
        await using var sourceStream = await source.OpenReadAsync();
        await using var destinationStream = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await sourceStream.CopyToAsync(destinationStream);
    }

    private static IReadOnlyList<string> ReadArchivePaths(string archivePath)
    {
        using IVfsReader reader = HasExtension(archivePath, ".pak")
            ? new PakFileReader(archivePath)
            : new IoStoreReader(archivePath);

        return reader.Mount(caseInsensitive: true).Keys
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e) => UpdateFileList();

    private void UpdateFileList()
    {
        var query = SearchBox.Text?.Trim();
        FilesListBox.ItemsSource = string.IsNullOrEmpty(query)
            ? _archivePaths
            : _archivePaths
                .Where(path => path.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    private static bool HasExtension(string fileName, string extension) =>
        string.Equals(Path.GetExtension(fileName), extension, StringComparison.OrdinalIgnoreCase);

    private sealed record PackageChoice(string Name, IStorageFile File);
}
