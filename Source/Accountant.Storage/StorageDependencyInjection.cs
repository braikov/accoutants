using Accountant.Storage.Abstractions;
using Accountant.Storage.Local;
using Accountant.Storage.Thumbnails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Accountant.Storage;

public static class StorageDependencyInjection
{
    public static IServiceCollection AddAccountantStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStore, LocalFileStore>();

        services.AddSingleton<IThumbnailRenderer, ImageThumbnailRenderer>();
        services.AddSingleton<IThumbnailRenderer, PdfThumbnailRenderer>();
        services.AddSingleton<ThumbnailDispatcher>();
        return services;
    }
}
