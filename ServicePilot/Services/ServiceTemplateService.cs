using System.IO;
using System.Text.Json;
using ServicePilot.Models;

namespace ServicePilot.Services;

public record ServiceTemplateDefinition(string Id, string Name, string Description);

public static class ServiceTemplateService
{
    public static IReadOnlyList<ServiceTemplateDefinition> Templates { get; } =
    [
        new("auto", "自动识别", "根据文件夹内容自动选择 Node.js、.NET 或 Python 模板。"),
        new("node", "Node.js 开发服务", "检测 package.json，按需安装依赖并运行 npm script。"),
        new("dotnet", ".NET 开发服务", "检测 .csproj，执行 dotnet restore 和 dotnet run。"),
        new("python", "Python 开发服务", "检测 requirements.txt、manage.py、app.py 或 main.py。")
    ];

    public static ServiceConfig Create(string templateId, string workingDirectory, string? serviceName = null, bool autoStart = false)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"工作目录不存在: {workingDirectory}");

        var resolvedTemplate = ResolveTemplate(templateId, workingDirectory);
        var name = string.IsNullOrWhiteSpace(serviceName)
            ? Path.GetFileName(Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : serviceName.Trim();

        return resolvedTemplate switch
        {
            "node" => CreateNode(name, workingDirectory, autoStart),
            "dotnet" => CreateDotNet(name, workingDirectory, autoStart),
            "python" => CreatePython(name, workingDirectory, autoStart),
            _ => throw new InvalidOperationException($"无法为目录识别模板: {workingDirectory}")
        };
    }

    private static string ResolveTemplate(string templateId, string workingDirectory)
    {
        var normalized = string.IsNullOrWhiteSpace(templateId)
            ? "auto"
            : templateId.Trim().ToLowerInvariant();

        if (normalized != "auto")
            return normalized;

        if (File.Exists(Path.Combine(workingDirectory, "package.json")))
            return "node";

        if (Directory.EnumerateFiles(workingDirectory, "*.csproj", SearchOption.TopDirectoryOnly).Any())
            return "dotnet";

        if (File.Exists(Path.Combine(workingDirectory, "requirements.txt")) ||
            File.Exists(Path.Combine(workingDirectory, "manage.py")) ||
            File.Exists(Path.Combine(workingDirectory, "app.py")) ||
            File.Exists(Path.Combine(workingDirectory, "main.py")))
            return "python";

        throw new InvalidOperationException("自动识别失败：未发现 package.json、.csproj 或 Python 入口文件。");
    }

    private static ServiceConfig CreateNode(string name, string workingDirectory, bool autoStart)
    {
        var script = DetectNpmScript(workingDirectory);
        return new ServiceConfig
        {
            Name = name,
            WorkingDirectory = workingDirectory,
            AutoStart = autoStart,
            ScriptSteps =
            [
                new ScriptStep
                {
                    Name = "Install dependencies",
                    ScriptType = ScriptType.Batch,
                    Content = "if not exist node_modules npm install",
                    Order = 0
                },
                new ScriptStep
                {
                    Name = $"Run npm {script}",
                    ScriptType = ScriptType.Batch,
                    Content = $"npm run {script}",
                    Order = 1
                }
            ]
        };
    }

    private static ServiceConfig CreateDotNet(string name, string workingDirectory, bool autoStart)
    {
        var project = Directory.EnumerateFiles(workingDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .FirstOrDefault();
        var runCommand = project == null ? "dotnet run" : $"dotnet run --project \"{project}\"";

        return new ServiceConfig
        {
            Name = name,
            WorkingDirectory = workingDirectory,
            AutoStart = autoStart,
            ScriptSteps =
            [
                new ScriptStep
                {
                    Name = "Restore packages",
                    ScriptType = ScriptType.Batch,
                    Content = project == null ? "dotnet restore" : $"dotnet restore \"{project}\"",
                    Order = 0
                },
                new ScriptStep
                {
                    Name = "Run dotnet",
                    ScriptType = ScriptType.Batch,
                    Content = runCommand,
                    Order = 1
                }
            ]
        };
    }

    private static ServiceConfig CreatePython(string name, string workingDirectory, bool autoStart)
    {
        var hasRequirements = File.Exists(Path.Combine(workingDirectory, "requirements.txt"));
        var startCommand = DetectPythonStartCommand(workingDirectory);
        var steps = new List<ScriptStep>();

        if (hasRequirements)
        {
            steps.Add(new ScriptStep
            {
                Name = "Install requirements",
                ScriptType = ScriptType.Batch,
                Content = "python -m pip install -r requirements.txt",
                Order = steps.Count
            });
        }

        steps.Add(new ScriptStep
        {
            Name = "Run python service",
            ScriptType = ScriptType.Batch,
            Content = startCommand,
            Order = steps.Count
        });

        return new ServiceConfig
        {
            Name = name,
            WorkingDirectory = workingDirectory,
            AutoStart = autoStart,
            ScriptSteps = steps
        };
    }

    private static string DetectNpmScript(string workingDirectory)
    {
        var packageJson = Path.Combine(workingDirectory, "package.json");
        using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
        if (!document.RootElement.TryGetProperty("scripts", out var scripts))
            return "start";

        if (scripts.TryGetProperty("dev", out _))
            return "dev";

        if (scripts.TryGetProperty("start", out _))
            return "start";

        return scripts.EnumerateObject().FirstOrDefault().Name ?? "start";
    }

    private static string DetectPythonStartCommand(string workingDirectory)
    {
        if (File.Exists(Path.Combine(workingDirectory, "manage.py")))
            return "python manage.py runserver";

        if (File.Exists(Path.Combine(workingDirectory, "app.py")))
            return "python app.py";

        if (File.Exists(Path.Combine(workingDirectory, "main.py")))
            return "python main.py";

        return "python app.py";
    }
}
