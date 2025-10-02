
using BurganAzureDevopsAggregator.Actions;
using BurganAzureDevopsAggregator.Business;
using BurganAzureDevopsAggregator.Controllers;
using BurganAzureDevopsAggregator.Database;
using BurganAzureDevopsAggregator.Helpers;
using Microsoft.EntityFrameworkCore;
using Elastic.Apm.NetCoreAll;

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
IConfigurationRoot configuration = new ConfigurationBuilder()
    .AddJsonFile($"appsettings.json", true, true)
    .AddJsonFile($"appsettings.{environment}.json", true, true)
    .AddEnvironmentVariables()
    .Build();
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS for React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "https://localhost:3000", "http://localhost:3001", "https://localhost:3001")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("constring"));
});
builder.Services.AddScoped<RulesService>();
builder.Services.AddScoped<XmlRuleProcessor>();
builder.Services.AddScoped<RulesEngineService>();
builder.Services.AddScoped<FieldProcessingService>();
builder.Services.AddScoped<XmlRuleImportService>();
builder.Services.AddScoped<XmlToJsonConverter>();
builder.Services.AddScoped<RulesHelper>();
builder.Services.AddScoped<RuleExecutionJsonLogger>();
builder.Services.AddHttpClient<AzureDevOpsClient>();
builder.Services.AddScoped<ActionExecutor>();
builder.Services.AddScoped<IActionHandler, ChangeStateActionHandler>();
builder.Services.AddScoped<IActionHandler, AddCommentActionHandler>();
builder.Services.AddScoped<IActionHandler, ExecuteXmlCalculationActionHandler>();
builder.Services.AddScoped<IActionHandler, SetFieldActionHandler>();
builder.Services.AddScoped<IActionHandler, UpdateFieldActionHandler>();
builder.Services.AddScoped<IActionHandler, TransitionToStateActionHandler>();
builder.Services.AddScoped<IActionHandler, RiskCalculationActionHandler>();
builder.Services.AddDbContext<ApplicationDbContext>();

 builder.Services.AddScoped<ActionMethods>();

// Add Elastic APM - configuration will be read from appsettings.json
builder.Services.AddAllElasticApm();

var app = builder.Build();

    app.UseSwagger();
    app.UseSwaggerUI();

// Enable CORS
app.UseCors("ReactApp");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
