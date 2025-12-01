using Microsoft.EntityFrameworkCore;
using ConsumerApp.Data;
using ConsumerApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHostedService<KafkaConsumerService>(); 
builder.Services.AddSingleton<FeedbackProducerService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        context.Database.Migrate();
        app.Logger.LogInformation("✅ Database migration muvaffaqiyatli bajarildi");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "❌ Database migration xatolik");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Approvals}/{action=Index}/{id?}");

app.Run();