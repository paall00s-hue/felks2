using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace TelegramBotController
{
    public class NetworkOptimizer : IDisposable
    {
        private readonly Stopwatch _pingTimer;
        private long _totalPingTime;
        private int _pingCount;
        private double _averagePing;
        private double _jitter;
        private double _packetLoss;
        
        public double AveragePing => _averagePing;
        public double Jitter => _jitter;
        public double PacketLoss => _packetLoss;
        public double CpuUsage { get; private set; }
        public double MemoryUsage { get; private set; }
        public bool IsNetworkStable => _averagePing < 100 && _jitter < 50 && _packetLoss < 5;
        
        public NetworkOptimizer()
        {
            _pingTimer = new Stopwatch();
            // Performance counters might not work on all platforms/environments without privileges
            // wrapping in try-catch
            try
            {
                // _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                // _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
            }
            catch { }
            
            Console.WriteLine("🌐 محسن الشبكة جاهز");
            Task.Run(MonitorNetworkAsync);
        }
        
        private async Task MonitorNetworkAsync()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(5000);
                    var pingTime = await MeasurePingAsync();
                    UpdateStatistics(pingTime);
                }
                catch { }
            }
        }
        
        private async Task<long?> MeasurePingAsync()
        {
            try
            {
                using var ping = new Ping();
                _pingTimer.Restart();
                var reply = await ping.SendPingAsync("8.8.8.8", 1000);
                _pingTimer.Stop();
                return reply.Status == IPStatus.Success ? reply.RoundtripTime : (long?)null;
            }
            catch { return null; }
        }
        
        private void UpdateStatistics(long? pingTime)
        {
            if (pingTime.HasValue)
            {
                _pingCount++;
                _totalPingTime += pingTime.Value;
                _averagePing = _totalPingTime / (double)_pingCount;
                if (_pingCount > 1)
                {
                    _jitter = (_jitter * 0.9) + (Math.Abs(pingTime.Value - _averagePing) * 0.1);
                }
                _packetLoss = (_packetLoss * 0.9);
            }
            else
            {
                _packetLoss = (_packetLoss * 0.9) + 10.0;
            }
        }
        
        public int CalculateOptimalOffset()
        {
            int baseOffset = 50;
            if (!IsNetworkStable)
            {
                baseOffset += (int)(_averagePing / 10);
                baseOffset += (int)(_jitter / 5);
            }
            return Math.Min(baseOffset, 200);
        }
        
        public void Dispose() { }
    }
}
