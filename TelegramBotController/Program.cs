using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using WolfLive.Api;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace TelegramBotController
{
    /// <summary>
    /// الفئة الرئيسية لتشغيل البرنامج
    /// </summary>
    class Program
    {
        private static string TokenFileName = ".bot_token";
        private static string ConfigFileName = "monitor_config.json";
        private const string ErrorLogFileName = "error.log";

        /// <summary>
        /// تسجيل الأخطاء في ملف نصي
        /// </summary>
        public static void LogError(string message, Exception? ex = null)
        {
            try
            {
                string logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                if (ex != null)
                {
                    logContent += $"\nException: {ex.Message}\nStack Trace: {ex.StackTrace}";
                }
                logContent += "\n--------------------------------------------------\n";
                
                File.AppendAllText(ErrorLogFileName, logContent);
            }
            catch
            {
                // تجاهل أخطاء الكتابة في ملف السجل لتجنب الدخول في حلقة مفرغة
            }
        }

        /// <summary>
        /// نقطة الدخول الرئيسية للبرنامج
        /// </summary>
        /// <param name="args">معاملات سطر الأوامر</param>
        static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => 
            {
                LogError("Unhandled Exception", e.ExceptionObject as Exception);
            };

            // معالجة البروفايل (Profile)
            string? profile = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--profile" && i + 1 < args.Length)
                {
                    profile = args[i + 1];
                    TokenFileName = $".bot_token_{profile}";
                    ConfigFileName = $"monitor_config_{profile}.json";
                    break;
                }
            }

            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch
            {
                // Ignored: Some consoles do not support UTF-8 encoding
            }

            Console.WriteLine("========================================");
            Console.WriteLine("    نظام إدارة بوتات WolfLive عبر Telegram");
            if (!string.IsNullOrEmpty(profile))
            {
                Console.WriteLine($"    Profile: {profile}");
            }
            Console.WriteLine("    حقوق النشر محفوظة © 2025");
            Console.WriteLine("========================================");
            
            // Handle command line arguments for non-interactive mode
            if (args.Length > 0)
            {
                if (args[0] == "--test-login")
                {
                    string? email = args.Length > 1 ? args[1] : null;
                    string? password = args.Length > 2 ? args[2] : null;
                    await TestWolfLogin(email, password);
                    return;
                }
            }

            while (true)
            {
                    // 1. الحصول على التوكن
                string? botToken = GetToken();

                if (string.IsNullOrEmpty(botToken))
                {
                    Console.WriteLine("❌ لم يتم إدخال توكن. حاول مرة أخرى.");
                    continue;
                }

                // 2. التحقق من صيغة التوكن
                if (!IsValidTokenFormat(botToken))
                {
                    Console.WriteLine("❌ صيغة التوكن غير صحيحة (يجب أن تكون: 123456:ABC-Def...).");
                    DeleteTokenFile();
                    continue;
                }

                // 3. التحقق من معرف المجموعة (Group ID)
                string? groupId = GetGroupId();
                if (string.IsNullOrEmpty(groupId))
                {
                    Console.WriteLine("❌ يجب تحديد معرف المجموعة (Group ID) للعمل.");
                    continue;
                }
                
                // حفظ المعرف في ملف الإعدادات
                UpdateConfigGroupId(groupId);

                // 4. محاولة الاتصال
                try
                {
                    Console.WriteLine("🔄 جاري التحقق من التوكن...");
                    var botClient = new TelegramBotClient(botToken);
                    var me = await botClient.GetMe(); // اختبار الاتصال
                    Console.WriteLine($"✅ تم التحقق بنجاح! مرحباً {me.FirstName} (@{me.Username})");

                    // حفظ التوكن إذا كان صحيحاً ولم يكن محفوظاً من قبل
                    if (!File.Exists(TokenFileName) || File.ReadAllText(TokenFileName) != botToken)
                    {
                        SaveToken(botToken);
                    }

                    // تشغيل النظام
                    Console.WriteLine("\n✅ جاري تشغيل بوت التليجرام...");
                    using var controller = new TelegramController(botToken);
                    await controller.StartAsync();
                    
                    break; // الخروج من الحلقة عند الانتهاء الطبيعي
                }
                catch (ApiRequestException ex)
                {
                    Console.WriteLine($"❌ التوكن غير صالح: {ex.Message}");
                    Console.WriteLine("⚠️ سيتم حذف التوكن المحفوظ لطلب إدخال جديد.");
                    DeleteTokenFile();
                }
                catch (System.Net.Http.HttpRequestException ex)
                {
                    Console.WriteLine($"\n⚠️ خطأ في الاتصال بالإنترنت: {ex.Message}");
                    Console.WriteLine("⏳ سيتم إعادة المحاولة تلقائياً خلال 10 ثوانٍ...");
                    await Task.Delay(10000); // الانتظار 10 ثوانٍ قبل إعادة المحاولة
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ حدث خطأ غير متوقع: {ex.Message}");
                    LogError("Critical Error in Main Loop", ex);
                    Console.WriteLine("سيتم إعادة التشغيل تلقائياً خلال 5 ثوانٍ...");
                    await Task.Delay(5000);
                }
            }
        }

        static string? GetToken()
        {
            // محاولة تحميل التوكن من الملف
            string? token = LoadToken();
            if (!string.IsNullOrEmpty(token)) return token;

            // محاولة من متغيرات البيئة
            token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            if (!string.IsNullOrEmpty(token)) return token;

            // الطلب من المستخدم
            Console.WriteLine("\n⚠️ لم يتم العثور على توكن محفوظ.");
            Console.Write("الرجاء إدخال توكن بوت التليجرام: ");
            token = Console.ReadLine()?.Trim();
            
            if (!string.IsNullOrEmpty(token) && IsValidTokenFormat(token))
            {
                SaveToken(token);
            }
            
            return token;
        }

        static bool IsValidTokenFormat(string token)
        {
            // Simple regex for Telegram Bot Token: digits:characters
            return Regex.IsMatch(token, @"^\d+:[a-zA-Z0-9_-]+$");
        }

        static string? GetGroupId()
        {
            // محاولة التحميل من الملف
            if (File.Exists(ConfigFileName))
            {
                try
                {
                    var json = File.ReadAllText(ConfigFileName);
                    var config = JsonConvert.DeserializeObject<MonitorConfigData>(json);
                    if (config != null && !string.IsNullOrEmpty(config.TargetGroupId))
                    {
                        return config.TargetGroupId;
                    }
                }
                catch { }
            }

            // إذا لم يوجد، اطلب من المستخدم
            Console.WriteLine("\n⚠️ لم يتم العثور على معرف مجموعة (Group ID) في الإعدادات.");
            Console.Write("الرجاء إدخال معرف المجموعة (مثال: 18822804): ");
            return Console.ReadLine()?.Trim();
        }

        static void UpdateConfigGroupId(string groupId)
        {
            MonitorConfigData config = new MonitorConfigData();

            if (File.Exists(ConfigFileName))
            {
                try
                {
                    var json = File.ReadAllText(ConfigFileName);
                    config = JsonConvert.DeserializeObject<MonitorConfigData>(json) ?? new MonitorConfigData();
                }
                catch { }
            }

            if (config.Phrases == null)
            {
                 config.Phrases = new List<PhraseConfig>
                 {
                     new PhraseConfig { Name = "صياد", Command = "!صياد 3" },
                     new PhraseConfig { Name = "صيد", Command = "!صيد ٣" },
                     new PhraseConfig { Name = "اسرق", Command = "!اسرق 5" },
                     new PhraseConfig { Name = "بطل", Command = "!بطل 5" },
                     new PhraseConfig { Name = "سباق_طاقة", Command = "!س طاقه" },
                     new PhraseConfig { Name = "سباق_جلد", Command = "!س جلد" }
                 };
            }

            if (config.TargetGroupId != groupId)
            {
                config.TargetGroupId = groupId;
                File.WriteAllText(ConfigFileName, JsonConvert.SerializeObject(config, Formatting.Indented));
                Console.WriteLine($"✅ تم حفظ معرف المجموعة: {groupId}");
            }
        }

        static async Task TestWolfLogin(string? email = null, string? password = null)
        {
            Console.WriteLine("\n=== اختبار تسجيل دخول ولف ===");
            
            if (string.IsNullOrEmpty(email))
            {
                Console.Write("البريد الإلكتروني: ");
                email = Console.ReadLine();
            }
            
            if (string.IsNullOrEmpty(password))
            {
                Console.Write("كلمة المرور: ");
                password = Console.ReadLine(); // Note: Plain text for simplicity in console
            }

            Console.WriteLine("جاري الاتصال...");
            try 
            {
                var client = new WolfClient();
                var success = await client.Login(email, password);
                
                if (success)
                {
                    Console.WriteLine("\n✅✅ تم تسجيل الدخول بنجاح! بيانات الاعتماد صحيحة.");
                }
                else
                {
                    Console.WriteLine("\n❌❌ فشل تسجيل الدخول. تأكد من صحة البيانات.");
                }
                
                await client.Connection.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ حدث خطأ أثناء الاختبار: {ex.Message}");
            }
            
            Console.WriteLine("\nاضغط أي مفتاح للعودة للقائمة...");
            Console.ReadKey();
        }

        static string? LoadToken()
        {
            try
            {
                if (File.Exists(TokenFileName))
                {
                    var token = File.ReadAllText(TokenFileName).Trim();
                    if (!string.IsNullOrEmpty(token))
                    {
                        Console.WriteLine("✅ تم تحميل التوكن المحفوظ.");
                        return token;
                    }
                }
            }
            catch { }
            return null;
        }

        static void SaveToken(string token)
        {
            try
            {
                File.WriteAllText(TokenFileName, token);
                Console.WriteLine("✅ تم حفظ التوكن للاستخدام المستقبلي.");
                
                try 
                {
                    File.SetAttributes(TokenFileName, File.GetAttributes(TokenFileName) | FileAttributes.Hidden);
                }
                catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ تحذير: فشل حفظ التوكن ({ex.Message})");
            }
        }

        static void DeleteTokenFile()
        {
            try
            {
                if (File.Exists(TokenFileName))
                {
                    File.SetAttributes(TokenFileName, FileAttributes.Normal); // Remove hidden attribute to delete
                    File.Delete(TokenFileName);
                    Console.WriteLine("🗑️ تم حذف ملف التوكن القديم.");
                }
            }
            catch { }
        }
    }
}
