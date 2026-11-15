using System.Security.Cryptography;
using EzCooking.Api.Configuration;
using EzCooking.Api.Recipes;

namespace EzCooking.Api.Media;

public static class MediaManagement
{
    public static async Task<string> Hash(Stream stream, CancellationToken ct) =>
        Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));

    public static async Task BackfillUploads(IRecipeStore store, AppPaths paths, CancellationToken ct)
    {
        foreach (var id in await store.UploadsWithoutHash(ct))
        {
            var file = Path.Combine(paths.For(id), "upload");
            FileStream stream;
            try
            {
                stream = File.OpenRead(file);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            await using var input = stream;
            await store.SetUploadHash(id, await Hash(input, ct), ct);
        }
    }

    public static void DeleteRecipeMedia(AppPaths paths, Guid id)
    {
        var dir = paths.For(id);
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(dir);
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (FileNotFoundException)
        {
            return;
        }
        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException("The recipe media path is not a directory.");
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The recipe media directory is a link.");
        }

        foreach (var child in Directory.EnumerateDirectories(dir))
        {
            throw new IOException($"Unexpected directory in recipe media: {Path.GetFileName(child)}.");
        }

        Directory.Delete(dir, recursive: true);
    }
}
