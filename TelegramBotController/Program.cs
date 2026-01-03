using System;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TelegramBotController;
using TelegramBotController.Services;

namespace TelegramBotController
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 1. قراءة التوكن لتحديد هوية البوت
            string token = "";
            if (File.Exists(".bot_token")) token = File.ReadAllText(".bot_token").Trim();
            else if (File.Exists("../.bot_token")) token = File.ReadAllText("../.bot_token").Trim();
            else if (File.Exists("TelegramBotController/.bot_token")) token = File.ReadAllText("TelegramBotController/.bot_token").Trim();

            // 2. إنشاء معرف فريد بناءً على التوكن (أو عام إذا لم يوجد)
            string mutexId = "Global\\WolfLiveBotController_NoToken";
            int port = 5000;

            if (!string.IsNullOrEmpty(token))
            {
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(token));
                    var hashString = BitConverter.ToString(hash).Replace("-", "");
                    mutexId = $"Global\\WolfLiveBotController_{hashString}";
                    
                    // توليد منفذ (Port) فريد بناءً على التوكن لتجنب تعارض البورتات عند تشغيل أكثر من بوت
                    // نستخدم آخر 4 أرقام من الهاش لضمان التوزيع
                    port = 5000 + (Math.Abs(BitConverter.ToInt16(hash, 0)) % 1000);
                }
            }

            // 3. منع تشغيل نفس البوت (نفس التوكن) أكثر من مرة
            using var mutex = new Mutex(true, mutexId, out bool createdNew);
            if (!createdNew)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n==================================================================");
                Console.WriteLine("⚠️  هذا البوت يعمل بالفعل! لا يمكن تشغيل نفس البوت مرتين.");
                Console.WriteLine("==================================================================\n");
                Console.ResetColor();
                Console.WriteLine("اضغط على أي زر للخروج...");
                try { Console.ReadKey(); } catch { }
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (sender, e) => 
            {
                Logger.LogError("Unhandled Exception", e.ExceptionObject as Exception);
            };

            var builder = WebApplication.CreateBuilder(args);
            
            // تعيين البورت الديناميكي
            builder.WebHost.UseUrls($"http://localhost:{port}");

            // Add services to the container.
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Register Bot Services
            builder.Services.AddSingleton<BotManager>();
            builder.Services.AddSingleton<AccountService>();
            
            // Register Telegram Controller
            builder.Services.AddSingleton<TelegramController>();

            // Register Background Service for Telegram (to keep it running if needed)
            builder.Services.AddHostedService<TelegramBackgroundService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // Enable Swagger even in production for user convenience
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "WolfBot API v1"));

            app.UseAuthorization();

            app.MapControllers();

            Console.WriteLine("========================================");
            Console.WriteLine("    نظام إدارة بوتات WolfLive - API Enabled");
            Console.WriteLine($"    Web API running on http://localhost:{port}");
            Console.WriteLine($"    Swagger UI: http://localhost:{port}/swagger");
            Console.WriteLine("========================================");

            await app.RunAsync();
        }
    }
}
