using System;
using System.Threading.Tasks;
using WolfLive.Api;
using WolfLive.Api.Models;
using System.Collections.Generic;
using System.Linq;

namespace TelegramBotController.Services
{
    public class HorseActivator
    {
        private WolfClient _client;
        private TaskCompletionSource<bool> _tcs;
        private string _currentStep;
        private readonly int _targetUserId = 80277459;
        private readonly string _targetGroupId = "18822804";

        public event Action<string> OnLog;

        public async Task<string> ActivateHorseAsync(string email, string password)
        {
            _client = new WolfClient();
            string log = "";
            bool success = true;

            void Log(string msg) 
            {
                log += msg + "\n";
                OnLog?.Invoke(msg);
            }

            try 
            {
                Log($"⏳ جاري الدخول للحساب: {email}...");
                var loginSuccess = await _client.Login(email, password);
                if (!loginSuccess) 
                {
                    Log($"❌ فشل تسجيل الدخول.");
                    return log;
                }

                Log($"✅ تم تسجيل الدخول بنجاح.");
                
                _client.Messaging.OnPrivateMessage += HandlePrivateMessage;
                
                // Step 1: Send !س انشاء
                Log("1️⃣ إرسال: !س انشاء");
                _currentStep = "init";
                _tcs = new TaskCompletionSource<bool>();
                await _client.PrivateMessage(_targetUserId.ToString(), "!س انشاء");
                
                if (await Task.WhenAny(_tcs.Task, Task.Delay(15000)) != _tcs.Task) 
                {
                    Log("❌ انتهى الوقت بانتظار الرد على !س انشاء (قد يكون الحساب مفعل مسبقاً أو البوت لا يستجيب)");
                    success = false;
                }
                
                if (success)
                {
                    // Step 2: Send ب23
                    Log("2️⃣ إرسال: ب23");
                    _currentStep = "b23";
                    _tcs = new TaskCompletionSource<bool>();
                    await _client.PrivateMessage(_targetUserId.ToString(), "ب23");
                    
                    if (await Task.WhenAny(_tcs.Task, Task.Delay(15000)) != _tcs.Task)
                    {
                        Log("❌ انتهى الوقت بانتظار الرد على ب23");
                        success = false;
                    }
                }

                if (success)
                {
                    // Step 3: Send F-35
                    Log("3️⃣ إرسال: F-35");
                    _currentStep = "name";
                    _tcs = new TaskCompletionSource<bool>();
                    await _client.PrivateMessage(_targetUserId.ToString(), "F-35");

                    if (await Task.WhenAny(_tcs.Task, Task.Delay(15000)) != _tcs.Task)
                    {
                        Log("❌ انتهى الوقت بانتظار الرد على F-35");
                        success = false;
                    }
                }
                
                if (success)
                {
                    // Step 4: Send ا
                    Log("4️⃣ إرسال: ا (تأكيد)");
                    _currentStep = "confirm";
                    _tcs = new TaskCompletionSource<bool>();
                    await _client.PrivateMessage(_targetUserId.ToString(), "ا");
                    
                    if (await Task.WhenAny(_tcs.Task, Task.Delay(15000)) != _tcs.Task)
                    {
                        Log("❌ انتهى الوقت بانتظار الرد النهائي");
                        success = false;
                    }
                }
                
                if (success)
                {
                    Log("✅ تم تفعيل الحصان بنجاح!");
                    
                    // Step 5: Join group and send !س ع
                    Log($"5️⃣ الانضمام للمجموعة {_targetGroupId} وإرسال !س ع...");
                    try 
                    {
                        await _client.JoinGroup(_targetGroupId);
                        await Task.Delay(2000); // Wait for join
                        await _client.GroupMessage(_targetGroupId, "!س ع");
                        Log("✅ تم الإرسال للمجموعة.");
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ تحذير: فشل التعامل مع المجموعة: {ex.Message}");
                    }
                }

            }
            catch (Exception ex)
            {
                Log($"❌ حدث خطأ غير متوقع: {ex.Message}");
            }
            finally
            {
                _client.Messaging.OnPrivateMessage -= HandlePrivateMessage;
                await _client.Connection.DisconnectAsync();
                Log("👋 تم تسجيل الخروج.");
            }
            
            return log;
        }

        private void HandlePrivateMessage(IWolfClient client, Message msg, User user)
        {
            if (msg.UserId != _targetUserId.ToString()) return;
            
            // Log response for debugging (optional)
            // OnLog?.Invoke($"📩 رد من البوت: {msg.Content}");

            if (_currentStep == "init" && (msg.Content.Contains("في الباقة المرسلة") || msg.Content.Contains("اختيارك النهائي"))) 
                _tcs.TrySetResult(true);
            else if (_currentStep == "b23" && msg.Content.Contains("تفاصيل حيوانك")) 
                _tcs.TrySetResult(true);
            else if (_currentStep == "name" && msg.Content.Contains("الاسم الذي اخترته")) 
                _tcs.TrySetResult(true);
            else if (_currentStep == "confirm" && msg.Content.Contains("(Y)")) 
                _tcs.TrySetResult(true);
        }
    }
}
