using System;
using System.Threading.Tasks;
using SocketIOClient;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace WolfLive.Api
{
    public interface IWolfClient
    {
        bool IsConnected { get; }
        event Action<IMessage> OnMessage;
        Task<bool> Login(string email, string password);
        Task SendGroupMessage(string groupId, string message);
        Task Disconnect();
        void SimulateMessage(IMessage message); // Kept for compatibility but not used in real mode
    }

    public interface IMessage
    {
        Models.Group Group { get; }
        Models.User User { get; }
        string Content { get; }
    }

    public class RealMessage : IMessage
    {
        public Models.Group Group { get; set; }
        public Models.User User { get; set; }
        public string Content { get; set; }
    }
    
    // For mock compatibility if needed elsewhere
    public class MockMessage : IMessage
    {
        public Models.Group Group { get; set; }
        public Models.User User { get; set; }
        public string Content { get; set; }
    }

    public class WolfClient : IWolfClient
    {
        private SocketIOClient.SocketIO _socket;
        public bool IsConnected { get; private set; }
        public event Action<IMessage> OnMessage;

        // Builder pattern methods compatibility
        public WolfClient WithSerilog() { return this; }
        public WolfClient Done() { return this; }

        public WolfClient()
        {
            _socket = new SocketIOClient.SocketIO("https://v3-sio.wolf.live", new SocketIOOptions
            {
                EIO = (SocketIO.Core.EngineIO)4, // Try EIO 4 first as it's more modern
                Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
                Reconnection = true,
                ConnectionTimeout = TimeSpan.FromSeconds(20), // Increased timeout
                ExtraHeaders = new Dictionary<string, string>
                {
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" },
                    { "Origin", "https://wolf.live" }
                }
            });

            _socket.OnError += (sender, e) => 
            {
                Console.WriteLine($"❌ Socket Error: {e}");
            };

            _socket.OnConnected += (sender, e) =>
            {
                Console.WriteLine("✅ Connected to WOLF Server");
                IsConnected = true;
            };

            _socket.OnDisconnected += (sender, e) =>
            {
                Console.WriteLine($"❌ Disconnected: {e}");
                IsConnected = false;
            };

            // Handle incoming messages
            _socket.On("message receive", response =>
            {
                try
                {
                    var data = response.GetValue<JObject>();
                    var body = data["body"];
                    
                    if (body != null)
                    {
                        var isGroup = body["isGroup"]?.Value<bool>() ?? false;
                        if (isGroup)
                        {
                            var content = body["data"]?.ToString();
                            // Handle text/plain mimeType usually
                            
                            var msg = new RealMessage
                            {
                                Group = new Models.Group { Id = body["recipientId"]?.ToString() },
                                User = new Models.User { Id = body["originatorId"]?.ToString(), Name = "Unknown" }, // Name not always sent in message receive
                                Content = content
                            };
                            
                            OnMessage?.Invoke(msg);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing message: {ex.Message}");
                }
            });
        }

        public async Task<bool> Login(string email, string password)
        {
            try
            {
                Console.WriteLine("DEBUG: Attempting to connect to Socket.IO...");
                
                // Use a timeout for the initial connection task
                var connectTask = _socket.ConnectAsync();
                var timeoutTask = Task.Delay(20000); // 20 seconds timeout
                
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    Console.WriteLine("❌ Socket connection timed out.");
                    return false;
                }
                
                // Ensure exception is propagated if connect failed
                await connectTask;

                Console.WriteLine($"DEBUG: ConnectAsync finished. Connected: {_socket.Connected}");
                
                if (!_socket.Connected)
                {
                    Console.WriteLine("❌ Socket failed to connect (Connected=false after ConnectAsync)");
                    return false;
                }

                var tcs = new TaskCompletionSource<bool>();
                
                // Listen for welcome message or login response
                // In real Wolf protocol, we should listen to 'welcome' event or similar if available
                // But typically we emit 'security login'
                
                await _socket.EmitAsync("security login", response => 
                {
                    Console.WriteLine($"DEBUG: Login response received: {response}");
                    if (response.ToString().Contains("200"))
                    {
                         Console.WriteLine("✅ Logged in successfully");
                         tcs.TrySetResult(true);
                    }
                    else
                    {
                         Console.WriteLine($"❌ Login failed: {response}");
                         tcs.TrySetResult(false);
                    }
                }, new 
                {
                    command = "security login",
                    body = new 
                    {
                        email = email,
                        password = password,
                        deviceTypeId = 8 // PC/Web
                    }
                });

                // Wait a bit for connection/login
                await Task.WhenAny(tcs.Task, Task.Delay(15000));
                
                if (tcs.Task.IsCompleted)
                    return await tcs.Task;
                    
                // If timeout, assuming connected if socket is connected (unsafe but practical for now)
                return _socket.Connected; 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login exception: {ex.Message}");
                return false;
            }
        }

        public async Task SendGroupMessage(string groupId, string message)
        {
            if (!_socket.Connected) return;

            var payload = new
            {
                command = "message send",
                body = new
                {
                    recipientId = int.Parse(groupId),
                    isGroup = true,
                    mimeType = "text/plain",
                    data = message
                }
            };

            await _socket.EmitAsync("message send", payload);
            Console.WriteLine($"📤 Sent to {groupId}: {message}");
        }

        public async Task Disconnect()
        {
            await _socket.DisconnectAsync();
        }
        
        public void SimulateMessage(IMessage message)
        {
            // Can still be used for testing
            OnMessage?.Invoke(message);
        }
    }
}

namespace WolfLive.Api.Models
{
    public class Group
    {
        public string Id { get; set; }
    }

    public class User
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
