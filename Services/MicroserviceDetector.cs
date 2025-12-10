using PathwayDevTool.Models;
using System.IO;

namespace PathwayDevTool.Services
{
    public class MicroserviceDetector
    {
        public List<AppData> Detect(string rootPath)
        {
            var src = Path.Combine(rootPath, "src");

            if (!Directory.Exists(src))
                throw new DirectoryNotFoundException($"Invalid structure: {src} not found");

            return
                [
                    Create(ProjectType.Microservice, Path.Combine(src, "Services")),
                    Create(ProjectType.Gateway,      Path.Combine(src, "ApiGateways")),
                    Create(ProjectType.Web,          Path.Combine(src, "Web"))
                ];
        }

        private bool IsApiProject(string csprojPath)
        {
            var text = File.ReadAllText(csprojPath);

            return text.Contains("Microsoft.NET.Sdk.Web")
                || csprojPath.EndsWith(".Api.csproj", StringComparison.OrdinalIgnoreCase);
        }
        private AppData Create(ProjectType type, string root)
        {
            var projects = Directory.Exists(root)
                ? Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
                           .Where(IsApiProject)
                           .Select(csproj => new Microservice
                           {
                               Name = Path.GetFileNameWithoutExtension(csproj),
                               ProjectPath = csproj,
                               ProcessId = 0
                           })
                           .ToList()
                : [];

            return new AppData
            {
                Type = type,
                Projects = projects
            };
        }

    }
}
