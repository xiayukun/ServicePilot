using System.IO;
using System.Text.Json;
using ServicePilot.Models;

namespace ServicePilot.Services;

public record ServiceTemplateDefinition(string Id, string Name, string Description);

public static class ServiceTemplateService
{
    private static readonly Guid DefaultDeveloperTemplateId = Guid.Parse("8fdcfd2d-8fe2-4e58-a1fd-56be53a7ff53");

    public static IReadOnlyList<ServiceTemplateDefinition> Templates { get; } =
    [
        new("auto", "自动识别", "根据文件夹内容自动选择 Node.js、.NET 或 Python 模板。"),
        new("node", "Node.js 开发服务", "检测 package.json，并生成安装依赖与运行 npm script 的动作。"),
        new("dotnet", ".NET 开发服务", "检测 .csproj，并生成 restore 与 run 动作。"),
        new("python", "Python 开发服务", "检测 requirements.txt、manage.py、app.py 或 main.py。")
    ];

    public static IReadOnlyList<ServiceTemplate> CreateBuiltInTemplates()
    {
        var now = DateTime.Now;
        return
        [
            new ServiceTemplate
            {
                Id = DefaultDeveloperTemplateId,
                Name = "默认开发动作模板",
                Description = "首次启动自动创建的通用开发动作模板：包含 Git 分支/Tag 操作、依赖/构建命令和常用工具打开入口。",
                CreatedAt = now,
                UpdatedAt = now,
                PresetVariables = [],
                ScriptSteps = CreateDefaultDeveloperSteps()
            }
        ];
    }

