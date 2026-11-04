using System;
using System.IO;
using HandlebarsDotNet;
using VickysMicroArc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;


public class CodeGenerator
{
    private readonly string _outputDirectory;
    private readonly string _namespace;
    private readonly string _solutionName;
    private readonly string _connectionString;

    public CodeGenerator(string outputDirectory, string @namespace, string solutionName, string connectionString)
    {
        _outputDirectory = outputDirectory;
        _namespace = @namespace;
        _solutionName = solutionName;
        _connectionString = connectionString;

        // Create output directories
        Directory.CreateDirectory(Path.Combine(_outputDirectory, "Models"));
        Directory.CreateDirectory(Path.Combine(_outputDirectory, "Repositories"));
        Directory.CreateDirectory(Path.Combine(_outputDirectory, "Controllers"));
    }

    public void GenerateModel(string tableName, IEnumerable<Column> columns)
    {
        var templatePath = "Templates/ModelTemplate.hbs";
        var outputPath = Path.Combine(_outputDirectory, "Models", $"{tableName}.cs");

        var hasDateTime = columns.Any(col => MapSqlTypeToDotNetType(col.DATA_TYPE) == "DateTime");

        var data = new
        {
            Namespace = _namespace,
            TableName = tableName,
            HasDateTime = hasDateTime,
            Columns = columns.Select(col => new
            {
                COLUMN_NAME = col.COLUMN_NAME == tableName ? $"{col.COLUMN_NAME}_" : col.COLUMN_NAME,
                DATA_TYPE = MapSqlTypeToDotNetType(col.DATA_TYPE)
            })
        };

        RenderTemplate(templatePath, outputPath, data);
    }



    private List<string> _repositories = new List<string>();

    public void GenerateRepository(string tableName, IEnumerable<Column> columns)
    {
        var templatePath = "Templates/RepositoryTemplate.hbs";
        var outputPath = Path.Combine(_outputDirectory, "Repositories", $"{tableName}Repository.cs");

        var primaryKey = GetPrimaryKey(tableName);

        var data = new
        {
            Namespace = _namespace,
            TableName = tableName,
            PrimaryKey = primaryKey ?? "Id", // Default to "Id" if no primary key is found
            Columns = columns.Select(col => new
            {
                COLUMN_NAME = col.COLUMN_NAME,
                DATA_TYPE = MapSqlTypeToDotNetType(col.DATA_TYPE)
            })
        };

        RenderTemplate(templatePath, outputPath, data);

        // Track generated repository
        _repositories.Add(tableName);
    }

    public void GenerateController(string tableName)
    {
        var templatePath = "Templates/ControllerTemplate.hbs";
        var outputPath = Path.Combine(_outputDirectory, "Controllers", $"{tableName}Controller.cs");

        var primaryKey = GetPrimaryKey(tableName);

        var data = new
        {
            Namespace = _namespace,
            TableName = tableName,
            PrimaryKey = primaryKey ?? "Id" // Default to "Id" if no primary key is found
        };

        RenderTemplate(templatePath, outputPath, data);
    }

    public void GenerateProgramFile(string layerName, List<string> repositories)
    {
        var repositoryRegistrations = string.Join(Environment.NewLine, repositories.Select(repo =>
            $@"builder.Services.AddScoped<I{repo}Repository, {repo}Repository>();"));

        var programContent = $@"
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;
using {_namespace}.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Read configuration from appsettings.json
builder.Configuration.AddJsonFile(""appsettings.json"", optional: false, reloadOnChange: true);

// Configure Serilog
builder.Host.UseSerilog((context, config) =>
{{
    config.ReadFrom.Configuration(context.Configuration);
}});

// Add services to the container
builder.Services.AddControllers();

// Register the connection string
builder.Services.AddSingleton(sp => builder.Configuration.GetConnectionString(""DefaultConnection""));

// Register repositories
{repositoryRegistrations}

// Configure JWT authentication
var key = Encoding.ASCII.GetBytes(builder.Configuration[""JwtConfig:Secret""]);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {{
        options.TokenValidationParameters = new TokenValidationParameters
        {{
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration[""JwtConfig:Issuer""],
            ValidAudience = builder.Configuration[""JwtConfig:Audience""],
            IssuerSigningKey = new SymmetricSecurityKey(key)
        }};
    }});

// Add Swagger documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{{
    options.SwaggerDoc(""v1"", new OpenApiInfo
    {{
        Title = ""Generated Microservices API"",
        Version = ""v1""
    }});
    options.AddSecurityDefinition(""Bearer"", new OpenApiSecurityScheme
    {{
        Description = ""JWT Authorization header using the Bearer scheme. Example: 'Authorization: Bearer {{token}}'"",
        Name = ""Authorization"",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = ""Bearer""
    }});
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {{
        {{
            new OpenApiSecurityScheme
            {{
                Reference = new OpenApiReference
                {{
                    Type = ReferenceType.SecurityScheme,
                    Id = ""Bearer""
                }}
            }},
            new string[] {{}}
        }}
    }});
}});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint(""/swagger/v1/swagger.json"", ""API v1""));
}}

