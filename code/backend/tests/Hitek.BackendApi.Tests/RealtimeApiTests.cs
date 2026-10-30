using System.Net;
using System.Text.Json;
using Hitek.BackendApi.Data;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Hitek.BackendApi.Tests;

// Kênh realtime SignalR mà Web Dashboard nghe để hiện cảnh báo ngay khi phát sinh.
public sealed class RealtimeApiTests : IDisposable
{
    private readonly BackendApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    // Long polling thay cho WebSocket vì máy chủ kiểm thử chạy trong bộ nhớ, không mở cổng thật.
    private HubConnection Connect(string? token) => new HubConnectionBuilder()
        .WithUrl(new Uri(_api.Server.BaseAddress, "hubs/alerts"), o =>
        {
            o.Transports = HttpTransportType.LongPolling;
            o.HttpMessageHandlerFactory = _ => _api.Server.CreateHandler();
            if (token is not null) o.AccessTokenProvider = () => Task.FromResult<string?>(token);
        })
        .Build();

    [Fact]
    public async Task CanhBaoMoi_ChiDayToiDashboardCuaCungTenant()
    {
        await using var tenantA = Connect(await _api.UserTokenAsync(BackendApiFactory.ViewerEmail));
        await using var tenantB = Connect(await _api.UserTokenAsync(BackendApiFactory.ViewerOtherTenantEmail));

        var receivedByA = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedByB = new List<JsonElement>();
        tenantA.On<JsonElement>("AlertUpdated", alert => receivedByA.TrySetResult(alert));
        tenantB.On<JsonElement>("AlertUpdated", alert => { lock (receivedByB) receivedByB.Add(alert); });

        await tenantA.StartAsync();
        await tenantB.StartAsync();

        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorPir, BackendApiFactory.RecentUtc(), true, true, true);

        var pushed = await receivedByA.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("MEDIUM", pushed.GetProperty("level").GetString());
        Assert.Equal("OPEN", pushed.GetProperty("handledStatus").GetString());
        Assert.Equal(new[] { "PIR" }, pushed.GetProperty("sources").Strings());

        // Nếu B cũng nằm trong nhóm nhận thì tin đã được xếp hàng cùng lúc với A; chờ thêm
        // một nhịp long polling là đủ để nó tới nơi.
        await Task.Delay(TimeSpan.FromSeconds(1));
        lock (receivedByB) Assert.Empty(receivedByB);
    }

    [Fact]
    public async Task KetNoiKhongCoToken_BiTuChoi()
    {
        await using var anonymous = Connect(token: null);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => anonymous.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task TokenService_KhongNgheDuocKenhCanhBao()
    {
        await using var service = Connect(await _api.ServiceTokenAsync());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.StartAsync());

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }
}
