using System.IO.Compression;
using Souma.EmailLogging.Abstractions;

namespace Souma.EmailLogging.Services;

/// <summary>
/// Implementación concreta de <see cref="IFileSystem"/> que opera directamente
/// sobre el sistema de archivos del sistema operativo.
/// </summary>
/// <remarks>
/// Esta clase se registra como Singleton en el contenedor de DI.
/// En tests unitarios, se reemplaza por un mock de <see cref="IFileSystem"/>
/// para evitar operaciones reales de I/O.
/// </remarks>
internal sealed class DefaultFileSystem : IFileSystem
{
    /// <inheritdoc/>
    public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        // Escribir primero a un archivo temporal y luego mover
        // para garantizar atomicidad en caso de fallo durante la escritura
        string tempPath = $"{path}.tmp";

        await File.WriteAllTextAsync(tempPath, content, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <inheritdoc/>
    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    /// <inheritdoc/>
    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    /// <inheritdoc/>
    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    /// <inheritdoc/>
    public long GetFileSize(string path)
    {
        FileInfo fileInfo = new(path);
        return fileInfo.Length;
    }

    /// <inheritdoc/>
    public string[] GetFiles(string directory, string searchPattern)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, searchPattern);
    }

    /// <inheritdoc/>
    public void DeleteFile(string path)
    {
        File.Delete(path);
    }

    /// <inheritdoc/>
    public async Task CompressFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using FileStream sourceStream = File.OpenRead(sourcePath);
        await using FileStream destinationStream = File.Create(destinationPath);
        await using GZipStream gzipStream = new(destinationStream, CompressionLevel.Optimal);

        await sourceStream.CopyToAsync(gzipStream, cancellationToken);
    }
}