app.UseRouting();

// Enable authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
";

        var outputPath = Path.Combine(_outputDirectory, layerName, "Program.cs");
        File.WriteAllText(outputPath, programContent);
        Console.WriteLine($"Generated Program.cs for {layerName}.");
    }


    public void GenerateProjectFiles()
    {
        CreateProjectFile("Models", "net6.0");
        CreateProjectFile("Repositories", "net6.0");
        CreateProjectFile("Controllers", "net6.0", isWebApi: true);

        GenerateAppSettingsFile("Controllers");
        GenerateProgramFile("Controllers", _repositories);
        GenerateAuthController("Controllers");

        CreateSolutionFile();
    }

    private void CreateProjectFile(string layerName, string framework, bool isWebApi = false)
    {
        var projectPath = Path.Combine(_outputDirectory, layerName, $"{layerName}.csproj");
        Directory.CreateDirectory(Path.Combine(_outputDirectory, layerName));

        var references = new List<string>();

        if (layerName == "Repositories")
        {
            references.Add("<ProjectReference Include=\"..\\Models\\Models.csproj\" />");
            references.Add("<PackageReference Include=\"Dapper\" Version=\"2.0.123\" />");
            references.Add("<PackageReference Include=\"Microsoft.Data.SqlClient\" Version=\"5.0.0\" />");
        }
        else if (layerName == "Controllers")
        {
            references.Add("<ProjectReference Include=\"..\\Repositories\\Repositories.csproj\" />");
            references.Add("<ProjectReference Include=\"..\\Models\\Models.csproj\" />");
            references.Add("<PackageReference Include=\"Microsoft.AspNetCore.Mvc\" Version=\"2.2.0\" />");
            references.Add("<PackageReference Include=\"Serilog.AspNetCore\" Version=\"6.0.0\" />");
            references.Add("<PackageReference Include=\"Swashbuckle.AspNetCore\" Version=\"6.5.0\" />");
            references.Add("<PackageReference Include=\"Microsoft.AspNetCore.Authentication.JwtBearer\" Version=\"6.0.16\" />"); // Appropriate version for .NET 6.0
        }
        else if (layerName == "Models")
        {
            references.Add("<PackageReference Include=\"System.Runtime\" Version=\"4.3.1\" />");
        }

        var referenceBlock = string.Join(Environment.NewLine, references);

        var projectContent = $@"
<Project Sdk=""Microsoft.NET.Sdk{(isWebApi ? ".Web" : "")}"">
  <PropertyGroup>
    <OutputType>{(isWebApi ? "Exe" : "Library")}</OutputType>
    <TargetFramework>{framework}</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    {referenceBlock}
  </ItemGroup>
</Project>";

        File.WriteAllText(projectPath, projectContent);
        Console.WriteLine($"Generated project file: {projectPath}");
    }

    private void CreateSolutionFile()
    {
        var solutionPath = Path.Combine(_outputDirectory, $"{_solutionName}.sln");

        ExecuteCommand($"dotnet new sln -n {_solutionName}", _outputDirectory);

        AddProjectToSolution("Models");
        AddProjectToSolution("Repositories");
        AddProjectToSolution("Controllers");
        // Set Controllers project as default startup
        SetStartupProject("Controllers");
        Console.WriteLine($"Generated solution file: {solutionPath}");
    }

    private void SetStartupProject(string projectName)
    {
        var startupProjectPath = Path.Combine(_outputDirectory, projectName, $"{projectName}.csproj");

        // Update the .sln file to set the Controllers project as the default startup project
        var solutionFilePath = Path.Combine(_outputDirectory, $"{_solutionName}.sln");
        var solutionContent = File.ReadAllText(solutionFilePath);

        // Add a comment to highlight the default startup project
        solutionContent += $"\n# Default startup project: {projectName}\n";

        File.WriteAllText(solutionFilePath, solutionContent);
        Console.WriteLine($"Set {projectName} as the default startup project.");
    }

    private void AddProjectToSolution(string layerName)
    {
        var projectPath = Path.Combine(_outputDirectory, layerName, $"{layerName}.csproj");
        ExecuteCommand($"dotnet sln add {projectPath}", _outputDirectory);
    }

    private void ExecuteCommand(string command, string workingDirectory)
    {
        Console.WriteLine($"Executing command: {command}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Console.WriteLine($"Command failed: {command}");
            Console.WriteLine($"Error: {error}");
            throw new InvalidOperationException($"Command failed: {command}\nError: {error}");
        }

        Console.WriteLine(output);
    }

    private string GetPrimaryKey(string tableName)
    {
        var schemaReader = new DatabaseSchemaReader(_connectionString);
        return schemaReader.GetPrimaryKey(tableName);
    }

    private string MapSqlTypeToDotNetType(string sqlType)
    {
        return sqlType switch
        {
            "int" => "int",
            "bigint" => "long",
            "smallint" => "short",
            "bit" => "bool",
            "decimal" => "decimal",
            "numeric" => "decimal",
            "float" => "double",
            "real" => "float",
            "char" => "string",
            "varchar" => "string",
            "nvarchar" => "string",
            "text" => "string",
            "ntext" => "string",
            "date" => "DateTime",
            "datetime" => "DateTime",
            "datetime2" => "DateTime",
            "time" => "TimeSpan",
            _ => "object"
        };
    }

    private void RenderTemplate(string templatePath, string outputPath, object data)
    {
        var templateContent = File.ReadAllText(templatePath);
        var template = HandlebarsDotNet.Handlebars.Compile(templateContent);
        var result = template(data);
        File.WriteAllText(outputPath, result);
        Console.WriteLine($"Generated: {outputPath}");
    }


    private void GenerateAppSettingsFile(string layerName)
    {
        var appSettingsContent = $@"
{{
  ""ConnectionStrings"": {{
    ""DefaultConnection"": ""Server=localhost;Database=YourDatabaseName;Trusted_Connection=True;""
  }},
  ""Logging"": {{
    ""LogLevel"": {{
      ""Default"": ""Information"",
      ""Microsoft"": ""Warning"",
      ""Microsoft.Hosting.Lifetime"": ""Information""
    }}
  }},
  ""JwtConfig"": {{
    ""Secret"": ""YourStrongJwtSecretKeyHere"",
    ""Issuer"": ""GeneratedMicroservices"",
    ""Audience"": ""GeneratedMicroservicesUsers""
  }},
  ""Serilog"": {{
    ""Using"": [ ""Serilog.Sinks.Console"", ""Serilog.Sinks.File"" ],
    ""MinimumLevel"": {{
      ""Default"": ""Information"",
      ""Override"": {{
        ""Microsoft"": ""Warning"",
        ""System"": ""Warning""
      }}
    }},
    ""WriteTo"": [
      {{ ""Name"": ""Console"" }},
      {{ ""Name"": ""File"", ""Args"": {{ ""path"": ""Logs/log-.txt"", ""rollingInterval"": ""Day"" }} }}
    ],
    ""Enrich"": [ ""FromLogContext"", ""WithMachineName"" ],
    ""Properties"": {{
      ""Application"": ""GeneratedMicroservicesApp""
    }}
  }},
  ""AllowedHosts"": ""*""
}}
";

        var outputPath = Path.Combine(_outputDirectory, layerName, "appsettings.json");
        File.WriteAllText(outputPath, appSettingsContent);
        Console.WriteLine($"Generated appsettings.json for {layerName}.");
    }

    public void GenerateAuthController(string layerName)
    {
        var authControllerContent = $@"
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System;

namespace {_namespace}.Controllers
{{
    [Route(""api/[controller]"")]
    [ApiController]
    public class AuthController : ControllerBase
    {{
        private readonly IConfiguration _configuration;

        public AuthController(IConfiguration configuration)
        {{
            _configuration = configuration;
        }}

        [HttpPost(""login"")]
        public IActionResult Login([FromBody] LoginRequest request)
        {{
            // Validate username and password (user-defined)
            if (ValidateUser(request.UserName, request.Password)) // Replace with your logic
            {{
                var token = GenerateJwtToken(request.UserName);
                return Ok(new {{ Token = token }});
            }}
            return Unauthorized(""Invalid credentials"");
        }}

        private bool ValidateUser(string username, string password)
        {{
            // User-defined validation logic (this is a placeholder)
            return username == ""admin"" && password == ""password"";
        }}

        private string GenerateJwtToken(string username)
        {{
            var claims = new[]
            {{
                new Claim(JwtRegisteredClaimNames.Sub, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            }};

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration[""JwtConfig:Secret""]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration[""JwtConfig:Issuer""],
                audience: _configuration[""JwtConfig:Audience""],
                claims: claims,
                expires: DateTime.Now.AddMinutes(30),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }}
    }}

    public class LoginRequest
    {{
        public string UserName {{ get; set; }}
        public string Password {{ get; set; }}
    }}
}}
";

        var outputPath = Path.Combine(_outputDirectory, layerName, "AuthController.cs");
        File.WriteAllText(outputPath, authControllerContent);
        Console.WriteLine($"Generated AuthController.cs for {layerName}.");
    }


}
