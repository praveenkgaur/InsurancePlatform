using Claims.Api.Services;
using Insurance.Helpers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInsuranceData(builder.Configuration);
builder.Services.AddInsuranceJwt(builder.Configuration);
builder.Services.AddScoped<ClaimsService>();
builder.Services.AddControllers();
builder.Services.AddInsuranceSwagger("Claims API");

var app = builder.Build();
app.UseInsuranceSwagger();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapInsuranceHealth("claims");
app.Run();
