using FoMed.Application.DTO.Auth;
using FoMed.Application.Services.Auth;
using FoMed.Infrastructure.Authentication;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Repositories;
using FoMed.Infrastructure.UnitOfWork;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
FoMed.Api.Middleware.CloudHostingConfiguration.ValidateRenderDatabase(builder.Configuration);
var corsOrigins = FoMed.Api.Middleware.CloudHostingConfiguration.CorsOrigins(builder.Configuration, builder.Environment.IsProduction());
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
{
    if (corsOrigins.Length > 0) policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
}));

// 1. Khởi tạo các service dùng cho API: controller, Swagger, EF Core, JWT.
// 2. Các dependency của app được đăng ký vào DI container để service/controller dùng.

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<FoMed.Application.Services.Clinical.IClinicalAuditContext, FoMed.Api.Middleware.ClinicalAuditContext>();
builder.Services.AddExceptionHandler<FoMed.Api.Middleware.ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<ClinicRepository>();
builder.Services.AddScoped<FoMed.Application.Services.Clinical.ClinicAccess>();
builder.Services.AddScoped<FoMed.Application.Services.Clinical.ClinicalService>();
builder.Services.AddScoped<FoMed.Application.Services.Clinical.IClinicalAttachmentStore, FoMed.Api.Middleware.PrivateClinicalAttachmentStore>();
builder.Services.AddScoped<FoMed.Application.Services.Clinical.ClinicalAttachmentService>();
builder.Services.AddScoped<FoMed.Application.Services.Billing.BillingService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<FoMed.Infrastructure.Payments.SePayOptions>()
    .BindConfiguration(FoMed.Infrastructure.Payments.SePayOptions.SectionName)
    .Validate(options => options.IsValid(), "SePay configuration is invalid; bank/account, HMAC secret and environment must be configured.")
    .Validate(options => options.CanUseDatabase(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(builder.Configuration.GetConnectionString("DefaultConnection")).InitialCatalog),
        "SePay requires an application database. Test mode requires an explicitly acknowledged demo database matching SePay:TestDatabaseName; Live must not use that demo database.")
    .ValidateOnStart();
builder.Services.AddScoped<FoMed.Application.Services.Billing.SePayService>();
builder.Services.AddScoped<FoMed.Application.Services.Billing.SePayWebhookAuthenticator>();
builder.Services.AddScoped<FoMed.Application.Services.Pharmacy.PharmacyService>();
builder.Services.AddScoped<FoMed.Application.Services.Doctor.DoctorAdminService>();
builder.Services.AddScoped<FoMed.Application.Services.Doctor.DoctorTimeOffService>();
builder.Services.AddScoped<FoMed.Application.Services.Billing.ServiceCatalogAdminService>();
builder.Services.AddScoped<AdminUserService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<FoMed.Application.Services.Billing.ReportService>();
builder.Services.AddEndpointsApiExplorer();

// Kết nối database SQL Server theo chuỗi ConnectionStrings:DefaultConnection.
builder.Services.AddDbContext<FoMedDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Cấu hình JWT và validate key khi app khởi động.
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Key) && options.Key.Length >= 32,
        "JWT key must contain at least 32 characters.")
    .ValidateOnStart();

// Repository và UnitOfWork: tầng dữ liệu.
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPatientRepository, PatientRepository>();
builder.Services.AddScoped<IDoctorRepository, DoctorRepository>();
builder.Services.AddScoped<ISpecialtyRepository, SpecialtyRepository>();
builder.Services.AddScoped<IDoctorScheduleRepository, DoctorScheduleRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<IServiceRepository, ServiceRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Các service nghiệp vụ.
builder.Services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FoMed.Application.Services.Appointment.AppointmentService>();
builder.Services.AddScoped<FoMed.Application.Services.Profile.ProfileService>();
builder.Services.AddScoped<FoMed.Application.Services.Patient.PatientService>();
builder.Services.AddScoped<FoMed.Application.Services.Patient.PatientStaffService>();
builder.Services.AddScoped<FoMed.Application.Services.Doctor.DoctorService>();
builder.Services.AddScoped<FoMed.Application.Services.Doctor.DoctorScheduleService>();
builder.Services.AddScoped<FoMed.Application.Services.Doctor.AdminDoctorScheduleService>();

// Cấu hình xác thực JWT cho API.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration is missing.");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
        };
    });

// Cho phép dùng [Authorize] và phân quyền theo role nếu cần.
builder.Services.AddAuthorization();

// Swagger để test API dễ dàng trong môi trường phát triển.
// Cấu hình JWT Bearer để Swagger hiển thị nút Authorize và cho phép nhập token.
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "FoMed API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập token theo dạng: Bearer {token}"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

var app = builder.Build();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // Swagger chỉ bật ở môi trường development.
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "FoMed API v1");
        options.RoutePrefix = "swagger";
    });
}

// Middleware xác thực và phân quyền phải đứng trước controller.
// Render terminates TLS and redirects HTTP at its edge. Never trust arbitrary forwarded headers.
if (!FoMed.Api.Middleware.CloudHostingConfiguration.IsRender(builder.Configuration)) app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Liveness only: Render health polling must not keep Azure SQL serverless awake.
app.MapGet("/health", () => Results.Json(new { status = "ok" })).AllowAnonymous();

app.Run();
