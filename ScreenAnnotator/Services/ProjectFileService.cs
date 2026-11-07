using System.Text.Json;
using ScreenAnnotator.Models;

namespace ScreenAnnotator.Services;

public static class ProjectFileService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static void Save(string path, AnnotationProject project) => File.WriteAllText(path, JsonSerializer.Serialize(project, Options));
    public static AnnotationProject Load(string path) => JsonSerializer.Deserialize<AnnotationProject>(File.ReadAllText(path), Options) ?? new();
}
