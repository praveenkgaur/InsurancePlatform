using Insurance.Helpers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInsuranceData(builder.Configuration);
builder.Services.AddInsuranceJwt(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddInsuranceSwagger("Policies API");

var app = builder.Build();
app.UseInsuranceSwagger();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapInsuranceHealth("policies");
app.Run();
