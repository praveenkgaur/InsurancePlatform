using Claims.Api.Services;
using Insurance.Helpers;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
    builder.WebHost.UseUrls("http://localhost:5283");
builder.Services.AddInsuranceData(builder.Configuration);
builder.Services.AddInsuranceJwt(builder.Configuration);
//builder.Services.AddHttpClient<ClaimantCreatedClient>(client => client.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddHttpClient<ClaimantCreatedClient>(client => client.Timeout = TimeSpan.FromSeconds(10)).AddInsuranceHttpResilience(builder.Configuration, TimeSpan.FromSeconds(10));
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
