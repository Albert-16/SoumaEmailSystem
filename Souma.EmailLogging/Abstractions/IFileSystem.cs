namespace Souma.EmailLogging.Abstractions;

/// <summary>
/// Abstracción del sistema de archivos para permitir testeo unitario
/// sin dependencia directa del sistema de archivos real.
/// </summary>
/// <remarks>
/// Esta interfaz encapsula todas las operaciones de I/O que usa
/// <see cref="Services.EmailLogCollector"/>. En producción se usa
/// <see cref="Services.DefaultFileSystem"/>, y en tests se puede
/// sustituir con un mock (Moq) para verificar comportamiento sin
/// tocar disco.
/// </remarks>
public interface IFileSystem
{
    /// <summary>
    /// Lee todo el contenido de un archivo como texto.
    /// </summary>
    /// <param name="path">Ruta completa del archivo.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Contenido del archivo como string.</returns>
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Escribe contenido de texto en un archivo, sobreescribiendo si ya existe.
    /// </summary>
    /// <param name="path">Ruta completa del archivo.</param>
    /// <param name="content">Contenido a escribir.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Verifica si un archivo existe en la ruta especificada.
    /// </summary>
    /// <param name="path">Ruta completa del archivo.</param>
    /// <returns><c>true</c> si el archivo existe.</returns>
    bool FileExists(string path);

    /// <summary>
    /// Verifica si un directorio existe en la ruta especificada.
    /// </summary>
    /// <param name="path">Ruta del directorio.</param>
    /// <returns><c>true</c> si el directorio existe.</returns>
    bool DirectoryExists(string path);

    /// <summary>
    /// Crea un directorio y todos los subdirectorios necesarios en la ruta especificada.
    /// </summary>
    /// <param name="path">Ruta del directorio a crear.</param>
    void CreateDirectory(string path);

    /// <summary>
    /// Obtiene el tamaño de un archivo en bytes.
    /// </summary>
    /// <param name="path">Ruta completa del archivo.</param>
    /// <returns>Tamaño en bytes.</returns>
    long GetFileSize(string path);

    /// <summary>
    /// Busca archivos en un directorio que coincidan con un patrón de búsqueda.
    /// </summary>
    /// <param name="directory">Directorio donde buscar.</param>
    /// <param name="searchPattern">Patrón de búsqueda (ej: "email-log-*.json").</param>
    /// <returns>Array de rutas completas de los archivos encontrados.</returns>
    string[] GetFiles(string directory, string searchPattern);

    /// <summary>
    /// Elimina un archivo de la ruta especificada.
    /// </summary>
    /// <param name="path">Ruta completa del archivo a eliminar.</param>
    void DeleteFile(string path);

    /// <summary>
    /// Copia un archivo a un archivo de destino comprimido con GZip.
    /// </summary>
    /// <param name="sourcePath">Ruta del archivo origen.</param>
    /// <param name="destinationPath">Ruta del archivo comprimido destino (.gz).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task CompressFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken);
}
