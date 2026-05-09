using Accountant.ReviewSite.Services;
using Accountant.ReviewSite.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSingleton<DocumentStore>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseMiddleware<BasicAuthMiddleware>();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapGet("/document-image", (string file, DocumentStore store) =>
{
    var path = store.ResolveImagePath(file);
    if (path is null)
    {
        return Results.NotFound();
    }

    return Results.File(path, contentType: GetContentType(path));
});

app.MapRazorPages()
   .WithStaticAssets();

app.Run();

static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".png" => "image/png",
    ".webp" => "image/webp",
    ".jpg" or ".jpeg" => "image/jpeg",
    _ => "application/octet-stream"
};
