using Dapper;

namespace Makana.Api.Storage;

public sealed class UploadRepository(SqliteControlDatabase database)
{
    public void SaveUpload(string path, string? contentType, byte[] content)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            INSERT INTO uploads (id, path, content_type, content, created_at)
            VALUES (@id, @path, @type, @content, @created);
            """,
            new
            {
                id = Guid.NewGuid().ToString("N"),
                path,
                type = contentType,
                content,
                created = DateTimeOffset.UtcNow.ToString("O")
            });
    }
}
