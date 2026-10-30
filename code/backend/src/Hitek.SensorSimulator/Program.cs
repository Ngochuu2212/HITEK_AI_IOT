using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

// Bộ giả lập ba cảm biến PIR, Door, Beam, publish qua đúng giao thức MQTT như thiết bị thật.
//
// Cách chạy: Hitek.SensorSimulator [host] [port] [chế độ]
//   random    (mặc định) mỗi cảm biến sinh giá trị ngẫu nhiên với xác suất thấp, giống điều
//             kiện vận hành bình thường — phần lớn thời gian không có gì bất thường.
//   intrusion cứ mỗi 60 giây diễn lại một vụ đột nhập: cửa mở, có chuyển động, tia bị cắt
//             liên tục 20 giây, để trình diễn cảnh báo đa nguồn lên tới Critical.
//
// Mã cảm biến cố định, trùng dữ liệu seed của Backend (Data/SeedIds.cs): bộ giả lập là tiến
// trình riêng, không đọc được database để biết cảm biến nào đang tồn tại.
var sensors = new[]
{
    (Id: Guid.Parse("11111111-1111-1111-1111-111111111111"), Name: "PIR", TrueChance: 0.15),
    (Id: Guid.Parse("22222222-2222-2222-2222-222222222222"), Name: "Door", TrueChance: 0.08),
    (Id: Guid.Parse("33333333-3333-3333-3333-333333333333"), Name: "Beam", TrueChance: 0.10)
};

var host = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("MQTT_HOST") ?? "localhost";
var port = args.Length > 1 ? int.Parse(args[1]) : int.Parse(Environment.GetEnvironmentVariable("MQTT_PORT") ?? "1883");
var mode = (args.Length > 2 ? args[2] : Environment.GetEnvironmentVariable("SIMULATOR_MODE") ?? "random").ToLowerInvariant();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

using var client = new MqttFactory().CreateMqttClient();
var options = new MqttClientOptionsBuilder()
    .WithTcpServer(host, port)
    .WithClientId($"hitek-sensor-simulator-{Environment.ProcessId}")
    .Build();
var connectLock = new SemaphoreSlim(1, 1);

await EnsureConnectedAsync();
Console.WriteLine($"Đang phát dữ liệu PIR + Door + Beam, chế độ {mode}. Ctrl+C để dừng.");

var random = new Random();
var startedAt = DateTime.UtcNow;
try
{
    await Task.WhenAll(sensors.Select(s => PublishLoopAsync(s.Id, s.Name, s.TrueChance)));
}
catch (OperationCanceledException)
{
}

// Broker chưa lên (lúc cả hệ thống cùng khởi động) hoặc vừa khởi động lại thì chờ rồi kết
// nối lại, thay vì thoát ngay ở lần thử đầu tiên.
async Task EnsureConnectedAsync()
{
    await connectLock.WaitAsync(cts.Token);
    try
    {
        while (!client.IsConnected)
        {
            try
            {
                Console.WriteLine($"Kết nối MQTT broker {host}:{port}...");
                await client.ConnectAsync(options, cts.Token);
                Console.WriteLine("Đã kết nối.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Chưa kết nối được ({ex.Message}), thử lại sau 3 giây.");
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
            }
        }
    }
    finally
    {
        connectLock.Release();
    }
}

bool InIntrusionWindow() => mode == "intrusion" && (DateTime.UtcNow - startedAt).TotalSeconds % 60 < 20;

async Task PublishLoopAsync(Guid sensorId, string name, double trueChance)
{
    while (!cts.IsCancellationRequested)
    {
        var intrusion = InIntrusionWindow();
        var value = intrusion || random.NextDouble() < trueChance;
        var payload = JsonSerializer.Serialize(new { value, recordedAt = DateTime.UtcNow });

        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"hitek/{sensorId}/reading")
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        try
        {
            await client.PublishAsync(message, cts.Token);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {name,-4} value={value}{(intrusion ? "  (kịch bản đột nhập)" : "")}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[{name}] gửi thất bại: {ex.Message}");
            await EnsureConnectedAsync();
            continue;
        }

        // Trong kịch bản đột nhập gửi dày hơn để vượt ngưỡng chống nhiễu ba lượt liên tiếp nhanh.
        var delay = intrusion ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(random.Next(2, 6));
        await Task.Delay(delay, cts.Token);
    }
}