    private static List<ScriptStep> CreateDefaultDeveloperSteps()
    {
        var branchVariables = new[]
        {
            "main",
            "master",
            "develop",
            "dev",
            "release/1.0.0",
            "release/2.0.0",
            "feature/1.0.0",
            "feature/2.0.0",
            "hotfix/1.0.0",
            "hotfix/2.0.0"
        };
        var tagVariables = new[] { "v1.0.0", "v1.1.0", "v2.0.0", "1.0.0", "2.0.0" };
        var actions = new List<ScriptStep>
        {
            NewAction("Git 拉取当前分支: pull --ff-only", ScriptType.Batch, "git pull --ff-only"),
            NewAction("Git 安全切换分支并拉取", ScriptType.PowerShell, GitCheckoutBranchScript(force: false), true, branchVariables),
            NewAction("Git 强制切换分支（丢弃修改）", ScriptType.PowerShell, GitCheckoutBranchScript(force: true), true, branchVariables),
            NewAction("Git 安全切换 Tag", ScriptType.PowerShell, GitCheckoutTagScript(force: false), true, tagVariables),
            NewAction("Git 强制切换 Tag（丢弃修改）", ScriptType.PowerShell, GitCheckoutTagScript(force: true), true, tagVariables),
            NewAction("依赖: npm install", ScriptType.Batch, "npm install"),
            NewAction("构建: npm run build", ScriptType.Batch, "npm run build"),
            NewAction("打开: 资源管理器", ScriptType.PowerShell, OpenExplorerScript("$dir"), openLogOnRun: false),
            NewAction("打开: CMD 当前目录", ScriptType.PowerShell, DetachedOpenScript("'cmd.exe'", "('/k cd /d ' + (Quote-LnkArg $dir))"), openLogOnRun: false),
            NewAction("打开: PowerShell 当前目录", ScriptType.PowerShell, DetachedOpenScript("'powershell.exe'", "('-NoExit -Command ' + (Quote-LnkArg ('Set-Location -LiteralPath ' + (Quote-LnkArg $dir))))"), openLogOnRun: false),
            NewAction("打开: Windows Terminal", ScriptType.PowerShell, OpenToolScript("Windows Terminal", ["wt.exe"], passDirectory: true), openLogOnRun: false),
            NewAction("打开: Git Bash", ScriptType.PowerShell, OpenToolScript("Git Bash", ["git-bash.exe", @"C:\Program Files\Git\git-bash.exe"], passDirectory: true), openLogOnRun: false),
            NewAction("打开: VS Code", ScriptType.PowerShell, OpenToolScript("VS Code", ["code.cmd", "code.exe"], passDirectory: true), openLogOnRun: false),
            NewAction("打开: Cursor", ScriptType.PowerShell, OpenToolScript("Cursor", ["cursor.cmd", "cursor.exe", "Cursor.exe", @"C:\Program Files\cursor\Cursor.exe", @"$env:LOCALAPPDATA\Programs\Cursor\Cursor.exe"], passDirectory: true), openLogOnRun: false),
            NewAction("打开: Visual Studio", ScriptType.PowerShell, OpenToolScript("Visual Studio", ["devenv.exe"], passDirectory: true), openLogOnRun: false),
            NewAction("打开: IntelliJ IDEA", ScriptType.PowerShell, OpenToolScript("IntelliJ IDEA", ["idea64.exe", "idea.exe"], passDirectory: true), openLogOnRun: false),
            NewAction("打开: WebStorm", ScriptType.PowerShell, OpenToolScript("WebStorm", ["webstorm64.exe", "webstorm.exe"], passDirectory: true), openLogOnRun: false),
            NewAction("打开: Rider", ScriptType.PowerShell, OpenToolScript("Rider", ["rider64.exe", "rider.exe"], passDirectory: true), openLogOnRun: false),
            NewAction("打开: Notepad++", ScriptType.PowerShell, OpenToolScript("Notepad++", ["notepad++.exe"], passDirectory: true), openLogOnRun: false),
            NewAction("打开: Postman", ScriptType.PowerShell, OpenToolScript("Postman", ["postman.exe", "Postman.exe"], passDirectory: false), openLogOnRun: false)
        };

        return WithComposite("启动", actions, actions.Take(1));
    }

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
        var install = NewAction("Install dependencies", ScriptType.Batch, "if not exist node_modules npm install");
        var run = NewAction($"Run npm {script}", ScriptType.Batch, $"npm run {script}");
        return NewService(name, workingDirectory, autoStart, [install, run], [install, run]);
    }

    private static ServiceConfig CreateDotNet(string name, string workingDirectory, bool autoStart)
    {
        var project = Directory.EnumerateFiles(workingDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .FirstOrDefault();
        var restore = NewAction("Restore packages", ScriptType.Batch, project == null ? "dotnet restore" : $"dotnet restore \"{project}\"");
        var run = NewAction("Run dotnet", ScriptType.Batch, project == null ? "dotnet run" : $"dotnet run --project \"{project}\"");
        return NewService(name, workingDirectory, autoStart, [restore, run], [restore, run]);
    }

    private static ServiceConfig CreatePython(string name, string workingDirectory, bool autoStart)
    {
        var actions = new List<ScriptStep>();
        if (File.Exists(Path.Combine(workingDirectory, "requirements.txt")))
            actions.Add(NewAction("Install requirements", ScriptType.Batch, "python -m pip install -r requirements.txt"));

        actions.Add(NewAction("Run python service", ScriptType.Batch, DetectPythonStartCommand(workingDirectory)));
        return NewService(name, workingDirectory, autoStart, actions, actions);
    }

    private static ServiceConfig NewService(
        string name,
        string workingDirectory,
        bool autoStart,
        List<ScriptStep> actions,
        IEnumerable<ScriptStep> startupMembers)
    {
        return new ServiceConfig
        {
            Name = name,
            WorkingDirectory = workingDirectory,
            AutoStart = autoStart,
            PresetVariables = [],
            ScriptSteps = WithComposite("启动", actions, startupMembers)
        };
    }

    private static List<ScriptStep> WithComposite(string compositeName, List<ScriptStep> actions, IEnumerable<ScriptStep> members)
    {
        var memberIds = members.Select(step => step.Id).ToList();
        var result = new List<ScriptStep>
        {
            new()
            {
                Name = compositeName,
                Kind = StepKind.Composite,
                UseVariable = false,
                OpenLogOnRun = false,
                MemberStepIds = memberIds,
                Order = 0
            }
        };

        for (var i = 0; i < actions.Count; i++)
        {
            actions[i].Kind = StepKind.Action;
            actions[i].Order = i + 1;
            result.Add(actions[i]);
        }

        return result;
    }

    private static ScriptStep NewAction(
        string name,
        ScriptType scriptType,
        string content,
        bool useVariable = false,
        IEnumerable<string>? variables = null,
        bool? openLogOnRun = null)
    {
        return new ScriptStep
        {
            Name = name,
            Kind = StepKind.Action,
            ScriptType = scriptType,
            Content = content,
            UseVariable = useVariable,
            OpenLogOnRun = openLogOnRun ?? ShouldOpenLogOnRun(name, content),
            StepVariables = variables?.ToList() ?? []
        };
    }

    private static bool ShouldOpenLogOnRun(string name, string content)
    {
        if (name.StartsWith("打开", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Open", StringComparison.OrdinalIgnoreCase))
            return false;

        var text = $"{name}\n{content}";
        string[] markers =
        [
            "git", "npm", "pnpm", "yarn", "npx", "dotnet", "mvn", "mvnw", "java",
            "修改", "写入", "替换", "接口", "数据库", "配置", "启动", "编译", "构建",
            "发布", "打包", "依赖", "安装", "拉取", "切换", "同步", "校验", "检查",
            "体检", "测试", "运行", "maven", "core", "api", "cli", "doctor", "check",
            "recommended", "build", "publish", "install", "start", "dev", "test"
        ];

        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string GitCheckoutBranchScript(bool force) => force
        ? """
          $target = $env:SERVICEPILOT_VARIABLE
          if ([string]::IsNullOrWhiteSpace($target)) { throw '请选择分支变量。' }
          git reset --hard
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git clean -fd
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git fetch --all --prune
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git checkout $target
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git pull --ff-only
          exit $LASTEXITCODE
          """
        : """
          $target = $env:SERVICEPILOT_VARIABLE
          if ([string]::IsNullOrWhiteSpace($target)) { throw '请选择分支变量。' }
          git status --short
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git fetch --all --prune
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git checkout $target
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git pull --ff-only
          exit $LASTEXITCODE
          """;

    private static string GitCheckoutTagScript(bool force) => force
        ? """
          $target = $env:SERVICEPILOT_VARIABLE
          if ([string]::IsNullOrWhiteSpace($target)) { throw '请选择 Tag 变量。' }
          git reset --hard
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git clean -fd
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git fetch --all --tags --prune
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git checkout $target
          exit $LASTEXITCODE
          """
        : """
          $target = $env:SERVICEPILOT_VARIABLE
          if ([string]::IsNullOrWhiteSpace($target)) { throw '请选择 Tag 变量。' }
          git status --short
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git fetch --all --tags --prune
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          git checkout $target
          exit $LASTEXITCODE
          """;

    private static string OpenToolScript(string toolName, IReadOnlyList<string> candidates, bool passDirectory)
    {
        var candidateList = string.Join(", ", candidates.Select(candidate => $"'{candidate.Replace("'", "''")}'"));
        var commandList = string.Join(", ", candidates
            .Select(Path.GetFileName)
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(command => $"'{command!.Replace("'", "''")}'"));
        var argumentsExpression = toolName switch
        {
            "Windows Terminal" => "'-d ' + (Quote-LnkArg $dir)",
            "Git Bash" => "Quote-LnkArg ('--cd=' + $dir)",
            _ => passDirectory ? "Quote-LnkArg $dir" : "''"
        };
        var workingDirectoryExpression = passDirectory ? "$dir" : "''";

        return $$"""
          {{DetachedOpenHeader()}}
          $dir = (Get-Location).Path
          $candidates = @({{candidateList}})
          $commands = @({{commandList}})
          $hit = Find-FirstExisting $candidates
          if (-not $hit) {
              foreach ($name in $commands) {
                  $command = Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1
                  if ($command) {
                      $hit = $command.Source
                      break
                  }
              }
          }
          if (-not $hit) { throw '{{toolName}} was not found.' }
          Invoke-DetachedOpen $hit ({{argumentsExpression}}) {{workingDirectoryExpression}}
          Write-Output ('Opened {{toolName}}: ' + $dir)
          """;
    }

    private static string OpenExplorerScript(string targetExpression) =>
        $$"""
          $ErrorActionPreference = 'Stop'
          $dir = (Get-Location).Path
          $target = {{targetExpression}}
          if (-not (Test-Path -LiteralPath $target)) { throw ('Folder not found: ' + $target) }
          $shell = New-Object -ComObject Shell.Application
          $shell.Open($target)
          Start-Sleep -Seconds 1
          Write-Output ('Opened Explorer: ' + $target)
          """;

    private static string DetachedOpenScript(
        string fileExpression,
        string argumentsExpression,
        string workingDirectoryExpression = "$dir") =>
        $$"""
          {{DetachedOpenHeader()}}
          $dir = (Get-Location).Path
          Invoke-DetachedOpen {{fileExpression}} {{argumentsExpression}} {{workingDirectoryExpression}}
          """;

    private static string DetachedOpenHeader() =>
        """
        $ErrorActionPreference = 'Stop'
        function Quote-LnkArg([string]$value) {
            if ($null -eq $value) { return '' }
            $escaped = $value.Replace('"', '""')
            return '"' + $escaped + '"'
        }
        function Find-FirstExisting([string[]]$paths) {
          foreach ($path in $paths) {
              if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path)) {
                  return $path
              }
          }
          return $null
        }
        function Invoke-DetachedOpen([string]$file, [string]$arguments, [string]$workingDirectory) {
            if ([string]::IsNullOrWhiteSpace($file)) { throw 'Executable not found.' }
            if (-not (Test-Path -LiteralPath $file) -and -not (Get-Command $file -ErrorAction SilentlyContinue)) {
                throw ('Executable not found: ' + $file)
            }
            $shortcutDir = Join-Path $env:TEMP 'ServicePilot\OpenShortcuts'
            [System.IO.Directory]::CreateDirectory($shortcutDir) | Out-Null
            Get-ChildItem -LiteralPath $shortcutDir -Filter '*.lnk' -ErrorAction SilentlyContinue |
                Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-1) } |
                Remove-Item -Force -ErrorAction SilentlyContinue
            $shortcutPath = Join-Path $shortcutDir ([Guid]::NewGuid().ToString('N') + '.lnk')
            $wsh = New-Object -ComObject WScript.Shell
            $shortcut = $wsh.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = $file
            $shortcut.Arguments = $arguments
            if (-not [string]::IsNullOrWhiteSpace($workingDirectory) -and (Test-Path -LiteralPath $workingDirectory)) {
                $shortcut.WorkingDirectory = $workingDirectory
            }
            $shortcut.WindowStyle = 1
            $shortcut.Save()
            & explorer.exe $shortcutPath
            Start-Sleep -Seconds 2
        }
        """;

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
